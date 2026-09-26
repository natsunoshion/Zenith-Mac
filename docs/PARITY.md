# Windows / macOS 一致性验收记录

记录日期：2026-09-26。当前实现可运行原 Scripted 皮肤和移植后的原渲染模块；**整体页面、全部交互、全部插件及逐帧画面的 1:1 验收尚未完成**。下面分别记录实际观察和自动检查的范围。

以下开头段落记录 0.1.1 包的历史验证；本轮变更见下方「0.1.2 更新检查」。
0.1.1 界面构建已重新打包并通过 `codesign --verify --deep --strict dist/Zenith.app`。
CUA 已实际打开最终包，确认 General 的绿色上下按钮、等粗白箭头、中缝、绿色下拉箭头和原生圆角外框；实机点击曾验证宽度 1920 → 1921 → 1920。
`artifacts/ui-refinement-qa/verification.md` 记录 General 与调色板默认/最小尺寸的布局和文字居中检查。
当前 0.1.1 包使用原仓库图标生成的 `Zenith-8155fd0117e6.icns`，`CFBundleIconFile` 指向该内容哈希文件名；启动时另由 `MacApplicationIcon` 显式设置 `NSApplication.applicationIconImage`。最终包已重新构建、签名并启动。原生图像解析与设置检查通过，但 Dock 本身的自动化截图曾超时，仍未将其列为已目视验收。
打包启动时发现主窗口错误地先读取开发目录资源，已移除重复路径查找并统一使用优先读取包内资源的 `AssetPaths.Root`；修复后实际启动检查约 2.3 秒返回主窗口。

## 对照环境与输入

| 项目 | 已确认状态 |
| --- | --- |
| 上游源码 | `arduano/Zenith-MIDI`，提交 `36f8ba3c06a6b26b9616f31a6d973d0d676e2747`。 |
| Windows 原版 | 用户 VMware 虚拟机内运行的 Zenith 2.1.5，已通过桌面操作直接查看和操作。 |
| Windows 本次预览输入 | Windows Downloads 中的 `銀の追憶.mid`。已 Start Preview，观察到音符、纹理键盘与光效。 |
| 已准备的同输入基准 | 同一个 `demo.mid` 已写入 Windows Downloads，名为 `Zenith-port-reference.mid`。Windows `Get-FileHash` 与本地 SHA-256 均为 `92d9e48bbc0109cc1b066c152f4c037e196d435876b97c014bc76a01154c4611`。 |
| macOS | Apple M3 Pro，macOS 15.7.4，原生 CGL OpenGL 4.1。 |
| macOS 自动导出输入 | 自检生成的 `tests/fixtures/demo.mid`：128 个音符、两个乐器轨道、tempo 变化及 Zenith 颜色元事件。 |
| 本轮真实 MIDI | 用户提供的 `40mP - からくりピエロ (instrumental)_quantized.mid`，SHA-256 `76fb32ef2d16508712147b16a495dc0d09fc18b5d35f147161316c2acc6196e9`；读取与渲染前后未变。用户已在 Windows VM 准备同一曲目与设置，本轮 CLI 样例使用皮肤默认值，尚未核对 VM 配置。 |
| 原 Scripted 资源 | `assets/windows/Zenith/Plugins/Assets/Scripted/Resources/Synthesia X.zrp` 及随包三个 Example 文件夹。 |

最初 Windows 与 macOS 成功预览/导出的输入不同。现已准备并核对哈希一致的参考 MIDI，开始进行同输入对照；尚未完成相同配置、相同时间的整帧验证。当前 Windows 观察保存在本次 CUA 会话中，没有保存为仓库内的基准截图。

## 已有验证证据

