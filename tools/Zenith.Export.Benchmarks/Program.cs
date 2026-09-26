using System.Diagnostics;
using System.Text.Json;
using Zenith.Core.Export;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using Zenith.Core.Scripted;
using ZenithEngine;

// Short, sequential native-GPU runs. No application windows or user settings.
if (args.Length > 0 && args[0] is "--help" or "help")
{
    Console.WriteLine("Usage: dotnet run -c Release --project tools/Zenith.Export.Benchmarks -- [midi] [output-folder] [frames=120] [repeats=2] [pipeline=false|true|both] [modes=render-only,x264-medium,x264-ultrafast,videotoolbox-20Mbps]");
    return;
}
string midiPath = args.Length > 0 ? args[0] : "tests/fixtures/demo.mid";
string output = args.Length > 1 ? args[1] : "artifacts/performance/export-baseline";
int frames = args.Length > 2 ? int.Parse(args[2]) : 120;
int repeats = args.Length > 3 ? int.Parse(args[3]) : 2;
bool[] pipelines = args.Length > 4 && args[4] == "both" ? [false, true] : [args.Length > 4 && bool.Parse(args[4])];
if (frames <= 0 || repeats <= 0) throw new ArgumentException("Frames and repeats must be positive.");
string[] modes = args.Length > 5 ? args[5].Split(',') : ["render-only", "x264-medium", "x264-ultrafast", "videotoolbox-20Mbps"];
if (modes.Any(mode => mode is not ("render-only" or "x264-medium" or "x264-ultrafast" or "videotoolbox-20Mbps")))
    throw new ArgumentException("Unknown benchmark mode; run with --help for the supported names.");
const int width = 1920, height = 1080, fps = 60;
Directory.CreateDirectory(output);
var midi = MidiSequence.Load(midiPath);
var results = new List<object>();
var packPath = AssetPaths.Resolve("Plugins/Assets/Scripted/Resources/Synthesia X.zrp");
foreach (var mode in modes)
for (int repeat = 0; repeat < repeats; repeat++)
foreach (bool pipeline in pipelines)
{
    GC.Collect(); GC.WaitForPendingFinalizers();
    var setup = Stopwatch.StartNew();
    using var pack = ScriptedPack.Load(packPath);
    var settings = new RenderSettings { width = width, height = height, fps = fps, downscale = 1, ffRender = true };
    using var scene = new SceneRenderer(midi, settings, null, pack);
    // Preserve sequential state; warm up with preroll, then render t=0 onward.
    for (int i = -10; i < 0; i++) scene.Render(i / (double)fps);
    setup.Stop();
    double renderMs = 0, callbackGapMs = 0;
    long previousReturn = 0, lastReturn = 0;
    var frameTimes = new List<double>(frames);
    var gcBytes = GC.GetTotalAllocatedBytes(true);
    var watch = Stopwatch.StartNew();
    ReadOnlyMemory<byte> Render(int index)
    {
        long begin = Stopwatch.GetTimestamp();
        if (previousReturn != 0) callbackGapMs += Stopwatch.GetElapsedTime(previousReturn, begin).TotalMilliseconds;
        var pixels = scene.Render(index / (double)fps);
        lastReturn = Stopwatch.GetTimestamp();
        double ms = Stopwatch.GetElapsedTime(begin, lastReturn).TotalMilliseconds;
        renderMs += ms; frameTimes.Add(ms); previousReturn = lastReturn;
        return pixels;
    }
    long encoded = 0;
    string? video = null;
    if (mode == "render-only")
        for (int i = 0; i < frames; i++) Render(i);
    else
    {
        bool hardware = mode.StartsWith("videotoolbox");
        video = Path.Combine(output, $"{mode}-{repeat + 1}-pipeline{pipeline}.mp4");
        var options = new VideoExportOptions
        {
            OutputPath = video, Width = width, Height = height, FramesPerSecond = fps,
            DurationSeconds = frames / (double)fps, VideoCodec = hardware ? "h264_videotoolbox" : "libx264",
            Crf = 17, Preset = mode == "x264-ultrafast" ? "ultrafast" : "medium",
            BitrateKbps = hardware ? 20_000 : null,
            PipelineEncoding = pipeline
        };
        var export = await FfmpegExporter.ExportAsync(options,
            (frame, _) => ValueTask.FromResult(Render(checked((int)frame.Index))));
        encoded = export.FrameCount;
    }
    watch.Stop();
    double lastWriteAndDrain = Stopwatch.GetElapsedTime(lastReturn).TotalMilliseconds;
    var allocated = GC.GetTotalAllocatedBytes(true) - gcBytes;
    frameTimes.Sort();
    var result = new
    {
        Mode = mode, Repeat = repeat + 1, PipelineEncoding = pipeline, Width = width, Height = height, Fps = fps, Frames = frames,
        SetupMs = setup.Elapsed.TotalMilliseconds, TotalMs = watch.Elapsed.TotalMilliseconds,
        EffectiveFps = frames / watch.Elapsed.TotalSeconds,
        RenderIncludingReadbackMs = renderMs, MeanRenderMs = renderMs / frames,
        P50RenderMs = frameTimes[frames / 2], P95RenderMs = frameTimes[Math.Min(frames - 1, (int)(frames * .95))],
        BetweenCallbacksMs = callbackGapMs, LastWriteAndDrainMs = lastWriteAndDrain,
        ManagedAllocatedBytes = allocated, EncodedFrames = encoded,
        OutputBytes = video == null ? 0 : new FileInfo(video).Length,
        Device = scene.Device
    };
    results.Add(result);
    Console.WriteLine(JsonSerializer.Serialize(result));
    File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
}
