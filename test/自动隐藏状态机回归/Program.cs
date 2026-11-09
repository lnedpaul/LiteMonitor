using LiteMonitor.src.Core;

internal static class Program
{
    private static int _failed;

    private static void Check(string name, bool actual, bool expected)
    {
        bool ok = actual == expected;
        if (!ok) _failed++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  expected={expected} actual={actual}");
    }

    private static void CheckInt(string name, int? actual, int? expected)
    {
        bool ok = actual == expected;
        if (!ok) _failed++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  expected={expected} actual={actual}");
    }

    private static void CheckEdge(string name, DockEdge actual, DockEdge expected)
        => Check(name, actual == expected, true);

    private static void CheckKind(string name, HideActionKind actual, HideActionKind expected)
        => Check(name, actual == expected, true);

    /// <summary>可手动推进的单调时钟，用于模拟 250ms tick 与拖拽超时。</summary>
    private sealed class FakeClock
    {
        private long _now;
        public FakeClock(long start = 100_000) => _now = start;
        public long Read() => _now;
        public void Advance(double seconds) => _now += (long)Math.Ceiling(seconds * 1000);
    }

    // 单屏 2560x1080；用户真机配置 Position=(2360,0)、PanelWidth=200 正好贴右边。
    private static ScreenRect Area => new(0, 0, 2560, 1080);
    private static WindowRect RightPanel => new(2360, 100, 200, 400);

    private static (AutoHideState State, FakeClock Clock) NewState()
    {
        var clock = new FakeClock();
        return (new AutoHideState(clock.Read), clock);
    }

    private static HideDecision Tick(AutoHideState s, WindowRect bounds, int x, int y,
                                     ScreenRect? area = null, bool enabled = true,
                                     bool visible = true, bool menuOpen = false)
        => s.Evaluate(enabled, visible, menuOpen, area ?? Area, bounds, x, y);

