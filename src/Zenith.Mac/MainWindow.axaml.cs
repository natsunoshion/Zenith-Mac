using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Zenith.Core.Scripted;
using Zenith.Core.Rendering;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Zenith.Mac;

public partial class MainWindow : Window
{
    public MainWindowState State { get; } = new();
    public string AssetRoot { get; private set; } = "";
    public event Action<string>? ActionRequested;
    public event Action<string>? ModuleChanged;
    public event Action<string>? SkinChanged;
    public event Action<string>? PaletteChanged;
    public event Action<string>? ProfileChanged;
    public event Action? ScriptSettingChanged;
    private readonly List<(object Source, EventInfo Event, Delegate Handler)> scriptSubscriptions = new();
    private readonly List<(object Source, string Property)> scriptValues = new();
    private ScriptedProfiles? scriptProfiles;
    private bool settingItems;
    private bool settingTheme;
    private bool exportingUiSnapshots;
    private BuiltinSettingsBuilder? builtinBuilder;
    private string selectedLanguage = "en";
    private readonly Dictionary<string, string> languages = new();
    public MainWindow()
    {
        foreach (var pair in EnglishResources.Values) Resources[pair.Key] = pair.Value;
        AvaloniaXamlLoader.Load(this);
        DataContext = State;
        this.FindControl<ComboBox>("ThemeSelect")!.ItemsSource = ThemeManager.Options;
        ApplyInterfaceTheme();
        this.FindControl<ComboBox>("CrfPresetSelect")!.ItemsSource = new[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" };
        State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(State.Status) && IsLoadDiagnostic(State.Status)) Console.WriteLine(State.Status);
            if (e.PropertyName is nameof(State.Status) or nameof(State.IsBusy) or nameof(State.IsRendering)
                or nameof(State.IsPreviewing) or nameof(State.Paused)) Dispatcher.UIThread.Post(UpdateStatusVisibility);
            if (e.PropertyName == nameof(State.LanguageCode)) Dispatcher.UIThread.Post(() => LoadLanguage(State.LanguageCode));
            if (e.PropertyName == nameof(State.ThemeId)) ApplyInterfaceTheme();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Space && e.Source is not TextBox && State.IsPreviewing) { State.Paused = !State.Paused; e.Handled = true; } };
        InitializeAssets(AssetPaths.Root);
        Closed += (_, _) => ClearScriptSubscriptions();
    }
    private static bool IsLoadDiagnostic(string status) => status.EndsWith(" textures loaded", StringComparison.Ordinal)
        || (status.Contains(" notes, ", StringComparison.Ordinal) && status.Contains(" tracks, ", StringComparison.Ordinal));
    private void UpdateStatusVisibility()
    {
        var status = State.Status;
        var transient = status == "Previewing" || status.StartsWith("Rendering ", StringComparison.Ordinal);
        this.FindControl<Border>("StatusBar")!.IsVisible = State.IsPreviewing ||
            (!string.IsNullOrWhiteSpace(status) && !IsLoadDiagnostic(status)
                && status != "Stopped" && (State.IsBusy || !transient));
        this.FindControl<TextBlock>("StatusText")!.Text = State.IsPreviewing && status == "Previewing"
            ? State.Paused ? "Preview · Paused" : "Preview · Playing"
            : status;
        var progress = this.FindControl<ProgressBar>("TaskProgress")!;
        progress.IsVisible = State.IsBusy && !State.IsPreviewing;
        progress.IsIndeterminate = !State.IsRendering && !status.StartsWith("Loading MIDI", StringComparison.Ordinal);
    }
    public void InitializeAssets(string assetRoot)
    {
        AssetRoot = assetRoot;
        var icon = Path.Combine(assetRoot, "icon.png");
        if (!File.Exists(icon))
        {
            var candidate = Path.Combine(Environment.CurrentDirectory, "upstream/Black-Midi-Render/icon.png");
            if (File.Exists(candidate)) icon = candidate;
        }
        if (File.Exists(icon)) this.FindControl<Image>("AppLogo")!.Source = new Bitmap(icon);
        var languageRoot = Path.Combine(assetRoot, "Languages");
        languages.Clear();
        if (Directory.Exists(languageRoot))
            foreach (var directory in Directory.GetDirectories(languageRoot).OrderBy(p => p))
            {
                var file = Path.Combine(directory, "window.xaml");
                if (!File.Exists(file)) continue;
                var name = ReadDictionary(file).GetValueOrDefault("LanguageName", Path.GetFileName(directory));
                languages[name] = Path.GetFileName(directory);
            }
        settingItems = true;
        var selector = this.FindControl<ComboBox>("LanguageSelect")!;
        selector.ItemsSource = languages.Keys.ToArray();
        selector.SelectedItem = languages.FirstOrDefault(p => p.Value == selectedLanguage).Key;
        settingItems = false;
        LoadLanguage(selectedLanguage);
    }
    private static Dictionary<string, string> ReadDictionary(string path) => XDocument.Load(path).Root!.Elements().Where(e => e.Name.LocalName == "String").ToDictionary(e => e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Value, e => e.Value);
    private void LoadLanguage(string language)
    {
        selectedLanguage = language;
        foreach (var pair in EnglishResources.Values) Resources[pair.Key] = pair.Value;
        foreach (var name in new[] { "window", "scripted" })
        {
            var file = Path.Combine(AssetRoot, "Languages", language, name + ".xaml");
            if (File.Exists(file)) foreach (var pair in ReadDictionary(file)) Resources[pair.Key] = pair.Value;
        }
        builtinBuilder?.SetLanguage(language);
        State.LanguageCode = language;
        settingItems = true;
        this.FindControl<ComboBox>("LanguageSelect")!.SelectedItem = languages.FirstOrDefault(p => p.Value == language).Key;
        settingItems = false;
    }
    private void ApplyInterfaceTheme()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(ApplyInterfaceTheme); return; }
        ThemeManager.Apply(this, State.ThemeId);
        settingTheme = true;
        try { this.FindControl<ComboBox>("ThemeSelect")!.SelectedItem = ThemeManager.Options.First(t => t.Id == State.ThemeId); }
        finally { settingTheme = false; }
    }
    private void ThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!settingTheme && sender is ComboBox { SelectedItem: ThemeOption option }) State.ThemeId = option.Id;
    }
    private void LanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!settingItems && sender is ComboBox { SelectedItem: string language } && languages.TryGetValue(language, out var code)) LoadLanguage(code);
    }
    private void TitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control source && (source is Button || source.GetVisualAncestors().Any(parent => parent is Button))) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
    private void MinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseClick(object? sender, RoutedEventArgs e) => Close();
    public void SelectPage(int index)
    {
        var pages = new[] { "GeneralPage", "ModulesPage", "ModuleSettingsPage", "RenderPage" };
        var tabs = new[] { "GeneralTab", "ModulesTab", "ModuleSettingsTab", "RenderTab" };
        for (var i = 0; i < pages.Length; i++)
        {
            this.FindControl<Grid>(pages[i])!.IsVisible = i == index;
            this.FindControl<Button>(tabs[i])!.Classes.Set("active", i == index);
        }
    }
    private void SelectMainPage(object? sender, RoutedEventArgs e) { if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var index)) SelectPage(index); }
    private async void ActionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        try
        {
            switch (action)
            {
                case "load":
                    var midi = await OpenFile("Load MIDI", new FilePickerFileType("MIDI files") { Patterns = new[] { "*.mid", "*.midi" } });
                    if (midi == null) return;
                    State.MidiPath = midi;
                    break;
                case "browse-background":
                    var bg = await OpenFile("Use Background", FilePickerFileTypes.ImageAll);
                    if (bg == null) return;
                    State.BackgroundPath = bg;
                    break;
                case "browse-audio":
                    var audio = await OpenFile("Include Audio", new FilePickerFileType("Audio") { Patterns = new[] { "*.wav", "*.mp3", "*.flac", "*.ogg", "*.m4a", "*.aac" } });
                    if (audio == null) return;
                    State.AudioPath = audio;
                    break;
                case "browse-video":
                case "browse-alpha":
                    var path = await SaveFile(action == "browse-alpha" ? "Render Transparency Mask" : "Save video", "mp4", new FilePickerFileType("Video") { Patterns = new[] { "*.mp4", "*.mkv", "*.mov", "*.avi" } });
                    if (path == null) return;
                    if (action == "browse-video") State.VideoPath = path; else State.AlphaPath = path;
                    break;
                case "toggle-audio": State.AudioEnabled = !State.AudioEnabled; State.AudioToggleLabel = State.AudioEnabled ? "Disable Audio" : "Enable Audio"; break;
                case "save-profile": await SaveProfile(); return;
                case "delete-profile": DeleteProfile(); return;
                case "default-profile": ResetProfile(); return;
                case "open-skins-folder": await OpenFolder(Path.Combine(AssetRoot, "Plugins/Assets/Scripted/Resources")); return;
                case "open-palettes-folder": await OpenFolder(PaletteService.PaletteDirectory); return;
            }
            ActionRequested?.Invoke(action);
        }
        catch (Exception ex) { await ShowError(ex.Message); }
    }
    public async Task<string?> OpenFile(string title, FilePickerFileType type)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = new[] { type, FilePickerFileTypes.All } });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    public async Task<string?> SaveFile(string title, string extension, FilePickerFileType type)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, DefaultExtension = extension, FileTypeChoices = new[] { type }, SuggestedFileName = Path.GetFileNameWithoutExtension(State.MidiPath) + "." + extension, ShowOverwritePrompt = true });
        return file?.TryGetLocalPath();
    }
    private async Task OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        await Launcher.LaunchUriAsync(new Uri(path + Path.DirectorySeparatorChar));
    }
    public void SetModules(IEnumerable<ModuleEntry> modules, string? selectedId = null)
    {
        settingItems = true;
        var order = new[] { "classic", "flat", "miditrail", "notecounter", "pfa", "scripted", "textured" };
        var items = modules.OrderBy(m => Array.IndexOf(order, m.Id) is var index && index >= 0 ? index : 99).ToArray(); var list = this.FindControl<ListBox>("ModulesList")!;
        list.ItemsSource = items; list.SelectedItem = items.FirstOrDefault(m => m.Id == selectedId) ?? items.FirstOrDefault();
        settingItems = false;
        if (list.SelectedItem is ModuleEntry entry) ApplyModule(entry, false);
    }
    private void ModuleSelected(object? sender, SelectionChangedEventArgs e) { if (!settingItems && sender is ListBox { SelectedItem: ModuleEntry module }) ApplyModule(module, true); }
    private void ApplyModule(ModuleEntry module, bool notify)
    {
        State.ModuleDescription = module.Description;
        SetImage(this.FindControl<Image>("ModulePreview")!, module.PreviewPath);
        var scripted = module.Id.Contains("script", StringComparison.OrdinalIgnoreCase) || module.Name == "Scripted";
        this.FindControl<TabControl>("ScriptedTabs")!.IsVisible = scripted;
        this.FindControl<ContentControl>("BuiltinSettingsHost")!.IsVisible = !scripted;
        if (notify) ModuleChanged?.Invoke(module.Id);
    }
    public void SetSkins(IEnumerable<SkinEntry> skins, string? selectedPath = null)
    {
        settingItems = true;
        var items = skins.OrderBy(s => s.Name, StringComparer.Ordinal).ToArray(); var list = this.FindControl<ListBox>("SkinsList")!;
        list.ItemsSource = items.Select(s =>
        {
            var label = new TextBlock { Text = s.Name, TextTrimming = TextTrimming.CharacterEllipsis };
            ThemeManager.BindBrush(label, TextBlock.ForegroundProperty, s.IsArchive ? "Success" : "Text");
            ToolTip.SetTip(label, s.Name);
            return new ListBoxItem { Content = label, Tag = s.Path };
        }).ToArray();
        list.SelectedItem = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string?)i.Tag == selectedPath);
        settingItems = false;
    }
    private void SkinSelected(object? sender, SelectionChangedEventArgs e) { if (!settingItems && sender is ListBox { SelectedItem: ListBoxItem { Tag: string path } }) SkinChanged?.Invoke(path); }
    public void SetSkinPreview(byte[]? image, string description) { State.SkinDescription = description; SetImage(this.FindControl<Image>("SkinPreview")!, image); }
    public void SetPalettes(IEnumerable<string> names, string? selectedPalette = null, bool? randomized = null)
    {
        var values = names.Distinct().OrderBy(n => n == "Random" ? 0 : 1).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        settingItems = true;
        var list = this.FindControl<ListBox>("PalettesList")!;
        var selected = selectedPalette ?? list.SelectedItem as string;
        list.ItemTemplate = new FuncDataTemplate<string>((name, _) => PaletteLabel(AssetRoot, name!));
        list.ItemsSource = values;
        list.SelectedItem = values.Contains(selected) ? selected : values.FirstOrDefault(v => v == "Random") ?? values.FirstOrDefault();
        if (randomized.HasValue) State.RandomizePalette = randomized.Value;
        settingItems = false;
        if (this.FindControl<ContentControl>("BuiltinSettingsHost")!.IsVisible) builtinBuilder?.SetPalettes(values);
    }
    internal static TextBlock PaletteLabel(string assetRoot, string name)
    {
        var role = "Text";
        var file = Path.Combine(PaletteService.PaletteDirectory, name + ".png");
        if (File.Exists(file)) { using var image = new Bitmap(file); if (image.PixelSize.Width == 32) role = "Info"; }
        var label = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis };
        ThemeManager.BindBrush(label, TextBlock.ForegroundProperty, role);
        ToolTip.SetTip(label, name);
        return label;
    }
    private void RandomizePaletteChanged(object? sender, RoutedEventArgs e)
    {
        if (settingItems || sender is not CheckBox checkbox) return;
        // IsCheckedChanged precedes the two-way binding's source update.
        State.RandomizePalette = checkbox.IsChecked == true;
        ActionRequested?.Invoke("randomize-palette");
    }
    private void PaletteSelected(object? sender, SelectionChangedEventArgs e) { if (!settingItems && sender is ListBox { SelectedItem: string palette }) PaletteChanged?.Invoke(palette); }
    private static void SetImage(Image target, string? path) { target.Source = path != null && File.Exists(path) ? new Bitmap(path) : null; }
    private static void SetImage(Image target, byte[]? bytes) { target.Source = bytes is { Length: > 0 } ? new Bitmap(new MemoryStream(bytes)) : null; }
    public void SetBuiltinSettings(string moduleId, object settings)
    {
        if (settings is Control customControl)
        {
            builtinBuilder = null;
            this.FindControl<ContentControl>("BuiltinSettingsHost")!.Content = customControl;
            this.FindControl<TabControl>("ScriptedTabs")!.IsVisible = false;
            this.FindControl<ContentControl>("BuiltinSettingsHost")!.IsVisible = true;
            return;
        }
        builtinBuilder = new BuiltinSettingsBuilder(this, moduleId, settings, AssetRoot, selectedLanguage);
        builtinBuilder.ActionRequested += action => ActionRequested?.Invoke("builtin:" + moduleId + ":" + action);
        builtinBuilder.PaletteChanged += palette => PaletteChanged?.Invoke(palette);
        builtinBuilder.Changed += () => ScriptSettingChanged?.Invoke();
        this.FindControl<ContentControl>("BuiltinSettingsHost")!.Content = builtinBuilder.Build();
        if (settings.GetType().GetField("currPack")?.GetValue(settings) is TexturedRender.Pack texturedPack) builtinBuilder.SetTexturedPack(texturedPack);
        this.FindControl<TabControl>("ScriptedTabs")!.IsVisible = false;
        this.FindControl<ContentControl>("BuiltinSettingsHost")!.IsVisible = true;
    }
    public void SetScriptSettings(IEnumerable<object> settings, string? profilePath = null)
    {
        ClearScriptSubscriptions(); scriptValues.Clear();
        var panel = this.FindControl<StackPanel>("ScriptSettingsPanel")!;
        panel.Children.Clear();
        PopulateScriptSettings(settings, panel);
        scriptProfiles = null;
        this.FindControl<Grid>("ProfilePanel")!.IsVisible = scriptValues.Count > 0;
        this.FindControl<TabControl>("ScriptedTabs")!.IsVisible = true;
        this.FindControl<ContentControl>("BuiltinSettingsHost")!.IsVisible = false;

        RefreshProfiles();
    }
    private void ClearScriptSubscriptions() { foreach (var s in scriptSubscriptions) s.Event.RemoveEventHandler(s.Source, s.Handler); scriptSubscriptions.Clear(); }
    private static T Read<T>(object source, string property, T fallback = default!) => source.GetType().GetProperty(property)?.GetValue(source) is T value ? value : fallback;
    private void SetScriptValue(object source, string property, object value) { source.GetType().GetProperty(property)!.SetValue(source, value); ScriptSettingChanged?.Invoke(); }
    private void Subscribe<T>(object source, string eventName, Action<T> action)
    {
        var info = source.GetType().GetEvent(eventName);
        if (info == null) return;
        Action<T> handler = value => { if (Dispatcher.UIThread.CheckAccess()) action(value); else Dispatcher.UIThread.Post(() => action(value)); };
        info.AddEventHandler(source, handler); scriptSubscriptions.Add((source, info, handler));
    }
    private void PopulateScriptSettings(IEnumerable<object> settings, StackPanel panel)
    {
        foreach (var s in settings)
        {
            var label = Read(s, "Text", "");
            var padding = Read(s, "Padding", 10d);
            Control control;
            switch (s.GetType().Name)
            {
                case "UILabel": panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = Read(s, "FontSize", 16d), Margin = new Thickness(0, 4, 0, padding + 4) }); continue;
                case "UITabs":
                    var tabs = new TabControl();
                    if (Read<object?>(s, "Tabs") is IDictionary dict)
                        foreach (DictionaryEntry entry in dict)
                        {
                            var content = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
                            PopulateScriptSettings(((IEnumerable)entry.Value!).Cast<object>(), content);
                            tabs.Items.Add(new TabItem { Header = entry.Key, Content = content });
                        }
                    panel.Children.Add(tabs); continue;
                case "UINumber":
                    var number = new NumericUpDown { Minimum = (decimal)Read(s, "Minimum", 0d), Maximum = (decimal)Read(s, "Maximum", 100d), Value = (decimal)Read(s, "Value", 0d), Increment = (decimal)Read(s, "Step", 1d), MinWidth = 100, FormatString = "F" + Read(s, "DecialPoints", 0) };
                    number.ValueChanged += (_, _) => { if (number.Value.HasValue) SetScriptValue(s, "Value", (double)number.Value.Value); };
                    Subscribe<double>(s, "ValueChanged", v => number.Value = (decimal)Math.Clamp(v, (double)number.Minimum, (double)number.Maximum));
                    scriptValues.Add((s, "Value")); control = number; break;
                case "UINumberSlider":
                    var slider = new ValueSlider { Minimum = Read(s, "Minimum", 0d), Maximum = Read(s, "Maximum", 100d), TrueMinimum = Read(s, "TrueMinimum", 0d), TrueMaximum = Read(s, "TrueMaximum", 100d), Value = Read(s, "Value", 0d), Step = Read(s, "Step", 1d), DecimalPlaces = Read(s, "DecialPoints", 0), Logarithmic = Read(s, "Logarithmic", false), HorizontalAlignment = HorizontalAlignment.Stretch };
                    slider.ValueChanged += v => SetScriptValue(s, "Value", v);
                    Subscribe<double>(s, "ValueChanged", v => slider.Value = v);
                    scriptValues.Add((s, "Value")); control = slider; break;
                case "UIDropdown":
                    var drop = new ComboBox { ItemsSource = Read(s, "Options", Array.Empty<string>()), SelectedIndex = Read(s, "Index", 0), FontSize = 14, MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left };
                    drop.SelectionChanged += (_, _) => { if (drop.SelectedIndex >= 0) SetScriptValue(s, "Index", drop.SelectedIndex); };
                    Subscribe<int>(s, "IndexChanged", v => drop.SelectedIndex = v);
                    scriptValues.Add((s, "Index")); control = drop; break;
                case "UICheckbox":
                    var check = new CheckBox { Content = label, IsChecked = Read(s, "Checked", false), FontSize = 14 };
                    check.IsCheckedChanged += (_, _) => SetScriptValue(s, "Checked", check.IsChecked == true);
                    Subscribe<bool>(s, "ValueChanged", v => check.IsChecked = v);
                    scriptValues.Add((s, "Checked")); control = check; label = ""; break;
                default: throw new NotSupportedException("Unsupported script setting type: " + s.GetType().FullName);
            }
            control.IsEnabled = Read(s, "Enabled", true);
            Subscribe<bool>(s, "EnableToggled", value => control.IsEnabled = value);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(string.IsNullOrEmpty(label) ? "*" : "210,*"), ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, padding) };
            if (!string.IsNullOrEmpty(label))
            {
                row.Children.Add(new TextBlock { Text = label, FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(control, 1);
            }
            if (control is NumericUpDown) { control.Width = 120; control.HorizontalAlignment = HorizontalAlignment.Left; }
            row.Children.Add(control); panel.Children.Add(row);
        }
    }
    public void SetScriptPack(ScriptedPack pack)
    {
        SetScriptSettings(pack.SettingsUI.Cast<object>());
        SetSkinPreview(pack.Preview, pack.Description);
        scriptProfiles = new ScriptedProfiles(pack);
        RefreshProfiles();
        settingItems = true;
        var modules = this.FindControl<ListBox>("ModulesList")!;
        modules.SelectedItem = modules.Items.Cast<ModuleEntry>().FirstOrDefault(m => m.Id == "scripted");
        if (modules.SelectedItem is ModuleEntry selected) ApplyModule(selected, false);
        var skins = this.FindControl<ListBox>("SkinsList")!;
        skins.SelectedItem = skins.Items.Cast<ListBoxItem>().FirstOrDefault(s => Path.GetFullPath((string)s.Tag!) == Path.GetFullPath(pack.Path));
        settingItems = false;
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--ui-snapshots");
        if (!exportingUiSnapshots && index >= 0 && index + 1 < args.Length) { exportingUiSnapshots = true; DispatcherTimer.RunOnce(async () => await ExportUiSnapshots(args[index + 1]), TimeSpan.FromSeconds(2)); }
    }

    /// <summary>Exports the live Avalonia visual tree for reproducible UI layout review.</summary>
    public async Task ExportUiSnapshots(string directory)
    {
        Directory.CreateDirectory(directory);
        async Task Capture(string name)
        {
            await Task.Delay(100);
            var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(Bounds.Width * 2), (int)Math.Ceiling(Bounds.Height * 2)), new Vector(192, 192));
            bitmap.Render(this);
            bitmap.Save(Path.Combine(directory, name + ".png"));
            bitmap.Dispose();
        }
        var names = new[] { "general", "modules", "script-resources", "render" };
        for (var page = 0; page < names.Length; page++) { SelectPage(page); await Capture(names[page]); }
        SelectPage(2);
        var tabs = this.FindControl<TabControl>("ScriptedTabs")!;
        tabs.SelectedIndex = 1; await Capture("script-settings");
        var scriptTabs = this.FindControl<StackPanel>("ScriptSettingsPanel")!.Children.OfType<TabControl>().FirstOrDefault();
        if (scriptTabs != null)
            for (var i = 0; i < scriptTabs.ItemCount; i++) { scriptTabs.SelectedIndex = i; await Capture("script-settings-tab-" + i.ToString("00")); }
        Console.WriteLine($"Scripted UI: {scriptValues.Count} value controls, {scriptProfiles?.Names.Count ?? 0} profiles");
        tabs.SelectedIndex = 2; await Capture("script-misc");
        foreach (var pair in new[] { ("classic", "ClassicRender.Settings"), ("flat", "FlatRender.Settings"), ("pfa", "PFARender.Settings"), ("miditrail", "MIDITrailRender.Settings"), ("notecounter", "NoteCountRender.Settings"), ("textured", "TexturedRender.Settings") })
        {
            var type = typeof(ScriptedPack).Assembly.GetType(pair.Item2)!;
            var builder = new BuiltinSettingsBuilder(this, pair.Item1, Activator.CreateInstance(type)!, AssetRoot, selectedLanguage);
            var view = builder.Build();
            this.FindControl<ContentControl>("BuiltinSettingsHost")!.Content = view;
            this.FindControl<ContentControl>("BuiltinSettingsHost")!.IsVisible = true;
            tabs.IsVisible = false;
            await Capture(pair.Item1 + "-settings");
            var moduleTabs = view.GetLogicalDescendants().OfType<TabControl>().FirstOrDefault();
            if (moduleTabs != null)
                for (var i = 1; i < moduleTabs.ItemCount; i++) { moduleTabs.SelectedIndex = i; await Capture(pair.Item1 + "-settings-tab-" + i); }
        }
        Console.WriteLine("UI snapshots saved to " + Path.GetFullPath(directory));
        Close();
    }
    public void SetTexturedPack(object pack) => builtinBuilder?.SetTexturedPack(pack);
    private void RefreshProfiles()
    {
        settingItems = true;
        this.FindControl<ComboBox>("ProfileSelect")!.ItemsSource = scriptProfiles?.Names.ToArray() ?? Array.Empty<string>();
        State.SelectedProfile = "";
        settingItems = false;
    }
    private async Task SaveProfile()
    {
        if (scriptProfiles == null) throw new InvalidOperationException("The selected script has no profile storage path.");
        if (scriptProfiles.Names.Contains(State.ProfileName.Trim()) && !await ConfirmAsync("Are you sure you want to override profile " + State.ProfileName.Trim() + "?")) return;
        scriptProfiles.Save(State.ProfileName);
        RefreshProfiles();
        this.FindControl<ComboBox>("ProfileSelect")!.SelectedItem = State.ProfileName.Trim();
    }
    private void DeleteProfile()
    {
        if (this.FindControl<ComboBox>("ProfileSelect")!.SelectedItem is string name) { scriptProfiles?.Delete(name); RefreshProfiles(); }
    }
    private void ResetProfile()
    {
        if (scriptProfiles != null) scriptProfiles.ResetDefaults();
        else foreach (var (source, property) in scriptValues) SetScriptValue(source, property, source.GetType().GetProperty("Default")!.GetValue(source)!);
    }
    private void ProfileSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (settingItems || sender is not ComboBox { SelectedItem: string name } || scriptProfiles == null) return;
        try { scriptProfiles.Apply(name); State.ProfileName = name; State.SelectedProfile = name; ProfileChanged?.Invoke(name); }
        catch (Exception ex) { _ = ShowError("Cannot apply profile: " + ex.Message); }
    }
    public async Task ShowError(string message)
    {
        var dialog = new Window { Title = "Zenith", Width = 560, SizeToContent = SizeToContent.Height, MaxHeight = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        dialog.Styles.Add(new StyleInclude(new Uri("avares://Zenith.Mac/")) { Source = new Uri("avares://Zenith.Mac/Styles/ZenithTheme.axaml") });
        ThemeManager.Inherit(dialog, this);
        var stack = new StackPanel { Margin = new Thickness(20), Spacing = 18 };
        stack.Children.Add(new TextBox { Text = message, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, MaxHeight = 350, Background = Brushes.Transparent, BorderThickness = new Thickness(0) });
        var button = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80 };
        button.Classes.Add("primary");
        button.Click += (_, _) => dialog.Close(); stack.Children.Add(button); dialog.Content = stack;
        await dialog.ShowDialog(this);
    }
    public async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new Window { Title = "Zenith", Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        dialog.Styles.Add(new StyleInclude(new Uri("avares://Zenith.Mac/")) { Source = new Uri("avares://Zenith.Mac/Styles/ZenithTheme.axaml") });
        ThemeManager.Inherit(dialog, this);
        var stack = new StackPanel { Margin = new Thickness(20), Spacing = 18 };
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        var yes = new Button { Content = "Yes", MinWidth = 80 };
        var no = new Button { Content = "No", MinWidth = 80 };
        yes.Classes.Add("primary");
        yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false);
        buttons.Children.Add(yes); buttons.Children.Add(no); stack.Children.Add(buttons); dialog.Content = stack;
        return await dialog.ShowDialog<bool>(this);
    }
}
