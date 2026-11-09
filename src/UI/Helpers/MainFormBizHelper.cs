using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.Core.Actions;
using LiteMonitor.src.SystemServices;

namespace LiteMonitor.src.UI.Helpers
{
    /// <summary>
    /// 主窗口业务助手 (Business Helper)
    /// 职责：自动隐藏、托盘交互、快捷动作、启动流程、布局切换
    /// </summary>
    public class MainFormBizHelper : IDisposable
    {
        private readonly Form _form;
        private readonly Settings _cfg;
        private readonly UIController _ui;
        private readonly MainFormWinHelper _winHelper;
        private readonly NotifyIcon _tray;

        // 自动隐藏相关：状态与决策全部委托给可独立测试的纯状态机 AutoHideState
        private System.Windows.Forms.Timer? _autoHideTimer;
        private System.Windows.Forms.Timer? _topMostTimer;
        private readonly AutoHideState _hideState = new AutoHideState();

        public bool IsHidden => _hideState.IsHidden;
        public bool IsDragging
        {
            get => _hideState.IsDragging;
            set { if (value) _hideState.BeginDrag(); else _hideState.EndDrag(); }
        }

        public MainFormBizHelper(Form form, Settings cfg, UIController ui, MainFormWinHelper winHelper)
        {
            _form = form;
            _cfg = cfg;
            _ui = ui;
            _winHelper = winHelper;
            _tray = new NotifyIcon();
        }

        public void Initialize()
        {
            InitTray();
            if (_cfg.AutoHide) StartTimer();
            StartTopMostTimer();
        }

        // =================================================================
        // 自动隐藏逻辑
        // =================================================================
        public void StartTimer()
        {
            _autoHideTimer ??= new System.Windows.Forms.Timer { Interval = 250 };
            _autoHideTimer.Tick -= AutoHideTick;
            _autoHideTimer.Tick += AutoHideTick;
            _autoHideTimer.Start();
        }

        public void StopTimer() => _autoHideTimer?.Stop();

        public void KeepVisible(double seconds) => _hideState.KeepVisible(seconds);

        public void ForceShow()
        {
            // 先取出隐藏时锁定的屏幕基准（ForceShow 会清空它），再复位状态。
            var dockArea = _hideState.DockArea;
            var edge = _hideState.Dock;
            bool wasHidden = _hideState.ForceShow();
            if (!wasHidden) return;

            if (dockArea.HasValue)
            {
                // 用同一块屏幕的工作区把窗口拉回，避免 Screen.FromControl 在中心越界时选错屏。
                int? left = edge switch
                {
                    DockEdge.Right => dockArea.Value.Right - _form.Width,
                    DockEdge.Left => dockArea.Value.Left,
                    _ => (int?)null
                };
                int? top = edge switch
                {
                    DockEdge.Top => dockArea.Value.Top,
                    DockEdge.Bottom => dockArea.Value.Bottom - _form.Height,
                    _ => (int?)null
                };
                if (left.HasValue) _form.Left = left.Value;
                if (top.HasValue) _form.Top = top.Value;
            }
            else
            {
                ClampToScreen(force: true);
            }
        }

        private void AutoHideTick(object? sender, EventArgs e) => CheckAutoHide();

        private void StartTopMostTimer()
        {
            _topMostTimer ??= new System.Windows.Forms.Timer { Interval = 10000 };
            _topMostTimer.Tick -= TopMostTick;
            _topMostTimer.Tick += TopMostTick;
            _topMostTimer.Start();
        }

        private void TopMostTick(object? sender, EventArgs e)
        {
            if (!_form.Visible) return;

            if (_cfg.TopMost)
            {
                if (!_winHelper.IsTopMostStyleApplied())
                {
                    _winHelper.RefreshTopMost(true, forceReinsert: true);
                }
            }
            else if (_winHelper.IsTopMostStyleApplied())
            {
                _winHelper.RefreshTopMost(false);
            }
        }

