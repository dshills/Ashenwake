using Ashenwake.Core.Combat;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Campaign;

public sealed partial class CampaignRuntimeSession
{
    public const string VaultEvent = "event.unremembered_vault", VaultEncounter = "exploration.unremembered_vault";
    private static readonly string[] HollowRooms = ["campaign.repeating_rooms", "campaign.identity_memory", "campaign.breach_heart", VaultEncounter];
    private bool HasHollowExploration => Content.Data.Exploration.Any(e => e.Id == VaultEvent);

    private IReadOnlyList<ExpeditionInteraction>? HollowInteractions()
    {
        if (!HasHollowExploration || story.CurrentState.CurrentAct != 5 || !HollowRooms.Contains(ActiveEncounterId)) return null;
        var targets = new List<ExpeditionInteraction>(); var state = story.CurrentState;
        if (ActiveEncounterId == VaultEncounter)
        {
            targets.Add(new("hollow.vault.return", "Return to the Repeating Rooms", HollowCampaignLayout.BranchReturn, HollowCampaignLayout.InteractionRange));
            if (EncounterCleared && !state.CompletedExploration.Contains(VaultEvent))
                targets.Add(new("hollow.vault.treasure", "Open the Vault Testament · Choir of the Unburied and 45 materials", HollowCampaignLayout.VaultTreasure, HollowCampaignLayout.InteractionRange));
        }
        else if (EncounterCleared && state.Exploration is null)
        {
            if (ActiveEncounterId == "campaign.repeating_rooms")
                targets.Add(new("hollow.vault.enter", state.CompletedExploration.Contains(VaultEvent) ? "The Unremembered Vault · testament claimed" : "Explore the Unremembered Vault", HollowCampaignLayout.VaultEntrance, HollowCampaignLayout.InteractionRange));
            string? back = ActiveEncounterId switch { "campaign.identity_memory" => "rooms", "campaign.breach_heart" => "memory", _ => null };
            if (back is not null) targets.Add(new("hollow.back." + back, "Return to " + (back == "rooms" ? "the Repeating Rooms" : "Identity Memory"), HollowCampaignLayout.BackExit, HollowCampaignLayout.InteractionRange));
            string? next = ActiveEncounterId switch { "campaign.repeating_rooms" => "campaign.identity_memory", "campaign.identity_memory" => "campaign.breach_heart", _ => null };
            if (next is not null && (state.CompletedEncounters.Contains(next) || View.EncounterId == next && ChoiceAllows(next)))
                targets.Add(new("hollow.forward." + (next == "campaign.breach_heart" ? "breach" : "memory"),
                    (state.CompletedEncounters.Contains(next) ? "Revisit " : "Continue to ") + (next == "campaign.breach_heart" ? "Breach Heart" : "Identity Memory"), HollowCampaignLayout.ForwardExit, HollowCampaignLayout.InteractionRange));
        }
        return targets;
    }

