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
    ///  - 只对真机验证过的 32 字节数据块形状放行，杜绝在其他品牌机型上误报假 RPM；
    ///  - 本机不提供该 WMI 类时，连续失败达到阈值后永久停用采集；
    ///  - 查询成功但数据块连续多帧不变（厂商控制中心最小化 / 停心跳）时判定冻结，
    ///    不再对外提供旧转速，避免界面显示一个静止不动的数字；
    ///  - 非管理员 / WMI 忙 / 数据异常时按可恢复错误处理并指数退避，主流程回退到 LHM。
    /// </summary>
    internal sealed class VendorFanReader : IFanSource
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
        private readonly VendorFanFreshness _freshness = new();
        private int _inRead;
        private int _failures;

        private float? _cpuRpm;
        private float? _gpuRpm;
        private bool _available;
        private FanSourceStatus _status = FanSourceStatus.Waiting;
        private string _rawHex = "";
        private string _lastError = "";
        private DateTime _lastSuccessUtc = DateTime.MinValue;

        private VendorFanReader()
        {
            // 先以 Infinite 创建，再启动，避免 dueTime=0 时回调里访问尚未赋值的 _timer。
            _timer = new System.Threading.Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(0, ActiveIntervalMs);
        }

        // ---- IFanSource ----
        public string Id => "vendor-wmi-rw-tmawmi";
        public string DisplayName => "厂商 ACPI WMI 数据块 (RW_TMAWMI)";
        public string DiagnosticText => GetDiagnosticText();
        public FanRpmPair Reading { get { lock (_sync) return new FanRpmPair(_cpuRpm, _gpuRpm); } }

        public FanSourceStatus Status { get { lock (_sync) return _status; } }
        public bool Available { get { lock (_sync) return _available; } }
        public bool IsFrozen { get { lock (_sync) return _status == FanSourceStatus.Frozen; } }
        public string RawHex { get { lock (_sync) return _rawHex; } }
        public string LastError { get { lock (_sync) return _lastError; } }
        public bool HasError { get { lock (_sync) return !string.IsNullOrEmpty(_lastError); } }
        public DateTime LastSuccessUtc { get { lock (_sync) return _lastSuccessUtc; } }

        /// <summary>
        /// 取指定监控项的转速。仅 CPU.Fan / GPU.Fan 走厂商通道，
        /// 其余项（水泵、机箱风扇）仍由 LHM 负责。
        /// 冻结时返回 null，由上层直接显示 "--"，不能让旧值从 lastValidMap 兜底回来。
        /// </summary>
        public float? GetRpm(string key)
        {
            lock (_sync)
            {
                if (_status != FanSourceStatus.Ok) return null;
                if ((DateTime.UtcNow - _lastSuccessUtc).TotalSeconds > StaleSeconds) return null;

                return key switch
                {
                    "CPU.Fan" => _cpuRpm,
                    "GPU.Fan" => _gpuRpm,
                    _ => null
                };
            }
        }

        /// <summary>供「系统硬件详情」展示的诊断信息（每秒刷新）。</summary>
        public string GetDiagnosticText()
        {
            lock (_sync)
            {
                switch (_status)
                {
                    case FanSourceStatus.Unsupported:
                        return "此机型不提供 RW_TMAWMI 厂商数据块，已停用采集；风扇读数以通用传感器为准";

                    case FanSourceStatus.Frozen:
                        return $"数据块停止刷新（厂商控制中心可能已最小化），已暂停显示旧转速 | 冻结帧 CPU {Fmt(_cpuRpm)} / GPU {Fmt(_gpuRpm)} RPM";

                    case FanSourceStatus.Error:
                        string reason = _lastError.Length > 140 ? _lastError.Substring(0, 140) : _lastError;
                        return "unavailable: " + reason;

                    case FanSourceStatus.Ok:
                        return $"CPU {Fmt(_cpuRpm)} RPM / GPU {Fmt(_gpuRpm)} RPM | {_rawHex}";

                    default:
                        return "waiting for the first RW_TMAWMI sample...";
                }
            }
        }

        private static string Fmt(float? value) => value.HasValue ? $"{value.Value:0}" : "--";

        private void OnTimer(object? state)
        {
            // 防重入：一次 WMI 查询慢于一个采集周期时，直接跳过本轮。
            if (Interlocked.Exchange(ref _inRead, 1) != 0) return;
            try
            {
                if (Status == FanSourceStatus.Unsupported) return;
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
                        if (bytes != null && bytes.Length == TmaFanBuffer.BlockLength)
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

            // 严格形状校验：挡住「同名类但布局不同」的其他机型，避免显示假 RPM。
            if (!TmaFanBuffer.IsValidVendorBlock(buffer))
            {
                MarkFailure("unexpected RW_TMAWMI data block shape");
                return;
            }

            if (!TmaFanBuffer.TryParse(buffer, out var cpu, out var gpu))
            {
                MarkFailure("no plausible RPM field in data block");
                return;
            }

            string hex = TmaFanBuffer.ToHex(buffer);
            bool frozen = _freshness.Observe(hex);

            lock (_sync)
            {
                _cpuRpm = cpu;
                _gpuRpm = gpu;
                _rawHex = hex;
                _available = true;
                _lastError = "";
                _lastSuccessUtc = DateTime.UtcNow;
                _status = frozen ? FanSourceStatus.Frozen : FanSourceStatus.Ok;
            }

            Interlocked.Exchange(ref _failures, 0);
            // 冻结期间仍保持 1 秒轮询，厂商控制中心恢复后能立刻解冻。
            _timer.Change(ActiveIntervalMs, ActiveIntervalMs);
        }

        private void MarkFailure(string error)
        {
            int failures = Interlocked.Increment(ref _failures);
            bool giveUp = failures >= FailureThreshold && VendorFanSupport.IsPermanentAbsence(error);

            lock (_sync)
            {
                _freshness.Reset();
                _available = false;
                _cpuRpm = null;
                _gpuRpm = null;
                _lastError = error;
                _status = giveUp ? FanSourceStatus.Unsupported : FanSourceStatus.Error;
            }

            if (giveUp)
            {
                // 本机根本没有这个 WMI 类：彻底停表，不再做注定失败的查询。
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            int interval = failures >= FailureThreshold ? RetryIntervalMs : ActiveIntervalMs;
            _timer.Change(interval, interval);
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
