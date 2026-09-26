# macOS renderer plugins

`ExamplePlugin` is a complete .NET plugin loaded through the same module lifecycle as the
six ported original renderers. It delegates actual drawing to the original Flat renderer.

```sh
dotnet build examples/ExamplePlugin -c Release
mkdir -p assets/windows/Zenith/Plugins/Mac
cp examples/ExamplePlugin/bin/Release/net9.0/Zenith.ExamplePlugin.dll assets/windows/Zenith/Plugins/Mac/
```

Click **Modules → Reload** to discover it. For the packaged application the corresponding
resource directory is `dist/Zenith.app/Contents/Resources/Zenith/Plugins/Mac`.
Run `./tools/build-app.sh` after installing a module to include it in the application
bundle. Only DLLs under `Plugins/Mac` are copied; the extracted Windows plugin DLLs
remain excluded. No example module is installed by default.

The host calls the public `Render(RenderSettings)` constructor, assigns `NoteColors`,
`Tempo` and `CurrentMidi`, then calls `Init`, `RenderFrame`, and `Dispose` on the GPU thread.
Bind `finalCompositeBuff` as the target framebuffer before drawing. Return a settings
object with public fields for the generated settings editor, or an Avalonia `Control`
for a custom settings page. Native GL calls are provided by OpenTK 4.9.4; use
`OpenTK.Mathematics` for vectors and colors. Apple supports a core OpenGL 4.1 context;
legacy WPF controls, GDI+, GL_QUADS and Win32 functions require porting.

Windows plugin DLLs bind to a different assembly ABI and WPF. The original DLLs are
retained in the extracted archive, but must be rebuilt from source for this host.

Scripted `.zrp` skins use a separate compatibility runtime. Existing skin code executes
through the compatibility API; the supplied skins have been tested without source edits
apart from the compile-time OpenTK namespace mapping. Arbitrary third-party skins and
their dependencies still require validation. Use the original three examples in
`Plugins/Assets/Scripted/Resources` as the script API reference. The provided Synthesia X
skin is executed directly from its original archive.