    private CampaignRuntimeResult InteractHollow(string id)
    {
        var target = Interactions.FirstOrDefault(i => i.ActionId == id);
        if (InHub || Combat.View.Actors.Single(a => a.Id == 1).Health <= 0 || target is null || !id.StartsWith("hollow.", StringComparison.Ordinal) ||
            Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, target.Position) > (long)target.Range * target.Range)
            return Failed("Approach this Hollow passage or testament before interacting.");
        return id switch
        {
            "hollow.vault.enter" => EnterHollowBranch(VaultEvent, VaultEncounter, "campaign.repeating_rooms"),
            "hollow.vault.return" => ReturnFromHollowBranch(),
            "hollow.vault.treasure" => ClaimVaultTestament(),
            "hollow.back.rooms" => RevisitHollowRoom("campaign.repeating_rooms"),
            "hollow.back.memory" => RevisitHollowRoom("campaign.identity_memory"),
            "hollow.forward.memory" => ForwardHollow("campaign.identity_memory"),
            "hollow.forward.breach" => ForwardHollow("campaign.breach_heart"),
            _ => Failed("Unknown Hollow passage.")
        };
    }

    private CampaignRuntimeResult EnterHollowBranch(string eventId, string encounterId, string parent)
    {
        if (!HasHollowExploration || InHub || ActiveEncounterId != parent || !EncounterCleared || story.CurrentState.Exploration is not null)
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

    private string? HollowBranchReturn() => HasHollowExploration && ActiveEncounterId == VaultEncounter ? "campaign.repeating_rooms" : null;

    private CampaignRuntimeResult ReturnFromHollowBranch()
    {
        string? parent = HollowBranchReturn();
        if (InHub || parent is null) return Failed("No Hollow branch return passage is active.");
        var messages = new List<string>();
        CacheOpeningRoom();
        if (story.CurrentState.Exploration is not null)
        {
            var leave = story.EnterAct(5); Require(leave); messages.AddRange(leave.Events);
        }
        explorationReturnEncounter = ""; ResumeOpeningRoom(parent);
        messages.Add("CampaignPassageEntered:" + parent);
        return new(true, "", [], messages.ToArray());
    }

    private CampaignRuntimeResult ClaimVaultTestament()
    {
        if (ActiveEncounterId != VaultEncounter || !EncounterCleared || story.CurrentState.Exploration?.Id != VaultEvent || story.CurrentState.CompletedExploration.Contains(VaultEvent))
            return Failed("Secure the Unremembered Vault before opening its testament.");
        Production.ReserveCampaignItemSequence(arena.Capture().NextObjectId);
        var reward = Production.GrantVaultTestament(Content.Data.Version); if (!reward.Success) return Failed(reward.Reason);
        var completion = story.CompleteExploration(VaultEncounter); Require(completion);
        var messages = completion.Events.Concat(reward.Events).Concat(Award("campaign.exploration." + VaultEvent, completion)).ToList();
        explorationReturnEncounter = ""; messages.Add("VaultTestamentClaimed");
        return new(true, "", [], messages.ToArray());
    }

    private CampaignRuntimeResult ForwardHollow(string id) => story.CurrentState.CompletedEncounters.Contains(id)
        ? RevisitHollowRoom(id) : ChangeWorld(new(CampaignRuntimeAction.AdvanceEncounter));

    private bool CanAdvanceHollow(string expected)
        => (ActiveEncounterId, expected) is ("campaign.repeating_rooms", "campaign.identity_memory") or ("campaign.identity_memory", "campaign.breach_heart") &&
            Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, HollowCampaignLayout.ForwardExit) <= (long)HollowCampaignLayout.InteractionRange * HollowCampaignLayout.InteractionRange;

    private CampaignRuntimeResult RevisitHollowPassage(string id)
    {
        if (!story.CurrentState.CompletedEncounters.Contains(id)) return Failed("Only secured adjoining areas can be revisited.");
        string? action = (ActiveEncounterId, id) switch
        {
            ("campaign.repeating_rooms", "campaign.identity_memory") => "hollow.forward.memory",
            ("campaign.identity_memory", "campaign.repeating_rooms") => "hollow.back.rooms",
            ("campaign.identity_memory", "campaign.breach_heart") => "hollow.forward.breach",
            ("campaign.breach_heart", "campaign.identity_memory") => "hollow.back.memory",
            _ => null
        };
        return action is null ? Failed("This passage does not connect to that area.") : InteractHollow(action);
    }

    private CampaignRuntimeResult RevisitHollowRoom(string id)
    {
        if (!HasHollowExploration || InHub || story.CurrentState.CurrentAct != 5 || story.CurrentState.Exploration is not null || !EncounterCleared || !story.CurrentState.CompletedEncounters.Contains(id))
            return Failed("Only secured adjoining areas can be revisited.");
        CacheOpeningRoom(); ResumeOpeningRoom(id);
        return new(true, "", [], ["CampaignPassageEntered:" + id]);
    }

    private void ValidateHollowExploration(string eventId)
    {
        if (!HasHollowExploration) return;
        if (eventId == VaultEvent && explorationReturnEncounter != "campaign.repeating_rooms")
            throw new InvalidDataException("The Unremembered Vault must return to the Repeating Rooms.");
    }

    private bool ValidateCompletedHollowRoom()
    {
        if (!HasHollowExploration || ActiveEncounterId != VaultEncounter) return false;
        if (story.CurrentState.CurrentAct != 5 || !story.CurrentState.CompletedExploration.Contains(VaultEvent) ||
            !story.CurrentState.CompletedEncounters.Contains("campaign.repeating_rooms") || !EncounterCleared || explorationReturnEncounter != "" ||
            arena.EncounterId != VaultEncounter && !(arena.EncounterId == "clear" && arena.Capture().RoomEncounterId == VaultEncounter) ||
            arena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
            throw new InvalidDataException("Completed Hollow branch arena is inconsistent.");
        return true;
    }
}
