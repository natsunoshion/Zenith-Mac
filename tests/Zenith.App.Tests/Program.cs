using Zenith.Mac;
using Zenith.Core.Rendering;
using ZenithEngine;

if (args.Contains("--shadow"))
{
    ShadowChecks.Run(export: args.Contains("--export"));
    return;
}

if (args.Contains("--background"))
{
    BackgroundChecks.Run();
    return;
}

if (args.Contains("--palette-arguments"))
{
    ScenePaletteArgumentChecks.Run();
    return;
}

// Exercise the real worker queue without creating an application window or GL
// context. Null factories are intentional failure fixtures for lifecycle paths.
Thread? failedThread = null;
try
{
    _ = new RenderWorker(() =>
    {
        failedThread = Thread.CurrentThread;
        throw new InvalidDataException("Factory failed");
    });
    throw new Exception("Constructor should report factory failure.");
}
catch (InvalidDataException) { }
Check(failedThread is { IsAlive: false }, "Failed construction joins the worker thread");

Thread? workerThread = null;
var worker = new RenderWorker(() => { workerThread = Thread.CurrentThread; return null!; });
Check(workerThread != Thread.CurrentThread, "Factory executes on its dedicated thread");
var order = new List<int>();
var resetTasks = Enumerable.Range(0, 100).Select(index => worker.Reset(() =>
{
    Check(Thread.CurrentThread == workerThread, "Reset stays on the owning thread");
    order.Add(index);
    return null!;
})).ToArray();
await Task.WhenAll(resetTasks);
Check(order.SequenceEqual(Enumerable.Range(0, 100)), "Queued resets retain their submission order");
try { await worker.Reset(() => throw new InvalidDataException("Reset failed")); }
catch (InvalidDataException) { }
try
{
    await worker.Run(_ => 1);
    throw new Exception("Run should report the unavailable renderer after failed reset.");
}
catch (InvalidOperationException) { }
worker.Dispose();
worker.Dispose();
Check(workerThread is { IsAlive: false }, "Dispose joins the thread and is safe to repeat");
try
{
    await worker.Run(_ => 1);
    throw new Exception("Run after disposal should fail.");
}
catch (ObjectDisposedException) { }
Console.WriteLine("PASS RenderWorker initialization failure, queue ordering, reset failure, disposal, and thread affinity");

var pluginDirectory = Path.Combine(Path.GetTempPath(), "zenith-plugin-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(pluginDirectory);
try
{
    File.Copy(Path.Combine(AppContext.BaseDirectory, "Zenith.GradientExample.dll"), Path.Combine(pluginDirectory, "Zenith.GradientExample.dll"));
    using var plugin = ModuleCatalog.Discover(pluginDirectory, new RenderSettings()).Single();
    Check(plugin.LanguageDictName == "gradient-example" && !plugin.Initialized,
        "External source plugin is discovered before GPU initialization");
    Check(plugin.SettingsControl.GetType().GetField("NoteScreenTime") != null,
        "External plugin exposes live settings to the generic UI");
    Console.WriteLine("PASS compiled Gradient Example plugin discovery and settings API");
}
finally { Directory.Delete(pluginDirectory, recursive: true); }

ResourceCleanupChecks.Run();
BackgroundChecks.Run();
ShadowChecks.Run();
PreviewTimingChecks.Run();
PaletteChecks.Run(args.SkipWhile(a => a != "--palette-midi").Skip(1).FirstOrDefault());
ScenePaletteArgumentChecks.Run();
ScriptedProfileStorageChecks.Run();
if (args.Contains("--export")) await RendererExportChecks.RunAsync();

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
