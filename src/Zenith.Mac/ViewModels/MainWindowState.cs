using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Zenith.Mac;

public sealed class MainWindowState : INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> values = new();
    private readonly object valueLock = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    private T Get<T>(T fallback, [CallerMemberName] string name = "") { lock (valueLock) return values.TryGetValue(name, out var value) ? (T)value! : fallback; }
    private void Set<T>(T value, [CallerMemberName] string name = "")
    {
        lock (valueLock)
        {
            if (EqualityComparer<T>.Default.Equals(Get(default(T)!, name), value) && values.ContainsKey(name)) return;
            values[name] = value;
        }
        PropertyChanged?.Invoke(this, new(name));
        if (name is nameof(MidiLoaded) or nameof(IsBusy) or nameof(IsRendering))
            foreach (var dependent in new[] { nameof(CanLoad), nameof(CanUnload), nameof(CanStart), nameof(CanEdit), nameof(NotRendering) })
                PropertyChanged?.Invoke(this, new(dependent));
        if (name == nameof(ProfileName)) PropertyChanged?.Invoke(this, new(nameof(CanSaveProfile)));
        if (name == nameof(SelectedProfile)) PropertyChanged?.Invoke(this, new(nameof(CanDeleteProfile)));
    }
    public string VersionName { get => Get("2.1.5"); set => Set(value.Replace("Zenith ", "")); }
    public string LanguageCode { get => Get("en"); set => Set(value); }
    public string ThemeId { get => Get("sage"); set => Set(value is "sage" or "nord-light" or "nord-dark" or "latte" or "mocha" or "solarized-light" ? value : "sage"); }
    public bool MidiLoaded { get => Get(false); set => Set(value); }
    public bool IsBusy { get => Get(false); set => Set(value); }
    public bool IsRendering { get => Get(false); set => Set(value); }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPreviewing { get => Get(false); set => Set(value); }
    public bool CanLoad => !MidiLoaded && !IsBusy;
    public bool CanUnload => MidiLoaded && !IsBusy;
    public bool CanStart => MidiLoaded && !IsBusy;
    public bool CanEdit => !IsBusy;
    public bool NotRendering => !IsRendering;
    public string MidiPath { get => Get(""); set => Set(value); }
    public string Status { get => Get(""); set => Set(value); }
    public double Progress { get => Get(0d); set => Set(value); }
    public decimal Width { get => Get(1920m); set => Set(value); }
    public decimal Height { get => Get(1080m); set => Set(value); }
    public decimal Ssaa { get => Get(1m); set => Set(value); }
    public decimal Fps { get => Get(60m); set => Set(value); }
    public int ResolutionPreset { get => Get(1); set { Set(value); if (value is < 0 or > 6) return; var sizes = new[] { (1280, 720), (1920, 1080), (2560, 1440), (3840, 2160), (5120, 2880), (7680, 4320), (15360, 8640) }; Width = sizes[value].Item1; Height = sizes[value].Item2; } }
    public int NoteSizeStyle { get => Get(0); set => Set(value); }
    public bool IgnoreColorEvents { get => Get(false); set => Set(value); }
    public bool UseBackground { get => Get(false); set => Set(value); }
    public string BackgroundPath { get => Get(""); set => Set(value); }
    public double BackgroundOpacityPercent { get => Get(100d); set => Set(double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 100); }
    public double BackgroundPositionXPercent { get => Get(50d); set => Set(double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 50); }
    public double BackgroundPositionYPercent { get => Get(50d); set => Set(double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 50); }
    public bool ShadowEnabled { get => Get(false); set => Set(value); }
    public double ShadowBlurPixels { get => Get(3d); set => Set(double.IsFinite(value) ? Math.Clamp(value, 0, 64) : 3); }
    public double ShadowAngleDegrees { get => Get(45d); set => Set(double.IsFinite(value) ? Math.Clamp(value, 0, 360) : 45); }
    public double ShadowDistancePixels { get => Get(18d); set => Set(double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 18); }
    public double ShadowOpacityPercent { get => Get(70d); set => Set(double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 70); }
    public double TempoMultiplier { get => Get(1d); set => Set(value); }
    public bool Vsync { get => Get(true); set => Set(value); }
    public bool Paused { get => Get(false); set => Set(value); }
    public bool RealtimePlayback { get => Get(true); set => Set(value); }
    public bool AudioEnabled { get => Get(true); set => Set(value); }
    public string AudioToggleLabel { get => Get("Disable Audio"); set => Set(value); }
    public string VideoPath { get => Get(""); set => Set(value); }
    public bool IncludeAudio { get => Get(false); set => Set(value); }
    public string AudioPath { get => Get(""); set => Set(value); }
    public bool IncludeAlpha { get => Get(false); set => Set(value); }
    public string AlphaPath { get => Get(""); set => Set(value); }
    public bool UseBitrate { get => Get(true); set => Set(value); }
    public decimal Bitrate { get => Get(20000m); set => Set(value); }
    public bool UseCrf { get => Get(false); set => Set(value); }
    public bool UseHardwareEncoding { get => Get(false); set => Set(value); }
    public decimal Crf { get => Get(17m); set => Set(value); }
    public string CrfPreset { get => Get("medium"); set => Set(value); }
    public bool UseCustomFfmpeg { get => Get(false); set => Set(value); }
    public string FfmpegOptions { get => Get("-c:v libx264 -pix_fmt yuv420p -b:v 20000k"); set => Set(value); }
    public decimal DelaySeconds { get => Get(0m); set => Set(value); }
    public bool FfmpegDebug { get => Get(false); set => Set(value); }
    public string ModuleDescription { get => Get(""); set => Set(value); }
    public string SkinDescription { get => Get(""); set => Set(value); }
    public string CreditText { get => Get("Rendered with Zenith\nhttps://github.com/arduano/Zenith-MIDI"); set => Set(value); }
    public decimal FirstNote { get => Get(0m); set => Set(value); }
    public decimal LastNote { get => Get(127m); set => Set(value); }
    public double NoteScreenTime { get => Get(300d); set => Set(value); }
    public bool RandomizePalette { get => Get(true); set => Set(value); }
    public string ProfileName { get => Get(""); set => Set(value); }
    public string SelectedProfile { get => Get(""); set => Set(value); }
    public bool CanSaveProfile => !string.IsNullOrWhiteSpace(ProfileName);
    public bool CanDeleteProfile => !string.IsNullOrEmpty(SelectedProfile);
}

public sealed record ModuleEntry(string Id, string Name, string Description, string? PreviewPath = null)
{
    public override string ToString() => Name;
}
public sealed record SkinEntry(string Path, string Name, bool IsArchive = false)
{
    public override string ToString() => Name;
}
