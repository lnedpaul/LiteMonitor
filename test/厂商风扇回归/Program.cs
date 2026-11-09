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

        Console.WriteLine();
        Console.WriteLine($"TOTAL={_total} FAILED={_failed}");
        if (_failed > 0) Environment.Exit(1);
        Console.WriteLine("ALL_ASSERTIONS_PASSED");
    }
}