        private void CheckAutoHide()
        {
            // 环境数据由 WinForms 采集，决策交给纯状态机 AutoHideState（可由回归测试覆盖）。
            // 隐藏态时状态机会使用自己锁定的 DockArea，这里传入的 area 只在未隐藏时生效，
            // 从而避免窗口中心越界后 Screen.FromPoint 切到另一块显示器。
            var center = new Point(_form.Left + _form.Width / 2, _form.Top + _form.Height / 2);
            var workingArea = Screen.FromPoint(center).WorkingArea;
            var area = new ScreenRect(workingArea.Left, workingArea.Top,
                                      workingArea.Right, workingArea.Bottom);
            var bounds = new WindowRect(_form.Left, _form.Top, _form.Width, _form.Height);
            var cursor = Cursor.Position;

            var decision = _hideState.Evaluate(
                _cfg.AutoHide,
                _form.Visible,
                _form.ContextMenuStrip?.Visible == true,
                area, bounds, cursor.X, cursor.Y);

            if (decision.Kind == HideActionKind.None) return;
            if (decision.NewLeft.HasValue) _form.Left = decision.NewLeft.Value;
            if (decision.NewTop.HasValue) _form.Top = decision.NewTop.Value;
        }

        // =================================================================
        // 托盘管理
        // =================================================================
        private void InitTray()
        {
            try { _tray.Icon = Properties.Resources.AppIcon ?? _form.Icon; } catch { _tray.Icon = _form.Icon; }
            _tray.Text = "LiteMonitor";
            _tray.Visible = !_cfg.HideTrayIcon;

            RebuildMenus();

            _tray.MouseUp += (_, e) => 
            {
                if (e.Button == MouseButtons.Right)
                {
                    MainFormWinHelper.ActivateWindow(_form.Handle);
                    _form.ContextMenuStrip?.Show(Cursor.Position);
                }
            };
            
            _tray.MouseDoubleClick += (_, e) => 
            {
                if (e.Button == MouseButtons.Left) 
                {
                    if (_form.Visible) ((MainForm)_form).HideMainWindow();
                    else ((MainForm)_form).ShowMainWindow();
                }
            };
        }

        public void RebuildMenus()
        {
            if (_form.ContextMenuStrip != null)
            {
                _form.ContextMenuStrip.Dispose();
                _form.ContextMenuStrip = null;
            }
            _form.ContextMenuStrip = MenuManager.Build((MainForm)_form, _cfg, _ui);
            UIUtils.ClearBrushCache();
        }

        public void ShowNotification(string title, string text, ToolTipIcon icon)
        {
            if (_tray.Visible) _tray.ShowBalloonTip(5000, title, text, icon);
        }

        public void SetTrayVisible(bool visible) => _tray.Visible = visible;

        // =================================================================
        // 布局切换与位置管理
        // =================================================================
        public void ToggleLayoutMode()
        {
            _form.SuspendLayout();
            try
            {
                // 记录旧模式
                bool oldMode = _cfg.HorizontalMode;
                
                _cfg.HorizontalMode = !oldMode;
                _cfg.Save();
                
                // ★ 统一使用 AppActions，传入旧模式以触发自动居中
                Core.Actions.AppActions.ApplyThemeAndLayout(_cfg, _ui, (MainForm)_form, oldMode);
            }
            finally
            {
                _form.ResumeLayout(true);
            }
        }

        public void SavePos()
        {
            // 隐藏态下 Left = area.Right - 4，是状态机的临时位置，绝不能写进配置：
            // 否则下次启动 RestorePos 会把「隐藏位置」当成正常位置，且会连带触发一次错误的钳制。
            if (_hideState.IsHidden) return;

            ClampToScreen(force: false);
            var center = new Point(_form.Left + _form.Width / 2, _form.Top + _form.Height / 2);
            var scr = Screen.FromPoint(center);

            _cfg.ScreenDevice = scr.DeviceName;
            _cfg.Position = new Point(_form.Left, _form.Top);
            _cfg.Save();
        }

        public void RestorePos()
        {
            Screen? savedScreen = null;
            if (!string.IsNullOrEmpty(_cfg.ScreenDevice))
            {
                savedScreen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _cfg.ScreenDevice);
            }

            if (savedScreen != null)
            {
                var area = savedScreen.WorkingArea;
                int x = _cfg.Position.X;
                int y = _cfg.Position.Y;
                SetSafeLocation(area, x, y);
            }
            else
            {
                var screen = Screen.FromControl(_form);
                var area = screen.WorkingArea;
                if (_cfg.Position.X >= 0) _form.Location = _cfg.Position;
                else
                {
                    int x = area.Right - _form.Width - 50; 
                    int y = area.Top + (area.Height - _form.Height) / 2;
                    _form.Location = new Point(x, y);
                }
            }

            // [Fix] 两个分支都必须复位隐藏状态。旧代码只在「找到原屏幕」分支做了这件事，
            // 显示器重编号 / 拔掉外接屏后走降级分支时，窗口已经被挪回可见区域，
            // 状态机却仍认为 _isHidden=true，隐藏分支被永久挡住 —— 这就是「有失效几率」的主因之一。
            _hideState.ResetAfterRelocate(0.5);
        }

