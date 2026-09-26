using System.Reflection;
using OpenTK.Mathematics;
using SkiaSharp;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using ZenithEngine;

internal static class PaletteChecks
{
    internal static void Run(string? userMidiPath = null)
    {
        string originalRoot = AssetPaths.Root, originalPfaPath = PaletteService.PfaConfigPath,
            originalPaletteDirectory = PaletteService.PaletteDirectory;
        string temp = Path.Combine(Path.GetTempPath(), "zenith-palette-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool condition, string description)
        {
            checks++;
            if (!condition) throw new Exception(description);
        }
        try
        {
            AssetPaths.Root = temp;
            string folder = PaletteService.PaletteDirectory = Path.Combine(temp, "Palettes");
            Directory.CreateDirectory(folder);
            Save("Red", 16, 1, (_, _) => SKColors.Red);
            Save("Green", 16, 1, (_, _) => SKColors.Lime);
            Save("Blue", 16, 1, (_, _) => SKColors.Blue);
            Save("Channels", 16, 2, (x, y) => new SKColor((byte)(x * 13), (byte)(y * 80), 73, 129));
            Save("Pairs", 32, 2, (x, y) => new SKColor((byte)(x * 7), (byte)(y * 80), 111, 203));
            Save("Invalid Width", 17, 1, (_, _) => SKColors.Red);
            File.WriteAllText(Path.Combine(folder, "Invalid Data.png"), "broken PNG");
            string bundled = AssetPaths.Resolve("Plugins/Assets/Palettes");
            Directory.CreateDirectory(bundled);
            File.Copy(Path.Combine(folder, "Red.png"), Path.Combine(bundled, "Bundled.png"));
            File.Copy(Path.Combine(folder, "Blue.png"), Path.Combine(bundled, "Green.png"));
            PaletteService.PfaConfigPath = Path.Combine(temp, "Pfa.xml");
            File.WriteAllText(PaletteService.PfaConfigPath, "<Config><Colors>" +
                string.Concat(Enumerable.Range(0, 16).Select(i => $"<Color R=\"{i}\" G=\"20\" B=\"30\"/>")) + "</Colors></Config>");
            var firstOwner = new object();
            var first = PaletteService.For(firstOwner);
            var second = PaletteService.For(new object(), .8f);
            Check(ReferenceEquals(first, PaletteService.For(firstOwner)), "A module retains its own picker instance");
            Check(!first.Randomized && first.Seed == 0, "Palette channel order is preserved by default");
            Check(!first.GetPaletteNames().Contains("Invalid Width") && !first.GetPaletteNames().Contains("Invalid Data"),
                "Palette enumeration filters invalid PNGs and unsupported widths");
            Check(first.GetPaletteNames().First() == "Random" && first.GetPaletteNames().Contains("PFA Config Colors"),
                "Default ordering and optional PFA Config palette are available");
            Check(first.GetPaletteNames().Contains("Bundled") && !File.Exists(Path.Combine(bundled, "Random.png")),
                "Missing bundled palettes are seeded into the writable folder without writing into the application bundle");
            first.SetRandomized(false);
            first.Select("Green");
            Check(first.GetColors(1)[0] == Color4.Lime, "Bundled palettes cannot overwrite an existing user palette");
            first.Select("Channels");
            var channels = first.GetColors(3);
            Check(channels[0] == channels[1] && channels[32] == channels[33] && channels[0] == channels[64],
                "16-pixel palettes duplicate left/right and repeat image rows across tracks");
            Check(Math.Abs(channels[2].R - 13f / 255) < .00001 && Math.Abs(channels[2].A - 129f / 255) < .00001,
                "Transparent palette RGB is decoded unpremultiplied without alpha rounding loss");
            first.Select("Pairs");
            var pairs = first.GetColors(1);
            Check(pairs[0].R == 0 && Math.Abs(pairs[1].R - 7f / 255) < .00001,
                "32-pixel palettes retain separate left/right colors");
            first.Select("Random"); second.Select("Random"); second.SetRandomized(false);
            Check(first.GetColors(1)[0].G == 0 && second.GetColors(1)[0].G > .19f,
                "PFA's 0.8 saturation snapshot stays independent of other modules' generated palettes");
            first.Select("Channels");
            var secondBefore = second.GetColors(3);
            first.SetRandomized(true);
            var shuffled = first.GetColors(3);
            Check(!shuffled.SequenceEqual(channels) && shuffled.SequenceEqual(first.GetColors(3)),
                "Enabling random order reshuffles deterministically for one seed");
            Check(secondBefore.SequenceEqual(second.GetColors(3)) && second.Seed == 0 && !second.Randomized,
                "Another module's shuffle cannot alter this picker");
            first.SetRandomized(false);
            Check(channels.SequenceEqual(first.GetColors(3)), "Disabling random order restores channel order");
            first.Select("Green");
            Save("Green", 16, 1, (_, _) => SKColors.Blue);
            first.Reload();
            Check(first.SelectedImage == "Green" && first.GetColors(1)[0] == Color4.Blue,
                "Reload preserves selection and reads changed PNG pixel data");
            File.Delete(Path.Combine(folder, "Green.png")); File.Delete(Path.Combine(bundled, "Green.png")); first.Reload();
            Check(first.SelectedImage == "Random", "Reload falls back to Random after selected palette deletion");
            first.Select("missing");
            Check(first.SelectedImage == "Random", "Unknown palette names use original Random fallback");
            Save("Green", 16, 1, (_, _) => SKColors.Lime);

            if (OperatingSystem.IsMacOS())
            {
                var midi = MakeMidi(embeddedBlue: false);
                VerifyScene(midi, .2, "fixture");
                if (userMidiPath != null)
                {
                    var userMidi = MidiSequence.Load(userMidiPath);
                    var note = userMidi.Notes.First(n => n.EndSeconds > n.StartSeconds + .1);
                    VerifyScene(userMidi, note.StartSeconds + .05, "user MIDI");
                    Console.WriteLine($"Palette user MIDI: {userMidi.NoteCount:N0} notes, {userMidi.Tracks.Length} tracks, {userMidi.ColorEvents.Length} embedded color events");
                }
                var settings = new RenderSettings { width = 128, height = 72 };
                var plugin = new FlatRender.Render(settings);
                ((FlatRender.Settings)plugin.SettingsControl).palette = "Red";
                using var scene = new SceneRenderer(MakeMidi(embeddedBlue: true), settings, plugin);
                var blue = scene.Render(.2);
                scene.ReloadPalette("Green");
                Check(scene.Render(.2).SequenceEqual(blue), "Palette changes preserve active embedded MIDI color events");
                settings.ignoreColorEvents = true;
                Check(!scene.Render(.2).SequenceEqual(blue), "Ignoring embedded colors restores the currently selected palette");
            }
            Console.WriteLine($"PASS {checks} palette assertions: valid data, alpha, pairs, independent modules, shuffle, reload, profile selection, live pixels, and MIDI event priority");

            void Save(string name, int width, int height, Func<int, int, SKColor> color)
            {
                using var image = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) image.SetPixel(x, y, color(x, y));
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                using var file = File.Create(Path.Combine(folder, name + ".png")); png.SaveTo(file);
            }
            void VerifyScene(MidiSequence midi, double seconds, string label)
            {
                var settings = new RenderSettings { width = 128, height = 72 };
                var plugin = new FlatRender.Render(settings);
                var fields = (FlatRender.Settings)plugin.SettingsControl;
                fields.palette = "Red";
                using var scene = new SceneRenderer(midi, settings, plugin);
                var red = scene.Render(seconds);
                var list = (FastList<Note>)typeof(SceneRenderer).GetField("visible", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!;
                var note = list.First;
                Check(note != null && note.color.left == Color4.Red,
                    label + ": without an explicit override, module palette reaches rendered notes");
                note!.meta = "kept";
                fields.palette = "Green"; // The same mutation made by applying an original profile/reset.
                var green = scene.Render(seconds);
                Check(!red.SequenceEqual(green) && note.color.left == Color4.Lime,
                    label + ": a changed profile palette reaches actual rendered pixels on the next frame");
                scene.ReloadPalette("Blue");
                Check(!scene.Render(seconds).SequenceEqual(green) && ReferenceEquals(note, list.First) && (string?)note.meta == "kept",
                    label + ": live selection updates color without resetting note/particle identity");
            }
        }
        finally
        {
            AssetPaths.Root = originalRoot;
            PaletteService.PfaConfigPath = originalPfaPath;
            PaletteService.PaletteDirectory = originalPaletteDirectory;
            Directory.Delete(temp, recursive: true);
        }
    }

    static MidiSequence MakeMidi(bool embeddedBlue)
    {
        byte[] color = embeddedBlue ? [0, 255, 10, 8, 0, 15, 0, 0, 0, 0, 255, 255] : [];
        byte[] events = [0, 0x90, 60, 100, 0x8F, 0, 0x80, 60, 0, 0, 255, 47, 0];
        using var data = new MemoryStream();
        data.Write("MThd"u8); data.Write([0, 0, 0, 6, 0, 0, 0, 1, 1, 224]);
        data.Write("MTrk"u8); data.Write([0, 0, 0, (byte)(color.Length + events.Length)]);
        data.Write(color); data.Write(events); data.Position = 0;
        return MidiSequence.Load(data);
    }
}
