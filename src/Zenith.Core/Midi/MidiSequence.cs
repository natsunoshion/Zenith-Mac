using System.Buffers.Binary;
using System.Text;

namespace Zenith.Core.Midi;

/// <summary>SMF format 0/1 reader and indexed, immutable playback timeline.</summary>
public sealed class MidiSequence
{
    const int IndexBlockSize = 128;
    readonly double[] blockMaxEnd;
    readonly int blockTreeBase;
    readonly Dictionary<int, MidiColorEvent[]> colorIndex;
    public string? FilePath { get; }
    public ushort Format { get; }
    public ushort Division => TempoMap.Division;
    public TempoMap TempoMap { get; }
    public MidiNote[] Notes { get; }
    public MidiEvent[] Events { get; }
    public MidiTrackInfo[] Tracks { get; }
    public MidiColorEvent[] ColorEvents { get; }
    public MidiTimeSignature[] TimeSignatures { get; }
    public MidiText[] TextEvents { get; }
    public long LengthTicks { get; }
    public double DurationSeconds { get; }
    public long NoteCount => Notes.LongLength;

    MidiSequence(string? path, ushort format, ushort division, List<MidiTrackInfo> tracks,
        List<MidiNote> notes, List<MidiEvent> events, List<MidiTempo> tempos,
        List<MidiColorEvent> colors, List<MidiTimeSignature> signatures, List<MidiText> texts)
    {
        FilePath = path;
        Format = format;
        Tracks = tracks.ToArray();
        TempoMap = new TempoMap(division, tempos);
        LengthTicks = tracks.Count == 0 ? 0 : tracks.Max(t => t.EndTick);
        DurationSeconds = TempoMap.TickToSeconds(LengthTicks);
        // List.Sort avoids the second full-size copy of a LINQ OrderBy over black MIDI notes.
        notes.Sort((a, b) =>
        {
            int c = a.StartTick.CompareTo(b.StartTick);
            if (c == 0) c = a.Track.CompareTo(b.Track);
            return c != 0 ? c : a.SequenceOrder.CompareTo(b.SequenceOrder);
        });
        Notes = notes.ToArray();
        events.Sort((a, b) => { int c = a.Tick.CompareTo(b.Tick); if (c == 0) c = a.Track.CompareTo(b.Track); return c != 0 ? c : a.Order.CompareTo(b.Order); });
        for (int i = 0; i < events.Count; i++) events[i] = events[i] with { Seconds = TempoMap.TickToSeconds(events[i].Tick) };
        Events = events.ToArray();
        ResolveSoundingDurations();
        foreach (var n in Notes)
        {
            n.StartSeconds = TempoMap.TickToSeconds(n.StartTick);
            n.EndSeconds = TempoMap.TickToSeconds(n.EndTick);
            n.SoundEndSeconds = TempoMap.TickToSeconds(n.SoundEndTick);
            n.SoundReleaseSeconds = TempoMap.TickToSeconds(n.SoundReleaseTick);
        }
        ColorEvents = colors.OrderBy(c => c.Tick).ThenBy(c => c.Track)
            .Select(c => c with { Seconds = TempoMap.TickToSeconds(c.Tick) }).ToArray();
        TimeSignatures = signatures.OrderBy(s => s.Tick).ToArray();
        TextEvents = texts.OrderBy(t => t.Tick).ToArray();
        var indexedColors = new Dictionary<int, List<MidiColorEvent>>();
        foreach (var color in ColorEvents)
        {
            int first = color.Channel == 127 ? 0 : color.Channel;
            int last = color.Channel == 127 ? 15 : color.Channel;
            for (int channel = first; channel <= last; channel++)
            {
                int key = color.Track * 16 + channel;
                if (!indexedColors.TryGetValue(key, out var list)) indexedColors[key] = list = [];
                list.Add(color);
            }
        }
        colorIndex = indexedColors.ToDictionary(p => p.Key, p => p.Value.ToArray());
        blockTreeBase = 1;
        int blockCount = (Notes.Length + IndexBlockSize - 1) / IndexBlockSize;
        while (blockTreeBase < blockCount) blockTreeBase *= 2;
        blockMaxEnd = new double[blockTreeBase * 2];
        Array.Fill(blockMaxEnd, double.NegativeInfinity);
        for (int i = 0; i < Notes.Length; i++)
        {
            int index = blockTreeBase + i / IndexBlockSize;
            blockMaxEnd[index] = Math.Max(blockMaxEnd[index], Math.Max(Notes[i].EndSeconds, Notes[i].SoundEndSeconds));
        }
        for (int i = blockTreeBase - 1; i > 0; i--) blockMaxEnd[i] = Math.Max(blockMaxEnd[2 * i], blockMaxEnd[2 * i + 1]);
    }

