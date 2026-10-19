# 依赖与运行环境

- 系统：Windows 10/11 x64。
- 运行时：.NET Framework 4.8，使用系统自带的 WPF、Windows Forms、System.Drawing 和 JSON 序列化组件。
- Codex：需要在这台电脑上运行并正常登录。聊天状态从本地 `sessions/*.jsonl` 增量读取；账户额度由现有 Codex 可执行程序的 App Server 查询。
- 构建：Windows 自带的 .NET Framework C# 编译器。`build.ps1` 使用 `%WINDIR%/Microsoft.NET/Framework64/v4.0.30319/csc.exe`，默认生成 x86 程序以降低本机测得的内存开销，可通过 `-Platform anycpu` 或 `-Platform x64` 切换。
- PowerShell：Windows PowerShell 5.1 或 PowerShell 7，用于构建、打包和性能采样。
- 无需 Node.js、Python、Electron、完整 .NET SDK，也没有 npm、pip 或 NuGet 第三方包依赖。

不需要为桌宠填写 API 密钥。额度连接使用 Codex 已有登录状态，只发送初始化与 `account/rateLimits/read` 请求，不发起模型推理。

首次从源码启动时，`start.cmd` 会在 `bin/` 中缺少程序时调用 `build.ps1`。运行包中已包含程序，可以直接双击 `start.cmd`。

`start-with-codex.cmd` 为联合启动入口，使用 Windows 已注册的 OpenAI.Codex 应用标识唤起 Codex，不会把 Codex CLI 当成桌面程序启动。

GPU 性能采样依赖 Windows 的 GPU Engine 与 GPU Process Memory 计数器；计数器不可用时会记为 `null`，不会当作 0。
