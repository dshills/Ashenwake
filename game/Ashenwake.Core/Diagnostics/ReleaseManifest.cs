using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Diagnostics;

public sealed record ReleaseInput(string Path, string Category);
public sealed record ReleaseEntry(string Path, string Category, long Bytes, string Sha256);
public sealed record ReleaseManifest(int SchemaVersion, string BuildId, string RulesVersion, string Runtime,
    string Platform, ReleaseEntry[] Files, string ContentHash, string AssetHash, string ManifestHash);
public sealed record ManifestCheck(bool Success, string[] Problems);

public static class ReleaseManifests
{
    public static ReleaseManifest Create(string root, string buildId, string rulesVersion, IEnumerable<ReleaseInput> inputs,
        string runtime = "net8.0", string platform = "portable-source")
    {
        ValidateIdentity(buildId, rulesVersion, runtime, platform);
        var selected = inputs.ToArray();
        if (selected.Length is < 1 or > 100000 || selected.Any(i => i is null) || selected.Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
            throw new InvalidDataException("Manifest requires a bounded set of unique portable paths.");
        var files = new List<ReleaseEntry>();
        foreach (var input in selected.OrderBy(i => i.Path, StringComparer.Ordinal))
        {
            ValidateCategory(input.Category); string full = Resolve(root, input.Path);
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            files.Add(new(input.Path, input.Category, stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
        }
        var manifest = new ReleaseManifest(1, buildId, rulesVersion, runtime, platform, files.ToArray(),
            GroupHash(files, "content"), GroupHash(files, "asset"), "");
        return manifest with { ManifestHash = Identity(manifest) };
    }
    public static ManifestCheck Verify(string root, ReleaseManifest manifest, bool rejectUnexpectedFiles = true)
    {
        var problems = new List<string>();
        try { Validate(manifest); }
        catch (InvalidDataException ex) { return new(false, [ex.Message]); }
        foreach (var entry in manifest.Files)
        {
            try
            {
                string path = Resolve(root, entry.Path);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length != entry.Bytes || Convert.ToHexString(SHA256.HashData(stream)) != entry.Sha256) problems.Add("File differs: " + entry.Path);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { problems.Add("File missing, unsafe, or unreadable: " + entry.Path); }
        }
        if (rejectUnexpectedFiles)
        {
            var expected = manifest.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            var directories = new Queue<string>(); directories.Enqueue(Path.GetFullPath(root));
            int inspected = 0;
            while (directories.Count > 0)
            {
                string directory = directories.Dequeue();
                try
                {
                    foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        if (++inspected > 200000) { problems.Add("Package enumeration exceeds the verification bound."); directories.Clear(); break; }
                        string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { problems.Add("Unsafe package link: " + relative); continue; }
                        if (Directory.Exists(path)) directories.Enqueue(path);
                        else if (!expected.Contains(relative)) problems.Add("Unexpected package file: " + relative);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add("Package directory is missing or unreadable."); }
            }
        }
        return new(problems.Count == 0, problems.ToArray());
    }
    public static void Validate(ReleaseManifest manifest)
    {
        static void Check([DoesNotReturnIf(false)] bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        Check(manifest is not null && manifest.SchemaVersion == 1 && manifest.Files is { Length: > 0 and <= 100000 } && manifest.Files.All(f => f is not null), "Invalid release manifest schema/files.");
        ValidateIdentity(manifest.BuildId, manifest.RulesVersion, manifest.Runtime, manifest.Platform);
        Check(manifest.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == manifest.Files.Length, "Manifest paths collide on a supported filesystem.");
        Check(manifest.Files.Select(f => f.Path).SequenceEqual(manifest.Files.Select(f => f.Path).OrderBy(x => x, StringComparer.Ordinal)), "Manifest paths must be in canonical order.");
        foreach (var entry in manifest.Files)
        {
            ValidateRelativePath(entry.Path); ValidateCategory(entry.Category);
            Check(entry.Bytes >= 0 && entry.Sha256 is { Length: 64 } && entry.Sha256.All(Uri.IsHexDigit), "Invalid release entry size/hash.");
        }
        Check(manifest.ContentHash == GroupHash(manifest.Files, "content") && manifest.AssetHash == GroupHash(manifest.Files, "asset") && manifest.ManifestHash == Identity(manifest), "Manifest metadata/content/asset identity was modified.");
    }
    private static string GroupHash(IEnumerable<ReleaseEntry> files, string category) => JsonData.Hash(files.Where(f => f.Category == category).ToArray());
    private static string Identity(ReleaseManifest manifest) => JsonData.Hash(manifest with { ManifestHash = "" });
    private static void ValidateIdentity(params string[] identities)
    {
        if (identities.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 120 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+'))))
            throw new InvalidDataException("Manifest identities must be bounded portable tokens.");
    }
    private static void ValidateCategory(string category)
    {
        if (category is not ("build" or "content" or "asset" or "source" or "license" or "fixture")) throw new InvalidDataException("Unknown release manifest category.");
    }
    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl) || path.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException("Release files require canonical relative paths without traversal.");
    }
    public static string Resolve(string root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        string fullRoot = Path.GetFullPath(root); string path = fullRoot;
        // The caller-selected root is the trust boundary. Reject every package link below it.
        // Ancestors may include operating-system aliases such as macOS /tmp -> /private/tmp.
        if (new DirectoryInfo(fullRoot).LinkTarget is not null) throw new InvalidDataException("Manifest root is a symbolic link.");
        foreach (string component in relativePath.Split('/'))
        {
            path = Path.Combine(path, component);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Release path contains a symbolic link.");
        }
        return path;
    }
}

public sealed record AssetCredit(string Id, string Name, string[] SourceFiles, string Origin, string RightsStatus,
    bool RedistributionApproved, string RequiredCredit);
public sealed record AssetInventory(int SchemaVersion, AssetCredit[] Assets, string[] ThirdPartyRuntimeNotices);
public sealed record AssetAudit(bool FilesPresent, bool RightsApproved, string[] Issues);
public static class AssetCredits
{
    public static AssetAudit Audit(string root, AssetInventory inventory)
    {
        if (inventory is null || inventory.SchemaVersion != 1 || inventory.Assets is not { Length: > 0 } || inventory.Assets.Any(a => a is null) || inventory.ThirdPartyRuntimeNotices is null)
            throw new InvalidDataException("Asset credit inventory is invalid.");
        var ids = new HashSet<string>(); var issues = new List<string>(); bool present = true, rights = true;
        foreach (var asset in inventory.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Id) || !ids.Add(asset.Id) || string.IsNullOrWhiteSpace(asset.Name) || asset.SourceFiles is not { Length: > 0 } || string.IsNullOrWhiteSpace(asset.Origin) || string.IsNullOrWhiteSpace(asset.RightsStatus) || string.IsNullOrWhiteSpace(asset.RequiredCredit))
                throw new InvalidDataException("Asset entries require unique identity, sources, origin, rights, and credit.");
            foreach (string path in asset.SourceFiles)
                try { ReleaseManifests.Resolve(root, path); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { present = false; issues.Add("Missing/unsafe asset source: " + path); }
            if (!asset.RedistributionApproved) { rights = false; issues.Add("Distribution rights await owner declaration: " + asset.Id); }
        }
        foreach (string notice in inventory.ThirdPartyRuntimeNotices)
            try { ReleaseManifests.Resolve(root, notice); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { present = false; issues.Add("Missing runtime notice: " + notice); }
        return new(present, rights, issues.ToArray());
    }
}
