# MIDI, audio, and export verification

Run the independent .NET 9 core checks:

```sh
dotnet run --project tests/Zenith.Core.SelfTest
dotnet run --project tests/Zenith.Core.SelfTest -- --audio --ffmpeg --stress
dotnet run --project tests/Zenith.Core.SelfTest -- --audio --ffmpeg --videotoolbox --stress
```

`--audio` initializes the native macOS Apple DLS AudioUnit graph and resets its channels without playing a test tone. `--ffmpeg` writes a short BGRA video and alpha mask, checks their frame count with ffprobe, verifies a renderer can extend output beyond its progress estimate, and verifies cancellation removes partial output. It also compares decoded serial/overlap video and mask frame hashes with a reused render buffer, checks completion before the next frame, and cancels while both encoder pipes have pending data. Add `--videotoolbox` to test an actual Apple H.264 hardware video/mask encode; this requires available hardware support and does not silently fall back. `--stress` parses one million notes and runs 1,000 interval queries. FFmpeg and ffprobe must be on `PATH` for the export checks.

The parser checks include SMF format 0/1, running status, FIFO overlapping notes, zero-velocity note-off, tempo changes, PPQ and SMPTE timing, SysEx, invalid and truncated input, Zenith 8/12-byte color events, sustain across tracks, sostenuto, channel-mode controllers, and note closure. Transport checks cover seeking, controller restoration, pause/resume, and speed.

The suite creates `tests/fixtures/demo.mid`, a generated 128-note example with two instrument tracks, a tempo change, and the original Zenith color meta events. Original visualization timing uses `MidiNote.EndTick`/`EndSeconds`. Audio uses separate release and sounding end times so pedal and channel-mode effects do not change the original note geometry.

For a real renderer export, run:

```sh
dotnet run --project src/Zenith.Cli -- render tests/fixtures/demo.mid artifacts/demo.mp4 \
  'assets/windows/Zenith/Plugins/Assets/Scripted/Resources/Synthesia X.zrp' \
  --width 1280 --height 720 --fps 60 --duration 3 --mask artifacts/demo-mask.mp4
```

Add `--audio /path/to/audio.wav` to mux source audio. `--start`, `--speed`, `--audio-offset`, and `--audio-trim` control alignment. `--custom` splits a quoted ffmpeg argument string into arguments without invoking a shell. Output files replace their destinations only after encoding succeeds; cancellation removes the temporary files.

The graphical app uses the original renderer's initial screen-time preroll, resets export speed to one, and ends after the MIDI parser lookahead reaches every track end followed by five nominal seconds of consecutive empty render frames. Its progress duration is an estimate; `VideoExportOptions.StopAfterFrame` supplies the actual completion condition. The CLI's explicit duration remains a fixed frame range.

To verify the actual original Flat renderer, preroll, and dynamic tail together with FFmpeg:

```sh
dotnet run --project tests/Zenith.App.Tests -- --export
```

This creates `artifacts/validation/flat-tail.mp4` and `flat-tail-mask.mp4`; both must contain exactly 349 frames for the test fixture. The test suite also covers preview timing and negative preroll without playing MIDI early.
