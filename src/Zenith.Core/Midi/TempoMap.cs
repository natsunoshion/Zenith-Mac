namespace Zenith.Core.Midi;

/// <summary>Piecewise exact conversion between SMF ticks and seconds, including SMPTE time division.</summary>
public sealed class TempoMap
{
    readonly Segment[] segments;
    readonly double smpteSecondsPerTick;
    public ushort Division { get; }
    public bool IsSmpte => (Division & 0x8000) != 0;
    public int TicksPerQuarter => IsSmpte ? 0 : Division;
    public IReadOnlyList<MidiTempo> Tempos { get; }
    readonly record struct Segment(long Tick, double Seconds, int Tempo, double SecondsPerTick);

    public TempoMap(ushort division, IEnumerable<MidiTempo> tempos)
    {
        Division = division;
        if (division == 0) throw new InvalidDataException("MIDI division cannot be zero.");
        var ordered = tempos.OrderBy(t => t.Tick).ThenBy(t => t.Track).ThenBy(t => t.Order).ToArray();
        if (ordered.Any(t => t.Tick < 0 || t.MicrosecondsPerQuarter <= 0))
            throw new InvalidDataException("Invalid MIDI tempo.");
        Tempos = ordered;
        if (IsSmpte)
        {
            var frames = -(sbyte)(division >> 8);
            var ticksPerFrame = division & 255;
            if (frames is not (24 or 25 or 29 or 30) || ticksPerFrame == 0)
                throw new InvalidDataException("Invalid SMPTE division.");
            smpteSecondsPerTick = 1.0 / ((frames == 29 ? 30000.0 / 1001.0 : frames) * ticksPerFrame);
            segments = [new(0, 0, 500000, smpteSecondsPerTick)];
            return;
        }
        var list = new List<Segment> { new(0, 0, 500000, .5 / division) };
        foreach (var tempo in ordered)
        {
            var previous = list[^1];
            var seconds = previous.Seconds + (tempo.Tick - previous.Tick) * previous.SecondsPerTick;
            var segment = new Segment(tempo.Tick, seconds, tempo.MicrosecondsPerQuarter,
                tempo.MicrosecondsPerQuarter / (1_000_000.0 * division));
            if (previous.Tick == tempo.Tick) list[^1] = segment;
            else list.Add(segment);
        }
        segments = list.ToArray();
    }

    public double TickToSeconds(double tick)
    {
        if (IsSmpte) return tick * smpteSecondsPerTick;
        int lo = 0, hi = segments.Length;
        while (lo + 1 < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (segments[mid].Tick <= tick) lo = mid; else hi = mid;
        }
        var s = segments[lo];
        return s.Seconds + (tick - s.Tick) * s.SecondsPerTick;
    }

    public double SecondsToTick(double seconds)
    {
        if (IsSmpte) return seconds / smpteSecondsPerTick;
        int lo = 0, hi = segments.Length;
        while (lo + 1 < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (segments[mid].Seconds <= seconds) lo = mid; else hi = mid;
        }
        var s = segments[lo];
        return s.Tick + (seconds - s.Seconds) / s.SecondsPerTick;
    }

    public double BeatsPerMinuteAt(double seconds)
    {
        var tick = SecondsToTick(seconds);
        int lo = 0, hi = segments.Length;
        while (lo + 1 < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (segments[mid].Tick <= tick) lo = mid; else hi = mid;
        }
        return 60_000_000.0 / segments[lo].Tempo;
    }
}
