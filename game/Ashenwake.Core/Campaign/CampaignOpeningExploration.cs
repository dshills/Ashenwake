using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Campaign;

public sealed partial class CampaignRuntimeSession
{
    public const string CryptEvent = "event.widow_crypt", CryptEncounter = "exploration.widow_crypt";
    private static readonly string[] OpeningRooms = ["campaign.road", "campaign.monastery", "campaign.bell_saint", CryptEncounter];
    private SortedDictionary<string, CombatSnapshot> clearedRooms = new(StringComparer.Ordinal);
    private bool HasOpeningExploration => Content.Data.Exploration.Any(e => e.Id == CryptEvent);

    private IReadOnlyList<ExpeditionInteraction>? OpeningInteractions()
    {
        if (!HasOpeningExploration || story.CurrentState.CurrentAct != 1 || !OpeningRooms.Contains(ActiveEncounterId)) return null;
        var targets = new List<ExpeditionInteraction>();
        var state = story.CurrentState;
        if (ActiveEncounterId == CryptEncounter)
        {
            targets.Add(new("opening.crypt.return", "Return to the Grey March road", OpeningCampaignLayout.CryptReturn, 1800));
            if (EncounterCleared && !state.CompletedExploration.Contains(CryptEvent))
                targets.Add(new("opening.crypt.treasure", "Open the Widow's Testament · rare armor and 25 materials", OpeningCampaignLayout.CryptTreasure, 1800));
        }
        else if (EncounterCleared && state.Exploration is null)
        {
            if (ActiveEncounterId == "campaign.road")
                targets.Add(new("opening.crypt.enter", state.CompletedExploration.Contains(CryptEvent) ? "The Widow's Crypt · treasure claimed" : "Explore the Widow's Crypt", OpeningCampaignLayout.CryptEntrance, 1800));
            string? prior = ActiveEncounterId switch { "campaign.monastery" => "road", "campaign.bell_saint" => "monastery", _ => null };
            if (prior is not null) targets.Add(new("opening.back." + prior, "Return to " + (prior == "road" ? "the Grey March road" : "the monastery courtyard"), OpeningCampaignLayout.BackExit, 1800));
            string? next = ActiveEncounterId switch { "campaign.road" => "monastery", "campaign.monastery" => "bell_saint", _ => null };
            if (next is not null && state.CompletedEncounters.Contains("campaign." + next))
                targets.Add(new("opening.forward." + (next == "bell_saint" ? "sanctum" : next), "Revisit " + (next == "monastery" ? "the monastery courtyard" : "the bell sanctuary"), OpeningCampaignLayout.ForwardExit, 1800));
        }
        return targets;
    }

