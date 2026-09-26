using System.Reflection;
using Zenith.Core.Midi;
using Zenith.Core.Rendering;
using ZenithEngine;

internal static class ScenePaletteArgumentChecks
{
    internal static void Run()
    {
        if (!OperatingSystem.IsMacOS()) return;
        byte[] bytes = [77, 84, 104, 100, 0, 0, 0, 6, 0, 0, 0, 1, 1, 224,
            77, 84, 114, 107, 0, 0, 0, 13, 0, 0x90, 60, 100, 0x8F, 0, 0x80, 60, 0, 0, 255, 47, 0];
        var midi = MidiSequence.Load(new MemoryStream(bytes));
        foreach (bool explicitOverride in new[] { false, true })
        {
            var settings = new RenderSettings { width = 64, height = 36 };
            var plugin = new FlatRender.Render(settings);
            var moduleSettings = (FlatRender.Settings)plugin.SettingsControl;
            moduleSettings.palette = explicitOverride ? "Random" : "Random Gradients";
            var selection = PaletteService.For(moduleSettings);
            using var scene = new SceneRenderer(midi, settings, plugin,
                palette: explicitOverride ? "Random Gradients" : null);
            if (moduleSettings.palette != "Random Gradients" || selection.SelectedImage != "Random Gradients")
                throw new Exception("Scene palette constructor precedence or synchronized module selection failed.");
            var expected = selection.GetColors(1);
            var first = scene.Render(.2);
            var notes = (FastList<Note>)typeof(SceneRenderer).GetField("visible", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!;
            if (notes.First.color.left != expected[0] || notes.First.color.right != expected[1])
                throw new Exception("The first frame did not use the constructor's selected palette.");
            if (!scene.Render(.2).SequenceEqual(first) || moduleSettings.palette != "Random Gradients")
                throw new Exception("A later frame restored the previous module palette.");
        }
        Console.WriteLine("PASS Scene explicit palette precedence, synchronized module field, null fallback, and stable subsequent-frame pixels");
    }
}
