# BatteryHelper

Windows 11 x64 任务栏功率小程序。功率显示直接作为任务栏子窗口运行，默认左侧、紧凑两行、登录自启动、全屏自动隐藏。

![BatteryHelper 实际任务栏显示](docs/images/taskbar-power.png)

## 使用

运行安装包 `BatteryHelper-Setup-1.0.1-win-x64.exe`，接受 Windows 管理员授权以配置 PawnIO 驱动与采集服务。日常界面以普通权限运行。安装后从开始菜单打开 BatteryHelper。

- 第一行显示电池放电功率或设备可提供的整机输入功率。充电时总输入缺失则显示电池充电功率，标签为“充电”。
- CPU / GPU 始终保留；无电池时第一行显示总功率。
- 传感器缺失、权限不足或超过 5 秒未更新时显示“— W”。电池查询失败不会被判定为无电池。
- 点击任务栏功率或托盘图标可打开详情。托盘右键可切换左右位置、显示/隐藏、自启动和退出。
- “显示与启动”页可选择显示器，设置全屏隐藏和自启动。
- 空间不足或无法确认安全区域时保留托盘入口；空位恢复后自动重新显示。任务栏自动隐藏时功率随父任务栏隐藏，Explorer 重建后自动重新挂载。
- 不再创建桌面悬浮窗。任务栏挂载使用 Win32 子窗口，微软未承诺 Windows 11 任务栏内部结构兼容性；未来系统更新造成挂载不可用时，仍可从托盘查看数据。
- 1.0.1 修复 Windows 11 25H2 上“窗口已挂载但任务栏没有文字”的问题，使用带独立合成层的原生任务栏子窗口；详情和设置继续使用 WPF。
- 关闭详情后继续常驻；选择“退出”会关闭采集连接，服务在没有客户端时暂停采样。

## 数据范围

电池来自 Windows BatteryReport；CPU/GPU 来自固定源码版本的 LibreHardwareMonitor 与 PawnIO。Intel 核显可使用 IGCL 能量计数器回退。

CPU 默认显示封装功率，其范围可能含核显。CPU 和 GPU 不相加作为整机输入。充电器额定功率也不会当作实时输入。无总功率传感器且无电池时，总功率不可用。

卸载通过 Windows“已安装的应用”或开始菜单执行，移除应用、采集服务和自启动项。PawnIO 是可供其他监测工具共用的驱动，应用卸载后保留；如不再需要，可单独卸载 PawnIO。用户设置位于 `%LOCALAPPDATA%\BatteryHelper`。

## 开发与验证

仓库包含应用、采集服务、自动测试和构建脚本。构建产物、下载的 SDK 和上游依赖放在已忽略的 `artifacts/`、`.tools/`、`.vendor/` 中。

在 Windows 11 x64 上克隆仓库后，在项目根目录运行下面的构建脚本。首次构建需要联网，从微软下载已校验 SHA-512 的 .NET SDK、从固定提交下载 LibreHardwareMonitor 源码，并通过 NuGet 恢复包。

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build.ps1
powershell -ExecutionPolicy Bypass -File scripts\Build.ps1 -Installer
```

构建脚本准备项目内的 .NET 10 SDK、固定的上游源码并应用 MSR 读取修正，然后运行测试与发布。构建安装包需要 Inno Setup 7；可通过 `-InnoCompiler` 指定 ISCC.exe。

```powershell
artifacts\publish\service\BatteryHelper.Service.exe --watch-pipe --seconds 10 --out samples.jsonl
artifacts\publish\service\BatteryHelper.Service.exe --probe --seconds 10 --details
```

`--watch-pipe` 验证普通权限读取服务；`--probe` 直接探测硬件，CPU 可能需要管理员权限。

本机验收结果见 [docs/VALIDATION.md](docs/VALIDATION.md)。原始采样和诊断日志在本地 `artifacts/validation/` 中，未提交到 Git 仓库；上面的图片为实际任务栏显示区域。自动测试不能代替拔插充电器、外接屏和休眠等真实操作验收。

## 许可证

BatteryHelper 自有代码采用 [MIT License](LICENSE)。LibreHardwareMonitor 派生文件保留 MPL-2.0；其他依赖保留各自的许可证。详细范围与修改说明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)，完整上游许可证和依赖声明见 [licenses/](licenses/)。
