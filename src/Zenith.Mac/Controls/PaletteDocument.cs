using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using OpenTK.Mathematics;
using SkiaSharp;
using Zenith.Core.Rendering;

namespace Zenith.Mac;

/// <summary>Editable straight RGBA pixels in Zenith's 16/32-column PNG format.</summary>
public sealed class PaletteDocument
{
    public const string PfaConfigName = "PFA Config Colors";
    public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255);
    readonly List<Rgba[]> rows = new();
    public bool UseGradients { get; set; }
    public int RowCount => rows.Count;
    public int PixelWidth => UseGradients ? 32 : 16;
    static readonly HashSet<string> protectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Random", "Random Gradients", "Random Alpha Gradients", "Random with Alpha",
        "Synthesia 10 Palette", "Synthesia 9-0.8 Palette", PfaConfigName
    };

    public static bool IsProtectedName(string name)
    {
        if (protectedNames.Contains(name)) return true;
        string bundledPath = Path.Combine(AssetPaths.Resolve("Plugins/Assets/Palettes"), name + ".png");
        return File.Exists(bundledPath);
    }

    public static bool CanDelete(string name) => !IsProtectedName(name) && FindPath(name) is { } path && File.Exists(path);

    public static void Delete(string name)
    {
        if (IsProtectedName(name)) throw new InvalidOperationException("Built-in palettes cannot be deleted.");
        string path = FindPath(name) ?? throw new FileNotFoundException("This custom palette no longer exists.");
        File.Delete(path);
    }
    public static PaletteDocument Create()
    {
        string[] colors = ["F05252", "F58B42", "E6BD3B", "9FCC45", "4AB96A", "36B5A2", "3EB8D7", "5295EA",
            "6C78E5", "9C6DDD", "C96EC8", "E475A4", "D58C6D", "A8AA64", "71ADA6", "9D9DC3"];
        var document = new PaletteDocument();
        document.rows.Add(colors.SelectMany(hex =>
        {
            uint rgb = Convert.ToUInt32(hex, 16);
            var color = new Rgba((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return new[] { color, color };
        }).ToArray());
        return document;
    }

    public static PaletteDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("This file is not a readable PNG palette.");
        if (codec.EncodedFormat != SKEncodedImageFormat.Png || codec.Info.Width is not (16 or 32) || codec.Info.Height < 1)
            throw new InvalidDataException("A Zenith palette must be a PNG with 16 or 32 columns and at least one row.");
        using var bitmap = new SKBitmap(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("The palette PNG could not be decoded completely.");
        var result = new PaletteDocument { UseGradients = bitmap.Width == 32 };
        byte[] bytes = new byte[bitmap.Width * 4];
        for (int y = 0; y < bitmap.Height; y++)
        {
            Marshal.Copy(bitmap.GetPixels() + y * bitmap.RowBytes, bytes, 0, bytes.Length);
            var row = new Rgba[32];
            for (int x = 0; x < 32; x++)
            {
                int source = (result.UseGradients ? x : x / 2) * 4;
                row[x] = new(bytes[source], bytes[source + 1], bytes[source + 2], bytes[source + 3]);
            }
            result.rows.Add(row);
        }
        return result;
    }

    public static PaletteDocument LoadPfaConfig(string path)
    {
        var colors = XDocument.Load(path).Descendants("Colors").FirstOrDefault()?.Elements().Take(16).ToArray();
        if (colors?.Length != 16) throw new InvalidDataException("The PFA configuration must contain 16 RGB colors.");
        // Match PaletteService's virtual palette: document order, duplicated
        // left/right endpoints and opaque alpha, without creating a source PNG.
        static byte Component(XElement color, string name) => color.Attribute(name) is { } value
            ? unchecked((byte)(int)value) : throw new InvalidDataException("A PFA color is missing its " + name + " component.");
        var result = new PaletteDocument();
        result.rows.Add(colors.SelectMany(color =>
        {
            var rgba = new Rgba(Component(color, "R"), Component(color, "G"), Component(color, "B"));
            return new[] { rgba, rgba };
        }).ToArray());
        return result;
    }

    public Rgba GetColor(int row, int channel, int side) => rows[row][channel * 2 + (UseGradients ? side : 0)];
    public Color4[] ToPaletteColors()
    {
        var colors = new Color4[checked(rows.Count * 32)];
        for (int y = 0; y < rows.Count; y++)
        for (int channel = 0; channel < 16; channel++)
        {
            var left = rows[y][channel * 2];
            var right = UseGradients ? rows[y][channel * 2 + 1] : left;
            colors[y * 32 + channel * 2] = new Color4(left.R, left.G, left.B, left.A);
            colors[y * 32 + channel * 2 + 1] = new Color4(right.R, right.G, right.B, right.A);
        }
        return colors;
    }
    public void SetColor(int row, int channel, int side, Rgba color)
    {
        if ((uint)channel >= 16 || (uint)side > 1) throw new ArgumentOutOfRangeException(nameof(channel));
        rows[row][channel * 2 + side] = color;
        if (!UseGradients) rows[row][channel * 2 + 1 - side] = color;
    }
    public bool HasDifferentGradientEnds => rows.Any(row => Enumerable.Range(0, 16).Any(i => row[i * 2] != row[i * 2 + 1]));
    public void AddRow(int copyFrom) => rows.Add((Rgba[])rows[copyFrom].Clone());
    public void RemoveRow(int row)
    {
        if (RowCount == 1) throw new InvalidOperationException("A palette needs at least one row.");
        rows.RemoveAt(row);
    }

    public static string ValidateName(string? text)
    {
        string name = (text ?? "").Trim();
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) throw new ArgumentException("Enter a palette name.");
        if (name is "." or ".." || name.StartsWith('.') || name.EndsWith('.') || name.EndsWith(' ')
            || name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) || Encoding.UTF8.GetByteCount(name) > 240)
            throw new ArgumentException("Use a name without path separators, control characters or <>:\"|?*. Maximum: 240 UTF-8 bytes.");
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem)
            || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9'))
            throw new ArgumentException("This filename is reserved. Choose another palette name.");
        if (IsProtectedName(name)) throw new ArgumentException("Built-in palettes are preserved. Choose a custom name to save a copy.");
        return name;
    }

    public static string? FindPath(string name) => Directory.Exists(PaletteService.PaletteDirectory)
        ? Directory.EnumerateFiles(PaletteService.PaletteDirectory, "*.png")
            .FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase))
        : null;
    public static string SuggestName(string stem)
    {
        string name = stem;
        for (int suffix = 2; FindPath(name) != null || IsProtectedName(name); suffix++) name = stem + " " + suffix;
        return name;
    }

    public string Save(string name, bool overwrite = false)
    {
        name = ValidateName(name);
        Directory.CreateDirectory(PaletteService.PaletteDirectory);
        string path = FindPath(name) ?? Path.Combine(PaletteService.PaletteDirectory, name + ".png");
        if (File.Exists(path) && !overwrite) throw new IOException("A palette with this name already exists.");
        string temporary = Path.Combine(PaletteService.PaletteDirectory, ".zenith-palette-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var bitmap = new SKBitmap(PixelWidth, RowCount, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            byte[] bytes = new byte[PixelWidth * 4];
            for (int y = 0; y < RowCount; y++)
            {
                for (int x = 0; x < PixelWidth; x++)
                {
                    var color = rows[y][UseGradients ? x : x * 2];
                    bytes[x * 4] = color.R; bytes[x * 4 + 1] = color.G; bytes[x * 4 + 2] = color.B; bytes[x * 4 + 3] = color.A;
                }
                Marshal.Copy(bytes, 0, bitmap.GetPixels() + y * bitmap.RowBytes, bytes.Length);
            }
            using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
            using (var file = File.Create(temporary)) data.SaveTo(file);
            File.Move(temporary, path, overwrite);
            return Path.GetFileNameWithoutExtension(path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
