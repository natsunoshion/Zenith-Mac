using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Zenith.Core.Rendering;

namespace Zenith.Mac;

/// <summary>Recreates the upstream settings layouts without executing WPF markup or Windows event handlers.</summary>
internal sealed class BuiltinSettingsBuilder
{
    private readonly MainWindow window;
    private readonly object settings;
    private readonly string root;
    private readonly string module;
    private readonly Dictionary<string, Control> controls = new();
    private readonly Dictionary<string, string> labels = new();
    private readonly List<(Control Target, string Property, string Key)> localized = new();
    private readonly List<Action> refreshers = new();
    private readonly List<(Control Target, string Source)> enabledDependencies = new();
    private bool refreshing;
    private bool selectingPack;
    private readonly JsonSerializerOptions jsonOptions = new() { IncludeFields = true, WriteIndented = true };
    private Dictionary<string, JsonElement> profiles = new();
    private bool profileSelectionWired;
    private bool auraSelectionWired;
    private readonly Dictionary<string, string> auraFiles = new();
    public event Action<string>? ActionRequested;
    public event Action<string>? PaletteChanged;
    public event Action? Changed;
    private ListBox? paletteList;
    private CheckBox? paletteRandom;
    private Button? paletteDelete;
    private PaletteSelection Palette => PaletteService.For(settings, module == "pfa" ? .8f : 1f);
    public void SetTexturedPack(object value)
    {
        if (value is not TexturedRender.Pack pack) throw new ArgumentException("Expected a Textured resource pack", nameof(value));
        if (controls.GetValueOrDefault("pluginList") is ListBox resources)
        {
            selectingPack = true;
            try { resources.SelectedItem = Path.GetFileName(pack.filepath); }
            finally { selectingPack = false; }
        }
        if (controls.GetValueOrDefault("pluginDesc") is TextBox description) description.Text = pack.description;
        if (controls.GetValueOrDefault("previewImg") is Image image)
        {
            image.Source = null;
            if (pack.preview != null)
            {
                var pixels = pack.preview.LockBits();
                var bitmap = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
                using (var locked = bitmap.Lock())
                {
                    var row = new byte[pixels.Width * 4];
                    for (var y = 0; y < pixels.Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(pixels.Scan0 + y * row.Length, row, 0, row.Length);
                        System.Runtime.InteropServices.Marshal.Copy(row, 0, locked.Address + y * locked.RowBytes, row.Length);
                    }
                }
                pack.preview.UnlockBits(pixels);
                image.Source = bitmap;
            }
        }
        if (controls.GetValueOrDefault("switchPanel") is not StackPanel panel) return;
        panel.Children.Clear();
        if (controls.GetValueOrDefault("switchTab") is TabItem tab) tab.IsVisible = pack.switchChoices.Count > 0;
        foreach (var name in pack.switchOrder)
        {
            if (!pack.switchChoices.TryGetValue(name, out var choices)) { panel.Children.Add(new Label { Content = name, FontSize = 16, Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 10, 0, 0) }); continue; }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Label { Content = name });
            var dropdown = new ComboBox { ItemsSource = choices, SelectedItem = pack.switchValues.GetValueOrDefault(name, choices[0]) };
            dropdown.SelectionChanged += (_, _) => { if (dropdown.SelectedItem is string selected) { pack.switchValues[name] = selected; ActionRequested?.Invoke("reloadPackButton"); } };
            row.Children.Add(dropdown); panel.Children.Add(row);
        }
    }
    public BuiltinSettingsBuilder(MainWindow window, string moduleId, object settings, string root, string language)
    {
        this.window = window; this.settings = settings; this.root = root;
        var id = moduleId.ToLowerInvariant();
        module = id.Contains("classic") || id.Contains("original") ? "classic" : id.Contains("miditrail") || id.Contains("trail") ? "miditrail" : id.Contains("notecount") || id.Contains("counter") ? "notecounter" : id.Contains("pfa") ? "pfa" : id.Contains("texture") ? "textured" : id.Contains("flat") ? "flat" : id;
        SetLanguage(language);
    }
    public void SetLanguage(string language)
    {
        labels.Clear();
        foreach (var locale in new[] { "en", language })
            foreach (var dictionary in new[] { "window", module })
            {
                var file = Path.Combine(root, "Languages", locale, dictionary + ".xaml");
                if (!File.Exists(file)) continue;
                foreach (var item in XDocument.Load(file).Root!.Elements())
                    if (item.Name.LocalName == "String" && item.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) is { } key) labels[key.Value] = item.Value;
            }
        foreach (var item in localized) PutText(item.Target, item.Property, labels.GetValueOrDefault(item.Key, item.Key));
    }
    public Control Build()
    {
        if (!BuiltinLayouts.Xml.TryGetValue(module, out var xml)) return BuildGenericSettings();
        var view = Read(XDocument.Parse(xml).Root!);
        foreach (var (target, source) in enabledDependencies)
            if (controls.TryGetValue(source, out var value) && value is CheckBox check)
            { target.IsEnabled = check.IsChecked == true; check.IsCheckedChanged += (_, _) => target.IsEnabled = check.IsChecked == true; }
        if (controls.TryGetValue("sameWidth", out var same) && same is CheckBox sameCheck && controls.TryGetValue("blackNotesAbove", out var black))
        { black.IsEnabled = sameCheck.IsChecked != true; sameCheck.IsCheckedChanged += (_, _) => black.IsEnabled = sameCheck.IsChecked != true; }
        if (controls.TryGetValue("auraSubControlGrid", out var aura) && aura is Grid auraGrid) auraGrid.Children.Add(Read(XDocument.Parse(BuiltinLayouts.Xml["aura"]).Root!));
        InitializeLists();
        return view;
    }
    private static string? Attr(XElement element, string name) => element.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
    private static double D(XElement e, string key, double value = 0) => double.TryParse(Attr(e, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : value;
    private static bool B(XElement e, string key, bool value = false) => bool.TryParse(Attr(e, key), out var b) ? b : value;
    private Control Read(XElement element)
    {
        var type = element.Name.LocalName;
        var name = Attr(element, "Name") ?? "";
        if (type == "UserControl") return Read(element.Elements().First(e => !e.Name.LocalName.Contains('.')));
        if (type == "NoteColorPalettePick") { var palette = PaletteControl(); SetCommon(palette, element); return palette; }
        Control control = type switch
        {
            "DockPanel" => new DockPanel { LastChildFill = B(element, "LastChildFill", true) },
            "StackPanel" => new StackPanel { Orientation = Attr(element, "Orientation") == "Horizontal" ? Orientation.Horizontal : Orientation.Vertical },
            "Grid" => new Grid(), "TabControl" => new TabControl(), "TabItem" => new TabItem(),
            "Label" => new Label(), "TextBlock" => new TextBlock(), "TextBox" => new TextBox(), "Button" => new Button(),
            "NumberSelect" => new NumericUpDown { Minimum = (decimal)D(element, "Minimum", 0), Maximum = (decimal)D(element, "Maximum", 100), Increment = (decimal)D(element, "Step", 1), FormatString = "F" + (int)D(element, "DecimalPoints", 0) },
            "ValueSlider" => new ValueSlider { Minimum = name == "noteDeltaScreenTime" ? Math.Pow(2, D(element, "Minimum")) : D(element, "Minimum"), Maximum = name == "noteDeltaScreenTime" ? Math.Pow(2, D(element, "Maximum", 100)) : D(element, "Maximum", 100), TrueMinimum = D(element, "TrueMin", 0), TrueMaximum = D(element, "TrueMax", 100), Logarithmic = name == "noteDeltaScreenTime", DecimalPlaces = (int)D(element, "DecimalPoints", 2), Step = D(element, "Step", 1) },
            "BetterCheckbox" => new CheckBox(), "BetterRadio" => new RadioButton { GroupName = "Separator" },
            "ComboBox" => new ComboBox(), "ComboBoxItem" => new ComboBoxItem(), "ListBox" => new ListBox(), "Image" => new Image { Stretch = Stretch.Uniform },
            _ => throw new NotSupportedException("Cannot translate upstream settings element " + type)
        };
        if (!string.IsNullOrEmpty(name)) controls[name] = control;
        SetCommon(control, element);
        foreach (var property in new[] { "Text", "Content", "Header" })
            if (Attr(element, property) is { } text) Localize(control, property, text);
        if (control is TextBox tb)
        {
            tb.AcceptsReturn = B(element, "AcceptsReturn");
            tb.TextWrapping = Attr(element, "TextWrapping") == "Wrap" ? TextWrapping.Wrap : TextWrapping.NoWrap;
            tb.IsReadOnly = B(element, "IsReadOnly") || Attr(element, "IsEnabled") == "False";
            if (Attr(element, "TextAlignment") == "Center") tb.TextAlignment = TextAlignment.Center;
            if (Attr(element, "MaxLength") is { } maxLength && int.TryParse(maxLength, out var max)) tb.MaxLength = max;
        }
        if (control is TextBlock textBlock) textBlock.TextWrapping = Attr(element, "TextWrapping") == "Wrap" ? TextWrapping.Wrap : TextWrapping.NoWrap;
        if (control is Grid grid)
        {
            foreach (var cols in element.Elements().Where(e => e.Name.LocalName == "Grid.ColumnDefinitions")) grid.ColumnDefinitions = new ColumnDefinitions(string.Join(',', cols.Elements().Select(e => Attr(e, "Width") ?? "*")));
            foreach (var rows in element.Elements().Where(e => e.Name.LocalName == "Grid.RowDefinitions")) grid.RowDefinitions = new RowDefinitions(string.Join(',', rows.Elements().Select(e => Attr(e, "Height") ?? "*")));
        }
        foreach (var child in element.Elements().Where(e => !e.Name.LocalName.Contains('.')))
        {
            var c = Read(child);
            if (control is Panel panel) panel.Children.Add(c);
            else if (control is ItemsControl items) items.Items.Add(c);
            else if (control is ContentControl content) content.Content = c;
        }
        // Keep a finite viewport for palette lists while the settings beside them scroll.
        if (control is DockPanel paletteDock && element.Elements().Any(e => e.Name.LocalName == "NoteColorPalettePick") && paletteDock.Children.Count == 2)
        {
            var content = paletteDock.Children[0]; var palette = paletteDock.Children[1];
            paletteDock.Children.Clear();
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,184"), ColumnSpacing = 12, Margin = paletteDock.Margin };
            content.Margin = new Thickness(0, 8, 8, 0); palette.Margin = new Thickness(0); palette.HorizontalAlignment = HorizontalAlignment.Stretch;
            columns.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
            Grid.SetColumn(palette, 1); columns.Children.Add(palette);
            control = columns;
        }
        else if (control is DockPanel sliderDock && sliderDock.Children.Count == 2 && sliderDock.Children[0] is Label && sliderDock.Children[1] is ValueSlider slider)
        {
            var label = sliderDock.Children[0]; sliderDock.Children.Clear();
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Margin = sliderDock.Margin };
            slider.Width = double.NaN; slider.HorizontalAlignment = HorizontalAlignment.Stretch;
            row.Children.Add(label); Grid.SetColumn(slider, 1); row.Children.Add(slider); control = row;
        }
        if (control is TabItem tab && tab.Content is Control childContent && childContent is not Grid)
        { tab.Content = null; tab.Content = new ScrollViewer { Content = childContent, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }; }
        WireControl(name, control);
        return control;
    }
    private void SetCommon(Control control, XElement element)
    {
        foreach (var attr in element.Attributes())
        {
            var value = attr.Value;
            switch (attr.Name.LocalName)
            {
                case "Margin": control.Margin = Thickness.Parse(value); break;
                case "Width": control.Width = double.Parse(value, CultureInfo.InvariantCulture); break;
                case "Height": control.Height = double.Parse(value, CultureInfo.InvariantCulture); break;
                case "MinWidth": control.MinWidth = double.Parse(value, CultureInfo.InvariantCulture); break;
                case "MinHeight": control.MinHeight = double.Parse(value, CultureInfo.InvariantCulture); break;
                case "HorizontalAlignment": if (Enum.TryParse<HorizontalAlignment>(value, out var ha)) control.HorizontalAlignment = ha; break;
                case "VerticalAlignment": if (Enum.TryParse<VerticalAlignment>(value, out var va)) control.VerticalAlignment = va; break;
                case "Grid.Column": Grid.SetColumn(control, int.Parse(value)); break;
                case "Grid.Row": Grid.SetRow(control, int.Parse(value)); break;
                case "Grid.ColumnSpan": Grid.SetColumnSpan(control, int.Parse(value)); break;
                case "Grid.RowSpan": Grid.SetRowSpan(control, int.Parse(value)); break;
                case "DockPanel.Dock": if (Enum.TryParse<Dock>(value, out var dock)) DockPanel.SetDock(control, dock); break;
                case "FontSize": if (control is TemplatedControl t) t.FontSize = double.Parse(value, CultureInfo.InvariantCulture); else if (control is TextBlock text) text.FontSize = double.Parse(value, CultureInfo.InvariantCulture); break;
                case "Padding": if (control is TemplatedControl templated) templated.Padding = Thickness.Parse(value); break;
                case "IsEnabled":
                    var match = Regex.Match(value, @"ElementName\s*=\s*([A-Za-z0-9_]+)");
                    if (match.Success && value.Contains("IsChecked")) enabledDependencies.Add((control, match.Groups[1].Value));
                    break;
            }
        }
        if (control is NumericUpDown number)
        {
            number.MinWidth = Math.Max(86, number.MinWidth);
            if (!double.IsNaN(number.Width)) number.Width = Math.Max(86, number.Width);
            number.Height = 28;
        }
        if (control is TextBox && Attr(element, "Name") == "barColorHex") { control.Width = 90; control.Height = 28; }
        if (control is ValueSlider && !double.IsNaN(control.Height)) control.Height = Math.Max(28, control.Height);
        if (module == "miditrail" && control is DockPanel && Attr(element, "DockPanel.Dock") == "Bottom")
        { control.Height = 28; control.Margin = new Thickness(0, 12, 0, 0); }
    }
    private void Localize(Control control, string property, string text)
    {
        if (text.StartsWith("{DynamicResource "))
        {
            var key = text[17..^1].Trim(); localized.Add((control, property, key)); text = labels.GetValueOrDefault(key, key);
        }
        PutText(control, property, text);
    }
    private static void PutText(Control control, string property, string text)
    {
        if (control is TabItem tab && property == "Header") tab.Header = text;
        else if (control is TextBlock block) block.Text = text;
        else if (control is TextBox box) box.Text = text;
        else if (control is ContentControl content) content.Content = text;
    }
    private string FieldName(string name) => name switch
    {
        "sameWidth" => "sameWidthNotes", "middleCSquare" => "middleC", "noteDeltaScreenTime" => "deltaTimeOnScreen",
        "FOVSlider" => "FOV", "viewAngSlider" => "camAng", "viewTurnSlider" => "camRot", "viewSpinSlider" => "camSpin", "renderDistSlider" => "viewdist", "renderDistBackSlider" => "viewback",
        "camOffsetX" => "viewOffset", "camOffsetY" => "viewHeight", "camOffsetZ" => "viewPan",
        "fontSelect" => "fontName", "alignSelect" => "textAlignment", "textTemplate" => "text", "csvPath" => "csvOutput", "ZeroPadding" => "PaddingZeroes",
        "BPMint" => "BPMintPad", "BPMDecPt" => "BPMDecPtPad", "NoteCount" => "NoteCountPad", "Polyphony" => "PolyphonyPad", "NPS" => "NPSPad", "Ticks" => "TicksPad", "Bars" => "BarCountPad", "Frames" => "FrCountPad", _ => name
    };
    private bool Angle(string name) => name is "FOVSlider" or "viewAngSlider" or "viewTurnSlider" or "viewSpinSlider";
    private double ToUi(string name, double value) => name == "lastNote" ? value - 1 : name == "pianoHeight" ? value * 100 : Angle(name) ? value / Math.PI * 180 : value;
    private double FromUi(string name, double value) => name == "lastNote" ? value + 1 : name == "pianoHeight" ? value / 100 : Angle(name) ? value / 180 * Math.PI : value;
    private object? Get(string field) => settings.GetType().GetField(field)?.GetValue(settings);
    private void Set(string field, object value)
    {
        var info = settings.GetType().GetField(field);
        if (info == null) return;
        info.SetValue(settings, info.FieldType.IsEnum ? Enum.ToObject(info.FieldType, Convert.ToInt32(value)) : Convert.ChangeType(value, info.FieldType, CultureInfo.InvariantCulture));
        Changed?.Invoke();
    }
    private void WireControl(string name, Control control)
    {
        if (name.Length == 0) return;
        var field = settings.GetType().GetField(FieldName(name));
        if (field != null)
        {
            void Refresh()
            {
                var value = field.GetValue(settings);
                switch (control)
                {
                    case NumericUpDown number: number.Value = (decimal)ToUi(name, Convert.ToDouble(value)); break;
                    case ValueSlider slider: slider.Value = ToUi(name, Convert.ToDouble(value)); break;
                    case CheckBox checkbox: checkbox.IsChecked = (bool)value!; break;
                    case TextBox text: text.Text = value?.ToString(); break;
                    case ComboBox combo when field.FieldType.IsEnum: combo.SelectedIndex = Convert.ToInt32(value); break;
                    case ComboBox combo: combo.SelectedItem = value?.ToString(); break;
                }
            }
            refreshers.Add(Refresh); refreshing = true; Refresh(); refreshing = false;
            switch (control)
            {
                case NumericUpDown number: number.ValueChanged += (_, _) => { if (!refreshing && number.Value.HasValue) Set(field.Name, FromUi(name, (double)number.Value.Value)); }; break;
                case ValueSlider slider: slider.ValueChanged += value => { if (!refreshing) Set(field.Name, FromUi(name, value)); }; break;
                case CheckBox checkbox: checkbox.IsCheckedChanged += (_, _) => { if (!refreshing) Set(field.Name, checkbox.IsChecked == true); }; break;
                case TextBox text: text.TextChanged += (_, _) => { if (!refreshing) Set(field.Name, text.Text ?? ""); }; break;
                case ComboBox combo: combo.SelectionChanged += (_, _) => { if (!refreshing && combo.SelectedIndex >= 0) Set(field.Name, field.FieldType.IsEnum ? combo.SelectedIndex : combo.SelectedItem?.ToString() ?? ""); }; break;
            }
        }
        if (name is "useCommas" or "useNothing" && control is RadioButton radio)
        {
            radio.IsChecked = Convert.ToInt32(Get("thousandSeparator") ?? 0) == (name == "useCommas" ? 0 : 2);
            radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) Set("thousandSeparator", name == "useCommas" ? 0 : 2); };
        }
        if (name == "barColorHex" && control is TextBox color)
        {
            color.Text = string.Concat(new[] { "topBarR", "topBarG", "topBarB" }.Select(f => Math.Clamp((int)(Convert.ToDouble(Get(f) ?? 0) * 255), 0, 255).ToString("X2")));
            color.TextChanged += (_, _) =>
            {
                if (color.Text?.Length == 6 && int.TryParse(color.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                { Set("topBarR", (rgb >> 16 & 255) / 255f); Set("topBarG", (rgb >> 8 & 255) / 255f); Set("topBarB", (rgb & 255) / 255f); }
            };
        }
        if (control is Button button) button.Click += async (_, _) => { try { await HandleAction(name); } catch (Exception e) { await window.ShowError(e.Message); } };
    }
    private Control PaletteControl()
    {
        var dock = new DockPanel { LastChildFill = true, Width = 184, Margin = new Thickness(0, 10, 10, 10) };
        var reload = new Button { Height = 26, Margin = new Thickness(0, 0, 0, 6) }; Localize(reload, "Content", "{DynamicResource palettes_reload}"); DockPanel.SetDock(reload, Dock.Top); dock.Children.Add(reload); reload.Click += (_, _) => ActionRequested?.Invoke("reload-palettes");
        var folder = new Button { Height = 26, Margin = new Thickness(0, 10, 0, 0), FontSize = 12, Padding = new Thickness(5, 0) }; Localize(folder, "Content", "{DynamicResource palettes_openFolder}"); DockPanel.SetDock(folder, Dock.Bottom); dock.Children.Add(folder); folder.Click += async (_, _) => await OpenFolder(PaletteService.PaletteDirectory);
        var editRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 3, Margin = new Thickness(0, 10, 0, 0) };
        var create = new Button { Content = "New", Height = 26, FontSize = 11, Padding = new Thickness(1, 0) };
        var edit = new Button { Content = "Edit", Height = 26, FontSize = 11, Padding = new Thickness(1, 0) };
        var delete = paletteDelete = new Button { Content = "Delete", Height = 26, FontSize = 11, Padding = new Thickness(1, 0), IsEnabled = false };
        ToolTip.SetTip(create, "New"); ToolTip.SetTip(edit, "Edit"); ToolTip.SetTip(delete, "Delete selected custom color set");
        create.Click += (_, _) => ActionRequested?.Invoke("new-palette");
        edit.Click += (_, _) => ActionRequested?.Invoke("edit-palette");
        delete.Click += (_, _) => ActionRequested?.Invoke("delete-palette");
        editRow.Children.Add(create); Grid.SetColumn(edit, 1); editRow.Children.Add(edit); Grid.SetColumn(delete, 2); editRow.Children.Add(delete);
        DockPanel.SetDock(editRow, Dock.Bottom); dock.Children.Add(editRow);
        var random = paletteRandom = new CheckBox { IsChecked = Palette.Randomized, Margin = new Thickness(0, 5, 0, 0), FontSize = 12 }; Localize(random, "Content", "{DynamicResource palettes_randomise}"); DockPanel.SetDock(random, Dock.Bottom); dock.Children.Add(random); random.IsCheckedChanged += (_, _) => { if (!refreshing) ActionRequested?.Invoke("randomize-palette:" + (random.IsChecked == true)); };
        paletteList = new ListBox { ItemTemplate = new FuncDataTemplate<string>((name, _) => MainWindow.PaletteLabel(root, name!)) }; dock.Children.Add(paletteList);
        paletteList.SelectionChanged += (_, _) =>
        {
            string? selected = paletteList.SelectedItem as string;
            if (paletteDelete != null) paletteDelete.IsEnabled = selected != null && PaletteDocument.CanDelete(selected);
            if (!refreshing && selected != null) { Palette.Select(selected); Set("palette", Palette.SelectedImage); PaletteChanged?.Invoke(Palette.SelectedImage); }
        };
        refreshers.Add(() => SetPalettes(Palette.GetPaletteNames()));
        ReloadPalettes(); return dock;
    }
    private void ReloadPalettes()
    {
        SetPalettes(Palette.GetPaletteNames());
    }
    public void SetPalettes(IEnumerable<string> names)
    {
        if (paletteList == null) return;
        bool wasRefreshing = refreshing;
        refreshing = true;
        try
        {
            Palette.Select(Get("palette")?.ToString() ?? "Random");
            Set("palette", Palette.SelectedImage);
            var values = names.Distinct().OrderBy(n => n == "Random" ? 0 : 1).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            paletteList.ItemsSource = values;
            paletteList.SelectedItem = Palette.SelectedImage;
            if (paletteDelete != null) paletteDelete.IsEnabled = paletteList.SelectedItem is string selected && PaletteDocument.CanDelete(selected);
            if (paletteRandom != null) paletteRandom.IsChecked = Palette.Randomized;
        }
        finally { refreshing = wasRefreshing; }
    }
    private void InitializeLists()
    {
        if (controls.TryGetValue("fontSelect", out var fontControl) && fontControl is ComboBox font)
        {
            font.ItemsSource = FontManager.Current.SystemFonts.Select(f => f.Name).OrderBy(s => s).ToArray(); font.SelectedItem = Get("fontName")?.ToString();
        }
        if (controls.TryGetValue("fontStyle", out var styleControl) && styleControl is ComboBox style)
        { style.ItemsSource = new[] { "Regular", "Bold", "Italic", "Bold Italic" }; style.SelectedIndex = Math.Clamp(Convert.ToInt32(Get("fontStyle") ?? 0), 0, 5); }
        if (module == "notecounter") ReloadTemplates();
        if (module == "miditrail") { ReloadProfiles(); ReloadAuras(); }
        if (module == "textured") ReloadPacks();
    }
    private string TemplatesDirectory => Path.Combine(root, "Plugins/Assets/NoteCounter/Templates");
    private void ReloadTemplates()
    {
        if (!controls.TryGetValue("templates", out var control) || control is not ComboBox combo) return;
        Directory.CreateDirectory(TemplatesDirectory);
        var files = Directory.GetFiles(TemplatesDirectory, "*.txt").OrderBy(p => p).ToArray();
        combo.ItemsSource = files.Select(Path.GetFileNameWithoutExtension).ToArray();
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string selected) { Set("text", File.ReadAllText(Path.Combine(TemplatesDirectory, selected + ".txt"))); RefreshAll(); } };
    }
    private string ProfilesFile => Path.Combine(root, "Plugins/Assets/MIDITrail/Profiles.json");
    private void ReloadProfiles()
    {
        if (!controls.TryGetValue("profileSelect", out var control) || control is not ComboBox combo) return;
        if (File.Exists(ProfilesFile)) profiles = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(ProfilesFile)) ?? new();
        combo.ItemsSource = profiles.Keys.ToArray();
        if (!profileSelectionWired)
        {
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string name && profiles.TryGetValue(name, out var data)) { ApplySettings(data); if (controls.GetValueOrDefault("profileName") is TextBox box) box.Text = name; } };
            profileSelectionWired = true;
        }
    }
    private void ApplySettings(JsonElement data)
    {
        var source = data.Deserialize(settings.GetType(), jsonOptions);
        if (source == null) return;
        var previousAura = Get("selectedAuraImage") as string;
        foreach (var field in settings.GetType().GetFields()) if (!field.IsInitOnly) field.SetValue(settings, field.GetValue(source));
        RefreshAll(); Changed?.Invoke();
        if (module == "miditrail" && previousAura != Get("selectedAuraImage") as string)
            ActionRequested?.Invoke("aura-changed");
    }
    private void RefreshAll() { refreshing = true; try { foreach (var refresh in refreshers) refresh(); } finally { refreshing = false; } }
    private Control BuildGenericSettings()
    {
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        foreach (var field in settings.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsInitOnly))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var name = Regex.Replace(field.Name, "([a-z])([A-Z])", "$1 $2");
            var value = field.GetValue(settings);
            row.Children.Add(new Label { Content = name, MinWidth = 160 });
            if (field.FieldType == typeof(bool))
            {
                var check = new CheckBox { IsChecked = (bool)value! }; check.IsCheckedChanged += (_, _) => Set(field.Name, check.IsChecked == true); row.Children.Add(check);
            }
            else if (field.FieldType.IsEnum)
            {
                var combo = new ComboBox { ItemsSource = Enum.GetNames(field.FieldType), SelectedItem = value?.ToString() };
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string selected) { field.SetValue(settings, Enum.Parse(field.FieldType, selected)); Changed?.Invoke(); } }; row.Children.Add(combo);
            }
            else if (field.FieldType == typeof(string))
            {
                var text = new TextBox { Text = value?.ToString(), MinWidth = 240 }; text.TextChanged += (_, _) => Set(field.Name, text.Text ?? ""); row.Children.Add(text);
            }
            else if (field.FieldType.IsPrimitive || field.FieldType == typeof(decimal))
            {
                var number = new NumericUpDown { Minimum = -100000000, Maximum = 100000000, Value = Convert.ToDecimal(value), Width = 130, FormatString = "0.####" }; number.ValueChanged += (_, _) => { if (number.Value.HasValue) Set(field.Name, number.Value.Value); }; row.Children.Add(number);
            }
            else continue;
            panel.Children.Add(row);
        }
        return new ScrollViewer { Content = panel };
    }
    private void ReloadAuras()
    {
        if (!controls.TryGetValue("imagesList", out var control) || control is not ListBox list) return;
        var dir = Path.Combine(root, "Plugins/Assets/MIDITrail/Aura");
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir).Where(f => new[] { ".png", ".jpg", ".bmp" }.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray() : Array.Empty<string>();
        auraFiles.Clear();
        foreach (var file in files) auraFiles[Path.GetFileNameWithoutExtension(file)] = file;
        bool reload = auraSelectionWired;
        if (!auraSelectionWired)
        {
            list.SelectionChanged += (_, _) =>
            {
                if (refreshing || list.SelectedItem is not string name) return;
                Set("selectedAuraImage", name);
                RefreshAuraSelection();
                ActionRequested?.Invoke("aura-changed");
            };
            refreshers.Add(RefreshAuraSelection);
            auraSelectionWired = true;
        }
        bool wasRefreshing = refreshing;
        refreshing = true;
        try { list.ItemsSource = auraFiles.Keys.ToArray(); RefreshAuraSelection(); }
        finally { refreshing = wasRefreshing; }
        if (reload) ActionRequested?.Invoke("aura-changed");
    }
    private void RefreshAuraSelection()
    {
        if (controls.GetValueOrDefault("imagesList") is not ListBox list) return;
        var name = Get("selectedAuraImage")?.ToString() ?? "";
        bool wasRefreshing = refreshing;
        refreshing = true;
        try
        {
            list.SelectedItem = auraFiles.ContainsKey(name) ? name : null;
            if (controls.GetValueOrDefault("imagePreview") is Image image)
            {
                var previous = image.Source;
                image.Source = auraFiles.TryGetValue(name, out var file) ? new Bitmap(file) : null;
                (previous as IDisposable)?.Dispose();
            }
        }
        finally { refreshing = wasRefreshing; }
    }
    private void ReloadPacks()
    {
        if (controls.GetValueOrDefault("pluginList") is not ListBox list) return;
        var dir = Path.Combine(root, "Plugins/Assets/Textured/Resources");
        var paths = Directory.Exists(dir) ? Directory.GetFileSystemEntries(dir).Where(f => Directory.Exists(f) || new[] { ".zip", ".zrp", ".rar", ".7z", ".tar" }.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToArray() : Array.Empty<string>();
        list.ItemsSource = paths.Select(Path.GetFileName).ToArray();
        list.SelectionChanged += (_, _) => { if (!selectingPack && list.SelectedItem is string selected) ActionRequested?.Invoke("select-pack:" + Path.Combine(dir, selected)); };
    }
    private async Task OpenFolder(string path) { Directory.CreateDirectory(path); await window.Launcher.LaunchUriAsync(new Uri(path + Path.DirectorySeparatorChar)); }
    private async Task HandleAction(string name)
    {
        if (name.EndsWith("Preset")) { CameraPreset(name); return; }
        switch (name)
        {
            case "SetDefault":
                foreach (var pair in new Dictionary<string, int> { ["BPMintPad"] = 3, ["BPMDecPtPad"] = 2, ["NoteCountPad"] = 5, ["PolyphonyPad"] = 3, ["NPSPad"] = 3, ["TicksPad"] = 5, ["BarCountPad"] = 3, ["FrCountPad"] = 5 }) Set(pair.Key, pair.Value);
                RefreshAll(); return;
            case "browseOutputSaveButton":
                var file = await window.SaveFile("Save CSV", "csv", new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } });
                if (file != null) { Set("csvOutput", file); RefreshAll(); } return;
            case "defaultsButton": ApplySettings(JsonSerializer.SerializeToElement(Activator.CreateInstance(settings.GetType()), jsonOptions)); return;
            case "newProfile":
                var profileName = (controls.GetValueOrDefault("profileName") as TextBox)?.Text?.Trim();
                if (string.IsNullOrEmpty(profileName)) throw new InvalidOperationException("Please write a name for the profile");
                if (module == "notecounter" && File.Exists(Path.Combine(TemplatesDirectory, Path.GetFileName(profileName) + ".txt")) && !await window.ConfirmAsync("Are you sure you want to override template " + profileName + "?")) return;
                if (module != "notecounter" && profiles.ContainsKey(profileName) && !await window.ConfirmAsync("Are you sure you want to override profile " + profileName + "?")) return;
                if (module == "notecounter") { Directory.CreateDirectory(TemplatesDirectory); File.WriteAllText(Path.Combine(TemplatesDirectory, Path.GetFileName(profileName) + ".txt"), Get("text")?.ToString()); ReloadTemplates(); }
                else { profiles[profileName] = JsonSerializer.SerializeToElement(settings, settings.GetType(), jsonOptions); WriteProfiles(); ReloadProfiles(); }
                return;
            case "deleteProfile": if (controls.GetValueOrDefault("profileSelect") is ComboBox { SelectedItem: string selected }) { profiles.Remove(selected); WriteProfiles(); ReloadProfiles(); } return;
            case "reload": if (module == "notecounter") ReloadTemplates(); else ReloadAuras(); return;
            case "openFolder": await OpenFolder(module == "notecounter" ? TemplatesDirectory : Path.Combine(root, "Plugins/Assets/MIDITrail/Aura")); return;
            case "openFolderButton": await OpenFolder(Path.Combine(root, "Plugins/Assets/Textured/Resources")); return;
            case "reloadListButton": ReloadPacks(); return;
            default: ActionRequested?.Invoke(name); return;
        }
    }
    private void WriteProfiles() { Directory.CreateDirectory(Path.GetDirectoryName(ProfilesFile)!); File.WriteAllText(ProfilesFile, JsonSerializer.Serialize(profiles, jsonOptions)); }
    private void CameraPreset(string name)
    {
        double[]? values = name switch
        {
            "farPreset" => new[] { 0.5, 0.4, 0, 60, 32.08, 0, 0, 14, 0.2 },
            "mediumPreset" => new[] { 0.52, 0.37, 0, 60, 34.98, 0, 0, 5.52, 0.2 },
            "closePreset" => new[] { 0.55, 0.33, 0, 60, 39.62, 0, 0, 3.06, 0.2 },
            "topPreset" => new[] { 10, -3.77, -1.53, 26, 90, -90, 0, 7.93, 0.64 },
            "perspectivePreset" => new[] { 0.67, 1.07, -0.32, 60, 33.24, -13.84, 0, 14, 0.98 }, _ => null
        };
        if (values == null) return;
        var names = new[] { "camOffsetY", "camOffsetX", "camOffsetZ", "FOVSlider", "viewAngSlider", "viewTurnSlider", "viewSpinSlider", "renderDistSlider", "renderDistBackSlider" };
        for (var i = 0; i < names.Length; i++) Set(FieldName(names[i]), FromUi(names[i], values[i]));
        RefreshAll();
    }
}
