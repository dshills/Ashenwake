using Ashenwake.Core.Exploration;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Campaign;

public sealed record CampaignRuntimeSave([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string StateHash, [property: JsonRequired] CampaignRuntimeSnapshot State);
public sealed record CampaignRuntimeLoadResult(CampaignRuntimeSession Session, bool RecoveredBackup);

public static class CampaignRuntimeSaveStore
{
    public static string ProfilePath(string savePath) => ProductionSaveStore.ProfilePath(savePath);
    public static CampaignRuntimeSession Read(string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign, string json)
    {
        try { return ReadWithLegendaryUpgrade(combatJson, adventure, policy, campaign, json); }
        catch (SaveCompatibilityException) when (OpeningCatalogMigration.TryPrevious(combatJson, policy, campaign,
            out var previousCombat, out var previousPolicy, out var previousCampaign))
        {
            var original = Read(previousCombat, adventure, previousPolicy, previousCampaign, json);
            return CampaignRuntimeSession.Restore(combatJson, adventure, policy, campaign,
                OpeningCatalogMigration.Rebind(original.Capture(), combatJson, adventure, policy, campaign));
        }
    }

    private static CampaignRuntimeSession ReadWithLegendaryUpgrade(string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign, string json)
    {
        try { return ReadExact(combatJson, adventure, policy, campaign, json); }
        catch (SaveCompatibilityException) when (LegendaryCatalogMigration.TryPrevious(combatJson, policy, out var previousCombat, out var previousPolicy))
        {
            var original = ReadExact(previousCombat, adventure, previousPolicy, campaign, json);
            return CampaignRuntimeSession.Restore(combatJson, adventure, policy, campaign,
                LegendaryCatalogMigration.Rebind(original.Capture(), combatJson, adventure, policy, campaign));
        }
    }

    private static CampaignRuntimeSession ReadExact(string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign, string json)
    {
        if (json.Length > 64 * 1024 * 1024) throw new InvalidDataException("Campaign archive exceeds its bounded size.");
        using var document = JsonDocument.Parse(json); ArchiveHeaders.Require(document.RootElement, 1);
        var state = ArchiveHeaders.Object(document.RootElement, "state"); ArchiveHeaders.Require(state, 1, "campaign-runtime.1");
        LocalMapAtlas.Inspect(state);
        var narrative = ArchiveHeaders.Object(state, "campaign"); ArchiveHeaders.Require(narrative, 1); ArchiveHeaders.Identity(narrative, "contentHash", campaign.Hash);
        string identity = CombatContent.Parse(combatJson).Identity;
        var arena = ArchiveHeaders.Object(state, "combat"); ArchiveHeaders.Require(arena, 1, "combat.1"); ArchiveHeaders.Identity(arena, "contentHash", identity);
        if (state.TryGetProperty("clearedRooms", out var rooms) && rooms.ValueKind != JsonValueKind.Null)
        {
            if (rooms.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid cleared room archive.");
            foreach (var room in rooms.EnumerateObject())
            { ArchiveHeaders.Require(room.Value, 1, "combat.1"); ArchiveHeaders.Identity(room.Value, "contentHash", identity); }
        }
        var permanent = ArchiveHeaders.Object(state, "production"); ArchiveHeaders.Require(permanent, 1, "production.1");
        var expedition = ArchiveHeaders.Object(permanent, "expedition"); ArchiveHeaders.Require(expedition, 1, "expedition.1");
        ArchiveHeaders.Identity(expedition, "adventureHash", ProductionContent.ResolveAdventure(combatJson, adventure).Hash);
        var body = ArchiveHeaders.Object(expedition, "combat"); ArchiveHeaders.Require(body, 1, "combat.1"); ArchiveHeaders.Identity(body, "contentHash", identity);
        var character = ArchiveHeaders.Object(ArchiveHeaders.Object(permanent, "progression"), "character"); ArchiveHeaders.Require(character, 1);
        var resolved = ProductionContent.Resolve(combatJson, CampaignRuntimeSession.ResolvePolicy(policy, campaign), adventure);
        ArchiveHeaders.Identity(character, "contentHash", resolved.Hash);
        ArchiveHeaders.Checksum(document.RootElement, "state");
        var save = JsonData.Read<CampaignRuntimeSave>(json);
        if (save.State is null || save.StateHash != JsonData.Hash(save.State)) throw new InvalidDataException("Campaign checksum mismatch.");
        return CampaignRuntimeSession.Restore(combatJson, adventure, policy, campaign, save.State);
    }
    public static void Write(string path, string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign, CampaignRuntimeSnapshot snapshot)
    {
        var session = CampaignRuntimeSession.Restore(combatJson, adventure, policy, campaign, snapshot);
        string full = Path.GetFullPath(path), profilePath = ProductionSaveStore.ProfilePath(full); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var lease = new FileStream(full + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        string? previous = null;
        if (File.Exists(full))
        {
            previous = File.ReadAllText(full);
            try { Read(combatJson, adventure, policy, campaign, previous); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { previous = null; }
        }
        var merged = LocalProfileStore.Merge(profilePath, session.Production.Content, session.Production.Capture().Progression.Profile);
        session.Production.MergeProfile(merged);
        if (previous is not null) AtomicFile.Write(full + ".bak", previous);
        var state = session.Capture(); AtomicFile.Write(full, JsonData.Write(new CampaignRuntimeSave(1, JsonData.Hash(state), state)));
    }
    public static CampaignRuntimeLoadResult Load(string path, string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign)
    {
        CampaignRuntimeSession session; bool recovered = false;
        try { session = Read(combatJson, adventure, policy, campaign, File.ReadAllText(path)); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FileNotFoundException)
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException("Campaign save is missing/corrupt and no valid backup exists.", ex);
            session = Read(combatJson, adventure, policy, campaign, File.ReadAllText(path + ".bak")); recovered = true;
        }
        string profilePath = ProductionSaveStore.ProfilePath(path);
        if (File.Exists(profilePath) || File.Exists(profilePath + ".bak")) session.Production.MergeProfile(LocalProfileStore.Load(profilePath, session.Production.Content).Profile);
        // Shared profile metadata establishes the new replay origin, as with production saves.
        session = CampaignRuntimeSession.Restore(combatJson, adventure, policy, campaign, session.Capture());
        return new(session, recovered);
    }
}

public static class CampaignRuntimeReplayRunner
{
    public static ReplayResult Run(string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign, CampaignRuntimeReplay replay)
    {
        if (replay is null || replay.SchemaVersion != 1 || replay.Initial is null || replay.Frames is not { Length: <= 1800 }) throw new InvalidDataException("Invalid campaign replay header or size.");
        var session = CampaignRuntimeSession.Restore(combatJson, adventure, policy, campaign, replay.Initial);
        for (int index = 0; index < replay.Frames.Length; index++)
        {
            var frame = replay.Frames[index]; if (frame is null || frame.Command is null) throw new InvalidDataException("Null campaign replay operation.");
            var result = session.Execute(frame.Command, recordReplay: false);
            if (session.StateHash != frame.StateHash) return new(false, index, "Campaign state diverged at operation index.", session.StateHash);
            if (JsonData.Hash(result) != frame.EventHash) return new(false, index, "Campaign events diverged at operation index.", session.StateHash);
        }
        return new(true, null, "Campaign combat, rewards, choices, and exploration hashes match.", session.StateHash);
    }
}
