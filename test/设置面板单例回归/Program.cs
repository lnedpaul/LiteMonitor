using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using LiteMonitor.src.Core;
using LiteMonitor.src.UI;
using LiteMonitor.src.UI.Helpers;

namespace SingleInstanceTest
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { _passed++; Console.WriteLine("PASS  " + name); }
            else { _failed++; Console.WriteLine("FAIL  " + name); }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "LiteMonitor.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Cannot locate repo root (LiteMonitor.sln). Run from inside the repo.");
        }

        [STAThread]
        private static int Main()
        {
            Console.WriteLine("=== SettingsForm single-instance regression ===");

            // ---- A. FindExisting 纯判定 ----
            var a = new Form();
            var b = new Form();
            var disposed = new Form();
            disposed.Dispose();

            Check(SettingsFormHost.FindExisting(Array.Empty<Form>(), _ => true) == null,
                "empty collection -> no existing window");
            Check(SettingsFormHost.FindExisting(null!, _ => true) == null,
                "null collection -> no existing window");
            Check(ReferenceEquals(SettingsFormHost.FindExisting(new[] { a, b }, _ => true), a),
                "returns the first matching window");
            Check(SettingsFormHost.FindExisting(new[] { a }, _ => false) == null,
                "non-matching predicate -> no existing window");
            Check(ReferenceEquals(SettingsFormHost.FindExisting(new[] { disposed, b }, _ => true), b),
                "disposed window is skipped");
            Check(SettingsFormHost.FindExisting(new Form[] { null!, b }, _ => true) == b,
                "null element is skipped");

            // ---- B. 源码守卫：所有生产入口必须收敛到 Host ----
            var root = FindRepoRoot();
            var callSites = new[]
            {
                "src/UI/MenuManager.cs",
                "src/UI/Helpers/MenuMonitorHelper.cs",
                "src/UI/Helpers/MainFormBizHelper.cs",
                "src/UI/Helpers/TaskbarBizHelper.cs"
            };
            foreach (var rel in callSites)
            {
                var text = File.ReadAllText(Path.Combine(root, rel));
                Check(!Regex.IsMatch(text, @"new\s+(LiteMonitor\.src\.UI\.)?SettingsForm\s*\("),
                    rel + ": no direct SettingsForm construction");
                Check(text.Contains("SettingsFormHost.ShowOrActivate"),
                    rel + ": routes through SettingsFormHost");
            }
            var hostSrc = File.ReadAllText(Path.Combine(root, "src/UI/Helpers/SettingsFormHost.cs"));
            Check(Regex.Matches(hostSrc, @"new\s+SettingsForm\s*\(").Count == 1,
                "host contains exactly one construction site");
            Check(hostSrc.Contains("Application.OpenForms"),
                "host checks Application.OpenForms before creating");

            // ---- C. 真实 WinForms 行为：连续调用只保留一个窗口 ----
            try
            {
                var owner = new MainForm();
                owner.CreateControl();
                var cfg = new Settings();
                var ui = new UIController();

                int Count() => Application.OpenForms.OfType<SettingsForm>().Count();

                SettingsFormHost.ShowOrActivate(cfg, ui, owner, modal: false);
                Check(Count() == 1, "first call creates exactly one settings window");

                SettingsFormHost.ShowOrActivate(cfg, ui, owner, "Taskbar", modal: false);
                Check(Count() == 1, "second call reuses existing window (no duplicate)");
                var form = Application.OpenForms.OfType<SettingsForm>().First();
                Check(form.CurrentPage == "Taskbar", "reuse switches to requested page");

                form.WindowState = FormWindowState.Minimized;
                SettingsFormHost.ShowOrActivate(cfg, ui, owner, "Plugins", modal: false);
                Check(Count() == 1, "third call still keeps a single window");
                Check(form.WindowState == FormWindowState.Normal, "reuse restores minimized window");
                Check(form.CurrentPage == "Plugins", "reuse switches page again");

                form.Close();
                form.Dispose();
                Check(Count() == 0, "closing the window removes it from OpenForms");

                SettingsFormHost.ShowOrActivate(cfg, ui, owner, modal: false);
                Check(Count() == 1, "after close a fresh call can open a new window");
                Application.OpenForms.OfType<SettingsForm>().First().Dispose();
                owner.Dispose();
            }
            catch (Exception ex)
            {
                Check(false, "WinForms reuse scenario threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            a.Dispose();
            b.Dispose();

            Console.WriteLine($"=== passed={_passed} failed={_failed} ===");
            Console.WriteLine(_failed == 0 ? "ALL_TESTS_PASSED" : "TESTS_FAILED");
            return _failed == 0 ? 0 : 1;
        }
    }
}
