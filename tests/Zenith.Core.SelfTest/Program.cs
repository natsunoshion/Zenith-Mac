using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Zenith.Core.Export;
using Zenith.Core.Midi;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    passed++; Console.WriteLine("PASS: " + name);
}
void Near(double actual, double expected, string name) => Check(Math.Abs(actual - expected) < 1e-8, name);
MidiSequence Load(params byte[][] tracks) => MidiSequence.Load(new MemoryStream(Smf(480, tracks)));

var tempoTrack = Track((0, Meta(0x51, [7, 161, 32])), (480, Meta(0x51, [15, 66, 64])), (960, Meta(0x2F, [])));
var noteTrack = Track((0, Meta(3, Encoding.UTF8.GetBytes("Piano"))), (0, new byte[] { 0x90, 60, 100 }),
    (240, new byte[] { 0xB0, 64, 127 }), (480, new byte[] { 0x80, 60, 40 }), (720, new byte[] { 0xB0, 64, 0 }), (960, Meta(0x2F, [])));
var midi = Load(tempoTrack, noteTrack);
Check(midi.Notes.Length == 1 && midi.Tracks[1].Name == "Piano", "multi-track notes and names");
Near(midi.DurationSeconds, 1.5, "tempo map duration");
Near(midi.Notes[0].EndSeconds, .5, "physical note release");
Near(midi.Notes[0].SoundEndSeconds, 1, "sustain pedal duration across tempo change");
Near(midi.TempoMap.SecondsToTick(1), 720, "inverse tempo conversion");
Near(midi.TempoMap.TickToSeconds(-480), -.5, "negative preroll tempo conversion");
Near(midi.TempoMap.BeatsPerMinuteAt(.8), 60, "active tempo");
Check(midi.QueryNotes(.8, .8).Count() == 0 && midi.QueryNotes(.8, .8, true).Count() == 1, "visual versus sounding interval queries");

var running = Load(Track((0, new byte[] { 0x90, 60, 100 }), (120, new byte[] { 60, 90 }),
    (240, new byte[] { 60, 0 }), (360, new byte[] { 60, 0 }), (480, Meta(0x2F, []))));
Check(running.Notes.Length == 2 && running.Notes[0].EndTick == 240 && running.Notes[1].EndTick == 360, "running status, velocity-zero release, FIFO repeated notes");

var colors = Load(Track((0, Meta(10, [0, 15, 127, 0, 255, 0, 0, 255, 0, 0, 255, 128])),
    (240, Meta(10, [0, 15, 1, 0, 0, 255, 0, 255])), (480, Meta(0x2F, []))));
Check(colors.GetEmbeddedColor(0, 9, 0)?.Right.B == 255 && colors.GetEmbeddedColor(0, 9, 0)?.Right.A == 128,
    "Zenith 12-byte gradient and all-channel color event");
Check(colors.GetEmbeddedColor(0, 1, .3)?.Left.G == 255 && colors.GetEmbeddedColor(0, 9, .3)?.Left.R == 255,
    "Zenith 8-byte channel color change");

var sost = Load(Track((0, new byte[] { 0x90, 60, 100 }), (10, new byte[] { 0xB0, 66, 127 }),
    (20, new byte[] { 0x90, 64, 100 }), (30, new byte[] { 0x80, 60, 0 }), (40, new byte[] { 0x80, 64, 0 }),
    (50, new byte[] { 0xB0, 66, 0 }), (60, Meta(0x2F, []))));
Check(sost.Notes[0].SoundEndTick == 50 && sost.Notes[1].SoundEndTick == 40, "sostenuto latches only already-held notes");

var crossTrack = Load(Track((0, new byte[] { 0xB0, 64, 127 }), (100, new byte[] { 0xB0, 64, 0 }), (120, Meta(0x2F, []))),
    Track((10, new byte[] { 0x90, 60, 100 }), (40, new byte[] { 0x80, 60, 0 }), (120, Meta(0x2F, []))));
Check(crossTrack.Notes[0].EndTick == 40 && crossTrack.Notes[0].SoundEndTick == 100, "pedals affect a shared channel across MIDI tracks");
var channelMode = Load(Track((0, new byte[] { 0xB0, 64, 127 }), (10, new byte[] { 0x90, 60, 100 }),
    (20, new byte[] { 0xB0, 120, 0 }), (30, new byte[] { 0x90, 64, 100 }), (40, new byte[] { 0x80, 60, 0 }),
    (50, new byte[] { 0x80, 64, 0 }), (100, new byte[] { 0xB0, 64, 0 }), (120, Meta(0x2F, []))));
