using System.Text.Json;
using ScriptedEngine;

namespace Zenith.Core.Scripted;

/// <summary>Original .profiles.json format: named arrays in recursive UI construction order.</summary>
public sealed class ScriptedProfiles
{
    private readonly List<UISetting> settings;
    private readonly Dictionary<string, JsonElement[]> profiles = new(StringComparer.Ordinal);
    public IReadOnlyCollection<string> Names => profiles.Keys;
    public string FilePath { get; }
    public int SettingCount => settings.Count;
    public static string UserProfilesDirectory { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Zenith-Mac", "ScriptedProfiles");

    public ScriptedProfiles(ScriptedPack pack, string? filePath = null)
    {
        string adjacentPath = pack.Path + ".profiles.json";
        FilePath = filePath ?? GetDefaultFilePath(pack.Path);
        settings = Flatten(pack.SettingsUI).ToList();
        string importedMarker = FilePath + ".bundled-imported";
        bool importBundled = filePath == null && FilePath != adjacentPath
            && File.Exists(adjacentPath) && !File.Exists(importedMarker);
        if (importBundled) LoadFile(adjacentPath);
        // User values win the first merge. Afterwards this is the only source,
        // so deleting a bundled profile does not bring it back on the next run.
        LoadFile(FilePath);
        if (importBundled)
        {
            Persist();
            File.WriteAllText(importedMarker, "Imported bundled profiles; user file is authoritative.\n");
        }
    }

    /// <summary>
    /// Bundled/development asset packs share a writable path by resource-relative
    /// name. External packs keep the original adjacent .profiles.json behavior.
    /// </summary>
    public static string GetDefaultFilePath(string packPath)
    {
        string fullPath = System.IO.Path.GetFullPath(packPath).Replace('\\', '/');
        const string appResources = ".app/Contents/Resources/";
        int index = fullPath.LastIndexOf(appResources, StringComparison.OrdinalIgnoreCase);
        string? relative = index >= 0 ? fullPath[(index + appResources.Length)..] : null;
        if (relative?.StartsWith("Zenith/", StringComparison.OrdinalIgnoreCase) == true)
            relative = relative[7..];
        if (relative == null)
            foreach (string marker in new[] { "/assets/windows/Zenith/", "/Assets/Zenith/" })
            {
                index = fullPath.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;
                relative = fullPath[(index + marker.Length)..];
                break;
            }
        return relative == null ? System.IO.Path.GetFullPath(packPath) + ".profiles.json"
            : System.IO.Path.Combine(UserProfilesDirectory, relative + ".profiles.json");
    }

    private void LoadFile(string path)
    {
        if (!File.Exists(path)) return;
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Profile file must contain an object of named settings arrays.");
        foreach (var entry in json.RootElement.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Array) continue;
            var values = entry.Value.EnumerateArray().Select(v => v.Clone()).ToArray();
            if (IsValid(values)) profiles[entry.Name] = values;
        }
    }

    public static IEnumerable<UISetting> Flatten(IEnumerable<UISetting> ui)
    {
        foreach (var item in ui)
        {
            if (item is UITabs tabs)
            {
                foreach (var tab in tabs.Tabs.Values)
                    foreach (var child in Flatten(tab)) yield return child;
            }
            else if (item is UINumber or UINumberSlider or UICheckbox or UIDropdown) yield return item;
        }
    }

    private bool IsValid(JsonElement[] values)
    {
        if (values.Length != settings.Count) return false;
        for (var i = 0; i < values.Length; i++)
        {
            switch (settings[i])
            {
                case UICheckbox when values[i].ValueKind is not (JsonValueKind.True or JsonValueKind.False): return false;
                case UINumber or UINumberSlider when values[i].ValueKind != JsonValueKind.Number: return false;
                case UIDropdown drop when values[i].ValueKind != JsonValueKind.Number || !values[i].TryGetInt32(out var index) || index < 0 || index >= drop.Options.Length: return false;
            }
        }
        return true;
    }

    public void Apply(string name)
    {
        if (!profiles.TryGetValue(name, out var values)) throw new KeyNotFoundException("Unknown profile: " + name);
        for (var i = 0; i < settings.Count; i++)
        {
            switch (settings[i])
            {
                case UINumber number: number.Value = Math.Clamp(values[i].GetDouble(), number.Minimum, number.Maximum); break;
                case UINumberSlider slider: slider.Value = Math.Clamp(values[i].GetDouble(), slider.TrueMinimum, slider.TrueMaximum); break;
                case UICheckbox checkbox: checkbox.Checked = values[i].GetBoolean(); break;
                case UIDropdown dropdown: dropdown.Index = values[i].GetInt32(); break;
            }
        }
    }

    public void ResetDefaults()
    {
        foreach (var item in settings)
        {
            switch (item)
            {
                case UINumber number: number.Value = number.Default; break;
                case UINumberSlider slider: slider.Value = slider.Default; break;
                case UICheckbox checkbox: checkbox.Checked = checkbox.Default; break;
                case UIDropdown dropdown: dropdown.Index = dropdown.Default; break;
            }
        }
    }

    public void Save(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Please write a name for the profile", nameof(name));
        profiles[name] = settings.Select<UISetting, JsonElement>(item => item switch
        {
            UINumber number => JsonSerializer.SerializeToElement(number.Value),
            UINumberSlider slider => JsonSerializer.SerializeToElement(slider.Value),
            UICheckbox checkbox => JsonSerializer.SerializeToElement(checkbox.Checked),
            UIDropdown dropdown => JsonSerializer.SerializeToElement(dropdown.Index),
            _ => throw new InvalidOperationException()
        }).ToArray();
        Persist();
    }

    public void Delete(string name)
    {
        if (profiles.Remove(name)) Persist();
    }

    private void Persist()
    {
        var directory = System.IO.Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
