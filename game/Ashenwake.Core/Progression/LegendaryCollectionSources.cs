using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;

namespace Ashenwake.Core.Progression;

/// <summary>Authored source identities. A negative Fracture encounter index means the final room.</summary>
public sealed record LegendaryCollectionEntry(string ItemId, string PowerId, EquipmentSlot Slot,
    string CampaignEncounterId, string FractureRegionId, int FractureEncounterIndex, string HuntId = "", string SecretChamberId = "", string RoamingChampionId = "");

public static class LegendaryCollectionCatalog
{
    public static IReadOnlyList<LegendaryCollectionEntry> Entries { get; } = Array.AsReadOnly(new LegendaryCollectionEntry[]
    {
        new(EquipmentSets.VigilHead, "", EquipmentSlot.Head, "campaign.monastery", "act.grey_march", 0),
        new(EquipmentSets.VigilChest, "", EquipmentSlot.Chest, "campaign.bell_saint", "act.grey_march", 1),
        new(EquipmentSets.BriarShoulders, "", EquipmentSlot.Shoulders, "campaign.living_ruins", "act.verdant_maw", 0),
        new(EquipmentSets.BriarGloves, "", EquipmentSlot.Gloves, "campaign.rootheart", "act.verdant_maw", 1),
        new(EquipmentSets.AshBelt, "", EquipmentSlot.Belt, "campaign.cinder_pack", "act.cinder_reach", 0),
        new(EquipmentSets.AshBoots, "", EquipmentSlot.Boots, "campaign.furnace_spindle", "act.cinder_reach", 1),
        new(LegendaryEquipment.Pyre, LegendaryEquipment.PyrePower, EquipmentSlot.Boots, "campaign.road", "act.grey_march", -1),
        new(LegendaryEquipment.Widow, LegendaryEquipment.WidowPower, EquipmentSlot.Gloves, "campaign.rootheart", "act.verdant_maw", -1),
        new(LegendaryEquipment.Rotwake, LegendaryEquipment.RotwakePower, EquipmentSlot.Ring1, "campaign.plague_village", "act.verdant_maw", 0),
        new(LegendaryEquipment.Mourning, LegendaryEquipment.MourningPower, EquipmentSlot.Shoulders, "campaign.extraction_floor", "act.verdant_maw", 1),
        new(LegendaryEquipment.Furnace, LegendaryEquipment.FurnacePower, EquipmentSlot.Belt, "campaign.furnace_spindle", "act.cinder_reach", -1),
        new(LegendaryEquipment.Oath, LegendaryEquipment.OathPower, EquipmentSlot.Chest, "campaign.covenant_warden", "act.shattered_spine", -1),
        new(LegendaryEquipment.Crown, LegendaryEquipment.CrownPower, EquipmentSlot.Head, "campaign.contract_hall", "act.shattered_spine", 1, "hunt.orrun_without_oath"),
        new(LegendaryEquipment.Witness, LegendaryEquipment.WitnessPower, EquipmentSlot.Amulet, "campaign.identity_memory", "act.hollow_night", 1, "hunt.thousand_memories"),
        new(LegendaryEquipment.Hour, LegendaryEquipment.HourPower, EquipmentSlot.Legs, "campaign.breach_heart", "act.hollow_night", -1, "hunt.nhal_reconstruction"),
        new(LegendaryEquipment.Grief, LegendaryEquipment.GriefPower, EquipmentSlot.OffHand, "", "", 0, SecretChamberId: "secret.belfry"),
        new(LegendaryEquipment.Widowthorn, LegendaryEquipment.WidowthornPower, EquipmentSlot.MainHand, "", "", 0, SecretChamberId: "secret.nest"),
        new(LegendaryEquipment.Emberwake, LegendaryEquipment.EmberwakePower, EquipmentSlot.Shoulders, "", "", 0, SecretChamberId: "secret.furnace"),
        new("item.last_toll", LegendaryEquipment.CrownPower, EquipmentSlot.OffHand, "", "", 0, RoamingChampionId: "champion.pilgrim"),
        new("item.broodkeepers_knot", LegendaryEquipment.RotwakePower, EquipmentSlot.Amulet, "", "", 0, RoamingChampionId: "champion.rootwidow"),
        new("item.tithebreakers_grasp", LegendaryEquipment.WidowPower, EquipmentSlot.Gloves, "", "", 0, RoamingChampionId: "champion.tithekeeper")
    });
}

public enum LegendaryCollectionSourceKind { Campaign, Fracture, GodHunt, SecretChamber, RoamingChampion }
public enum LegendaryCollectionSourceState { Locked, Available, Active, Completed }

