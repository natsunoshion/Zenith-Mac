using System.IO.Compression;
using System.Security.Cryptography;
using SharpCompress.Archives;

namespace Zenith.Core.Scripted;

/// <summary>Reads the same pack formats and embedded AES header as the Windows loader.</summary>
internal sealed class ScriptedPackArchive : IDisposable
{
    private readonly string? folder;
    private readonly ZipArchive? zip;
    private readonly IArchive? compressed;
    private readonly Stream? stream;
    private readonly string basePath;

    public string Source { get; }
    public string ScriptEntry { get; }

    public ScriptedPackArchive(string path)
    {
        if (Directory.Exists(path))
        {
            folder = Path.GetFullPath(path);
            ScriptEntry = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(p => Path.GetFileName(p) == "script.cs")
                .Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/'))
                .OrderBy(p => p.Length).FirstOrDefault()
                ?? throw new InvalidDataException("Could not find script.cs file");
        }
        else if (Path.GetExtension(path).Equals(".zrp", StringComparison.OrdinalIgnoreCase))
        {
            using var encoded = File.OpenRead(path);
            var key = new byte[16];
            var iv = new byte[16];
            encoded.ReadExactly(key);
            encoded.ReadExactly(iv);
            var decoded = new MemoryStream();
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var decryptor = aes.CreateDecryptor(key, iv);
            using (var crypto = new CryptoStream(encoded, decryptor, CryptoStreamMode.Read))
                crypto.CopyTo(decoded);
            decoded.Position = 0;
            stream = decoded;
            zip = new ZipArchive(decoded, ZipArchiveMode.Read, leaveOpen: true);
            ScriptEntry = FindScript(zip.Entries.Select(e => e.FullName));
        }
        else if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            zip = ZipFile.OpenRead(path);
            ScriptEntry = FindScript(zip.Entries.Select(e => e.FullName));
        }
        else
        {
            compressed = ArchiveFactory.OpenArchive(path);
            ScriptEntry = FindScript(compressed.Entries.Where(e => !e.IsDirectory).Select(e => e.Key!));
        }

        basePath = ScriptEntry[..^"script.cs".Length];
        using var sourceStream = OpenEntry(ScriptEntry);
        using var reader = new StreamReader(sourceStream);
        Source = reader.ReadToEnd();
    }

    private static string FindScript(IEnumerable<string> entries) => entries
        .Where(e => e.Replace('\\', '/').Split('/').Last() == "script.cs")
        .OrderBy(e => e.Length).FirstOrDefault()
        ?? throw new InvalidDataException("Could not find script.cs file");

    public byte[] ReadAsset(string relativePath)
    {
        var entry = basePath + relativePath.Replace('\\', '/');
        using var input = OpenEntry(entry);
        using var result = new MemoryStream();
        input.CopyTo(result);
        return result.ToArray();
    }

    private Stream OpenEntry(string entry)
    {
        entry = entry.Replace('\\', '/');
        if (folder != null)
        {
            var fullPath = Path.GetFullPath(Path.Combine(folder, entry));
            if (!fullPath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Pack resource path leaves its folder: " + entry);
            return File.OpenRead(fullPath);
        }
        if (zip != null)
            return (zip.GetEntry(entry) ?? zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == entry)
                ?? throw new FileNotFoundException("Could not open " + entry)).Open();
        var match = compressed!.Entries.FirstOrDefault(e => e.Key?.Replace('\\', '/') == entry);
        return match?.OpenEntryStream() ?? throw new FileNotFoundException("Could not open " + entry);
    }

    public void Dispose()
    {
        zip?.Dispose();
        compressed?.Dispose();
        stream?.Dispose();
    }
}
