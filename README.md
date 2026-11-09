[English](./README.en.md)

# <img src="./resources/screenshots/logo.png"  width="28" style="vertical-align: middle; margin-top: -4px;" /> LiteMonitor
一款轻量、可定制的开源桌面硬件监控软件 — 实时监测 CPU、GPU、内存、磁盘、网络等系统性能。

A lightweight and customizable desktop hardware monitoring tool — real-time monitoring of system performance such as CPU, GPU, memory, disk, and network.

支持横/竖屏/任务栏/网页显示、主题切换、多语言、透明度显示、三色报警等，界面简洁且高度可配置 。
> 🟢 **立即下载最新版本：** [📦 GitHub Releases → LiteMonitor 最新版](https://github.com/Diorser/LiteMonitor/releases/latest)    /  [⏬国内镜像网站下载](https://litemonitor.cn/)    

> 🟢 已支持【内存清理】和【自定义插件】功能，详见：[插件开发指南（无需编程）](./resources/plugins/PLUGIN_DEV_GUIDE.md#🔌-plugin-system)


![LiteMonitor 主界面](./resources/screenshots/overview.png)

###  🟢 横条模式 / 任务栏显示模式
![LiteMonitor 横屏/任务栏显示](./resources/screenshots/overview3.png)


# 🖥️ 系统监控功能

| 分类 | 监控指标 |
|------|-----------|
| 💻 **处理器（CPU）**  | 实时监测 CPU 使用率、温度、频率、功耗、风扇、水冷等数据。 |
| 🎮 **显卡（GPU）**  | 展示 GPU 使用率、核心温度、显存、频率、功耗 风扇，兼容 NVIDIA / AMD / Intel 显卡。 |
| 💾 **主机（HOST）** | 显示系统内存占用、FPS刷新率、磁盘温度、主板温度、机箱风扇等，清晰了解电脑综合情况。 |
| 🔋 **电池（Battery）** | 监测电池状态（充电状态、电量、功耗、电流、电压等）。 |
| 📀 **磁盘（Disk）**   | 监控磁盘读取与写入速度（KB/s、MB/s），帮助分析存储 I/O 活跃情况。支持自动/手动选择磁盘。 |
| 🌐 **网络（Network）** | 实时显示上传与下载速度（KB/s、MB/s），提供轻量级网络流量监控。支持自动/手动选择网卡。 |
| 📈 **流量统计（Traffic）** | 统计每日上传与下载流量，帮助分析网络使用习惯。 |
| 🔌 **插件系统（Plugin）** | 支持监控天气、股票、加密货币、代理延迟、汇率等，支持自定义插件，扩展监控项与功能。 |
| 🔧 **硬件传感器（Sensors）** | 硬件详情面板可以查看和监控所有系统硬件传感器数据。 |



> 💡 LiteMonitor 持续完善中，如需更多监控项或功能支持，欢迎在 [GitHub Issues](https://github.com/Diorser/LiteMonitor/issues) 中反馈建议！

---

###  🟢 网络测速功能 
![LiteMonitor 网速测试](./resources/screenshots/overview4.png)  👉 ![LiteMonitor 菜单](./resources/screenshots/overview5.jpg)


###  🟢 监控历史
![LiteMonitor 监控历史](./resources/screenshots/overview9.png)

###  🟢 历史流量统计
![LiteMonitor 主题编辑器](./resources/screenshots/overview7.png)

###  🟢 网页版监控
![LiteMonitor 网页版监控](./resources/screenshots/web.png)
 
---

# 产品功能

| 功能 | 说明 |
|---|---|
| 🎨 自定义主题 | 通过 JSON 定义颜色、字体、间距、圆角等，主题可扩展与复用。主题系统 v2 更易维护。 |
| 🟥🟨🟩 **三色报警** | 监控项根据阈值自动切换进度条/数值颜色，支持自定义颜色与网络/磁盘独立阈值。 |
| 🌍 多语言界面 | 内置多语言，所有菜单/短标签/监控项即时国际化。 |
| 📊 监控项显示管理 | 按需显示或隐藏 CPU、GPU、VRAM、内存、磁盘、网络等模块。 |
| 🧮 **横屏模式** | 全新横条布局，支持每列独立宽度、单位智能格式化、两行显示、自动计算面板宽度。 |
| 📏 面板宽度调整 | 即时调整面板宽度，布局自动重排。 |
| 🔠 **UI 缩放** | 自适应 DPI + 用户自定义缩放，界面与字体完美比例缩放。 |
| 🎞️ **动画平滑** | 数值更新支持平滑动画，降低突变带来的跳动感，可自行调节速度。 |
| 🪟 窗口与界面 | 圆角显示、透明度调节、阴影、高质量字体渲染，视觉干净优雅。 |
| 🧭 靠边自动隐藏 | 靠屏幕边缘自动收起，靠近边缘自动弹出，支持多屏幕正确判断。 |
| 🧲 **限制拖出屏幕** | 选项开启后，窗口不可拖出屏幕可视区域。 |
| 👆 鼠标穿透模式 | 启用后，窗口不拦截鼠标事件，可直接操作背后应用。 |
| 🎨 UI 与主题即时切换 | 切换主题/语言后界面即时刷新，无需重启。 |
| 🔍 数值智能格式化 | 自动格式化单位与小数位，横屏模式支持智能“/s”去除、>=100 自动取整等。 |
| 🔄 自动更新检测 | 启动时静默检查新版本，手动检查时展示弹窗。支持国内与 GitHub 双源。 |
| 🚀 开机自启 | 通过计划任务方式实现管理员级别自启动。 |
| 📂 配置文件存储 | 所有设置实时写入 `settings.json`，支持迁移与备份。 |

---

## 📦 安装与使用

1. 前往 [Releases 页面](https://github.com/Diorser/LiteMonitor/releases) 下载最新版压缩包  
2. 解压后运行 `LiteMonitor.exe`  
3. 程序会自动根据系统语言加载对应语言文件

---

## 🎨 主题系统

主题文件位于 `/themes/` 目录。

示例：
```json
{
  "name": "DarkFlat_Classic",
  "layout": { "rowHeight": 40, "cornerRadius": 10 },
  "color": {
    "background": "#202225",
    "textPrimary": "#EAEAEA",
    "barLow": "#00C853"
  }
}
```

> ✨ 主题系统 v2 新特点：  
> - 布局字段更精简、统一  
> - 字体与布局分别独立缩放  
> - 所有布局由 Theme.Scale 自动处理  
> - 更容易构建自己的主题模板  

---

## ⚙️ 设置文件（settings.json）

| 字段 | 说明 |
|------|------|
| `Skin` | 当前主题名称 |
| `PanelWidth` | 面板宽度（支持横竖屏两种模式） |
| `UIScale` | 用户界面缩放倍率 |
| `Opacity` | 透明度（0.1 ~ 1.0） |
| `Language` | 当前语言（自动检测或手动切换） |
| `TopMost` | 是否置顶窗口 |
| `AutoStart` | 是否开机启动 |
| `AutoHide` | 是否启用靠边自动隐藏 |
| `ClampToScreen` | 拖动后限制窗口在屏幕内 |
| `ClickThrough` | 是否启用鼠标穿透 |
| `RefreshMs` | 刷新间隔（支持完整预设） |
| `AnimationSpeed` | 数值平滑动画速度 |
| `HorizontalMode` | 横屏/竖屏显示模式 |
| `PreferredNetwork` | 手动选择网卡（空=自动） |
| `PreferredDisk` | 手动选择磁盘（空=自动） |
| `Enabled` | 各监控项开关（CPU/GPU/MEM/NET/DISK） |

---

## 🧩 架构概览

| 文件 | 功能 |
|------|------|
| `MainForm_Transparent.cs` | 主窗体、拖拽、菜单托盘、自动隐藏、透明度、位置保存 |
| `UIController.cs` | 主题加载、DPI/UIScale 缩放、布局重构、渲染入口、定时刷新 |
| `UIRenderer.cs` | 竖屏渲染器（组块、进度条、标题渲染） |
| `HorizontalRenderer.cs` | 横屏渲染器（两行布局、智能标签与数值） |
| `UILayout.cs` | 竖屏动态布局计算 |
| `HorizontalLayout.cs` | 横屏列宽计算、面板总宽度计算 |
| `ThemeManager.cs` | 主题加载、颜色解析、字体构建 |
| `LanguageManager.cs` | 多语言加载、扁平化 Key 访问 |
| `HardwareMonitor.cs` | 采集 CPU/GPU/MEM/NET/DISK 信息；自动/手动设备选择 |
| `AutoStart.cs` | 管理计划任务，实现开机自启 |
| `UpdateChecker.cs` | GitHub + 国内双源版本检测 |
| `AboutForm.cs` | 关于窗口 |

---

## 🛠️ 编译说明

### 环境要求
- Windows 10 / 11  
- .NET 10 SDK  
- Visual Studio 2022 或 Rider

### 编译命令
```bash
git clone https://github.com/Diorser/LiteMonitor.git
cd LiteMonitor
dotnet build -c Release
```

输出文件：
```
/bin/Release/net10.0-windows/LiteMonitor.exe
```

### 发布（单文件版，顺序不可颠倒）

提供两种形态，默认与上游一致走**框架依赖单文件**：

| 形态 | 命令 | exe 体积 | 分发 zip | 目标机器 |
| --- | --- | --- | --- | --- |
| 框架依赖（默认） | `.\build-net10.ps1` | 约 5.8 MB | 约 2.4 MB | 需已装 .NET 10 桌面运行时 |
| 自包含（可选） | `.\build-net10.ps1 -SelfContained` | 约 82 MB | — | 免装运行时 |

上游 v1.3.6 的发布包约 2.8 MB，用的就是「框架依赖单文件 + 框架依赖更新器（约 166 KB）+ 打成 zip」
这套组合；本分支默认形态与之对齐。两种形态都仍是真正的单文件：默认主题 / 语言 / 插件配置
与更新器都作为 `EmbeddedResource` 内嵌在 exe 里。

```powershell
.\build-net10.ps1                    # 默认：小体积版
.\build-net10.ps1 -SelfContained     # 可选：免运行时的自包含版
```

脚本依次执行：

1. 发布单文件更新器（默认框架依赖，约 170 KB）→ 覆盖 `resources\LiteMonitor.Updater.exe`；
   （更新器由构建期生成，已不再入库）
2. 以 `PublishSingleFile=true` 发布主程序，把 `resources` 下的配置与更新器嵌入 exe；
3. 自检输出目录只剩一个 exe、确认资源没有被复制成外部文件、记录 SHA256，并清理中间目录；
4. 框架依赖版额外产出只含 exe 的分发 zip（不会把 `settings.json` 等用户数据打进去）。

默认产物：`dist\LiteMonitor-single\LiteMonitor.exe`（6,135,578 B / 5.85 MB）
与 `LiteMonitor-net10-win-x64.zip`（2,498,024 B / 2.38 MB）。

**构建工具链**：.NET 10 SDK `10.0.403` 以便携版随项目放在 `_sdk10\`（已加入 `.gitignore`，
不入库、不受其他项目清理影响）。脚本默认使用 `_sdk10\dotnet\dotnet.exe`，找不到才回退到 PATH
里的 `dotnet`；也可显式指定 `.\build-net10.ps1 -DotNet "F:\path\to\dotnet.exe"`。
若 `_sdk10` 被清理，从微软官方重新下载并解压即可，无需系统级安装、不改 PATH：
`https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.403/dotnet-sdk-10.0.403-win-x64.zip`
→ 解压到 `_sdk10\dotnet\`，使 `dotnet.exe` 位于 `_sdk10\dotnet\dotnet.exe`。

本次构建的校验值：

- 框架依赖版 exe SHA256 `FBFCB7B3D2B2097E8BE530A4C315A093DDC577F7E86FF0FDBBFFDA1D7474D282`
- 分发 zip SHA256 `4245B188224B4DD0E22CB39A393BCA8DBF34879D812CD58F4EFAE87A913977E5`
- 内嵌更新器 SHA256 `7ABEE20463286B9DAB395B658CFBE4C9E61745F964E5C36EFB6ED391CDFF7EE7`
- 自包含版 exe SHA256 `56D3E809FD3EB45D2792C846D808D3F1C10537057AF134EFB4E582382961ED54`（上一次构建，本次代码改动仅重新发布了框架依赖版）

> **取舍**：框架依赖版要求目标机器装有 .NET 10 桌面运行时（本机 SDK 自带 10.0.14）；
> 自包含版把运行时一起打进 exe，可拷到任意 Win10/11 x64，代价是约 82 MB。
> `EnableCompressionInSingleFile` 只对自包含发布有效（框架依赖会报 NETSDK1176），脚本已按形态自动开关。
>
> **首次运行**：程序会在 exe 所在目录自动生成 `resources\` 子目录（主题 / 语言 / 插件 / 更新器），
> 之后照常读写同目录下的 `settings.json` 等数据文件。已存在的文件不会被覆盖，
> 用户自定义主题、语言、插件与更新过的更新器都会保留。
>
> 请把 exe 放在**可写目录**（不要放 `C:\Program Files`）——这一点与原版一致。
> 干净 clone 后请先跑一次 `build-net10.ps1` 再启动；直接 `dotnet build` 仍是常规多文件形态，
> 资源按 Content 复制，适用于开发调试。
>
> **入口防呆**：主界面、任务栏悬浮窗、托盘三个交互入口若同时失效（隐藏主界面或开启鼠标穿通、
> 关闭任务栏显示或任务栏穿通、隐藏托盘图标），程序会自动恢复托盘图标并立即写回配置，
> 避免软件变成「看得见却点不到、也无处可调」的悬浮条；启动时也会自愈历史上已经写坏的
> `settings.json`。回归测试见 `test/入口守卫回归/`（20 条断言）。
>
> **设置面板单实例**：右键菜单、托盘、任务栏悬浮窗、插件管理入口和双击动作统一走
> `SettingsFormHost`。已有设置窗口时只切页 / 恢复 / 激活，不再叠加新窗口，避免多个实例
> 各自持有 Draft 配置而互相覆盖。回归测试见 `test/设置面板单例回归/`（24 条断言）。
>
> **开机启动状态对账**：本开关使用 Windows 计划任务 `LiteMonitor_AutoStart`（登录触发、
> 最高权限），因此**不会**出现在「设置 → 应用 → 启动」列表里——那个列表只枚举注册表 Run 项
> 和启动文件夹快捷方式。核对请打开「任务计划程序 → 任务计划程序库」。程序现在会回读任务的
> 真实状态（禁用 / 指向另一份 exe / 被外部删除都会显示未启用），写入失败时回滚勾选并提示原因，
> 设置面板点「应用」时也会自动重建丢失的任务。
>
> **靠边自动隐藏状态机**：决策逻辑抽到 `src/Core/AutoHideState.cs`（纯 C#、可单测），修复了
> 拖拽 `MouseUp` 丢失导致永久不隐藏、位置钳制与隐藏状态脱节、显示器变化后降级分支未重置状态、
> 多屏下隐藏后唤不出、系统校时影响保护期等问题，并补上贴底边隐藏。
> 回归测试见 `test/自动隐藏状态机回归/`（55 条断言）。

---

## 📄 开源协议
本项目基于 **MIT License** 开源，可自由使用、修改与分发。

---

## 📬 联系方式
**作者**：Diorser  
**项目主页**：[https://github.com/Diorser/LiteMonitor](https://github.com/Diorser/LiteMonitor)
