using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class LegendaryCollectionSourceTests
{
    private static readonly CampaignContent Campaign = CampaignContent.Default();
    private static readonly EndgameContent Endgame = EndgameContent.Default();
    private static EndgameRuntimeView Locked => new(false, true, 0, [], [], 0, new Dictionary<string, int>(), false, null, 0, 0);
    private static LegendaryCollectionSourceView[] Sources(string item, CampaignState? state = null, EndgameRuntimeView? endgame = null, string active = "hub", string runId = "")
    {
        var story = CampaignSession.Create(Campaign);
        var ledger = state ?? story.Capture();
        var view = story.View with { AvailableActs = Enumerable.Range(1, Math.Min(5, ledger.CompletedActs.Count + 1)).ToArray() };
        return LegendaryCollectionSources.Project(Campaign.Capture(), ledger, view, active, Endgame.Capture(), endgame ?? Locked, item, runId);
    }
    private static CampaignState Finished => new() { HighestActVisited = 5, CompletedActs = [1, 2, 3, 4, 5], CompletedEncounters = new(Campaign.Capture().Acts.SelectMany(a => a.Encounters).Select(e => e.Id)) };
    private static EndgameRunView Run(string kind, string region, int index, bool cleared = false, int rooms = 4) =>
        new(1, kind, "Cosmetic name", region, 1, index, rooms, 3, 0, 100, "Active", cleared, false, [], [], [], 0, false, false, true);

    [Fact]
    public void DefinitionsMatchAuthoredItemsAndTheirExclusiveSources()
    {
        var content = ProgressionContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "progression.json"))).Capture();
        Assert.Equal(12, LegendaryCollectionCatalog.Entries.Count);
        Assert.Equal(12, LegendaryCollectionCatalog.Entries.Select(e => e.ItemId).Distinct().Count());
        foreach (var entry in LegendaryCollectionCatalog.Entries)
        {
            var item = content.Items.Single(i => i.Id == entry.ItemId);
            Assert.Equal(item.Property, entry.PowerId); Assert.Contains(entry.Slot, item.Slots);
            if (entry.SecretChamberId.Length > 0)
            {
                Assert.Equal(entry.ItemId, SecretChamberCatalog.Find(entry.SecretChamberId)!.RewardItemId);
                Assert.Empty(entry.CampaignEncounterId); Assert.Empty(entry.FractureRegionId); Assert.Empty(entry.HuntId);
                continue;
            }
            Assert.Equal(entry.ItemId, LegendaryEquipment.EncounterReward(entry.CampaignEncounterId));
            Assert.Contains(entry.FractureRegionId, Endgame.Capture().Regions);
            Assert.InRange(entry.FractureEncounterIndex, -1, 1);
        }
        Assert.Equal("hunt.orrun_without_oath", LegendaryCollectionCatalog.Entries.Single(e => e.ItemId == LegendaryEquipment.Crown).HuntId);
        Assert.Equal("hunt.thousand_memories", LegendaryCollectionCatalog.Entries.Single(e => e.ItemId == LegendaryEquipment.Witness).HuntId);
        Assert.Equal("hunt.nhal_reconstruction", LegendaryCollectionCatalog.Entries.Single(e => e.ItemId == LegendaryEquipment.Hour).HuntId);
        Assert.Empty(Sources("not-an-item"));
    }

    [Theory]
    [InlineData(LegendaryEquipment.Pyre, "act.grey_march", -1)]
    [InlineData(LegendaryEquipment.Widow, "act.verdant_maw", -1)]
    [InlineData(LegendaryEquipment.Rotwake, "act.verdant_maw", 0)]
    [InlineData(LegendaryEquipment.Mourning, "act.verdant_maw", 1)]
    [InlineData(LegendaryEquipment.Furnace, "act.cinder_reach", -1)]
    [InlineData(LegendaryEquipment.Oath, "act.shattered_spine", -1)]
    [InlineData(LegendaryEquipment.Crown, "act.shattered_spine", 1)]
    [InlineData(LegendaryEquipment.Witness, "act.hollow_night", 1)]
    [InlineData(LegendaryEquipment.Hour, "act.hollow_night", -1)]
    public void EveryRepeatableRegionAndRoomMatchesTheCombatRewardContract(string item, string region, int room)
    {
        var entry = LegendaryCollectionCatalog.Entries.Single(e => e.ItemId == item);
        Assert.Equal(region, entry.FractureRegionId); Assert.Equal(room, entry.FractureEncounterIndex);
    }

    [Fact]
    public void LiveSessionProjectionDoesNotMutateGameplayOrReplay()
    {
        static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
        var endgame = EndgameContent.Parse(Read("endgame.json"));
        string combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson,
            Read("endgame-combat.json"), endgame).CombatJson;
        var session = EndgameRuntimeSession.Create(combat, AdventureContent.Parse(Read("adventure.json")),
            ProgressionContent.Parse(Read("progression.json")), CampaignContent.Parse(Read("campaign.json")), endgame);
        string before = session.StateHash; int frames = session.CaptureReplay().Frames.Length;
        foreach (var entry in LegendaryCollectionCatalog.Entries) Assert.NotEmpty(LegendaryCollectionSources.For(session, entry.ItemId));
        Assert.Equal(before, session.StateHash); Assert.Equal(frames, session.CaptureReplay().Frames.Length);
    }

    [Fact]
    public void NewCharacterSeesRequirementsWithoutFutureEncounterOrHuntIdentities()
    {
        foreach (var entry in LegendaryCollectionCatalog.Entries)
        {
            var sources = Sources(entry.ItemId);
            Assert.All(sources, s => Assert.Equal(LegendaryCollectionSourceState.Locked, s.State));
            Assert.All(sources, s => Assert.Empty(s.EncounterId));
            Assert.All(sources, s => Assert.Empty(s.HuntId));
            Assert.All(sources, s => Assert.DoesNotContain("Nhal", s.Label));
            if (entry.SecretChamberId.Length == 0) Assert.Contains(sources, s => s.Kind == LegendaryCollectionSourceKind.Fracture && s.Requirement.Contains("Complete this character's campaign"));
        }
        var crown = Sources(LegendaryEquipment.Crown).First();
        Assert.Contains("Complete Act 3", crown.Requirement); Assert.Empty(crown.RegionId);
    }

    [Fact]
    public void ReachedCampaignSourceIsNamedAndClearedSourceNeverPromisesAnotherDrop()
    {
        var state = CampaignSession.Create(Campaign).Capture(); state.InHub = false;
        var current = Sources(LegendaryEquipment.Pyre, state, active: "campaign.road").First();
        Assert.Equal(LegendaryCollectionSourceState.Active, current.State); Assert.Equal("campaign.road", current.EncounterId);
        state.CompletedEncounters.Add("campaign.road"); state.InHub = true;
        var cleared = Sources(LegendaryEquipment.Pyre, state).First();
        Assert.Equal(LegendaryCollectionSourceState.Completed, cleared.State); Assert.False(cleared.Repeatable);
        Assert.Contains("does not grant another copy", cleared.Requirement);
    }

    [Fact]
    public void FractureAvailabilityRequiresMatchingUnconsumedSigilAndNoOtherActiveRun()
    {
        var unlocked = Locked with { Unlocked = true, AvailableSigils = [new(1, 42, "act.verdant_maw", 1, [], "Ilyra", "Materials"), new(2, 43, "act.shattered_spine", 1, [], "Orrun", "Materials", true)] };
        LegendaryCollectionSourceView Fracture(EndgameRuntimeView v) => Sources(LegendaryEquipment.Crown, Finished, v).Single(s => s.Kind == LegendaryCollectionSourceKind.Fracture);
        Assert.Equal(LegendaryCollectionSourceState.Locked, Fracture(unlocked).State);
        unlocked = unlocked with { AvailableSigils = [.. unlocked.AvailableSigils, new(3, 44, "act.shattered_spine", 2, [], "Orrun", "Materials")] };
        Assert.Equal(LegendaryCollectionSourceState.Available, Fracture(unlocked).State); Assert.Equal(3, Fracture(unlocked).SigilId);
        unlocked = unlocked with { Run = Run("Fracture", "act.verdant_maw", 0) };
        Assert.Equal(LegendaryCollectionSourceState.Locked, Fracture(unlocked).State); Assert.Equal(0, Fracture(unlocked).SigilId);
        unlocked = unlocked with { Run = Run("Fracture", "act.shattered_spine", 0) };
        Assert.Equal(LegendaryCollectionSourceState.Active, Fracture(unlocked).State);
        unlocked = unlocked with { Run = Run("Fracture", "act.shattered_spine", 1, true) };
        Assert.Equal(LegendaryCollectionSourceState.Completed, Fracture(unlocked).State);
        Assert.True(Fracture(unlocked).Repeatable);
    }

    [Fact]
    public void FinalRoomResolutionUsesRunEncounterCount()
    {
        var view = Locked with { Unlocked = true, Run = Run("Fracture", "act.hollow_night", 3, true, 5) };
        Assert.Equal(LegendaryCollectionSourceState.Active, Sources(LegendaryEquipment.Hour, Finished, view).Single(s => s.Kind == LegendaryCollectionSourceKind.Fracture).State);
        view = view with { Run = Run("Fracture", "act.hollow_night", 4, true, 5) };
        Assert.Equal(LegendaryCollectionSourceState.Completed, Sources(LegendaryEquipment.Hour, Finished, view).Single(s => s.Kind == LegendaryCollectionSourceKind.Fracture).State);
    }

    [Fact]
    public void HuntAvailabilityUsesAuthoritativeUnlockedListAndSecretIdentityStaysHidden()
    {
        const string secret = "hunt.nhal_reconstruction";
        var view = Locked with { Unlocked = true, HighestClearedTier = 10 };
        LegendaryCollectionSourceView Hunt(EndgameRuntimeView v, string activeId = "") => Sources(LegendaryEquipment.Hour, Finished, v, runId: activeId).Single(s => s.Kind == LegendaryCollectionSourceKind.GodHunt);
        Assert.Equal(LegendaryCollectionSourceState.Locked, Hunt(view).State); Assert.Empty(Hunt(view).HuntId);
        Assert.Contains("every primary God Hunt", Hunt(view).Requirement); Assert.DoesNotContain("Nhal", Hunt(view).Label);
        view = view with { UnlockedHunts = [secret] };
        Assert.Equal(LegendaryCollectionSourceState.Available, Hunt(view).State); Assert.Equal(secret, Hunt(view).HuntId);
        view = view with { Run = Run("GodHunt", "", 0, rooms: 3) };
        Assert.Equal(LegendaryCollectionSourceState.Active, Hunt(view, secret).State);
        Assert.Equal(LegendaryCollectionSourceState.Locked, Hunt(view, "hunt.false_vael").State);
    }
    [Theory]
    [InlineData("secret.belfry")]
    [InlineData("secret.nest")]
    [InlineData("secret.furnace")]
    public void SecretSourcesHideIdentityUntilDoorRevealedAndNeverPromiseRepeatRewards(string id)
    {
        var definition = SecretChamberCatalog.Find(id)!;
        var story = CampaignSession.Create(Campaign);
        LegendaryCollectionSourceView Project(bool revealed, bool claimed)
        {
            var secrets = new SecretChambersView([new(id, revealed ? definition.Name : "An unmarked passage", definition.Act,
                true, revealed, claimed, revealed ? 3 : 1, false, null)], null);
            return Assert.Single(LegendaryCollectionSources.Project(Campaign.Capture(), story.Capture(), story.View, "hub",
                Endgame.Capture(), Locked, definition.RewardItemId, secrets: secrets));
        }
        var hidden = Project(false, false);
        Assert.Equal(LegendaryCollectionSourceState.Locked, hidden.State);
        Assert.Empty(hidden.ChamberId); Assert.Empty(hidden.EncounterId); Assert.Empty(hidden.RegionId);
        Assert.DoesNotContain(definition.Name, hidden.Label); Assert.DoesNotContain(definition.GuardianName, hidden.Requirement);
        var found = Project(true, false);
        Assert.Equal(id, found.ChamberId); Assert.Equal(definition.SourceEncounterId, found.EncounterId);
        Assert.Equal(LegendaryCollectionSourceState.Available, found.State); Assert.False(found.Repeatable);
        var claimed = Project(true, true);
        Assert.Equal(LegendaryCollectionSourceState.Completed, claimed.State); Assert.False(claimed.Repeatable);
        Assert.Contains("does not grant another copy", claimed.Requirement);
    }

}