Check(channelMode.Notes[0].EndTick == 40 && channelMode.Notes[0].SoundEndTick == 20 && channelMode.Notes[1].SoundEndTick == 100,
    "all sound off retains visual note length and pedal state");
var notesOff = Load(Track((0, new byte[] { 0x90, 60, 100 }), (10, new byte[] { 0xB0, 64, 127 }),
    (20, new byte[] { 0xB0, 123, 0 }), (50, new byte[] { 0xB0, 64, 0 }), (100, new byte[] { 0x80, 60, 0 }), (120, Meta(0x2F, []))));
Check(notesOff.Notes[0].EndTick == 100 && notesOff.Notes[0].SoundReleaseTick == 20 && notesOff.Notes[0].SoundEndTick == 50,
    "all notes off respects sustain and preserves original Zenith visual length");

var closed = Load(Track((0, new byte[] { 0x90, 64, 100 }), (120, Meta(0x2F, []))));
Check(closed.Notes[0].EndTick == 120 && closed.Notes[0].SoundEndTick == 120, "unreleased note closes at track end");

var smpte = MidiSequence.Load(new MemoryStream(Smf(0xE728, Track((1000, Meta(0x2F, []))))));
Near(smpte.DurationSeconds, 1, "SMPTE 25fps x 40 ticks timing");
var dropFrame = new TempoMap(0xE350, []);
Near(dropFrame.TickToSeconds(2400000), 1001, "SMPTE 29.97 drop-frame timing");

var sysex = Load(Track((0, new byte[] { 0xF0, 5, 0x7E, 0x7F, 9, 1, 0xF7 }), (1, Meta(0x2F, []))));
Check(sysex.Events.Length == 1 && sysex.Events[0].Payload!.Length == 5, "length-prefixed SMF SysEx packet");
bool rejected = false;
try { Load(Track((0, new byte[] { 60, 100 }))); } catch (InvalidDataException) { rejected = true; }
Check(rejected, "invalid running status rejected");
rejected = false;
try { MidiSequence.Load(new MemoryStream(Smf(480, noteTrack)[..^1])); } catch (InvalidDataException) { rejected = true; }
Check(rejected, "truncated track rejected");

var output = new RecordingOutput();
using (var player = new PlaybackController(midi, output, realtime: false))
{
    player.Play(); player.Advance(.6);
    Check(output.Messages.Any(m => m.IsNoteOn) && output.Messages.Any(m => m.IsNoteOff), "playback scheduler delivers events");
    player.Seek(.75);
    Check(output.Messages.Count >= 3 && output.Messages[^1].IsNoteOff && output.Messages[^2].IsNoteOn,
        "seek restores controllers and sustained notes");
    player.Speed = 2; player.Advance(.1); Near(player.PositionSeconds, .95, "playback speed");
    player.Pause(); player.Advance(.2); Near(player.PositionSeconds, .95, "paused transport holds position");
    player.Stop(); Near(player.PositionSeconds, 0, "stop rewinds");
    player.Play(); player.Pause(); player.Play();
    Check(output.Messages.Any(m => m.IsNoteOn), "resume exactly on a note boundary retriggers the note");
}

Check(FfmpegExporter.ParseArguments("-vf \"scale=640:360, drawtext=text='hello world'\" -metadata title='演示 video' -empty \"\"")
    .SequenceEqual(new[] { "-vf", "scale=640:360, drawtext=text='hello world'", "-metadata", "title=演示 video", "-empty", "" }),
    "custom ffmpeg arguments preserve quoting and empty arguments without a shell");
rejected = false;
try { FfmpegExporter.ParseArguments("-vf \"unterminated"); } catch (FormatException) { rejected = true; }
Check(rejected, "custom ffmpeg arguments reject unclosed quotes");

