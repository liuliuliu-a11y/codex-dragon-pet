# 依赖与运行环境

## 运行桌宠

- Windows 10/11 x64，正式 ZIP 自带 .NET 8 Windows Desktop 运行时。
- Codex 工作状态与额度联动需要本机已安装并登录 Codex。
- 聊天、记忆和养成需要另行配置兼容的本机小G服务；基础桌宠不依赖 Python 或模型 API 密钥。

## 当前动画宿主

宿主使用 WPF / .NET 8 和 VPet-Simulator.Core 1.1.0.72，依赖 LinePutScript、LinePutScript.Localization.WPF、Panuon.WPF、Panuon.WPF.UI 和 SkiaSharp。具体版本锁定在项目依赖中，正式包附第三方声明与许可文本。

六档动画使用预先生成的 PNG 和时间线。运行时无需联网生成图像或执行光流计算，缓存限制为近期动作。

## 构建与素材生成

- .NET 8 SDK：当前 `src/VPetHost` 宿主和原生窗口测试。
- 系统 .NET Framework 4.x 编译器：状态桥接与 Codex 联动程序。
- Windows PowerShell 5.1 或 PowerShell 7：构建、更新与打包脚本。
- 重新烘焙六档动画时需要 Python、Pillow、numpy 和 OpenCV。源码附六张原始图，运行 ZIP 已含烘焙结果。

原 `build.ps1` 继续维护旧版分层 WPF 实现，历史说明见 [旧版资料](docs/LEGACY-WPF.md)。当前宿主使用 `scripts/Build-VPet.ps1`，ZIP 使用 `scripts/Package-VPet.ps1` 生成。
