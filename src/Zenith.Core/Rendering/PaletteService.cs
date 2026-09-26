using System.Runtime.CompilerServices;
using System.Xml.Linq;
using OpenTK.Mathematics;
using SkiaSharp;

namespace Zenith.Core.Rendering;

/// <summary>One original NoteColorPalettePick instance, independent of other modules.</summary>
public sealed class PaletteSelection
{
    readonly object gate = new();
    readonly float saturation;
    readonly float value;
    Dictionary<string, Color4[]> palettes = new(StringComparer.Ordinal);
    Color4[]? previewColors;
    string selectedImage = "Random";
    bool randomized;
    // Keep palette columns aligned with MIDI channels until randomization is requested.
    int seed;
    long revision;

    public PaletteSelection(float defaultSaturation = 1, float defaultValue = 1)
    {
        saturation = defaultSaturation;
        value = defaultValue;
        Reload();
    }

    public string SelectedImage { get { lock (gate) return selectedImage; } }
    public bool Randomized { get { lock (gate) return randomized; } }
    public int Seed { get { lock (gate) return seed; } }
    public long Revision { get { lock (gate) return revision; } }
    public IReadOnlyList<string> GetPaletteNames() { lock (gate) return palettes.Keys.ToArray(); }

    /// <summary>Temporarily overrides the selected palette for live editor preview; null restores the saved palette.</summary>
    public void SetPreviewColors(Color4[]? colors)
    {
        lock (gate)
        {
            previewColors = colors == null ? null : (Color4[])colors.Clone();
            revision++;
        }
    }

    public void Select(string name)
    {
        lock (gate)
        {
            string selected = palettes.ContainsKey(name) ? name : "Random";
            if (selectedImage == selected) return;
            selectedImage = selected;
            revision++;
        }
    }

    public void SetRandomized(bool enabled)
    {
        lock (gate)
        {
            randomized = enabled;
            if (enabled) seed++;
            revision++;
        }
    }

