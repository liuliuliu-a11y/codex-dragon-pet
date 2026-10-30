## 当前 ZIP 运行包

最新版使用 v0.8.1 白龙原生宿主。Windows 10/11 64 位下载完整 ZIP，解压后双击 `start.cmd`；升级现有安装时解压到新文件夹并运行 `update.cmd`。详细操作见 [白龙使用说明](WHITE-DRAGON.md)。当前 ZIP 已包含 .NET 8 运行时。

旧安装器与分层 WPF 版本的安装、卸载和诊断资料保留在下文，对应各自历史版本。

---
# 安装、更新与卸载

支持 Windows 10/11 与 .NET Framework 4.8。本机需要安装并登录 Codex，桌宠不要求另行填写 API 密钥。

## 下载与首次运行

1. 打开 [最新 GitHub Release](https://github.com/liuliuliu-a11y/codex-dragon-pet/releases/latest)。
2. 下载 `codex-dragon-pet-0.6.0.zip` 和 `SHA256SUMS.txt`。本版提供已编译 ZIP，没有 Setup.exe；GitHub 自动附带的 Source code 是源码。
3. 用 `Get-FileHash ./codex-dragon-pet-0.6.0.zip -Algorithm SHA256` 核对清单中的哈希。
4. 解压到长期保留的位置，双击 `start.cmd`。默认大小为 50%，右键“调整大小”可更改。
5. 希望随 Codex 启动时运行 `scripts/Install.ps1`；它为当前用户创建桌面入口和自动联动快捷方式。只想手动运行，直接使用 `start.cmd` 即可。

公开发布包不需要 GitHub 令牌。匿名 API 限流时可用 Release 页面下载，不必提供账号凭据。本程序尚无发布者代码签名，请确认来源并核对校验清单。

## 更新已有安装

1. 将新版 ZIP **解压到一个新文件夹**，不要直接解压覆盖运行中的程序。
2. 双击新文件夹内的 `update.cmd`。
3. 更新程序从当前用户的 `Codex Dragon Pet Link.lnk` 找到旧安装目录；没有联动入口时，检查 `%LOCALAPPDATA%/Programs/CodexDragonPet`。
4. 更新程序停止该目录下的桌宠与联动程序，备份旧 `bin`，替换并校验运行文件，然后重新启动。备份保留在原安装目录 `backup/update-时间-编号/bin` 中。
5. 用户设置与自动联动入口不变。已有用户想采用新版小尺寸，可右键选择“调整大小 → 50%”。

自定义便携目录可显式指定：

```powershell
powershell -NoProfile -File ./scripts/Update-Installed.ps1 -TargetDirectory '旧桌宠目录'
```

请先退出其他目录中的便携版桌宠，避免单实例机制让旧进程继续占用显示。更新入口不关闭 Codex，也不改聊天记录。复制或校验失败会尝试恢复备份；失败提示保留在命令窗口中。

如果需要手动回退，先退出桌宠和联动程序，把备份中的 `bin` 内容复制回原安装目录的 `bin`，再启动原路径下的程序。不要把其他个人文件放入运行时 `bin/assets` 目录。

## 联动与设置

- 设置位于 `%LOCALAPPDATA%/CodexDragonPet/settings.json`，与程序目录分开。
- 联动程序只观察桌面宿主进程与窗口事件，不读取窗口文字、按键或聊天内容。
- 当前用户 Startup 中的 `Codex Dragon Pet Link.lnk` 应指向保留位置的 `bin/CodexPetWatcher.exe`。若 Windows 禁用了该启动项，需自行重新启用。
- 没有登录或额度数据时显示连接状态，不伪造额度。
- 关闭自动联动可运行 `scripts/Disable-CodexLink.ps1`。

## 卸载

便携版：先关闭联动与桌宠，再移除它自己的解压目录和桌面入口。用户设置与 Codex 聊天记录可以继续保留。

历史 Setup.exe 安装版：可在 Windows“已安装的应用”中卸载。历史 0.2.2 安装器不含本版功能，不应作为更新包下载。
