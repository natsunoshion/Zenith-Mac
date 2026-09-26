using System.Diagnostics;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace Zenith.Core.Export;

public sealed class VideoExportOptions
{
    public required string OutputPath { get; init; }
    public string? MaskOutputPath { get; init; }
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public double FramesPerSecond { get; init; } = 60;
    public double DurationSeconds { get; init; }
    public double StartSeconds { get; init; }
    public double PlaybackSpeed { get; init; } = 1;
    /// <summary>Optional completion condition evaluated after rendering each frame, before rendering the next. DurationSeconds becomes a progress estimate.</summary>
    public Func<ExportFrame, bool>? StopAfterFrame { get; init; }
    public int? BitrateKbps { get; init; }
    public int Crf { get; init; } = 17;
    public string Preset { get; init; } = "medium";
    public string VideoCodec { get; init; } = "libx264";
    /// <summary>Overlap one encoder write with the following render. A private frame copy supports callbacks that reuse their buffer.</summary>
    public bool PipelineEncoding { get; init; } = true;
    public bool FlipVertically { get; init; }
    public string? AudioPath { get; init; }
    public double AudioOffsetSeconds { get; init; }
    public double AudioTrimSeconds { get; init; }
    public string FfmpegPath { get; init; } = "ffmpeg";
    /// <summary>Additional ffmpeg output options as individual arguments, with no shell parsing.</summary>
    public IReadOnlyList<string> AdditionalArguments { get; init; } = [];
}

public readonly record struct ExportFrame(long Index, long TotalFrames, double OutputSeconds, double TimelineSeconds, int Width, int Height);
public readonly record struct ExportProgress(long CompletedFrames, long TotalFrames, double Fraction, TimeSpan Elapsed, TimeSpan? Remaining, bool IsEstimate = false);
public readonly record struct ExportResult(string OutputPath, string? MaskOutputPath, long FrameCount, TimeSpan Elapsed);

/// <summary>Deterministic offline export from tightly packed BGRA frames. ffmpeg must be installed or bundled.</summary>
public static class FfmpegExporter
{
    /// <summary>Splits the original custom-ffmpeg options field without invoking a shell.</summary>
    public static IReadOnlyList<string> ParseArguments(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var result = new List<string>();
        var value = new StringBuilder();
        char quote = '\0';
        bool started = false;
        for (int i = 0; i < arguments.Length; i++)
        {
            char c = arguments[i];
            if (quote == '\0' && char.IsWhiteSpace(c))
            {
                if (started) { result.Add(value.ToString()); value.Clear(); started = false; }
                continue;
            }
            started = true;
            if (c == '\\' && quote != '\'' && i + 1 < arguments.Length)
            {
                char next = arguments[i + 1];
                if (next == '\\' || next == '"' || (quote == '\0' && (next == '\'' || char.IsWhiteSpace(next))))
                { value.Append(next); i++; continue; }
            }
            if (c is '\'' or '"')
            {
                if (quote == '\0') { quote = c; continue; }
                if (quote == c) { quote = '\0'; continue; }
            }
            value.Append(c);
        }
        if (quote != '\0') throw new FormatException("Unclosed quotation mark in custom ffmpeg options.");
        if (started) result.Add(value.ToString());
        return result;
    }

