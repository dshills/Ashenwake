using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    public static bool IsStashAction(ProductionAction action) => action is ProductionAction.StoreItem or ProductionAction.RetrieveItem or ProductionAction.MoveStashedItem or ProductionAction.RenameStashTab;
    public PersonalStashView Stash
    {
        get { string blocked = StashBlockedReason(); return progression.Stash with { CanUse = blocked == "", Requirement = blocked }; }
    }
    private string StashBlockedReason()
    {
        if (View.RoomId != "room.greyhaven") return "Return to your personal stash in Greyhaven.";
        if (!progression.CharacterState.CompletedObjectives.Contains("objective.torren")) return "Rescue Torren to unlock the personal stash beside his workshop.";
        if (!Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0)) return "Cannot use the stash while defeated.";
        if (Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, PersonalStashCatalog.Position) > (long)PersonalStashCatalog.Range * PersonalStashCatalog.Range) return "Approach the personal stash beside Torren.";
        if (Combat.Capture().Experiment is not null) return "End the borrowed-memory experiment before using your stash.";
        return "";
    }
    private ProductionResult ExecuteStash(ProductionCommand command)
    {
        string blocked = StashBlockedReason();
        if (blocked.Length > 0) return new(false, blocked, [], []);
        SynchronizeItemSequence(); string operation = "player." + operationSequence;
        var result = command.Action switch
        {
            ProductionAction.StoreItem => progression.StoreItem(operation, command.ItemId, command.Id),
            ProductionAction.RetrieveItem => progression.RetrieveItem(operation, command.ItemId),
            ProductionAction.MoveStashedItem => progression.MoveStashedItem(operation, command.ItemId, command.Id),
            ProductionAction.RenameStashTab => progression.RenameStashTab(operation, command.Id, command.Value),
            _ => throw new InvalidDataException("Unknown stash action.")
        };
        if (result.Success) ProjectPermanentInventory();
        return new(result.Success, result.Reason, [], result.Events);
    }
}
