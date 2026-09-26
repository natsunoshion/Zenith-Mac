using ZenithEngine;

namespace Zenith.ExamplePlugin;

/// <summary>A complete loadable module that delegates drawing to the ported original Flat renderer.
/// Replace RenderFrame with your own OpenTK drawing code to create a new module.</summary>
public sealed class Render : IPluginRender
{
    readonly FlatRender.Render renderer;
    public Render(RenderSettings settings) => renderer = new(settings);
    public string Name => "Example plugin (Flat)";
    public string Description => "Loadable macOS renderer SDK example using the original Flat rendering algorithm.";
    public string LanguageDictName => "example-flat";
    public bool Initialized => renderer.Initialized;
    public object PreviewImage => renderer.PreviewImage;
    public bool ManualNoteDelete => renderer.ManualNoteDelete;
    public double NoteCollectorOffset => renderer.NoteCollectorOffset;
    public NoteColor[][] NoteColors { set => renderer.NoteColors = value; }
    public double Tempo { set => renderer.Tempo = value; }
    public MidiInfo CurrentMidi { set => renderer.CurrentMidi = value; }
    public double NoteScreenTime => renderer.NoteScreenTime;
    public long LastNoteCount => renderer.LastNoteCount;
    public object SettingsControl => renderer.SettingsControl;
    public void Init() => renderer.Init();
    public void RenderFrame(FastList<Note> notes, double midiTime, int finalCompositeBuff) => renderer.RenderFrame(notes, midiTime, finalCompositeBuff);
    public void ReloadTrackColors() => renderer.ReloadTrackColors();
    public void Dispose() => renderer.Dispose();
}