    public static MidiSequence Load(string path, MidiLoadOptions? options = null)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Load(stream, options, Path.GetFullPath(path));
    }

    public static Task<MidiSequence> LoadAsync(string path, MidiLoadOptions? options = null) =>
        Task.Run(() => Load(path, options), options?.CancellationToken ?? default);

    public static MidiSequence Load(Stream stream, MidiLoadOptions? options = null, string? filePath = null)
    {
        options ??= new();
        if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("A readable, seekable MIDI stream is required.", nameof(stream));
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (ReadFourCc(reader) != "MThd") throw new InvalidDataException("Not a Standard MIDI File: missing MThd header.");
        uint headerLength = ReadUInt32(reader);
        if (headerLength < 6 || headerLength > stream.Length - stream.Position) throw new InvalidDataException("Invalid MIDI header size.");
        ushort format = ReadUInt16(reader), trackCount = ReadUInt16(reader), division = ReadUInt16(reader);
        if (format > 1) throw new NotSupportedException("As in Zenith, independent sequences (SMF format 2) are not supported.");
        if (trackCount == 0 || (format == 0 && trackCount != 1)) throw new InvalidDataException("Invalid track count for MIDI format.");
        _ = new TempoMap(division, []); // Validate before parsing potentially large tracks.
        stream.Position += headerLength - 6;
        var tracks = new List<MidiTrackInfo>();
        var notes = new List<MidiNote>();
        var events = new List<MidiEvent>();
        var tempos = new List<MidiTempo>();
        var colors = new List<MidiColorEvent>();
        var signatures = new List<MidiTimeSignature>();
        var texts = new List<MidiText>();
        while (stream.Position < stream.Length && tracks.Count < trackCount)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            if (stream.Length - stream.Position < 8) throw new InvalidDataException("Truncated MIDI chunk header.");
            string chunk = ReadFourCc(reader);
            uint length = ReadUInt32(reader);
            long end = checked(stream.Position + length);
            if (end > stream.Length) throw new InvalidDataException($"Truncated {chunk} chunk.");
            if (chunk == "MTrk")
            {
                var info = new MidiTrackInfo { Index = tracks.Count };
                var parser = new TrackParser(reader, end, info, notes, events, tempos, colors, signatures, texts, options);
                try { parser.Parse(); }
                catch (Exception ex) when (ex is EndOfStreamException or OverflowException)
                { throw new InvalidDataException($"Truncated or invalid MIDI track {info.Index + 1} at byte {stream.Position}.", ex); }
                tracks.Add(info);
            }
            stream.Position = end;
            options.Progress?.Report((double)stream.Position / stream.Length * .9);
        }
        if (tracks.Count != trackCount) throw new InvalidDataException($"Header declares {trackCount} tracks; found {tracks.Count}.");
        options.CancellationToken.ThrowIfCancellationRequested();
        var result = new MidiSequence(filePath, format, division, tracks, notes, events, tempos, colors, signatures, texts);
        options.Progress?.Report(1);
        return result;
    }

    static string ReadFourCc(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));
    static uint ReadUInt32(BinaryReader reader) => BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
    static ushort ReadUInt16(BinaryReader reader) => BinaryPrimitives.ReverseEndianness(reader.ReadUInt16());

    /// <summary>Only visits intersecting index blocks, so preview cost depends on visible notes rather than file duration.</summary>
    public IEnumerable<MidiNote> QueryNotes(double startSeconds, double endSeconds, bool includeSustain = false)
    {
        if (double.IsNaN(startSeconds) || double.IsNaN(endSeconds) || endSeconds < startSeconds) yield break;
        foreach (var note in QueryNode(1, 0, blockTreeBase, startSeconds, endSeconds, includeSustain)) yield return note;
    }

    IEnumerable<MidiNote> QueryNode(int node, int firstBlock, int afterBlock, double start, double end, bool sustain)
    {
        int first = firstBlock * IndexBlockSize;
        if (first >= Notes.Length || blockMaxEnd[node] < start || Notes[first].StartSeconds > end) yield break;
        if (node >= blockTreeBase)
        {
            int after = Math.Min(first + IndexBlockSize, Notes.Length);
            for (int i = first; i < after; i++)
            {
                var n = Notes[i];
                if (n.StartSeconds > end) break;
                if ((sustain ? n.SoundEndSeconds : n.EndSeconds) >= start) yield return n;
            }
        }
        else
        {
            int middle = firstBlock + (afterBlock - firstBlock) / 2;
            foreach (var n in QueryNode(node * 2, firstBlock, middle, start, end, sustain)) yield return n;
            foreach (var n in QueryNode(node * 2 + 1, middle, afterBlock, start, end, sustain)) yield return n;
        }
    }

    public long NotesStartedAt(double seconds)
    {
        int lo = 0, hi = Notes.Length;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (Notes[mid].StartSeconds <= seconds) lo = mid + 1; else hi = mid; }
        return lo;
    }

    public int FindEventIndex(double seconds)
    {
        int lo = 0, hi = Events.Length;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (Events[mid].Seconds < seconds) lo = mid + 1; else hi = mid; }
        return lo;
    }

    /// <summary>Zenith color meta events recolor a whole track/channel, including notes already visible.</summary>
    public MidiNoteColor? GetEmbeddedColor(int track, int channel, double seconds)
    {
        if (!colorIndex.TryGetValue(track * 16 + channel, out var colors)) return null;
        int lo = 0, hi = colors.Length;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (colors[mid].Seconds <= seconds) lo = mid + 1; else hi = mid; }
        return lo == 0 ? null : colors[lo - 1].Color;
    }

    // Pedals and channel-mode messages affect a MIDI channel across every track.
    // Visual EndTick deliberately remains the original Zenith note-off/EOT length.
    void ResolveSoundingDurations()
    {
        var held = new Dictionary<int, Queue<MidiNote>>();
        var released = Enumerable.Range(0, 16).Select(_ => new List<MidiNote>()).ToArray();
        var sostenuto = Enumerable.Range(0, 16).Select(_ => new HashSet<MidiNote>()).ToArray();
        bool[] sustainDown = new bool[16], sostenutoDown = new bool[16];
        int noteCursor = 0;
        long tick = 0;
        void Release(MidiNote note)
        {
            if (note.SoundKeyReleased) return;
            note.SoundKeyReleased = true;
            note.SoundReleaseTick = tick;
            if (sustainDown[note.Channel] || sostenuto[note.Channel].Contains(note)) released[note.Channel].Add(note);
            else note.SoundEndTick = tick;
        }
        void FinishReleased(int channel)
        {
            if (sustainDown[channel]) return;
            var list = released[channel];
            int write = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var note = list[i];
                if (sostenuto[channel].Contains(note)) list[write++] = note;
                else note.SoundEndTick = tick;
            }
            if (write < list.Count) list.RemoveRange(write, list.Count - write);
        }
        IEnumerable<MidiNote> HeldOnChannel(int channel) => held.Values.SelectMany(queue => queue).Where(note => note.Channel == channel);
        foreach (var message in Events)
        {
            tick = message.Tick;
            int channel = message.Channel;
            int key = message.Track * 2048 + channel * 128 + message.Data1;
            if (message.IsNoteOn)
            {
                var note = Notes[noteCursor++];
                if (!held.TryGetValue(key, out var queue)) held.Add(key, queue = new());
                queue.Enqueue(note);
            }
            else if (message.IsNoteOff)
            {
                if (held.TryGetValue(key, out var queue) && queue.Count != 0)
                {
                    Release(queue.Dequeue());
                }
            }
            else if (message.Command == 0xB0)
            {
                switch (message.Data1)
                {
                    case 64:
                        sustainDown[channel] = message.Data2 >= 64;
                        FinishReleased(channel); break;
                    case 66:
                        bool down = message.Data2 >= 64;
                        if (down && !sostenutoDown[channel])
                            foreach (var note in HeldOnChannel(channel))
                                if (!note.SoundKeyReleased) sostenuto[channel].Add(note);
                        if (!down) { sostenuto[channel].Clear(); FinishReleased(channel); }
                        sostenutoDown[channel] = down; break;
                    case 120:
                        foreach (var note in HeldOnChannel(channel))
                            if (note.SoundEndTick < 0) { note.SoundKeyReleased = true; note.SoundReleaseTick = Math.Min(note.SoundReleaseTick < 0 ? tick : note.SoundReleaseTick, tick); note.SoundEndTick = tick; }
                        foreach (var note in released[channel]) note.SoundEndTick = tick;
                        released[channel].Clear(); sostenuto[channel].Clear(); break;
                    case 121:
                        sustainDown[channel] = sostenutoDown[channel] = false;
                        sostenuto[channel].Clear(); FinishReleased(channel); break;
                    case >= 123 and <= 127:
                        foreach (var note in HeldOnChannel(channel)) Release(note);
                        break;
                }
            }
        }
        foreach (var note in Notes)
        {
            if (note.SoundEndTick < 0) note.SoundEndTick = LengthTicks;
            if (note.SoundReleaseTick < 0) note.SoundReleaseTick = LengthTicks;
        }
    }

    sealed class TrackParser(BinaryReader reader, long end, MidiTrackInfo info, List<MidiNote> notes,
        List<MidiEvent> events, List<MidiTempo> tempos, List<MidiColorEvent> colors,
        List<MidiTimeSignature> signatures, List<MidiText> texts, MidiLoadOptions options)
    {
        readonly Queue<MidiNote>?[] held = new Queue<MidiNote>?[16 * 128];
        long tick, order;
        byte runningStatus;

        byte ReadByte()
        {
            if (reader.BaseStream.Position >= end) throw new EndOfStreamException();
            return reader.ReadByte();
        }
        byte ReadData()
        {
            byte b = ReadByte();
            if (b >= 128) throw new InvalidDataException($"Invalid MIDI data byte 0x{b:X2} in track {info.Index + 1}.");
            return b;
        }
        int ReadVariable()
        {
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                byte b = ReadByte(); value = (value << 7) | (b & 127);
                if (b < 128) return value;
            }
            throw new InvalidDataException("MIDI variable-length integer exceeds four bytes.");
        }
        byte[] ReadPayload(int size)
        {
            if (size > end - reader.BaseStream.Position) throw new EndOfStreamException();
            var bytes = reader.ReadBytes(size);
            if (bytes.Length != size) throw new EndOfStreamException();
            return bytes;
        }

        public void Parse()
        {
            bool ended = false;
            while (reader.BaseStream.Position < end && !ended)
            {
                if ((order & 4095) == 0)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    options.Progress?.Report((double)reader.BaseStream.Position / reader.BaseStream.Length * .9);
                }
                tick = checked(tick + ReadVariable());
                byte status = ReadByte();
                int firstData = -1;
                if (status < 128)
                {
                    if (runningStatus < 0x80 || runningStatus >= 0xF0) throw new InvalidDataException("MIDI running status has no preceding channel message.");
                    firstData = status; status = runningStatus;
                }
                if (status < 0xF0)
                {
                    runningStatus = status;
                    byte d1 = firstData < 0 ? ReadData() : (byte)firstData;
                    int command = status & 0xF0, channel = status & 15;
                    byte d2 = command is 0xC0 or 0xD0 ? (byte)0 : ReadData();
                    events.Add(new(tick, 0, info.Index, status, d1, d2, Order: order));
                    info.ChannelMask |= 1 << channel;
                    if (command == 0x90 && d2 > 0)
                    {
                        var note = new MidiNote { StartTick = tick, Key = d1, Velocity = d2, Channel = (byte)channel, Track = info.Index, SequenceOrder = order };
                        (held[channel * 128 + d1] ??= new()).Enqueue(note);
                        notes.Add(note); info.NoteCount++;
                    }
                    else if (command == 0x80 || command == 0x90) ReleaseKey(channel, d1, d2);
                }
                else if (status == 0xFF)
                {
                    runningStatus = 0;
                    byte type = ReadByte();
                    int size = ReadVariable();
                    if (size > end - reader.BaseStream.Position) throw new EndOfStreamException();
                    long after = reader.BaseStream.Position + size;
                    switch (type)
                    {
                        case 0x2F:
                            if (size != 0) throw new InvalidDataException("Invalid end-of-track event.");
                            ended = true; break;
                        case 0x51:
                            if (size != 3) throw new InvalidDataException("Invalid tempo event.");
                            int tempo = ReadByte() << 16 | ReadByte() << 8 | ReadByte();
                            if (tempo == 0) throw new InvalidDataException("MIDI tempo cannot be zero.");
                            tempos.Add(new(tick, tempo, info.Index, order)); break;
                        case 0x58:
                            if (size != 4) throw new InvalidDataException("Invalid time signature event.");
                            byte numerator = ReadByte(), denominator = ReadByte(), clocks = ReadByte(), thirtySeconds = ReadByte();
                            if (denominator > 30) throw new InvalidDataException("Invalid time signature denominator.");
                            signatures.Add(new(tick, numerator, 1 << denominator, clocks, thirtySeconds)); break;
                        case 0x0A:
                            if (size is 8 or 12)
                            {
                                byte[] bytes = ReadPayload(size);
                                if (bytes[0] == 0 && bytes[1] == 15 && (bytes[2] < 16 || bytes[2] == 127))
                                {
                                    var left = new MidiColor(bytes[4], bytes[5], bytes[6], bytes[7]);
                                    var right = size == 12 ? new MidiColor(bytes[8], bytes[9], bytes[10], bytes[11]) : left;
                                    colors.Add(new(tick, 0, info.Index, bytes[2], new(left, right)));
                                }
                            }
                            break;
                        case >= 1 and <= 9:
                            if (options.PreserveText || type is 3 or 4)
                            {
                                string text = Encoding.UTF8.GetString(ReadPayload(size));
                                if (type == 3) info.Name = text;
                                if (type == 4) info.Instrument = text;
                                if (options.PreserveText) texts.Add(new(tick, info.Index, type, text));
                            }
                            break;
                    }
                    reader.BaseStream.Position = after;
                }
                else if (status is 0xF0 or 0xF7)
                {
                    runningStatus = 0;
                    int size = ReadVariable();
                    if (size > end - reader.BaseStream.Position) throw new EndOfStreamException();
                    if (options.PreserveSystemExclusive) events.Add(new(tick, 0, info.Index, status, 0, 0, ReadPayload(size), order));
                    else reader.BaseStream.Position += size;
                }
                else throw new InvalidDataException($"Unexpected SMF status 0x{status:X2} in track {info.Index + 1}.");
                order++;
            }
            for (int channel = 0; channel < 16; channel++)
                for (int key = 0; key < 128; key++)
                    while (held[channel * 128 + key] is { Count: > 0 }) ReleaseKey(channel, key, 0);
            info.EndTick = tick;
        }

        void ReleaseKey(int channel, int key, byte velocity)
        {
            var queue = held[channel * 128 + key];
            if (queue == null || queue.Count == 0) return;
            var note = queue.Dequeue(); // Zenith pairs repeated note-on events in FIFO order.
            note.EndTick = tick;
            note.ReleaseVelocity = velocity;
        }
    }
}
