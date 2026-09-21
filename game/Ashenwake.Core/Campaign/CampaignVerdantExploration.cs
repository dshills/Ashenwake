using Ashenwake.Core.Combat;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Campaign;

public sealed partial class CampaignRuntimeSession
{
    public const string ShrineEvent = "event.briar_shrine", ShrineEncounter = "exploration.briar_shrine";
    public const string HuntEvent = "event.wake_hunt", HuntEncounter = "exploration.antler_hunt";
    private static readonly string[] VerdantRooms = ["campaign.living_ruins", "campaign.plague_village", "campaign.rootheart", ShrineEncounter, HuntEncounter];
    private bool HasVerdantExploration => Content.Data.Exploration.Any(e => e.Id == ShrineEvent);
    private bool IsRetainedRoom(string id) => HasOpeningExploration && OpeningRooms.Contains(id) || HasVerdantExploration && VerdantRooms.Contains(id) || HasCinderExploration && CinderRooms.Contains(id) || HasSpineExploration && SpineRooms.Contains(id) || HasHollowExploration && HollowRooms.Contains(id);
    private bool TrackingVerdantHunt => HasVerdantExploration && story.CurrentState.Exploration?.Id == HuntEvent && ActiveEncounterId == "clear";

    private IReadOnlyList<ExpeditionInteraction>? VerdantInteractions()
    {
        if (!HasVerdantExploration || story.CurrentState.CurrentAct != 2 || !(VerdantRooms.Contains(ActiveEncounterId) || TrackingVerdantHunt)) return null;
        var targets = new List<ExpeditionInteraction>(); var state = story.CurrentState;
        if (ActiveEncounterId == ShrineEncounter)
        {
            targets.Add(new("verdant.shrine.return", "Return to the Living Ruins", VerdantCampaignLayout.BranchReturn, 1800));
            if (EncounterCleared && !state.CompletedExploration.Contains(ShrineEvent))
                targets.Add(new("verdant.shrine.treasure", "Open the Briar Testament · Orrun's Oathseal and 30 materials", VerdantCampaignLayout.ShrineTreasure, 1800));
        }
        else if (ActiveEncounterId == HuntEncounter || TrackingVerdantHunt)
        {
            targets.Add(new("verdant.hunt.return", EncounterCleared && ActiveEncounterId == HuntEncounter && !state.CompletedExploration.Contains(HuntEvent)
                ? "Complete the hunt and return to the village" : "Return from the Antler Grove", VerdantCampaignLayout.BranchReturn, 1800));
            if (TrackingVerdantHunt)
            {
                var current = state.Exploration!; var definition = Content.Data.Exploration.Single(e => e.Id == HuntEvent);
                if (current.TrackedClues < definition.Clues.Length)
                {
                    string clue = definition.Clues[current.TrackedClues];
                    targets.Add(new(clue, "Track " + clue.Replace("clue.", "", StringComparison.Ordinal).Replace('_', ' '),
                        VerdantCampaignLayout.HuntClues[current.TrackedClues % VerdantCampaignLayout.HuntClues.Length], 2200));
                }
            }
        }
        else if (EncounterCleared && state.Exploration is null)
        {
            if (ActiveEncounterId == "campaign.living_ruins")
                targets.Add(new("verdant.shrine.enter", state.CompletedExploration.Contains(ShrineEvent) ? "Briarheart Shrine · testament claimed" : "Explore Briarheart Shrine", VerdantCampaignLayout.ShrineEntrance, 1800));
            if (ActiveEncounterId == "campaign.plague_village")
                targets.Add(new("verdant.hunt.enter", state.CompletedExploration.Contains(HuntEvent) ? "Revisit the Antler Grove" : "Follow the Antler God's trail", VerdantCampaignLayout.HuntEntrance, 1800));
            string? back = ActiveEncounterId switch { "campaign.plague_village" => "ruins", "campaign.rootheart" => "village", _ => null };
            if (back is not null) targets.Add(new("verdant.back." + back, "Return to " + (back == "ruins" ? "the Living Ruins" : "the Plague Village"), VerdantCampaignLayout.BackExit, 1800));
            string? next = ActiveEncounterId switch { "campaign.living_ruins" => "campaign.plague_village", "campaign.plague_village" => "campaign.rootheart", _ => null };
            if (next is not null && (state.CompletedEncounters.Contains(next) || View.EncounterId == next && ChoiceAllows(next)))
                targets.Add(new("verdant.forward." + (next == "campaign.rootheart" ? "rootheart" : "village"),
                    (state.CompletedEncounters.Contains(next) ? "Revisit " : "Continue to ") + (next == "campaign.rootheart" ? "Rootheart" : "the Plague Village"), VerdantCampaignLayout.ForwardExit, 1800));
        }
        return targets;
    }

