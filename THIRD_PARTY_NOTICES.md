# Attribution

This is a macOS port of **Zenith MIDI**, by **Arduano** and the original contributors.
Upstream: https://github.com/arduano/Zenith-MIDI
Source revision: 36f8ba3c06a6b26b9616f31a6d973d0d676e2747.

The upstream source is distributed under the DON'T BE A DICK PUBLIC LICENSE 1.1.
The complete original license is retained at `upstream/LICENSE`. The renderer algorithms,
shader programs, scripting API, keyboard layout math, UI layout definitions and translations
are derived from that source. They are not represented as original work by this port.

The user's `Zenith.7z` supplies the existing texture packs, palettes, translations and
`Synthesia X.zrp`. The archive and pack are retained without modification. These assets
retain their authors' rights; this workspace does not grant additional redistribution rights.

Runtime dependencies retain their upstream licenses: Avalonia (MIT), OpenTK (MIT/X11),
SkiaSharp (MIT; native Skia BSD), Microsoft.CodeAnalysis (MIT), Newtonsoft.Json (MIT),
SharpCompress (MIT). .NET runtime license files are included by self-contained publishing.
FFmpeg is an external executable, used under the license of the installed build.

## Interface color palettes

The selectable interface themes adapt these projects' published color palettes
to Zenith's controls; they are not official ports endorsed by those projects.
Sage Light is a custom palette for this application.

- Nord Light / Nord Dark: [Nord](https://www.nordtheme.com/),
  Copyright (c) 2016-present Sven Greb. [MIT license](licenses/Nord-MIT.txt).
- Catppuccin Latte / Mocha: [Catppuccin](https://catppuccin.com/palette/),
  Copyright (c) 2021 Catppuccin. [MIT license](licenses/Catppuccin-MIT.txt).
- Solarized Light: [Solarized](https://ethanschoonover.com/solarized/),
  Copyright (c) 2011 Ethan Schoonover. [MIT license](licenses/Solarized-MIT.txt).

These themes affect application controls, not the original skins, MIDI palette
images, exported frames, or reference artwork. The theme license notices are
also included in the packaged application's Resources/licenses directory.