/// <summary>A presentation-only route. IDs for unreached encounters and secret hunts are deliberately empty.</summary>
public sealed record LegendaryCollectionSourceView(LegendaryCollectionSourceKind Kind,
    LegendaryCollectionSourceState State, string Label, string Requirement, int Act = 0,
    string EncounterId = "", string RegionId = "", long SigilId = 0, string HuntId = "", bool Repeatable = false, string ChamberId = "", string ChampionId = "");

/// <summary>Read-only source guidance; all travel and launch commands still use their ordinary authoritative gates.</summary>
public static class LegendaryCollectionSources
{
    public static LegendaryCollectionSourceView[] For(EndgameRuntimeSession session, string itemId) => Project(
        session.Campaign.Content.Capture(), session.Campaign.Capture().Campaign, session.Campaign.View,
        session.Campaign.ActiveEncounterId, session.Content.Capture(), session.View, itemId, session.CurrentRunContentId, session.SecretChambers, session.RoamingChampions);

    public static LegendaryCollectionSourceView[] Project(CampaignDefinition content, CampaignState state,
        CampaignView campaign, string activeEncounterId, EndgameDefinition endgameContent,
        EndgameRuntimeView endgame, string itemId, string activeRunContentId = "", SecretChambersView? secrets = null, RoamingChampionsView? champions = null)
    {
        var entry = LegendaryCollectionCatalog.Entries.FirstOrDefault(e => e.ItemId == itemId);
        if (entry is null) return [];
        if (entry.RoamingChampionId.Length > 0)
        {
            var champion = champions?.Entries.FirstOrDefault(e => e.Id == entry.RoamingChampionId && e.Discovered);
            if (champion is null) return [new(LegendaryCollectionSourceKind.RoamingChampion, LegendaryCollectionSourceState.Locked,
                "Undiscovered roaming champion", "Explore secured regional side areas to discover this optional champion and its one-time treasure.")];
            bool inside = champions!.Run?.Id == champion.Id;
            return [new(LegendaryCollectionSourceKind.RoamingChampion,
                champion.Claimed ? LegendaryCollectionSourceState.Completed : inside ? LegendaryCollectionSourceState.Active : LegendaryCollectionSourceState.Available,
                champion.Name, champion.Claimed ? "Signature treasure already claimed by this character. This champion does not grant another copy."
                    : champion.Defeated ? "The champion is defeated. Return to its sighting and collect the waiting treasure."
                    : inside ? "Challenge the champion, then collect its signature treasure. You may retreat and try again."
                    : "Return to the recorded sighting in its secured campaign room and inspect the optional challenge.",
                champion.Act, champion.SourceEncounterId, ChampionId: champion.Id)];
        }
        if (entry.SecretChamberId.Length > 0)
        {
            var chamber = secrets?.Entries.FirstOrDefault(e => e.Id == entry.SecretChamberId && e.Revealed);
            if (chamber is null) return [new(LegendaryCollectionSourceKind.SecretChamber, LegendaryCollectionSourceState.Locked,
                "Undiscovered hidden chamber", "Inspect unusual details in the world to uncover this one-time treasure.")];
            var definition = SecretChamberCatalog.Find(chamber.Id)!;
            bool inside = secrets!.Run?.Id == chamber.Id;
            return [new(LegendaryCollectionSourceKind.SecretChamber,
                chamber.Claimed ? LegendaryCollectionSourceState.Completed : inside ? LegendaryCollectionSourceState.Active : LegendaryCollectionSourceState.Available,
                chamber.Name, chamber.Claimed ? "Treasure already claimed by this character. This chamber does not grant another copy."
                    : chamber.Defeated ? "The guardian is defeated. Return through the revealed passage and claim its waiting treasure."
                    : inside ? "Challenge the guardian, then collect the treasure. You can leave safely and return later."
                    : "Return to the discovered doorway, enter its safe foyer, and challenge the optional guardian when ready.",
                chamber.Act, definition.SourceEncounterId, ChamberId: chamber.Id)];
        }
        var result = new List<LegendaryCollectionSourceView>();
        var act = content.Acts.Single(a => a.Encounters.Any(e => e.Id == entry.CampaignEncounterId));
        bool completed = state.CompletedEncounters.Contains(entry.CampaignEncounterId);
        bool active = !state.InHub && activeEncounterId == entry.CampaignEncounterId && endgame.Run?.Status != "Active";
        bool reached = completed || active;
        string campaignLabel = reached ? $"Act {act.Number} · {Readable(entry.CampaignEncounterId)}" : $"Act {act.Number} · Undiscovered campaign encounter";
        string campaignRequirement = completed ? "Already cleared. Revisiting does not grant another copy; collect any remaining ground loot or use a repeatable source."
            : active ? "Defeat this encounter and collect its legendary ground drop."
            : !campaign.AvailableActs.Contains(act.Number) ? $"Complete Act {act.Number - 1} to open this region, then follow its campaign route."
            : $"Continue the campaign route in Act {act.Number} to discover this encounter.";
        result.Add(new(LegendaryCollectionSourceKind.Campaign,
            completed ? LegendaryCollectionSourceState.Completed : active ? LegendaryCollectionSourceState.Active : LegendaryCollectionSourceState.Locked,
            campaignLabel, campaignRequirement, act.Number, reached ? entry.CampaignEncounterId : "", reached || state.HighestActVisited >= act.Number ? act.Id : ""));

        var region = content.Acts.Single(a => a.Id == entry.FractureRegionId);
        string regionName = state.HighestActVisited >= region.Number ? region.Name : $"Act {region.Number}";
        string room = entry.FractureEncounterIndex < 0 ? "final room" : $"room {entry.FractureEncounterIndex + 1}";
        var sigil = endgame.AvailableSigils.Where(s => !s.Consumed && s.Region == entry.FractureRegionId).OrderBy(s => s.Tier).ThenBy(s => s.Id).FirstOrDefault();
        bool running = endgame.Run?.Status == "Active";
        bool matchingRun = running && endgame.Run!.Kind == "Fracture" && endgame.Run.Region == entry.FractureRegionId;
        int sourceIndex = entry.FractureEncounterIndex < 0 ? (endgame.Run?.EncounterCount ?? 1) - 1 : entry.FractureEncounterIndex;
        bool passed = matchingRun && (endgame.Run!.EncounterIndex > sourceIndex || endgame.Run.EncounterIndex == sourceIndex && endgame.Run.EncounterCleared);
        var fractureState = !endgame.Unlocked ? LegendaryCollectionSourceState.Locked
            : passed ? LegendaryCollectionSourceState.Completed
            : matchingRun ? LegendaryCollectionSourceState.Active
            : !running && sigil is not null ? LegendaryCollectionSourceState.Available : LegendaryCollectionSourceState.Locked;
        string fractureRequirement = !endgame.Unlocked ? "Complete this character's campaign to unlock Fractures."
            : passed ? "This run's source room is cleared. Collect any remaining drop; another copy requires a new matching sigil and run."
            : matchingRun ? $"Continue this Fracture to its {room}; defeat the encounter and collect the legendary drop."
            : running ? "Finish or abandon the current expedition before starting another source run."
            : sigil is null ? $"Obtain an unconsumed {regionName} sigil. Sigils from other regions cannot target this source."
            : $"{(endgame.InHub ? "Visit" : "Return to")} Greyhaven's Fracture gate and select sigil #{sigil.Id} (tier {sigil.Tier}). Collect the drop in the {room}.";
        result.Add(new(LegendaryCollectionSourceKind.Fracture, fractureState, $"{regionName} Fracture · {room}", fractureRequirement,
            region.Number, RegionId: state.HighestActVisited >= region.Number ? region.Id : "", SigilId: endgame.Unlocked && !running ? sigil?.Id ?? 0 : 0, Repeatable: true));

        if (entry.HuntId.Length > 0)
        {
            var hunt = endgameContent.Hunts.Single(h => h.Id == entry.HuntId);
            bool unlocked = endgame.UnlockedHunts.Contains(hunt.Id);
            bool known = endgame.Unlocked && (!hunt.Secret || unlocked);
            bool current = running && endgame.Run!.Kind == "GodHunt" && activeRunContentId == hunt.Id;
            string requirement = !endgame.Unlocked ? "Complete this character's campaign to unlock God Hunts."
                : current ? "Finish this God Hunt's final phase and collect the legendary drop."
                : !unlocked ? $"Clear a tier {hunt.RequiredTier} Fracture" + (hunt.Secret ? " and complete every primary God Hunt." : ".")
                : running ? "Finish or abandon the current expedition before starting this God Hunt."
                : $"{(endgame.InHub ? "Visit" : "Return to")} Greyhaven's Fracture gate and select this God Hunt. Its final phase drops this item.";
            result.Add(new(LegendaryCollectionSourceKind.GodHunt,
                current ? LegendaryCollectionSourceState.Active : unlocked && !running ? LegendaryCollectionSourceState.Available : LegendaryCollectionSourceState.Locked,
                known ? hunt.Name + " · final phase" : "Undiscovered God Hunt · final phase", requirement,
                HuntId: known ? hunt.Id : "", Repeatable: true));
        }
        return result.ToArray();
    }

    private static string Readable(string id) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id[(id.IndexOf('.') + 1)..].Replace('_', ' '));
}
