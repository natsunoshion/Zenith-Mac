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

`Zenith.png` is the 1024 px Avalonia window icon. `build-app.sh` copies
`Zenith.icns` into the macOS bundle's Resources with a content-hash filename and
sets `CFBundleIconFile`. The current 0.1.1 bundle uses
`Contents/Resources/Zenith-8155fd0117e6.icns`; its Info.plist points to that exact
filename. Since Avalonia.Native 11.3.9 treats `Window.Icon` as a
no-op on macOS, `MacApplicationIcon` also explicitly sets this process's
`NSApplication.applicationIconImage` from the embedded original-art ICNS during
framework initialization and desktop startup. It does not change Dock
preferences or other applications.

Apple ImageIO decoding of all ten ICNS representations was checked against
their embedded PNG pixels. Read-only native checks of the previous running
bundle's `NSWorkspace` and `NSRunningApplication` icons returned pixels identical
to the 1024 px original-art PNG. An isolated process with activation policy
Prohibited (no windows or Dock item) tested the production native setter and
read back its application icon: alpha geometry was unchanged, and sampled green
colors matched after converting the display profile back to sRGB. Validation
records are in `artifacts/icon-review/verification.txt` and
`artifacts/icon-review/dock-diagnostic/verification.md`.

The current application has been rebuilt, signed and launched with this startup
correction. Dock's displayed icon has **not** been visually confirmed: an earlier
Dock automation attempt timed out. Native image resolution and setter readback
are not a screenshot of Dock's actual presentation.

Original artwork remains under the upstream license; see `upstream/LICENSE`
and `THIRD_PARTY_NOTICES.md`.
