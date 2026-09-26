using System.Text.Json;
using ScriptedEngine;
using Zenith.Core.Scripted;

internal static class ScriptedProfileStorageChecks
{
    internal static void Run()
    {
        string originalDirectory = ScriptedProfiles.UserProfilesDirectory;
        string temp = Path.Combine(Path.GetTempPath(), "zenith-profile-storage-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool condition, string description)
        {
            checks++;
            if (!condition) throw new Exception(description);
        }
        try
        {
            ScriptedProfiles.UserProfilesDirectory = Path.Combine(temp, "UserProfiles");
            const string resource = "Plugins/Assets/Scripted/Resources/Example";
            string appPack = Path.Combine(temp, "Zenith.app/Contents/Resources/Zenith", resource);
            string devPack = Path.Combine(temp, "assets/windows/Zenith", resource);
            Directory.CreateDirectory(appPack);
            File.WriteAllText(Path.Combine(appPack, "script.cs"), """
                using System.Collections.Generic;
                using ScriptedEngine;
                using ZenithEngine;
                public class Script {
                    public UISetting[] SettingsUI = { new UINumber("Amount", 1, 0, 100, 0) };
                    public void Load() { }
                    public void Render(IEnumerable<Note> notes, RenderOptions options) { }
                }
                """);
            string bundledPath = appPack + ".profiles.json";
            const string original = "{\"Original\":[2],\"Edited\":[3]}";
            File.WriteAllText(bundledPath, original);
            string userPath = ScriptedProfiles.GetDefaultFilePath(appPack);
            Check(userPath == ScriptedProfiles.GetDefaultFilePath(devPack), "Development and app resources resolve to the same persistent profile path");
            Check(userPath != ScriptedProfiles.GetDefaultFilePath(appPack.Replace("/Resources/Example", "/Resources/Nested/Example")),
                "Same-named skins in different resource folders do not collide");
            Directory.CreateDirectory(Path.GetDirectoryName(userPath)!);
            File.WriteAllText(userPath, "{\"Edited\":[9],\"User\":[4]}");
            using var pack = ScriptedPack.Load(appPack);
            var migrated = new ScriptedProfiles(pack);
            Check(migrated.FilePath == userPath && migrated.Names.Count == 3, "First import merges original and existing user profiles");
            migrated.Apply("Edited");
            var value = (UINumber)pack.SettingsUI.Single();
            Check(value.Value == 9, "User edits win over the corresponding bundled profile");
            value.Value = 7;
            migrated.Save("Comparison");
            migrated.Delete("Original");
            var reopened = new ScriptedProfiles(pack);
            reopened.Apply("Comparison");
            Check(value.Value == 7 && !reopened.Names.Contains("Original"), "Saved values and deletions survive reopening without reimporting defaults");
            Check(File.ReadAllText(bundledPath) == original, "Migration, saving and deleting never modify the app's bundled profiles");
            string explicitPath = Path.Combine(temp, "explicit.json");
            var explicitProfiles = new ScriptedProfiles(pack, explicitPath);
            Check(explicitProfiles.FilePath == explicitPath && explicitProfiles.Names.Count == 0,
                "An explicit file path stays isolated and does not import bundled profiles");
            string externalPack = Path.Combine(temp, "External", "Example.zrp");
            Check(ScriptedProfiles.GetDefaultFilePath(externalPack) == externalPack + ".profiles.json",
                "External packs retain the original adjacent profile rule");
            using var persisted = JsonDocument.Parse(File.ReadAllText(userPath));
            Check(persisted.RootElement.GetProperty("Comparison")[0].GetInt32() == 7,
                "Persistent storage keeps the original named-array JSON format");
            Console.WriteLine($"PASS {checks} Scripted profile storage assertions: first merge, user precedence, bundle preservation, deletion, path identity, explicit isolation");
        }
        finally
        {
            ScriptedProfiles.UserProfilesDirectory = originalDirectory;
            Directory.Delete(temp, recursive: true);
        }
    }
}
