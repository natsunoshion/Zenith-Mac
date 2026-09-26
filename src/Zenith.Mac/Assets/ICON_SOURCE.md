# Zenith application icon

Original artwork from `arduano/Zenith-MIDI`, commit
`36f8ba3c06a6b26b9616f31a6d973d0d676e2747`:

- `upstream/Black-Midi-Render/icon.ico`: nine original 16–256 px images,
  SHA256 `9a204328c582d9f932df0b9deef344f2bd3a1766c6fc7dcf9c7b1e92284a491e`.
- `upstream/Black-Midi-Render/icon.png`: original 1113×760 transparent artwork,
  SHA256 `9fd9aafef57b4244f01a6121e936a30a802c9d7c602f7df3561ad6bbd34d392e`.

Run `swift tools/make-icon.swift` from the repository root to reproduce
`Zenith.icns` and `Zenith.png`. The ICNS contains ten standard/Retina entries
from 16 to 1024 pixels. Entries up to 256 pixels retain the original ICO's
RGBA pixels. The 512/1024 images proportionally scale the original PNG onto a
transparent square canvas; there is no new artwork or recoloring. The visible
artwork occupies 68.359375% of the height at 256, 512 and 1024 pixels.

`Zenith.png` is the 1024 px Avalonia window icon. The `AppIcon.appiconset` asset
catalog is generated from the same ten ICNS representations and compiled into
`Assets.car` by `build-app.sh`; the bundle declares `CFBundleIconName=AppIcon`
and keeps `CFBundleIconFile=AppIcon` as a fallback. Since Avalonia.Native
11.3.9 treats `Window.Icon` as a no-op on macOS, `MacApplicationIcon` also explicitly sets this process's
`NSApplication.applicationIconImage` from the embedded original-art ICNS during
framework initialization and desktop startup. It does not change Dock
preferences or other applications, and asks AppKit to redraw its Dock tile.

Apple ImageIO decoding of all ten ICNS representations was checked against
their embedded PNG pixels. Read-only native checks of the previous running
bundle's `NSWorkspace` and `NSRunningApplication` icons returned pixels identical
to the 1024 px original-art PNG. An isolated process with activation policy
Prohibited (no windows or Dock item) tested the production native setter and
read back its application icon: alpha geometry was unchanged, and sampled green
colors matched after converting the display profile back to sRGB. Validation
records are in `artifacts/icon-review/verification.txt` and
`artifacts/icon-review/dock-diagnostic/verification.md`.

The app icon must still be checked in the Stage Manager sidebar on the target
macOS session; bundle registration and native image resolution alone do not
confirm the sidebar's displayed icon.

Original artwork remains under the upstream license; see `upstream/LICENSE`
and `THIRD_PARTY_NOTICES.md`.
