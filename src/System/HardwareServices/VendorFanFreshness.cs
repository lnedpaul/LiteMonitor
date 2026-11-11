using System;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 纯逻辑：判定「查询成功但数据冻结」。
    ///
    /// 小米游戏盒子最小化时会 clearInterval + gaming_os.stop()，此后 RW_TMAWMI
    /// 查询仍然成功、Active 仍然 True，但 BufferBytes 停在同一帧。基于
    /// lastSuccessUtc 的陈旧判定抓不住这种情况（因为「成功时间」一直在更新），
    /// 必须逐帧比对原始字节。
    ///
    /// 不访问 WMI、不读时钟、不开线程，便于回归测试直接驱动。
    /// </summary>
    internal sealed class VendorFanFreshness
    {
        /// <summary>连续 8 帧（约 8 秒）完全相同才判冻结，避免静息时正常抖动被误判。</summary>
        public const int DefaultFrozenThreshold = 8;

        private readonly int _threshold;
        private string _lastHex = "";
        private int _sameCount;

        public VendorFanFreshness(int threshold = DefaultFrozenThreshold)
        {
            if (threshold < 2) throw new ArgumentOutOfRangeException(nameof(threshold));
            _threshold = threshold;
        }

        public bool IsFrozen { get; private set; }
        public int SameCount => _sameCount;
        public int Threshold => _threshold;

        /// <summary>喂入一帧成功采集的原始字节；返回当前是否已判定冻结。</summary>
        public bool Observe(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                Reset();
                return IsFrozen;
            }

            if (string.Equals(hex, _lastHex, StringComparison.Ordinal))
            {
                _sameCount++;
            }
            else
            {
                _lastHex = hex;
                _sameCount = 1;
                IsFrozen = false;
            }

            if (_sameCount >= _threshold) IsFrozen = true;
            return IsFrozen;
        }

        /// <summary>采集失败 / 数据源切换时清空，避免跨故障期累积相同帧计数。</summary>
        public void Reset()
        {
            _lastHex = "";
            _sameCount = 0;
            IsFrozen = false;
        }
    }

    /// <summary>
    /// 纯逻辑：判断一次采集失败是否属于「本机根本没有这个厂商 WMI 类」。
    /// 只有连续失败达到阈值且命中这些特征才永久停用采集；
    /// 权限不足（普通用户运行）、WMI 忙、超时、瞬时读不到实例仍按可恢复错误处理，
    /// 否则在非小米机型上会每秒/每 30 秒做一次注定失败的 WMI 查询。
    /// </summary>
    internal static class VendorFanSupport
    {
        public static bool IsPermanentAbsence(string? error)
        {
            if (string.IsNullOrEmpty(error)) return false;

            // 这是「类在、但没匹配到 ACPI\PNP0C14 实例」的业务错误，
            // 属于瞬时故障（WMI 忙 / 驱动未就绪），不能当成机型不支持。
            if (error.StartsWith("RW_TMAWMI data block", StringComparison.OrdinalIgnoreCase))
                return false;

            return Has(error, "Invalid class")
                || Has(error, "无效的类")
                || Has(error, "Invalid namespace")
                || Has(error, "无效命名空间")
                || Has(error, "not found")
                || Has(error, "找不到");
        }

        private static bool Has(string text, string fragment) =>
            text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
