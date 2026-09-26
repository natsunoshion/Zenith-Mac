# Preview playback controls

**General → Start Preview** opens a player with a separate control area below
the rendered image. Controls are not painted over the MIDI scene or included
in exported frames. The main window has a bottom status panel with **Show
preview**, replacing the previous status/progress overlay in the title bar.

## Controls

- Drag or click the timeline to seek. Dragging previews the target time and
  commits once on release; incoming frames do not move the thumb while dragging.
- Play/pause uses the existing preview pause state. After Stop or natural
  completion, the final frame remains visible and the button offers Replay.
- Restart returns through the normal preview start path, including the original
  lead-in. Mute is synchronized with the main window's audio control.
- Fullscreen keeps the control area separate from the image.

| Key | Action |
| --- | --- |
| Space | Pause/resume, or replay when stopped. Holding the key does not repeatedly toggle. |
| Enter | Toggle fullscreen. |
| Left / Right | Seek backward/forward 5 seconds. |
| Ctrl + Left / Right | Seek 20 seconds. |
| Shift + Left / Right | Seek 60 seconds. |
| Home / End | Seek to the beginning/end when the timeline is focused. |
| Page Up / Page Down | Seek forward/backward 20 seconds when the timeline is focused. |

The timeline covers 0 through the MIDI duration. Original negative lead-in and
post-MIDI tail frames are still rendered; the time/status labels identify them.
Seeking retains the original renderer and MIDI-controller reset behavior. A
seek reconstructs the renderer at the target time; it does not simulate all
earlier particle history.

## Verification

On macOS with the prepared original resources:

```sh
dotnet run --project tests/Zenith.Preview.Tests -v:quiet
```

This uses real Avalonia dispatch and CGL rendering with isolated preferences.
The checks cover pause/audio synchronization, seek/clamping and renderer reset,
Show Preview, stop/replay, natural tail completion, and the independence of an
export from an old preview window. It does not modify the user's preferences.

Local visual and pointer-event checks are recorded in `artifacts/player-qa/`
and `artifacts/preview-status-qa/` (generated artifacts are not committed).
These cover default/minimum window sizes, dragging, a single seek on release,
cancelled capture, keyboard behavior, and separation of preview versus task
progress. Render algorithms and export frame composition are unchanged by this
interface update.

The packaged application was also operated through its native controls with a
289-second MIDI and the original Synthesia X resource pack. Pause, a timeline
click to 2:24, mute/unmute, fullscreen entry/exit, restart with negative lead-in,
and an accessibility timeline increment were observed working. The final
paused frame showed the transport below the keyboard without obscuring it.
This is an interaction check, not a Windows/macOS frame comparison.
