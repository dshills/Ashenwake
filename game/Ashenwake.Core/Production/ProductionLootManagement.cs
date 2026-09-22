using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    public static bool IsItemOrganizationAction(ProductionAction action) => action is ProductionAction.SetItemFavorite or ProductionAction.SetItemLocked;
    public ProductionResult SetItemFavorite(long itemId, bool value) => Execute(new(ProductionAction.SetItemFavorite, ItemId: itemId, Value: value ? "true" : "false"));
    public ProductionResult SetItemLocked(long itemId, bool value) => Execute(new(ProductionAction.SetItemLocked, ItemId: itemId, Value: value ? "true" : "false"));
    public ProductionResult Salvage(long itemId, bool confirmPermanent = false) => Execute(new(ProductionAction.Salvage, ItemId: itemId, ConfirmPermanent: confirmPermanent));
    public SalvagePreview PreviewSalvage(long itemId)
    {
        var preview = progression.PreviewSalvage(itemId);
        string blocked = SalvageServiceBlockedReason();
        return blocked.Length == 0 ? preview : preview with { Success = false, Reason = blocked };
    }
    private string SalvageServiceBlockedReason()
    {
        if (View.RoomId != "room.greyhaven") return "Salvage equipment at Greyhaven's workshops.";
        if (!Near("service.torren")) return "Visit Torren to salvage equipment.";
        if (!Combat.View.Actors.Any(actor => actor.Id == 1 && actor.Health > 0)) return "Cannot salvage equipment while defeated.";
        return "";
    }
    private ProductionResult ExecuteItemOrganization(ProductionCommand command)
    {
        if (!Combat.View.Actors.Any(actor => actor.Id == 1 && actor.Health > 0)) return new(false, "Cannot organize equipment while defeated.", [], []);
        if (command.Value is not ("true" or "false")) return new(false, "Choose true or false for item protection.", [], []);
        string operation = "player." + operationSequence;
        var result = command.Action == ProductionAction.SetItemFavorite
            ? progression.SetItemFavorite(operation, command.ItemId, command.Value == "true")
            : progression.SetItemLocked(operation, command.ItemId, command.Value == "true");
        // Flags change ownership metadata only. Re-projecting an active combat would be unnecessary.
        return new(result.Success, result.Reason, [], result.Events);
    }
}
