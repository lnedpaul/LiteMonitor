using System;
using System.Buffers.Binary;
using System.Text;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 小米游戏本（TM1806 / Quanta-Inventec ODM）厂商 ACPI WMI 数据块解析。
    ///
    /// 来源：GamingBox 1.5.0.882 依赖的 root\wmi:RW_TMAWMI（InstanceName=ACPI\PNP0C14\...），
    /// 数据块 32 字节，管理员权限可读；普通权限会被驱动拒绝。
    /// 字段布局由真机采样与厂商工具读数对照确定（2026-11-09）：
    ///
    ///   均衡模式  UI: GPU 3302 RPM / 59 C，CPU 3307 RPM / 68 C
    ///   快照 1    [4]=0x0D04=3332  [8]=0x0CE6=3302  [12]=0x51=81  [16]=0x3A=58
    ///   快照 2    [4]=0x0CF0=3312  [8]=0x0CD6=3286  [12]=0x47=71  [16]=0x39=57
    ///   快照 3    [4]=0x0CDB=3291  [8]=0x0CDB=3291  [12]=0x45=69  [16]=0x39=57
    ///
    /// 温度排列 [12]=CPU、[16]=GPU 与 UI 的 68/59 相符，据此确定 [4]=CPU 风扇、[8]=GPU 风扇
    /// （注意与 UI 左右顺序相反：UI 左 GPU、右 CPU）。
    /// 全速模式厂商工具显示 6160 / 6231 RPM，均在同一 uint32 字段可表示范围内。
    ///
    /// 这里只做纯解析，不访问 WMI，便于回归测试直接复用。
    /// </summary>
    internal static class TmaFanBuffer
    {
        public const int CpuFanOffset = 4;
        public const int GpuFanOffset = 8;
        public const int CpuTempOffset = 12;
        public const int GpuTempOffset = 16;

        /// <summary>解析所需的最小长度（到 GPU 温度字段结束）。</summary>
        public const int RequiredLength = 20;

        /// <summary>真机数据块固定长度；采集路径要求精确匹配，解析函数本身仍保持宽松。</summary>
        public const int BlockLength = 32;

        /// <summary>低于此值视为底噪/无效；与上游 FanMapper 的 200 RPM 门槛一致。</summary>
        public const uint MinRpm = 200;

        /// <summary>高于此值视为驱动异常数据，不展示。</summary>
        public const uint MaxRpm = 20000;

        /// <summary>
        /// 尝试从数据块中解析两个风扇转速。任一字段有效即返回 true，
        /// 无效字段返回 null（调用方据此显示 "--" 而不是误导性的 0）。
        /// </summary>
        public static bool TryParse(ReadOnlySpan<byte> data, out float? cpuRpm, out float? gpuRpm)
        {
            cpuRpm = null;
            gpuRpm = null;

            if (data.Length < RequiredLength) return false;

            uint cpu = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(CpuFanOffset, 4));
            uint gpu = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(GpuFanOffset, 4));

            if (IsPlausible(cpu)) cpuRpm = cpu;
            if (IsPlausible(gpu)) gpuRpm = gpu;

            return cpuRpm.HasValue || gpuRpm.HasValue;
        }

        public static bool IsPlausible(uint rpm) => rpm >= MinRpm && rpm <= MaxRpm;

        /// <summary>
        /// 严格形状校验：只让真正的小米 TM1806 数据块进入解析。
        ///
        /// 真机所有快照（均衡 / 全速 / 最小化冻结）都符合：
        ///   [0..3]  全 0 保留区
        ///   [4..7]  CPU RPM uint32LE，只用低 16 位
        ///   [8..11] GPU RPM uint32LE，只用低 16 位
        ///   [12..15] CPU 温度 uint32LE，只用最低 1 字节
        ///   [16..19] GPU 温度 uint32LE，只用最低 1 字节
        ///   [20..31] 全 0 填充区
        ///
        /// 其他品牌即使偶然存在同名 WMI 类、且 [4..11] 数值恰好落在 200..20000，
        /// 也会因保留区/填充区/高位字节不为 0 而被挡住，避免显示一个假 RPM。
        /// </summary>
        public static bool IsValidVendorBlock(ReadOnlySpan<byte> data)
        {
            if (data.Length != BlockLength) return false;

            for (int i = 0; i < CpuFanOffset; i++)
                if (data[i] != 0) return false;

            for (int i = RequiredLength; i < BlockLength; i++)
                if (data[i] != 0) return false;

            // RPM 字段只用低 16 位（真机全速约 6200 RPM，远小于 65536）
            if (data[6] != 0 || data[7] != 0) return false;
            if (data[10] != 0 || data[11] != 0) return false;

            // 温度字段只用最低 1 字节
            for (int i = CpuTempOffset + 1; i < GpuTempOffset; i++)
                if (data[i] != 0) return false;
            for (int i = GpuTempOffset + 1; i < RequiredLength; i++)
                if (data[i] != 0) return false;

            byte cpuTemp = data[CpuTempOffset];
            byte gpuTemp = data[GpuTempOffset];
            if (cpuTemp < 1 || cpuTemp > 125 || gpuTemp < 1 || gpuTemp > 125) return false;

            return true;
        }

        /// <summary>取数据块第 12/16 字节的 CPU/GPU 温度，仅用于诊断展示。</summary>
        public static bool TryParseTemps(ReadOnlySpan<byte> data, out uint cpuTemp, out uint gpuTemp)
        {
            cpuTemp = 0;
            gpuTemp = 0;
            if (data.Length < RequiredLength) return false;
            cpuTemp = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(CpuTempOffset, 4));
            gpuTemp = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(GpuTempOffset, 4));
            return true;
        }

        public static string ToHex(ReadOnlySpan<byte> data)
        {
            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