| 范围 | 实际检查 | 能支持的结论 |
| --- | --- | --- |
| Windows 页面与预览 | 依次查看 General、Modules、Scripted Resources、Synthesia X Settings；加载 MIDI 并预览。 | 原版在现有 Windows 环境可操作，已取得直接观察依据。发现的界面几何与颜色差异正在调整；未完成页面一致性验收。 |
| macOS 应用包 | `dist/Zenith.app/Contents/MacOS/Zenith.Mac` 为 arm64 Mach-O；包内存在 `libcoreclr.dylib`、原 `.zrp`、纹理与配置资源。此前最终合成和导出时序修正后重新打包，`codesign --verify --deep --strict dist/Zenith.app` 通过，并读取了主窗口辅助功能控件树。 | 该次包可启动。后续桌面操作遇到 ScreenCaptureKit `-3812` 错误，未完成该次 Load → 预览全程交互验收；此历史记录不能证明最新 UI/调色板改动已随包验收。 |
| 原版应用图标 | `artifacts/icon-review/verification.txt` 验证十种 ICNS 图像原生解码；`artifacts/icon-review/dock-diagnostic/verification.md` 记录此前包与运行进程的原生图标读取得到原图，以及无窗口、无 Dock 条目的隔离进程实际调用启动 setter 并读回图像。当前包为 0.1.1，使用哈希文件名，framework 初始化与 desktop startup 均设置应用图标。 | 图标来源、像素、包元数据和原生设置路径已检查。隔离读回的 alpha 全图不变，转换回 sRGB 后所采样绿色与原图相同；这不证明 Dock 当前显示画面已更新，尚无 Dock 目视验收结论。 |
| MIDI、播放状态与导出核心 | `dotnet run --project tests/Zenith.Core.SelfTest -- --ffmpeg --audio --stress`，38 项通过。 | 覆盖 SMF 0/1、running status、tempo/SMPTE、重叠音符、跨轨道踏板、Zenith 颜色事件、定位状态恢复、错误输入、实际编码、超过时长估计的动态尾帧及取消清理。 |
| macOS 音源 | 自检实际创建、启动并重置 Apple DLS AudioUnit graph。事件与 transport 另用记录输出验证。 | 原生音源可初始化，测试的 MIDI 状态转换正确。未证明其音色、延迟、音频波形与 Windows KDMAPI/系统音源相同。 |
| 百万音符解析 | 合成 1,000,000 个短音符的序列，解析后执行 1,000 次索引区间查询。该次结果为解析 0.771 秒、查询合计 80.6 毫秒。 | 验证该输入规模下的解析与索引工作正常；不等于百万音符同时可见、粒子全开或长时间导出的负载验证。 |
| Scripted 资源与配置 | `dotnet run --project tests/Zenith.Scripted.Tests -v:quiet` 最近已完成的运行报告 1,124 项断言通过（包含下列 GPU、最终合成与场景检查）。测试加载原脚本，检查 Synthesia X 源码哈希、88 张纹理、2 种字体、306 项可编辑设置、11 个现有配置；每个配置执行 30 帧并检查有限几何与纹理命令。 | 随包资源可编译、加载并执行这些路径，配置可读取及往返保存。不能推论所有参数组合、第三方脚本及其依赖均兼容。 |
| GPU 着色器 | `GpuParityChecks` 在 2×2 framebuffer 上检查三种纹理着色器、两种混合模式、RGB/alpha 与 UV 方向，像素值与源代码公式比较。 | 所测小图元的 macOS GPU 结果符合参考公式；此检查未捕获 Windows 原程序的整帧图像。 |
| 最终合成与 SSAA | `PostProcessingChecks` 的 299 项检查覆盖原 postShader 的 alpha/RGB 运算及混合、SSAA 采样偏移与 Linear/Repeat、背景方向及 maskColor，以独立 CPU 公式和实际 CGL 像素比较。 | 所测采样与合成规则已按源代码检查；没有把它当作 Windows 原程序整帧输出的替代证据。 |
| 十万音符场景列表 | 合成 100,000 音符的 SMF，使用 16×16 framebuffer 和不绘制图元的 probe 模块，检查 100 帧的音符顺序、可见范围、`Note.meta` 身份及手动删除。该次 100 帧为 134 毫秒，20 个暂停帧分配 25,600 字节。 | 验证场景音符收集及持久列表行为；这里的耗时不是原皮肤、粒子或纹理绘制的 GPU 吞吐测量。 |
| 渲染任务、故障释放与模块发现 | `dotnet run --project tests/Zenith.App.Tests -v:quiet` 已通过。worker 检查覆盖初始化失败、任务顺序、reset 失败、线程归属与释放；另有真实 CGL 下部分 Init、Init/Dispose 双失败、背景解码、framebuffer 构造失败及 worker 关闭检查。实际编译的 Gradient Example DLL 可被发现并暴露设置字段。 | 覆盖线程队列、所列 GPU 故障的资源释放和测试模块的发现路径；不是整套窗口操作或第三方 DLL 兼容性验收。 |
| 预览/导出前滚与时间推进 | App.Tests 的 34 项时序检查通过：初始 tempo 下的 tick 前滚、毫秒模式、Scripted 初始零缓存、暂停、静音负时间、跨 MIDI 零点、速度、源码 `fps / 4` 整数限步、导出延迟及音频偏移计算、20 帧预读与连续空帧结束条件。应用构建 0 warning / 0 error。 | 所测时序与上游代码规则一致。VSync 对应等待改用 Avalonia 下一次 compositor animation tick，与 realtime/fixed-frame 推进分离；尚未测量其与 Windows SwapBuffers 的实际显示延迟或每帧呈现时刻差异。 |
| 原 Flat 导出尾帧 | `dotnet run --project tests/Zenith.App.Tests -v:quiet -- --export` 实际运行 Flat → CGL → FFmpeg，输入为 0.5 秒音符及 1 秒 track end；输出 `flat-tail.mp4` 和 `flat-tail-mask.mp4`，ffprobe 均确认 349 帧。 | 验证 300 tick 前滚和连续 300 个空帧的结束规则，动态编码按实际结束条件收尾。不是与 Windows 原程序导出文件的逐帧比较。 |
| 渲染模块样例 | `artifacts/gpu/` 中有 Classic、Flat、PFA、MIDITrail、Textured、Note Counter 及 Synthesia X 的生成图像。CLI `gpu-test` 检查渲染不报错且输出非纯黑。 | 已有各模块运行样例；非纯黑检查不证明与 Windows 画面一致。 |
| macOS 页面 | `artifacts/ui/` 保存了主页、模块页、导出页和多个内置/Scripted 设置页截图。 | 可检查这些已捕获的 macOS 布局；不能作为尚未保存的 Windows 对应页面的像素差异报告。 |
| 真实视频导出 | `artifacts/validation/synthesia-x.mp4` 经 ffprobe 检查为 H.264、640×360、60 帧、2.000 秒，含 2.000 秒 AAC 音轨；对应 mask 为同规格 60 帧视频、无音轨。 | 原皮肤渲染、帧写入、编码、外部音频合并和单独遮罩输出已贯通。样例使用生成的正弦 WAV，并非 MIDI 音源录制。 |
| 用户真实 MIDI | `artifacts/validation/user-midi/`：1,991 音符、2 轨、960 PPQ、289.056 秒、323 tempo 事件、840 CC64。原 SynX 默认设置输出 245–249 秒的 640×360/30 fps 视频，完整解码 120 帧；另检查从零连续渲染到 2 秒的 PNG。 | 真实输入的解析和该段渲染通过，目视可见音符、纹理键盘和光效。无音频、无 Windows 同帧比较；片段从 245 秒新建脚本实例，未预热之前的粒子历史。一次 GL 纹理驱动警告保留于日志。 |
| 调色板与配置联动 | `artifacts/palette-ui-review/result.txt` 检查各模块独立选择/随机状态、用户切换只递增一次种子、Scripted 绑定旧值修复、模块往返、MIDITrail profile/defaults 选色与 Aura 同步、重复加载不增加处理器。 | 所测模型与 UI 联动通过；渲染路径已接入调色板热更新。此 UI 检查不调用 GPU，不能替代 Windows 颜色对照。 |
| 调色板编辑器 | `artifacts/palette-editor-qa/verification.txt`：独立生成 16×3/32×5 PNG，Pillow 逐字节核对 RGBA 往返、透明像素 RGB 保留及未编辑像素；真实编辑窗口保存第 3 行/第 6 通道/右色 `#12345601`，输出精确为 `(18,52,86,1)`。 | 新建/编辑、多行、渐变、RGBA/Hex、名称与覆盖检查通过；Scripted 右栏及内置模块均有入口，保存自动选中并热更新。验证未修改用户调色板文件或随机状态；详情见 `docs/PALETTE_EDITOR.md`。 |
| Scripted 配置存储 | App.Tests 的 9 项 `ScriptedProfileStorageChecks` 通过；`artifacts/validation/user-midi/profile-migration/` 实测包内 SynX 迁移后 12 份配置、306 个值逐项一致，包内源文件哈希未变。 | 内置/开发资源首次合并到用户目录，用户值优先；删除不会在重启后复活，外部皮肤保留相邻文件路径。12 份包括当前用户配置，不改变原包自带 11 份的计数。 |
| 控件对齐与外框修正 | `artifacts/alignment-ui-review/verification.txt` 与 PNG 记录数字文本居中、数值步进器高度和单层 hover；后续 `artifacts/ui-refinement-qa/verification.md` 补充原版绿色按钮、13×5 等粗白箭头、1 px 中缝、普通输入框上下居中及默认/最小尺寸布局检查；内部新增圆角已撤回。主窗口改用 `SystemDecorations=Full` 和扩展客户区 `NoChrome`。另通过 CUA 查看新版实机窗口截图。 | 客户区快照与几何检查通过；实机四个原生 NSWindow 外框圆角及数字居中已目视确认。该桌面截图仅在当前 CUA 会话中，未保存为仓库文件；不是 Windows 整页像素对照。 |
| 新版预览播放器 | `dotnet run --project tests/Zenith.Preview.Tests -v:quiet` 的 25 项原生 Avalonia/CGL 检查通过，覆盖暂停、声音、定位范围、渲染器重置、找回窗口、停止/重播、自然尾帧以及旧预览与导出的隔离。`artifacts/player-qa/` 与 `artifacts/preview-status-qa/` 另检查拖动提交/取消、快捷键、默认与最小尺寸和底部状态栏。 | 预览控件使用独立区域，主窗口状态移至底部；加载/导出进度不会叠在标题栏或预览画面上。该界面按用户要求采用独立的新样式，不追求 Windows 控件外观一致。 |
| 最新应用实机预览交互 | 0.1.1 应用重新打包、签名校验并启动；通过 CUA 在 289 秒 MIDI、原 Synthesia X 和已保存配置下操作暂停、点击时间轴跳到 2:24、静音/恢复、进入/退出全屏、重播和辅助功能时间轴增量。目视确认控件位于画面下方。 | 所列按钮和时间状态实际变化，重播保留负时间前滚；当前预览入口及播放器可用。原生文件面板自动操作曾发生粘贴超时和异常缩放，随后已成功加载该曲目；未将这一自动操作异常归因于 MIDI 类型筛选。没有据此宣称完整 Windows/macOS 一致。 |

