using System.Drawing;
using System.Windows.Forms;
using LiteMonitor.src.Core;

// 用最小桩替换真实业务类型，使真实源码 SettingsFormHost.cs 能被独立编译执行。
namespace LiteMonitor.src.Core
{
    public class Settings { }
}

namespace LiteMonitor.src.UI
{
    public class UIController { }

    public class MainForm : Form { }

    public class SettingsForm : Form
    {
        public string? CurrentPage { get; private set; }

        public SettingsForm(Settings cfg, UIController ui, MainForm form)
        {
            // 测试窗口不打扰用户：移出可视区域、不进任务栏
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
        }

        public void SwitchPage(string key) => CurrentPage = key;
    }
}
