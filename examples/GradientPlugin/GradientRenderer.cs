using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using ScriptedEngine;
using Zenith.Core.Rendering;
using Zenith.Core.Scripted;
using ZenithEngine;
using Blend = ScriptedEngine.BlendFunc;

namespace Zenith.GradientExample;

public sealed class GradientSettings
{
    public double NoteScreenTime = 1920;
    public bool ShowKeyboard = true;
}

/// <summary>A complete source plugin example using the host's native GL command renderer.</summary>
public sealed class GradientRenderer(RenderSettings renderSettings) : IPluginRender
{
    readonly GradientSettings settings = new();
    ScriptedGlRenderer? graphics;
    public string Name => "Gradient Example";
    public string Description => "Example macOS source plugin with persistent notes and live settings.";
    public string LanguageDictName => "gradient-example";
    public object SettingsControl => settings;
    public object PreviewImage => null!;
    public bool Initialized => graphics != null;
    public bool ManualNoteDelete => false;
    public double NoteCollectorOffset => 0;
    public double NoteScreenTime => Math.Max(1, settings.NoteScreenTime);
    public long LastNoteCount { get; private set; }
    public NoteColor[][] NoteColors { private get; set; } = [];
    public double Tempo { private get; set; }
    public MidiInfo CurrentMidi { private get; set; } = new();

    public void Init() => graphics = new ScriptedGlRenderer();
    public void ReloadTrackColors() { } // The host's NoteColor objects already carry palette and MIDI color events.
    public void RenderFrame(FastList<Note> notes, double midiTime, int finalCompositeBuff)
    {
        var commands = new List<ScriptedDrawCommand>();
        var keyboardHeight = settings.ShowKeyboard ? .12 : 0;
        LastNoteCount = 0;
        foreach (var note in notes)
        {
            if (note.start > midiTime + NoteScreenTime) break;
            if (note.end < midiTime || note.key >= 128) continue;
            var left = note.key / 128d;
            var right = (note.key + 1) / 128d;
            var bottom = keyboardHeight + Math.Max(0, note.start - midiTime) / NoteScreenTime * (1 - keyboardHeight);
            var top = keyboardHeight + (note.end - midiTime) / NoteScreenTime * (1 - keyboardHeight);
            commands.Add(Quad(left, top, right, bottom, note.color.left, note.color.right));
            LastNoteCount++;
        }
        if (settings.ShowKeyboard)
            for (var key = 0; key < 128; key++)
            {
                var color = Util.IsBlackKey(key) ? Color4.Black : Color4.White;
                commands.Add(Quad(key / 128d, keyboardHeight, (key + .94) / 128d, 0, color, color));
            }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, finalCompositeBuff);
        graphics!.Draw(new ScriptedFrame(commands), renderSettings.width / (double)renderSettings.height);
    }

    static QuadCommand Quad(double left, double top, double right, double bottom, Color4 a, Color4 b) =>
        new(new(left, top), new(right, top), new(right, bottom), new(left, bottom), a, b, b, a,
            null, Vector2d.Zero, Vector2d.Zero, Vector2d.Zero, Vector2d.Zero, TextureShaders.Normal, Blend.Mix);

    public void Dispose()
    {
        var previous = graphics;
        graphics = null;
        previous?.Dispose();
    }
}