导出抽帧分别为 `artifacts/validation/synthesia-x-frame.png` 与 `synthesia-x-mask-frame.png`。该默认 SynX 场景背景不透明，遮罩抽帧为全白；不能据此声称所有透明背景、粒子或皮肤配置的遮罩视觉效果均已对照原版。

## 0.1.2 更新检查

- 最终 `osx-arm64` 应用包已完成 Release publish，版本为 0.1.2，`codesign --verify --deep --strict dist/Zenith.app` 通过。构建保留 7 项既有原代码警告，无错误。六主题与本节修复已打入此包；根据用户要求交由用户直接试用，最终包未重复进行完整桌面交互验收。
- 背景开关不再停止并重启预览。图片与不透明度通过渲染线程更新；暂停时可仅重新合成已有前景，保留脚本和粒子状态。`App.Tests -- --background` 的 80 项检查覆盖 0/50/100% 不透明度、原 PNG alpha、SSAA、预览/导出/遮罩、资源切换和 GL 纹理绑定恢复。
- `Scripted.Tests -- --postprocessing` 的 299 项原合成检查通过，100% 背景继续使用原 shader 分支。
- `App.Tests -- --shadow --export` 的 159 项阴影检查通过：SSAA 1/2/4、四方向与 45° 偏移、Gaussian 模糊、半透明轮廓、边缘采样、开关后的原画面一致性、GL 资源释放、暂停重合成，以及 3 帧视频和遮罩编码后的像素检查。阴影取整个皮肤前景的 alpha；皮肤自带不透明背景需要关闭，General 背景不投影。产物位于 `artifacts/validation/shadow/`，使用说明见 [背景与阴影](BACKGROUND_EFFECTS.md)。
- 根据音符边缘反馈，Blur 改为 Gaussian 标准差：3 px 现在对应柔化强度 3 px，而非之前的 1 px。同时修复原 alpha 补偿上限被阴影改变的亮边：独立半透明样例的红通道曾由关闭时 180 增至开启时 185，现为 180→180；另覆盖高亮前景不被额外压暗。对缓存的真实 SynX 前景（4.017 秒，SSAA 1/2）检查，开启后无 RGB 增亮，黑背景下音符 RGB 逐字节不变；此项为同一 macOS 前景的开关对照，不是 Windows 整帧验收。记录位于 `artifacts/validation/shadow-edge-audit/`。SSAA 1 的原皮肤纹理/图元仍可能保留像素台阶。
- `Preview.Tests` 的 40 项检查通过，包含背景/阴影切换不改变 `IsBusy`、`IsPreviewing`、状态文字和播放器实例，暂停画面实时更新，导出中的背景/阴影设置不变，以及不透明度、阴影、主题和编码选项的保存恢复。暂停时切换主题不改变渲染版本、位置或播放器实例。
- 主界面按新的设计要求采用圆角控件和分区布局。首轮深灰绿主题之后，根据反馈加入六套可选主题，默认改为浅石灰＋鼠尾草绿；播放器保持独立深色。此前关于原版方角、25 px 步进器等记录属于旧主题，不再作为新界面的视觉要求。
- `artifacts/modern-main-qa/` 记录 900×600 默认与 780×550 最小窗口尺寸的页面、脚本设置和调色板布局；数值框修改背景不透明度和阴影模糊度可传回 State，硬件编码与原软件选项互斥。
- `artifacts/theme-qa/` 记录六主题的普通/禁用状态、调色板编辑器、浅色确认框、浅深主题菜单及最小尺寸页面。主题选择回传 State、JSON 保存恢复、局部预览主题隔离和调色板文件像素不变检查通过；来源与使用方式见 [界面主题](THEMES.md)。
- 编码流水线与 VideoToolbox 路径通过 44 项核心自检，包含复用缓冲、动态尾帧、视频与遮罩解码像素一致性、取消及实际硬件编码。短 1080p/60、SSAA 1、无背景/阴影的稀疏 Synthesia X 样例中，流水线使成对平均耗时降低约 17–18%；范围和复现命令见 [渲染性能](PERFORMANCE.md)。

