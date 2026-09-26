using System.Diagnostics;
using Zenith.Core.Export;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using Zenith.Mac;
using ZenithEngine;

internal static class RendererExportChecks
{
    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsMacOS()) return;
        byte[] track = [0, 0x90, 60, 100, 0x83, 0x60, 0x80, 60, 0, 0x83, 0x60, 0xFF, 0x2F, 0];
        using var bytes = new MemoryStream();
        bytes.Write("MThd"u8); bytes.Write([0, 0, 0, 6, 0, 0, 0, 1, 1, 224]);
        bytes.Write("MTrk"u8); bytes.Write([0, 0, 0, (byte)track.Length]); bytes.Write(track); bytes.Position = 0;
        var midi = MidiSequence.Load(bytes);
        var settings = new RenderSettings { width = 64, height = 36, fps = 60, ffRender = true, ffRenderMask = true };
        var plugin = new FlatRender.Render(settings);
        var start = PreviewTiming.ExportStart(midi.TempoMap, plugin.NoteScreenTime, false, 60, 0);
        var completion = new RenderCompletion(midi, false, 60);
        string folder = Path.GetFullPath("artifacts/validation");
        Directory.CreateDirectory(folder);
        using var worker = new RenderWorker(() => new SceneRenderer(midi, settings, plugin));
        var result = await FfmpegExporter.ExportAsync(new()
        {
            OutputPath = Path.Combine(folder, "flat-tail.mp4"),
            MaskOutputPath = Path.Combine(folder, "flat-tail-mask.mp4"),
            Width = 64, Height = 36, FramesPerSecond = 60, StartSeconds = start.StartSeconds,
            DurationSeconds = midi.DurationSeconds - start.StartSeconds + 5,
            StopAfterFrame = _ => completion.Complete, Preset = "ultrafast"
        }, async (frame, token) => await worker.Run(scene =>
        {
            double screen = scene.NoteScreenTime;
            var pixels = scene.Render(frame.TimelineSeconds);
            completion.Observe(frame.TimelineSeconds, screen, scene.LastNoteCount);
            return (ReadOnlyMemory<byte>)pixels;
        }));
        // 300 ticks of initial screen time = .3125 s. The note's physical end
        // is .5 s. Frame 49 is the first empty frame; 300 empty frames end at
        // frame index 348, so the actual output must contain 349 frames.
        if (result.FrameCount != 349) throw new Exception($"Original Flat tail expected 349 frames, got {result.FrameCount}.");
        foreach (string path in new[] { result.OutputPath, result.MaskOutputPath! })
        {
            using var probe = Process.Start(new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true,
                ArgumentList = { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=nb_frames", "-of", "csv=p=0", path } })!;
            string frames = await probe.StandardOutput.ReadToEndAsync(); await probe.WaitForExitAsync();
            if (probe.ExitCode != 0 || frames.Trim() != "349") throw new Exception("Encoded renderer tail frame count differs from raw output.");
        }
        Console.WriteLine("PASS native Flat renderer → FFmpeg: original preroll and 300 empty tail frames, video and mask both exactly 349 frames");
    }
}
