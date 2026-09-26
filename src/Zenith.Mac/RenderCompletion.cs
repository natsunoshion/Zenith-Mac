using Zenith.Core.Midi;

namespace Zenith.Mac;

/// <summary>The original parser lookahead and consecutive empty-render-frame exit condition.</summary>
internal sealed class RenderCompletion(MidiSequence sequence, bool timeBasedNotes, int framesPerSecond)
{
    bool tracksParsed;
    long emptyFrames;
    public bool Complete => tracksParsed && emptyFrames >= framesPerSecond * 5L;

    public bool Observe(double seconds, double noteScreenTime, long lastNoteCount,
        double tempoMultiplier = 1, double previousFrameMultiplier = 1)
    {
        if (!tracksParsed)
        {
            double position = timeBasedNotes ? seconds * 1000 : sequence.TempoMap.SecondsToTick(seconds);
            double unitsPerSecond = timeBasedNotes ? 1000 : sequence.TempoMap.IsSmpte
                ? sequence.TempoMap.SecondsToTick(seconds + 1) - sequence.TempoMap.SecondsToTick(seconds)
                : sequence.Division * sequence.TempoMap.BeatsPerMinuteAt(Math.Max(0, seconds)) / 60;
            // MainWindow.tryToParse parses twenty nominal frames beyond the
            // visible horizon; parsing never rewinds when screen time shrinks.
            double horizon = position + noteScreenTime + unitsPerSecond / framesPerSecond * 20
                * tempoMultiplier * Math.Max(previousFrameMultiplier, 1);
            double end = timeBasedNotes ? sequence.DurationSeconds * 1000 : sequence.LengthTicks;
            tracksParsed = horizon >= end;
        }
        emptyFrames = tracksParsed && lastNoteCount == 0 ? emptyFrames + 1 : 0;
        return Complete;
    }
}
