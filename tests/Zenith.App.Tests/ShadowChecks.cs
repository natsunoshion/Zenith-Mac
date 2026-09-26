using System.Reflection;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using Zenith.Core.Scripted;
using Zenith.Core.Export;
using ZenithEngine;

internal static class ShadowChecks
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(bool export = false)
    {
        if (!OperatingSystem.IsMacOS()) return;
        int count = 0;
        void Check(bool success, string message)
        {
            if (!success) throw new Exception(message);
            count++;
        }
        int At(int x, int y, int channel = 3) => (y * 24 + x) * 4 + channel;
        var folder = Path.Combine(Path.GetTempPath(), "zenith-shadow-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string white = Path.Combine(folder, "white.png");
            SceneRenderer.SavePng(white, [255, 255, 255, 255], 1, 1);
            using (var context = new NativeContext())
            foreach (int factor in new[] { 1, 2, 4 })
            {
                using var source = new RenderTarget(24 * factor, 24 * factor);
                void Shape(int left = 7, int top = 7, int width = 3, int height = 3, byte alpha = 255, byte red = 160)
                {
                    byte[] pixels = new byte[source.Width * source.Height * 4];
                    for (int y = top * factor; y < (top + height) * factor; y++)
                    for (int x = left * factor; x < (left + width) * factor; x++)
                    {
                        int i = ((source.Height - 1 - y) * source.Width + x) * 4;
                        pixels[i] = red; pixels[i + 1] = 100; pixels[i + 2] = 60; pixels[i + 3] = alpha;
                    }
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, source.Texture);
                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, source.Width, source.Height,
                        PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                }
                Shape();
                using var post = new ScenePostProcessor(source.Width, source.Height, factor, white);
                var original = post.Render(source.Texture);
                Check(typeof(ScenePostProcessor).GetField("shadowRenderer", Fields)!.GetValue(post) == null,
                    "Disabled shadow must not allocate targets or shaders");
                post.SetShadow(new(true, 3, 45, 18, 0));
                Check(post.Render(source.Texture).SequenceEqual(original)
                    && typeof(ScenePostProcessor).GetField("shadowRenderer", Fields)!.GetValue(post) == null,
                    "Zero opacity must preserve exact original output without GPU shadow resources");
                foreach (var (angle, x, y, oppositeX, oppositeY) in new[]
                {
                    (0, 14, 8, 2, 8), (90, 8, 14, 8, 2), (180, 2, 8, 14, 8), (270, 8, 2, 8, 14)
                })
                {
                    post.SetShadow(new(true, 0, angle, 6, .5));
                    var shaded = post.Render(source.Texture);
                    Check(Math.Abs(shaded[At(x, y, 2)] - 128) <= 1 && shaded[At(x, y)] == 255,
                        $"SSAA {factor}, {angle} degrees: 50% shadow must move exactly six output pixels and darken white by half");
                    Check(shaded[At(oppositeX, oppositeY, 2)] == 255,
                        "Shadow must not appear in the opposite direction");
                    Check(shaded.AsSpan(At(8, 8, 0), 4).SequenceEqual(original.AsSpan(At(8, 8, 0), 4)),
                        "Opaque foreground must cover its shadow without changing its color");
                    post.SetBackground(null, 1);
                    var mask = post.Render(source.Texture, alphaMask: true);
                    Check(Math.Abs(mask[At(x, y)] - 128) <= 1 && mask[At(x, y, 2)] == 0,
                        "Separate mask must include 50% black shadow, without squaring its opacity");
                    post.SetBackground(white, 1);
                }
                post.SetShadow(ForegroundShadowOptions.Default);
                Check(post.Render(source.Texture).SequenceEqual(original), "Disabling an allocated shadow must restore byte-identical original pixels");

                // Half-transparent geometry uses the coverage of the existing
                // compositor: sqrt(A) without SSAA, averaged A with SSAA.
                Shape(alpha: 64);
                post.SetBackground(null, 1);
                post.SetShadow(new(true, 0, 0, 6, .5));
                byte[] alphaShadow = post.Render(source.Texture, alphaMask: true);
                double expectedCoverage = factor == 1 ? Math.Sqrt(64 / 255d) : 64 / 255d;
                Check(Math.Abs(alphaShadow[At(14, 8)] - expectedCoverage * .5 * 255) <= 2,
                    "Shadow coverage must follow the original foreground alpha/SSAA path");

                Shape();
                post.SetShadow(new(true, 0, 45, 12, 1));
                var diagonal = post.Render(source.Texture, alphaMask: true);
                double mass = 0, centerX = 0, centerY = 0;
                for (int y = 12; y < 24; y++)
                for (int x = 12; x < 24; x++)
                {
                    double a = diagonal[At(x, y)];
                    mass += a; centerX += x * a; centerY += y * a;
                }
                double phase = (factor - 1) / (2d * factor);
                Check(Math.Abs(centerX / mass - (8 - phase + 12 / Math.Sqrt(2))) < .08
                    && Math.Abs(centerY / mass - (8 + phase + 12 / Math.Sqrt(2))) < .08,
                    $"SSAA {factor}: a fractional diagonal offset must preserve the expected coverage centroid in output pixels");
                if (factor == 1)
                {
                    post.SetShadow(new(true, 3, 0, 0, .7));
                    var blurred = post.Render(source.Texture, alphaMask: true);
                    // Independent Gaussian convolution: Blur 3 means sigma 3,
                    // truncated at 9px. The three source columns have distances
                    // 2/3/4 from this point, and the rows have distances 1/0/1.
                    double Weight(int distance) => Math.Exp(-distance * distance / 18d);
                    double z = 1 + 2 * Enumerable.Range(1, 9).Sum(Weight);
                    double expected = (Weight(2) + Weight(3) + Weight(4)) / z
                        * (1 + 2 * Weight(1)) / z * .7 * 255;
                    Check(Math.Abs(blurred[At(11, 8)] - expected) <= 2 && blurred[At(11, 8)] > 0,
                        "Gaussian blur must spread a soft edge with the expected normalized strength");
                    Check(Math.Abs(blurred[At(11, 8)] - blurred[At(5, 8)]) <= 1
                        && Math.Abs(blurred[At(8, 11)] - blurred[At(8, 5)]) <= 1,
                        "Zero-distance blur must be symmetric on both axes");
                    Shape(left: 0);
                    post.SetShadow(new(true, 3, 0, 8, .7));
                    var border = post.Render(source.Texture, alphaMask: true);
                    Check(border[At(6, 8)] > 0 && border[At(23, 8)] == 0,
                        "A touching-edge note must retain its shifted soft edge without wrapping to the opposite edge");
                    post.SetShadow(new(true, 3, 180, 8, .7));
                    var outside = post.Render(source.Texture, alphaMask: true);
                    Check(outside[At(6, 8)] == 0 && outside[At(23, 8)] == 0,
                        "A shadow shifted completely outside the frame must not become a clamped black stripe");
                }
                Shape(alpha: 64, red: 128);
                post.SetBackground(white, .5);
                post.SetShadow(ForegroundShadowOptions.Default);
                var legacyEdge = post.Render(source.Texture);
                post.SetShadow(new(true, 0, 0, 0, .2));
                var legacyShadow = post.Render(source.Texture);
                Check(legacyShadow[At(8, 8, 2)] <= legacyEdge[At(8, 8, 2)],
                    "Black shadow must never brighten squared-alpha legacy edges against a 50% background");
                if (factor == 1)
                    Check(Math.Abs(legacyEdge[At(8, 8, 2)] - 180) <= 2
                        && Math.Abs(legacyShadow[At(8, 8, 2)] - legacyEdge[At(8, 8, 2)]) <= 1,
                        "Legacy clamp counterexample must retain its cap rather than change red from 180 to 185");
                byte[] twoHalves = new byte[source.Width * source.Height * 4];
                for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                {
                    int i = (y * source.Width + x) * 4;
                    twoHalves[i] = x < 12 * factor ? (byte)255 : (byte)181;
                    twoHalves[i + 3] = x < 12 * factor ? (byte)255 : (byte)128;
                }
                GL.BindTexture(TextureTarget.Texture2D, source.Texture);
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, source.Width, source.Height,
                    PixelFormat.Rgba, PixelType.UnsignedByte, twoHalves);
                post.SetBackground(null, 1); post.SetShadow(ForegroundShadowOptions.Default);
                var pureBright = post.Render(source.Texture);
                post.SetBackground(white, .5);
                var brightOnBackground = post.Render(source.Texture);
                foreach (double strength in new[] { .7, 1 })
                {
                    post.SetShadow(new(true, 0, 0, 8, strength));
                    var brightShadow = post.Render(source.Texture);
                    Check(brightShadow[At(16, 8, 2)] >= pureBright[At(16, 8, 2)] - 1
                        && brightShadow[At(16, 8, 2)] <= brightOnBackground[At(16, 8, 2)] + 1,
                        "A 70/100% cast shadow must stay between the pure bright foreground and its original background composite");
                    if (strength == 1)
                        Check(Math.Abs(brightShadow[At(16, 8, 2)] - pureBright[At(16, 8, 2)]) <= 1,
                            "A fully black shadow removes all background light without subtracting the foreground");
                }
                foreach (bool linearAlpha in new[] { false, true })
                {
                    byte[] ramp = new byte[source.Width * source.Height * 4];
                    for (int y = 0; y < source.Height; y++)
                    for (int x = 0; x < source.Width; x++)
                    {
                        int i = (y * source.Width + x) * 4;
                        double coverage = (x + 1d) / source.Width;
                        ramp[i] = (byte)Math.Round(coverage * 255);
                        ramp[i + 1] = (byte)Math.Round(coverage * y / source.Height * 255);
                        ramp[i + 2] = (byte)Math.Round(coverage * .3 * 255);
                        ramp[i + 3] = (byte)Math.Round((linearAlpha ? coverage : coverage * coverage) * 255);
                    }
                    GL.BindTexture(TextureTarget.Texture2D, source.Texture);
                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, source.Width, source.Height,
                        PixelFormat.Rgba, PixelType.UnsignedByte, ramp);
                    post.SetBackground(null, 1);
                    post.SetShadow(ForegroundShadowOptions.Default);
                    var foregroundAlone = post.Render(source.Texture);
                    foreach (double opacity in new[] { 0, .5, 1 })
                    foreach (bool mask in new[] { false, true })
                    {
                        post.SetBackground(white, opacity);
                        post.SetShadow(ForegroundShadowOptions.Default);
                        var without = post.Render(source.Texture, mask);
                        post.SetShadow(new(true, 3, 45, 6, .7));
                        var with = post.Render(source.Texture, mask);
                        Check(Enumerable.Range(0, with.Length).Where(i => i % 4 != 3)
                            .All(i => with[i] <= without[i] + 1),
                            $"SSAA {factor}, linear alpha {linearAlpha}, BG {opacity}, mask {mask}: black shadow must never brighten any RGB channel");
                        if (!mask)
                            Check(Enumerable.Range(0, with.Length).Where(i => i % 4 != 3)
                                .All(i => with[i] + 1 >= foregroundAlone[i]),
                                "A background shadow must not subtract the foreground's own light at legacy clipped edges");
                        if (opacity == 0 && !mask)
                            Check(Enumerable.Range(0, with.Length).Where(i => i % 4 != 3)
                                .All(i => Math.Abs(with[i] - without[i]) <= 1),
                                "With no visible background, shadow must preserve the original foreground RGB including low-alpha edges");
                    }
                }
                post.SetBackground(null, 1);
                Shape(left: 0);
                post.SetShadow(ForegroundShadowOptions.Default);
                var edgeBaseline = post.Render(source.Texture, alphaMask: true);
                post.SetShadow(new(true, 3, 0, 8, .7));
                var shiftedEdge = post.Render(source.Texture, alphaMask: true);
                // The original SSAA pass itself repeats its edge samples; the
                // optional shadow must add no new alpha at that opposite edge.
                Check(shiftedEdge[At(6, 8)] > 0 && shiftedEdge[At(23, 8)] == edgeBaseline[At(23, 8)],
                    $"SSAA {factor}: resolved coverage must retain a shifted soft boundary without wraparound");
                Shape(left: 0, top: 0, width: 24, height: 24);
                post.SetShadow(ForegroundShadowOptions.Default);
                var opaqueSkin = post.Render(source.Texture);
                post.SetShadow(new(true, 3, 45, 18, .7));
                Check(post.Render(source.Texture).SequenceEqual(opaqueSkin),
                    "A fully opaque skin must cover its whole-layer shadow; it must not generate a visible black rectangle");
                Check(GL.GetError() == ErrorCode.NoError, "Shadow passes must leave no GL errors");
                var effect = typeof(ScenePostProcessor).GetField("shadowRenderer", Fields)!.GetValue(post)!;
                var baseline = (RenderTarget)typeof(ScenePostProcessor).GetField("shadowBaseline", Fields)!.GetValue(post)!;
                var target = (RenderTarget)effect.GetType().GetField("horizontal", Fields)!.GetValue(effect)!;
                int texture = target.Texture;
                var resolved = (RenderTarget?)effect.GetType().GetField("resolvedCoverage", Fields)!.GetValue(effect);
                int coverageTexture = resolved?.Texture ?? 0;
                int sampler = (int)effect.GetType().GetField("sampler", Fields)!.GetValue(effect)!;
                int program = (int)effect.GetType().GetField("horizontalProgram", Fields)!.GetValue(effect)!;
                post.Dispose();
                Check(!GL.IsTexture(texture) && !GL.IsTexture(baseline.Texture) && (coverageTexture == 0 || !GL.IsTexture(coverageTexture))
                    && !GL.IsSampler(sampler) && !GL.IsProgram(program),
                    "Disposal must release lazily allocated shadow textures, samplers and shaders");
            }

            // Public Scene path: live edits may recompose the current foreground
            // but must never call the original script again or reset particles.
            var scriptFolder = Path.Combine(folder, "script");
            Directory.CreateDirectory(scriptFolder);
            File.WriteAllText(Path.Combine(scriptFolder, "script.cs"), """
                using System.Collections.Generic;
                using OpenTK.Graphics;
                using ScriptedEngine;
                using ZenithEngine;
                public class Script {
                    public long LastNoteCount;
                    public void Load() { }
                    public void RenderInit(RenderOptions options) { LastNoteCount = 1000; }
                    public void Render(IEnumerable<Note> notes, RenderOptions options) {
                        LastNoteCount++;
                        IO.RenderQuad(.3, .6, .4, .4, new Color4(.4f, .8f, .2f, 1f));
                    }
                }
                """);
            byte[] midiBytes = [77, 84, 104, 100, 0, 0, 0, 6, 0, 0, 0, 1, 1, 224,
                77, 84, 114, 107, 0, 0, 0, 4, 0, 255, 47, 0];
            var midi = MidiSequence.Load(new MemoryStream(midiBytes));
            using var script = ScriptedPack.Load(scriptFolder);
            var settings = new RenderSettings { width = 24, height = 24, BGImage = white };
            using var scene = new SceneRenderer(midi, settings, script: script);
            var disabled = scene.Render(0);
            scene.UpdateShadow(new(true, 3, 45, 6, .7));
            var preview = scene.Recompose();
            Check(!preview.SequenceEqual(disabled) && scene.LastNoteCount == 1001,
                "Enabling shadow on a paused frame must change pixels while retaining the original script state");
            settings.ffRender = true;
            Check(scene.Recompose().SequenceEqual(preview), "Preview and normal export must have identical shadow pixels");
            scene.UpdateShadow(ForegroundShadowOptions.Default);
            Check(scene.Recompose().SequenceEqual(disabled) && scene.LastNoteCount == 1001,
                "Disabling live shadow must restore the old frame without rendering or resetting the script");
            scene.Dispose();
            string artifacts = Path.GetFullPath("artifacts/validation/shadow");
            Directory.CreateDirectory(artifacts);
            var visualSettings = new RenderSettings { width = 320, height = 180, BGImage = white };
            using var visual = new SceneRenderer(midi, visualSettings, script: script);
            SceneRenderer.SavePng(Path.Combine(artifacts, "disabled.png"), visual.Render(0), 320, 180);
            visual.UpdateShadow(ForegroundShadowOptions.Default with { Enabled = true });
            SceneRenderer.SavePng(Path.Combine(artifacts, "shadow-on-white.png"), visual.Recompose(), 320, 180);
            visual.UpdateBackground(null, 1);
            visualSettings.ffRender = visualSettings.ffRenderMask = true;
            var encodedFrame = visual.Recompose();
            byte[] maskPixels = new byte[encodedFrame.Length];
            for (int i = 0; i < maskPixels.Length; i += 4)
            {
                maskPixels[i] = maskPixels[i + 1] = maskPixels[i + 2] = encodedFrame[i + 3];
                maskPixels[i + 3] = 255;
            }
            SceneRenderer.SavePng(Path.Combine(artifacts, "shadow-alpha-mask.png"), maskPixels, 320, 180);
            if (export)
            {
                var result = FfmpegExporter.ExportAsync(new()
                {
                    OutputPath = Path.Combine(artifacts, "shadow-color.mp4"),
                    MaskOutputPath = Path.Combine(artifacts, "shadow-mask.mp4"),
                    Width = 320, Height = 180, FramesPerSecond = 3, DurationSeconds = 1, Crf = 0, Preset = "ultrafast"
                }, (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(encodedFrame)).GetAwaiter().GetResult();
                byte[] Decode(string path)
                {
                    using var decoder = Process.Start(new ProcessStartInfo("ffmpeg")
                    {
                        RedirectStandardOutput = true, RedirectStandardError = true,
                        ArgumentList = { "-v", "error", "-i", path, "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1" }
                    })!;
                    using var bytes = new MemoryStream();
                    var errors = decoder.StandardError.ReadToEndAsync();
                    decoder.StandardOutput.BaseStream.CopyTo(bytes);
                    decoder.WaitForExit();
                    if (decoder.ExitCode != 0) throw new Exception(errors.GetAwaiter().GetResult());
                    return bytes.ToArray();
                }
                var video = Decode(result.OutputPath);
                var mask = Decode(result.MaskOutputPath!);
                Check(video.Length == encodedFrame.Length * 3 && mask.Length == video.Length,
                    "Actual FFmpeg video and mask must both decode to three complete frames");
                for (int frame = 0; frame < 3; frame++)
                {
                    int shadowPixel = encodedFrame.Length * frame + (95 * 320 + 132) * 4;
                    int notePixel = encodedFrame.Length * frame + (90 * 320 + 110) * 4;
                    Check(video[shadowPixel + 2] <= 2 && Math.Abs(mask[shadowPixel + 2] - 179) <= 2
                        && Math.Abs(video[notePixel + 2] - 102) <= 4 && Math.Abs(video[notePixel + 1] - 204) <= 4,
                        "Encoded color must retain the note color while the separate mask carries the 70% black shadow");
                }
            }
            Console.WriteLine($"PASS {count} GPU shadow checks: disabled identity, cardinal angles, final-pixel SSAA distance, Gaussian softness, opacity/mask alpha, transparent border, opaque-skin coverage, live script-state preservation and cleanup.");
            Console.WriteLine("Shadow visual oracles: " + artifacts);
        }
        finally { Directory.Delete(folder, true); }
    }
}
