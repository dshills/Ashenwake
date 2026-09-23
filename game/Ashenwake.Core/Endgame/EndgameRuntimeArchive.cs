using Ashenwake.Core.Exploration;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Endgame;

public sealed record EndgameRuntimeSave([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string StateHash, [property: JsonRequired] EndgameRuntimeSnapshot State);
public sealed record EndgameRuntimeLoadResult(EndgameRuntimeSession Session, bool RecoveredBackup);

public static class EndgameRuntimeSaveStore
{
    public static string ProfilePath(string savePath) => ProductionSaveStore.ProfilePath(savePath);

    internal static void Inspect(JsonElement state, string combatJson, AdventureContent adventure,
        ProgressionContent policy, CampaignContent campaign, EndgameContent endgame)
    {
        ArchiveHeaders.Require(state, 1, "endgame-runtime.1");
        LocalMapAtlas.Inspect(state);
        LocalMapAtlas.Inspect(ArchiveHeaders.Object(state, "campaign"));
        var ledger = ArchiveHeaders.Object(state, "endgame"); ArchiveHeaders.Require(ledger, 1); ArchiveHeaders.Identity(ledger, "contentHash", endgame.Hash);
        string identity = CombatContent.Parse(combatJson).Identity;
        void CombatHeader(JsonElement value)
        {
            ArchiveHeaders.Require(value, 1, "combat.1"); ArchiveHeaders.Identity(value, "contentHash", identity);
            if (value.TryGetProperty("experiment", out var loan) && loan.ValueKind != JsonValueKind.Null)
            {
                ArchiveHeaders.Require(loan, 1, "borrowed-memory.1");
                var rules = ArchiveHeaders.Object(loan, "rules"); ArchiveHeaders.Require(rules, 1); ArchiveHeaders.Identity(rules, "version", "echoes.1");
            }
            if (value.TryGetProperty("endgame", out var runtime) && runtime.ValueKind != JsonValueKind.Null)
            {
                ArchiveHeaders.Require(runtime, 1, "endgame-combat.1");
                var source = ArchiveHeaders.Object(runtime, "manifest"); ArchiveHeaders.Require(source, 1, "endgame-combat.1"); ArchiveHeaders.Identity(source, "contentHash", identity);
            }
        }
        if (state.TryGetProperty("secretChambers", out var secrets) && secrets.ValueKind != JsonValueKind.Null)
        {
            ArchiveHeaders.Require(secrets, 1);
            if (secrets.TryGetProperty("combat", out var secretCombat) && secretCombat.ValueKind != JsonValueKind.Null) CombatHeader(secretCombat);
        }
        if (state.TryGetProperty("roamingChampions", out var champions) && champions.ValueKind != JsonValueKind.Null)
        {
            ArchiveHeaders.Require(champions, 1);
            if (champions.TryGetProperty("combat", out var championCombat) && championCombat.ValueKind != JsonValueKind.Null) CombatHeader(championCombat);
        }
        if (state.TryGetProperty("regionalHunts", out var regional) && regional.ValueKind != JsonValueKind.Null)
        {
            ArchiveHeaders.Require(regional, 1);
            if (regional.TryGetProperty("combat", out var huntCombat) && huntCombat.ValueKind != JsonValueKind.Null) CombatHeader(huntCombat);
        }
        if (state.TryGetProperty("combat", out var current) && current.ValueKind != JsonValueKind.Null)
            CombatHeader(current);
        if (state.TryGetProperty("manifest", out var manifest) && manifest.ValueKind != JsonValueKind.Null)
        { ArchiveHeaders.Require(manifest, 1, "endgame-combat.1"); ArchiveHeaders.Identity(manifest, "contentHash", identity); }
        var journey = ArchiveHeaders.Object(state, "campaign"); ArchiveHeaders.Require(journey, 1, "campaign-runtime.1");
        var narrative = ArchiveHeaders.Object(journey, "campaign"); ArchiveHeaders.Require(narrative, 1); ArchiveHeaders.Identity(narrative, "contentHash", campaign.Hash);
        CombatHeader(ArchiveHeaders.Object(journey, "combat"));
        if (journey.TryGetProperty("clearedRooms", out var rooms) && rooms.ValueKind != JsonValueKind.Null)
        {
            if (rooms.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid cleared room archive.");
            foreach (var room in rooms.EnumerateObject()) CombatHeader(room.Value);
        }
        var permanent = ArchiveHeaders.Object(journey, "production"); ArchiveHeaders.Require(permanent, 1, "production.1");
        var expedition = ArchiveHeaders.Object(permanent, "expedition"); ArchiveHeaders.Require(expedition, 1, "expedition.1");
        ArchiveHeaders.Identity(expedition, "adventureHash", ProductionContent.ResolveAdventure(combatJson, adventure).Hash);
        CombatHeader(ArchiveHeaders.Object(expedition, "combat"));
        var character = ArchiveHeaders.Object(ArchiveHeaders.Object(permanent, "progression"), "character"); ArchiveHeaders.Require(character, 1);
        if (character.TryGetProperty("stash", out var personalStash) && personalStash.ValueKind != JsonValueKind.Null) ArchiveHeaders.Require(personalStash, 1);
        if (character.TryGetProperty("endgame", out var permanentEndgame) && permanentEndgame.ValueKind != JsonValueKind.Null) ArchiveHeaders.Require(permanentEndgame, 1);
        var resolved = ProductionContent.Resolve(combatJson, CampaignRuntimeSession.ResolvePolicy(EndgameProgression.Resolve(policy), campaign), adventure);
        ArchiveHeaders.Identity(character, "contentHash", resolved.Hash);
    }

    public static EndgameRuntimeSession Read(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, string json)
    {
        try { return ReadWithLegendaryUpgrade(combatJson, adventure, policy, campaign, endgame, json); }
        catch (SaveCompatibilityException) when (OpeningCatalogMigration.TryPrevious(combatJson, policy, campaign,
            out var previousCombat, out var previousPolicy, out var previousCampaign))
        {
            var original = Read(previousCombat, adventure, previousPolicy, previousCampaign, endgame, json);
            return EndgameRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame,
                OpeningCatalogMigration.Rebind(original.Capture(), combatJson, adventure, policy, campaign));
        }
    }

    private static EndgameRuntimeSession ReadWithLegendaryUpgrade(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, string json)
    {
        try { return ReadExact(combatJson, adventure, policy, campaign, endgame, json); }
        catch (SaveCompatibilityException) when (LegendaryCatalogMigration.TryPrevious(combatJson, policy, out var previousCombat, out var previousPolicy))
        {
            var original = ReadWithLegendaryUpgrade(previousCombat, adventure, previousPolicy, campaign, endgame, json);
            return EndgameRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame,
                LegendaryCatalogMigration.Rebind(original.Capture(), combatJson, adventure, policy, campaign));
        }
    }

    private static EndgameRuntimeSession ReadExact(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, string json)
    {
        if (json.Length > 96 * 1024 * 1024) throw new InvalidDataException("Endgame archive exceeds its bounded size.");
        using var document = JsonDocument.Parse(json); ArchiveHeaders.Require(document.RootElement, 1);
        Inspect(ArchiveHeaders.Object(document.RootElement, "state"), combatJson, adventure, policy, campaign, endgame);
        ArchiveHeaders.Checksum(document.RootElement, "state");
        var save = JsonData.Read<EndgameRuntimeSave>(json);
        if (save.State is null || save.StateHash != JsonData.Hash(save.State)) throw new InvalidDataException("Endgame checksum mismatch.");
        return EndgameRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, save.State);
    }

    public static void Write(string path, string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, EndgameRuntimeSnapshot snapshot)
    {
        var session = EndgameRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, snapshot);
        string full = Path.GetFullPath(path), profilePath = ProfilePath(full); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var lease = new FileStream(full + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        string? previous = null;
        if (File.Exists(full))
        {
            previous = File.ReadAllText(full);
            try { Read(combatJson, adventure, policy, campaign, endgame, previous); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { previous = null; }
        }
        // Validate compatibility and combine metadata before touching either durable file.
        // Character power commits first; a failed profile publication can safely retry
        // because the character already carries its own monotonic profile discoveries.
        if (File.Exists(profilePath) || File.Exists(profilePath + ".bak"))
            session.Production.MergeProfile(LocalProfileStore.Load(profilePath, session.Production.Content).Profile);
        if (previous is not null) AtomicFile.Write(full + ".bak", previous);
        var state = session.Capture(); AtomicFile.Write(full, JsonData.Write(new EndgameRuntimeSave(1, JsonData.Hash(state), state)));
        // Merge takes the profile's own writer lease and rereads current metadata so
        // discoveries committed concurrently by another character cannot be lost.
        LocalProfileStore.Merge(profilePath, session.Production.Content, session.Production.Capture().Progression.Profile);
    }

    public static EndgameRuntimeLoadResult Load(string path, string combatJson, AdventureContent adventure,
        ProgressionContent policy, CampaignContent campaign, EndgameContent endgame)
    {
        EndgameRuntimeSession session; bool recovered = false;
        try { session = Read(combatJson, adventure, policy, campaign, endgame, File.ReadAllText(path)); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FileNotFoundException)
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException("Endgame save is missing/corrupt and no valid backup exists.", ex);
            session = Read(combatJson, adventure, policy, campaign, endgame, File.ReadAllText(path + ".bak")); recovered = true;
        }
        string profilePath = ProfilePath(path);
        if (File.Exists(profilePath) || File.Exists(profilePath + ".bak")) session.Production.MergeProfile(LocalProfileStore.Load(profilePath, session.Production.Content).Profile);
        session = EndgameRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, session.Capture());
        return new(session, recovered);
    }
}

