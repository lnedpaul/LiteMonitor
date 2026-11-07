namespace LiteMonitor
{
    // 与真实 Settings 同名同命名空间的最小桩，只暴露守卫用到的字段
    public class Settings
    {
        public bool HideMainForm;
        public bool ClickThrough;
        public bool ShowTaskbar;
        public bool TaskbarClickThrough;
        public bool HideTrayIcon;
    }

    // 替身：记录落盘次数与最后一次写盘时的托盘开关值
    public static class SettingsHelper
    {
        public static int SaveCount;
        public static bool LastSavedHideTrayIcon;

        public static void Save(Settings settings)
        {
            SaveCount++;
            LastSavedHideTrayIcon = settings.HideTrayIcon;
        }
    }
}