## 0.1.3 背景取景与透明合成

- General 背景改为保持比例铺满画面，裁掉超出的部分。增加水平／垂直位置滑块和 Center image；默认 50% 居中，0% 左／上，100% 右／下。图片在某轴没有超出画幅时，该轴位置不改变画面。预览暂停时仅更新合成，导出冻结开始时的位置。
- 从实际 Synthesia X 预览与用户已完成的成片确认，Sparkle Glow 周边灰圈并非仅由 Drop shadow 引起。白色柔光透明度被累加及旧 sqrt 补偿高估，导致背景变暗。Scripted 现在保留原纹理、RGB Mix/Add 运算和粒子逻辑，改用 source-over 覆盖率与预乘颜色合成；SSAA 先平均颜色和覆盖率，阴影使用相同覆盖率。这是有意修正的渲染差异，不再声称 Scripted 透明边缘、mask 与旧 postshader 逐像素一致。内置渲染器保留原合成路径。
- Scripted 的半透明背景 PNG 同时使用原 PNG alpha 和 Opacity 计算颜色；相对旧 RGB 补偿会有变化。不透明背景仍按 Opacity 直接减淡。8 位 straight-color + mask 无法无损表示超过覆盖率的全部加法发光，现有文件格式仍有此限制。
- Start Render 等按钮采用独立 ControlTheme，悬停／按下改变背景并有 100 ms 过渡，避免 Fluent 子模板仅覆盖文字颜色。`artifacts/button-crop-qa/` 验证六主题实际鼠标事件下的普通／悬停／按下状态、文字颜色及 bounds 稳定，另检查键盘焦点、数字步进按钮、滑块拖动即时回传、居中复位和最小窗口布局。
- `App.Tests -- --premultiplied` 的 90 项 GPU 检查通过，覆盖单层／重叠白柔光、原 RGB 运算保留、Mix/Add 纹理、SSAA 1/2/4、阴影覆盖率及 mask 重建。159 项既有阴影检查（含 FFmpeg 色彩／遮罩编码）也通过。
- 固定一份 Synthesia X 的 188 条绘制命令，在新旧路径分别重放，三种 SSAA 下原始 RGB 均未改变。SSAA 1 样例的 8,514 个白色柔光像素中，比背景暗超过 2 级的像素由 3,757 降至 0；同帧关闭／开启阴影图均已目视检查。真实 keyHaze 纹理按运行时解码后，透明像素 RGB 全白，无透明黑 texel。证据位于 `artifacts/validation/synx-halo-audit/`；不将此局部检查视为所有皮肤及 Windows 整帧验收。
- `Preview.Tests` 的 46 项原生检查通过，新增背景位置默认／边界／保存恢复、暂停时上下移动后真实像素变化，以及不重启任务、不推进时间、不改导出快照的检查。

