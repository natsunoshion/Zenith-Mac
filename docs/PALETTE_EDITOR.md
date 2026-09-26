# Palette editor

**New Palette / Edit Palette** are available in Scripted's right sidebar and
the builtin modules' palette controls. They open an editor window through
`PaletteEditorWindow.ShowEditor(owner, existingName)`. It returns the saved palette
name, or `null` on cancellation. The caller reloads its own picker and selects the
returned name. The editor does not change random ordering or its seed.
Saving automatically selects the palette and updates the running preview.

Choose one of 16 MIDI channel swatches, then use the spectrum, RGBA values or
`#RRGGBBAA` input. Six-digit `#RRGGBB` is accepted as fully opaque. The color-row
selector visits every existing row; each row supplies 16 channel colors, and rows
repeat across tracks. Gradients expose independent left and right colors.

Saving writes the original 16- or 32-column PNG representation into
`PaletteService.PaletteDirectory`. PNG decoding and encoding use straight RGBA;
all rows and unchanged pixels are retained, including RGB values at zero alpha.
Switching to solid output writes each channel's left color. Files are replaced
atomically only after the user confirms a name collision. Random palettes and the
two bundled Synthesia palettes must be saved under a custom name.
The virtual **PFA Config Colors** entry reads its 16 colors directly from the PFA
configuration and also saves as a custom copy, leaving the configuration untouched.

The spectrum is supplied by the official Avalonia 11.3.9 ColorPicker package,
with its [Fluent theme](https://docs.avaloniaui.net/controls/input/selectors/colorpicker).
The editor hides the unused spectrum/component mode header and provides explicit
byte-valued RGBA controls.

Validation artifacts are in `artifacts/palette-editor-qa`: independent PNG fixtures
at 16×3 and 32×5, pixel-exact round trips checked with Pillow, an actual dialog edit
and save, and screenshots of both solid and gradient editing. The real dialog
saved row 3/channel 6/right color `#12345601` as exact RGBA `(18,52,86,1)` while
retaining the 32×5 dimensions. `verification.txt` records the checks; validation
did not modify user palette files or random state.
