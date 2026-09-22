using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Serialization;

/// <summary>Rebalances authenticated campaign reward receipts without replaying rewards.
/// Archive readers restore the published predecessor before this projection is called.</summary>
internal static class CampaignPacingMigration
{
    internal const string Version = "campaign.pacing.7";
    private static readonly Lazy<CampaignContent> PreviousCampaign = new(() =>
    {
        using var stream = typeof(CampaignPacingMigration).Assembly.GetManifestResourceStream("Ashenwake.PreviousPacing.Campaign.json")
            ?? throw new InvalidOperationException("Missing authenticated pacing migration catalog.");
        using var reader = new StreamReader(stream);
        return CampaignContent.Parse(reader.ReadToEnd());
    });

    private static readonly Lazy<CampaignContent> PublishedPacingCampaign = new(() =>
    {
        using var stream = typeof(CampaignPacingMigration).Assembly.GetManifestResourceStream("Ashenwake.PreviousOpeningDepth.Campaign.json")
            ?? throw new InvalidOperationException("Missing authenticated published pacing catalog.");
        using var reader = new StreamReader(stream);
        return CampaignContent.Parse(reader.ReadToEnd());
    });

    private static bool Required(CampaignRuntimeSnapshot original, CampaignContent campaign)
        => campaign.Capture().Version is Version or "campaign.opening_depth.8" && original.Campaign.ContentHash != campaign.Hash &&
            // An explicit multi-build import can skip the intermediate pacing reader.
            // Earlier rewards still need their one-time top-up, while an authenticated
            // pacing archive already owns it even when the next story identity changes.
            original.Campaign.ContentHash != PublishedPacingCampaign.Value.Hash;

    internal static CampaignRuntimeSnapshot Rebind(CampaignRuntimeSnapshot original, CampaignRuntimeSnapshot rebound,
        string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign)
    {
        if (!Required(original, campaign)) return rebound;
        var previous = PreviousCampaign.Value.Capture().Acts.SelectMany(act => act.Encounters).ToDictionary(encounter => encounter.Id);
        var current = campaign.Capture().Acts.SelectMany(act => act.Encounters).ToDictionary(encounter => encounter.Id);
        var state = JsonData.Copy(rebound);
        var character = state.Production.Progression.Character;
        long difference = 0;
        foreach (string id in state.Campaign.CompletedEncounters)
        {
            if (!previous.TryGetValue(id, out var old) || !current.TryGetValue(id, out var next) || old.Materials != next.Materials)
                throw new InvalidDataException("Pacing migration cannot substitute an unknown campaign reward.");
            string receipt = "campaign.encounter." + id;
            if (!character.OperationReceipts.TryGetValue(receipt, out string? hash) ||
                hash != JsonData.Hash(new { Action = "Experience", amount = old.Experience, materials = old.Materials }))
                throw new InvalidDataException("Pacing migration requires the original exact reward receipt.");
            character.OperationReceipts[receipt] = JsonData.Hash(new { Action = "Experience", amount = next.Experience, materials = next.Materials });
            difference += next.Experience - old.Experience;
        }
        // The published schedule has a nonnegative delta at every completed prefix.
        // Extra XP from imports or expeditions belongs to the player and is never removed.
        if (difference < 0) throw new InvalidDataException("Pacing migration must not reduce earned experience.");
        var resolvedPolicy = CampaignRuntimeSession.ResolvePolicy(policy, campaign);
        var resolved = ProductionContent.Resolve(combatJson, resolvedPolicy, adventure);
        var rules = resolved.Capture();
        long maximumExperience = (long)rules.ExperiencePerLevel * rules.LevelCap * (rules.LevelCap - 1) / 2;
        character.Experience = Math.Min(maximumExperience, checked(character.Experience + difference));
        state.Campaign.EarnedExperience = current.Values.Where(encounter => state.Campaign.CompletedEncounters.Contains(encounter.Id)).Sum(encounter => encounter.Experience);
        var production = ProductionSession.RestorePacingProjection(combatJson, adventure, resolvedPolicy, state.Production);
        var build = production.Combat.ProgressionBuild;
        return state with
        {
            Production = production.Capture(),
            Combat = Reproject(state.Combat, combatJson, build),
            ClearedRooms = state.ClearedRooms is null ? null : new SortedDictionary<string, CombatSnapshot>(state.ClearedRooms.ToDictionary(
                pair => pair.Key,
                // A cached room may retain an earlier discipline/loadout. Preserve it
                // until ordinary re-entry projects current ownership, updating only XP-derived fields.
                pair => Reproject(pair.Value, combatJson, pair.Value.ProgressionBuild with
                { Level = build.Level, UltimateUnlocked = build.UltimateUnlocked })), StringComparer.Ordinal)
        };
    }

    internal static EndgameRuntimeSnapshot RebindEndgameArena(EndgameRuntimeSnapshot original, EndgameRuntimeSnapshot rebound,
        string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign)
    {
        if (!Required(original.Campaign, campaign) || rebound.Combat is null) return rebound;
        return rebound with { Combat = Reproject(rebound.Combat, combatJson, rebound.Campaign.Production.Expedition.Combat.ProgressionBuild) };
    }

    private static CombatSnapshot Reproject(CombatSnapshot snapshot, string combatJson, CombatProgressionBuild build)
    {
        var session = CombatSession.Restore(combatJson, snapshot);
        session.ApplyProgressionBuild(build);
        return session.Capture();
    }
}
