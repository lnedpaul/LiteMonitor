using LiteMonitor;

internal static class Program
{
    private static int _failed;

    private static void Check(string name, bool actual, bool expected)
    {
        bool ok = actual == expected;
        if (!ok) _failed++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  expected={expected} actual={actual}");
    }

    private static Settings Cfg(bool hideMain, bool through, bool showBar, bool tbThrough, bool hideTray)
        => new Settings
        {
            HideMainForm = hideMain,
            ClickThrough = through,
            ShowTaskbar = showBar,
            TaskbarClickThrough = tbThrough,
            HideTrayIcon = hideTray
        };

    private static int Main()
    {
        Console.WriteLine("=== 真值表 ===");
        Check("默认配置", SettingsGuard.IsAllEntriesBlocked(Cfg(false, false, true, false, false)), false);
        Check("用户反馈组合：横条+穿透+关任务栏+隐藏托盘（旧逻辑漏判）",
              SettingsGuard.IsAllEntriesBlocked(Cfg(false, true, false, false, true)), true);
        Check("传统三隐藏（旧逻辑能拦）", SettingsGuard.IsAllEntriesBlocked(Cfg(true, false, false, false, true)), true);
        Check("隐藏主界面+任务栏穿透+隐藏托盘", SettingsGuard.IsAllEntriesBlocked(Cfg(true, false, true, true, true)), true);
        Check("主界面穿透但任务栏可用 => 仍有入口", SettingsGuard.IsAllEntriesBlocked(Cfg(false, true, true, false, true)), false);
        Check("托盘可见 => 永不死锁", SettingsGuard.IsAllEntriesBlocked(Cfg(true, true, false, true, false)), false);
        Check("仅隐藏主界面，任务栏与托盘都在", SettingsGuard.IsAllEntriesBlocked(Cfg(true, false, true, false, true)), false);
        Check("仅任务栏穿透，主界面可交互", SettingsGuard.IsAllEntriesBlocked(Cfg(false, false, true, true, true)), false);
        Check("纯布尔重载与对象重载一致", SettingsGuard.IsAllEntriesBlocked(false, true, false, false, true), true);
        Check("null 配置不判定为死锁", SettingsGuard.IsAllEntriesBlocked((Settings)null), false);

        Console.WriteLine("=== 纠正与落盘 ===");
        var bad = Cfg(false, true, false, false, true);
        int before = SettingsHelper.SaveCount;
        Check("死锁配置触发纠正", SettingsGuard.EnsureInteractiveEntry(bad), true);
        Check("纠正后托盘恢复显示", bad.HideTrayIcon, false);
        Check("纠正后立即落盘", SettingsHelper.SaveCount == before + 1, true);
        Check("纠正后不再判定为死锁", SettingsGuard.IsAllEntriesBlocked(bad), false);
        Check("重复调用幂等", SettingsGuard.EnsureInteractiveEntry(bad), false);
        Check("幂等不重复写盘", SettingsHelper.SaveCount == before + 1, true);

        Console.WriteLine("=== Draft 场景 ===");
        var draft = Cfg(false, true, false, false, true);
        int beforeDraft = SettingsHelper.SaveCount;
        SettingsGuard.EnsureInteractiveEntry(draft, persist: false);
        Check("Draft 纠正不写盘", SettingsHelper.SaveCount == beforeDraft, true);
        Check("Draft 仍完成纠正", draft.HideTrayIcon, false);

        Console.WriteLine("=== 正常配置不打扰 ===");
        var normal = Cfg(false, false, true, false, false);
        int beforeNormal = SettingsHelper.SaveCount;
        SettingsGuard.EnsureInteractiveEntry(normal);
        Check("正常配置不触发写盘", SettingsHelper.SaveCount == beforeNormal, true);

        Console.WriteLine("=== 模拟菜单『先 Save 再 Apply』 ===");
        var menuCfg = Cfg(false, true, false, false, true);
        SettingsHelper.Save(menuCfg);
        Check("菜单先把坏值写盘", SettingsHelper.LastSavedHideTrayIcon, true);
        SettingsGuard.EnsureInteractiveEntry(menuCfg);
        Check("Apply 阶段纠正覆盖此前落盘的坏值", SettingsHelper.LastSavedHideTrayIcon, false);

        Console.WriteLine(_failed == 0 ? "ALL_TESTS_PASSED" : $"TESTS_FAILED={_failed}");
        return _failed == 0 ? 0 : 1;
    }
}