public static class EndgameRuntimeReplayRunner
{
    public static EndgameRuntimeReplay Read(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, string json)
    {
        if (json.Length > 96 * 1024 * 1024) throw new InvalidDataException("Endgame replay exceeds its bounded size.");
        using var document = JsonDocument.Parse(json); ArchiveHeaders.Require(document.RootElement, 1);
        EndgameRuntimeSaveStore.Inspect(ArchiveHeaders.Object(document.RootElement, "initial"), combatJson, adventure, policy, campaign, endgame);
        return JsonData.Read<EndgameRuntimeReplay>(json);
    }

    public static ReplayResult Run(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, EndgameRuntimeReplay replay)
    {
        if (replay is null || replay.SchemaVersion != 1 || replay.Initial is null || replay.Frames is not { Length: <= 1800 }) throw new InvalidDataException("Invalid endgame replay header or size.");
        var session = EndgameRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, replay.Initial);
        for (int index = 0; index < replay.Frames.Length; index++)
        {
            var frame = replay.Frames[index]; if (frame is null || frame.Command is null) throw new InvalidDataException("Null endgame replay operation.");
            var result = session.Execute(frame.Command, recordReplay: false);
            if (session.StateHash != frame.StateHash) return new(false, index, "Endgame state diverged at operation index.", session.StateHash);
            if (JsonData.Hash(result) != frame.EventHash) return new(false, index, "Endgame events diverged at operation index.", session.StateHash);
        }
        return new(true, null, "Endgame manifests, combat, attempts, crafting, and rewards match.", session.StateHash);
    }
}
