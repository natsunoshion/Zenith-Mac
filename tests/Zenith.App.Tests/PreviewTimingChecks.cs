using Zenith.Core.Midi;
using Zenith.Mac;

internal static class PreviewTimingChecks
{
    public static void Run()
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception("Preview timing: " + message);
        }
        void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 1e-9, message);
        var tempo = new TempoMap(480, [new MidiTempo(0, 750000)]);
        Near(PreviewTiming.InitialSeconds(tempo, 960, false), -1.5, "tick preroll uses the initial MIDI tempo");
        Near(PreviewTiming.InitialSeconds(tempo, 2000, true), -2, "time-based preroll uses milliseconds");
        Near(PreviewTiming.InitialSeconds(tempo, 0, false), 0, "a new Scripted renderer's zero screen-time cache starts at zero");
        Near(PreviewTiming.StepSeconds(.01, true, 60), .01, "realtime progress follows elapsed time");
        Near(PreviewTiming.StepSeconds(1, true, 60), .25, "realtime stalls use the upstream frame cap");
        Near(PreviewTiming.StepSeconds(1, true, 30), 7d / 30, "upstream fps / 4 is integer division");
        Near(PreviewTiming.StepSeconds(.001, false, 30), 1d / 30, "fixed-frame progress is independent of elapsed time");
        Near(PreviewTiming.StepSeconds(2, false, 30), 1d / 30, "fixed-frame progress also ignores slow presentation");
        var tickExport = PreviewTiming.ExportStart(tempo, 960, false, 60, 2);
        Near(tickExport.StartSeconds, -3.49992, "tick export includes screen preroll and the original quantized delay");
        Near(tickExport.AudioOffsetSeconds, 3.5, "audio alignment includes screen preroll and rounds as upstream does");
        var timeExport = PreviewTiming.ExportStart(tempo, 2000, true, 60, 2);
        Near(timeExport.StartSeconds, -4, "time-based export adds screen milliseconds and delay seconds");
        Near(timeExport.AudioOffsetSeconds, 6.25, "time-based export preserves the original tick-based audio-offset calculation");

        byte[] track = [0, 0x90, 60, 100, 0x83, 0x60, 0x80, 60, 0, 0x83, 0x60, 0xFF, 0x2F, 0];
        using var bytes = new MemoryStream();
        bytes.Write("MThd"u8); bytes.Write([0, 0, 0, 6, 0, 0, 0, 1, 1, 224]);
        bytes.Write("MTrk"u8); bytes.Write([0, 0, 0, (byte)track.Length]); bytes.Write(track); bytes.Position = 0;
        var midi = MidiSequence.Load(bytes);
        var output = new RecordingOutput();
        using var playback = new PlaybackController(midi, output, realtime: false);
        double position = -.5;
        position = PreviewTiming.Advance(playback, position, .1, paused: true);
        Near(position, -.5, "paused preview keeps the negative first frame");
        Check(!playback.IsPlaying && output.Messages.Count == 0, "paused preroll does not start MIDI");
        position = PreviewTiming.Advance(playback, position, .4, paused: false);
        Near(position, -.1, "negative visual time advances normally");
        Check(!playback.IsPlaying && output.Messages.Count == 0, "moving preroll remains silent");
        position = PreviewTiming.Advance(playback, position, .2, paused: false);
        Near(position, .1, "crossing zero retains the positive frame tail");
        Near(playback.PositionSeconds, .1, "audio clock matches the positive tail");
        Check(output.Messages.Count(m => m.IsNoteOn) == 1, "MIDI zero dispatches its initial note once");
        playback.Speed = 2;
        position = PreviewTiming.Advance(playback, position, .1, paused: false);
        Near(position, .3, "tempo multiplier applies once to visual time");
        Near(playback.PositionSeconds, .3, "tempo multiplier applies once to audio time");
        position = PreviewTiming.Advance(playback, position, .2, paused: true);
        Near(position, .3, "pause holds positive visual time");
        Check(!playback.IsPlaying, "pause stops active audio playback");
        position = PreviewTiming.Advance(playback, position, .05, paused: false);
        Near(position, .4, "resume continues the held timeline");
        Near(playback.PositionSeconds, .4, "resume keeps audio aligned");
        position = PreviewTiming.Advance(playback, position, .5, paused: false);
        Near(position, 1.4, "visual time continues past MIDI end for tail rendering");
        Near(playback.PositionSeconds, 1, "audio stops at MIDI end while visual tail continues");
        Check(!playback.IsPlaying, "tail frames do not restart audio");

        var completion = new RenderCompletion(midi, false, 60);
        bool remainedOpen = true;
        for (int i = 0; i < 500; i++) remainedOpen &= !completion.Observe(0, 0, 0);
        Check(remainedOpen, "blank frames cannot finish before parser lookahead reaches track end");
        Check(!completion.Observe(0, 960, 0), "parser reaching track end starts the empty-frame counter");
        remainedOpen = true;
        for (int i = 1; i < 299; i++) remainedOpen &= !completion.Observe(0, 0, 0);
        Check(remainedOpen, "parsed state persists after screen time shrinks");
        Check(!completion.Observe(0, 0, 2), "a visible note resets the consecutive-empty-frame counter");
        remainedOpen = true;
        for (int i = 0; i < 299; i++) remainedOpen &= !completion.Observe(2, 0, 0);
        Check(remainedOpen, "fewer than five nominal seconds of empty frames does not complete");
        Check(completion.Observe(2, 0, 0), "exactly fps times five consecutive empty frames completes");
        Console.WriteLine($"PASS {assertions} preview preroll, pause, zero crossing, speed, and original frame-step assertions");
    }

    sealed class RecordingOutput : IMidiOutput
    {
        public List<MidiEvent> Messages { get; } = [];
        public void Send(MidiEvent message) => Messages.Add(message);
        public void Reset() { }
        public void Dispose() { }
    }
}
