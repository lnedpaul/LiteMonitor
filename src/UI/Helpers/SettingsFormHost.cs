using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI;

namespace LiteMonitor.src.UI.Helpers
{
    /// <summary>
    /// 设置面板的唯一入口。
    ///
    /// 上游各调用点各自 new SettingsForm：右键菜单那条路径用 ShowDialog 且完全不检查
    /// 已有实例，而托盘图标 (MainFormBizHelper) 与任务栏悬浮窗 (TaskbarForm) 是独立于
    /// MainForm 的窗口，模态对话框并不会禁用它们，于是每点一次菜单就叠加一个新窗口。
    /// 每个实例还各自持有一份 Draft 配置深拷贝，后保存的会覆盖先保存的，容易丢设置。
    ///
    /// 这里统一收敛：已存在实例时只切页 / 恢复 / 激活，不再新建。
    /// </summary>
    public static class SettingsFormHost
    {
        // 只保护「构造实例」这一步：SettingsForm 构造期间 InitPages -> SwitchPage
        // 会调用 Application.DoEvents()，极端情况下菜单 handler 可能重入。
        // 不覆盖 ShowDialog 的消息循环，否则模态期间再次点菜单会连激活都被拦掉。
        private static bool _creating;

        public static void ShowOrActivate(
            Settings cfg,
            UIController? ui,
            MainForm form,
            string? targetPage = null,
            bool modal = true)
        {
            var existing = FindExisting(Application.OpenForms.Cast<Form>(), f => f is SettingsForm);
            if (existing != null)
            {
                if (existing is SettingsForm sf && !string.IsNullOrEmpty(targetPage))
                    sf.SwitchPage(targetPage);

                if (existing.WindowState == FormWindowState.Minimized)
                    existing.WindowState = FormWindowState.Normal;

                existing.BringToFront();
                existing.Activate();
                return;
            }

            if (_creating) return;
            _creating = true;
            SettingsForm f;
            try
            {
                f = new SettingsForm(cfg, ui!, form);
                if (!string.IsNullOrEmpty(targetPage)) f.SwitchPage(targetPage);
            }
            finally
            {
                _creating = false;
            }

            if (modal)
            {
                using (f) f.ShowDialog(form);
            }
            else
            {
                // 非模态路径保持与原来的双击行为一致；Close() 会自行释放窗体。
                f.Show(form);
            }
        }

        /// <summary>可从外部注入窗体集合与匹配谓词，便于回归测试覆盖已释放 / 空集合等情形。</summary>
        internal static Form? FindExisting(IEnumerable<Form> forms, Func<Form, bool> match)
        {
            if (forms == null) return null;
            return forms.FirstOrDefault(f => f != null && !f.IsDisposed && match(f));
        }
    }
}