    /// <summary>Regenerates the four original default palettes and reloads valid PNG files.</summary>
    public void Reload()
    {
        lock (gate)
        {
            previewColors = null;
            string folder = PaletteService.PaletteDirectory;
            Directory.CreateDirectory(folder);
            string bundled = AssetPaths.Resolve("Plugins/Assets/Palettes");
            if (Directory.Exists(bundled))
                foreach (string source in Directory.GetFiles(bundled, "*.png"))
                {
                    string destination = Path.Combine(folder, Path.GetFileName(source));
                    if (!File.Exists(destination)) File.Copy(source, destination);
                }
            WriteGenerated(folder, "Random", false, false);
            WriteGenerated(folder, "Random Gradients", true, false);
            WriteGenerated(folder, "Random Alpha Gradients", true, true);
            WriteGenerated(folder, "Random with Alpha", false, true);
            var next = new Dictionary<string, Color4[]>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(folder, "*.png")
                         .OrderBy(p => Path.GetFileName(p) == "Random.png" ? 0 : 1)
                         .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var data = SKData.Create(path);
                    using var codec = SKCodec.Create(data);
                    if (codec == null || codec.Info.Width is not (16 or 32) || codec.Info.Height < 1) continue;
                    using var bitmap = new SKBitmap(codec.Info.Width, codec.Info.Height,
                        SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) continue;
                    next[Path.GetFileNameWithoutExtension(path)] = Enumerable.Range(0, 32 * bitmap.Height)
                        .Select(i =>
                        {
                            var c = bitmap.GetPixel(bitmap.Width == 16 ? (i % 32) / 2 : i % 32, i / 32);
                            return new Color4(c.Red, c.Green, c.Blue, c.Alpha);
                        }).ToArray();
                }
                catch (IOException) { }
                catch (InvalidDataException) { }
            }
            ReadPfaConfig(next);
            palettes = next;
            if (!palettes.ContainsKey(selectedImage)) selectedImage = "Random";
            revision++;
        }
    }

    void WriteGenerated(string folder, string name, bool gradient, bool alpha)
    {
        int width = gradient ? 32 : 16;
        using var bitmap = new SKBitmap(width, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (int i = 0; i < width * 8; i++)
        {
            float hue = gradient && i % 2 != 0 ? ((i - 1) * .12345f + .166f) % 1 : i * .12345f % 1;
            var c = Color4.FromHsv(new Vector4(hue, saturation, value, alpha ? .8f : 1));
            bitmap.SetPixel(i % width, i / width, new SKColor((byte)(c.R * 255),
                (byte)(c.G * 255), (byte)(c.B * 255), (byte)(c.A * 255)));
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        data.SaveTo(file);
    }

    static void ReadPfaConfig(Dictionary<string, Color4[]> destination)
    {
        try
        {
            if (!File.Exists(PaletteService.PfaConfigPath)) return;
            var colors = XDocument.Load(PaletteService.PfaConfigPath).Descendants("Colors").First().Elements().Take(16).ToArray();
            if (colors.Length != 16) return;
            destination["PFA Config Colors"] = colors.SelectMany(c =>
            {
                var color = new Color4((byte)(int)c.Attribute("R")!, (byte)(int)c.Attribute("G")!,
                    (byte)(int)c.Attribute("B")!, (byte)255);
                return new[] { color, color };
            }).ToArray();
        }
        catch { /* Like the original picker, an unavailable/invalid PFA config is optional. */ }
    }

    public Color4[] GetColors(int tracks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tracks);
        lock (gate)
        {
            var palette = previewColors ?? palettes[selectedImage];
            int count = checked(tracks * 16);
            var order = new double[count];
            var coordinates = new int[count];
            var random = new Random(seed);
            for (int i = 0; i < count; i++) { order[i] = random.NextDouble(); coordinates[i] = i; }
            if (randomized) Array.Sort(order, coordinates);
            var result = new Color4[count * 2];
            for (int i = 0; i < count; i++)
            {
                int pair = coordinates[i] * 2 % palette.Length;
                result[i * 2] = palette[pair];
                result[i * 2 + 1] = palette[pair + 1];
            }
            return result;
        }
    }
}

public static class PaletteService
{
    static readonly ConditionalWeakTable<object, PaletteSelection> selections = new();
    static readonly Lazy<PaletteSelection> defaults = new(() => new PaletteSelection());
    static readonly Dictionary<string, RasterImage> auras = new();
    static long auraRevision = 1;
    /// <summary>Writable palette folder; bundled PNGs seed it without replacing user files.</summary>
    public static string PaletteDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Zenith-Mac", "Palettes");
    public static string PfaConfigPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Piano From Above", "Config.xml");
    public static PaletteSelection Default => defaults.Value;
    public static PaletteSelection For(object owner, float defaultSaturation = 1, float defaultValue = 1) =>
        selections.GetValue(owner, _ => new PaletteSelection(defaultSaturation, defaultValue));
    public static Color4[] GetColors(object owner, string name, int tracks, float defaultSaturation = 1)
    {
        var selection = For(owner, defaultSaturation);
        selection.Select(name);
        return selection.GetColors(tracks);
    }
    public static Color4[] GetColors(string name, int tracks)
    {
        Default.Select(name);
        return Default.GetColors(tracks);
    }
    public static void SetRandomized(bool value) => Default.SetRandomized(value);
    public static void ReloadPalettes() => Default.Reload();
    public static long AuraRevision => auraRevision;
    public static void ReloadAuras() { foreach (var image in auras.Values) image.Dispose(); auras.Clear(); auraRevision++; }
    public static RasterImage Aura(string name)
    {
        var path = AssetPaths.Resolve($"Plugins/Assets/MIDITrail/Aura/{name}.png");
        if (!auras.TryGetValue(path, out var image)) auras[path] = image = new RasterImage(path);
        return image;
    }
}
