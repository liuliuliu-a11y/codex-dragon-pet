# 形象与生成记录

托盘图标 `tray-icon.png` 同样由内置 ImageGen 生成，参考本项目的小龙形象，提示词为：single compact white-haired violet-eyed dragon-girl head icon, ivory horns, bold plum outline, large violet eyes, simple high-contrast shapes, fully transparent background, readable at 16 px and 32 px, no text or generic information symbol。`scripts/Export-TrayIcon.ps1` 只负责将原图等比缩放并封装为包含 16/20/24/32/48/64 像素的 `tray.ico`，运行时只加载小图标，不加载大头像。

工具：内置 `image_gen.imagegen`；生成与清理均要求 `transparent_background: true`。最终运行素材为 `dragon-sprites-clean.png`，1536 × 1024、RGBA，三列两行。窗口只裁取对应格显示，保留原始图像与透明通道。

参考：[用户提供的 B 站视频](https://www.bilibili.com/video/BV1ikaZ6rECj/)。借鉴银白长发、紫瞳、龙角、精灵耳、白色中式服装、淡紫色龙翼与尾巴等角色特征，重新生成六个姿态。

六格依次为待机、抱电脑工作、思考、挥手庆祝、抱沙漏等待、抱低电量图标。

## 初次生成的主要规格

Production sprite sheet for a Windows desktop pet. A beautiful feminine GPT-inspired dragon companion, based on the silver-haired white dragon girl in the user's video reference. One transparent-background landscape sheet, 3 columns by 2 rows, six equally sized square cells. Same full-body 3-head-tall chibi woman in each cell: long silver-white hair, purple-violet eyes, two small ivory segmented dragon horns, pointed ears, white rose hair ornament with pale violet tassel, miniature white qipao-inspired dress, pale violet waist bow, white shoes, lavender wings and curled white tail. Fine crisp anime line art and polished cel shading. Equal scale and padding, no text, no logos. Poses: idle smile; working with laptop; thinking with hand at chin; joyful completion wave; patiently holding hourglass; worried holding a nearly empty battery.

## 最终清理提示词（原文）

Edit this sprite sheet to produce clean production-ready desktop-pet sprites with a truly transparent background. Keep all six characters EXACTLY the same identity, poses, expressions, white qipao costumes, silver hair, purple eyes, dragon horns wings and tails. Preserve the exact 1536 x 1024 canvas and 3-column 2-row arrangement, exact sprite placement and size. The existing file has a gray/lavender semi-transparent background haze and noisy pale/magenta pixel halos around the character silhouettes. REMOVE EVERY TRACE OF THE BACKGROUND AND EDGE NOISE. The blank padding around and between the six characters must be fully clear alpha = 0 with NO gradient, NO checkerboard drawn into the image, NO glow, NO cast shadows, NO floating dots, NO residual colored fragments. Silhouettes should have crisp clean anti-aliased edges suitable for overlay on white or dark desktops. Keep the character artwork fully opaque, including pale white hair and dress, only preserving natural soft transparency in fine outer hair edges. Do not add anything, no labels, no text, no changes to character design or framing.
