using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using LiteMonitor;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 开机启动：以 Windows 计划任务（LogonTrigger + HighestAvailable）实现。
    ///
    /// 说明：Windows 11「设置 → 应用 → 启动」和任务管理器「启动应用」只枚举
    /// 注册表 Run 项与启动文件夹快捷方式，**不会**列出计划任务，所以本开关
    /// 不会出现在那个列表里。核对位置是「任务计划程序库 → LiteMonitor_AutoStart」。
    ///
    /// 保留计划任务而不是改注册表 Run 的原因：读硬件传感器需要 HighestAvailable
    /// 提权，且需要 DisallowStartIfOnBatteries=false 才能在电池状态下自启。
    /// </summary>
    public static class AutoStart
    {
        private const string TaskName = "LiteMonitor_AutoStart";

        // 状态查询需要启动 schtasks 进程，菜单每次重建都会读，这里做短缓存避免卡顿。
        private static readonly object _cacheLock = new object();
        private static bool? _cachedState;
        private static DateTime _cacheUntilUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(20);

        private static string CurrentExePath => Process.GetCurrentProcess().MainModule!.FileName!;

        public static void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedState = null;
                _cacheUntilUtc = DateTime.MinValue;
            }
        }

        /// <summary>
        /// 查询系统里的真实状态。
        /// true = 任务存在、已启用且指向当前 exe；false = 任务确实不存在/已禁用/指向旧副本；
        /// null = 查询过程本身失败（权限、服务不可用等），调用方不应据此改动配置。
        /// </summary>
        public static bool? QueryState(bool useCache = true)
        {
            if (useCache)
            {
                lock (_cacheLock)
                {
                    if (_cachedState.HasValue && DateTime.UtcNow < _cacheUntilUtc)
                        return _cachedState;
                }
            }

            bool? result = QueryFromSystem();

            lock (_cacheLock)
            {
                _cachedState = result;
                _cacheUntilUtc = DateTime.UtcNow + CacheTtl;
            }
            return result;
        }

        /// <summary>菜单显示用：优先系统真实状态；无法判定时回退本地配置。</summary>
        public static bool GetDisplayState(Settings cfg) => QueryState() ?? (cfg?.AutoStart ?? false);

        /// <summary>
        /// 把本地配置与系统真实状态对齐。仅在能确定系统状态时改写，避免查询失败把用户勾选清掉。
        /// 返回 true 表示确实改动了配置（调用方应刷新菜单）。
        /// </summary>
        public static bool Reconcile(Settings cfg)
        {
            if (cfg == null) return false;
            var real = QueryState();
            if (!real.HasValue || real.Value == cfg.AutoStart) return false;

            cfg.AutoStart = real.Value;
            SettingsHelper.Save(cfg);
            return true;
        }

        /// <summary>兼容旧调用：查询失败也算未启用。</summary>
        public static bool IsEnabled() => QueryState() == true;

        private static bool? QueryFromSystem()
        {
            try
            {
                var (code, stdout, stderr) = RunSchtasks($"/Query /TN \"{TaskName}\" /XML");
                if (code != 0)
                {
                    // 只有「明确找不到任务」才能判定为未启用；权限错误等一律返回 null。
                    return LooksLikeMissing(stdout + " " + stderr) ? false : (bool?)null;
                }

                string xml = (stdout ?? "").TrimStart('\uFEFF', '\r', '\n', ' ', '\t');
                if (xml.Length == 0) return null;

                try
                {
                    var doc = XDocument.Parse(xml);
                    var enabled = doc.Descendants()
                        .FirstOrDefault(x => x.Name.LocalName == "Enabled" &&
                                             x.Parent?.Name.LocalName == "Settings")?.Value;
                    if (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase))
                        return false;

                    var command = doc.Descendants()
                        .FirstOrDefault(x => x.Name.LocalName == "Command")?.Value;
                    // 任务还在、但指向另一份 exe（用户换了目录）时，当前这份并没有自启。
                    if (!string.IsNullOrEmpty(command) &&
                        !string.Equals(command, CurrentExePath, StringComparison.OrdinalIgnoreCase))
                        return false;

                    return true;
                }
                catch
                {
                    // 某些系统语言/控制台代码页下 /XML 输出无法解析，此时退出码 0 已足够说明任务存在。
                    return true;
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool LooksLikeMissing(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string[] markers =
            {
                "cannot find", "does not exist", "not found",
                "找不到", "未找到", "不存在", "没有找到"
            };
            return markers.Any(m => text.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// 写入/删除计划任务。返回 false 时 error 给出原因，调用方必须回滚勾选状态。
        /// 旧实现完全忽略 schtasks 退出码，导致杀软拦截或权限不足时「菜单打勾但系统里什么都没有」。
        /// </summary>
        public static bool Set(bool enabled, out string? error)
        {
            error = null;
            string exePath = CurrentExePath;

            try
            {
                string root = Path.GetPathRoot(exePath)!;
                if ((!string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network) ||
                    new Uri(exePath).IsUnc)
                {
                    error = "程序位于网络路径，Windows 计划任务不支持在此运行，请移动到本地硬盘。";
                    return false;
                }
            }
            catch { /* 路径判定失败不阻断后续创建 */ }

            // 已经处于目标状态就不重复写系统，避免每次点「应用」都触发杀软拦截。
            var current = QueryState(useCache: false);
            if (current.HasValue && current.Value == enabled) return true;

            if (enabled)
            {
                string tempXmlPath = Path.Combine(Path.GetTempPath(), $"LiteMonitor_Task_{Guid.NewGuid()}.xml");
                try
                {
                    // XDocument.Save 自动写 UTF-16，schtasks /XML 需要这个编码。
                    GetTaskXml(exePath).Save(tempXmlPath);

                    var (code, _, stderr) = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{tempXmlPath}\" /F");
                    InvalidateCache();
                    if (code != 0)
                    {
                        error = string.IsNullOrWhiteSpace(stderr)
                            ? $"schtasks 创建任务失败（退出码 {code}）"
                            : $"schtasks 创建任务失败（退出码 {code}）：{stderr.Trim()}";
                        return false;
                    }

                    // 退出码 0 也可能因为任务被策略禁用而未真正生效，做一次软校验。
                    var verify = QueryState(useCache: false);
                    if (verify.HasValue && !verify.Value)
                    {
                        error = "计划任务创建后校验未通过，可能被安全软件或组策略拦截。";
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
                finally
                {
                    try { if (File.Exists(tempXmlPath)) File.Delete(tempXmlPath); }
                    catch { /* 临时文件残留无害 */ }
                }
            }
            else
            {
                try
                {
                    var (code, stdout, stderr) = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
                    InvalidateCache();
                    if (code != 0)
                    {
                        string output = (stdout + " " + stderr) ?? "";
                        if (LooksLikeMissing(output)) return true; // 本来就删了/不存在
                        error = string.IsNullOrWhiteSpace(output.Trim())
                            ? $"schtasks 删除任务失败（退出码 {code}）"
                            : $"schtasks 删除任务失败（退出码 {code}）：{output.Trim()}";
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        /// <summary>兼容旧签名（忽略错误信息），保留给不关心结果的调用点。</summary>
        public static bool Set(bool enabled) => Set(enabled, out _);

        private static (int Code, string Out, string Err) RunSchtasks(string arguments)
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var p = Process.Start(psi);
            if (p == null) return (-1, "", "无法启动 schtasks.exe");

            // 并行排空两个管道，避免 stderr 缓冲区写满造成死锁。
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            return (p.ExitCode,
                    outTask.GetAwaiter().GetResult(),
                    errTask.GetAwaiter().GetResult());
        }

        /// <summary>生成任务 XML：登录触发、延迟 5 秒、最高权限、不受电池策略限制。</summary>
        private static XDocument GetTaskXml(string exePath)
        {
            string exeDir = Path.GetDirectoryName(exePath)!;
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

            return new XDocument(
                new XDeclaration("1.0", "UTF-16", null),
                new XElement(ns + "Task",
                    new XAttribute("version", "1.2"),
                    new XElement(ns + "RegistrationInfo",
                        new XElement(ns + "Description", "LiteMonitor Auto Start")
                    ),
                    new XElement(ns + "Triggers",
                        new XElement(ns + "LogonTrigger",
                            new XElement(ns + "Enabled", "true"),
                            new XElement(ns + "Delay", "PT5S")
                        )
                    ),
                    new XElement(ns + "Principals",
                        new XElement(ns + "Principal",
                            new XAttribute("id", "Author"),
                            new XElement(ns + "LogonType", "InteractiveToken"),
                            new XElement(ns + "RunLevel", "HighestAvailable")
                        )
                    ),
                    new XElement(ns + "Settings",
                        new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                        new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                        new XElement(ns + "StopIfGoingOnBatteries", "false"),
                        new XElement(ns + "AllowHardTerminate", "true"),
                        new XElement(ns + "StartWhenAvailable", "false"),
                        new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                        new XElement(ns + "IdleSettings",
                            new XElement(ns + "StopOnIdleEnd", "true"),
                            new XElement(ns + "RestartOnIdle", "false")
                        ),
                        new XElement(ns + "AllowStartOnDemand", "true"),
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "Hidden", "false"),
                        new XElement(ns + "RunOnlyIfIdle", "false"),
                        new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                        new XElement(ns + "Priority", "7")
                    ),
                    new XElement(ns + "Actions",
                        new XAttribute("Context", "Author"),
                        new XElement(ns + "Exec",
                            new XElement(ns + "Command", exePath),
                            new XElement(ns + "WorkingDirectory", exeDir)
                        )
                    )
                )
            );
        }
    }
}
