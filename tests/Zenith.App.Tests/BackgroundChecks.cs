using System.Reflection;
using OpenTK.Graphics.OpenGL;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using Zenith.Core.Scripted;
using ZenithEngine;

internal static class BackgroundChecks
{
    internal static void Run()
    {
        if (!OperatingSystem.IsMacOS()) return;
        int count = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            count++;
        }
        var folder = Path.Combine(Path.GetTempPath(), "zenith-background-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var scriptFolder = Path.Combine(folder, "script");
            Directory.CreateDirectory(scriptFolder);
            File.WriteAllText(Path.Combine(scriptFolder, "script.cs"), """
                using System.Collections.Generic;
                using ScriptedEngine;
                using ZenithEngine;
                public class Script {
                    public long LastNoteCount;
                    public void Load() { }
                    public void RenderInit(RenderOptions options) { LastNoteCount = 1000; }
                    public void Render(IEnumerable<Note> notes, RenderOptions options) { LastNoteCount++; }
                }
                """);
            byte[] midiBytes = [77, 84, 104, 100, 0, 0, 0, 6, 0, 0, 0, 1, 1, 224,
                77, 84, 114, 107, 0, 0, 0, 4, 0, 255, 47, 0];
            var midi = MidiSequence.Load(new MemoryStream(midiBytes));
            using var script = ScriptedPack.Load(scriptFolder);
            foreach (byte alpha in new byte[] { 255, 128 })
            foreach (int ssaa in new[] { 1, 2 })
            {
                string path = Path.Combine(folder, $"background-{alpha}.png");
                SceneRenderer.SavePng(path, [20, 40, 80, alpha], 1, 1);
                var settings = new RenderSettings { width = 4 * ssaa, height = 4 * ssaa, downscale = ssaa,
                    BGImage = path, BGOpacity = 1 };
                using var scene = new SceneRenderer(midi, settings, script: script);
                byte[] original = scene.Render(0);
                // Scripted now uses standard premultiplied composition: source
                // PNG color is multiplied by its own alpha and global opacity.
                // The separate direct-post tests retain the builtin legacy oracle.
                double pngAlpha = alpha / 255d;
                Check(Math.Abs(original[2] - 80 * pngAlpha) <= 1 && Math.Abs(original[3] - alpha) <= 1,
                    "Scripted background at 100% must premultiply PNG RGB by its alpha exactly once");
                long renderCount = scene.LastNoteCount;
                foreach (double opacity in new[] { 0, .5, 1 })
                {
                    scene.UpdateBackground(path, opacity);
                    var preview = scene.Recompose();
                    Check(Math.Abs(preview[2] - 80 * pngAlpha * opacity) <= 2 && Math.Abs(preview[1] - 40 * pngAlpha * opacity) <= 2
                        && Math.Abs(preview[0] - 20 * pngAlpha * opacity) <= 2 && Math.Abs(preview[3] - alpha * opacity) <= 2,
                        $"Scripted PNG alpha {alpha}, SSAA {ssaa}, opacity {opacity}: RGB must be premultiplied by PNG alpha and opacity once");
                    settings.ffRender = true;
                    var export = scene.Recompose();
                    Check(export.SequenceEqual(preview), "Preview and ordinary export must use identical background opacity");
                    settings.ffRenderMask = true;
                    var mask = scene.Recompose();
                    Check(Math.Abs(mask[3] - alpha * opacity) <= 2,
                        "Mask export must retain PNG alpha multiplied by global background opacity");
                    settings.ffRender = settings.ffRenderMask = false;
                    Check(scene.LastNoteCount == renderCount, "Background-only composition must not execute or reset the script");
                }
                Check(scene.Recompose().SequenceEqual(original), "Restoring 100% must restore exact previous pixels");
                var processor = (ScenePostProcessor)typeof(SceneRenderer).GetField("postProcessor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                var textureField = typeof(ScenePostProcessor).GetField("background", BindingFlags.Instance | BindingFlags.NonPublic)!;
                int texture = (int)textureField.GetValue(processor)!;
                scene.UpdateBackground(null, 1);
                Check(scene.Recompose().All(value => value == 0), "Disabled background must be transparent black");
                scene.UpdateBackground(path, 1);
                Check((int)textureField.GetValue(processor)! == texture && scene.Recompose().SequenceEqual(original),
                    "Disable/enable must reuse the existing image texture and preserve exact pixels");
                string replacementPath = Path.Combine(folder, $"replacement-{alpha}.png");
                File.Copy(path, replacementPath, true);
                int unrelatedTexture = GL.GenTexture();
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, texture);
                GL.ActiveTexture(TextureUnit.Texture3);
                GL.BindTexture(TextureTarget.Texture2D, unrelatedTexture);
                try
                {
                    scene.UpdateBackground(replacementPath, 1);
                    GL.GetInteger(GetPName.ActiveTexture, out int active);
                    GL.GetInteger(GetPName.TextureBinding2D, out int bound);
                    Check(active == (int)TextureUnit.Texture3 && bound == unrelatedTexture,
                        "Background uploads must restore the foreground module's active texture and binding");
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.GetInteger(GetPName.TextureBinding2D, out int bound0);
                    Check(bound0 == (int)textureField.GetValue(processor)! && !GL.IsTexture(texture),
                        "A previous binding to the deleted background must use the replacement, not recreate the deleted texture");
                }
                finally
                {
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                    GL.DeleteTexture(unrelatedTexture);
                }
                var corrupt = Path.Combine(folder, "corrupt.png");
                File.WriteAllText(corrupt, "not an image");
                try { scene.UpdateBackground(corrupt, .5); throw new Exception("Corrupt background was accepted"); }
                catch (InvalidDataException) { }
                Check(scene.Recompose().SequenceEqual(original), "Failed replacement must preserve the last valid background");
                Check(scene.LastNoteCount == renderCount && GL.GetError() == ErrorCode.NoError,
                    "Hot background edits must retain script state and release resources without GL errors");
            }

            // Independent source-coordinate oracles for cover: square images
            // lose top/bottom rows on a landscape frame; very wide images lose
            // left/right columns. Each ramp encodes its original x/y position.
            var cases = new[]
            {
                (Name: "square", Width: 8, Height: 8, OutputWidth: 4, OutputHeight: 2, StartX: .5, StartY: .5, CropX: 0d, CropY: 4d, Step: 2d),
                (Name: "wide", Width: 16, Height: 4, OutputWidth: 4, OutputHeight: 4, StartX: 0d, StartY: 0d, CropX: 12d, CropY: 0d, Step: 1d),
                (Name: "portrait", Width: 4, Height: 12, OutputWidth: 4, OutputHeight: 2, StartX: 0d, StartY: 0d, CropX: 0d, CropY: 10d, Step: 1d),
                (Name: "same-ratio", Width: 8, Height: 4, OutputWidth: 4, OutputHeight: 2, StartX: .5, StartY: .5, CropX: 0d, CropY: 0d, Step: 2d)
            };
            foreach (var item in cases)
            foreach (int ssaa in new[] { 1, 2 })
            {
                byte[] pixels = new byte[item.Width * item.Height * 4];
                for (int y = 0; y < item.Height; y++)
                for (int x = 0; x < item.Width; x++)
                {
                    int i = (y * item.Width + x) * 4;
                    pixels[i] = (byte)(10 + 4 * x);
                    pixels[i + 1] = (byte)(20 + 5 * y);
                    pixels[i + 2] = (byte)(20 + 3 * x + 2 * y);
                    pixels[i + 3] = 128;
                }
                string path = Path.Combine(folder, item.Name + ".png");
                SceneRenderer.SavePng(path, pixels, item.Width, item.Height);
                var settings = new RenderSettings
                {
                    width = item.OutputWidth * ssaa, height = item.OutputHeight * ssaa, downscale = ssaa,
                    BGImage = path, BGOpacity = .5, BGPositionX = .5, BGPositionY = .5
                };
                using var scene = new SceneRenderer(midi, settings, script: script);
                var centered = scene.Render(0);
                long renderCount = scene.LastNoteCount;
                var processor = (ScenePostProcessor)typeof(SceneRenderer).GetField("postProcessor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                var textureField = typeof(ScenePostProcessor).GetField("background", BindingFlags.Instance | BindingFlags.NonPublic)!;
                int texture = (int)textureField.GetValue(processor)!;
                foreach (double position in new[] { .5, 0, 1, .5 })
                {
                    scene.UpdateBackground(path, .5, position, position);
                    byte[] result = scene.Recompose();
                    bool correct = true;
                    for (int y = 0; y < item.OutputHeight; y++)
                    for (int x = 0; x < item.OutputWidth; x++)
                    {
                        double sourceX = item.StartX + item.CropX * position + item.Step * x;
                        double sourceY = item.StartY + item.CropY * position + item.Step * y;
                        int i = (y * item.OutputWidth + x) * 4;
                        const double coverage = 128d / 255 * .5;
                        correct &= Math.Abs(result[i] - (10 + 4 * sourceX) * coverage) <= 2
                            && Math.Abs(result[i + 1] - (20 + 5 * sourceY) * coverage) <= 2
                            && Math.Abs(result[i + 2] - (20 + 3 * sourceX + 2 * sourceY) * coverage) <= 2
                            && Math.Abs(result[i + 3] - 64) <= 1;
                    }
                    Check(correct, $"Scripted {item.Name}, SSAA {ssaa}, position {position}: cover must retain aspect ratio, correct left/top origin and premultiplied PNG coverage");
                    Check((int)textureField.GetValue(processor)! == texture && scene.LastNoteCount == renderCount,
                        "Moving a paused background must reuse its texture and leave the original foreground state untouched");
                    Check(settings.BGPositionX == position && settings.BGPositionY == position,
                        "The renderer must retain the current crop position in its live settings");
                    settings.ffRender = true;
                    Check(scene.Recompose().SequenceEqual(result), "Cover framing must be identical in preview and normal export");
                    settings.ffRenderMask = true;
                    byte[] mask = scene.Recompose();
                    Check(Enumerable.Range(0, result.Length).Where(i => i % 4 != 3)
                        .All(i => Math.Abs(mask[i] * mask[i / 4 * 4 + 3] / 255d - result[i]) <= 2)
                        && Enumerable.Range(0, result.Length / 4).All(i => Math.Abs(mask[i * 4 + 3] - 64) <= 1),
                        "Positioned background color and alpha must reconstruct consistently from mask export");
                    settings.ffRender = settings.ffRenderMask = false;
                    scene.UpdateShadow(ForegroundShadowOptions.Default with { Enabled = true });
                    byte[] withShadow = scene.Recompose();
                    Check(withShadow.Zip(result).All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                        "Foreground shadow composition must not shift or change the cropped background");
                    scene.UpdateShadow(ForegroundShadowOptions.Default);
                    if (position == .5)
                        Check(scene.Recompose().SequenceEqual(centered), "Centered cover must return to exact initial pixels");
                }
                scene.UpdateBackground(path, 1, 0, 1);
                scene.UpdateBackground(null, .5);
                scene.UpdateBackground(path, .5);
                Check(settings.BGPositionX == 0 && settings.BGPositionY == 1
                    && (int)textureField.GetValue(processor)! == texture,
                    "Legacy opacity/toggle calls must preserve the selected crop and cached texture");
                byte[] beforeInvalid = scene.Recompose();
                foreach (double invalid in new[] { -.01, 1.01, double.NaN, double.PositiveInfinity })
                {
                    try { scene.UpdateBackground(path, .5, invalid, .5); throw new Exception("Invalid X crop was accepted"); }
                    catch (ArgumentOutOfRangeException) { }
                    try { scene.UpdateBackground(path, .5, .5, invalid); throw new Exception("Invalid Y crop was accepted"); }
                    catch (ArgumentOutOfRangeException) { }
                }
                Check(scene.Recompose().SequenceEqual(beforeInvalid) && scene.LastNoteCount == renderCount,
                    "Invalid crop positions must leave the valid paused frame and script unchanged");
                Check(GL.GetError() == ErrorCode.NoError, "Cover composition must not leak OpenGL errors");
            }
            Console.WriteLine($"PASS {count} native background checks: opacity/PNG alpha, aspect-preserving cover, crop direction/position, SSAA, preview/export/mask/shadow, pure recomposition, cached toggles and failed replacement.");
        }
        finally { Directory.Delete(folder, true); }
    }
}
