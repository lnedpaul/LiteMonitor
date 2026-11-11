using System;
using System.Diagnostics;
using System.Threading;
using LibreHardwareMonitor.Hardware.Motherboard.Lpc.EC;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// EC 直读风扇源：按 FanProfile 指定的寄存器读取 16 位小端 RPM。
    ///
    /// 与厂商 WMI 通道不同，EC 的 tachometer 由固件持续更新，不依赖厂商控制中心
    /// 的前端心跳（2026-11-11 两次管理员采样证实：盒子最小化后仍持续变化）。
    /// 因此它是已验证机型上的首选源，WMI 只作为交叉校准与降级源。
    ///
    /// 严格只读：只用 WindowsEmbeddedControllerIO.Read，不写寄存器、不碰端口命令、
    /// 不启动任何服务；每 2 秒只读 4 个寄存器（约 0.5 秒），远轻于全量 EC 扫描。
    /// </summary>
    internal sealed class EcFanSource : IFanSource
    {
        private const int ActiveIntervalMs = 2000;
        private const int RetryIntervalMs = 30000;
        private const int FailureThreshold = 3;

        private readonly FanProfile _profile;
        private readonly Func<FanRpmPair> _reference;
        private readonly VendorFanFreshness _freshness = new();
        private readonly System.Threading.Timer _timer;
        private readonly ushort[] _registers;
        private readonly object _sync = new();

        private WindowsEmbeddedControllerIO? _ec;
        private int _inRead;
        private int _failures;
        private bool _swap;
        private bool _calibrated;
        private FanSourceStatus _status = FanSourceStatus.Waiting;
        private FanRpmPair _reading;
        private string _lastError = "";
        private string _rawHex = "";
        private long _lastReadMs;

        public EcFanSource(FanProfile profile, Func<FanRpmPair> reference)
        {
            _profile = profile;
            _reference = reference;
            _registers = new ushort[]
            {
                profile.CpuRpmRegister, (ushort)(profile.CpuRpmRegister + 1),
                profile.GpuRpmRegister, (ushort)(profile.GpuRpmRegister + 1)
            };

            _timer = new System.Threading.Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(0, ActiveIntervalMs);
        }

        public string Id => _profile.Id;
        public string DisplayName => _profile.DisplayName;
        public FanSourceStatus Status { get { lock (_sync) return _status; } }
        public FanRpmPair Reading { get { lock (_sync) return _reading; } }

        public string DiagnosticText
        {
            get
            {
                lock (_sync)
                {
                    string head = $"[{_profile.Id}] EC 0x{_profile.CpuRpmRegister:X2}/0x{_profile.GpuRpmRegister:X2} (16LE)";
                    string calibration = !_calibrated
                        ? "未校准·默认顺序"
                        : (_swap ? "已校准·左右互换" : "已校准·默认顺序");

                    switch (_status)
                    {
                        case FanSourceStatus.Ok:
                            return $"{head} | CPU {Fmt(_reading.Cpu)} / GPU {Fmt(_reading.Gpu)} RPM | {calibration} | {_lastReadMs} ms | {_rawHex}";
                        case FanSourceStatus.Frozen:
                            return $"{head} | 读数连续 8 帧不变，已判为冻结，自动回退到其它数据源";
                        case FanSourceStatus.Error:
                            return $"{head} | unavailable: {Trim(_lastError)}";
                        default:
                            return $"{head} | 等待首帧采样...";
                    }
                }
            }
        }

        private static string Fmt(float? value) => value.HasValue ? $"{value.Value:0}" : "--";

        private static string Trim(string text) => text.Length > 120 ? text.Substring(0, 120) : text;

        private void OnTimer(object? state)
        {
            // 防重入：一次 EC 读慢于一个采集周期时跳过本轮。
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
            var data = new byte[4];
            var sw = Stopwatch.StartNew();
            try
            {
                _ec ??= new WindowsEmbeddedControllerIO();
                _ec.Read(_registers, data);
            }
            catch (Exception ex)
            {
                MarkFailure(ex.GetType().Name + ": " + ex.Message);
                return;
            }
            sw.Stop();

            if (!EcFanDecoder.Decode(data, _profile, _swap, out var pair))
            {
                MarkFailure("EC values outside plausible RPM range");
                return;
            }

            string hex = BitConverter.ToString(data).Replace("-", " ");
            bool frozen = _freshness.Observe(hex);

            lock (_sync)
            {
                // 交叉校准只做一次，且仅在参考源（RW_TMAWMI）本身处于实时状态时才采信。
                // 校准失败不封杀 EC 源，后续帧仍可重试，避免因参考源冻结而永久拒绝启用。
                if (!_calibrated)
                {
                    var reference = _reference();
                    if (reference.HasBoth && pair.HasBoth &&
                        FanProfileVerifier.TryConfirm(pair, reference, out bool swap, out _))
                    {
                        _swap = swap;
                        _calibrated = true;
                        EcFanDecoder.Decode(data, _profile, _swap, out pair);
                    }
                }

                _reading = pair;
                _rawHex = hex;
                _lastReadMs = sw.ElapsedMilliseconds;
                _lastError = "";
                _status = frozen ? FanSourceStatus.Frozen : FanSourceStatus.Ok;
            }

            Interlocked.Exchange(ref _failures, 0);
            _timer.Change(ActiveIntervalMs, ActiveIntervalMs);
        }

        private void MarkFailure(string error)
        {
            int failures = Interlocked.Increment(ref _failures);
            lock (_sync)
            {
                _freshness.Reset();
                _reading = default;
                _lastError = error;
                _status = FanSourceStatus.Error;
            }

            int interval = failures >= FailureThreshold ? RetryIntervalMs : ActiveIntervalMs;
            _timer.Change(interval, interval);
        }
    }
}