        public void ClampToScreen(bool force = false)
        {
            // 隐藏态的位置由状态机独占（Left/Right 故意留在屏幕外 4px）。
            // 旧代码会把它强行拉回屏幕内却不重置隐藏标志，造成「看得见但不再隐藏」的卡死。
            if (_hideState.IsHidden) return;
            if (!_cfg.ClampToScreen && !force) return;

            var area = Screen.FromControl(_form).WorkingArea;
            int x = _form.Left;
            int y = _form.Top;

            // 修复逻辑：
            // 如果启用了自动隐藏，允许窗口稍微贴边，而不是强制弹开
            // 只有当窗口完全跑出屏幕外时，才强制拉回
            // 之前的 margin = _hideThreshold + 1 逻辑会导致：用户刚拖到边缘想隐藏，就被弹回来了

            if (x < area.Left) x = area.Left;
            if (x + _form.Width > area.Right) x = area.Right - _form.Width;
            if (y < area.Top) y = area.Top;
            if (y + _form.Height > area.Bottom) y = area.Bottom - _form.Height;

            _form.Location = new Point(x, y);
        }

        private void SetSafeLocation(Rectangle area, int x, int y)
        {
            if (x < area.Left) x = area.Left;
            if (y < area.Top) y = area.Top;
            if (x + _form.Width > area.Right) x = area.Right - _form.Width;
            if (y + _form.Height > area.Bottom) y = area.Bottom - _form.Height;
            _form.Location = new Point(x, y);
        }

        // =================================================================
        // 快捷动作
        // =================================================================
        public void HandleDoubleClick()
        {
            switch (_cfg.MainFormDoubleClickAction)
            {
                case 1: OpenTaskManager(); break;
                case 2: OpenSettings(); break;
                case 3: OpenTrafficHistory(); break;
                case 4: CleanMemory(); break;
                case 5: WebActions.OpenWebMonitor(_cfg); break;
                case 0: default: ToggleLayoutMode(); break;
            }
        }

        public void OpenTaskManager()
        {
            try { Process.Start(new ProcessStartInfo("taskmgr") { UseShellExecute = true }); } catch { }
        }

        public void OpenSettings()
        {
            SettingsFormHost.ShowOrActivate(_cfg, _ui, (MainForm)_form, modal: false);
        }

        public void OpenTrafficHistory()
        {
            foreach (Form f in Application.OpenForms) { if (f is TrafficHistoryForm) { f.Activate(); return; } }
            new TrafficHistoryForm(_cfg).Show();
        }

        public async void CleanMemory()
        {
            try { using (var form = new CleanMemoryForm()) await form.StartCleaningAsync(); } catch { }
        }

        // =================================================================
        // 启动流程
        // =================================================================
        public async Task RunStartupChecksAsync()
        {
            try
            {
                if (HardwareMonitor.Instance != null) await HardwareMonitor.Instance.SmartCheckDriver();
                await UpdateChecker.CheckAsync();
                CheckUpdateSuccess();

                // 如果发现新版本，重新构建菜单以显示“发现新版本”按钮
                if (UpdateChecker.IsUpdateFound)
                {
                    _form.BeginInvoke(new Action(() => RebuildMenus()));
                }

                // [Fix] 再次确认窗口属性（置顶、穿透），作为启动后的二次校验，确保功能与 UI 勾选一致
                _form.BeginInvoke(new Action(() => {
                    Core.Actions.AppActions.ApplyWindowAttributes(_cfg, (MainForm)_form);
                }));
            }
            catch { }
        }

        private void CheckUpdateSuccess()
        {
            string tokenPath = Path.Combine(AppContext.BaseDirectory, "update_success");
            if (File.Exists(tokenPath))
            {
                try { File.Delete(tokenPath); } catch { }
                string title = "⚡️LiteMonitor_v" + UpdateChecker.GetCurrentVersion();
                string content = _cfg.Language == "zh" ? "🎉 软件已成功更新到最新版本！" : "🎉 Software updated to latest version!";
                ShowNotification(title, content, ToolTipIcon.Info);
            }
        }

        public void Dispose()
        {
            _tray.Visible = false;
            _tray.Dispose();
            _autoHideTimer?.Stop();
            _autoHideTimer?.Dispose();
            _topMostTimer?.Stop();
            _topMostTimer?.Dispose();
        }
    }
}
