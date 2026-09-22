using Ashenwake.Core.Combat;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Campaign;

public sealed partial class CampaignRuntimeSession
{
    public const string FoundryEvent = "event.sealed_foundry", FoundryEncounter = "exploration.sealed_foundry";
    public const string StormEvent = "event.resonance_storm", StormEncounter = "exploration.burning_rain";
    private static readonly string[] CinderRooms = ["campaign.cinder_pack", "campaign.extraction_floor", "campaign.furnace_spindle", FoundryEncounter, StormEncounter];
    private bool HasCinderExploration => Content.Data.Exploration.Any(e => e.Id == FoundryEvent);

    private IReadOnlyList<ExpeditionInteraction>? CinderInteractions()
    {
        if (!HasCinderExploration || story.CurrentState.CurrentAct != 3 || !CinderRooms.Contains(ActiveEncounterId)) return null;
        var targets = new List<ExpeditionInteraction>(); var state = story.CurrentState;
        if (ActiveEncounterId == FoundryEncounter)
        {
            targets.Add(new("cinder.foundry.return", "Return to the Cinder fields", CinderCampaignLayout.BranchReturn, CinderCampaignLayout.InteractionRange));
            if (EncounterCleared && !state.CompletedExploration.Contains(FoundryEvent))
                targets.Add(new("cinder.foundry.treasure", "Open the Foundry Testament · Cinderwake Saber and 35 materials", CinderCampaignLayout.FoundryTreasure, CinderCampaignLayout.InteractionRange));
        }
        else if (ActiveEncounterId == StormEncounter)
        {
            string parent = CinderBranchReturn() switch
            {
                "campaign.cinder_pack" => "the Cinder fields",
                "campaign.furnace_spindle" => "Furnace Spindle",
                _ => "the Extraction Floor"
            };
            targets.Add(new("cinder.storm.return", "Return to " + parent + (state.Exploration is not null ? " · ends this storm attempt" : ""),
                CinderCampaignLayout.BranchReturn, CinderCampaignLayout.InteractionRange));
        }
        else if (EncounterCleared && state.Exploration is null)
        {
            if (ActiveEncounterId == "campaign.cinder_pack")
                targets.Add(new("cinder.foundry.enter", state.CompletedExploration.Contains(FoundryEvent) ? "The Sealed Foundry · testament claimed" : "Explore the Sealed Foundry", CinderCampaignLayout.FoundryEntrance, CinderCampaignLayout.InteractionRange));
            if (ActiveEncounterId == "campaign.extraction_floor")
                targets.Add(new("cinder.storm.enter", state.CompletedExploration.Contains(StormEvent) ? "Revisit the storm collectors" : "Enter the Burning Rain · 30 seconds", CinderCampaignLayout.StormEntrance, CinderCampaignLayout.InteractionRange));
            string? back = ActiveEncounterId switch { "campaign.extraction_floor" => "fields", "campaign.furnace_spindle" => "floor", _ => null };
            if (back is not null) targets.Add(new("cinder.back." + back, "Return to " + (back == "fields" ? "the Cinder fields" : "the Extraction Floor"), CinderCampaignLayout.BackExit, CinderCampaignLayout.InteractionRange));
            string? next = ActiveEncounterId switch { "campaign.cinder_pack" => "campaign.extraction_floor", "campaign.extraction_floor" => "campaign.furnace_spindle", _ => null };
            if (next is not null && (state.CompletedEncounters.Contains(next) || View.EncounterId == next && ChoiceAllows(next)))
                targets.Add(new("cinder.forward." + (next == "campaign.furnace_spindle" ? "spindle" : "floor"),
                    (state.CompletedEncounters.Contains(next) ? "Revisit " : "Continue to ") + (next == "campaign.furnace_spindle" ? "Furnace Spindle" : "the Extraction Floor"), CinderCampaignLayout.ForwardExit, CinderCampaignLayout.InteractionRange));
        }
        return targets;
    }

