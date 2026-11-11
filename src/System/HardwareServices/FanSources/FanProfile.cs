using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 一条「机型 → EC 风扇寄存器布局」规则。
    ///
    /// EC 寄存器布局没有行业标准，每个品牌/ODM 机型都可能不同，因此不能写一个
    /// 「通用 EC 偏移」到处跑。新增品牌 = 新增一条规则（必要时再配一个适配器实现）。
    /// </summary>
    internal sealed class FanProfile
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";

        /// <summary>命中的前提：root\wmi 里必须存在这个厂商数据类（null 表示不检查）。</summary>
        public string? RequiredWmiClass { get; init; }

        /// <summary>可选的机型/厂商名关键字（不区分大小写），null 表示不检查。</summary>
        public string? ModelPattern { get; init; }

        /// <summary>CPU 风扇 RPM 的 16 位小端寄存器起始地址。</summary>
        public ushort CpuRpmRegister { get; init; }

        /// <summary>GPU 风扇 RPM 的 16 位小端寄存器起始地址。</summary>
        public ushort GpuRpmRegister { get; init; }

        public uint MinRpm { get; init; } = 200;
        public uint MaxRpm { get; init; } = 20000;

        /// <summary>
        /// 是否必须先与另一数据源交叉校验才允许对外供数。
        /// 目前内置规则用「自洽性校验」（合理量级 + 持续变化）即可启用，
        /// 交叉校验只用于判定 CPU/GPU 左右顺序，因此设为 false。
        /// </summary>
        public bool RequireCrossCheck { get; init; }
    }

    /// <summary>本机机型信息，用于匹配 FanProfile。全部为只读采集结果。</summary>
    internal sealed class FanMachineInfo
    {
        public string Manufacturer { get; init; } = "";
        public string Model { get; init; } = "";
        public string[] AvailableWmiClasses { get; init; } = Array.Empty<string>();

        public bool HasWmiClass(string name) => AvailableWmiClasses.Any(
            c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>内置机型规则目录。顺序即优先级。</summary>
    internal static class FanProfileCatalog
    {
        /// <summary>
        /// 小米 / TIMI 游戏本（TM1806，Quanta-Inventec ODM）。
        ///
        /// 依据 2026-11-11 两次管理员权限 EC 采样：
        ///   ec-watch（盒子打开 / 最小化各 15 帧）0x00、0x02 始终在 2167..2227 RPM 间持续变化，
        ///   与同期 root\wmi:RW_TMAWMI 的 ~2200 RPM 同量级，且独立于厂商前端心跳。
        /// 注意：CPU/GPU 左右顺序由字段排列推断，运行时若 WMI 可读会自动校准。
        /// </summary>
        private static readonly FanProfile[] Profiles =
        {
            new FanProfile
            {
                Id = "xiaomi-tm1806-ec",
                DisplayName = "小米 / TIMI 游戏本 (TM1806 系列) · EC 直读",
                RequiredWmiClass = "RW_TMAWMI",
                ModelPattern = null,
                CpuRpmRegister = 0x00,
                GpuRpmRegister = 0x02,
                MinRpm = 200,
                MaxRpm = 20000,
                RequireCrossCheck = false
            }
        };

        public static IReadOnlyList<FanProfile> All => Profiles;

        /// <summary>只需探测规则里声明过的类，避免枚举 root\wmi 的 1600+ 个类。</summary>
        public static IEnumerable<string> RequiredWmiClasses() => Profiles
            .Where(p => !string.IsNullOrEmpty(p.RequiredWmiClass))
            .Select(p => p.RequiredWmiClass!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        public static FanProfile? Match(FanMachineInfo machine) =>
            Profiles.FirstOrDefault(p => Matches(p, machine));

        public static bool Matches(FanProfile profile, FanMachineInfo machine)
        {
            if (profile.RequiredWmiClass != null && !machine.HasWmiClass(profile.RequiredWmiClass))
                return false;

            if (!string.IsNullOrEmpty(profile.ModelPattern))
            {
                string pattern = profile.ModelPattern;
                if (!Has(machine.Model, pattern) && !Has(machine.Manufacturer, pattern))
                    return false;
            }

            return true;
        }

        private static bool Has(string text, string fragment) =>
            !string.IsNullOrEmpty(text) &&
            text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 纯逻辑：用另一路读数（RW_TMAWMI）校准 EC 两个字段的 CPU/GPU 左右顺序。
    /// 只做一次，结果留在内存；若 EC 与参考值差异过大则判定「这两个寄存器不是 RPM」，
    /// 拒绝启用，避免把一个恰好在合理区间的常量当作风扇转速。
    /// </summary>
    internal static class FanProfileVerifier
    {
        /// <summary>两项误差之和的绝对上限（RPM）。</summary>
        public const float MaxAbsoluteError = 300f;

        /// <summary>相对参考均值的误差上限（15%）。</summary>
        public const float MaxRelativeError = 0.15f;

        public static bool TryConfirm(FanRpmPair ec, FanRpmPair reference,
            out bool swapCpuGpu, out float error)
        {
            swapCpuGpu = false;
            error = float.MaxValue;

            if (!ec.HasBoth || !reference.HasBoth) return false;

            float direct = Diff(ec.Cpu!.Value, ec.Gpu!.Value, reference.Cpu!.Value, reference.Gpu!.Value);
            float swapped = Diff(ec.Gpu!.Value, ec.Cpu!.Value, reference.Cpu!.Value, reference.Gpu!.Value);

            error = Math.Min(direct, swapped);
            swapCpuGpu = swapped < direct;

            float scale = (reference.Cpu.Value + reference.Gpu.Value) / 2f;
            float tolerance = Math.Max(MaxAbsoluteError, scale * MaxRelativeError);
            return error <= tolerance;
        }

        private static float Diff(float c, float g, float rc, float rg) =>
            Math.Abs(c - rc) + Math.Abs(g - rg);
    }

    /// <summary>
    /// 纯逻辑：把 4 个 EC 寄存器字节（cpuLo, cpuHi, gpuLo, gpuHi）解成两个 RPM。
    /// 与硬件读取解耦，便于回归测试直接驱动。
    /// </summary>
    internal static class EcFanDecoder
    {
        public static bool Decode(ReadOnlySpan<byte> data, FanProfile profile, bool swapCpuGpu,
            out FanRpmPair pair)
        {
            pair = default;
            if (data.Length < 4) return false;

            uint a = (uint)(data[0] | (data[1] << 8));
            uint b = (uint)(data[2] | (data[3] << 8));

            float? first = InRange(a, profile) ? (float?)a : null;
            float? second = InRange(b, profile) ? (float?)b : null;

            pair = swapCpuGpu
                ? new FanRpmPair(second, first)
                : new FanRpmPair(first, second);

            return pair.HasAny;
        }

        private static bool InRange(uint value, FanProfile profile) =>
            value >= profile.MinRpm && value <= profile.MaxRpm;
    }
}
