using System.Security;
using System.Text.Json;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Client;

/// <summary>Read-only, validated character previews. Launching a character must load its archive again.</summary>
public sealed class ClientCharacterCatalog
{
    public const int MaximumSlots = 128;
    public const long MaximumArchiveBytes = 96L * 1024 * 1024;
    private readonly string _combatJson;
    private readonly AdventureContent _adventure;
    private readonly ProgressionContent _progression;
    private readonly CampaignContent _campaign;
    private readonly EndgameContent _endgame;
    private readonly ExperimentContent _experiment;
    private readonly CampaignDefinition _campaignDefinition;

    /// <summary>More slots exist than this menu scan can display. No archives are removed.</summary>
    public bool LimitReached { get; private set; }

    public ClientCharacterCatalog(string combatJson, AdventureContent adventure, ProgressionContent progression,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent experiment)
    {
        _combatJson = combatJson; _adventure = adventure; _progression = progression;
        _campaign = campaign; _endgame = endgame; _experiment = experiment;
        _campaignDefinition = campaign.Capture();
    }

    public CharacterSlot[] Scan(string directory)
    {
        LimitReached = false;
        if (!Directory.Exists(directory))
        {
            // A genuinely absent directory is a fresh installation. Existing files or
            // inaccessible directory entries should reach the caller's menu notice.
            try { _ = File.GetAttributes(directory); }
            catch (DirectoryNotFoundException) { return []; }
            catch (FileNotFoundException) { return []; }
            throw new IOException("The character save directory is unavailable.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(path);
            if (name.EndsWith(".bak", StringComparison.Ordinal)) name = name[..^4];
            if (!IsValidFilename(name) || !names.Add(name)) continue;
            if (names.Count > MaximumSlots) { names.Remove(name); LimitReached = true; break; }
        }
        return names.Select(name => Inspect(directory, name)).OrderByDescending(slot => slot.SavedUtc)
            .ThenBy(slot => slot.Filename, StringComparer.Ordinal).ToArray();
    }

    public CharacterSlot Inspect(string directory, string filename)
    {
        bool echoes = IsEchoesFilename(filename);
        if (!IsValidFilename(filename)) return Unavailable(filename, echoes, "This is not a supported character slot.");
        string path = Path.Combine(directory, filename);
        try
        {
            // Load performs the authoritative checksum, version, rules and state validation,
            // including recovery from a valid backup. Bound every file it may read first.
            // Profile discoveries are merged in memory by Load; scanning never writes them.
            Preflight(path); Preflight(path + ".bak");
            string profile = EndgameRuntimeSaveStore.ProfilePath(path);
            Preflight(profile); Preflight(profile + ".bak");
            EndgameRuntimeSession session; bool recovered;
            if (echoes)
            {
                var loaded = ExperimentSaveStore.Load(path, _combatJson, _adventure, _progression, _campaign, _endgame, _experiment);
                session = loaded.Session.Endgame; recovered = loaded.RecoveredBackup;
            }
            else
            {
                var loaded = EndgameRuntimeSaveStore.Load(path, _combatJson, _adventure, _progression, _campaign, _endgame);
                session = loaded.Session; recovered = loaded.RecoveredBackup;
            }
            var progression = session.Production.ProgressionView;
            var permanent = session.Production.Capture();
            var appearance = CharacterAppearance.FromProgression(permanent.Progression, session.Production.View.ActiveManifestations,
                permanent.Expedition.Adventure.Anatomy.Values);
            return new(filename, progression.Discipline, progression.Level, Location(session), echoes, true, recovered,
                recovered ? "The previous valid backup is ready to continue. The original archive is preserved." : "",
                appearance, File.GetLastWriteTimeUtc(recovered ? path + ".bak" : path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or SecurityException or JsonException or
            ArgumentException or InvalidOperationException or KeyNotFoundException or OverflowException or NotSupportedException)
        {
            string notice = ex switch
            {
                SaveCompatibilityException => "This character needs its matching game version or a supported migration. Its archive is preserved.",
                UnauthorizedAccessException or SecurityException => "This character could not be read. Check access to the save folder; its archive is preserved.",
                ArchiveLimitException => "This character or its shared profile exceeds the supported save size. Its archive is preserved.",
                UnsupportedAliasException => "This slot uses a file link or unsupported file type. Its archive is preserved.",
                InvalidDataException or JsonException => "This character or its shared profile is damaged, and no usable backup could be loaded. Its archive is preserved.",
                _ => "This character could not be loaded. Its archive is preserved; try again after checking the save folder."
            };
            return Unavailable(filename, echoes, notice, LastSaved(path));
        }
    }

    public static bool IsEchoesFilename(string filename) => IsValidFilename(filename) && filename.StartsWith("echoes.", StringComparison.Ordinal);

    public static bool IsValidFilename(string filename)
    {
        if (string.IsNullOrEmpty(filename) || filename.Length > 240 || filename != Path.GetFileName(filename)) return false;
        if (filename == "endgame.save.json") return true;
        int prefix = filename.StartsWith("endgame.", StringComparison.Ordinal) ? 8 : filename.StartsWith("echoes.", StringComparison.Ordinal) ? 7 : 0;
        const string ending = ".save.json";
        if (prefix == 0 || !filename.EndsWith(ending, StringComparison.Ordinal) || filename.Length <= prefix + ending.Length) return false;
        string suffix = filename[prefix..^ending.Length];
        if (!char.IsAsciiLetterOrDigit(suffix[0]) || !char.IsAsciiLetterOrDigit(suffix[^1]) || suffix.Contains("..", StringComparison.Ordinal) ||
            suffix.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_'))) return false;
        return !suffix.Split('.', '-', '_').Any(part => part is "checkpoint" or "smoke" or "replay");
    }

    private string Location(EndgameRuntimeSession session)
    {
        if (session.InHub) return "Greyhaven";
        if (session.RunView is { } run && session.Combat.View.Endgame is not null)
        {
            string region = _campaignDefinition.Acts.FirstOrDefault(act => act.Id == run.Region)?.Name ?? run.Region;
            string room = $" · {Math.Min(run.EncounterIndex + 1, run.EncounterCount)}/{run.EncounterCount}";
            return run.Kind == "GodHunt" ? run.Name + room : $"{region} · Fracture {run.Tier}" + room;
        }
        return session.Campaign.View.Region;
    }

    private static CharacterSlot Unavailable(string filename, bool echoes, string notice, DateTime savedUtc = default)
        => new(filename, "Unknown", 0, "Unavailable", echoes, false, false, notice, null, savedUtc);

    internal static void Preflight(string path)
    {
        var file = new FileInfo(path);
        if (file.LinkTarget is not null) throw new UnsupportedAliasException();
        if (!file.Exists) return;
        if ((file.Attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            throw new UnsupportedAliasException();
        if (file.Length > MaximumArchiveBytes) throw new ArchiveLimitException();
    }

    private static DateTime LastSaved(string path)
    {
        try
        {
            if (File.Exists(path)) return File.GetLastWriteTimeUtc(path);
            if (File.Exists(path + ".bak")) return File.GetLastWriteTimeUtc(path + ".bak");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { }
        return default;
    }

    private sealed class ArchiveLimitException : IOException;
    private sealed class UnsupportedAliasException : IOException;
}