    private static int Main()
    {
        Console.WriteLine("=== 1. 正常循环：贴右隐藏 -> 靠边弹出 ===");
        {
            var (s, _) = NewState();
            var d = Tick(s, RightPanel, 1000, 700);
            CheckKind("贴右且鼠标离开 => 隐藏", d.Kind, HideActionKind.Hide);
            CheckEdge("停靠边为 Right", d.Edge, DockEdge.Right);
            CheckInt("隐藏位置 Left = Right-4", d.NewLeft, 2556);
            Check("状态已置为隐藏", s.IsHidden, true);
            Check("隐藏时锁定屏幕工作区", s.DockArea.HasValue, true);

            var d2 = Tick(s, new WindowRect(2556, 100, 200, 400), 2559, 200);
            CheckKind("鼠标回到右边缘 => 弹出", d2.Kind, HideActionKind.Show);
            CheckInt("弹出位置 Left = Right-窗口宽", d2.NewLeft, 2360);
            Check("弹出后恢复可见态", s.IsHidden, false);
            Check("弹出后清除停靠边", s.Dock == DockEdge.None, true);
            Check("弹出后清除锁定区域", s.DockArea.HasValue, false);
        }

        Console.WriteLine("=== 2. 卡死路径①：MouseUp 丢失，拖拽标志超时自动解锁 ===");
        {
            var (s, clock) = NewState();
            s.BeginDrag();
            CheckKind("拖拽中不隐藏", Tick(s, RightPanel, 1000, 700).Kind, HideActionKind.None);
            clock.Advance(4.9);
            CheckKind("4.9s 仍未解锁（不误判）", Tick(s, RightPanel, 1000, 700).Kind, HideActionKind.None);
            Check("此时仍标记为拖拽中", s.IsDragging, true);
            clock.Advance(0.2);
            var d = Tick(s, RightPanel, 1000, 700);
            CheckKind("超过 5s 自动解锁后恢复隐藏", d.Kind, HideActionKind.Hide);
            Check("拖拽标志已被清除", s.IsDragging, false);

            // 用干净实例验证正常 EndDrag 路径（上面的实例此时已处于隐藏态）。
            var sEnd = NewState().State;
            sEnd.BeginDrag();
            CheckKind("BeginDrag 后拖拽中不隐藏", Tick(sEnd, RightPanel, 1000, 700).Kind, HideActionKind.None);
            sEnd.EndDrag();
            CheckKind("正常 EndDrag 后立刻可隐藏", Tick(sEnd, RightPanel, 1000, 700).Kind, HideActionKind.Hide);
        }

        Console.WriteLine("=== 3. 卡死路径②/③：位置被恢复或钳制后必须复位状态 ===");
        {
            var (s, clock) = NewState();
            Tick(s, RightPanel, 1000, 700);
            Check("先进入隐藏态", s.IsHidden, true);

            // 对应 RestorePos 的降级分支（ScreenDevice 找不到时只改 Location）。
            s.ResetAfterRelocate(0.5);
            Check("位置恢复后不再处于隐藏态", s.IsHidden, false);
            Check("位置恢复后清除停靠边", s.Dock == DockEdge.None, true);
            Check("位置恢复后清除锁定区域", s.DockArea.HasValue, false);
            clock.Advance(0.6);
            CheckKind("状态机重新接管，可再次隐藏", Tick(s, RightPanel, 1000, 700).Kind, HideActionKind.Hide);

            Check("ForceShow 报告原来确实隐藏", s.ForceShow(), true);
            Check("ForceShow 后恢复可见态", s.IsHidden, false);
            CheckKind("ForceShow 的 3 秒保护期生效", Tick(s, RightPanel, 1000, 700).Kind, HideActionKind.None);
            clock.Advance(3.1);
            CheckKind("保护期结束后恢复隐藏", Tick(s, RightPanel, 1000, 700).Kind, HideActionKind.Hide);
        }

        Console.WriteLine("=== 4. 卡死路径④：隐藏后基准屏幕漂移仍要能唤出 ===");
        {
            var (s, _) = NewState();
            Tick(s, RightPanel, 1000, 700);
            Check("已隐藏并锁定原屏", s.DockArea.HasValue, true);

            // 模拟窗口中心越界后 Screen.FromPoint 返回右侧第二块屏 (2560..4480)。
            var otherScreen = new ScreenRect(2560, 0, 4480, 1080);
            var d = Tick(s, new WindowRect(2556, 100, 200, 400), 2559, 200, area: otherScreen);
            CheckKind("换屏后仍按锁定的原屏判定弹出", d.Kind, HideActionKind.Show);
            CheckInt("弹出位置仍落在原屏内", d.NewLeft, 2360);
        }

        Console.WriteLine("=== 5. 左 / 上 / 下三边 ===");
        {
            var (s, _) = NewState();
            var d = Tick(s, new WindowRect(0, 100, 200, 400), 1000, 700);
            CheckEdge("贴左 => Left", d.Edge, DockEdge.Left);
            CheckInt("贴左隐藏位置", d.NewLeft, -196);
            var dl = Tick(s, new WindowRect(-196, 100, 200, 400), 1, 200);
            CheckKind("贴左隐藏后可弹出", dl.Kind, HideActionKind.Show);
            CheckInt("贴左弹出位置", dl.NewLeft, 0);

            var s2 = NewState().State;
            var dt = Tick(s2, new WindowRect(100, 0, 200, 400), 1000, 700);
            CheckEdge("贴上 => Top", dt.Edge, DockEdge.Top);
            CheckInt("贴上隐藏位置", dt.NewTop, -396);
            var dts = Tick(s2, new WindowRect(100, -396, 200, 400), 200, 1);
            CheckKind("贴上隐藏后可弹出", dts.Kind, HideActionKind.Show);
            CheckInt("贴上弹出位置", dts.NewTop, 0);

            var s3 = NewState().State;
            var db = Tick(s3, new WindowRect(100, 680, 200, 400), 1000, 300);
            CheckEdge("贴下 => Bottom（旧逻辑不支持）", db.Edge, DockEdge.Bottom);
            CheckInt("贴下隐藏位置", db.NewTop, 1076);
            var dbs = Tick(s3, new WindowRect(100, 1076, 200, 400), 200, 1079);
            CheckKind("贴下隐藏后可弹出", dbs.Kind, HideActionKind.Show);
            CheckInt("贴下弹出位置", dbs.NewTop, 680);
        }

        Console.WriteLine("=== 6. 边界条件与保护期（单调时钟） ===");
        {
            var (s, clock) = NewState();
            CheckKind("鼠标仍压在窗口上不隐藏", Tick(s, RightPanel, 2400, 200).Kind, HideActionKind.None);
            CheckKind("未贴边不隐藏", Tick(s, new WindowRect(1000, 100, 200, 400), 500, 700).Kind, HideActionKind.None);
            CheckKind("关闭自动隐藏不动作", Tick(s, RightPanel, 1000, 700, enabled: false).Kind, HideActionKind.None);
            CheckKind("窗口不可见不动作", Tick(s, RightPanel, 1000, 700, visible: false).Kind, HideActionKind.None);
            CheckKind("右键菜单打开不动作", Tick(s, RightPanel, 1000, 700, menuOpen: true).Kind, HideActionKind.None);

            s.KeepVisible(3.0);
            CheckKind("保护期内不隐藏", Tick(s, RightPanel, 1000, 700).Kind, HideActionKind.None);
            CheckKind("保护期内重复 tick 仍不隐藏（不依赖系统时间）", Tick(s, RightPanel, 1000, 700).Kind, HideActionKind.None);
            clock.Advance(3.1);
            CheckKind("推进单调时钟后保护期结束", Tick(s, RightPanel, 1000, 700).Kind, HideActionKind.Hide);

            CheckKind("隐藏态下鼠标不在边缘：不重复 Hide", Tick(s, new WindowRect(2556, 100, 200, 400), 1000, 700).Kind, HideActionKind.None);
            Check("隐藏态保持不变", s.IsHidden, true);

            // 弹出后 0.3s 宽限，避免刚弹出立刻又被判定隐藏
            var shown = Tick(s, new WindowRect(2556, 100, 200, 400), 2559, 200);
            CheckKind("鼠标靠边弹出", shown.Kind, HideActionKind.Show);
            CheckKind("弹出宽限期内不立刻重新隐藏", Tick(s, new WindowRect(2360, 100, 200, 400), 1000, 700).Kind, HideActionKind.None);
            clock.Advance(0.4);
            CheckKind("宽限期结束后可再次隐藏", Tick(s, new WindowRect(2360, 100, 200, 400), 1000, 700).Kind, HideActionKind.Hide);
        }

        Console.WriteLine(_failed == 0 ? "ALL_TESTS_PASSED" : $"TESTS_FAILED={_failed}");
        return _failed == 0 ? 0 : 1;
    }
}