    private CampaignRuntimeResult InteractVerdant(string id)
    {
        var target = Interactions.FirstOrDefault(i => i.ActionId == id);
        if (InHub || Combat.View.Actors.Single(a => a.Id == 1).Health <= 0 || target is null || !id.StartsWith("verdant.", StringComparison.Ordinal) ||
            Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, target.Position) > (long)target.Range * target.Range)
            return Failed("Approach this Verdant passage or testament before interacting.");
        return id switch
        {
            "verdant.shrine.enter" => EnterVerdantBranch(ShrineEvent, ShrineEncounter, "campaign.living_ruins"),
            "verdant.hunt.enter" => EnterVerdantBranch(HuntEvent, HuntEncounter, "campaign.plague_village"),
            "verdant.shrine.return" or "verdant.hunt.return" => ReturnFromVerdantBranch(),
            "verdant.shrine.treasure" => ClaimBriarTestament(),
            "verdant.back.ruins" => RevisitVerdantRoom("campaign.living_ruins"),
            "verdant.back.village" => RevisitVerdantRoom("campaign.plague_village"),
            "verdant.forward.village" => ForwardVerdant("campaign.plague_village"),
            "verdant.forward.rootheart" => ForwardVerdant("campaign.rootheart"),
            _ => Failed("Unknown Verdant passage.")
        };
    }

    private CampaignRuntimeResult EnterVerdantBranch(string eventId, string encounterId, string parent)
    {
        if (!HasVerdantExploration || InHub || ActiveEncounterId != parent || !EncounterCleared || story.CurrentState.Exploration is not null)
            return Failed("This trail branches from its secured adjoining area.");
        var messages = new List<string>(); bool completed = story.CurrentState.CompletedExploration.Contains(eventId);
        if (!completed)
        {
            var begin = story.BeginExploration(eventId); Require(begin); messages.AddRange(begin.Events);
            explorationReturnEncounter = parent;
        }
        CacheOpeningRoom();
        if (clearedRooms.ContainsKey(encounterId) || completed) ResumeOpeningRoom(encounterId, restoreAtAnchor: true);
        else if (eventId == HuntEvent)
        {
            Production.ClearCampaignEffects();
            arena = CombatSession.CreateClearedEncounter(combatJson, arena.Capture().Seed, HuntEncounter,
                Production.ProjectCampaignCombat(arena.Capture()).Capture(), restoreAtAnchor: true);
            ActiveEncounterId = "clear";
        }
        else StartEncounter(encounterId, restoreAtAnchor: true);
        messages.Add("CampaignPassageEntered:" + encounterId);
        return new(true, "", [], messages.ToArray());
    }

    private string? VerdantBranchReturn()
    {
        if (!HasVerdantExploration) return null;
        if (ActiveEncounterId == ShrineEncounter) return "campaign.living_ruins";
        if (ActiveEncounterId != HuntEncounter && !TrackingVerdantHunt) return null;
        // Earlier catalogs permitted starting this hunt from any secured Act II room.
        return explorationReturnEncounter is "campaign.living_ruins" or "campaign.plague_village" or "campaign.rootheart"
            ? explorationReturnEncounter : story.CurrentState.CompletedEncounters.Contains("campaign.plague_village") ? "campaign.plague_village" : "campaign.living_ruins";
    }

    private CampaignRuntimeResult ReturnFromVerdantBranch()
    {
        string? parent = VerdantBranchReturn();
        if (InHub || parent is null) return Failed("No Verdant branch return passage is active.");
        var messages = new List<string>(); var state = story.CurrentState;
        if (state.Exploration?.Id == HuntEvent && ActiveEncounterId == HuntEncounter && EncounterCleared)
        {
            var completion = story.CompleteExploration(HuntEncounter); Require(completion); messages.AddRange(completion.Events);
            messages.AddRange(Award("campaign.exploration." + HuntEvent, completion));
        }
        CacheOpeningRoom();
        if (story.CurrentState.Exploration is not null)
        {
            var leave = story.EnterAct(2); Require(leave); messages.AddRange(leave.Events);
        }
        explorationReturnEncounter = ""; ResumeOpeningRoom(parent);
        messages.Add("CampaignPassageEntered:" + parent);
        return new(true, "", [], messages.ToArray());
    }

    private CampaignRuntimeResult ClaimBriarTestament()
    {
        if (ActiveEncounterId != ShrineEncounter || !EncounterCleared || story.CurrentState.Exploration?.Id != ShrineEvent || story.CurrentState.CompletedExploration.Contains(ShrineEvent))
            return Failed("Secure Briarheart Shrine before opening its testament.");
        Production.ReserveCampaignItemSequence(arena.Capture().NextObjectId);
        var reward = Production.GrantBriarTestament(); if (!reward.Success) return Failed(reward.Reason);
        var completion = story.CompleteExploration(ShrineEncounter); Require(completion);
        var messages = completion.Events.Concat(reward.Events).Concat(Award("campaign.exploration." + ShrineEvent, completion)).ToList();
        explorationReturnEncounter = ""; messages.Add("BriarTestamentClaimed");
        return new(true, "", [], messages.ToArray());
    }

    private void RevealVerdantHunt()
    {
        var before = arena.Capture(); var position = before.Actors.Single(a => a.Id == 1).Position;
        StartEncounter(HuntEncounter, restoreAtAnchor: false);
        var revealed = arena.Capture(); revealed.Actors.Single(a => a.Id == 1).Position = position;
        arena = CombatSession.Restore(combatJson, revealed);
    }

    private CampaignRuntimeResult ForwardVerdant(string id) => story.CurrentState.CompletedEncounters.Contains(id)
        ? RevisitVerdantRoom(id) : ChangeWorld(new(CampaignRuntimeAction.AdvanceEncounter));

    private bool CanAdvanceVerdant(string expected)
        => (ActiveEncounterId, expected) is ("campaign.living_ruins", "campaign.plague_village") or ("campaign.plague_village", "campaign.rootheart") &&
            Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, VerdantCampaignLayout.ForwardExit) <= 2000L * 2000;

    private CampaignRuntimeResult RevisitVerdantPassage(string id)
    {
        if (!story.CurrentState.CompletedEncounters.Contains(id)) return Failed("Only secured adjoining areas can be revisited.");
        string? action = (ActiveEncounterId, id) switch
        {
            ("campaign.living_ruins", "campaign.plague_village") => "verdant.forward.village",
            ("campaign.plague_village", "campaign.living_ruins") => "verdant.back.ruins",
            ("campaign.plague_village", "campaign.rootheart") => "verdant.forward.rootheart",
            ("campaign.rootheart", "campaign.plague_village") => "verdant.back.village",
            _ => null
        };
        return action is null ? Failed("This passage does not connect to that area.") : InteractVerdant(action);
    }

    private CampaignRuntimeResult RevisitVerdantRoom(string id)
    {
        if (!HasVerdantExploration || InHub || story.CurrentState.CurrentAct != 2 || story.CurrentState.Exploration is not null || !EncounterCleared || !story.CurrentState.CompletedEncounters.Contains(id))
            return Failed("Only secured adjoining areas can be revisited.");
        CacheOpeningRoom(); ResumeOpeningRoom(id);
        return new(true, "", [], ["CampaignPassageEntered:" + id]);
    }

    private void ValidateVerdantExploration(string eventId, string expected)
    {
        if (!HasVerdantExploration) return;
        if (eventId == ShrineEvent && explorationReturnEncounter != "campaign.living_ruins")
            throw new InvalidDataException("Briarheart Shrine must return to the Living Ruins.");
        if (eventId != HuntEvent) return;
        if (explorationReturnEncounter is not ("campaign.living_ruins" or "campaign.plague_village" or "campaign.rootheart") ||
            expected == "clear" && arena.Capture().RoomEncounterId != HuntEncounter)
            throw new InvalidDataException("Antler tracking must retain its grove layout and regional return passage.");
    }

    private bool ValidateCompletedVerdantRoom()
    {
        if (!HasVerdantExploration || ActiveEncounterId is not (ShrineEncounter or HuntEncounter)) return false;
        string eventId = ActiveEncounterId == ShrineEncounter ? ShrineEvent : HuntEvent;
        if (story.CurrentState.CurrentAct != 2 || !story.CurrentState.CompletedExploration.Contains(eventId) || !EncounterCleared ||
            arena.EncounterId != ActiveEncounterId && !(arena.EncounterId == "clear" && arena.Capture().RoomEncounterId == ActiveEncounterId) ||
            arena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
            throw new InvalidDataException("Completed Verdant branch arena is inconsistent.");
        return true;
    }
}
