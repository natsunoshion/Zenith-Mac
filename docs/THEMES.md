# Interface themes

Choose **Theme** in the General page's bottom bar. Changes apply immediately and
the selection is saved with the application's other settings. The default is
**Sage Light**, a soft limestone background with sage-green accents.

| Theme | Appearance | Palette reference |
| --- | --- | --- |
| Sage Light | Warm neutral surfaces, sage green actions | Custom Zenith palette |
| Nord Snow Storm | Cool light gray with blue accents | [Nord: Snow Storm and Frost](https://www.nordtheme.com/) |
| Nord Polar Night | Blue-gray surfaces with icy accents | [Nord: Polar Night and Frost](https://www.nordtheme.com/) |
| Catppuccin Latte | Pale surfaces with pastel violet accents | [Catppuccin Latte](https://catppuccin.com/palette/) |
| Catppuccin Mocha | Dark violet-gray surfaces with pastel accents | [Catppuccin Mocha](https://catppuccin.com/palette/) |
| Solarized Light | Warm paper tones with blue/cyan accents | [Solarized](https://ethanschoonover.com/solarized/) |

The reference palettes are mapped to application roles such as background,
surface, text, border, selection and action. Some role colors are adapted for
legibility; these are independent Zenith themes, not official ports of the
referenced projects. Attribution and license copies are in
[Third-party notices](../THIRD_PARTY_NOTICES.md).

The theme applies to main pages, dynamic plugin settings, the palette editor
and application dialogs. The preview player's existing dark controls remain
independent. MIDI palette swatches, skin textures, the preview image and exported
video colors are not theme resources. Switching themes does not restart a
preview or change its render settings.

The stable settings IDs are `sage`, `nord-light`, `nord-dark`, `latte`, `mocha`
and `solarized-light`. A missing or unrecognized ID falls back to Sage Light.
