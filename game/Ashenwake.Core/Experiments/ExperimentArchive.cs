using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Experiments;

public sealed record ExperimentSave([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string StateHash, [property: JsonRequired] ExperimentSnapshot State);
public sealed record ExperimentLoadResult(ExperimentRuntimeSession Session, bool RecoveredBackup);
public static class ExperimentSaveStore
{
    public static string ProfilePath(string path) => EndgameRuntimeSaveStore.ProfilePath(path);
    internal static void Inspect(JsonElement state, string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent experiment)
    {
        ArchiveHeaders.Require(state, 1, "experiment-runtime.1"); ArchiveHeaders.Identity(state, "contentHash", experiment.Hash);
        var enclosed = ArchiveHeaders.Object(state, "endgame"); EndgameRuntimeSaveStore.Inspect(enclosed, combatJson, adventure, policy, campaign, endgame);
        if (enclosed.TryGetProperty("combat", out var combat) && combat.ValueKind == JsonValueKind.Object && combat.TryGetProperty("experiment", out var memory) && memory.ValueKind != JsonValueKind.Null)
        {
            ArchiveHeaders.Require(memory, 1, "borrowed-memory.1"); ArchiveHeaders.Identity(memory, "contentHash", experiment.Hash);
            var rules = ArchiveHeaders.Object(memory, "rules"); ArchiveHeaders.Require(rules, 1); ArchiveHeaders.Identity(rules, "version", experiment.Capture().Version);
        }
    }
    public static ExperimentRuntimeSession Read(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent experiment, string json)
    {
        try { return ReadWithLegendaryUpgrade(combatJson, adventure, policy, campaign, endgame, experiment, json); }
        catch (SaveCompatibilityException) when (OpeningCatalogMigration.TryPrevious(combatJson, policy, campaign,
            out var previousCombat, out var previousPolicy, out var previousCampaign))
        {
            var original = Read(previousCombat, adventure, previousPolicy, previousCampaign, endgame, experiment, json).Capture();
            return ExperimentRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, experiment,
                original with { Endgame = OpeningCatalogMigration.Rebind(original.Endgame, combatJson, adventure, policy, campaign) });
        }
    }

    private static ExperimentRuntimeSession ReadWithLegendaryUpgrade(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent experiment, string json)
    {
        try { return ReadExact(combatJson, adventure, policy, campaign, endgame, experiment, json); }
        catch (SaveCompatibilityException) when (LegendaryCatalogMigration.TryPrevious(combatJson, policy, out var previousCombat, out var previousPolicy))
        {
            var original = ReadExact(previousCombat, adventure, previousPolicy, campaign, endgame, experiment, json).Capture();
            return ExperimentRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, experiment,
                original with { Endgame = LegendaryCatalogMigration.Rebind(original.Endgame, combatJson, adventure, policy, campaign) });
        }
    }

    private static ExperimentRuntimeSession ReadExact(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent experiment, string json)
    {
        if (json.Length > 96 * 1024 * 1024) throw new InvalidDataException("Experiment archive exceeds its size bound.");
        using var document = JsonDocument.Parse(json); ArchiveHeaders.Require(document.RootElement, 1);
        Inspect(ArchiveHeaders.Object(document.RootElement, "state"), combatJson, adventure, policy, campaign, endgame, experiment);
        ArchiveHeaders.Checksum(document.RootElement, "state");
        var save = JsonData.Read<ExperimentSave>(json);
        if (save.State is null || save.StateHash != JsonData.Hash(save.State)) throw new InvalidDataException("Experiment checksum mismatch.");
        return ExperimentRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, experiment, save.State);
    }
    public static ExperimentRuntimeSession ImportEndgame(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent experiment, string json)
        => ExperimentRuntimeSession.FromEndgame(combatJson, adventure, policy, campaign, endgame, experiment, EndgameRuntimeSaveStore.Read(combatJson, adventure, policy, campaign, endgame, json).Capture());
    public static void Write(string path, string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent experiment, ExperimentSnapshot snapshot)
    {
        var session = ExperimentRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, experiment, snapshot);
        string full = Path.GetFullPath(path), profilePath = ProfilePath(full); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var lease = new FileStream(full + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        string? previous = null;
        if (File.Exists(full))
        {
            previous = File.ReadAllText(full);
            try { Read(combatJson, adventure, policy, campaign, endgame, experiment, previous); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { previous = null; }
        }
        if (File.Exists(profilePath) || File.Exists(profilePath + ".bak")) session.Production.MergeProfile(LocalProfileStore.Load(profilePath, session.Production.Content).Profile);
        if (previous is not null) AtomicFile.Write(full + ".bak", previous);
        var state = session.Capture(); AtomicFile.Write(full, JsonData.Write(new ExperimentSave(1, JsonData.Hash(state), state)));
        LocalProfileStore.Merge(profilePath, session.Production.Content, session.Production.Capture().Progression.Profile);
    }
    public static ExperimentLoadResult Load(string path, string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent experiment)
    {
        ExperimentRuntimeSession session; bool recovered = false;
        try { session = Read(combatJson, adventure, policy, campaign, endgame, experiment, File.ReadAllText(path)); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FileNotFoundException)
        {
            if (!File.Exists(path + ".bak")) throw new InvalidDataException("Experiment save is missing/corrupt and has no valid backup.", ex);
            session = Read(combatJson, adventure, policy, campaign, endgame, experiment, File.ReadAllText(path + ".bak")); recovered = true;
        }
        string profile = ProfilePath(path);
        if (File.Exists(profile) || File.Exists(profile + ".bak")) session.Production.MergeProfile(LocalProfileStore.Load(profile, session.Production.Content).Profile);
        session = ExperimentRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, experiment, session.Capture());
        return new(session, recovered);
    }
}
public static class ExperimentReplayRunner
{
    public static ExperimentReplay Read(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent experiment, string json)
    {
        if (json.Length > 96 * 1024 * 1024) throw new InvalidDataException("Experiment replay exceeds its size bound.");
        using var document = JsonDocument.Parse(json); ArchiveHeaders.Require(document.RootElement, 1);
        ExperimentSaveStore.Inspect(ArchiveHeaders.Object(document.RootElement, "initial"), combatJson, adventure, policy, campaign, endgame, experiment);
        return JsonData.Read<ExperimentReplay>(json);
    }
    public static ReplayResult Run(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent experiment, ExperimentReplay replay)
    {
        if (replay is null || replay.SchemaVersion != 1 || replay.Initial is null || replay.Frames is not { Length: <= 1800 } || replay.Admission is not ("Available" or "Retired")) throw new InvalidDataException("Invalid experiment replay.");
        var session = ExperimentRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, experiment.WithAdmission(replay.Admission), replay.Initial);
        for (int i = 0; i < replay.Frames.Length; i++)
        {
            var frame = replay.Frames[i]; if (frame?.Command is null) throw new InvalidDataException("Missing experiment replay command.");
            var result = session.Execute(frame.Command, recordReplay: false);
            if (session.StateHash != frame.StateHash) return new(false, i, "Experiment state diverged.", session.StateHash);
            if (JsonData.Hash(result) != frame.EventHash) return new(false, i, "Experiment outcome diverged.", session.StateHash);
        }
        return new(true, null, "Experiment choice, memory, ordinary combat, and cosmetic receipt match.", session.StateHash);
    }
}
