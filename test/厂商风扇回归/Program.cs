using System.Buffers.Binary;
using System.Globalization;
using LiteMonitor.src.SystemServices;

internal static class Program
{
    private static int _total;
    private static int _failed;

    private static void Check(string name, bool ok, string detail = "")
    {
        _total++;
        if (!ok) _failed++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
    }

    private static byte[] Hex(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }

    private static byte[] Build(uint cpuRpm, uint gpuRpm, uint cpuTemp = 0, uint gpuTemp = 0, int length = 32)
    {
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(TmaFanBuffer.CpuFanOffset, 4), cpuRpm);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(TmaFanBuffer.GpuFanOffset, 4), gpuRpm);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(TmaFanBuffer.CpuTempOffset, 4), cpuTemp);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(TmaFanBuffer.GpuTempOffset, 4), gpuTemp);
        return bytes;
    }

    private static byte[] Mutate(byte[] source, int index, byte value)
    {
        var copy = (byte[])source.Clone();
        copy[index] = value;
        return copy;
    }

    private sealed class StubFanSource : IFanSource
    {
        private readonly string _id;
        private readonly FanSourceStatus _status;
        private readonly FanRpmPair _reading;

        public StubFanSource(string id, FanSourceStatus status, FanRpmPair reading)
        {
            _id = id;
            _status = status;
            _reading = reading;
        }

        public string Id => _id;
        public string DisplayName => _id;
        public FanSourceStatus Status => _status;
        public FanRpmPair Reading => _reading;
        public string DiagnosticText => _id;
    }

    private static void CheckSnapshot(string name, string hex, float? expectedCpu, float? expectedGpu, uint expectedCpuTemp, uint expectedGpuTemp)
    {
        var data = Hex(hex);
        bool parsed = TmaFanBuffer.TryParse(data, out var cpu, out var gpu);
        Check(name + " / TryParse", parsed, $"cpu={cpu} gpu={gpu}");
        Check(name + " / CPU RPM", cpu == expectedCpu, $"expected={expectedCpu} actual={cpu}");
        Check(name + " / GPU RPM", gpu == expectedGpu, $"expected={expectedGpu} actual={gpu}");

        bool temps = TmaFanBuffer.TryParseTemps(data, out var cpuTemp, out var gpuTemp);
        Check(name + " / temps", temps && cpuTemp == expectedCpuTemp && gpuTemp == expectedGpuTemp,
              $"expected={expectedCpuTemp}/{expectedGpuTemp} actual={cpuTemp}/{gpuTemp}");
    }

    private static void Main()
    {
        // ---- 1. 字段布局常量：这一段一旦被误改，所有真机数据都会错位 ----
        Check("layout / CPU fan offset", TmaFanBuffer.CpuFanOffset == 4);
        Check("layout / GPU fan offset", TmaFanBuffer.GpuFanOffset == 8);
        Check("layout / CPU temp offset", TmaFanBuffer.CpuTempOffset == 12);
        Check("layout / GPU temp offset", TmaFanBuffer.GpuTempOffset == 16);
        Check("layout / required length", TmaFanBuffer.RequiredLength == 20);
        Check("layout / min RPM", TmaFanBuffer.MinRpm == 200);
        Check("layout / max RPM", TmaFanBuffer.MaxRpm == 20000);

        // ---- 2. 合理性边界 ----
        Check("plausible / 0 rejected", !TmaFanBuffer.IsPlausible(0));
        Check("plausible / 199 rejected", !TmaFanBuffer.IsPlausible(199));
        Check("plausible / 200 accepted", TmaFanBuffer.IsPlausible(200));
        Check("plausible / 3302 accepted", TmaFanBuffer.IsPlausible(3302));
        Check("plausible / 20000 accepted", TmaFanBuffer.IsPlausible(20000));
        Check("plausible / 20001 rejected", !TmaFanBuffer.IsPlausible(20001));
        Check("plausible / uint max rejected", !TmaFanBuffer.IsPlausible(uint.MaxValue));

        // ---- 3. 真机三份提权快照（均衡模式，厂商工具显示 GPU 3302 / CPU 3307 RPM） ----
        CheckSnapshot("snapshot 1",
            "00 00 00 00 04 0D 00 00 E6 0C 00 00 51 00 00 00 3A 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
            3332f, 3302f, 81, 58);
        CheckSnapshot("snapshot 2",
            "00 00 00 00 F0 0C 00 00 D6 0C 00 00 47 00 00 00 39 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
            3312f, 3286f, 71, 57);
        CheckSnapshot("snapshot 3",
            "00 00 00 00 DB 0C 00 00 DB 0C 00 00 45 00 00 00 39 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
            3291f, 3291f, 69, 57);

        // ---- 4. 全速模式量级：厂商工具显示 CPU 6231 / GPU 6160 RPM ----
        bool full = TmaFanBuffer.TryParse(Build(6231, 6160, 71, 59), out var fullCpu, out var fullGpu);
        Check("full speed / TryParse", full);
        Check("full speed / CPU 6231", fullCpu == 6231f, $"actual={fullCpu}");
        Check("full speed / GPU 6160", fullGpu == 6160f, $"actual={fullGpu}");

        // ---- 5. 长度与脏数据 ----
        Check("length / 19 bytes rejected", !TmaFanBuffer.TryParse(new byte[19], out _, out _));
        Check("length / 20 bytes accepted", TmaFanBuffer.TryParse(Build(3300, 3300, length: 20), out _, out _));
        Check("length / 32 bytes accepted", TmaFanBuffer.TryParse(Build(3300, 3300, length: 32), out _, out _));
        Check("length / all zero rejected", !TmaFanBuffer.TryParse(new byte[32], out _, out _));
        Check("length / empty rejected", !TmaFanBuffer.TryParse(ReadOnlySpan<byte>.Empty, out _, out _));

        // ---- 6. 单边异常：只有一侧有效时另一侧必须是 null（界面显示 "--"，不能是 0） ----
        bool cpuOnly = TmaFanBuffer.TryParse(Build(3307, 0), out var cOnly, out var gOnly);
        Check("partial / CPU valid -> true", cpuOnly);
        Check("partial / CPU valid value", cOnly == 3307f, $"actual={cOnly}");
        Check("partial / GPU invalid -> null", gOnly is null, $"actual={gOnly}");

        bool gpuOnly = TmaFanBuffer.TryParse(Build(0, 6160), out var cNull, out var gVal);
        Check("partial / GPU valid -> true", gpuOnly);
        Check("partial / CPU invalid -> null", cNull is null, $"actual={cNull}");
        Check("partial / GPU valid value", gVal == 6160f, $"actual={gVal}");

        Check("partial / both overflow rejected", !TmaFanBuffer.TryParse(Build(20001, 20001), out _, out _));
        Check("boundary / 200 + 20000", TmaFanBuffer.TryParse(Build(200, 20000), out var bCpu, out var bGpu)
                                        && bCpu == 200f && bGpu == 20000f);

        // ---- 7. 小端序：手工字节必须与 0x0D04 = 3332 对齐 ----
        Check("endian / 04 0D 00 00 = 3332",
            TmaFanBuffer.TryParse(Hex("00 00 00 00 04 0D 00 00 00 00 00 00 00 00 00 00 00 00 00 00"), out var leCpu, out _)
            && leCpu == 3332f, $"actual={leCpu}");

        // ---- 8. 诊断辅助 ----
        Check("hex / format", TmaFanBuffer.ToHex(Hex("00 0D 04 FF")) == "00 0D 04 FF",
              TmaFanBuffer.ToHex(Hex("00 0D 04 FF")));
        Check("hex / empty", TmaFanBuffer.ToHex(ReadOnlySpan<byte>.Empty) == "");
        Check("temps / short buffer rejected", !TmaFanBuffer.TryParseTemps(new byte[19], out _, out _));

        // ---- 9. 严格形状校验：只放行真机 TM1806 数据块，挡住其他机型的同名类 ----
        const string real1 = "00 00 00 00 04 0D 00 00 E6 0C 00 00 51 00 00 00 3A 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";
        const string frozen = "00 00 00 00 A5 0A 00 00 9F 0A 00 00 49 00 00 00 38 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";
        byte[] good = Hex(real1);
        Check("shape / block length", TmaFanBuffer.BlockLength == 32);
        Check("shape / real snapshot accepted", TmaFanBuffer.IsValidVendorBlock(good));
        Check("shape / frozen block accepted", TmaFanBuffer.IsValidVendorBlock(Hex(frozen)));
        Check("shape / full speed accepted", TmaFanBuffer.IsValidVendorBlock(Build(6231, 6160, 71, 59)));
        Check("shape / 20 bytes rejected", !TmaFanBuffer.IsValidVendorBlock(Build(3300, 3300, 70, 60, length: 20)));
        Check("shape / 33 bytes rejected", !TmaFanBuffer.IsValidVendorBlock(Build(3300, 3300, 70, 60, length: 33)));
        Check("shape / all zero rejected", !TmaFanBuffer.IsValidVendorBlock(new byte[32]));
        Check("shape / header non-zero rejected", !TmaFanBuffer.IsValidVendorBlock(Mutate(good, 0, 0x01)));
        Check("shape / tail non-zero rejected", !TmaFanBuffer.IsValidVendorBlock(Mutate(good, 31, 0x01)));
        Check("shape / CPU RPM high byte rejected", !TmaFanBuffer.IsValidVendorBlock(Mutate(good, 7, 0x01)));
        Check("shape / GPU RPM high byte rejected", !TmaFanBuffer.IsValidVendorBlock(Mutate(good, 11, 0x01)));
        Check("shape / CPU temp high byte rejected", !TmaFanBuffer.IsValidVendorBlock(Mutate(good, 13, 0x01)));
        Check("shape / GPU temp high byte rejected", !TmaFanBuffer.IsValidVendorBlock(Mutate(good, 17, 0x01)));
        Check("shape / zero CPU temp rejected", !TmaFanBuffer.IsValidVendorBlock(Mutate(good, 12, 0)));
        Check("shape / zero GPU temp rejected", !TmaFanBuffer.IsValidVendorBlock(Mutate(good, 16, 0)));
        Check("shape / implausible temp rejected", !TmaFanBuffer.IsValidVendorBlock(Mutate(good, 12, 200)));
        Check("shape / random block with RPM-looking bytes rejected",
            !TmaFanBuffer.IsValidVendorBlock(Mutate(Build(3332, 3302, 70, 60), 0, 0xAA)));

        // ---- 10. 冻结检测：查询成功但字节连续不变 ----
        var f = new VendorFanFreshness(3);
        Check("freshness / threshold", f.Threshold == 3);
        Check("freshness / first frame not frozen", !f.Observe("AA BB") && f.SameCount == 1);
        Check("freshness / second identical not frozen", !f.Observe("AA BB") && f.SameCount == 2);
        Check("freshness / third identical frozen", f.Observe("AA BB") && f.IsFrozen && f.SameCount == 3);
        Check("freshness / stays frozen", f.Observe("AA BB") && f.IsFrozen);
        Check("freshness / new frame thaws", !f.Observe("AA BC") && !f.IsFrozen && f.SameCount == 1);
        f.Reset();
        Check("freshness / reset", !f.IsFrozen && f.SameCount == 0);
        Check("freshness / refreeze after reset",
            !f.Observe("CC") && !f.Observe("CC") && f.Observe("CC") && f.IsFrozen);
        Check("freshness / empty frame resets", !f.Observe("") && f.SameCount == 0);
        bool threw = false;
        try { _ = new VendorFanFreshness(1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check("freshness / bad threshold throws", threw);

        var d = new VendorFanFreshness();
        Check("freshness / default threshold", d.Threshold == VendorFanFreshness.DefaultFrozenThreshold);
        bool frozenBefore8 = false;
        for (int i = 0; i < 7; i++) frozenBefore8 |= d.Observe("SAME");
        Check("freshness / 7 identical frames not frozen", !frozenBefore8 && !d.IsFrozen);
        Check("freshness / 8th identical frame frozen", d.Observe("SAME") && d.IsFrozen);

        // ---- 11. 机型守卫：只有「类不存在」才永久停用 ----
        Check("support / invalid class permanent", VendorFanSupport.IsPermanentAbsence("System.Management.ManagementException: Invalid class"));
        Check("support / localized invalid class permanent", VendorFanSupport.IsPermanentAbsence("ManagementException: 无效的类"));
        Check("support / invalid namespace permanent", VendorFanSupport.IsPermanentAbsence("Invalid namespace"));
        Check("support / not found permanent", VendorFanSupport.IsPermanentAbsence("ManagementException: not found"));
        Check("support / localized not found permanent", VendorFanSupport.IsPermanentAbsence("ManagementException: 找不到"));
        Check("support / access denied recoverable", !VendorFanSupport.IsPermanentAbsence("ManagementException: Access denied"));
        Check("support / timeout recoverable", !VendorFanSupport.IsPermanentAbsence("ManagementException: Timed out"));
        Check("support / missing instance recoverable", !VendorFanSupport.IsPermanentAbsence("RW_TMAWMI data block not found or too short"));
        Check("support / wrong shape recoverable", !VendorFanSupport.IsPermanentAbsence("unexpected RW_TMAWMI data block shape"));
        Check("support / empty recoverable", !VendorFanSupport.IsPermanentAbsence(""));
        Check("support / null recoverable", !VendorFanSupport.IsPermanentAbsence(null));

        // ---- 12. 适配层契约 ----
        Check("pair / default empty", !default(FanRpmPair).HasAny && !default(FanRpmPair).HasBoth);
        Check("pair / one side", new FanRpmPair(2200f, null).HasAny && !new FanRpmPair(2200f, null).HasBoth);
        Check("pair / both", new FanRpmPair(2200f, 2210f).HasBoth);

        // ---- 13. 机型规则目录 ----
        var xiaomi = new FanMachineInfo
        {
            Manufacturer = "Xiaomi", Model = "TM1806",
            AvailableWmiClasses = new[] { "RW_TMAWMI" }
        };
        var other = new FanMachineInfo
        {
            Manufacturer = "OtherBrand", Model = "Generic",
            AvailableWmiClasses = Array.Empty<string>()
        };
        FanProfile? profile = FanProfileCatalog.Match(xiaomi);
        Check("profile / xiaomi matched", profile != null);
        Check("profile / id", profile!.Id == "xiaomi-tm1806-ec");
        Check("profile / CPU register 0x00", profile.CpuRpmRegister == 0x00);
        Check("profile / GPU register 0x02", profile.GpuRpmRegister == 0x02);
        Check("profile / other brand not matched", FanProfileCatalog.Match(other) == null);
        Check("profile / class match case insensitive",
            FanProfileCatalog.Match(new FanMachineInfo { AvailableWmiClasses = new[] { "rw_tmawmi" } }) != null);
        Check("profile / required classes enumerable", FanProfileCatalog.RequiredWmiClasses().Contains("RW_TMAWMI"));
        Check("profile / model pattern enforced",
            !FanProfileCatalog.Matches(new FanProfile { ModelPattern = "ROG" }, xiaomi));

        // ---- 14. EC 16 位小端解码 ----
        byte[] ecOk = { 0xA3, 0x08, 0xA5, 0x08 }; // 2211 / 2213
        Check("ec decode / normal", EcFanDecoder.Decode(ecOk, profile, false, out var ecPair)
              && ecPair.Cpu == 2211f && ecPair.Gpu == 2213f);
        Check("ec decode / swapped", EcFanDecoder.Decode(ecOk, profile, true, out var ecSwapped)
              && ecSwapped.Cpu == 2213f && ecSwapped.Gpu == 2211f);
        Check("ec decode / short buffer rejected", !EcFanDecoder.Decode(new byte[3], profile, false, out _));
        Check("ec decode / one side invalid", EcFanDecoder.Decode(new byte[] { 0x00, 0x00, 0xA5, 0x08 }, profile, false, out var ecPartial)
              && ecPartial.Cpu is null && ecPartial.Gpu == 2213f && ecPartial.HasAny);
        Check("ec decode / all invalid rejected", !EcFanDecoder.Decode(new byte[] { 0x01, 0x00, 0x02, 0x00 }, profile, false, out _));
        Check("ec decode / 200 accepted", EcFanDecoder.Decode(new byte[] { 0xC8, 0x00, 0xC8, 0x00 }, profile, false, out var ecLo)
              && ecLo.Cpu == 200f);
        Check("ec decode / 20000 accepted", EcFanDecoder.Decode(new byte[] { 0x20, 0x4E, 0x20, 0x4E }, profile, false, out var ecHi)
              && ecHi.Cpu == 20000f);
        Check("ec decode / 20001 rejected", !EcFanDecoder.Decode(new byte[] { 0x21, 0x4E, 0x21, 0x4E }, profile, false, out _));

        // ---- 15. 用 WMI 读数校准 EC 左右顺序 ----
        Check("verify / direct mapping", FanProfileVerifier.TryConfirm(
              new FanRpmPair(2200f, 2210f), new FanRpmPair(2210f, 2220f), out bool swap1, out float err1)
              && !swap1 && err1 == 20f);
        Check("verify / swapped mapping", FanProfileVerifier.TryConfirm(
              new FanRpmPair(3000f, 2000f), new FanRpmPair(2000f, 3000f), out bool swap2, out float err2)
              && swap2 && err2 == 0f);
        Check("verify / large mismatch rejected", !FanProfileVerifier.TryConfirm(
              new FanRpmPair(2200f, 2200f), new FanRpmPair(6000f, 6000f), out _, out _));
        Check("verify / null side rejected", !FanProfileVerifier.TryConfirm(
              new FanRpmPair(2200f, null), new FanRpmPair(2200f, 2200f), out _, out _));
        Check("verify / empty rejected", !FanProfileVerifier.TryConfirm(
              default, new FanRpmPair(2200f, 2200f), out _, out _));
        Check("verify / absolute tolerance", FanProfileVerifier.TryConfirm(
              new FanRpmPair(2200f, 2200f), new FanRpmPair(2300f, 2300f), out _, out _));
        Check("verify / relative tolerance at full speed", FanProfileVerifier.TryConfirm(
              new FanRpmPair(6100f, 6100f), new FanRpmPair(6231f, 6160f), out _, out _));

        // ---- 16. 数据源选择与回退 ----
        IFanSource ecLive = new StubFanSource("ec", FanSourceStatus.Ok, new FanRpmPair(2200f, 2210f));
        IFanSource wmiFrozen = new StubFanSource("wmi-frozen", FanSourceStatus.Frozen, new FanRpmPair(2725f, 2719f));
        IFanSource wmiOk = new StubFanSource("wmi-ok", FanSourceStatus.Ok, new FanRpmPair(2232f, 2180f));
        IFanSource broken = new StubFanSource("error", FanSourceStatus.Error, new FanRpmPair(1f, 2f));
        IFanSource empty = new StubFanSource("empty", FanSourceStatus.Ok, default);
        IFanSource unsupported = new StubFanSource("unsupported", FanSourceStatus.Unsupported, new FanRpmPair(999f, 999f));
        IFanSource waiting = new StubFanSource("waiting", FanSourceStatus.Waiting, new FanRpmPair(999f, 999f));

        var normal = new[] { ecLive, wmiFrozen };
        Check("selector / first healthy", FanSourceSelector.Select(normal) == 0);
        Check("selector / CPU value", FanSourceSelector.Read(normal, "CPU.Fan") == 2200f);
        Check("selector / GPU value", FanSourceSelector.Read(normal, "GPU.Fan") == 2210f);
        Check("selector / unrelated key", FanSourceSelector.Read(normal, "CPU.Pump") is null);
        Check("selector / has live source", FanSourceSelector.HasLiveSource(normal));
        Check("selector / skips frozen primary", FanSourceSelector.Select(new[] { wmiFrozen, ecLive }) == 1);
        Check("selector / skips error, waiting, unsupported and empty",
            FanSourceSelector.Select(new[] { broken, waiting, unsupported, empty, ecLive }) == 4);
        Check("selector / nothing healthy", FanSourceSelector.Select(new[] { wmiFrozen, broken, empty }) == -1);
        Check("selector / no live source", !FanSourceSelector.HasLiveSource(new[] { wmiFrozen, broken, empty }));
        Check("selector / null array", FanSourceSelector.Select(null) == -1 && FanSourceSelector.Read(null, "CPU.Fan") is null);
        Check("selector / null element skipped", FanSourceSelector.Select(new IFanSource[] { null!, ecLive }) == 1);

        // ---- 17. 两个关键真机场景 ----
        // 盒子最小化：WMI 冻结但 EC 仍实时 → 仍应显示 EC 的实时值，不是 --，也不是冻结的 2725。
        Check("scenario / minimized control center keeps live EC RPM",
            FanSourceSelector.Select(normal) == 0 && FanSourceSelector.Read(normal, "CPU.Fan") == 2200f);
        // EC 读取失败：自动降级到 WMI。
        Check("scenario / EC failure falls back to WMI",
            FanSourceSelector.Select(new[] { broken, wmiOk }) == 1
            && FanSourceSelector.Read(new[] { broken, wmiOk }, "CPU.Fan") == 2232f);
        // 其他品牌：全部适配器不可用 → 返回 null（界面 --），不残留旧值。
        Check("scenario / no vendor source returns null",
            FanSourceSelector.Read(new[] { unsupported }, "CPU.Fan") is null);

        Console.WriteLine();
        Console.WriteLine($"TOTAL={_total} FAILED={_failed}");
        if (_failed > 0) Environment.Exit(1);
        Console.WriteLine("ALL_ASSERTIONS_PASSED");
    }
}
