# macOS 插件接口

有两种可用入口：原 Scripted C# 资源包，以及面向本项目 `Zenith.Core` 编译的渲染模块 DLL。

## 原 Scripted 资源包

把文件夹、`.zip`、`.zrp`、`.7z`、`.rar` 或 `.tar` 放入 `Plugins/Assets/Scripted/Resources`，在 Scripted 的 Resources 页面刷新列表后选择。资源包必须包含 `script.cs`，多个同名文件时采用路径最短的一项；图片路径相对于该脚本所在目录。

脚本声明全局 `public class Script`，提供 `Load()` 和 `Render(IEnumerable<ZenithEngine.Note>, ScriptedEngine.RenderOptions)`。可选接口包括 `RenderInit(RenderOptions)`、`RenderDispose()`、`SettingsUI`、`UseProfiles`、`Description`、`Preview`、`NoteScreenTime`、`NoteCollectorOffset`、`ManualNoteDelete` 和 `LastNoteCount`。完整接口和重载见 `src/Zenith.Core/Scripted/IO.cs`、`ExtraUI.cs`、`EngineTypes.cs`。

`IO.LoadTexture` 和 `IO.LoadFont` 用于构造函数或 `Load()`；绘制通过 `IO.RenderQuad`、`IO.RenderShape`、`IO.RenderText` 及混合/着色器选择函数完成。原 OpenTK 3 数学命名空间会在编译阶段映射到 OpenTK 4。`Note` 对象及其 `meta` 在可见期间保持身份；启用 `ManualNoteDelete` 后，由脚本设置 `note.delete = true` 释放音符。

`.profiles.json` 使用 Windows 原版的按 UI 深度顺序排列的值数组。兼容层提供原版动态 UI 和数值变化事件类型；随包 SynX 和三个 Example 的设置代码已直接加载测试，任意第三方皮肤及依赖仍需验证。脚本以本机用户权限执行；原版命名空间检查不是安全沙箱。

## 编译模块示例

构建仓库内可运行的示例：

```sh
dotnet build examples/GradientPlugin -c Release
mkdir -p assets/windows/Zenith/Plugins/Mac
cp examples/GradientPlugin/bin/Release/net9.0/Zenith.GradientExample.dll assets/windows/Zenith/Plugins/Mac/
```

启动应用后，在 Modules 页面点击 Reload，选择 **Gradient Example**。开发目录的资源根是 `assets/windows/Zenith`；打包应用的资源根是 `Zenith.app/Contents/Resources/Zenith`。示例只使用宿主已经提供的依赖，不需要复制另一份 `Zenith.Core.dll`。

执行 `./tools/build-app.sh` 后，开发资源根 `Plugins/Mac` 内的模块 DLL 会复制到应用包的同一路径。这个目录只用于本项目 SDK 编译的跨平台模块；其他资源目录中的 DLL，以及所有目录中的 Windows EXE、BAT、CMD，均不随资源打包。示例模块不会默认安装，只有主动复制到 `Plugins/Mac` 后才会随构建发布。也可把模块手动放入现有应用包的资源根 `Plugins/Mac` 后重新签名。

模块实现 `ZenithEngine.IPluginRender`，包含一个公开的 `RenderSettings` 构造函数。`LanguageDictName` 用作唯一模块 ID，应避免占用内置 ID。`SettingsControl` 返回普通设置对象；未知模块的公共数字、布尔、字符串和枚举字段由宿主生成设置控件。

| 接口 | 调用约定 |
| --- | --- |
| 构造函数、元数据、设置 | UI 线程调用；此时没有 OpenGL context，不得创建 GPU 资源。 |
| `Init()` | 在专用 GPU 线程及已绑定的 macOS OpenGL 4.1 core context 中创建资源。 |
| `RenderFrame(notes, midiTime, finalCompositeBuff)` | 相同 GPU 线程调用；音符按开始时间排序，列表和音符跨帧保持。绑定传入 framebuffer 绘制。 |
| `NoteScreenTime` | 采样窗口长度；tick 模式单位为 tick，time 模式为毫秒。 |
| `NoteCollectorOffset` | 音符回收时刻的偏移量；负值可保留刚刚结束的音符。 |
| `ManualNoteDelete` | 为 `true` 时只回收 `note.delete` 标记的音符。 |
| `NoteColors`、`CurrentMidi`、`Tempo` | 宿主提供调色板引用、MIDI 信息和当前 BPM。 |
| `Dispose()` | 在拥有 context 的 GPU 线程释放资源；模块对象可在新 session 再次 `Init()`。 |

播放、导出或向后定位会开始新的渲染 session；资源释放必须允许重复调用。预览中的设置由 UI 线程修改，模块应避免在设置事件里调用 OpenGL。示例使用 `ScriptedGlRenderer`，也可以直接使用 OpenTK OpenGL 4.1 API；macOS core profile 不支持 `GL_QUADS` 和固定管线绘制。

原 Windows WPF 插件 DLL 需要移植源代码后重新编译。模块发现使用本机 `.NET 9` 程序集，不能直接运行 Windows WPF 控件、DirectX 或 Windows 原生 DLL。额外第三方依赖应随宿主明确部署；当前示例不演示自定义依赖隔离或热卸载。
