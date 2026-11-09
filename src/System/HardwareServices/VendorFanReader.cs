using System;
using System.Management;
using System.Threading;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 厂商风扇通道（小米游戏盒子 / Quanta-Inventec ODM 的 ACPI WMI 数据块）。
    ///
    /// 这类笔记本由 EC 直接控风扇，LibreHardwareMonitor 枚举不到 Fan 传感器，
    /// 但 BIOS 通过 root\wmi:RW_TMAWMI 暴露了现成的 RPM 值，厂商控制软件读的就是它。
    /// 该类只做「读」：不写 EC 寄存器、不调用厂商 WMI 方法、不启动/停止任何服务。
    ///
    /// 设计要点：
    ///  - 后台 Timer 定时采集并缓存，取值路径只读内存字段，绝不阻塞 UI 线程；
    ///  - 非管理员 / 无此 WMI 类 / 数据异常时自动判定不可用并指数退避，主流程回退到 LHM；
    ///  - 超过 10 秒没有成功采集即视为陈旧数据，不再对外提供，避免显示冻结的旧转速。
    /// </summary>
    internal sealed class VendorFanReader
    {
        private const string WmiScope = @"\\.\root\wmi";
        private const string WmiQuery = "SELECT * FROM RW_TMAWMI";
        private const string AcpiInstancePrefix = @"ACPI\PNP0C14";

        private const int ActiveIntervalMs = 1000;
        private const int RetryIntervalMs = 30000;
        private const int FailureThreshold = 3;
        private const int StaleSeconds = 10;

        private static readonly Lazy<VendorFanReader> LazyInstance =
            new(() => new VendorFanReader(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>访问即启动后台采集；程序生命周期内保持单例。</summary>
        public static VendorFanReader Instance => LazyInstance.Value;

        private readonly object _sync = new();
        private readonly System.Threading.Timer _timer;
        private int _inRead;
        private int _failures;

        private float? _cpuRpm;
        private float? _gpuRpm;
        private bool _available;
        private string _rawHex = "";
        private string _lastError = "";
        private DateTime _lastSuccessUtc = DateTime.MinValue;

        private VendorFanReader()
        {
            // 先以 Infinite 创建，再启动，避免 dueTime=0 时回调里访问尚未赋值的 _timer。
            _timer = new System.Threading.Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(0, ActiveIntervalMs);
        }

        public bool Available { get { lock (_sync) return _available; } }
        public string RawHex { get { lock (_sync) return _rawHex; } }
        public string LastError { get { lock (_sync) return _lastError; } }
        public bool HasError { get { lock (_sync) return !string.IsNullOrEmpty(_lastError); } }
        public DateTime LastSuccessUtc { get { lock (_sync) return _lastSuccessUtc; } }

        /// <summary>
        /// 取指定监控项的转速。仅 CPU.Fan / GPU.Fan 走厂商通道，
        /// 其余项（水泵、机箱风扇）仍由 LHM 负责。
        /// </summary>
        public float? GetRpm(string key)
        {
            lock (_sync)
            {
                if (!_available) return null;
                if ((DateTime.UtcNow - _lastSuccessUtc).TotalSeconds > StaleSeconds) return null;

                return key switch
                {
                    "CPU.Fan" => _cpuRpm,
                    "GPU.Fan" => _gpuRpm,
                    _ => null
                };
            }
        }

        /// <summary>供「系统硬件详情」展示的诊断信息。</summary>
        public string GetDiagnosticText()
        {
            lock (_sync)
            {
                if (!_available)
                {
                    return string.IsNullOrEmpty(_lastError)
                        ? "unavailable (no RW_TMAWMI data block)"
                        : "unavailable: " + _lastError;
                }

                string cpu = _cpuRpm.HasValue ? $"{_cpuRpm.Value:0}" : "--";
                string gpu = _gpuRpm.HasValue ? $"{_gpuRpm.Value:0}" : "--";
                return $"CPU {cpu} RPM / GPU {gpu} RPM | {_rawHex}";
            }
        }

        private void OnTimer(object? state)
        {
            // 防重入：一次 WMI 查询慢于一个采集周期时，直接跳过本轮。
            if (Interlocked.Exchange(ref _inRead, 1) != 0) return;
            try
            {
                Refresh();
            }
            catch (Exception ex)
            {
                MarkFailure("unexpected: " + ex.GetType().Name);
            }
            finally
            {
                Interlocked.Exchange(ref _inRead, 0);
            }
        }

        private void Refresh()
        {
            byte[]? buffer = null;
            string error = "";

            try
            {
                var scope = new ManagementScope(WmiScope);
                var query = new ObjectQuery(WmiQuery);
                var options = new System.Management.EnumerationOptions
                {
                    ReturnImmediately = true,
                    Rewindable = false,
                    Timeout = TimeSpan.FromSeconds(5)
                };

                using var searcher = new ManagementObjectSearcher(scope, query, options);
                using var results = searcher.Get();

                foreach (ManagementObject mo in results)
                {
                    using (mo)
                    {
                        string instance = mo["InstanceName"]?.ToString() ?? "";
                        if (!instance.StartsWith(AcpiInstancePrefix, StringComparison.OrdinalIgnoreCase))
                            continue;

                        byte[]? bytes = ToByteArray(mo["BufferBytes"]);
                        if (bytes != null && bytes.Length >= TmaFanBuffer.RequiredLength)
                        {
                            buffer = bytes;
                            break;
                        }
                    }
                }

                if (buffer == null) error = "RW_TMAWMI data block not found or too short";
            }
            catch (Exception ex)
            {
                // 常见：非管理员时被驱动拒绝访问、ACPI 类不存在、WMI 服务忙。
                error = ex.GetType().Name + ": " + ex.Message;
            }

            if (buffer == null)
            {
                MarkFailure(error);
                return;
            }

            if (!TmaFanBuffer.TryParse(buffer, out var cpu, out var gpu))
            {
                MarkFailure("no plausible RPM field in data block");
                return;
            }

            lock (_sync)
            {
                _cpuRpm = cpu;
                _gpuRpm = gpu;
                _rawHex = TmaFanBuffer.ToHex(buffer);
                _available = true;
                _lastError = "";
                _lastSuccessUtc = DateTime.UtcNow;
            }

            Interlocked.Exchange(ref _failures, 0);
            _timer.Change(ActiveIntervalMs, ActiveIntervalMs);
        }

        private void MarkFailure(string error)
        {
            lock (_sync)
            {
                _available = false;
                _cpuRpm = null;
                _gpuRpm = null;
                _lastError = error;
            }

            int failures = Interlocked.Increment(ref _failures);
            if (failures >= FailureThreshold)
            {
                // 连续失败后降频，避免每秒抛一次 ManagementException 拖慢系统。
                _timer.Change(RetryIntervalMs, RetryIntervalMs);
            }
        }

        /// <summary>WMI uint8 数组在不同运行时可能映射为 byte[] 或 Array，统一转换。</summary>
        private static byte[]? ToByteArray(object? raw)
        {
            if (raw is byte[] bytes) return bytes;
            if (raw is Array array)
            {
                var result = new byte[array.Length];
                for (int i = 0; i < array.Length; i++)
                {
                    try { result[i] = Convert.ToByte(array.GetValue(i)); }
                    catch { return null; }
                }
                return result;
            }
            return null;
        }
    }
}
