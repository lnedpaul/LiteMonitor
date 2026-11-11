using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using Microsoft.Win32;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 只读机型探测：SMBIOS 厂商/型号（注册表）+ 规则里声明过的厂商 WMI 类是否存在。
    /// 只查规则里列的类，不枚举 root\wmi 的 1600+ 个类，启动开销控制在一次查询。
    /// </summary>
    internal static class FanMachineDetector
    {
        public static FanMachineInfo Detect()
        {
            string manufacturer = ReadBios("SystemManufacturer");
            string model = ReadBios("SystemProductName");

            var classes = new List<string>();
            foreach (string name in FanProfileCatalog.RequiredWmiClasses())
            {
                if (HasWmiClass(name)) classes.Add(name);
            }

            return new FanMachineInfo
            {
                Manufacturer = manufacturer,
                Model = model,
                AvailableWmiClasses = classes.ToArray()
            };
        }

        private static string ReadBios(string valueName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                return key?.GetValue(valueName)?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static bool HasWmiClass(string name)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM " + name);
                using var results = searcher.Get();
                // Get() 在类不存在时会抛 ManagementException；访问 Count 强制真正执行查询。
                _ = results.Count;
                return true;
            }
            catch (Exception ex)
            {
                // 只有「类不存在」才算机型不匹配；权限不足 / WMI 忙只是暂时读不到，
                // 类仍然存在，应让适配器自己判定并回退，不能把 EC 源直接排除掉。
                return !VendorFanSupport.IsPermanentAbsence(ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    /// <summary>
    /// 风扇数据源管理器（单例）。
    ///
    /// 优先级：机型匹配的 EC 直读源 → 厂商 ACPI WMI 源 →（管理器之外）LHM 通用传感器。
    /// 选择器每帧按健康状态挑源，冻结/失败会自动降级，恢复后自动切回。
    /// 没有命中任何机型规则时，只有 WMI 源，它会自行判定「本机不适用」并停表，
    /// 此时全部回退到 LHM，不会在其他品牌笔记本上做注定失败的轮询。
    /// </summary>
    internal sealed class FanSourceManager
    {
        private static readonly Lazy<FanSourceManager> LazyInstance =
            new(() => new FanSourceManager(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static FanSourceManager Instance => LazyInstance.Value;

        // 机型探测要查一次 WMI，放在后台线程做完后原子替换，避免拖慢启动线程。
        // 探测完成前只有 WMI 源供数，完成后 EC 源自动成为首选。
        private volatile IFanSource[] _sources;
        private volatile string _machineText = "";

        private FanSourceManager()
        {
            var wmi = VendorFanReader.Instance;
            _sources = new IFanSource[] { wmi };

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var machine = FanMachineDetector.Detect();
                    _machineText = $"{machine.Manufacturer} {machine.Model}".Trim();

                    var profile = FanProfileCatalog.Match(machine);
                    if (profile == null) return;

                    IFanSource reference = wmi;
                    var ec = new EcFanSource(profile, () =>
                        reference.Status == FanSourceStatus.Ok
                            ? reference.Reading
                            : default(FanRpmPair));

                    // 数组顺序 = 优先级；volatile 引用整体替换，读侧永远看到完整的一组。
                    _sources = new IFanSource[] { ec, wmi };
                }
                catch
                {
                    // 机型探测失败（WMI 服务忙/权限异常）就保持只有 WMI 源，
                    // 不影响主程序启动，诊断面板会说明没有命中机型规则。
                }
            });
        }

        /// <summary>统一的 CPU.Fan / GPU.Fan 取值入口；无可用源返回 null（界面显示 "--"）。</summary>
        public float? GetRpm(string key) => FanSourceSelector.Read(_sources, key);

        /// <summary>是否至少有一个实时数据源，用于禁止把缓存里的旧转速兜回来。</summary>
        public bool HasLiveSource => FanSourceSelector.HasLiveSource(_sources);

        /// <summary>「系统硬件详情」的诊断行（每秒刷新）。</summary>
        public string[] GetDiagnosticLines()
        {
            IFanSource[] sources = _sources; // 取局部快照，避免后台替换导致行数与内容不一致
            int selected = FanSourceSelector.Select(sources);
            string current = selected >= 0 ? sources[selected].DisplayName : "无（回退到通用传感器）";

            var lines = new List<string>
            {
                string.IsNullOrEmpty(_machineText)
                    ? "机型: 未知 | 当前数据源: " + current
                    : $"机型: {_machineText} | 当前数据源: {current}"
            };

            for (int i = 0; i < sources.Length; i++)
            {
                IFanSource source = sources[i];
                string mark = i == selected ? "[生效]" : "[备用]";
                lines.Add($"{mark} [{i + 1}/{sources.Length}] {source.DisplayName}: {source.DiagnosticText}");
            }

            if (selected < 0)
            {
                lines.Add("没有可用的 EC/厂商数据源；CPU/GPU 风扇显示 --，LHM 通用传感器仍会兜底");
            }

            return lines.ToArray();
        }
    }
}
