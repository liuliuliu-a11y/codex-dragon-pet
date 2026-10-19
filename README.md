# Codex 小龙桌宠

当前版本：0.2.1。Windows 本地悬浮桌宠，陪伴使用 Codex，并提示工作结束和额度变化。

**下载即用，无需编译：[Windows 安装包](https://github.com/liuliuliu-a11y/codex-dragon-pet/releases/download/v0.2.1/CodexDragonPet-Setup-0.2.1.exe)** · [所有发布文件](https://github.com/liuliuliu-a11y/codex-dragon-pet/releases/latest)

<img src="preview-v0.2.0.png" width="300" alt="Codex 小龙桌宠预览，额度为示例数据">

Windows 悬浮桌面伙伴。银白发、紫瞳、龙角、白色中式裙装的 Q 版形象，借鉴用户指定视频中的 GPT 龙娘特征，由内置 ImageGen 工具重新生成。

形象文件及提示词见 `assets/dragon-sprites-clean.png` 和 `assets/ART-NOTES.md`。

## 使用

普通用户下载上面的 `CodexDragonPet-Setup-0.2.1.exe`，双击安装即可。默认安装到当前用户的 `%LOCALAPPDATA%/Programs/CodexDragonPet`，提供桌面快捷方式和 Codex 自动联动选项，不要求管理员权限。请先在这台电脑安装并登录 Codex。卸载可从 Windows“已安装的应用”进入，会保留个人设置和 Codex 聊天。安装包尚未进行发布者代码签名。

也提供已编译的 `codex-dragon-pet-0.2.1.zip`：解压到长期保留的位置，双击 `start.cmd`，或者直接运行 `bin/CodexDragonPet.exe`。桌宠默认出现在屏幕右下角，置顶显示。已经运行时再次启动会把她从托盘唤回。不要把 GitHub 自动生成的 Source code 压缩包当作免编译运行包。

校验、无人值守安装、升级和卸载说明见 [安装说明](docs/INSTALLATION.md)。

需要同时打开 Codex 时，双击 `start-with-codex.cmd`；它会启动或复用已打开的 Codex 桌面应用，并启动小龙。这是联合启动入口，不会修改系统开机启动项或添加常驻监控程序。`start.cmd` 仍然只启动小龙。

要让原来的 Codex 图标也能自动带起小龙，执行 `./scripts/Install.ps1`。它会为当前用户安装一个轻量联动程序并加入用户登录启动项；以后打开或重新激活新开的 Codex 桌面宿主窗口时，会启动小龙。联动程序只观察进程名和窗口激活事件，不读取窗口文本、键盘或聊天内容，也不做循环轮询。Codex 桌面宿主可能显示为 ChatGPT。

只想手动启动可用 `./scripts/Install.ps1 -NoAutoLink`；关闭已安装的自动联动用 `./scripts/Disable-CodexLink.ps1`，不会删除源码、桌宠设置或聊天。开启自动联动后请保留安装目录。

## 简介与 AI 安装指令（可直接转发）

分享一个我做的 Codex 小龙桌宠：Q 版小龙陪你工作，支持随 Codex 自动启动、简短工作状态、任务完成提醒和额度用量显示，还能拖动、摸头互动。支持 Windows 10/11，下载即可安装，无需编译，也不用另外配置 API 密钥。

把下面这段发给能操作你电脑的 AI（如 Codex）：

> 请帮我安装这个桌宠：https://github.com/liuliuliu-a11y/codex-dragon-pet 。阅读 README 和 docs/INSTALLATION.md，从最新 Release 下载已编译安装包和 SHA256SUMS.txt，校验后按当前用户安装，开启 Codex 自动联动并启动检查。请保留已有设置和聊天记录，不要上传本机数据。

使用前请在本机安装并登录 Codex；需要 .NET Framework 4.8。没有本机操作能力的 AI 也可以按文档指导你手动安装。

开发者也可以从源码安装：

```powershell
git clone https://github.com/liuliuliu-a11y/codex-dragon-pet.git
cd codex-dragon-pet
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Install.ps1
```

上述执行策略只对本次安装进程生效。请先在 Codex 桌面应用中正常登录；保持项目目录不被移动或删除，登录启动项才能继续找到联动程序。

- **拖动角色**：移动位置，退出时自动保存。
- **单击角色**：角色碎碎念和轻轻摇摆。
- **双击角色 / 单击底部状态条**：打开工作与额度面板。
- **右键角色**：切换气泡、提示音、置顶、大小，或收起到托盘、退出。
- **鼠标穿透**：在右键菜单或工作面板中开启；在系统托盘菜单取消勾选即可恢复互动。
- **安静陪伴**：保留状态和重要气泡，关闭声音与托盘弹窗；普通进展气泡自动收起。
- **减少动画**：停止浮动与互动动画，继续保留状态变化及提醒。
- **双击托盘图标**：重新显示小龙和工作面板。

系统托盘和程序使用同一套小龙头像图标，包含 16–64 像素尺寸；源图与图标文件见 `assets/tray-icon.png` 和 `assets/tray.ico`。

## 已实现

六个角色姿态：待机、工作、思考、完成、等待、额度偏低。角色带呼吸浮动，完成时摇摆庆祝。工作进展气泡显示 Codex 公开的 commentary 消息与工具类别；碎碎念明确标注为角色台词。

单击摸头会显示简短回应，姿态切换有淡入过渡。桌面气泡只显示一句话状态，例如“正在阅读文件”“正在检查和测试”；完整公开进展和结果摘要保留在工作面板中。普通气泡过一段时间自动收起，完成时重新显示简短提醒。面板包含最近结束的本机聊天入口。

监听本机 Codex 聊天记录，约每秒读取增量。当观察到正在运行的一轮发出 `task_complete` 事件时，用角色气泡、托盘通知和可关闭的提示音提醒。启动时不重播历史提醒，同一轮不会重复提醒；中断与失败单独显示。完成提示表示该轮已结束，具体结果以 Codex 回复为准。

额度通过本机 Codex 的 `app-server`，调用官方 `account/rateLimits/read` 读取，通常每 60 秒刷新；聊天日志已有新鲜额度时会复用记录，避免重复启动查询进程。支持多个额度桶与实际窗口时长，显示已用比例、剩余比例、重置时间、数据时间；没有数据时显示不可用。80% 和 95% 阈值各提醒一次。过期或较旧数据会标记，等待刷新。

工作面板可自动跟随正在工作的最近聊天，也可固定到一个本机聊天。并行子代理不会单独弹完成提醒。仅监测这台电脑保存的本地聊天，尚不覆盖只在云端运行的任务。

## 数据与设置

- 读取 `$CODEX_HOME/sessions`，未设置时读取 `%USERPROFILE%/.codex/sessions`；不修改聊天记录。
- 设置保存到 `%LOCALAPPDATA%/CodexDragonPet/settings.json`。
- 额度子进程只执行初始化与账户额度读取，不发起模型推理，不额外消耗聊天 token。认证由现有 Codex 程序处理。
- 不读取、展示或保存内部推理文本。公开进展与角色台词各有标识。
- 程序不上传聊天内容，也没有开放本地网络端口。额度刷新需要 Codex 正常登录和联网。
- 当前使用本地 JSONL 事件作为桌面应用的旁路状态来源，日志格式未来变化时可能需要更新适配。读取中断时保留最近数据及更新时间。

## 构建和验证

需要 Windows 与 .NET Framework 4.8。当前机器已直接编译和运行，无需安装 Node、Python、Electron 或额外包。
详细依赖见 [DEPENDENCIES.md](DEPENDENCIES.md)。默认生成 x86 程序；从源码下载后，先双击 `start.cmd` 即可自动构建并启动，也可以执行下列命令。
构建前先退出正在运行的小龙，避免 Windows 锁定程序文件。

```powershell
./build.ps1 -Test
./build.ps1 -Test -UiTest
./bin/CodexDragonPet.exe --diagnose ./diagnostics.json
./bin/CodexDragonPet.exe --preview ./preview.png
./bin/CodexDragonPet.exe --preview-dashboard ./dashboard.png
./scripts/Package.ps1
```

验证覆盖：任务生命周期、重复完成、旧轮迟到事件、中断、公开消息边界、实际额度窗口、多额度桶、空额度、历史提醒抑制、文件截断、UTF-8 分段写入、超长记录、损坏记录、实时文件监听和旧额度覆盖防护。预览图片中的额度明确标为示例。

另有原生 WPF 集成验收：用隔离会话实际写入工作、额度和完成事件，验证窗口可见、工作与庆祝姿态切换、额度抵达界面、完成气泡显示以及通知回调执行。测试记录与界面渲染保存在源码目录的 `verification/ui-integration/`。该测试为独立演示，使用自己的聊天与设置目录。

## 资源占用与限制

背景慢速浮动为 6 fps，安静陪伴为 3 fps，短暂互动与姿态过渡为 24 fps；隐藏时停止持续动画。已复用日志读取缓冲区及未变化的额度控件。测量方法和本机结果见 [性能记录](docs/PERFORMANCE.md)，结果会随硬件、缩放比例和使用方式变化。

状态适配的是本机 Codex JSONL 日志，格式变化时可能需要更新；仅云端运行的任务不在监测范围内。工作状态来自公开进展与工具类别，角色台词不代表内部推理文本。额度是账户窗口的已用比例，不是费用估算。系统通知是否出现、提示音是否可闻，也受 Windows 通知与声音设置影响。

上传排除 `bin/`、`verification/`、`.context/`、依赖目录、缓存、日志和本机设置；诊断记录可能包含聊天标识或公开进展，保留在本地，不上传仓库。

参考：[用户指定视频](https://www.bilibili.com/video/BV1ikaZ6rECj/)；[官方 App Server 文档](https://learn.chatgpt.com/docs/app-server)。形象是个人桌宠设计，不是 OpenAI 官方吉祥物。
