using System.IO.Compression;
using System.Security.Cryptography;
using OpenTK.Mathematics;
using ScriptedEngine;
using Zenith.Core.Scripted;
using ZenithEngine;

// This is an integration executable: it compiles and runs the actual shipped C#
// packs, their textures and saved particle profiles. No Windows process is used.
var root = FindRoot();
var resources = Path.Combine(root, "assets/windows/Zenith/Plugins/Assets/Scripted/Resources");
var temp = Path.Combine(Path.GetTempPath(), "zenith-script-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
var assertions = 0;
try
{
    if (args.Contains("--postprocessing"))
    {
        PostProcessingChecks.Run(temp, Assert);
        Console.WriteLine($"PASS {assertions} postprocessing assertions");
        return;
    }
    foreach (var folder in Directory.EnumerateDirectories(resources))
    {
        using var pack = ScriptedPack.Load(folder);
        var frame = pack.Render(Notes(), Options());
        Assert(frame.Commands.OfType<QuadCommand>().Any(), pack.Name + " emits geometry");
        Assert(frame.Commands.OfType<QuadCommand>().All(Finite), pack.Name + " geometry is finite");
        Console.WriteLine($"PASS {pack.Name}: {pack.Textures.Count} textures, {frame.Commands.Count} commands");
    }

    var zrpPath = Path.Combine(resources, "Synthesia X.zrp");
    using (var pack = ScriptedPack.Load(zrpPath))
    {
        Assert(pack.SourceHash == "b156434b749695904c03d81089c78b8c1f242a3e7e29f7f613e0b5ca946918f5", "Loads the original supplied Synthesia X source");
        Assert(pack.Textures.Count == 88 && pack.Fonts.Count == 2, "Loads all Synthesia X textures and fonts");
        Assert(pack.Preview is { Length: > 0 } && pack.Description.Contains("Arduano"), "Loads original preview and description");
        Assert(ScriptedProfiles.Flatten(pack.SettingsUI).Count() == 306, "Builds all original 306 editable settings");
        // Verify the eleven original shipped profiles independently of any
        // saved user profiles in Application Support.
        var profiles = new ScriptedProfiles(pack, pack.Path + ".profiles.json");
        Assert(profiles.Names.Count == 11, "Reads all 11 shipped Windows profiles");
        foreach (var name in profiles.Names)
        {
            profiles.Apply(name);
            var options = Options();
            pack.Reset(options);
            var notes = Notes();
            var maxDraws = 0;
            for (var frameNumber = 0; frameNumber < 30; frameNumber++)
            {
                options.midiTime = frameNumber * 16;
                var frame = pack.Render(notes, options);
                var quads = frame.Commands.OfType<QuadCommand>().ToArray();
                Assert(quads.All(Finite), name + " emits finite geometry");
                Assert(quads.Any(q => q.Texture != null), name + " uses original textures");
                maxDraws = Math.Max(maxDraws, frame.Commands.Count);
            }
            Console.WriteLine($"PASS Synthesia X profile: {name} ({maxDraws} maximum commands)");
        }
        var copiedProfiles = Path.Combine(temp, "roundtrip.profiles.json");
        var roundtrip = new ScriptedProfiles(pack, copiedProfiles);
        var style = ScriptedProfiles.Flatten(pack.SettingsUI).OfType<UIDropdown>().First();
        style.Index = 2;
        roundtrip.Save("Mac profile");
        style.Index = 0;
        new ScriptedProfiles(pack, copiedProfiles).Apply("Mac profile");
        Assert(style.Index == 2, "Profile values round trip in Windows array format");
        roundtrip.ResetDefaults();
        Assert(style.Index == style.Default, "Reset restores script-defined defaults");
    }

    // Independently unwrap the supplied file, then confirm ZIP loading selects
    // the same original script and provides the identical resource inventory.
    var bytes = File.ReadAllBytes(zrpPath);
    using (var aes = Aes.Create())
    using (var decryptor = aes.CreateDecryptor(bytes[..16], bytes[16..32]))
    {
        var zipPath = Path.Combine(temp, "original.zip");
        File.WriteAllBytes(zipPath, decryptor.TransformFinalBlock(bytes, 32, bytes.Length - 32));
        using var zipPack = ScriptedPack.Load(zipPath);
        Assert(zipPack.Textures.Count == 88, "AES-CBC ZRP payload is a normal supported ZIP");
    }

    if (File.Exists(Path.Combine(root, "Zenith.7z")))
    {
        using var sevenZip = ScriptedPack.Load(Path.Combine(root, "Zenith.7z"));
        Assert(sevenZip.Render(Notes(), Options()).Commands.Count > 0, "7z loader finds and executes the nearest script in original distribution");
    }
    var tarPath = Path.Combine(temp, "Textured.tar");
    System.Formats.Tar.TarFile.CreateFromDirectory(Path.Combine(resources, "Example Textured"), tarPath, includeBaseDirectory: false);
    using (var tarPack = ScriptedPack.Load(tarPath)) Assert(tarPack.Textures.Count == 7, "TAR pack loader resolves original script textures");

    var probe = Path.Combine(temp, "Lifecycle");
    Directory.CreateDirectory(probe);
    File.WriteAllText(Path.Combine(probe, "script.cs"), """
        using System;
        using System.Collections.Generic;
        using ScriptedEngine;
        using ZenithEngine;
        using OpenTK;
        using OpenTK.Graphics;
        public class Script {
            public bool UseProfiles=true;
            public bool ManualNoteDelete=true;
            public long LastNoteCount=0;
            public double NoteScreenTime=2000;
            public double NoteCollectorOffset=-25;
            public UISetting[] SettingsUI={new UICheckbox("Checked",true)};
            public void Load() {}
            public void RenderInit(RenderOptions options){LastNoteCount=10;}
            public void RenderDispose(){LastNoteCount=0;}
            public void Render(IEnumerable<Note> notes,RenderOptions options){
                LastNoteCount++;
                IO.SelectTextureShader(TextureShaders.Hybrid);
                IO.SetBlendFunc(BlendFunc.Add);
                IO.RenderQuad(0,1,1,0,new Color4(255,128,0,255));
                IO.ForceFlushBuffer();
                foreach(var n in notes){n.meta="executed";n.delete=true;}
            }
        }
        """);
    using (var pack = ScriptedPack.Load(probe))
    {
        var notes = Notes();
        var frame = pack.Render(notes, Options());
        var q = (QuadCommand)frame.Commands[0];
        Assert(pack.LastNoteCount == 11 && pack.NoteScreenTime == 2000 && pack.NoteCollectorOffset == -25, "Reads original script lifecycle fields");
        Assert(pack.ManualNoteDelete && notes[0].delete && (string?)notes[0].meta == "executed", "Preserves mutable Note identity and manual deletion");
        Assert(q.Shader == TextureShaders.Hybrid && q.Blend == BlendFunc.Add && frame.Commands[1] is FlushCommand, "Preserves shader/blend/flush command sequence");
        Assert(q.C1.R == 1 && Math.Abs(q.C1.G - 128f / 255) < 0.0001, "OpenTK byte color constructor matches original normalization");
        pack.Reset(Options());
        Assert(pack.LastNoteCount == 10, "Reset repeats RenderDispose and RenderInit");
    }
    GpuParityChecks.Run(temp, Assert);
    PostProcessingChecks.Run(temp, Assert);
    LargeMidiChecks.Run(temp, Assert);
    Console.WriteLine($"PASS {assertions} Scripted integration assertions");
}
finally { Directory.Delete(temp, recursive: true); }

void Assert(bool result, string message)
{
    assertions++;
    if (!result) throw new Exception("FAIL " + message);
}

static bool Finite(QuadCommand q) => new[] { q.V1, q.V2, q.V3, q.V4, q.Uv1, q.Uv2, q.Uv3, q.Uv4 }
    .All(v => double.IsFinite(v.X) && double.IsFinite(v.Y));

static Note[] Notes() => Enumerable.Range(0, 12).Select(i => new Note
{
    start = i * 40, end = 900 + i * 40, hasEnded = true, key = (byte)(60 + i), vel = 100,
    color = new NoteColor { left = new Color4(0.2f, 0.6f, 1f, 1f), right = new Color4(0.8f, 0.3f, 1f, 1f) }
}).ToArray();

static RenderOptions Options() => new()
{
    firstKey = 0, lastKey = 128, renderWidth = 1920, renderHeight = 1080,
    renderFPS = 60, renderSSAA = 1, noteScreenTime = 1920, midiPPQ = 480,
    midiBarLength = 1920, midiTimeSignature = new TimeSignature()
};

static string FindRoot()
{
    for (var path = new DirectoryInfo(Environment.CurrentDirectory); path != null; path = path.Parent)
        if (Directory.Exists(Path.Combine(path.FullName, "assets/windows/Zenith"))) return path.FullName;
    throw new DirectoryNotFoundException("Run from the Zenith-Mac repository.");
}
