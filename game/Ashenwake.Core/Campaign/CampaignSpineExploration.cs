using Ashenwake.Core.Combat;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Campaign;

public sealed partial class CampaignRuntimeSession
{
    public const string ArchiveEvent = "event.oathkeeper_archive", ArchiveEncounter = "exploration.oathkeeper_archive";
    public const string MemoryEvent = "event.divine_memory", MemoryEncounter = "exploration.first_oath";
    private static readonly string[] SpineRooms = ["campaign.bone_causeway", "campaign.contract_hall", "campaign.covenant_warden", ArchiveEncounter, MemoryEncounter];
    private bool HasSpineExploration => Content.Data.Exploration.Any(e => e.Id == ArchiveEvent);

    private IReadOnlyList<ExpeditionInteraction>? SpineInteractions()
    {
        if (!HasSpineExploration || story.CurrentState.CurrentAct != 4 || !SpineRooms.Contains(ActiveEncounterId)) return null;
        var targets = new List<ExpeditionInteraction>(); var state = story.CurrentState;
        if (ActiveEncounterId == ArchiveEncounter)
        {
            targets.Add(new("spine.archive.return", "Return to the Bone Causeway", SpineCampaignLayout.BranchReturn, SpineCampaignLayout.InteractionRange));
            if (EncounterCleared && !state.CompletedExploration.Contains(ArchiveEvent))
                targets.Add(new("spine.archive.treasure", "Open the Archive Testament · Vowkeeper’s Carapace and 40 materials", SpineCampaignLayout.ArchiveTreasure, SpineCampaignLayout.InteractionRange));
        }
        else if (ActiveEncounterId == MemoryEncounter)
        {
            string parent = SpineBranchReturn() switch
            {
                "campaign.bone_causeway" => "the Bone Causeway",
                "campaign.covenant_warden" => "Covenant Warden",
                _ => "the Contract Hall"
            };
            targets.Add(new("spine.memory.return", "Return to " + parent + (state.Exploration is not null ? " · leave this memory" : ""),
                SpineCampaignLayout.BranchReturn, SpineCampaignLayout.InteractionRange));
        }
        else if (EncounterCleared && state.Exploration is null)
        {
            if (ActiveEncounterId == "campaign.bone_causeway")
                targets.Add(new("spine.archive.enter", state.CompletedExploration.Contains(ArchiveEvent) ? "The Oathkeeper’s Archive · testament claimed" : "Explore the Oathkeeper’s Archive", SpineCampaignLayout.ArchiveEntrance, SpineCampaignLayout.InteractionRange));
            if (ActiveEncounterId == "campaign.contract_hall")
                targets.Add(new("spine.memory.enter", state.CompletedExploration.Contains(MemoryEvent) ? "Revisit the Promise Before Stone" : "Enter the Promise Before Stone", SpineCampaignLayout.MemoryEntrance, SpineCampaignLayout.InteractionRange));
            string? back = ActiveEncounterId switch { "campaign.contract_hall" => "causeway", "campaign.covenant_warden" => "hall", _ => null };
            if (back is not null) targets.Add(new("spine.back." + back, "Return to " + (back == "causeway" ? "the Bone Causeway" : "the Contract Hall"), SpineCampaignLayout.BackExit, SpineCampaignLayout.InteractionRange));
            string? next = ActiveEncounterId switch { "campaign.bone_causeway" => "campaign.contract_hall", "campaign.contract_hall" => "campaign.covenant_warden", _ => null };
            if (next is not null && (state.CompletedEncounters.Contains(next) || View.EncounterId == next && ChoiceAllows(next)))
                targets.Add(new("spine.forward." + (next == "campaign.covenant_warden" ? "warden" : "hall"),
                    (state.CompletedEncounters.Contains(next) ? "Revisit " : "Continue to ") + (next == "campaign.covenant_warden" ? "Covenant Warden" : "the Contract Hall"), SpineCampaignLayout.ForwardExit, SpineCampaignLayout.InteractionRange));
        }
        return targets;
    }

