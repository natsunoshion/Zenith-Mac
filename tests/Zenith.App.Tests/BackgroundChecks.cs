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
                // These low RGB values avoid the original postshader's clamp:
                // its compensated 100% background writes RGB unchanged and A
                // equal to PNG alpha. This is an independent numeric oracle.
                Check(Math.Abs(original[2] - 80) <= 1 && Math.Abs(original[3] - alpha) <= 1,
                    "100% background must preserve the existing PNG/postshader behavior");
                long renderCount = scene.LastNoteCount;
                foreach (double opacity in new[] { 0, .5, 1 })
                {
                    scene.UpdateBackground(path, opacity);
                    var preview = scene.Recompose();
                    Check(Math.Abs(preview[2] - 80 * opacity) <= 2 && Math.Abs(preview[1] - 40 * opacity) <= 2
                        && Math.Abs(preview[0] - 20 * opacity) <= 2 && Math.Abs(preview[3] - alpha * opacity) <= 2,
                        $"PNG alpha {alpha}, SSAA {ssaa}, opacity {opacity}: background RGB and alpha must scale once");
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
            Console.WriteLine($"PASS {count} native background checks: 0/50/100%, PNG alpha, SSAA, preview/export/mask, pure recomposition, cached toggles and failed replacement.");
        }
        finally { Directory.Delete(folder, true); }
    }
}