    private CampaignRuntimeResult InteractOpening(string id)
    {
        var target = Interactions.FirstOrDefault(i => i.ActionId == id);
        if (InHub || Combat.View.Actors.Single(a => a.Id == 1).Health <= 0 || target is null || !id.StartsWith("opening.", StringComparison.Ordinal) ||
            Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, target.Position) > (long)target.Range * target.Range)
            return Failed("Approach this passage or reliquary before interacting.");
        return id switch
        {
            "opening.crypt.enter" => EnterCrypt(),
            "opening.crypt.return" => ReturnFromCrypt(),
            "opening.crypt.treasure" => ClaimCryptTreasure(),
            "opening.back.road" => RevisitOpeningRoom("campaign.road"),
            "opening.back.monastery" => RevisitOpeningRoom("campaign.monastery"),
            "opening.forward.monastery" => RevisitOpeningRoom("campaign.monastery"),
            "opening.forward.sanctum" => RevisitOpeningRoom("campaign.bell_saint"),
            _ => Failed("Unknown opening passage.")
        };
    }

    private CampaignRuntimeResult EnterCrypt()
    {
        if (!HasOpeningExploration || InHub || ActiveEncounterId != "campaign.road" || !EncounterCleared || story.CurrentState.Exploration is not null)
            return Failed("The Widow's Crypt branches from the secured Grey March road.");
        var messages = new List<string>();
        bool claimed = story.CurrentState.CompletedExploration.Contains(CryptEvent);
        if (!claimed)
        {
            var begin = story.BeginExploration(CryptEvent); Require(begin); messages.AddRange(begin.Events);
            explorationReturnEncounter = "campaign.road";
        }
        CacheOpeningRoom();
        if (clearedRooms.ContainsKey(CryptEncounter) || claimed) ResumeOpeningRoom(CryptEncounter, restoreAtAnchor: true);
        else StartEncounter(CryptEncounter, restoreAtAnchor: true);
        messages.Add("CampaignPassageEntered:" + CryptEncounter);
        return new(true, "", [], messages.ToArray());
    }

    private CampaignRuntimeResult ReturnFromCrypt()
    {
        if (InHub || ActiveEncounterId != CryptEncounter) return Failed("The crypt return passage is not active.");
        CacheOpeningRoom();
        if (story.CurrentState.Exploration is not null) Require(story.EnterAct(1));
        explorationReturnEncounter = "";
        ResumeOpeningRoom("campaign.road");
        return new(true, "", [], ["CampaignPassageEntered:campaign.road"]);
    }

    private CampaignRuntimeResult ClaimCryptTreasure()
    {
        if (ActiveEncounterId != CryptEncounter || !EncounterCleared || story.CurrentState.Exploration?.Id != CryptEvent ||
            story.CurrentState.CompletedExploration.Contains(CryptEvent)) return Failed("Secure the crypt before opening its testament.");
        // Execute's transaction covers the chest, story ledger and permanent reward together.
        Production.ReserveCampaignItemSequence(arena.Capture().NextObjectId);
        var reward = Production.GrantCryptTestament(Content.Data.Version);
        if (!reward.Success) return Failed(reward.Reason);
        var completion = story.CompleteExploration(CryptEncounter); Require(completion);
        var messages = completion.Events.Concat(reward.Events).Concat(Award("campaign.exploration." + CryptEvent, completion)).ToList();
        explorationReturnEncounter = "";
        messages.Add("CryptTestamentClaimed");
        return new(true, "", [], messages.ToArray());
    }

    private CampaignRuntimeResult RevisitOpeningRoom(string id)
    {
        var state = story.CurrentState;
        if (!HasOpeningExploration || InHub || state.CurrentAct != 1 || state.Exploration is not null || !EncounterCleared || !state.CompletedEncounters.Contains(id))
            return Failed("Only secured adjoining areas can be revisited.");
        bool adjacent = (ActiveEncounterId, id) is ("campaign.road", "campaign.monastery") or
            ("campaign.monastery", "campaign.road") or ("campaign.monastery", "campaign.bell_saint") or
            ("campaign.bell_saint", "campaign.monastery");
        if (!adjacent) return Failed("This passage does not connect to that area.");
        CacheOpeningRoom(); ResumeOpeningRoom(id);
        return new(true, "", [], ["CampaignPassageEntered:" + id]);
    }

    private void CacheOpeningRoom(bool restoreDeadPlayer = false)
    {
        if (InHub || !IsRetainedRoom(ActiveEncounterId) || !EncounterCleared || !restoreDeadPlayer && arena.View.Actors.Single(a => a.Id == 1).Health <= 0 ||
            ActiveEncounterId == HuntEncounter && !story.CurrentState.CompletedExploration.Contains(HuntEvent) ||
            HasCinderExploration && ActiveEncounterId == StormEncounter && !story.CurrentState.CompletedExploration.Contains(StormEvent) ||
            HasSpineExploration && ActiveEncounterId == MemoryEncounter && !story.CurrentState.CompletedExploration.Contains(MemoryEvent)) return;
        var snapshot = arena.Capture();
        if (restoreDeadPlayer)
        {
            var player = snapshot.Actors.Single(a => a.Id == 1);
            player.Health = player.MaxHealth; player.DeathProcessed = false; player.Barrier = 0;
            player.InvulnerableUntil = snapshot.Tick;
        }
        // Revisited cleared rooms contain their remaining loot, not lingering combat effects.
        snapshot.Projectiles.Clear(); snapshot.Areas.Clear();
        snapshot.Campaign?.Hazards.Clear();
        snapshot.BufferedCommand = null; snapshot.Legendary = null;
        foreach (var actor in snapshot.Actors)
        {
            actor.Pending = null; actor.MoveX = actor.MoveZ = 0; actor.Statuses.Clear();
            if (actor.Health > 0) { actor.State = "Idle"; actor.RecoveryUntil = snapshot.Tick; }
        }
        Production.ReserveCampaignItemSequence(snapshot.NextObjectId);
        clearedRooms[ActiveEncounterId] = snapshot;
    }

    private void ResumeOpeningRoom(string id, bool restoreAtAnchor = false)
    {
        Production.ClearCampaignEffects();
        if (clearedRooms.Remove(id, out var retained))
        {
            arena = Production.ProjectCampaignCombat(retained);
            if (restoreAtAnchor)
            {
                var restored = arena.Capture(); var player = restored.Actors.Single(a => a.Id == 1);
                player.Health = player.MaxHealth; player.DeathProcessed = false; player.Barrier = 0;
                player.Pending = null; player.Statuses.Clear(); player.State = "Idle";
                player.Position = arena.Room.PlayerSpawn;
                player.InvulnerableUntil = player.RecoveryUntil = restored.Tick;
                restored.PotionCharges = 3; restored.PotionReadyTick = restored.DodgeReadyTick = restored.Tick;
                restored.Cooldowns.Clear();
                arena = CombatSession.Restore(combatJson, restored);
            }
        }
        else arena = CombatSession.CreateClearedEncounter(combatJson, arena.Capture().Seed, id,
            Production.ProjectCampaignCombat(arena.Capture()).Capture(), restoreAtAnchor: true);
        ActiveEncounterId = id;
    }

    private void RestoreClearedRooms(SortedDictionary<string, CombatSnapshot>? rooms)
        => clearedRooms = rooms is null ? new(StringComparer.Ordinal) : JsonData.Copy(rooms);

    private void ValidateClearedRooms()
    {
        int capacity = (HasOpeningExploration ? OpeningRooms.Length : 0) + (HasVerdantExploration ? VerdantRooms.Length : 0) + (HasCinderExploration ? CinderRooms.Length : 0) + (HasSpineExploration ? SpineRooms.Length : 0) + (HasHollowExploration ? HollowRooms.Length : 0);
        if (clearedRooms.Count > capacity) throw new InvalidDataException("Invalid retained campaign rooms.");
        var owned = Production.Capture().Progression.Character.Items.Select(i => i.Id).ToHashSet();
        var lootIds = Combat.View.Loot.Select(l => l.Id).ToHashSet();
        long reservedItems = Production.Capture().Progression.Character.NextItemId;
        foreach (var (id, snapshot) in clearedRooms)
        {
            bool unlocked = id switch
            {
                CryptEncounter => story.CurrentState.CompletedEncounters.Contains("campaign.road"),
                ShrineEncounter => story.CurrentState.CompletedEncounters.Contains("campaign.living_ruins"),
                HuntEncounter => story.CurrentState.CompletedExploration.Contains(HuntEvent),
                FoundryEncounter => story.CurrentState.CompletedEncounters.Contains("campaign.cinder_pack"),
                StormEncounter => story.CurrentState.CompletedExploration.Contains(StormEvent),
                ArchiveEncounter => story.CurrentState.CompletedEncounters.Contains("campaign.bone_causeway"),
                VaultEncounter => story.CurrentState.CompletedEncounters.Contains("campaign.repeating_rooms"),
                MemoryEncounter => story.CurrentState.CompletedExploration.Contains(MemoryEvent),
                _ => story.CurrentState.CompletedEncounters.Contains(id)
            };
            if (!IsRetainedRoom(id) || id == ActiveEncounterId || !unlocked || snapshot is null ||
                snapshot.EncounterId != id && !(snapshot.EncounterId == "clear" && snapshot.RoomEncounterId == id))
                throw new InvalidDataException("Invalid retained campaign room identity.");
            var saved = CombatSession.Restore(combatJson, snapshot);
            if (snapshot.NextObjectId > reservedItems || saved.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) || saved.View.Actors.Single(a => a.Id == 1).Health <= 0)
                throw new InvalidDataException("A retained campaign room must be secured.");
            foreach (var loot in snapshot.Loot)
                if (owned.Contains(loot.Id) || !lootIds.Add(loot.Id)) throw new InvalidDataException("Retained room loot duplicates another owner.");
        }
        if (story.CurrentState.CompletedExploration.Contains(CryptEvent) != Production.ContainsCampaignReceipt("campaign.crypt.testament") ||
            story.CurrentState.CompletedExploration.Contains(CryptEvent) && !Production.HasCryptTestamentReceipt)
            throw new InvalidDataException("Crypt completion differs from its testament receipt.");
        if (story.CurrentState.CompletedExploration.Contains(ShrineEvent) != Production.ContainsCampaignReceipt("campaign.briar.testament") ||
            story.CurrentState.CompletedExploration.Contains(ShrineEvent) && !Production.HasBriarTestamentReceipt)
            throw new InvalidDataException("Briar shrine completion differs from its testament receipt.");
        if (story.CurrentState.CompletedExploration.Contains(FoundryEvent) != Production.ContainsCampaignReceipt("campaign.foundry.testament") ||
            story.CurrentState.CompletedExploration.Contains(FoundryEvent) && !Production.HasFoundryTestamentReceipt)
            throw new InvalidDataException("Foundry completion differs from its testament receipt.");
        if (story.CurrentState.CompletedExploration.Contains(ArchiveEvent) != Production.ContainsCampaignReceipt("campaign.archive.testament") ||
            story.CurrentState.CompletedExploration.Contains(ArchiveEvent) && !Production.HasArchiveTestamentReceipt)
            throw new InvalidDataException("Archive completion differs from its testament receipt.");
        if (story.CurrentState.CompletedExploration.Contains(VaultEvent) != Production.ContainsCampaignReceipt("campaign.vault.testament") ||
            story.CurrentState.CompletedExploration.Contains(VaultEvent) && !Production.HasVaultTestamentReceipt)
            throw new InvalidDataException("Vault completion differs from its testament receipt.");
    }
}