    private CampaignRuntimeResult InteractSpine(string id)
    {
        var target = Interactions.FirstOrDefault(i => i.ActionId == id);
        if (InHub || Combat.View.Actors.Single(a => a.Id == 1).Health <= 0 || target is null || !id.StartsWith("spine.", StringComparison.Ordinal) ||
            Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, target.Position) > (long)target.Range * target.Range)
            return Failed("Approach this Spine passage or testament before interacting.");
        return id switch
        {
            "spine.archive.enter" => EnterSpineBranch(ArchiveEvent, ArchiveEncounter, "campaign.bone_causeway"),
            "spine.memory.enter" => EnterSpineBranch(MemoryEvent, MemoryEncounter, "campaign.contract_hall"),
            "spine.archive.return" or "spine.memory.return" => ReturnFromSpineBranch(),
            "spine.archive.treasure" => ClaimArchiveTestament(),
            "spine.back.causeway" => RevisitSpineRoom("campaign.bone_causeway"),
            "spine.back.hall" => RevisitSpineRoom("campaign.contract_hall"),
            "spine.forward.hall" => ForwardSpine("campaign.contract_hall"),
            "spine.forward.warden" => ForwardSpine("campaign.covenant_warden"),
            _ => Failed("Unknown Spine passage.")
        };
    }

    private CampaignRuntimeResult EnterSpineBranch(string eventId, string encounterId, string parent)
    {
        if (!HasSpineExploration || InHub || ActiveEncounterId != parent || !EncounterCleared || story.CurrentState.Exploration is not null)
            return Failed("This trail branches from its secured adjoining area.");
        var messages = new List<string>(); bool completed = story.CurrentState.CompletedExploration.Contains(eventId);
        if (!completed)
        {
            var begin = story.BeginExploration(eventId); Require(begin); messages.AddRange(begin.Events);
            explorationReturnEncounter = parent;
        }
        CacheOpeningRoom();
        if (clearedRooms.ContainsKey(encounterId) || completed) ResumeOpeningRoom(encounterId, restoreAtAnchor: true);
        else StartEncounter(encounterId, restoreAtAnchor: true);
        messages.Add("CampaignPassageEntered:" + encounterId);
        return new(true, "", [], messages.ToArray());
    }

    private string? SpineBranchReturn()
    {
        if (!HasSpineExploration) return null;
        if (ActiveEncounterId == ArchiveEncounter) return "campaign.bone_causeway";
        if (ActiveEncounterId != MemoryEncounter) return null;
        // Earlier catalogs permitted starting this memory from any secured Act IV room.
        return explorationReturnEncounter is "campaign.bone_causeway" or "campaign.contract_hall" or "campaign.covenant_warden"
            ? explorationReturnEncounter : story.CurrentState.CompletedEncounters.Contains("campaign.contract_hall") ? "campaign.contract_hall" : "campaign.bone_causeway";
    }

    private CampaignRuntimeResult ReturnFromSpineBranch()
    {
        string? parent = SpineBranchReturn();
        if (InHub || parent is null) return Failed("No Spine branch return passage is active.");
        var messages = new List<string>(); var state = story.CurrentState;
        if (state.Exploration?.Id == MemoryEvent && ActiveEncounterId == MemoryEncounter && EncounterCleared)
        {
            var completion = story.CompleteExploration(MemoryEncounter); Require(completion); messages.AddRange(completion.Events);
            messages.AddRange(Award("campaign.exploration." + MemoryEvent, completion));
        }
        CacheOpeningRoom();
        if (story.CurrentState.Exploration is not null)
        {
            var leave = story.EnterAct(4); Require(leave); messages.AddRange(leave.Events);
        }
        explorationReturnEncounter = ""; ResumeOpeningRoom(parent);
        messages.Add("CampaignPassageEntered:" + parent);
        return new(true, "", [], messages.ToArray());
    }

    private CampaignRuntimeResult ClaimArchiveTestament()
    {
        if (ActiveEncounterId != ArchiveEncounter || !EncounterCleared || story.CurrentState.Exploration?.Id != ArchiveEvent || story.CurrentState.CompletedExploration.Contains(ArchiveEvent))
            return Failed("Secure the Oathkeeper’s Archive before opening its testament.");
        Production.ReserveCampaignItemSequence(arena.Capture().NextObjectId);
        var reward = Production.GrantArchiveTestament(Content.Data.Version); if (!reward.Success) return Failed(reward.Reason);
        var completion = story.CompleteExploration(ArchiveEncounter); Require(completion);
        var messages = completion.Events.Concat(reward.Events).Concat(Award("campaign.exploration." + ArchiveEvent, completion)).ToList();
        explorationReturnEncounter = ""; messages.Add("ArchiveTestamentClaimed");
        return new(true, "", [], messages.ToArray());
    }

    private CampaignRuntimeResult ForwardSpine(string id) => story.CurrentState.CompletedEncounters.Contains(id)
        ? RevisitSpineRoom(id) : ChangeWorld(new(CampaignRuntimeAction.AdvanceEncounter));

    private bool CanAdvanceSpine(string expected)
        => (ActiveEncounterId, expected) is ("campaign.bone_causeway", "campaign.contract_hall") or ("campaign.contract_hall", "campaign.covenant_warden") &&
            Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, SpineCampaignLayout.ForwardExit) <= (long)SpineCampaignLayout.InteractionRange * SpineCampaignLayout.InteractionRange;

    private CampaignRuntimeResult RevisitSpinePassage(string id)
    {
        if (!story.CurrentState.CompletedEncounters.Contains(id)) return Failed("Only secured adjoining areas can be revisited.");
        string? action = (ActiveEncounterId, id) switch
        {
            ("campaign.bone_causeway", "campaign.contract_hall") => "spine.forward.hall",
            ("campaign.contract_hall", "campaign.bone_causeway") => "spine.back.causeway",
            ("campaign.contract_hall", "campaign.covenant_warden") => "spine.forward.warden",
            ("campaign.covenant_warden", "campaign.contract_hall") => "spine.back.hall",
            _ => null
        };
        return action is null ? Failed("This passage does not connect to that area.") : InteractSpine(action);
    }

    private CampaignRuntimeResult RevisitSpineRoom(string id)
    {
        if (!HasSpineExploration || InHub || story.CurrentState.CurrentAct != 4 || story.CurrentState.Exploration is not null || !EncounterCleared || !story.CurrentState.CompletedEncounters.Contains(id))
            return Failed("Only secured adjoining areas can be revisited.");
        CacheOpeningRoom(); ResumeOpeningRoom(id);
        return new(true, "", [], ["CampaignPassageEntered:" + id]);
    }

    private void ValidateSpineExploration(string eventId)
    {
        if (!HasSpineExploration) return;
        if (eventId == ArchiveEvent && explorationReturnEncounter != "campaign.bone_causeway")
            throw new InvalidDataException("The Oathkeeper’s Archive must return to the Bone Causeway.");
        if (eventId != MemoryEvent) return;
        if (explorationReturnEncounter is not ("campaign.bone_causeway" or "campaign.contract_hall" or "campaign.covenant_warden"))
            throw new InvalidDataException("The Promise Before Stone must retain a secured regional return passage.");
    }

    private bool ValidateCompletedSpineRoom()
    {
        if (!HasSpineExploration || ActiveEncounterId is not (ArchiveEncounter or MemoryEncounter)) return false;
        string eventId = ActiveEncounterId == ArchiveEncounter ? ArchiveEvent : MemoryEvent;
        if (story.CurrentState.CurrentAct != 4 || !story.CurrentState.CompletedExploration.Contains(eventId) || !EncounterCleared ||
            arena.EncounterId != ActiveEncounterId && !(arena.EncounterId == "clear" && arena.Capture().RoomEncounterId == ActiveEncounterId) ||
            arena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
            throw new InvalidDataException("Completed Spine branch arena is inconsistent.");
        if (explorationReturnEncounter != "" && (ActiveEncounterId != MemoryEncounter ||
            explorationReturnEncounter is not ("campaign.bone_causeway" or "campaign.contract_hall" or "campaign.covenant_warden") ||
            !story.CurrentState.CompletedEncounters.Contains(explorationReturnEncounter)))
            throw new InvalidDataException("Completed memory return passage is inconsistent.");
        return true;
    }
}
