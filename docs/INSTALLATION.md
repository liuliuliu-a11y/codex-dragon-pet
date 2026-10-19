# 安装与卸载

支持 Windows 10/11 x64 与 .NET Framework 4.8。本机需要安装并登录 Codex，桌宠不会替你安装 Codex，也不要求填写 OpenAI API 密钥。

## 直接安装

1. 打开 [最新 GitHub Release](https://github.com/liuliuliu-a11y/codex-dragon-pet/releases/latest)。
2. 下载 `CodexDragonPet-Setup-版本.exe` 和 `SHA256SUMS.txt`。`Source code.zip` 是源码，不是免编译安装包。
3. 在 PowerShell 中用 `Get-FileHash ./CodexDragonPet-Setup-版本.exe -Algorithm SHA256` 计算哈希，与清单中的对应行比较。
4. 双击安装包，按向导安装。默认仅安装到当前用户目录，不要求管理员权限。可选择桌面快捷方式与 Codex 自动联动。
5. 完成安装后启动小龙。新开 Codex 桌面宿主窗口时，小龙会自动出现；没有 Codex 登录或额度数据时，会显示连接状态，不伪造额度。

默认目录为 `%LOCALAPPDATA%/Programs/CodexDragonPet`。自动联动通过当前用户登录启动快捷方式运行轻量窗口事件程序；不记录按键或窗口文本。此安装包尚无发布者代码签名，Windows 可能显示未知发布者。请确认来源为本仓库 Release 并校验文件。

## 让自己的 AI 安装

把仓库网址交给 AI，要求它阅读本文件、优先下载最新 Release 的已编译安装包、核对 SHA-256，并在当前用户下安装。不要提供聊天记录、密码或令牌。

AI 可以用 GitHub API 查找最新 Release；如果匿名 API 返回限流错误，应改用 [最新发布页面](https://github.com/liuliuliu-a11y/codex-dragon-pet/releases/latest) 或 README 的直接下载链接。页面会跳转到最新版本，附件也可以公开下载。不要因为 API 限流而要求用户提供 GitHub 令牌或登录凭据。

支持 Inno Setup 的标准无人值守参数，例如：

```powershell
$setup = Start-Process -FilePath './CodexDragonPet-Setup-0.2.2.exe' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=desktopicon,autolink' -PassThru
$setup.WaitForExit()
if ($setup.ExitCode -ne 0) { throw "Installation failed: $($setup.ExitCode)" }
```

静默安装后可运行 `%LOCALAPPDATA%/Programs/CodexDragonPet/bin/CodexDragonPet.exe`。默认选择的联动程序也会运行；如果 Codex 已打开，它会带起桌宠。

这里使用进程对象的 `WaitForExit()`；PowerShell 的 `Start-Process -Wait` 会连同常驻桌宠等子进程一起等待，可能一直不返回。

`/TASKS=desktopicon,autolink` 明确开启桌面快捷方式与自动联动，避免沿用上次安装时关闭联动的选择。AI 应确认有本机程序执行能力；仅能聊天的 AI 需要指导用户运行安装器。

安装完成后，AI 还应检查以下结果，不能只凭安装器退出码就报告全部成功：

- 当前用户 Startup 中存在 `Codex Dragon Pet Link.lnk`，目标指向实际安装目录下的 `bin/CodexPetWatcher.exe`；对应的辅助进程正在运行。
- 正常打开 Codex 桌面应用后，小龙可见。v0.2.2 支持 Codex 在同一个进程里新开窗口；重复启动不会多开桌宠。
- 若 Windows“启动应用”设置禁用了该快捷方式，后续登录不会启动联动程序，需要用户重新启用。不要为测试强行关闭正在运行安装任务的 Codex、退出 Windows 或重启电脑。

本次发布已验证公开下载与 SHA-256、当前用户安装、真实登录启动快捷方式，以及通过 Windows 注册入口打开实际 Codex 后自动带起桌宠。新窗口的回归检查另用隔离窗口宿主覆盖，测试不依赖读取真实聊天。

## 升级与卸载

运行新版本安装包，使用原安装目录即可升级。安装和卸载会先关闭这个安装目录下的桌宠与联动程序；Codex 本身和其他目录中的程序不会被关闭。请先退出其他目录中的便携版桌宠，避免单实例机制使新安装的程序无法接管。

卸载入口在 Windows“已安装的应用”或开始菜单“Codex 小龙桌宠”中。卸载只移除安装器记录的程序文件、快捷方式和卸载注册信息，保留 `%LOCALAPPDATA%/CodexDragonPet` 下的设置及 Codex 聊天记录。用户后来放入安装目录的额外文件也不会被递归清空。

## 免安装 ZIP

下载 `codex-dragon-pet-版本.zip`，解压到固定目录，双击 `start.cmd` 即可运行。需要自动联动时运行 `scripts/Install.ps1`；关闭联动运行 `scripts/Disable-CodexLink.ps1`。便携版没有安装器卸载入口，关闭程序及联动后可删除它自己的解压目录。
