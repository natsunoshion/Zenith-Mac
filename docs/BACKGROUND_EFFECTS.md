# Background and drop shadow

These controls are on **General**, below the render options. They apply to both
the preview and exported frames, without editing the original skin package.

## Background framing and opacity

Enable **Use Background**, choose an image, then set **Opacity** between 0% and
100%. At 50%, an opaque image is half as bright against the black canvas. Any
transparency in the image is retained and multiplied by this opacity.

The image keeps its original proportions and fills the output frame. Excess
areas are cropped, with no letterboxing or stretching. A square image in a 16:9
frame fills the width and crops the top and bottom; a wider image crops the
left and right instead. This changes the previous full-image stretch behavior.

**Horizontal position** and **Vertical position** choose which part of the image
remains visible. 0% aligns to the left/top, 50% centers the crop, and 100% aligns
to the right/bottom. Only an axis with excess image area can move. **Center image**
resets both sliders to 50%. Position is saved with the other application settings;
the same crop is used in preview and export. Source transparency or black pixels
already present in an image remain part of that image.

Changing the image, checkbox, position or opacity during preview updates the composition
without restarting playback. A paused preview can update its background while
retaining its current foreground and particle state. Disabled controls keep
their place in the layout.

## Scripted glow composition

Scripted rendering now accumulates the actual transparent coverage of its
layers and composites their premultiplied colors once. This prevents a white
Sparkle Glow from darkening the background into a gray disc, including when
multiple glows overlap. The skin's textures, RGB Mix/Add operations and particle
settings remain in use; disabling the sparkle effect is not required.

This deliberately corrects the old summed-alpha/square-root composition.
Transparent edges, SSAA and exported masks can therefore differ from older
Scripted output. Half-transparent background PNGs in this path also use their
alpha when calculating visible color. Opaque images still dim directly with
the Opacity slider. Builtin renderers retain their original composition.

## Drop shadow

Enable **Drop shadow → Enable shadow**. The initial values are:

| Control | Initial value | Meaning |
| --- | --- | --- |
| Blur | 3 px | Gaussian softness (standard deviation); 0 makes a sharp shadow. |
| Direction | 45° | Clockwise: 0° right, 90° down, 180° left, 270° up. |
| Distance | 18 px | Offset from the foreground. |
| Opacity | 70% | Strength of the black shadow. |

Blur and distance use **final output pixels**. The Gaussian kernel extends to
three times the blur value on each side. Increasing SSAA does not multiply
the offset; changing output resolution does not automatically scale it. For
example, double the pixel values when moving from 1080p to 2160p if the same
relative size is desired.

The shadow follows the alpha of the **whole skin foreground**, including notes,
keyboard and effects. Original skins do not label their draw commands as
notes/keyboard/particles, so this is not an isolated note-only mask. To reveal
the note silhouettes, disable any opaque background inside the skin (for
Synthesia X: **Module Settings → Settings → Background → Display Background**).
The separate General background image does not cast a shadow.

The shadow is drawn behind the foreground and above the General background.
Changes update the current preview, including while paused. The effect is off
by default.

## Export and editing

Export takes a snapshot of the background and shadow settings when it starts.
Later changes do not alter an export in progress. **Render Transparency Mask**
includes the shadow's soft alpha. For a separate foreground layer in a video
editor, disable both background sources and export the color video together
with its transparency mask.

Shadow blur adds two GPU passes while enabled. With SSAA above 1, one preceding
pass resolves coverage at the output resolution so every blur tap does not
repeat the SSAA work. A separate composition baseline for builtin renderers retains the original
color limit at transparent edges, preventing a black shadow from introducing
bright fringes through the legacy alpha compensation. Builtins allocate two intermediate render
targets at SSAA 1, or three at higher SSAA. Scripted needs one fewer target because
its corrected composition does not need this baseline. These targets omit depth buffers
and are cached until the preview or export renderer closes. Larger blur values
and SSAA increase its cost.
The throughput examples
in [Rendering performance](PERFORMANCE.md) were measured with shadows disabled.

## Verification

```sh
dotnet run --project tests/Zenith.App.Tests -- --background
dotnet run --project tests/Zenith.App.Tests -- --premultiplied
dotnet run --project tests/Zenith.App.Tests -- --shadow --export
dotnet run --project tests/Zenith.Scripted.Tests -- --postprocessing
dotnet run --project tests/Zenith.Preview.Tests
```

The GPU checks use independent shape/alpha oracles for direction, Gaussian blur,
SSAA, transparent borders, background composition and mask output. The optional
shadow export check encodes and decodes both color video and its mask with
FFmpeg. Native preview checks exercise live changes, pause, export snapshots
and settings persistence. Local shadow images and short videos are under
`artifacts/validation/shadow/`; generated media are excluded from Git.
