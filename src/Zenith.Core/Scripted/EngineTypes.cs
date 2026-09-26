using OpenTK.Mathematics;

// Field names and mutability are part of the original Scripted pack ABI.
namespace ZenithEngine;

public class Note
{
    public double start;
    public double end;
    public bool hasEnded;
    public byte channel;
    public byte key;
    public byte vel;
    public bool delete;
    public object? meta;
    public int track;
    public NoteColor color = new();
}

public class NoteColor
{
    public Color4 left = Color4.White;
    public Color4 right = Color4.White;
    public bool isDefault = true;
}

public class TimeSignature
{
    public int numerator { get; set; } = 4;
    public int denominator { get; set; } = 4;
}

public class MidiInfo
{
    public int division;
    public int trackCount;
    public long noteCount;
    public int firstTempo;
    public long tickLength;
    public double secondsLength;
    public TimeSignature timeSig = new();
}