## 明确的差异与待完成检查

- **同输入画面对照**：仍需固定同一 MIDI、同一皮肤及配置、调色板、分辨率、时间与字体，保存 Windows 和 macOS 对应帧后比较。尚无整段视频的逐帧差异统计。
- **界面与交互**：原 WPF 页面与 Avalonia 页面仍在按实机观察调整；各分辨率、缩放比例、全部语言、输入范围、快捷键、错误提示和所有设置联动尚未逐项验收。
- **调色板资源**：可写目录已迁至 `~/Library/Application Support/Zenith-Mac/Palettes`；补充缺失的内置 PNG，四种程序生成的 Random 调色板按原逻辑重新生成，不写应用包。仍需在相同选色与随机状态下对照 Windows 渲染颜色。
- **字体**：原 Windows GDI 与 macOS Skia 的字体选择、测量和栅格化实现不同。当前文字可绘制不代表每种字体和字符集的度量一致。
- **二进制插件**：原 Windows WPF 插件 DLL 不能直接作为 macOS 模块加载。当前 `Plugins/Mac` 接口面向引用本项目 `Zenith.Core` 重新编译的模块；任意第三方 DLL、额外依赖、热卸载均未做通用兼容性验证。
- **Scripted 皮肤**：已验证随包 SynX 与三个 Example 的测试路径。`OpenTK` 数学命名空间映射、兼容类型和原脚本执行不构成任意 C# 皮肤可用的保证；RAR 等未列出实际样例的归档变体也不应标为已验证。
- **声音**：macOS 使用 Apple DLS 音源，Windows 实际声音还取决于原系统/KDMAPI 配置；实时调度和波形一致性没有测试结论。外部音频合成使用指定音频，不要求两端实时音源相同。
- **导出选项**：已验证上表的短视频、外部 WAV、遮罩和取消路径；所有 FFmpeg 自定义参数、编解码器、音频偏移/变速组合、超长视频和磁盘/编码器故障组合尚未穷举。
- **预览与导出的完整时序**：已按源码加入模块前滚、导出速度重置、原音频偏移计算、预读/空帧结束规则，并分开 VSync 与时间步长。全部生命周期操作、不同 MIDI 与皮肤下的实际呈现/导出时序仍需同输入原版对照，不能由单个样例帧数正确推断完整时序一致。
- **平台与规模**：仅有本机 Apple Silicon 的运行证据。Intel Mac、Info.plist 标注的最低系统版本及其他 macOS/GPU 组合均未实机验证。百万短音符解析结果不代表最大文件大小、同时可见音符数、音源复音数或渲染吞吐上限。