var random = new Random(981);
var bigEvents = new List<(long, byte[])>();
for (int i = 0; i < 20000; i++)
{
    long start = i * 10;
    byte key = (byte)(i % 100 + 20);
    bigEvents.Add((start, [0x90, key, 90])); bigEvents.Add((start + random.Next(1, 800), [0x80, key, 0]));
}
bigEvents.Add((201000, Meta(0x2F, [])));
var big = Load(Track(bigEvents.ToArray()));
for (int i = 0; i < 100; i++)
{
    double start = random.NextDouble() * big.DurationSeconds, end = start + .25;
    if (big.QueryNotes(start, end).Count() != big.Notes.Count(n => n.StartSeconds <= end && n.EndSeconds >= start))
        throw new Exception("Indexed interval query differs from brute force.");
}
Check(true, "20,000-note interval index matches 100 brute-force queries");

if (args.Contains("--stress"))
{
    const int count = 1_000_000;
    using var track = new MemoryStream(count * 8 + 4);
    byte[] pair = [1, 0x90, 60, 100, 1, 0x80, 60, 0];
    for (int i = 0; i < count; i++) { pair[2] = pair[6] = (byte)(i % 128); track.Write(pair); }
    track.Write([0, 0xFF, 0x2F, 0]);
    var clock = Stopwatch.StartNew();
    var blackMidi = Load(track.ToArray());
    double loadSeconds = clock.Elapsed.TotalSeconds;
    clock.Restart();
    long visible = 0;
    for (int i = 0; i < 1000; i++) visible += blackMidi.QueryNotes(i + .01, i + 1.01).LongCount();
    Check(blackMidi.NoteCount == count && visible > 0, "one million notes parse and 1,000 indexed range queries");
    Console.WriteLine($"STRESS: load={loadSeconds:0.000}s; 1,000 queries={clock.Elapsed.TotalMilliseconds:0.0}ms; managed memory={GC.GetTotalMemory(false) / 1024 / 1024} MiB");
}

string fixtureDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../fixtures"));
Directory.CreateDirectory(fixtureDirectory);
var melody = new List<(long, byte[])> { (0, Meta(3, Encoding.UTF8.GetBytes("Zenith demo — piano"))),
    (0, Meta(10, [0, 15, 127, 0, 40, 180, 255, 255, 100, 70, 235, 255])), (0, [0xC0, 0]) };
var bass = new List<(long, byte[])> { (0, Meta(3, Encoding.UTF8.GetBytes("Arpeggio"))),
    (0, Meta(10, [0, 15, 127, 0, 255, 150, 45, 255, 255, 70, 150, 255])), (0, [0xC1, 10]) };
int[][] chords = [[60, 64, 67, 72], [57, 60, 64, 69], [53, 57, 60, 65], [55, 59, 62, 67]];
for (int bar = 0; bar < 8; bar++)
{
    int[] chord = chords[bar % chords.Length];
    for (int beat = 0; beat < 8; beat++)
    {
        long tick = bar * 1920 + beat * 240;
        byte key = (byte)(chord[(beat + bar) % 4] + (beat >= 4 ? 12 : 0));
        melody.Add((tick, [0x90, key, (byte)(80 + beat * 4)])); melody.Add((tick + 210, [0x80, key, 50]));
        byte low = (byte)(chord[beat % 4] - 24);
        bass.Add((tick, [0x91, low, 75])); bass.Add((tick + 420, [0x81, low, 40]));
    }
}
melody.Add((16000, Meta(0x2F, []))); bass.Add((16000, Meta(0x2F, [])));
File.WriteAllBytes(Path.Combine(fixtureDirectory, "demo.mid"), Smf(480,
    Track((0, Meta(0x51, [7, 161, 32])), (0, Meta(0x58, [4, 2, 24, 8])), (7680, Meta(0x51, [6, 0xDD, 0xD0])), (16000, Meta(0x2F, []))),
    Track(melody.ToArray()), Track(bass.ToArray())));
Check(MidiSequence.Load(Path.Combine(fixtureDirectory, "demo.mid")).NoteCount == 128, "demo.mid generated and round-tripped");

