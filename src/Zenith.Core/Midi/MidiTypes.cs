namespace Zenith.Core.Midi;

public readonly record struct MidiColor(byte R, byte G, byte B, byte A = 255);
public readonly record struct MidiNoteColor(MidiColor Left, MidiColor Right);

public sealed class MidiNote
{
    internal long SequenceOrder { get; set; }
    internal bool SoundKeyReleased { get; set; }
    public long StartTick { get; internal set; }
    public long EndTick { get; internal set; } = -1;
    public long SoundEndTick { get; internal set; } = -1;
    public long SoundReleaseTick { get; internal set; } = -1;
    public double StartSeconds { get; internal set; }
    public double EndSeconds { get; internal set; }
    public double SoundEndSeconds { get; internal set; }
    public double SoundReleaseSeconds { get; internal set; }
    public int Track { get; internal set; }
    public byte Channel { get; internal set; }
    public byte Key { get; internal set; }
    public byte Velocity { get; internal set; }
    public byte ReleaseVelocity { get; internal set; }
}

public sealed class MidiTrackInfo
{
    public int Index { get; internal set; }
    public string Name { get; internal set; } = "";
    public string Instrument { get; internal set; } = "";
    public long EndTick { get; internal set; }
    public long NoteCount { get; internal set; }
    public int ChannelMask { get; internal set; }
}

public readonly record struct MidiTempo(long Tick, int MicrosecondsPerQuarter, int Track = 0, long Order = 0);
public readonly record struct MidiTimeSignature(long Tick, byte Numerator, int Denominator, byte ClocksPerClick, byte ThirtySecondsPerQuarter);
public readonly record struct MidiText(long Tick, int Track, byte Type, string Text);
public readonly record struct MidiColorEvent(long Tick, double Seconds, int Track, byte Channel, MidiNoteColor Color);

/// <summary>A channel message or a SysEx packet. Payload excludes F0 but includes a terminal F7 when present.</summary>
public readonly record struct MidiEvent(long Tick, double Seconds, int Track, byte Status, byte Data1, byte Data2, byte[]? Payload = null, long Order = 0)
{
    public int Channel => Status & 15;
    public int Command => Status & 0xF0;
    public int PackedMessage => Status | (Data1 << 8) | (Data2 << 16);
    public bool IsNoteOn => Command == 0x90 && Data2 != 0;
    public bool IsNoteOff => Command == 0x80 || (Command == 0x90 && Data2 == 0);
}

public sealed class MidiLoadOptions
{
    public bool PreserveText { get; init; } = true;
    public bool PreserveSystemExclusive { get; init; } = true;
    public IProgress<double>? Progress { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
