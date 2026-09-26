# Zenith for macOS

基于 [arduano/Zenith-MIDI](https://github.com/arduano/Zenith-MIDI) 的 macOS 源码移植，复用原渲染算法并执行原 Scripted 皮肤。上游通过 Git 子模块固定在 `36f8ba3c06a6b26b9616f31a6d973d0d676e2747`。

**首次克隆后需要准备资源并构建。** Git 仓库不包含原 Windows 发布包 `Zenith.7z`、解压资源 `assets/windows/Zenith`、构建后的 `dist/Zenith.app`，也不包含 `artifacts/` 中的截图、视频和验证产物。

## 从源码构建

需要 macOS、.NET 9 SDK，以及资源准备所用的 `7z` 和 Python 3。视频导出另需安装 macOS 版 FFmpeg。

```sh
git clone --recurse-submodules https://github.com/natsunoshion/Zenith-Mac.git
cd Zenith-Mac
```

如果已经普通克隆，先执行 `git submodule update --init --recursive`。将原 Windows 发布包 `Zenith.7z` 放到仓库根目录，然后执行：

```sh
./tools/prepare-assets.sh          # 解压资源并准备模块预览图
dotnet build Zenith.Mac.sln
dotnet run --project src/Zenith.Mac
```

资源准备完成后，也可用 `run.command` 启动开发版本。生成可双击打开的应用包：

```sh
./tools/build-app.sh               # Apple Silicon：dist/Zenith.app
./tools/build-app.sh osx-x64        # Intel 目标，尚未验证；覆盖同一个输出目录
```

生成的 `.app` 包含 .NET 运行时，运行时无需另外安装 SDK。更新源码后需要重新打包。FFmpeg 不随应用包打包；程序会查找应用可执行目录、`/opt/homebrew/bin/ffmpeg`、`/usr/local/bin/ffmpeg` 和 PATH。原发布包中的 `ffmpeg.exe` 不能用于 macOS 导出。

## 打开应用

完成上述打包后，双击生成的 `dist/Zenith.app`。

1. **General → Load MIDI** 选择 `.mid` 文件。
2. **Modules → Scripted** 选择脚本模块。
3. **Module Settings → Resources** 选择 **Synthesia X.zrp**；**Settings** 页显示脚本自身生成的设置和原配置列表。
4. **Start Preview** 打开预览；底部独立播放器栏支持拖动进度、播放/暂停、重播、静音和全屏。主窗口底部的 **Show preview** 可找回预览窗口；可继续实时修改模块设置。快捷键见 [播放控制](docs/PLAYBACK.md)。
5. **Render** 设置文件名、音频、遮罩和编码参数，点击 **Start Render**。

## 实现范围

- 复用原 Classic / Flat / PFA / MIDITrail / Textured / Note Counter 渲染算法和着色器；在 macOS 原生 CGL OpenGL 4.1 上运行。
- 保留 General / Modules / Module Settings / Render 四页结构和多语言字典，采用圆角控件和分区布局。General 的 **Theme** 可切换六套浅色／深色主题并保存选择，默认浅石灰＋鼠尾草绿；主题仅改变界面，保留 MIDI、皮肤和导出画面的颜色。内置模块设置由原 XAML 定义转换，脚本设置仍由皮肤生成。方案与来源见 [界面主题](docs/THEMES.md)。
- 背景图片支持 0–100% 不透明度；预览中调整开关、图片和不透明度会直接更新背景，保留播放位置、暂停状态和粒子状态。100% 保持原有合成结果，50% 可在黑底上减淡背景；导出使用开始时的背景设置。
- General 提供 **Drop shadow**：调整黑色投影的模糊度、方向、距离和不透明度，预览可实时修改，导出遮罩包含柔和阴影。它基于整个皮肤前景的透明轮廓；关闭皮肤自带的不透明背景即可显露音符投影。使用方法见 [背景与阴影](docs/BACKGROUND_EFFECTS.md)。
- 原 `.zrp` AES/ZIP 解码、C# 运行时编译、纹理、文字、四顶点颜色/UV、三种纹理着色器、两种混合模式、粒子、可变 Note 元数据、生命周期和动态 UI。
- 已验证的原 `Synthesia X.zrp` 加载 88 张纹理、2 种字体、306 项设置、11 份原配置。也支持原 Example Flat / Textured / Particles；这些资源需从原发布包准备。
- 调色板选择、随机开关和种子按模块独立保存于运行状态，支持预览热更新；配置与默认值恢复会同步选色和 MIDITrail Aura。调色板位于 `~/Library/Application Support/Zenith-Mac/Palettes`，内置 PNG 仅补充缺失文件，不向应用包写入。内置 Scripted 配置首次合并到同级 `ScriptedProfiles` 用户目录；外部皮肤仍使用相邻 `.profiles.json`。
- Scripted 右栏及内置模块提供 **New Palette / Edit Palette**：可编辑 16 通道、多行颜色、左右渐变、RGBA 和 Hex，保存后自动选中并热更新，保留原 Random 行为。说明与验证见 [调色板编辑器](docs/PALETTE_EDITOR.md)。
- MIDI format 0/1、running status、tempo map、SMPTE、重叠音符、跨轨道延音/选择性延音踏板、颜色事件、按时序定位与控制器状态恢复，以及 Apple AudioUnit 实时音源。
- FFmpeg 离线定帧导出、SSAA、背景图、软件 H.264 CRF/码率、Apple 硬件 H.264 码率、自定义参数、外部音频合并、独立 alpha 遮罩、进度与取消。硬件编码是独立选项，不将软件 CRF 值换算成硬件质量参数。
- macOS 插件发现机制：将面向 `Zenith.Core` 编译、实现 `ZenithEngine.IPluginRender` 的程序集放到资源目录的 `Plugins/Mac`。

导出会将编码写入与下一帧渲染重叠，脚本仍按帧顺序执行。Apple 硬件模式需要当前 FFmpeg 与 Mac 支持 VideoToolbox；无法使用时会显示错误，可手动选择软件模式。实测数据、复现方法与进一步优化方向见 [渲染性能](docs/PERFORMANCE.md)。

## 与“完全一样”的验收边界

这是实际执行原脚本和原渲染算法的源码移植。**目前不能宣称已经通过 Windows/macOS 整体逐像素、所有行为的 1:1 验收。**

- WPF 与 Avalonia、Windows GDI 与 macOS Skia 的字体栅格化和部分控件度量有差异。
- 主设置页与预览播放器采用统一的新样式，并保留 macOS 原生窗口外框；界面外观按用户要求独立设计。应用图标来自原仓库绿色双箭头。已有默认与最小尺寸的布局、文字居中及部分交互检查，仍未覆盖所有系统缩放和交互状态。
- 原 Windows WPF 插件 DLL 无法直接在 macOS 加载；它们需要源代码移植并重新编译。已测试的 Synthesia X 和三个 Example 皮肤按原脚本加载，在编译时映射 OpenTK 3 数学命名空间至 OpenTK 4；这不表示任意第三方脚本及依赖均兼容。
- MIDI 播放使用 Apple DLS 音源，Windows KDMAPI/系统音源的音色取决于原系统配置；使用相同外部音频进行视频合成可保留该音频。
- 已参照 Windows 原版 Zenith 2.1.5 检查页面和预览。尚未完成相同 MIDI、相同配置和相同时间的逐帧对照，亦未验证所有第三方插件或极端规模黑 MIDI。
- 当前验证环境为 Apple Silicon M3 Pro / macOS 15.7.4。打包脚本接受 `osx-x64` 参数，但尚无 Intel 构建及实机运行验证结果。

具体证据、测试覆盖范围和待完成项见 [一致性验收记录](docs/PARITY.md)。

## 测试与命令行

完成资源准备后，在 macOS 上运行以下检查。带 `--ffmpeg` 或 `--export` 的检查需要 FFmpeg：

```sh
dotnet run --project tests/Zenith.Core.SelfTest -- --ffmpeg --audio --stress
# 本机支持 VideoToolbox 时，另加 --videotoolbox 检查硬件编码
dotnet run --project tests/Zenith.Scripted.Tests
dotnet run --project tests/Zenith.App.Tests
dotnet run --project tests/Zenith.App.Tests -- --export  # 增加实际 Flat 尾帧/遮罩编码检查
dotnet run --project tests/Zenith.App.Tests -- --background
dotnet run --project tests/Zenith.App.Tests -- --shadow --export
dotnet run --project tests/Zenith.Preview.Tests -v:quiet # macOS 原生预览交互与生命周期
dotnet run --project src/Zenith.Cli -- gpu-test tests/fixtures/demo.mid artifacts/gpu
```

测试和 CLI 会在本地生成检查产物。文档中引用的 `artifacts/` 路径是开发验证记录，历史截图、音频和视频不随 Git 提供。

已有真实 Synthesia X → CGL → FFmpeg 导出验证，覆盖短视频、外部音频和遮罩。另以一份 1,991 音符、289.056 秒的 MIDI 验证默认皮肤：4 秒片段输出 640×360、30 fps、120 帧无音频视频并完整解码通过；该结果不构成 Windows 同配置、同帧对照。

MIDI/音频/导出自检在启用 `--ffmpeg --audio --stress --videotoolbox` 时共 44 项通过，包含 100 万音符的合成文件解析、1,000 次区间查询、动态尾帧、流水线帧顺序/像素一致性及实际硬件编码。该压力样例为短音符序列，不代表已经验证百万音符同时可见的渲染负载。另有 34 项预览/导出时序检查及实际 Flat 渲染的 349 帧视频/遮罩验证。自检与完整导出命令见 [核心测试说明](tests/Zenith.Core.SelfTest/README.md)。

命令行支持 `inspect`、`frame`、`gpu-test` 和 `render`。运行不带参数的 CLI 查看完整语法：

```sh
dotnet run --project src/Zenith.Cli --
```

源码目录：`src/Zenith.Core` 为解析/脚本/渲染/导出，`src/Zenith.Mac` 为窗口与控制器，`src/Zenith.Cli` 为自动渲染与诊断入口。`upstream` 为只读参考子模块；`tools/port_originals.py` 可生成初始机械转换结果，正式源码另包含平台适配修复。请勿提交个人 MIDI、音频、配置或生成的视频。原作者与依赖版权见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)，许可证见 [LICENSE](LICENSE)。
