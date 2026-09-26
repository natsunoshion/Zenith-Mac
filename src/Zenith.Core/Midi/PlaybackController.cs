using System.Diagnostics;

namespace Zenith.Core.Midi;

public interface IMidiOutput : IDisposable
{
    void Send(MidiEvent message);
    void Reset();
}

public sealed class NullMidiOutput : IMidiOutput
{
    public void Send(MidiEvent message) { }
    public void Reset() { }
    public void Dispose() { }
}

/// <summary>Realtime MIDI scheduling, deterministic stepping, transport, and state restoration after seeking.</summary>
public sealed class PlaybackController : IDisposable
{
    readonly object sync = new();
    readonly IMidiOutput output;
    readonly bool realtime;
    readonly Timer timer;
    readonly MidiEvent[] stateEvents;
    readonly Stopwatch clock = new();
    int eventIndex;
    double position, speed = 1, lastClock;
    bool playing, muted, disposed;
    public MidiSequence Sequence { get; }
    public event Action? Ended;
    public event Action<Exception>? PlaybackError;
    public bool IsPlaying { get { lock (sync) return playing; } }
    public double PositionSeconds { get { lock (sync) return position; } }
    public double PositionTicks => Sequence.TempoMap.SecondsToTick(PositionSeconds);
    public double Speed
    {
        get { lock (sync) return speed; }
        set { if (!double.IsFinite(value) || value <= 0 || value > 1000) throw new ArgumentOutOfRangeException(nameof(value)); lock (sync) speed = value; }
    }
    public bool Muted
    {
        get { lock (sync) return muted; }
        set
        {
            lock (sync)
            {
                if (value == muted) return;
                muted = value;
                if (muted) output.Reset();
                else if (playing)
                {
                    eventIndex = Sequence.FindEventIndex(position);
                    RestoreState(retriggerNotes: true);
                    AdvanceCore(0);
                }
            }
        }
    }

    public PlaybackController(MidiSequence sequence, IMidiOutput? output = null, bool realtime = true)
    {
        Sequence = sequence;
        this.output = output ?? new NullMidiOutput();
        this.realtime = realtime;
        stateEvents = sequence.Events.Where(e => !e.IsNoteOn && !e.IsNoteOff && e.Command != 0xA0).ToArray();
        timer = new Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Play()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (playing) return;
            if (position >= Sequence.DurationSeconds) SeekCore(0);
            eventIndex = Sequence.FindEventIndex(position);
            RestoreState(retriggerNotes: true);
            playing = true;
            clock.Restart(); lastClock = 0;
            AdvanceCore(0);
            if (realtime) timer.Change(0, 2);
        }
    }

    public void Pause()
    {
        lock (sync)
        {
            if (disposed) return;
            playing = false; timer.Change(Timeout.Infinite, Timeout.Infinite);
            clock.Stop(); output.Reset();
        }
    }

    public void Stop() { lock (sync) { Pause(); SeekCore(0); } }

    public void Seek(double seconds)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            SeekCore(seconds);
            if (playing) RestoreState(retriggerNotes: true);
            clock.Restart(); lastClock = 0;
        }
    }

    void SeekCore(double seconds)
    {
        output.Reset();
        position = Math.Clamp(seconds, 0, Sequence.DurationSeconds);
        eventIndex = Sequence.FindEventIndex(position);
    }

    void RestoreState(bool retriggerNotes)
    {
        if (muted) return;
        output.Reset();
        var sostenutoAt = new double?[16];
        var sostenutoDown = new bool[16];
        foreach (var message in stateEvents)
        {
            if (message.Seconds >= position) break;
            if (message.Command == 0xB0 && message.Data1 == 66)
            {
                if (message.Data2 < 64) sostenutoAt[message.Channel] = null;
                else if (!sostenutoDown[message.Channel]) sostenutoAt[message.Channel] = message.Seconds;
                sostenutoDown[message.Channel] = message.Data2 >= 64;
            }
            else
            {
                if (message.Command == 0xB0 && message.Data1 is 120 or 121) sostenutoAt[message.Channel] = null;
                if (message.Command == 0xB0 && message.Data1 == 121) sostenutoDown[message.Channel] = false;
                output.Send(message);
            }
        }
        if (!retriggerNotes) return;
        var sounding = Sequence.QueryNotes(position, position, includeSustain: true)
            .Where(note => note.StartSeconds < position && note.SoundEndSeconds > position).ToArray();
        bool IsLatched(MidiNote note) => sostenutoAt[note.Channel] is { } latch && note.StartSeconds <= latch && note.SoundReleaseSeconds >= latch;
        void NoteOn(MidiNote note) => output.Send(new(0, position, note.Track, (byte)(0x90 | note.Channel), note.Key, note.Velocity));
        foreach (var note in sounding.Where(IsLatched)) NoteOn(note);
        for (int channel = 0; channel < 16; channel++)
            if (sostenutoDown[channel]) output.Send(new(0, position, 0, (byte)(0xB0 | channel), 66, 127));
        foreach (var note in sounding.Where(note => !IsLatched(note))) NoteOn(note);
        foreach (var note in sounding)
        {
            if (note.SoundReleaseSeconds < position)
                output.Send(new(0, position, note.Track, (byte)(0x80 | note.Channel), note.Key, note.ReleaseVelocity));
        }
    }

    /// <summary>Advances a manually clocked player. Elapsed time is wall time; Speed is applied once.</summary>
    public void Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        bool ended;
        lock (sync) ended = playing && AdvanceCore(elapsedSeconds);
        if (ended) Ended?.Invoke();
    }

    bool AdvanceCore(double elapsedSeconds)
    {
        position = Math.Min(Sequence.DurationSeconds, position + elapsedSeconds * speed);
        var events = Sequence.Events;
        while (eventIndex < events.Length && events[eventIndex].Seconds <= position)
        {
            if (!muted) output.Send(events[eventIndex]);
            eventIndex++;
        }
        if (position < Sequence.DurationSeconds) return false;
        playing = false; timer.Change(Timeout.Infinite, Timeout.Infinite); clock.Stop(); output.Reset();
        return true;
    }

    void Tick(object? state)
    {
        bool ended = false;
        try
        {
            lock (sync)
            {
                if (!playing || disposed) return;
                double now = clock.Elapsed.TotalSeconds;
                double delta = now - lastClock; lastClock = now;
                ended = AdvanceCore(delta);
            }
            if (ended) Ended?.Invoke();
        }
        catch (Exception ex)
        {
            try { Pause(); } catch { }
            PlaybackError?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            Pause(); disposed = true; timer.Dispose(); output.Dispose();
        }
    }
}
