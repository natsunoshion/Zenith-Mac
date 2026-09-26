using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using ScriptedEngine;
using SkiaSharp;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using Zenith.Core.Scripted;
using ZenithEngine;
using ScriptBlend = ScriptedEngine.BlendFunc;

internal static class PremultipliedCompositingChecks
{
    internal static void Run()
    {
        if (!OperatingSystem.IsMacOS()) return;
        int count = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            count++;
        }
        const int width = 24, height = 16;
        static int At(int x, int y, int channel = 3) => (y * width + x) * 4 + channel;
        static QuadCommand Quad(Color4 color, ScriptBlend blend = ScriptBlend.Mix, Texture? texture = null,
            double left = 0, double bottom = 0, double right = 1, double top = 1)
            => new(new(left, top), new(right, top), new(right, bottom), new(left, bottom),
                color, color, color, color, texture, new(0, 0), new(1, 0), new(1, 1), new(0, 1), TextureShaders.Normal, blend);

        string folder = Path.Combine(Path.GetTempPath(), "zenith-premultiplied-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string white = Path.Combine(folder, "white.png");
            SceneRenderer.SavePng(white, [255, 255, 255, 255], 1, 1);
            byte[] hazePng;
            using (var bitmap = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul))
            {
                for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    double radius = Math.Sqrt(Math.Pow((x - 3.5) / 4, 2) + Math.Pow((y - 3.5) / 4, 2));
                    // Keep the fixture's white color defined at every texel.
                    // PNG codecs can canonicalize zero-alpha RGB to black;
                    // linear sampling that straight-color edge is a different
                    // texture-fringing test, not a coverage/compositing oracle.
                    bitmap.SetPixel(x, y, new SKColor(255, 255, 255, (byte)Math.Max(1, Math.Round(Math.Max(0, 1 - radius) * 100))));
                }
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                hazePng = encoded.ToArray();
            }
            var hazeTexture = new Texture { path = "white radial haze fixture", Data = hazePng,
                width = 8, height = 8, aspectRatio = 1, linear = true, looped = false };

            using (var context = new NativeContext())
            foreach (int factor in new[] { 1, 2, 4 })
            {
                using var source = new RenderTarget(width * factor, height * factor);
                using var legacy = new ScriptedGlRenderer();
                using var corrected = new ScriptedGlRenderer(correctCoverage: true);
                using var post = new ScenePostProcessor(source.Width, source.Height, factor, white);
                post.SetPremultipliedForeground(true);
                byte[] Draw(ScriptedGlRenderer renderer, params ScriptedDrawCommand[] commands)
                {
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, source.Framebuffer);
                    GL.Viewport(0, 0, source.Width, source.Height);
                    GL.ColorMask(true, true, true, true);
                    GL.ClearColor(0, 0, 0, 0);
                    GL.Clear(ClearBufferMask.ColorBufferBit);
                    renderer.Draw(new ScriptedFrame(commands), (double)width / height);
                    return source.ReadBgra();
                }

                foreach (int layers in new[] { 1, 2, 10 })
                {
                    var commands = Enumerable.Range(0, layers)
                        .Select(_ => (ScriptedDrawCommand)Quad(new Color4(1f, 1f, 1f, .1f))).ToArray();
                    byte[] original = Draw(legacy, commands);
                    byte[] raw = Draw(corrected, commands);
                    Check(Enumerable.Range(0, raw.Length).Where(i => i % 4 != 3).All(i => raw[i] == original[i]),
                        $"SSAA {factor}, {layers} white layers: correcting coverage must not alter the original Scripted RGB blend");
                    Check(Math.Abs(raw[3] - (1 - Math.Pow(.9, layers)) * 255) <= 3
                        && raw[0] == raw[3] && raw[1] == raw[3] && raw[2] == raw[3],
                        "White layers must retain equal premultiplied color and actual coverage, including overlaps");
                    Check(Math.Abs(original[3] - Math.Min(1, layers * .1) * 255) <= 3,
                        "The public legacy constructor must retain the old additive alpha behavior");
                    post.SetBackground(white, 1);
                    var onWhite = post.Render(source.Texture);
                    Check(onWhite.All(value => value >= 254),
                        "Any number of white haze layers over white must stay white, with no dark disk or ring");
                    post.SetBackground(white, .5);
                    var onHalf = post.Render(source.Texture);
                    double coverage = raw[3] / 255d;
                    Check(Math.Abs(onHalf[2] - (coverage + .5 * (1 - coverage)) * 255) <= 2
                        && Math.Abs(onHalf[3] - (coverage + .5 * (1 - coverage)) * 255) <= 2,
                        "Semi-transparent background must use source-over coverage without sqrt compensation");
                    post.SetBackground(null, 1);
                    var onBlack = post.Render(source.Texture);
                    Check(Math.Abs(onBlack[2] - raw[2]) <= 1 && Math.Abs(onBlack[3] - raw[3]) <= 1,
                        "SSAA resolve must preserve a uniform Scripted foreground's color and coverage on transparent black");
                }

                // Texture alpha, overlap and alternating blend modes exercise
                // the real command batching path used by SynX glow/particles.
                foreach (ScriptBlend blend in new[] { ScriptBlend.Mix, ScriptBlend.Add })
                {
                    var textured = new ScriptedDrawCommand[]
                    {
                        Quad(Color4.White, texture: hazeTexture),
                        Quad(new Color4(1f, 1f, 1f, .6f), blend, hazeTexture, left: .2, right: .9),
                        Quad(new Color4(1f, 1f, 1f, .4f), texture: hazeTexture)
                    };
                    byte[] original = Draw(legacy, textured);
                    byte[] raw = Draw(corrected, textured);
                    Check(Enumerable.Range(0, raw.Length).Where(i => i % 4 != 3).All(i => raw[i] == original[i]),
                        "Mix/Add transitions and texture sampling must keep the existing foreground RGB exactly");
                    post.SetBackground(white, 1);
                    var composed = post.Render(source.Texture);
                    Check(composed.All(value => value >= 253),
                        $"SSAA {factor}, {blend}: textured white glow may brighten but must never darken a white background; min={composed.Min()}, raw min(R-A)={Enumerable.Range(0, raw.Length / 4).Min(i => raw[i * 4 + 2] - raw[i * 4 + 3])}");
                }

                Draw(corrected,
                    Quad(new Color4(.4f, .8f, .2f, 1f), left: 2d / width, right: 6d / width, bottom: .25, top: .75),
                    Quad(new Color4(.4f, .8f, .2f, .5f), left: 8d / width, right: 12d / width, bottom: .25, top: .75));
                post.SetBackground(null, 1);
                post.SetShadow(ForegroundShadowOptions.Default);
                var withoutShadow = post.Render(source.Texture);
                post.SetShadow(new(true, 0, 0, 6, .6));
                var withShadow = post.Render(source.Texture);
                Check(Enumerable.Range(0, 3).All(c => Math.Abs(withShadow[At(3, 8, c)] - withoutShadow[At(3, 8, c)]) <= 1),
                    "Shadow behind an opaque note must not alter that note's RGB");
                Check(Enumerable.Range(0, withShadow.Length).Where(i => i % 4 != 3)
                    .All(i => Math.Abs(withShadow[i] - withoutShadow[i]) <= 1),
                    "Black shadow on transparent black may add coverage but must retain every foreground color");
                Check(Math.Abs(withShadow[At(9, 8)] - .8 * 255) <= 2,
                    "A 50% note over a 60% cast shadow must have 80% combined coverage");
                post.SetBackground(white, .5);
                var withBackground = post.Render(source.Texture);
                var mask = post.Render(source.Texture, alphaMask: true);
                Check(Math.Abs(withBackground[At(9, 8)] - .9 * 255) <= 2
                    && Math.Abs(withBackground[At(9, 8, 2)] - .3 * 255) <= 3,
                    "50% red=.4 note over 60% shadow and 50% white background must retain .3 red and .9 alpha");
                Check(Enumerable.Range(0, mask.Length).Where(i => i % 4 != 3)
                    .All(i => Math.Abs(mask[i] * mask[i / 4 * 4 + 3] / 255d - withBackground[i]) <= 2),
                    "Straight-color export and alpha mask must reconstruct the normal premultiplied frame");
                Check(Enumerable.Range(0, mask.Length / 4).All(i => mask[i * 4 + 3] == withBackground[i * 4 + 3]),
                    "Video and mask paths must carry exactly the same combined background/foreground/shadow coverage");
                Check(GL.GetError() == ErrorCode.NoError, "Corrected Scripted composition must not leak GL errors");
            }

            // Check that the public Scene path actually opts Scripted into the
            // corrected renderer, rather than only testing an explicit switch.
            string scriptFolder = Path.Combine(folder, "script");
            Directory.CreateDirectory(scriptFolder);
            File.WriteAllText(Path.Combine(scriptFolder, "script.cs"), """
                using System.Collections.Generic;
                using OpenTK.Graphics;
                using ScriptedEngine;
                using ZenithEngine;
                public class Script {
                    public void Load() { }
                    public void Render(IEnumerable<Note> notes, RenderOptions options) {
                        for (int i = 0; i < 10; i++) IO.RenderQuad(0, 1, 1, 0, new Color4(1f, 1f, 1f, .1f));
                    }
                }
                """);
            byte[] midiBytes = [77, 84, 104, 100, 0, 0, 0, 6, 0, 0, 0, 1, 1, 224,
                77, 84, 114, 107, 0, 0, 0, 4, 0, 255, 47, 0];
            var midi = MidiSequence.Load(new MemoryStream(midiBytes));
            using var script = ScriptedPack.Load(scriptFolder);
            foreach (int factor in new[] { 1, 2, 4 })
            {
                using var scene = new SceneRenderer(midi, new RenderSettings
                    { width = width * factor, height = height * factor, downscale = factor, BGImage = white }, script: script);
                Check(scene.Render(0).All(value => value >= 254),
                    "SceneRenderer must automatically select correct coverage and composition for Scripted resources");
            }
            Console.WriteLine($"PASS {count} premultiplied Scripted checks: original RGB retention, overlapping white haze, Mix/Add textures, SSAA 1/2/4, background/shadow coverage and mask reconstruction.");
        }
        finally { Directory.Delete(folder, true); }
    }
}
