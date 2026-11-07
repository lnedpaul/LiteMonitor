namespace LiteMonitor
{
    /// <summary>
    /// 可交互入口守卫。
    ///
    /// 软件只有三个能唤出菜单 / 设置中心的入口：
    ///   1. 主界面（面板或横条）右键菜单：隐藏主界面，或开启鼠标穿透，都会让它失效；
    ///   2. 任务栏悬浮窗右键菜单：关闭任务栏显示，或开启任务栏穿透，都会让它失效；
    ///   3. 托盘图标菜单：隐藏托盘图标即失效。
    ///
    /// 任何配置变更路径（右键菜单、设置窗口、启动加载）都必须经过这里，
    /// 保证至少保留一个可用入口，避免把界面锁成「看得见、点不到、也叫不出来」的死状态。
    /// </summary>
    public static class SettingsGuard
    {
        /// <summary>
        /// 纯判定：三个入口是否全部不可用。
        /// 用布尔参数重载，便于测试与设置页在控件值未提交时直接复用同一套规则。
        /// </summary>
        public static bool IsAllEntriesBlocked(
            bool hideMainForm,
            bool clickThrough,
            bool showTaskbar,
            bool taskbarClickThrough,
            bool hideTrayIcon)
        {
            bool mainUnusable = hideMainForm || clickThrough;
            bool taskbarUnusable = !showTaskbar || taskbarClickThrough;
            return mainUnusable && taskbarUnusable && hideTrayIcon;
        }

        /// <summary>基于完整配置对象的判定。</summary>
        public static bool IsAllEntriesBlocked(Settings cfg)
        {
            if (cfg == null) return false;
            return IsAllEntriesBlocked(
                cfg.HideMainForm,
                cfg.ClickThrough,
                cfg.ShowTaskbar,
                cfg.TaskbarClickThrough,
                cfg.HideTrayIcon);
        }

        /// <summary>
        /// 纠正：强制恢复托盘图标，并立即落盘。
        ///
        /// 必须落盘的原因：右键菜单的多个 handler 是「先 cfg.Save() 再 Apply...」的顺序，
        /// 若纠正只发生在内存，磁盘上仍是坏值，重启后死锁会原样复活。
        /// 显式调用 SettingsHelper.Save 而非扩展方法，避免依赖扩展方法的命名空间查找。
        ///
        /// 返回 true 表示确实发生了纠正，调用方需据此刷新托盘显示与菜单勾选状态。
        /// </summary>
        /// <param name="cfg">待检查的配置</param>
        /// <param name="persist">是否立即写盘；设置窗口的 Draft 对象应传 false</param>
        public static bool EnsureInteractiveEntry(Settings cfg, bool persist = true)
        {
            if (!IsAllEntriesBlocked(cfg)) return false;

            cfg.HideTrayIcon = false;
            if (persist) SettingsHelper.Save(cfg);
            return true;
        }
    }
}
