using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using ScriptedEngine;
using SkiaSharp;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using Zenith.Core.Scripted;
using ZenithEngine;
using Blend = ScriptedEngine.BlendFunc;

internal static class GpuParityChecks
{
    internal static void Run(string temp, Action<bool, string> assert)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using (var context = new NativeContext())
        using (var target = new RenderTarget(2, 2))
        using (var renderer = new ScriptedGlRenderer())
        {
            using var bitmap = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            bitmap.SetPixel(0, 0, new SKColor(128, 64, 192, 128));
            bitmap.SetPixel(1, 0, new SKColor(32, 96, 160, 255));
            bitmap.SetPixel(0, 1, new SKColor(255, 0, 0, 255));
            bitmap.SetPixel(1, 1, new SKColor(0, 255, 0, 255));
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            var tex = new Texture { Data = png.ToArray(), width = 2, height = 2, looped = false };
            var color = new Color4(.4f, .8f, .2f, .6f);
            foreach (var shader in Enum.GetValues<TextureShaders>())
            foreach (var blend in Enum.GetValues<Blend>())
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, target.Framebuffer);
                GL.Viewport(0, 0, 2, 2);
                GL.ClearColor(0, 0, 0, 0);
                GL.Clear(ClearBufferMask.ColorBufferBit);
                var q = new QuadCommand(new(0, 1), new(1, 1), new(1, 0), new(0, 0),
                    color, color, color, color, tex, new(0, 0), new(1, 0), new(1, 1), new(0, 1), shader, blend);
                renderer.Draw(new ScriptedFrame(new[] { q, q }), 1);
                var pixels = target.ReadBgra();
                // Two composited draws reproduce the original separate RGB/alpha
                // factors, including Hybrid's intentional doubled source alpha.
                var expected = Shade(new[] { 128d / 255, 64d / 255, 192d / 255, 128d / 255 }, color, shader);
                for (var channel = 0; channel < 3; channel++)
                {
                    var first = expected[channel] * expected[3];
                    var twice = first + first * (blend == Blend.Add ? 1 : 1 - expected[3]);
                    assert(Math.Abs(pixels[2 - channel] - Math.Clamp(twice * 255, 0, 255)) <= 2,
                        $"GPU {shader}/{blend} original RGB equation channel {channel}");
                }
                assert(Math.Abs(pixels[3] - Math.Min(255, expected[3] * 2 * 255)) <= 2,
                    $"GPU {shader}/{blend} original separate alpha accumulation");
                assert(pixels[8 + 2] > pixels[8 + 1], $"GPU {shader}/{blend} texture UV top/bottom orientation");
            }
            assert(GL.GetError() == ErrorCode.NoError, "Shader parity checks finish without GL errors");
            Console.WriteLine("PASS native CGL pixel parity: all 3 texture shaders, both blend modes, alpha and UV orientation");
        }

        var sceneProbe = Path.Combine(temp, "Scene");
        Directory.CreateDirectory(sceneProbe);
        File.WriteAllText(Path.Combine(sceneProbe, "script.cs"), """
            using System.Collections.Generic;
            using System.Linq;
            using ScriptedEngine;
            using ZenithEngine;
            public class Script {
                public long LastNoteCount;
                public double NoteScreenTime=0;
                public void Load(){}
                public void RenderInit(RenderOptions options){LastNoteCount=-1;}
                public void RenderDispose(){LastNoteCount=-2;}
                public void Render(IEnumerable<Note> notes,RenderOptions options){
                    LastNoteCount=notes.Count()*10000+options.midiBarLength;
                    foreach(var n in notes)n.delete=true;
                }
            }
            """);
        byte[] track = [0, 255, 88, 4, 3, 2, 24, 8,
            0x83, 0x60, 0x90, 60, 100, 0x87, 0x40, 0x90, 61, 100,
            0x83, 0x60, 0x80, 60, 0, 0, 0x80, 61, 0, 0, 255, 47, 0];
        using var midiBytes = new MemoryStream();
        midiBytes.Write("MThd"u8); midiBytes.Write([0, 0, 0, 6, 0, 0, 0, 1, 1, 224]);
        midiBytes.Write("MTrk"u8); midiBytes.Write([0, 0, 0, (byte)track.Length]); midiBytes.Write(track); midiBytes.Position = 0;
        var midi = MidiSequence.Load(midiBytes);
        using var pack = ScriptedPack.Load(sceneProbe);
        using (var scene = new SceneRenderer(midi, new RenderSettings { width = 16, height = 16 }, script: pack))
        {
            scene.ScreenTime = 960;
            scene.Render(0);
            assert(pack.LastNoteCount == 11440, "First frame collects future notes and passes 3/4 bar length");
            scene.Render(.01);
            assert(pack.LastNoteCount == 11440, "Nonmanual collector ignores script note.delete as upstream does");
            scene.ScreenTime = 1800;
            scene.Render(.02);
            assert(pack.LastNoteCount == 21440, "Live screen-time increase collects newly visible notes immediately");
        }
        assert(pack.LastNoteCount == -2, "Scene disposal ends rendering without invoking RenderInit again");
        Console.WriteLine("PASS scene lifecycle, 3/4 timing, note lifetime, and live screen-time changes");
    }

    private static double[] Shade(double[] tex, Color4 color, TextureShaders shader)
    {
        double[] c = [color.R, color.G, color.B];
        var result = new double[4];
        for (var i = 0; i < 3; i++) result[i] = shader switch
        {
            TextureShaders.Normal => tex[i] * c[i],
            TextureShaders.Inverted => 1 - (1 - tex[i]) * (1 - c[i]),
            _ => tex[i] > .5 ? 1 - (2 - tex[i] * 2) * (1 - c[i]) : tex[i] * 2 * c[i]
        };
        result[3] = Math.Clamp(tex[3] * color.A * (shader == TextureShaders.Hybrid ? 2 : 1), 0, 1);
        return result;
    }
}