## 命令与路径核对

在仓库根目录执行开发命令；开发启动需要 .NET 9 SDK：

```sh
./tools/prepare-assets.sh
dotnet build Zenith.Mac.sln
dotnet run --project src/Zenith.Mac
./tools/build-app.sh
```

`prepare-assets.sh` 从根目录 `Zenith.7z` 解压资源，需要 `7z` 和 Python 3。`build-app.sh` 默认使用 `osx-arm64`，输出 `dist/Zenith.app`，内含运行时；FFmpeg 不随该脚本打包。传入 `osx-x64` 会使用同一个输出目录，因此会覆盖 arm64 包；当前记录不包含该架构的成功构建或运行结论。

开发资源根为 `assets/windows/Zenith`；应用包资源根为 `dist/Zenith.app/Contents/Resources/Zenith`。打包脚本排除 Windows 可执行文件和普通资源目录中的 DLL，只允许开发资源根 `Plugins/Mac` 下的 macOS SDK 模块 DLL 随资源打包。把自行移植的模块复制到该目录后，需重新运行 `./tools/build-app.sh` 才会更新应用包。

内置和开发资源皮肤的可写配置位于 `~/Library/Application Support/Zenith-Mac/ScriptedProfiles/`，按资源相对路径区分皮肤；首次合并包内配置后以用户文件为准。外部皮肤仍使用原相邻 `.profiles.json` 路径。

