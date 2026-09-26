using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace Zenith.Mac;

public sealed record ThemeOption(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Window-local interface colors. Never changes render settings, note palettes, or preview pixels.</summary>
public static class ThemeManager
{
    const string ThemeIdKey = "Zenith.InterfaceThemeId";
    public static IReadOnlyList<ThemeOption> Options { get; } = new[]
    {
        new ThemeOption("sage", "Sage Light"),
        new ThemeOption("nord-light", "Nord Snow Storm"),
        new ThemeOption("nord-dark", "Nord Polar Night"),
        new ThemeOption("latte", "Catppuccin Latte"),
        new ThemeOption("mocha", "Catppuccin Mocha"),
        new ThemeOption("solarized-light", "Solarized Light")
    };

    // Palette values: nordtheme.com/docs/colors-and-palettes, catppuccin.com/palette,
    // ethanschoonover.com/solarized. UI role mapping and hover blends are Zenith-specific.
    // Sage Light is an original palette. A few light-theme semantic colors are darkened
    // for readable small labels rather than using the source's brighter syntax accents.
    sealed record Palette(bool Dark, string Window, string Surface, string Header, string Input,
        string Text, string Muted, string Border, string Button, string Hover, string Selection,
        string Accent, string Primary, string OnPrimary, string Success, string Info, string Error, string Warning);

    static readonly IReadOnlyDictionary<string, Palette> Palettes = new Dictionary<string, Palette>
    {
        ["sage"] = new(false, "#F1F3EC", "#FAFBF7", "#E7EBE1", "#FFFFFF",
            "#29352B", "#596952", "#CCD5C6", "#E2E9DA", "#D4DEC9", "#D9E5D0",
            "#527A47", "#527A47", "#FFFFFF", "#3F7139", "#356C9A", "#AF4141", "#946415"),
        ["nord-light"] = new(false, "#E5E9F0", "#ECEFF4", "#D8DEE9", "#ECEFF4",
            "#2E3440", "#4C566A", "#B8C3D3", "#D8DEE9", "#C4D1DF", "#D0DDE8",
            "#5E81AC", "#88C0D0", "#2E3440", "#536F3B", "#42668F", "#A3444E", "#86621E"),
        ["nord-dark"] = new(true, "#2E3440", "#3B4252", "#2E3440", "#434C5E",
            "#ECEFF4", "#D8DEE9", "#4C566A", "#434C5E", "#4C566A", "#4C566A",
            "#88C0D0", "#88C0D0", "#2E3440", "#A3BE8C", "#88C0D0", "#BF616A", "#EBCB8B"),
        ["latte"] = new(false, "#E6E9EF", "#EFF1F5", "#DCE0E8", "#EFF1F5",
            "#4C4F69", "#5C5F77", "#BCC0CC", "#CCD0DA", "#BCC0CC", "#CCD0DA",
            "#8839EF", "#8839EF", "#EFF1F5", "#2F7B20", "#1E66F5", "#D20F39", "#A6650C"),
        ["mocha"] = new(true, "#1E1E2E", "#313244", "#181825", "#1E1E2E",
            "#CDD6F4", "#BAC2DE", "#585B70", "#45475A", "#585B70", "#45475A",
            "#CBA6F7", "#CBA6F7", "#1E1E2E", "#A6E3A1", "#89B4FA", "#F38BA8", "#F9E2AF"),
        ["solarized-light"] = new(false, "#EEE8D5", "#FDF6E3", "#EEE8D5", "#FDF6E3",
            "#073642", "#586E75", "#BEC5B9", "#EEE8D5", "#D8DDCE", "#DDE9DA",
            "#268BD2", "#586E75", "#FDF6E3", "#687700", "#176FA7", "#DC322F", "#9B7500")
    };

    public static string GetThemeId(Window window) => window.Resources.TryGetResource(ThemeIdKey, null, out var value)
        && value is string id && Palettes.ContainsKey(id) ? id : "sage";

    public static void Inherit(Window window, Window owner) => Apply(window, GetThemeId(owner));

    public static void Apply(Window window, string? themeId)
    {
        string id = themeId != null && Palettes.ContainsKey(themeId) ? themeId : "sage";
        var p = Palettes[id];
        var resources = window.Resources;
        resources[ThemeIdKey] = id;
        void Brush(string name, string color) => resources[name] = new SolidColorBrush(Color.Parse(color));
        string disabled = Mix(p.Input, p.Header, .6);
        string disabledText = Mix(p.Text, p.Surface, .52);
        string pressed = Mix(p.Hover, p.Text, .12);
        string listHover = Mix(p.Surface, p.Accent, p.Dark ? .12 : .08);
        string listSelectedHover = Mix(p.Selection, p.Accent, p.Dark ? .14 : .10);
        string scrollbarThumb = Mix(p.Muted, p.Surface, .28);
        // Move away from the label luminance so pointer states retain its contrast.
        string primaryTarget = Luminance(p.OnPrimary) > Luminance(p.Primary) ? "#000000" : "#FFFFFF";
        string primaryHover = Mix(p.Primary, primaryTarget, .08);
        string primaryPressed = Mix(p.Primary, primaryTarget, .16);
        var colors = new Dictionary<string, string>
        {
            ["Window"] = p.Window, ["Surface"] = p.Surface, ["Header"] = p.Header,
            ["Input"] = p.Input, ["InputHover"] = Mix(p.Input, p.Button, .22),
            ["Disabled"] = disabled, ["Text"] = p.Text, ["MutedText"] = p.Muted,
            ["DisabledText"] = disabledText, ["Border"] = p.Border, ["BorderHover"] = p.Accent,
            ["Button"] = p.Button, ["ButtonHover"] = p.Hover, ["ButtonPressed"] = pressed,
            ["Accent"] = p.Accent, ["Primary"] = p.Primary, ["PrimaryHover"] = primaryHover,
            ["PrimaryPressed"] = primaryPressed, ["OnPrimary"] = p.OnPrimary,
            ["Selection"] = p.Selection, ["SelectionText"] = p.Text,
            ["ListHover"] = listHover, ["ListSelectedHover"] = listSelectedHover,
            ["Success"] = p.Success, ["Info"] = p.Info, ["Error"] = p.Error,
            ["Warning"] = p.Warning, ["PreviewWell"] = p.Surface,
            ["CloseHover"] = p.Error, ["ClosePressed"] = Mix(p.Error, p.Dark ? "#FFFFFF" : "#000000", .14),
            ["OnDanger"] = p.Dark ? p.Window : "#FFFFFF"
        };
        foreach (var (name, color) in colors) Brush("Zenith" + name + "Brush", color);

        // Fluent's template states have their own resources. Defining them in this
        // window keeps popups/disabled inputs in its palette without affecting PreviewWindow.
        foreach (var (name, color) in new Dictionary<string, string>
        {
            ["TextControlBackground"] = p.Input, ["TextControlBackgroundPointerOver"] = colors["InputHover"],
            ["TextControlBackgroundFocused"] = p.Input, ["TextControlBackgroundDisabled"] = disabled,
            ["TextControlBorderBrush"] = p.Border, ["TextControlBorderBrushPointerOver"] = p.Accent,
            ["TextControlBorderBrushFocused"] = p.Accent, ["TextControlBorderBrushDisabled"] = p.Border,
            ["TextControlForeground"] = p.Text, ["TextControlForegroundPointerOver"] = p.Text,
            ["TextControlForegroundFocused"] = p.Text, ["TextControlForegroundDisabled"] = disabledText,
            ["TextControlPlaceholderForeground"] = p.Muted, ["TextControlPlaceholderForegroundFocused"] = p.Muted,
            ["TextControlPlaceholderForegroundPointerOver"] = p.Muted, ["TextControlPlaceholderForegroundDisabled"] = disabledText,
            ["ComboBoxBackground"] = p.Input, ["ComboBoxBackgroundPointerOver"] = colors["InputHover"],
            ["ComboBoxBackgroundPressed"] = p.Selection, ["ComboBoxBackgroundDisabled"] = disabled,
            ["ComboBoxBorderBrush"] = p.Border, ["ComboBoxBorderBrushPointerOver"] = Mix(p.Border, p.Accent, .4),
            ["ComboBoxBorderBrushPressed"] = p.Accent, ["ComboBoxBorderBrushDisabled"] = p.Border,
            ["ComboBoxForeground"] = p.Text, ["ComboBoxForegroundPointerOver"] = p.Text,
            ["ComboBoxForegroundPressed"] = p.Text, ["ComboBoxForegroundDisabled"] = disabledText,
            ["ComboBoxDropDownBackground"] = p.Surface, ["ComboBoxDropDownBorderBrush"] = p.Border,
            ["ComboBoxItemBackgroundSelected"] = p.Selection, ["ComboBoxItemBackgroundSelectedPointerOver"] = listSelectedHover,
            ["ComboBoxItemBackgroundPointerOver"] = listHover, ["ComboBoxItemForegroundSelected"] = p.Text,
            ["ComboBoxItemBorderBrushPointerOver"] = "#00000000", ["ComboBoxItemBorderBrushSelectedPointerOver"] = "#00000000",
            ["ListBoxItemBackgroundSelected"] = p.Selection, ["ListBoxItemBackgroundSelectedPointerOver"] = listSelectedHover,
            ["ListBoxItemBackgroundPointerOver"] = listHover, ["ListBoxItemForegroundSelected"] = p.Text,
            ["ScrollBarBackground"] = "#00000000", ["ScrollBarForeground"] = scrollbarThumb,
            ["ScrollBarBorderBrush"] = "#00000000", ["ScrollBarTrackFill"] = "#00000000",
            ["ScrollBarTrackFillPointerOver"] = Mix(p.Surface, p.Accent, .035),
            ["ScrollBarTrackStroke"] = "#00000000", ["ScrollBarTrackStrokePointerOver"] = "#00000000",
            ["ScrollBarPanningThumbBackground"] = scrollbarThumb,
            ["ScrollBarThumbFillPointerOver"] = p.Accent, ["ScrollBarThumbFillPressed"] = p.Accent,
            ["ScrollBarThumbFillDisabled"] = p.Border, ["ScrollBarThumbBackgroundColor"] = p.Accent,
            ["ScrollBarButtonBackground"] = "#00000000", ["ScrollBarButtonBorderBrush"] = "#00000000",
            ["SystemControlForegroundBaseHighBrush"] = p.Text,
            ["SystemControlForegroundBaseMediumBrush"] = p.Muted,
            ["SystemControlHighlightAccentBrush"] = p.Accent
        }) Brush(name, color);
        resources["ScrollBarSize"] = 7d;
        resources["ScrollBarTrackBorderThemeThickness"] = 0d;
        resources["SystemAccentColor"] = Color.Parse(p.Accent);
        resources["SystemAccentColorDark1"] = Color.Parse(p.Primary);
        resources["SystemAccentColorLight1"] = Color.Parse(p.Accent);
        window.RequestedThemeVariant = p.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    public static void BindBrush(AvaloniaObject target, AvaloniaProperty property, string role) =>
        target.Bind(property, new DynamicResourceExtension("Zenith" + role + "Brush"));

    static double Luminance(string hex)
    {
        var c = Color.Parse(hex);
        static double Linear(byte value) { double s = value / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
        return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
    }

    static string Mix(string first, string second, double amount)
    {
        var a = Color.Parse(first); var b = Color.Parse(second);
        byte Channel(byte x, byte y) => (byte)Math.Round(x + (y - x) * amount);
        return $"#{Channel(a.R, b.R):X2}{Channel(a.G, b.G):X2}{Channel(a.B, b.B):X2}";
    }
}