    public static async Task<ExportResult> ExportAsync(VideoExportOptions options,
        Func<ExportFrame, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> renderFrame,
        IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Validate(options);
        ArgumentNullException.ThrowIfNull(renderFrame);
        cancellationToken.ThrowIfCancellationRequested();
        string output = Path.GetFullPath(options.OutputPath);
        string? mask = string.IsNullOrWhiteSpace(options.MaskOutputPath) ? null : Path.GetFullPath(options.MaskOutputPath);
        if (mask == output) throw new ArgumentException("Video and mask need different output paths.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (mask != null) Directory.CreateDirectory(Path.GetDirectoryName(mask)!);
        string stagedOutput = StagingPath(output), stagedMask = mask == null ? "" : StagingPath(mask);
        var watch = Stopwatch.StartNew();
        Encoder? videoEncoder = null, maskEncoder = null;
        long frameCount = checked((long)Math.Ceiling(options.DurationSeconds * options.FramesPerSecond));
        long completedFrames = 0;
        int frameBytes = checked(options.Width * options.Height * 4);
        byte[]? maskBytes = mask == null ? null : new byte[frameBytes];
        byte[]? encodingBytes = options.PipelineEncoding ? ArrayPool<byte>.Shared.Rent(frameBytes) : null;
        Task? pendingWrite = null;
        try
        {
            videoEncoder = Encoder.Start(options, stagedOutput, withAudio: true);
            if (mask != null) maskEncoder = Encoder.Start(options, stagedMask, withAudio: false);
            async Task WriteFrame(ReadOnlyMemory<byte> pixels)
            {
                if (maskEncoder == null)
                    await videoEncoder.WriteAsync(pixels, cancellationToken).ConfigureAwait(false);
                else
                {
                    CreateAlphaMask(pixels.Span, maskBytes!);
                    await Task.WhenAll(videoEncoder.WriteAsync(pixels, cancellationToken),
                        maskEncoder.WriteAsync(maskBytes!, cancellationToken)).ConfigureAwait(false);
                }
            }
            double lastReport = -1;
            for (long i = 0; options.StopAfterFrame != null || i < frameCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double outputSeconds = i / options.FramesPerSecond;
                var frame = new ExportFrame(i, frameCount, outputSeconds,
                    options.StartSeconds + outputSeconds * options.PlaybackSpeed, options.Width, options.Height);
                var pixels = await renderFrame(frame, cancellationToken).ConfigureAwait(false);
                if (pixels.Length != frameBytes)
                    throw new InvalidDataException($"Frame {i} has {pixels.Length} bytes; expected {frameBytes} BGRA bytes.");
                // Completion may observe state mutated by the callback. Capture
                // it before advancing the stateful renderer to another frame.
                bool complete = options.StopAfterFrame?.Invoke(frame) ?? i + 1 == frameCount;
                if (encodingBytes == null)
                    await WriteFrame(pixels).ConfigureAwait(false);
                else
                {
                    if (pendingWrite != null) await pendingWrite.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    pixels.CopyTo(encodingBytes);
                    pendingWrite = WriteFrame(encodingBytes.AsMemory(0, frameBytes));
                    if (complete) await pendingWrite.ConfigureAwait(false);
                }
                completedFrames = i + 1;
                if (watch.Elapsed.TotalSeconds - lastReport >= .1 || complete)
                {
                    double fraction = complete ? 1 : Math.Min(.99, (double)completedFrames / frameCount);
                    progress?.Report(new(completedFrames, complete ? completedFrames : frameCount, fraction, watch.Elapsed,
                        options.StopAfterFrame != null && !complete ? null : TimeSpan.FromSeconds(watch.Elapsed.TotalSeconds * (1 - fraction) / fraction),
                        options.StopAfterFrame != null && !complete));
                    lastReport = watch.Elapsed.TotalSeconds;
                }
                if (complete) break;
            }
            await videoEncoder.CompleteAsync(cancellationToken).ConfigureAwait(false);
            if (maskEncoder != null) await maskEncoder.CompleteAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(stagedOutput, output, overwrite: true);
            if (mask != null) File.Move(stagedMask, mask, overwrite: true);
            return new(output, mask, completedFrames, watch.Elapsed);
        }
        finally
        {
            videoEncoder?.Dispose(); maskEncoder?.Dispose();
            // A cancelled/failed pipe must release its memory before it returns
            // to the shared pool. Dispose above also closes a faulted encoder.
            if (pendingWrite != null)
                try { await pendingWrite.ConfigureAwait(false); } catch { }
            if (encodingBytes != null) ArrayPool<byte>.Shared.Return(encodingBytes);
            TryDelete(stagedOutput);
            if (mask != null) TryDelete(stagedMask);
        }
    }

    static void Validate(VideoExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.OutputPath)) throw new ArgumentException("An output file is required.");
        if (options.Width <= 0 || options.Height <= 0 || options.Width > 32768 || options.Height > 32768)
            throw new ArgumentException("Invalid video dimensions.");
        if ((options.Width & 1) != 0 || (options.Height & 1) != 0)
            throw new ArgumentException("YUV 4:2:0 video width and height must be even.");
        if (!double.IsFinite(options.FramesPerSecond) || options.FramesPerSecond <= 0 || options.FramesPerSecond > 1000)
            throw new ArgumentException("Invalid video frame rate.");
        if (!double.IsFinite(options.DurationSeconds) || options.DurationSeconds <= 0)
            throw new ArgumentException("Export duration must be positive.");
        if (!double.IsFinite(options.StartSeconds) || !double.IsFinite(options.PlaybackSpeed) || options.PlaybackSpeed <= 0)
            throw new ArgumentException("Invalid timeline settings.");
        if (options.Crf is < 0 or > 51 || options.BitrateKbps is <= 0) throw new ArgumentException("Invalid encoding quality.");
        if (options.VideoCodec == "h264_videotoolbox" && options.BitrateKbps == null)
            throw new ArgumentException("Apple H.264 hardware encoding requires an explicit bitrate; CRF applies to software encoding.");
        if (!double.IsFinite(options.AudioOffsetSeconds) || !double.IsFinite(options.AudioTrimSeconds) || options.AudioTrimSeconds < 0)
            throw new ArgumentException("Invalid audio timing.");
        if (!string.IsNullOrWhiteSpace(options.AudioPath))
        {
            if (!File.Exists(options.AudioPath)) throw new FileNotFoundException("Audio file was not found.", options.AudioPath);
            if (Path.GetFullPath(options.AudioPath) == Path.GetFullPath(options.OutputPath))
                throw new ArgumentException("Video output must not overwrite its audio input.");
            if (!string.IsNullOrWhiteSpace(options.MaskOutputPath) && Path.GetFullPath(options.AudioPath) == Path.GetFullPath(options.MaskOutputPath))
                throw new ArgumentException("Mask output must not overwrite its audio input.");
        }
        _ = checked(options.Width * options.Height * 4);
    }

    static void CreateAlphaMask(ReadOnlySpan<byte> pixels, Span<byte> mask)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            mask[i] = mask[i + 1] = mask[i + 2] = pixels[i + 3]; mask[i + 3] = 255;
        }
    }
    static string StagingPath(string path) => Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileNameWithoutExtension(path)}.zenith-{Guid.NewGuid():N}{Path.GetExtension(path)}");
    static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    sealed class Encoder : IDisposable
    {
        readonly Process process;
        readonly Task readErrors;
        readonly StringBuilder errorLog = new();
        readonly bool hardwareH264;
        bool complete;

        Encoder(Process process, bool hardwareH264)
        {
            this.process = process;
            this.hardwareH264 = hardwareH264;
            readErrors = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                    lock (errorLog)
                    {
                        errorLog.AppendLine(line);
                        if (errorLog.Length > 32768) errorLog.Remove(0, errorLog.Length - 16384);
                    }
            });
        }

        public static Encoder Start(VideoExportOptions options, string path, bool withAudio)
        {
            var start = new ProcessStartInfo(options.FfmpegPath)
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            void Add(params string[] args) { foreach (string arg in args) start.ArgumentList.Add(arg); }
            Add("-hide_banner", "-loglevel", "warning", "-nostats", "-y", "-f", "rawvideo", "-pixel_format", "bgra",
                "-video_size", $"{options.Width}x{options.Height}", "-framerate", Number(options.FramesPerSecond), "-i", "pipe:0");
            bool audio = withAudio && !string.IsNullOrWhiteSpace(options.AudioPath);
            if (audio)
            {
                if (options.AudioTrimSeconds > 0) Add("-ss", Number(options.AudioTrimSeconds));
                if (options.AudioOffsetSeconds != 0) Add("-itsoffset", Number(options.AudioOffsetSeconds));
                Add("-i", Path.GetFullPath(options.AudioPath!));
            }
            Add("-map", "0:v:0");
            if (audio) Add("-map", "1:a:0", "-c:a", "aac"); else Add("-an");
            if (options.FlipVertically) Add("-vf", "vflip");
            Add("-c:v", options.VideoCodec, "-pix_fmt", "yuv420p");
            // An explicitly requested hardware mode must not silently fall back.
            if (options.VideoCodec == "h264_videotoolbox") Add("-allow_sw", "0");
            if (options.BitrateKbps is { } bitrate) Add("-b:v", $"{bitrate}k", "-maxrate", $"{bitrate}k", "-bufsize", $"{bitrate * 2L}k");
            else if (options.VideoCodec is "libx264" or "libx265") Add("-crf", options.Crf.ToString(CultureInfo.InvariantCulture), "-preset", options.Preset);
            if (audio && options.PlaybackSpeed != 1)
            {
                double speed = options.PlaybackSpeed;
                var filters = new List<string>();
                while (speed > 2) { filters.Add("atempo=2"); speed /= 2; }
                while (speed < .5) { filters.Add("atempo=0.5"); speed *= 2; }
                filters.Add($"atempo={Number(speed)}");
                Add("-af", string.Join(',', filters));
            }
            // The original app closes its raw-video pipe when the renderer's
            // tail finishes. A duration cap would truncate retained particles.
            if (options.StopAfterFrame == null) Add("-t", Number(options.DurationSeconds));
            foreach (string arg in options.AdditionalArguments) start.ArgumentList.Add(arg);
            Add(path);
            try
            {
                var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start.");
                return new(process, options.VideoCodec == "h264_videotoolbox");
            }
            catch (System.ComponentModel.Win32Exception ex)
            { throw new InvalidOperationException($"Unable to start ffmpeg at '{options.FfmpegPath}'. Install ffmpeg or select its executable.", ex); }
        }

        public async Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
        {
            try { await process.StandardInput.BaseStream.WriteAsync(frame, token).ConfigureAwait(false); }
            catch (IOException ex)
            {
                if (process.HasExited) await readErrors.ConfigureAwait(false);
                throw new InvalidOperationException($"ffmpeg stopped while receiving video. {ErrorText()}", ex);
            }
        }

        public async Task CompleteAsync(CancellationToken token)
        {
            process.StandardInput.Close();
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            await readErrors.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}. {ErrorText()}");
            complete = true;
        }

        string ErrorText()
        {
            lock (errorLog) return (hardwareH264
                ? "Apple H.264 hardware encoding failed. The encoder may be unavailable or busy; select software H.264 to use the CPU encoder. FFmpeg details:\n"
                : "") + errorLog;
        }

        public void Dispose()
        {
            if (!complete)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                try { process.StandardInput.Close(); } catch (IOException) { }
            }
            process.Dispose();
        }
    }
}
