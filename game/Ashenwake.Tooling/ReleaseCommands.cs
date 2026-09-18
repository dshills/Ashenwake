using System.Globalization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Content;
using Ashenwake.Core.Diagnostics;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Tooling;

internal static class ReleaseCommands
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) return Usage();
        if (args[1] == "projectile-ceiling" && args.Length == 2)
        {
            var report = ProjectileCeilingAudit.Run(File.ReadAllText("content/combat.json"));
            AtomicFile.Write("artifacts/release/projectile-ceiling.json", JsonData.Write(report));
            Console.WriteLine(JsonData.Write(report)); return 0;
        }
        if (args[1] == "endgame-soak" && args.Length is 3 or 4)
            return ReleaseEndgameSoak.Run(args.Length == 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 3, args[2]);
        if (args[1] == "manifest" && args.Length is 7 or 8)
        {
            string root = Path.GetFullPath(args[2]);
            var manifest = ReleaseManifests.Create(root, args[3], args[4], EnumerateInputs(root), args[5], args[6]);
            string output = args.Length == 8 ? args[7] : "artifacts/release/manifest.json";
            string fullOutput = Path.GetFullPath(output);
            if (fullOutput.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || fullOutput == root)
                throw new ArgumentException("Keep the immutable manifest outside the package root.");
            AtomicFile.Write(output, JsonData.Write(manifest));
            Console.WriteLine(JsonData.Write(new { kind = "ReleaseManifestCreated", manifest.BuildId, manifest.ManifestHash, manifest.ContentHash, manifest.AssetHash, files = manifest.Files.Length, output })); return 0;
        }
        if (args[1] == "verify" && args.Length == 4)
        {
            var result = ReleaseManifests.Verify(args[2], JsonData.Read<ReleaseManifest>(File.ReadAllText(args[3])));
            Console.WriteLine(JsonData.Write(result)); return result.Success ? 0 : 1;
        }
        if (args[1] == "soak" && args.Length is >= 2 and <= 5)
        {
            int sessions = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 10;
            int ticks = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 900;
            var report = CombatSoak.Run(File.ReadAllText("content/combat.json"), sessions, ticks);
            string output = args.Length > 4 ? args[4] : "artifacts/release/soak.json";
            AtomicFile.Write(output, JsonData.Write(report)); Console.WriteLine(JsonData.Write(report)); return report.Success ? 0 : 1;
        }
        if (args[1] is "fixtures" or "audit" && args.Length is 2 or 3)
        {
            string root = Path.GetFullPath(args.Length == 3 ? args[2] : ".");
            var fixtures = SaveFixtureAudit.Run(root, JsonData.Read<SaveFixtureInventory>(File.ReadAllText(Path.Combine(root, "fixtures/save-fixtures.json"))),
                AdventureContent.Parse(File.ReadAllText(Path.Combine(root, "content/adventure.json"))),
                File.ReadAllText(Path.Combine(root, "content/combat.json")), ContentCompiler.Compile(File.ReadAllText(Path.Combine(root, "content/phase0.json"))));
            if (args[1] == "fixtures") { Console.WriteLine(JsonData.Write(fixtures)); return fixtures.Success ? 0 : 1; }
            var assets = AssetCredits.Audit(root, JsonData.Read<AssetInventory>(File.ReadAllText(Path.Combine(root, "assets/credits.json"))));
            var report = new
            {
                kind = "ReleaseEngineeringAudit",
                automatedChecksPassed = fixtures.Success && assets.FilesPresent,
                readyForPublicRelease = false,
                fixtures,
                assets,
                unresolvedReleaseGates = new[] { "Target OS/GPU/input matrix and distribution-artifact playthrough", "External combat/build readability playtests", "Production campaign/endgame content and art acceptance", "Signing, notarization, packaging, and third-party runtime license notices", "Owner approval of asset distribution rights", "Measured long-session stability and accessibility/localization acceptance" }
            };
            AtomicFile.Write(Path.Combine(root, "artifacts/release/readiness.json"), JsonData.Write(report)); Console.WriteLine(JsonData.Write(report));
            return report.automatedChecksPassed ? 0 : 1;
        }
        return Usage();
    }
    private static ReleaseInput[] EnumerateInputs(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Distribution root does not exist.");
        var inputs = new List<ReleaseInput>(); var directories = new Queue<string>(); directories.Enqueue(root);
        while (directories.Count > 0)
        {
            string directory = directories.Dequeue();
            foreach (string path in Directory.EnumerateFileSystemEntries(directory).OrderBy(p => p, StringComparer.Ordinal))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Distribution must not contain symbolic links.");
                if (Directory.Exists(path)) { directories.Enqueue(path); continue; }
                string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                string extension = Path.GetExtension(path).ToLowerInvariant(); string name = Path.GetFileName(path).ToLowerInvariant();
                string category = relative.StartsWith("content/", StringComparison.Ordinal) || name is "content.bundle.json" or "combat.json" or "adventure.json" or "progression.json" or "campaign.json" or "endgame.json" ? "content" :
                    relative.StartsWith("assets/", StringComparison.Ordinal) || extension is ".png" or ".jpg" or ".ogg" or ".wav" or ".glb" or ".gltf" or ".svg" ? "asset" :
                    name.Contains("license", StringComparison.Ordinal) || name.Contains("credits", StringComparison.Ordinal) || name.Contains("notice", StringComparison.Ordinal) ? "license" : "build";
                inputs.Add(new(relative, category));
                if (inputs.Count > 100000) throw new InvalidDataException("Distribution manifest file limit exceeded.");
            }
        }
        return inputs.ToArray();
    }
    private static int Usage()
    {
        Console.Error.WriteLine("release manifest <distribution-root> <build-id> <rules> <runtime> <platform> [output] | release verify <root> <manifest> | release soak [sessions] [ticks] [output] | release endgame-soak <new-output-directory> [extra-fractures] | release projectile-ceiling | release fixtures [repo-root] | release audit [repo-root]");
        return 2;
    }
}