if (args.Contains("--audio") && OperatingSystem.IsMacOS())
{
    using var synth = new MacMidiSynth(); synth.Reset();
    Check(true, "macOS AudioUnit graph and DLS synth initialize");
}
if (args.Contains("--ffmpeg"))
{
    string destination = Path.Combine(fixtureDirectory, "export-test.mp4");
    string mask = Path.Combine(fixtureDirectory, "export-mask-test.mp4");
    byte[] frame = new byte[64 * 64 * 4];
    var result = await FfmpegExporter.ExportAsync(new() { OutputPath = destination, MaskOutputPath = mask,
        Width = 64, Height = 64, FramesPerSecond = 24, DurationSeconds = .5, Preset = "ultrafast" },
        (info, token) =>
        {
            for (int i = 0; i < frame.Length; i += 4)
            { frame[i] = (byte)(info.Index * 20); frame[i + 1] = 80; frame[i + 2] = 200; frame[i + 3] = (byte)(i / 4 % 64 * 4); }
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(frame);
        });
    Check(result.FrameCount == 12 && new FileInfo(destination).Length > 0 && new FileInfo(mask).Length > 0, "ffmpeg encodes video and alpha mask");
    var probe = Process.Start(new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true,
        ArgumentList = { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,nb_frames", "-of", "csv=p=0", destination } })!;
    string description = await probe.StandardOutput.ReadToEndAsync(); await probe.WaitForExitAsync();
    Check(description.Trim() == "64,64,12", "ffprobe verifies dimensions and exact output frame count");
    string dynamicPath = Path.Combine(fixtureDirectory, "dynamic-tail-test.mp4");
    var dynamicResult = await FfmpegExporter.ExportAsync(new() { OutputPath = dynamicPath, Width = 64, Height = 64,
        FramesPerSecond = 24, DurationSeconds = .1, StopAfterFrame = info => info.Index == 6, Preset = "ultrafast" },
        (info, token) => ValueTask.FromResult<ReadOnlyMemory<byte>>(frame));
    Check(dynamicResult.FrameCount == 7, "renderer completion controls actual frame count beyond the progress estimate");
    using var dynamicProbe = Process.Start(new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true,
        ArgumentList = { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=nb_frames", "-of", "csv=p=0", dynamicPath } })!;
    string actualDynamicFrames = await dynamicProbe.StandardOutput.ReadToEndAsync(); await dynamicProbe.WaitForExitAsync();
    Check(actualDynamicFrames.Trim() == "7", "dynamic renderer tails are not truncated by an ffmpeg duration cap");
    using var cancel = new CancellationTokenSource();
    string cancelledPath = Path.Combine(fixtureDirectory, "cancelled-export.mp4");
    bool cancelled = false;
    try
    {
        await FfmpegExporter.ExportAsync(new() { OutputPath = cancelledPath, Width = 64, Height = 64, FramesPerSecond = 24, DurationSeconds = 5 },
            (info, token) => { cancel.Cancel(); return ValueTask.FromResult<ReadOnlyMemory<byte>>(frame); }, cancellationToken: cancel.Token);
    }
    catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled && !File.Exists(cancelledPath) && !Directory.EnumerateFiles(fixtureDirectory, ".cancelled-export.zenith-*").Any(),
        "cancelled export removes its partial output");
}
Console.WriteLine($"All {passed} tests passed. Fixture: {Path.Combine(fixtureDirectory, "demo.mid")}");

static byte[] Meta(byte type, byte[] data) => [0xFF, type, .. Vlq(data.Length), .. data];
static byte[] Track(params (long Tick, byte[] Message)[] events)
{
    using var stream = new MemoryStream();
    long previous = 0;
    foreach (var e in events.OrderBy(e => e.Tick)) { stream.Write(Vlq(e.Tick - previous)); stream.Write(e.Message); previous = e.Tick; }
    return stream.ToArray();
}
static byte[] Vlq(long value)
{
    Span<byte> bytes = stackalloc byte[4];
    int at = 3; bytes[at] = (byte)(value & 127);
    while ((value >>= 7) > 0) bytes[--at] = (byte)((value & 127) | 128);
    return bytes[at..].ToArray();
}
static byte[] Smf(ushort division, params byte[][] tracks)
{
    using var stream = new MemoryStream();
    stream.Write("MThd"u8); stream.Write([0, 0, 0, 6, 0, tracks.Length == 1 ? (byte)0 : (byte)1]);
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)tracks.Length); stream.Write(buffer[..2]);
    BinaryPrimitives.WriteUInt16BigEndian(buffer, division); stream.Write(buffer[..2]);
    foreach (var track in tracks) { stream.Write("MTrk"u8); BinaryPrimitives.WriteInt32BigEndian(buffer, track.Length); stream.Write(buffer); stream.Write(track); }
    return stream.ToArray();
}

sealed class RecordingOutput : IMidiOutput
{
    public List<MidiEvent> Messages { get; } = [];
    public void Send(MidiEvent message) => Messages.Add(message);
    public void Reset() => Messages.Clear();
    public void Dispose() { }
}
