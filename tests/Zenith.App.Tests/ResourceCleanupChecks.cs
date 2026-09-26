using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using Zenith.Mac;
using ZenithEngine;

internal static class ResourceCleanupChecks
{
    [DllImport("/System/Library/Frameworks/OpenGL.framework/OpenGL")]
    private static extern IntPtr CGLGetCurrentContext();

    internal static void Run()
    {
        if (!OperatingSystem.IsMacOS()) return;
        byte[] bytes = [77, 84, 104, 100, 0, 0, 0, 6, 0, 0, 0, 1, 1, 224,
            77, 84, 114, 107, 0, 0, 0, 4, 0, 255, 47, 0];
        var midi = MidiSequence.Load(new MemoryStream(bytes));
        foreach (var disposalAlsoFails in new[] { false, true })
        {
            var plugin = new FailingPlugin { ThrowOnInit = true, ThrowOnDispose = disposalAlsoFails };
            try
            {
                _ = new SceneRenderer(midi, Settings(), plugin);
                throw new Exception("Scene construction should fail.");
            }
            catch (InvalidDataException error)
            {
                Check(error.Message == "Plugin initialization failed", "Constructor preserves its original failure");
                Check(error.Data.Contains("SceneCleanupFailure") == disposalAlsoFails,
                    "Secondary cleanup failure is retained without hiding the constructor error");
            }
            Check(plugin.DisposeCalls == 1 && plugin.DisposedWithCurrentContext,
                "A partially initialized plugin is disposed with its own context current");
            Check(CGLGetCurrentContext() == IntPtr.Zero, "Failed Scene constructor unbinds and releases its context");
        }

        var failingDispose = new FailingPlugin { ThrowOnDispose = true };
        var scene = new SceneRenderer(midi, Settings(), failingDispose);
        try { scene.Dispose(); throw new Exception("Plugin Dispose should be reported."); }
        catch (InvalidOperationException error) { Check(error.Message == "Plugin disposal failed", "Dispose preserves the plugin exception"); }
        Check(CGLGetCurrentContext() == IntPtr.Zero && NativeHandlesReleased(scene),
            "Scene releases its CGL and framework handles after plugin disposal throws");
        scene.Dispose();
        Check(failingDispose.DisposeCalls == 1, "Scene disposal is idempotent after a cleanup failure");
        try { scene.Render(0); throw new Exception("Disposed scene should reject rendering."); }
        catch (ObjectDisposedException) { }

        var corruptBackground = Path.Combine(Path.GetTempPath(), "zenith-bad-background-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(corruptBackground, "not an image");
        try
        {
            var plugin = new FailingPlugin();
            var settings = Settings(); settings.BGImage = corruptBackground;
            try { _ = new SceneRenderer(midi, settings, plugin); throw new Exception("Invalid background should fail."); }
            catch (InvalidDataException) { }
            Check(plugin.DisposeCalls == 1 && CGLGetCurrentContext() == IntPtr.Zero,
                "Background decode failure rolls back the initialized plugin and native resources");
        }
        finally { File.Delete(corruptBackground); }

        var invalidSize = Settings(); invalidSize.width = 0;
        try { _ = new SceneRenderer(midi, invalidSize); throw new Exception("Invalid framebuffer should fail."); }
        catch (ArgumentOutOfRangeException) { }
        Check(CGLGetCurrentContext() == IntPtr.Zero, "Failure before the framebuffer exists still releases the scene context");

        using (var context = new NativeContext())
        {
            GL.GetInteger(GetPName.MaxTextureSize, out var maximum);
            try { _ = new RenderTarget(maximum + 1, 1); throw new Exception("An oversized texture should fail."); }
            catch (InvalidOperationException) { }
            GL.GetInteger(GetPName.FramebufferBinding, out var framebuffer);
            GL.GetInteger(GetPName.TextureBinding2D, out var texture);
            GL.GetInteger(GetPName.RenderbufferBinding, out var depth);
            Check(framebuffer == 0 && texture == 0 && depth == 0,
                "Partially allocated framebuffer, texture, and depth buffer are all deleted");
            while (GL.GetError() != ErrorCode.NoError) { } // The intentional oversized allocation sets GL_INVALID_VALUE.
        }

        SceneRenderer? workerScene = null;
        var workerPlugin = new FailingPlugin { ThrowOnDispose = true };
        var worker = new RenderWorker(() => workerScene = new SceneRenderer(midi, Settings(), workerPlugin));
        try { worker.Dispose(); throw new Exception("Worker should propagate plugin Dispose failure."); }
        catch (InvalidOperationException) { }
        Check(workerPlugin.OwningThread is { IsAlive: false } && NativeHandlesReleased(workerScene!),
            "A disposal failure releases GPU resources before the RenderWorker thread exits");
        worker.Dispose();
        Console.WriteLine("PASS native CGL cleanup: partial Init, double failure, bad background, invalid/partial framebuffer, Dispose failure, worker shutdown");
    }

    private static RenderSettings Settings() => new() { width = 16, height = 16 };
    private static bool NativeHandlesReleased(SceneRenderer scene)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var context = typeof(SceneRenderer).GetField("context", flags)!.GetValue(scene)!;
        return (IntPtr)typeof(NativeContext).GetField("context", flags)!.GetValue(context)! == IntPtr.Zero
            && (IntPtr)typeof(NativeContext).GetField("library", flags)!.GetValue(context)! == IntPtr.Zero;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class FailingPlugin : IPluginRender
    {
        private IntPtr context;
        private int buffer;
        public bool ThrowOnInit { get; init; }
        public bool ThrowOnDispose { get; init; }
        public int DisposeCalls { get; private set; }
        public bool DisposedWithCurrentContext { get; private set; }
        public Thread? OwningThread { get; private set; }
        public string Name => "Cleanup failure probe";
        public string Description => Name;
        public bool Initialized { get; private set; }
        public object PreviewImage => null!;
        public bool ManualNoteDelete => false;
        public double NoteCollectorOffset => 0;
        public NoteColor[][] NoteColors { private get; set; } = [];
        public double Tempo { private get; set; }
        public MidiInfo CurrentMidi { private get; set; } = new();
        public string LanguageDictName => "cleanup-test";
        public double NoteScreenTime => 1920;
        public long LastNoteCount => 0;
        public object SettingsControl => this;
        public void Init()
        {
            OwningThread = Thread.CurrentThread;
            context = CGLGetCurrentContext();
            buffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            GL.BufferData(BufferTarget.ArrayBuffer, 16, IntPtr.Zero, BufferUsageHint.StaticDraw);
            if (ThrowOnInit) throw new InvalidDataException("Plugin initialization failed");
            Initialized = true;
        }
        public void ReloadTrackColors() { }
        public void RenderFrame(FastList<Note> notes, double midiTime, int finalCompositeBuff) { }
        public void Dispose()
        {
            DisposeCalls++;
            DisposedWithCurrentContext = context != IntPtr.Zero && CGLGetCurrentContext() == context;
            if (buffer != 0) GL.DeleteBuffer(buffer);
            buffer = 0;
            Initialized = false;
            if (ThrowOnDispose) throw new InvalidOperationException("Plugin disposal failed");
        }
    }
}
