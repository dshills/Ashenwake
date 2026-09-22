using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Production;

public sealed record ProductionSave([property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string StateHash, [property: JsonRequired] ProductionSnapshot State);
public sealed record ProductionLoadResult(ProductionSession Session, bool RecoveredBackup);

public static class ProductionSaveStore
{
    public static ProductionSession Read(string combatJson, AdventureContent adventure, ProgressionContent policy, string json)
    {
        try { return ReadExact(combatJson, adventure, policy, json); }
        catch (SaveCompatibilityException) when (LegendaryCatalogMigration.TryPrevious(combatJson, policy, out var previousCombat, out var previousPolicy))
        {
            var original = Read(previousCombat, adventure, previousPolicy, json);
            return ProductionSession.Restore(combatJson, adventure, policy,
                LegendaryCatalogMigration.Rebind(original.Capture(), combatJson, adventure, policy));
        }
    }

    private static ProductionSession ReadExact(string combatJson, AdventureContent adventure, ProgressionContent policy, string json)
    {
        if (json.Length > 64 * 1024 * 1024) throw new InvalidDataException("Production save exceeds the bounded archive size.");
        using var headers = JsonDocument.Parse(json);
        ArchiveHeaders.Require(headers.RootElement, 1);
        var logical = ArchiveHeaders.Object(headers.RootElement, "state"); ArchiveHeaders.Require(logical, 1, "production.1");
        var expedition = ArchiveHeaders.Object(logical, "expedition"); ArchiveHeaders.Require(expedition, 1, "expedition.1");
        var combatState = ArchiveHeaders.Object(expedition, "combat"); ArchiveHeaders.Require(combatState, 1, "combat.1");
        var character = ArchiveHeaders.Object(ArchiveHeaders.Object(logical, "progression"), "character"); ArchiveHeaders.Require(character, 1);
        var resolved = ProductionContent.Resolve(combatJson, policy, adventure); var world = ProductionContent.ResolveAdventure(combatJson, adventure);
        ArchiveHeaders.Identity(expedition, "adventureHash", world.Hash);
        ArchiveHeaders.Identity(combatState, "contentHash", CombatContent.Parse(combatJson).Identity);
        ArchiveHeaders.Identity(character, "contentHash", resolved.Hash);
        ArchiveHeaders.Checksum(headers.RootElement, "state");
        var save = JsonData.Read<ProductionSave>(json);
        if (save.SchemaVersion != 1) throw new SaveCompatibilityException("Unsupported production save version; preserve it for its matching build.");
        if (save.State is null || save.State.Expedition?.Combat is null || save.State.Progression?.Character is null)
            throw new InvalidDataException("Production save is missing its logical state.");
        if (save.State.SchemaVersion != 1 || save.State.RulesVersion != "production.1")
            throw new SaveCompatibilityException("Unsupported production state rules; preserve this save.");
        if (save.State.Expedition.AdventureHash != world.Hash || save.State.Expedition.Combat.ContentHash != CombatContent.Parse(combatJson).Identity || save.State.Progression.Character.ContentHash != resolved.Hash)
            throw new SaveCompatibilityException("Production content differs; an explicit migration is required.");
        if (save.StateHash != JsonData.Hash(save.State)) throw new InvalidDataException("Production save checksum mismatch.");
        return ProductionSession.Restore(combatJson, adventure, policy, save.State);
    }
    public static void Write(string path, string combatJson, AdventureContent adventure, ProgressionContent policy, ProductionSnapshot snapshot)
    {
        var session = ProductionSession.Restore(combatJson, adventure, policy, snapshot);
        string full = Path.GetFullPath(path); ProfilePath(full); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var lease = new FileStream(full + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string? previous = null;
        if (File.Exists(full))
        {
            previous = File.ReadAllText(full);
            try { Read(combatJson, adventure, policy, previous); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { previous = null; }
        }
        // Only monotonic profile discoveries are shared. A profile write can survive a later
        // failed character write; it never carries currency, XP, mastery, or equipment.
        var merged = LocalProfileStore.Merge(ProfilePath(full), session.Content, session.Capture().Progression.Profile);
        session.MergeProfile(merged);
        if (previous is not null) AtomicFile.Write(full + ".bak", previous);
        var state = session.Capture(); AtomicFile.Write(full, JsonData.Write(new ProductionSave(1, JsonData.Hash(state), state)));
    }
    public static ProductionLoadResult Load(string path, string combatJson, AdventureContent adventure, ProgressionContent policy)
    {
        ProfilePath(path);
        ProductionSession session; bool recovered = false;
        try { session = Read(combatJson, adventure, policy, File.ReadAllText(path)); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FileNotFoundException)
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException("Production save is missing or corrupt and has no backup.", ex);
            session = Read(combatJson, adventure, policy, File.ReadAllText(path + ".bak")); recovered = true;
        }
        string profile = ProfilePath(path);
        if (File.Exists(profile) || File.Exists(profile + ".bak"))
            session.MergeProfile(LocalProfileStore.Load(profile, session.Content).Profile);
        // Merging shared metadata establishes a new replay origin rather than modifying a
        // running recording's initial state behind its back.
        session = ProductionSession.Restore(combatJson, adventure, policy, session.Capture());
        return new(session, recovered);
    }
    public static string ProfilePath(string savePath)
    {
        string full = Path.GetFullPath(savePath);
        if (Path.GetFileName(full).StartsWith("profile.json", StringComparison.OrdinalIgnoreCase) || new FileInfo(full).LinkTarget is not null)
            throw new ArgumentException("Choose a character filename separate from the reserved profile ledger and its backups; symbolic-link save aliases are unsupported.");
        return Path.Combine(Path.GetDirectoryName(full)!, "profile.json");
    }
}

public static class ProductionReplayRunner
{
    public static ReplayResult Run(string combatJson, AdventureContent adventure, ProgressionContent policy, ProductionReplay replay)
    {
        if (replay is null || replay.SchemaVersion != 1 || replay.Initial is null || replay.Frames is not { Length: <= 1800 })
            throw new InvalidDataException("Malformed or unsupported production replay.");
        var session = ProductionSession.Restore(combatJson, adventure, policy, replay.Initial);
        for (int index = 0; index < replay.Frames.Length; index++)
        {
            var frame = replay.Frames[index];
            if (frame is null || frame.Command is null) throw new InvalidDataException("Null production replay frame.");
            var result = session.Execute(frame.Command, recordReplay: false);
            if (session.StateHash != frame.StateHash) return new(false, index, "Production state diverged at operation index.", session.StateHash);
            if (JsonData.Hash(result) != frame.EventHash) return new(false, index, "Production events diverged at operation index.", session.StateHash);
        }
        return new(true, null, "All permanent progression, combat, and world operation hashes match.", session.StateHash);
    }
}

/// <summary>Explicit migration for the maintained Phase 2 hub format. It does not rewrite its source file.</summary>
public static class PhaseTwoMigration
{
    public const string CombatHash = "F378B5144C97B807236EB19246BDA871890AE8F3BA288E9BE77EE2D7ED2C5263";
    public const string AdventureHash = "D2ACF3F8ABAE0712E7417CF70F8ADD8BEE26E9AA60AB975DE728A583DC9F66E7";
    public static ProductionSession Read(string json, string combatJson, AdventureContent adventure, ProgressionContent policy)
    {
        if (json.Length > 32 * 1024 * 1024) throw new InvalidDataException("Legacy save is too large.");
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out int version) || version != 1)
            throw new SaveCompatibilityException("Unsupported legacy expedition envelope; preserve the original.");
        if (!root.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.Object || !root.TryGetProperty("stateHash", out var hash) || hash.ValueKind != JsonValueKind.String || hash.GetString() != JsonData.Hash(state))
            throw new InvalidDataException("Legacy expedition checksum mismatch.");
        // Verify original bytes before deserializing into types with newly introduced defaults.
        var legacy = JsonData.Read<ExpeditionSnapshot>(state.GetRawText());
        if (legacy.SchemaVersion != 1 || legacy.RulesVersion != "expedition.1" || legacy.AdventureHash != AdventureHash || legacy.Combat is null || legacy.Combat.ContentHash != CombatHash || legacy.Combat.SchemaVersion != 1 || legacy.Combat.RulesVersion != "combat.1")
            throw new SaveCompatibilityException("This legacy bundle is not in the maintained migration registry.");
        if (legacy.Adventure is null || legacy.GodwroughtItems is null) throw new InvalidDataException("Legacy world ownership is missing.");
        if (legacy.EncounterId != "hub" || legacy.Adventure.RoomId != "room.greyhaven" || legacy.Combat.EncounterId != "hub")
            throw new SaveCompatibilityException("Return to Greyhaven in the previous build before migrating this character.");
        if (legacy.Adventure.Godwrought is null || legacy.Adventure.Godwrought.Any(g => g is null))
            throw new InvalidDataException("Legacy Godwrought ownership is missing.");
        return ProductionSession.ImportPhaseTwo(combatJson, adventure, policy, legacy);
    }
}
