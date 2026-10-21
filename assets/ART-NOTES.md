# 形象与生成记录

## 0.4.0 分层动画

`assets/layered/` 包含原待机形象、身体补片、静止袖手、活动左右袖手、闭眼和茶杯纹理。原脸部区域与原图逐字节一致；原图完全透明像素中的 RGB 被清零，不改变可见像素。补绘的袖手、茶杯、闭眼与被遮挡衣服由内置 ImageGen 生成，参考本项目原待机形象。待机和喝茶由 `src/LayeredDragonRig.cs` 连续计算肩、肘、腕轨迹和贴图网格，主程序与预览共用同一实现。

重新注册素材可运行 `samples/LayeredPet/pack_assets.py`，需要维护者自行准备 Python Pillow；普通运行包不需要 Python。源图在 `samples/LayeredPet/source/`，运行素材与注册说明在 `assets/layered/`。旧图集中的喝茶与眨眼格保留作为历史素材，0.4.0 的这两个动作不再按完整角色帧播放。

## 0.3.0-local.1 动作扩展

`dragon-actions.png` 是新增运行图集，1024×1536、RGBA、四列六行，每格 256×256。依次为八帧喝茶、四帧眨眼、四帧伸展与哈欠、两帧拿起与两帧落地、四帧结束庆祝。素材由内置 ImageGen 以原六姿态图集为形象参考生成，并进行一次间距与透明背景修订；完整提示词和原始生成图保存在本次本地任务记录中。`scripts/Pack-ActionAtlas.py` 负责按已检查的透明间隙切帧、统一缩放与脚底位置、打包到准确格网，保留帧内 RGBA。

检查透明 PNG 时应分别合成到深、浅背景。直接显示透明像素内未参与合成的 RGB 会出现灰底、白色或紫色杂点，不能据此认定实际桌面窗口存在这些像素。

托盘图标 `tray-icon.png` 同样由内置 ImageGen 生成，参考本项目的小龙形象，提示词为：single compact white-haired violet-eyed dragon-girl head icon, ivory horns, bold plum outline, large violet eyes, simple high-contrast shapes, fully transparent background, readable at 16 px and 32 px, no text or generic information symbol。`scripts/Export-TrayIcon.ps1` 只负责将原图等比缩放并封装为包含 16/20/24/32/48/64 像素的 `tray.ico`，运行时只加载小图标，不加载大头像。

工具：内置 `image_gen.imagegen`；生成与清理均要求 `transparent_background: true`。最终运行素材为 `dragon-sprites-clean.png`，1536 × 1024、RGBA，三列两行。窗口只裁取对应格显示，保留原始图像与透明通道。

参考：[用户提供的 B 站视频](https://www.bilibili.com/video/BV1ikaZ6rECj/)。借鉴银白长发、紫瞳、龙角、精灵耳、白色中式服装、淡紫色龙翼与尾巴等角色特征，重新生成六个姿态。

六格依次为待机、抱电脑工作、思考、挥手庆祝、抱沙漏等待、抱低电量图标。

## 初次生成的主要规格

Production sprite sheet for a Windows desktop pet. A beautiful feminine GPT-inspired dragon companion, based on the silver-haired white dragon girl in the user's video reference. One transparent-background landscape sheet, 3 columns by 2 rows, six equally sized square cells. Same full-body 3-head-tall chibi woman in each cell: long silver-white hair, purple-violet eyes, two small ivory segmented dragon horns, pointed ears, white rose hair ornament with pale violet tassel, miniature white qipao-inspired dress, pale violet waist bow, white shoes, lavender wings and curled white tail. Fine crisp anime line art and polished cel shading. Equal scale and padding, no text, no logos. Poses: idle smile; working with laptop; thinking with hand at chin; joyful completion wave; patiently holding hourglass; worried holding a nearly empty battery.

## 最终清理提示词（原文）

Edit this sprite sheet to produce clean production-ready desktop-pet sprites with a truly transparent background. Keep all six characters EXACTLY the same identity, poses, expressions, white qipao costumes, silver hair, purple eyes, dragon horns wings and tails. Preserve the exact 1536 x 1024 canvas and 3-column 2-row arrangement, exact sprite placement and size. The existing file has a gray/lavender semi-transparent background haze and noisy pale/magenta pixel halos around the character silhouettes. REMOVE EVERY TRACE OF THE BACKGROUND AND EDGE NOISE. The blank padding around and between the six characters must be fully clear alpha = 0 with NO gradient, NO checkerboard drawn into the image, NO glow, NO cast shadows, NO floating dots, NO residual colored fragments. Silhouettes should have crisp clean anti-aliased edges suitable for overlay on white or dark desktops. Keep the character artwork fully opaque, including pale white hair and dress, only preserving natural soft transparency in fine outer hair edges. Do not add anything, no labels, no text, no changes to character design or framing.
