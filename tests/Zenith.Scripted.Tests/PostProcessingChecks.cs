using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using Zenith.Core.Scripted;
using ZenithEngine;

internal static class PostProcessingChecks
{
    internal static void Run(string temp, Action<bool, string> assert)
    {
        if (!OperatingSystem.IsMacOS()) return;
        // Source rows are in OpenGL order; background rows are image-file order.
        // A CPU transcription of RenderWindow's shader + GL blend equations is
        // independent of the port's GPU pass implementation. This is source
        // parity evidence, not a screenshot comparison against Windows.
        byte[] background = [220, 30, 80, 64, 20, 120, 60, 190,
            5, 10, 200, 255, 50, 200, 30, 128];
        var backgroundPath = Path.Combine(temp, "post-background.png");
        using (var image = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul))
        {
            System.Runtime.InteropServices.Marshal.Copy(background, 0, image.GetPixels(), background.Length);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(backgroundPath);
            data.SaveTo(file);
        }
        using (var context = new NativeContext())
        foreach (var factor in new[] { 1, 2, 3 })
        {
            const int width = 3, height = 2;
            int sourceWidth = width * factor, sourceHeight = height * factor;
            var source = new byte[sourceWidth * sourceHeight * 4];
            for (var y = 0; y < sourceHeight; y++)
            for (var x = 0; x < sourceWidth; x++)
            {
                var p = (y * sourceWidth + x) * 4;
                source[p] = (byte)((x * 71 + y * 13 + 11) % 256);
                source[p + 1] = (byte)((x * 17 + y * 89 + 37) % 256);
                source[p + 2] = (byte)((x * 43 + y * 29 + 23) % 256);
                source[p + 3] = (byte)((x * 67 + y * 53 + 32) % 224 + 32);
            }
            using var target = new RenderTarget(sourceWidth, sourceHeight);
            GL.BindTexture(TextureTarget.Texture2D, target.Texture);
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, sourceWidth, sourceHeight,
                PixelFormat.Rgba, PixelType.UnsignedByte, source);
            foreach (var withBackground in new[] { false, true })
            using (var post = new ScenePostProcessor(sourceWidth, sourceHeight, factor,
                       withBackground ? backgroundPath : null))
            foreach (var mask in new[] { false, true })
            {
                var actual = post.Render(target.Texture, mask);
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    double u = (x + .5) / width, v = 1 - (y + .5) / height;
                    // User-requested cover framing: this square background is
                    // cropped to its middle 2/3 vertically in a 3:2 frame.
                    // The original alpha/compositing oracle stays unchanged.
                    var bg = withBackground ? Post(Sample(background, 2, 2, u,
                        1d / 6 + (1 - v) * 2 / 3, repeat: false)) : new double[4];
                    var foreground = new double[4];
                    for (var i = 0; i < factor; i++)
                    for (var j = 0; j < factor; j++)
                    {
                        var sample = Sample(source, sourceWidth, sourceHeight,
                            u + (double)i / sourceWidth, v + (double)j / sourceHeight, repeat: true);
                        for (var c = 0; c < 4; c++) foreground[c] += sample[c] / (factor * factor);
                    }
                    var composite = Blend(factor == 1 ? AlphaShader(foreground) : foreground, bg);
                    var expected = mask
                        ? new[] { Quantize(composite[0] / composite[3]), Quantize(composite[1] / composite[3]),
                            Quantize(composite[2] / composite[3]), composite[3] }
                        : Post(composite);
                    var offset = (y * width + x) * 4;
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var index = channel == 3 ? 3 : 2 - channel;
                        assert(Math.Abs(actual[offset + index] - expected[channel] * 255) <= 3,
                            $"Original terminal pipeline SSAA={factor}, background={withBackground}, mask={mask}, " +
                            $"pixel=({x},{y}), channel={channel}: GPU {actual[offset + index]}, reference {expected[channel] * 255:F2}");
                    }
                }
            }
            assert(GL.GetError() == ErrorCode.NoError, "Terminal pass has no GL errors");
        }

        // Exercise the public Scene path and the existing encoder's alpha
        // transport contract, rather than only invoking the postprocessor.
        var folder = Path.Combine(temp, "PostProcessScript");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "script.cs"), """
            using System.Collections.Generic;
            using OpenTK.Graphics;
            using ScriptedEngine;
            using ZenithEngine;
            public class Script {
                public void Load() { }
                public void Render(IEnumerable<Note> notes, RenderOptions options) {
                    IO.RenderQuad(0, 1, 1, 0, new Color4(.4f, .2f, .1f, .5f));
                }
            }
            """);
        byte[] midiBytes = [77, 84, 104, 100, 0, 0, 0, 6, 0, 0, 0, 1, 1, 224,
            77, 84, 114, 107, 0, 0, 0, 4, 0, 255, 47, 0];
        var midi = MidiSequence.Load(new MemoryStream(midiBytes));
        using var script = ScriptedPack.Load(folder);
        foreach (var factor in new[] { 1, 2 })
        {
            var settings = new RenderSettings { width = 4 * factor, height = 4 * factor,
                downscale = factor, ffRender = true };
            using var scene = new SceneRenderer(midi, settings, script: script);
            var normal = scene.Render(0);
            settings.ffRenderMask = true;
            var mask = scene.Render(0);
            assert(Math.Abs(normal[2] - 51) <= 2,
                "Corrected Scripted Scene preserves premultiplied RGB at every SSAA factor without another alpha multiplication");
            assert(Math.Abs(normal[3] - 128) <= 2,
                "Corrected Scripted Scene preserves true 50% coverage while resolving SSAA");
            assert(Math.Abs(mask[2] - 102) <= 3 && mask[3] == normal[3],
                "Mask export transports unpremultiplied video RGB and separate mask gray value");
            settings.ffRender = false;
            assert(scene.Render(0).AsSpan().SequenceEqual(normal), "Preview ignores stale ffRenderMask flag");
        }
        Console.WriteLine("PASS native CGL terminal pipeline: legacy alpha compensation/SSAA 1/2/3 sampling and wrap, transparent background/flip, mask color/alpha; corrected Scripted Scene coverage and export flags");
    }

    static double Quantize(double value) => Math.Round(Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 1) * 255) / 255;
    static double[] AlphaShader(double[] color) =>
        [color[0] / Math.Sqrt(color[3]), color[1] / Math.Sqrt(color[3]), color[2] / Math.Sqrt(color[3]), Math.Sqrt(color[3])];
    static double[] Post(double[] color) => Blend(AlphaShader(color), new double[4]);
    static double[] Blend(double[] source, double[] destination)
    {
        var result = new double[4];
        for (var i = 0; i < 4; i++)
            result[i] = Quantize(Math.Clamp(source[i], 0, 1) * source[3] + destination[i] * (1 - source[3]));
        return result;
    }
    static double[] Sample(byte[] image, int width, int height, double u, double v, bool repeat)
    {
        double sx = u * width - .5, sy = v * height - .5;
        int left = (int)Math.Floor(sx), bottom = (int)Math.Floor(sy);
        double fx = sx - left, fy = sy - bottom;
        var result = new double[4];
        for (var dx = 0; dx < 2; dx++)
        for (var dy = 0; dy < 2; dy++)
        {
            int x = repeat ? ((left + dx) % width + width) % width : Math.Clamp(left + dx, 0, width - 1);
            int y = repeat ? ((bottom + dy) % height + height) % height : Math.Clamp(bottom + dy, 0, height - 1);
            double weight = (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
            for (var c = 0; c < 4; c++) result[c] += image[(y * width + x) * 4 + c] / 255d * weight;
        }
        return result;
    }
}
