using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OpenTK.Mathematics;
using Zenith.Core.Rendering;
using ShapeRectangle = Avalonia.Controls.Shapes.Rectangle;

namespace Zenith.Mac;

/// <summary>A native-window editor for the original Zenith palette PNG format.</summary>
public sealed class PaletteEditorWindow : Window
{
    readonly PaletteDocument document;
    readonly string? editedPaletteName;
    readonly Action<Color4[]?>? previewPaletteChanged;
    readonly TextBox nameBox = new() { Name = "PaletteName", MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock selectedLabel = new() { FontSize = 16 };
    readonly TextBlock rowCount = new();
    readonly NumericUpDown rowNumber = new() { Name = "PaletteRow", Minimum = 1, Value = 1, Increment = 1, FormatString = "0", Width = 75 };
    readonly CheckBox gradients = new() { Name = "UseGradients", Content = "Left / right gradients" };
    readonly RadioButton left = new() { Content = "Left", GroupName = "PaletteSide", IsChecked = true };
    readonly RadioButton right = new() { Content = "Right", GroupName = "PaletteSide" };
    readonly ColorView colorView = new()
    {
        Name = "PaletteColorPicker", IsAlphaEnabled = true, IsAlphaVisible = false,
        IsColorComponentsVisible = false, IsColorPaletteVisible = false, IsColorPreviewVisible = false,
        IsColorModelVisible = false, IsHexInputVisible = false, Width = 312,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    readonly NumericUpDown[] components = new NumericUpDown[4];
    readonly TextBox hexBox = new() { Name = "PaletteHex", Watermark = "#RRGGBBAA", Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Button save = new() { Name = "SavePalette", Content = "Save palette", MinWidth = 130, IsDefault = true };
    readonly Button previous = new() { Content = "‹", Width = 32, Padding = new Thickness(0) };
    readonly Button next = new() { Content = "›", Width = 32, Padding = new Thickness(0) };
    readonly Button remove = new() { Content = "Remove row" };
    readonly PaletteSwatch[] swatches = new PaletteSwatch[16];
    readonly Button[] swatchButtons = new Button[16];
    readonly Border[] swatchRings = new Border[16];
    int row, channel, side;
    bool refreshing, validHex = true;

    public static Task<string?> ShowEditor(Window owner, string? existingName = null, Action<Color4[]?>? previewChanged = null)
    {
        var editor = new PaletteEditorWindow(existingName, previewChanged);
        ThemeManager.Inherit(editor, owner);
        return editor.ShowDialog<string?>(owner);
    }

    public PaletteEditorWindow(string? existingName = null, Action<Color4[]?>? previewChanged = null)
    {
        previewPaletteChanged = previewChanged;
        Closed += (_, _) => { if (ResultName == null) previewPaletteChanged?.Invoke(null); };
        if (existingName != null && !PaletteDocument.IsProtectedName(existingName))
            editedPaletteName = existingName;
        Title = "Palette Editor — Zenith";
        Width = 700; Height = 640; MinWidth = 690; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.Full;
        ThemeManager.Apply(this, "sage");
        ThemeManager.BindBrush(error, TextBlock.ForegroundProperty, "Warning");
        Styles.Add(new StyleInclude(new Uri("avares://Zenith.Mac/"))
        { Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml") });
        // This editor uses only the spectrum page; omit its redundant mode tab.
        colorView.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Border>("TabBackgroundBorder") is { } tabBackground) tabBackground.IsVisible = false;
            if (e.NameScope.Find<Border>("ContentBackgroundBorder") is { } contentBackground) contentBackground.Margin = new Thickness(0);
            if (e.NameScope.Find<TabControl>("PART_TabControl") is { } tabs)
            {
                tabs.Width = 324; tabs.Height = 250;
                tabs.TemplateApplied += (_, tabTemplate) =>
                {
                    if (tabTemplate.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter") is { } header) header.IsVisible = false;
                };
            }
        };
        EventHandler? styleSpectrumAfterLayout = null;
        styleSpectrumAfterLayout = (_, _) =>
        {
            if (FindVisual<ColorSpectrum>(colorView, "ColorSpectrum") is not { } spectrum) return;
            colorView.LayoutUpdated -= styleSpectrumAfterLayout;
            spectrum.TemplateApplied += (_, _) => StyleSpectrumSurface(spectrum);
            StyleSpectrumSurface(spectrum);
        };
        colorView.LayoutUpdated += styleSpectrumAfterLayout;

        if (existingName == null) document = PaletteDocument.Create();
        else if (string.Equals(existingName, PaletteDocument.PfaConfigName, StringComparison.OrdinalIgnoreCase))
            document = PaletteDocument.LoadPfaConfig(PaletteService.PfaConfigPath);
        else
        {
            string path = PaletteDocument.FindPath(existingName)
                ?? Path.Combine(AssetPaths.Resolve("Plugins/Assets/Palettes"), existingName + ".png");
            document = PaletteDocument.Load(path);
        }
        nameBox.Text = existingName == null ? PaletteDocument.SuggestName("Custom Palette")
            : PaletteDocument.IsProtectedName(existingName) ? PaletteDocument.SuggestName(existingName + " Custom") : existingName;
        save.Classes.Add("primary");
        Content = BuildContent(existingName != null && PaletteDocument.IsProtectedName(existingName));
        gradients.IsChecked = document.UseGradients;
        gradients.IsCheckedChanged += async (_, _) => await ChangeGradientMode();
        rowNumber.ValueChanged += (_, _) => { if (!refreshing && rowNumber.Value.HasValue) { row = (int)rowNumber.Value.Value - 1; Refresh(); } };
        left.IsCheckedChanged += (_, _) => { if (!refreshing && left.IsChecked == true) { side = 0; RefreshColor(); } };
        right.IsCheckedChanged += (_, _) => { if (!refreshing && right.IsChecked == true) { side = 1; RefreshColor(); } };
        colorView.ColorChanged += (_, _) => { if (!refreshing) ApplyColor(colorView.Color); };
        hexBox.TextChanged += (_, _) =>
        {
            if (refreshing) return;
            if (TryParseHex(hexBox.Text, out var color)) { validHex = true; error.Text = ""; ApplyColor(color, preserveHex: true); }
            else { validHex = false; error.Text = "Enter #RRGGBB or #RRGGBBAA (alpha comes last)."; }
            save.IsEnabled = validHex;
        };
        save.Click += async (_, _) => await Save();
        Refresh();
        PublishPreview();
    }

    Control BuildContent(bool builtIn)
    {
        var outer = new Grid { Margin = new Thickness(18, 14, 18, 16), RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = 10 };
        var naming = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        naming.Children.Add(new Label { Content = "Palette name" });
        Grid.SetColumn(nameBox, 1); naming.Children.Add(nameBox);
        outer.Children.Add(naming);
        var columns = new Grid
        {
            Width = 654,
            ColumnDefinitions = new("290,348"),
            ColumnSpacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(columns, 1); outer.Children.Add(columns);

        var palettePanel = new StackPanel { Spacing = 10 };
        var board = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
        board.Classes.Add("card");
        var boardContent = new StackPanel { Spacing = 8 };
        var boardTitle = new TextBlock { Text = "Channel colors", FontSize = 18, FontWeight = FontWeight.SemiBold };
        boardContent.Children.Add(boardTitle);
        var subtitle = new TextBlock { Text = "16 MIDI channels · colors repeat for each track", FontSize = 11, TextWrapping = TextWrapping.Wrap };
        ThemeManager.BindBrush(subtitle, TextBlock.ForegroundProperty, "MutedText"); boardContent.Children.Add(subtitle);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        var rowLabel = new TextBlock { Text = "Color row", FontWeight = FontWeight.Medium, Margin = new Thickness(0, 0, 4, 0) };
        previous.Click += (_, _) => { row--; Refresh(); }; next.Click += (_, _) => { row++; Refresh(); };
        navigation.Children.Add(rowLabel); navigation.Children.Add(previous); navigation.Children.Add(rowNumber); navigation.Children.Add(next);
        ThemeManager.BindBrush(rowCount, TextBlock.ForegroundProperty, "MutedText"); navigation.Children.Add(rowCount);
        boardContent.Children.Add(navigation);
        // Keep the swatches visually grouped instead of spreading four columns
        // across a wide, mostly empty area.
        var colors = new UniformGrid { Rows = 4, Columns = 4, Width = 216, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < 16; i++)
        {
            int index = i;
            swatches[i] = new PaletteSwatch { Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var ring = new Border { Name = "ChannelRing" + (i + 1), Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Padding = new Thickness(2), BorderThickness = new Thickness(1), Child = swatches[i] };
            ring.Classes.Add("palette-color-ring");
            swatchRings[i] = ring;
            var number = new TextBlock { Text = (i + 1).ToString("00"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
            ThemeManager.BindBrush(number, TextBlock.ForegroundProperty, "MutedText");
            var tileContent = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            tileContent.Children.Add(ring); tileContent.Children.Add(number);
            swatchButtons[i] = new Button { Name = "Channel" + (i + 1), Content = tileContent,
                Width = 54, Height = 54, CornerRadius = new CornerRadius(10), Padding = new Thickness(2), Margin = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
            swatchButtons[i].Classes.Add("palette-swatch");
            swatchButtons[i].Click += (_, _) => { channel = index; Refresh(); };
            colors.Children.Add(swatchButtons[i]);
        }
        boardContent.Children.Add(colors);
        boardContent.Children.Add(gradients);
        board.Child = boardContent;
        palettePanel.Children.Add(board);

        var hint = new TextBlock { Text = (builtIn ? "Built-in palettes save as custom copies. " : "")
            + "Turn off Randomise in the sidebar to assign fixed colors.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(2, 0) };
        ThemeManager.BindBrush(hint, TextBlock.ForegroundProperty, "MutedText"); palettePanel.Children.Add(hint);
        var rowActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var add = new Button { Content = "Duplicate row", MinWidth = 120 };
        add.Click += (_, _) => { document.AddRow(row); row = document.RowCount - 1; Refresh(); PublishPreview(); };
        remove.Click += async (_, _) => { if (await Confirm("Remove this color row?", "The other rows will be kept.", "Remove row")) { document.RemoveRow(row); row = Math.Min(row, document.RowCount - 1); Refresh(); PublishPreview(); } };
        rowActions.Children.Add(add); rowActions.Children.Add(remove); palettePanel.Children.Add(rowActions);
        columns.Children.Add(new ScrollViewer { Content = palettePanel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        var editorCard = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
        editorCard.Classes.Add("card");
        var editor = new StackPanel { Spacing = 9 };
        selectedLabel.FontSize = 16; selectedLabel.FontWeight = FontWeight.SemiBold;
        editor.Children.Add(selectedLabel);
        var sides = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 }; sides.Children.Add(left); sides.Children.Add(right); editor.Children.Add(sides);
        editor.Children.Add(colorView);
        var rgba = new Grid { ColumnDefinitions = new("*,*,*,*"), ColumnSpacing = 6 };
        for (int i = 0; i < 4; i++)
        {
            var component = components[i] = new NumericUpDown { Name = "RGBA"[i].ToString(), Minimum = 0, Maximum = 255, Increment = 1, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch };
            var part = new StackPanel { Spacing = 4 };
            var label = new TextBlock { Text = "RGBA"[i].ToString(), FontSize = 11, FontWeight = FontWeight.SemiBold };
            ThemeManager.BindBrush(label, TextBlock.ForegroundProperty, "MutedText"); part.Children.Add(label); part.Children.Add(component);
            Grid.SetColumn(part, i); rgba.Children.Add(part);
            component.ValueChanged += (_, _) => { if (!refreshing && components.All(c => c?.Value != null)) ApplyColor(Color.FromArgb((byte)components[3].Value!.Value, (byte)components[0].Value!.Value, (byte)components[1].Value!.Value, (byte)components[2].Value!.Value)); };
        }
        editor.Children.Add(rgba);
        var hex = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8 };
        hex.Children.Add(new Label { Content = "Hex" }); Grid.SetColumn(hexBox, 1); hex.Children.Add(hexBox); editor.Children.Add(hex);
        var hexHint = new TextBlock { Text = "#RRGGBBAA · alpha 0 is transparent", FontSize = 11, TextWrapping = TextWrapping.Wrap };
        ThemeManager.BindBrush(hexHint, TextBlock.ForegroundProperty, "MutedText"); editor.Children.Add(hexHint);
        editorCard.Child = new ScrollViewer { Content = editor, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(editorCard, 1); columns.Children.Add(editorCard);

        Grid.SetRow(error, 2); outer.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true }; cancel.Click += (_, _) => Close((string?)null);
        actions.Children.Add(cancel); actions.Children.Add(save); Grid.SetRow(actions, 3); outer.Children.Add(actions);
        return outer;
    }

    void Refresh()
    {
        refreshing = true;
        try
        {
            rowNumber.Maximum = document.RowCount; rowNumber.Value = row + 1; rowCount.Text = "/ " + document.RowCount;
            previous.IsEnabled = row > 0; next.IsEnabled = row + 1 < document.RowCount; remove.IsEnabled = document.RowCount > 1;
            right.IsEnabled = document.UseGradients; if (!document.UseGradients) side = 0;
            left.IsChecked = side == 0; right.IsChecked = side == 1;
            for (int i = 0; i < 16; i++)
            {
                swatches[i].Left = ToColor(document.GetColor(row, i, 0)); swatches[i].Right = ToColor(document.GetColor(row, i, 1)); swatches[i].InvalidateVisual();
                swatches[i].UseGradient = document.UseGradients;
                swatchRings[i].Classes.Set("selected", i == channel);
            }
        }
        finally { refreshing = false; }
        RefreshColor();
    }
    void RefreshColor(bool preserveHex = false)
    {
        refreshing = true;
        try
        {
            var color = ToColor(document.GetColor(row, channel, side));
            selectedLabel.Text = $"Channel {channel + 1}" + (document.UseGradients ? side == 0 ? " · Left" : " · Right" : " · Solid color");
            colorView.Color = color;
            components[0].Value = color.R; components[1].Value = color.G; components[2].Value = color.B; components[3].Value = color.A;
            if (!preserveHex) hexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
            validHex = true; save.IsEnabled = true;
        }
        finally { refreshing = false; }
    }
    void ApplyColor(Color color, bool preserveHex = false)
    {
        document.SetColor(row, channel, side, new(color.R, color.G, color.B, color.A));
        swatches[channel].Left = ToColor(document.GetColor(row, channel, 0)); swatches[channel].Right = ToColor(document.GetColor(row, channel, 1)); swatches[channel].InvalidateVisual();
        error.Text = ""; RefreshColor(preserveHex);
        PublishPreview();
    }
    async Task ChangeGradientMode()
    {
        if (refreshing) return;
        bool enable = gradients.IsChecked == true;
        if (!enable && document.HasDifferentGradientEnds && !await Confirm("Save solid colors instead?", "A solid palette uses each channel's left color when saved. You can switch gradients back on before saving.", "Use left colors"))
        { refreshing = true; gradients.IsChecked = true; refreshing = false; return; }
        document.UseGradients = enable; Refresh(); PublishPreview();
    }
    async Task Save()
    {
        try
        {
            if (!validHex) return;
            var name = PaletteDocument.ValidateName(nameBox.Text);
            bool exists = PaletteDocument.FindPath(name) != null;
            bool isCurrentPalette = string.Equals(name, editedPaletteName, StringComparison.OrdinalIgnoreCase);
            if (exists && !isCurrentPalette && !await Confirm("Replace existing palette?", $"“{name}” already exists. Replace its colors with this palette?", "Replace")) return;
            ResultName = document.Save(name, overwrite: exists);
            Close(ResultName);
        }
        catch (Exception e) { error.Text = e.Message; }
    }

    string? ResultName { get; set; }

    void PublishPreview() => previewPaletteChanged?.Invoke(document.ToPaletteColors());

    static void StyleSpectrumSurface(ColorSpectrum spectrum)
    {
        spectrum.BorderBrush = Brushes.Transparent;
        spectrum.BorderThickness = new Thickness(0);
        spectrum.CornerRadius = new CornerRadius(9);
        ApplyRoundedClip(spectrum, spectrum.Bounds.Size);
        spectrum.SizeChanged += (_, e) => ApplyRoundedClip(spectrum, e.NewSize);
        if (FindVisual<Panel>(spectrum, "PART_LayoutRoot") is { } layout)
        {
            ApplyRoundedClip(layout, layout.Bounds.Size);
            layout.SizeChanged += (_, e) => ApplyRoundedClip(layout, e.NewSize);
        }
        if (FindVisual<ShapeRectangle>(spectrum, "PART_SpectrumRectangle") is { } surface)
        {
            surface.RadiusX = surface.RadiusY = 8;
            surface.Stroke = Brushes.Transparent;
            surface.StrokeThickness = 0;
        }
        if (FindVisual<ShapeRectangle>(spectrum, "PART_SpectrumOverlayRectangle") is { } overlay)
        {
            overlay.RadiusX = overlay.RadiusY = 8;
            overlay.Stroke = Brushes.Transparent;
            overlay.StrokeThickness = 0;
        }
    }

    static void ApplyRoundedClip(Visual visual, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        visual.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, size.Width, size.Height),
            RadiusX = 9,
            RadiusY = 9
        };
    }

    static T? FindVisual<T>(Visual root, string name) where T : Visual
    {
        if (root is T match && root is Control control && control.Name == name) return match;
        foreach (var child in root.GetVisualChildren())
            if (FindVisual<T>(child, name) is { } found) return found;
        return null;
    }

    async Task<bool> Confirm(string title, string text, string action)
    {
        var dialog = new Window { Title = title, Width = 430, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, SystemDecorations = SystemDecorations.Full };
        dialog.Styles.Add(new StyleInclude(new Uri("avares://Zenith.Mac/")) { Source = new Uri("avares://Zenith.Mac/Styles/ZenithTheme.axaml") });
        ThemeManager.Inherit(dialog, this);
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 18 };
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        var cancel = new Button { Content = "Cancel", IsCancel = true }; var accept = new Button { Content = action };
        accept.Classes.Add("primary");
        cancel.Click += (_, _) => dialog.Close(false); accept.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancel); buttons.Children.Add(accept); panel.Children.Add(buttons); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }
    static Color ToColor(PaletteDocument.Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    static bool TryParseHex(string? text, out Color color)
    {
        color = default; string hex = (text ?? "").Trim().TrimStart('#');
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint value)) return false;
        color = hex.Length == 6 ? Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : Color.FromArgb((byte)value, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8));
        return true;
    }

    sealed class PaletteSwatch : Control
    {
        public Color Left, Right;
        public bool UseGradient;
        public override void Render(DrawingContext context)
        {
            double size = Math.Min(Bounds.Width, Bounds.Height);
            var rect = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
            IBrush fill = UseGradient
                ? new LinearGradientBrush { StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative), GradientStops = new() { new(Left, 0), new(Right, 1) } }
                : new SolidColorBrush(Left);
            context.DrawEllipse(fill, new Pen(new SolidColorBrush(Color.FromArgb(46, 0, 0, 0)), 1), rect);
        }
    }
}
