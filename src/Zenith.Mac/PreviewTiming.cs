using Zenith.Core.Midi;

namespace Zenith.Mac;

/// <summary>Timeline rules from the original RenderWindow, independent of presentation cadence.</summary>
internal static class PreviewTiming
{
    public static double InitialSeconds(TempoMap tempo, double noteScreenTime, bool timeBasedNotes)
    {
        if (!double.IsFinite(noteScreenTime)) throw new InvalidOperationException("The renderer supplied an invalid note screen time.");
        // StartButton_Click sets renderSecondsDelay to zero for preview. Only
        // the renderer's pre-initialization NoteScreenTime contributes here.
        return timeBasedNotes ? -noteScreenTime / 1000 : tempo.TickToSeconds(-noteScreenTime);
    }

    public static double StepSeconds(double elapsedSeconds, bool realtimePlayback, int framesPerSecond)
    {
        if (framesPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        if (!realtimePlayback) return 1d / framesPerSecond;
        // Upstream caps mv at settings.fps / 4 (integer division), then
        // advances by mv nominal frames. At 30 fps this is 7/30 s, not 1/4 s.
        return Math.Clamp(elapsedSeconds, 0, (framesPerSecond / 4) / (double)framesPerSecond);
    }

    public static (double StartSeconds, double AudioOffsetSeconds) ExportStart(TempoMap tempo, double noteScreenTime,
        bool timeBasedNotes, int framesPerSecond, double delaySeconds)
    {
        // RenderWindow's constructor uses integer 1000000 / fps in tick mode;
        // its FFmpeg audio offset also uses that step, then rounds to 2 places.
        double firstTempo = 60_000_000 / tempo.BeatsPerMinuteAt(0);
        double tickStep = tempo.Division / firstTempo * (1_000_000 / framesPerSecond);
        double step = timeBasedNotes ? 1000d / framesPerSecond : tickStep;
        double startUnits = -noteScreenTime - step * delaySeconds * framesPerSecond;
        double startSeconds = timeBasedNotes ? startUnits / 1000 : tempo.TickToSeconds(startUnits);
        double offset = Math.Round(-startUnits / tickStep / framesPerSecond * 100) / 100;
        return (startSeconds, offset);
    }

    public static double Advance(PlaybackController playback, double timelineSeconds, double elapsedSeconds, bool paused)
    {
        if (paused)
        {
            if (playback.IsPlaying) playback.Pause();
            return timelineSeconds;
        }
        timelineSeconds += elapsedSeconds * playback.Speed;
        if (timelineSeconds >= 0)
        {
            if (!playback.IsPlaying && playback.PositionSeconds < playback.Sequence.DurationSeconds) playback.Play();
            // A frame crossing MIDI zero advances audio only by its positive
            // tail. The negative part is visible preroll, with no MIDI output.
            double audioStep = (Math.Min(timelineSeconds, playback.Sequence.DurationSeconds) - playback.PositionSeconds) / playback.Speed;
            if (audioStep > 0) playback.Advance(audioStep);
        }
        return timelineSeconds;
    }
}
