# 白龙动画宿主

WPF / .NET 8 宿主，使用 VPet-Simulator.Core 1.1.0.72 播放本项目白龙素材。旧版状态解析器通过独立的 `CodexStateBridge.exe` 接入，现有旧版窗口代码可继续独立维护。

构建需要 Windows、.NET 8 SDK 和系统 .NET Framework 4.x 编译器。先使用 `scripts/Pack-WhiteDragon.ps1 -OutputDirectory <新素材目录>` 导入原始图，再用 `scripts/Build-VPet.ps1 -Dotnet <dotnet.exe> -AssetDirectory <素材目录> -OutputDirectory <运行目录> -SelfContained -Test` 构建可独立运行的版本。

`CodexVPet.exe --demo --verify <新证据目录>` 使用隔离角色设置演示实际动作并导出 PNG 帧、截图及检查结果。`scripts/Test-VPetBridge.ps1` 在隔离会话目录验证工作、完成、桥接恢复与通知去重。普通运行不注入这些测试事件。

宿主支持 `CODEX_WHITE_DRAGON_DATA_DIR` 指定绝对数据目录，默认使用当前用户 LocalApplicationData 下的 CodexDragonPet。状态桥接只输出公开进展和完成事件。原动作图位于 `assets/white-dragon-v2/sheets`，运行时不会从网络下载图像。
