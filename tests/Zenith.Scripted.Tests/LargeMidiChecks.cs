using System.Buffers.Binary;
using System.Diagnostics;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using ZenithEngine;

internal static class LargeMidiChecks
{
    internal static void Run(string temp, Action<bool, string> assert)
    {
        // Consecutive removals exercise the original iterator's detached-node
        // corner case, which matters once the display list persists across frames.
        var list = new FastList<int>();
        for (var i = 0; i < 10_000; i++) list.Add(i);
        var iterator = list.Iterate();
        while (iterator.MoveNext(out var value)) if (value % 10 < 7) iterator.Remove();
        assert(list.Count() == 3000 && list.All(x => x % 10 >= 7), "FastList removes consecutive head, middle, and tail nodes");
        iterator.Reset();
        while (iterator.MoveNext(out _)) iterator.Remove();
        assert(list.ZeroLen, "FastList can remove every node in one traversal");
        list.Add(42);
        assert(list.Count() == 1 && list.First == 42, "FastList appends correctly after removing the old tail");

        const int noteCount = 100_000;
        var path = Path.Combine(temp, "100000-notes.mid");
        WriteMidi(path, noteCount);
        var midi = MidiSequence.Load(path);
        assert(midi.NoteCount == noteCount && midi.Notes[^1].EndTick == 40_000, "Parses the generated 100,000-note SMF fixture");
        if (!OperatingSystem.IsMacOS()) return;

        var probe = new Probe { NoteScreenTime = 1920 };
        using (var scene = new SceneRenderer(midi, new RenderSettings { width = 16, height = 16 }, plugin: probe))
        {
            double horizon = 0;
            var watch = Stopwatch.StartNew();
            for (var frame = 0; frame < 100; frame++)
            {
                var tick = 12_000 + frame * 16;
                if (frame == 20) probe.NoteScreenTime = 3840;
                if (frame == 40) probe.NoteScreenTime = 960;
                horizon = Math.Max(horizon, tick + probe.NoteScreenTime);
                scene.Render(midi.TempoMap.TickToSeconds(tick));
                var expected = midi.QueryNotes(midi.TempoMap.TickToSeconds(tick), midi.TempoMap.TickToSeconds(horizon)).LongCount();
                assert(probe.LastNoteCount == expected, "100k MIDI cursor and collector preserve note horizon at frame " + frame);
            }
            watch.Stop();
            assert(probe.Frames == 100 && probe.PersistedVisits > 100_000, "Persistent Note.meta objects survive repeated dense frames");
            // A paused frame adds no notes: its allocations must not scale with
            // the number of visible notes. This catches per-frame list rebuilding.
            var paused = midi.TempoMap.TickToSeconds(12_000 + 99 * 16);
            scene.Render(paused);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 20; i++) scene.Render(paused);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            assert(allocated < 256_000, "Paused dense frames avoid note-count-proportional collection allocations");
            Console.WriteLine($"PASS 100,000-note scene: 100 ordered frames in {watch.ElapsedMilliseconds} ms; 20 paused frames allocate {allocated:N0} bytes");
        }

        var manual = new Probe { ManualNoteDelete = true, NoteScreenTime = 960, MarkDelete = n => n.key < 65 };
        using (var scene = new SceneRenderer(midi, new RenderSettings { width = 16, height = 16 }, plugin: manual))
        {
            const int tick = 12_000;
            var seconds = midi.TempoMap.TickToSeconds(tick);
            scene.Render(seconds);
            var initialCount = manual.LastNoteCount;
            assert(initialCount == midi.Notes.LongCount(n => n.StartTick <= tick + 960), "Manual mode retains past notes on the first seek frame");
            manual.MarkDelete = null;
            scene.Render(seconds);
            assert(manual.LastNoteCount * 2 == initialCount, "Manual collector deletes adjacent flagged notes without losing unflagged identities");
            scene.Render(seconds);
            assert(manual.LastNoteCount * 2 == initialCount, "Manual deletion does not rewind the MIDI cursor or resurrect notes");
        }
    }

    private static void WriteMidi(string path, int count)
    {
        using var track = new MemoryStream();
        for (var group = 0; group < count / 10; group++)
        {
            for (var key = 60; key < 70; key++) track.Write([0, 0x90, (byte)key, 100]);
            for (var key = 60; key < 70; key++) track.Write([(byte)(key == 60 ? 4 : 0), 0x80, (byte)key, 0]);
        }
        track.Write([0, 255, 47, 0]);
        using var file = File.Create(path);
        file.Write("MThd"u8); file.Write([0, 0, 0, 6, 0, 0, 0, 1, 1, 224]);
        file.Write("MTrk"u8);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, checked((int)track.Length));
        file.Write(length); track.Position = 0; track.CopyTo(file);
    }

    private sealed class Probe : IPluginRender
    {
        private FastList<Note>? firstList;
        private sealed record Identity(Note Note);
        public int Frames { get; private set; }
        public long PersistedVisits { get; private set; }
        public Func<Note, bool>? MarkDelete { get; set; }
        public string Name => "Note lifetime probe";
        public string Description => Name;
        public bool Initialized { get; private set; }
        public object PreviewImage => null!;
        public bool ManualNoteDelete { get; set; }
        public double NoteCollectorOffset => 0;
        public NoteColor[][] NoteColors { private get; set; } = [];
        public double Tempo { private get; set; }
        public MidiInfo CurrentMidi { private get; set; } = new();
        public string LanguageDictName => "test";
        public double NoteScreenTime { get; set; }
        public long LastNoteCount { get; private set; }
        public object SettingsControl => null!;
        public void Init() => Initialized = true;
        public void ReloadTrackColors() { }
        public void Dispose() => Initialized = false;
        public void RenderFrame(FastList<Note> notes, double midiTime, int finalCompositeBuff)
        {
            firstList ??= notes;
            if (!ReferenceEquals(firstList, notes)) throw new Exception("Scene rebuilt its FastList between frames.");
            var previous = double.NegativeInfinity;
            LastNoteCount = 0;
            foreach (var note in notes)
            {
                if (note.start < previous || note.start % 4 != 0) throw new Exception("Note cursor order or integer tick precision changed.");
                previous = note.start;
                if (note.meta is Identity identity)
                {
                    if (!ReferenceEquals(identity.Note, note)) throw new Exception("Scene replaced a live Note identity.");
                    PersistedVisits++;
                }
                else note.meta = new Identity(note);
                // The normal collector must ignore this flag; manual mode tests
                // mark five adjacent notes in each ten-note batch for deletion.
                if (!ManualNoteDelete || MarkDelete?.Invoke(note) == true) note.delete = true;
                LastNoteCount++;
            }
            Frames++;
        }
    }
}
