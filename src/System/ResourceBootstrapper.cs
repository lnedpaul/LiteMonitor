using System;
using System.IO;
using System.Reflection;

namespace LiteMonitor
{
    /// <summary>
    /// 单文件发布（PublishSingleFile）时，resources 下的默认主题 / 语言 / 插件配置
    /// 以及独立更新器会以 EmbeddedResource 形式打进 exe。
    ///
    /// 单文件应用的 AppContext.BaseDirectory 指向 exe 所在目录，而 bundle 的
    /// 自解压临时目录无法被现有代码直接访问，所以在启动最早期把这些资源
    /// 释放回 exe 旁的 resources 目录，让 LanguageManager / ThemeManager /
    /// PluginManager / UpdateDialog 原有的 BaseDirectory 读取逻辑保持零改动。
    ///
    /// 已存在的文件一律不覆盖，以保留用户自定义的主题、语言、插件，
    /// 以及自动更新流程替换过的更新器。
    /// </summary>
    internal static class ResourceBootstrapper
    {
        private const string Prefix = "LiteMonitor.Embedded.";

        internal static void Ensure()
        {
            // 常规 build / 多文件发布不带这批嵌入资源，GetManifestResourceNames
            // 仍会返回 Properties.Resources 等其它资源，前缀过滤后自然为空。
            string[] names;
            try
            {
                names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            }
            catch
            {
                return;
            }

            var assembly = Assembly.GetExecutingAssembly();

            foreach (var name in names)
            {
                if (!name.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    string relative = name.Substring(Prefix.Length)
                                          .Replace('/', Path.DirectorySeparatorChar);
                    if (relative.Length == 0)
                    {
                        continue;
                    }

                    string target = Path.Combine(AppContext.BaseDirectory, relative);
                    if (File.Exists(target))
                    {
                        continue;
                    }

                    string? dir = Path.GetDirectoryName(target);
                    if (string.IsNullOrEmpty(dir))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(dir);

                    using var source = assembly.GetManifestResourceStream(name);
                    if (source == null)
                    {
                        continue;
                    }

                    // CreateNew：多个实例并发启动时由文件系统保证只有一个写入者
                    using (var dest = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        source.CopyTo(dest);
                    }
                }
                catch (IOException)
                {
                    // 目标已被另一个实例创建，或目录不可写：忽略，不阻断启动
                }
                catch (UnauthorizedAccessException)
                {
                    // 只读目录：忽略（settings.json 本来也要求可写目录）
                }
                catch
                {
                    // 单个资源释放失败不应导致程序无法启动
                }
            }
        }
    }
}