    private CampaignRuntimeResult InteractCinder(string id)
    {
        var target = Interactions.FirstOrDefault(i => i.ActionId == id);
        if (InHub || Combat.View.Actors.Single(a => a.Id == 1).Health <= 0 || target is null || !id.StartsWith("cinder.", StringComparison.Ordinal) ||
            Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, target.Position) > (long)target.Range * target.Range)
            return Failed("Approach this Cinder passage or testament before interacting.");
        return id switch
        {
            "cinder.foundry.enter" => EnterCinderBranch(FoundryEvent, FoundryEncounter, "campaign.cinder_pack"),
            "cinder.storm.enter" => EnterCinderBranch(StormEvent, StormEncounter, "campaign.extraction_floor"),
            "cinder.foundry.return" or "cinder.storm.return" => ReturnFromCinderBranch(),
            "cinder.foundry.treasure" => ClaimFoundryTestament(),
            "cinder.back.fields" => RevisitCinderRoom("campaign.cinder_pack"),
            "cinder.back.floor" => RevisitCinderRoom("campaign.extraction_floor"),
            "cinder.forward.floor" => ForwardCinder("campaign.extraction_floor"),
            "cinder.forward.spindle" => ForwardCinder("campaign.furnace_spindle"),
            _ => Failed("Unknown Cinder passage.")
        };
    }

    private CampaignRuntimeResult EnterCinderBranch(string eventId, string encounterId, string parent)
    {
        if (!HasCinderExploration || InHub || ActiveEncounterId != parent || !EncounterCleared || story.CurrentState.Exploration is not null)
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

    private string? CinderBranchReturn()
    {
        if (!HasCinderExploration) return null;
        if (ActiveEncounterId == FoundryEncounter) return "campaign.cinder_pack";
        if (ActiveEncounterId != StormEncounter) return null;
        // Earlier catalogs permitted starting this storm from any secured Act III room.
        return explorationReturnEncounter is "campaign.cinder_pack" or "campaign.extraction_floor" or "campaign.furnace_spindle"
            ? explorationReturnEncounter : story.CurrentState.CompletedEncounters.Contains("campaign.extraction_floor") ? "campaign.extraction_floor" : "campaign.cinder_pack";
    }

    private CampaignRuntimeResult ReturnFromCinderBranch()
    {
        string? parent = CinderBranchReturn();
        if (InHub || parent is null) return Failed("No Cinder branch return passage is active.");
        var messages = new List<string>(); var state = story.CurrentState;
        if (state.Exploration?.Id == StormEvent && ActiveEncounterId == StormEncounter && EncounterCleared)
        {
            var completion = story.CompleteExploration(StormEncounter); Require(completion); messages.AddRange(completion.Events);
            messages.AddRange(Award("campaign.exploration." + StormEvent, completion));
        }
        CacheOpeningRoom();
        if (story.CurrentState.Exploration is not null)
        {
            var leave = story.EnterAct(3); Require(leave); messages.AddRange(leave.Events);
        }
        explorationReturnEncounter = ""; ResumeOpeningRoom(parent);
        messages.Add("CampaignPassageEntered:" + parent);
        return new(true, "", [], messages.ToArray());
    }

    private CampaignRuntimeResult ClaimFoundryTestament()
    {
        if (ActiveEncounterId != FoundryEncounter || !EncounterCleared || story.CurrentState.Exploration?.Id != FoundryEvent || story.CurrentState.CompletedExploration.Contains(FoundryEvent))
            return Failed("Secure the Sealed Foundry before opening its testament.");
        Production.ReserveCampaignItemSequence(arena.Capture().NextObjectId);
        var reward = Production.GrantFoundryTestament(Content.Data.Version); if (!reward.Success) return Failed(reward.Reason);
        var completion = story.CompleteExploration(FoundryEncounter); Require(completion);
        var messages = completion.Events.Concat(reward.Events).Concat(Award("campaign.exploration." + FoundryEvent, completion)).ToList();
        explorationReturnEncounter = ""; messages.Add("FoundryTestamentClaimed");
        return new(true, "", [], messages.ToArray());
    }

    private CampaignRuntimeResult ForwardCinder(string id) => story.CurrentState.CompletedEncounters.Contains(id)
        ? RevisitCinderRoom(id) : ChangeWorld(new(CampaignRuntimeAction.AdvanceEncounter));

    private bool CanAdvanceCinder(string expected)
        => (ActiveEncounterId, expected) is ("campaign.cinder_pack", "campaign.extraction_floor") or ("campaign.extraction_floor", "campaign.furnace_spindle") &&
            Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, CinderCampaignLayout.ForwardExit) <= (long)CinderCampaignLayout.InteractionRange * CinderCampaignLayout.InteractionRange;

    private CampaignRuntimeResult RevisitCinderPassage(string id)
    {
        if (!story.CurrentState.CompletedEncounters.Contains(id)) return Failed("Only secured adjoining areas can be revisited.");
        string? action = (ActiveEncounterId, id) switch
        {
            ("campaign.cinder_pack", "campaign.extraction_floor") => "cinder.forward.floor",
            ("campaign.extraction_floor", "campaign.cinder_pack") => "cinder.back.fields",
            ("campaign.extraction_floor", "campaign.furnace_spindle") => "cinder.forward.spindle",
            ("campaign.furnace_spindle", "campaign.extraction_floor") => "cinder.back.floor",
            _ => null
        };
        return action is null ? Failed("This passage does not connect to that area.") : InteractCinder(action);
    }

    private CampaignRuntimeResult RevisitCinderRoom(string id)
    {
        if (!HasCinderExploration || InHub || story.CurrentState.CurrentAct != 3 || story.CurrentState.Exploration is not null || !EncounterCleared || !story.CurrentState.CompletedEncounters.Contains(id))
            return Failed("Only secured adjoining areas can be revisited.");
        CacheOpeningRoom(); ResumeOpeningRoom(id);
        return new(true, "", [], ["CampaignPassageEntered:" + id]);
    }

    private void ValidateCinderExploration(string eventId)
    {
        if (!HasCinderExploration) return;
        if (eventId == FoundryEvent && explorationReturnEncounter != "campaign.cinder_pack")
            throw new InvalidDataException("The Sealed Foundry must return to the Cinder fields.");
        if (eventId != StormEvent) return;
        if (explorationReturnEncounter is not ("campaign.cinder_pack" or "campaign.extraction_floor" or "campaign.furnace_spindle"))
            throw new InvalidDataException("The Burning Rain must retain a secured regional return passage.");
    }

    private bool ValidateCompletedCinderRoom()
    {
        if (!HasCinderExploration || ActiveEncounterId is not (FoundryEncounter or StormEncounter)) return false;
        string eventId = ActiveEncounterId == FoundryEncounter ? FoundryEvent : StormEvent;
        if (story.CurrentState.CurrentAct != 3 || !story.CurrentState.CompletedExploration.Contains(eventId) || !EncounterCleared ||
            arena.EncounterId != ActiveEncounterId && !(arena.EncounterId == "clear" && arena.Capture().RoomEncounterId == ActiveEncounterId) ||
            arena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
            throw new InvalidDataException("Completed Cinder branch arena is inconsistent.");
        if (explorationReturnEncounter != "" && (ActiveEncounterId != StormEncounter ||
            explorationReturnEncounter is not ("campaign.cinder_pack" or "campaign.extraction_floor" or "campaign.furnace_spindle") ||
            !story.CurrentState.CompletedEncounters.Contains(explorationReturnEncounter)))
            throw new InvalidDataException("Completed storm return passage is inconsistent.");
        return true;
    }
}