CLI 在源项目中独立运行，打包脚本只发布图形应用。当前 CLI 帮助提供 `inspect`、`frame`、`gpu-test`、`render`，并已核对实际帮助输出：

```sh
dotnet run --project src/Zenith.Cli -- --help
dotnet run --project tests/Zenith.Core.SelfTest -- --ffmpeg --audio --stress
dotnet run --project tests/Zenith.Scripted.Tests
dotnet run --project tests/Zenith.App.Tests
dotnet run --project tests/Zenith.App.Tests -- --export
dotnet run --project src/Zenith.Cli -- gpu-test tests/fixtures/demo.mid artifacts/gpu
```

上述 core 自检会创建 `tests/fixtures/demo.mid`。重现真实皮肤短视频导出：

```sh
dotnet run --project src/Zenith.Cli -- render \
  tests/fixtures/demo.mid artifacts/validation/synthesia-x.mp4 \
  'assets/windows/Zenith/Plugins/Assets/Scripted/Resources/Synthesia X.zrp' \
  --width 640 --height 360 --fps 30 --duration 2 \
  --audio 'artifacts/validation/source audio.wav' \
  --mask artifacts/validation/synthesia-x-mask.mp4 --preset ultrafast
```

该命令使用当前已存在的 `source audio.wav`；纯视频验证可省略 `--audio` 及其路径。CLI 默认通过 PATH 查找 FFmpeg，也可传入 `--ffmpeg /path/to/ffmpeg`。图形应用额外查找包内可执行目录、`/opt/homebrew/bin/ffmpeg` 与 `/usr/local/bin/ffmpeg`。
