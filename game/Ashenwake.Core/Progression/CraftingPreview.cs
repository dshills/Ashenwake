using System.Globalization;

namespace Ashenwake.Core.Progression;

/// <summary>A detached projection of one recipe, including its authoritative costs and restrictions.</summary>
public sealed record CraftingPreview(bool Success, string Reason, bool RequiresConfirmation, ProgressionSnapshot Before, ProgressionSnapshot After);

public sealed partial class ProgressionSession
{
    public CraftingPreview PreviewCraft(CraftingRequest request)
    {
        var before = Capture();
        var projection = Restore(content, before);
        string operationId = "craft.preview";
        // At most the bounded receipt count can collide. Previewing must never replay an existing receipt.
        for (int suffix = 0; before.Character.OperationReceipts.ContainsKey(operationId); suffix++)
            operationId = "craft.preview." + suffix.ToString(CultureInfo.InvariantCulture);
        var result = projection.Craft(request with { OperationId = operationId, ConfirmPermanent = true });
        // Craft owns every recipe rule and rolls back its isolated transaction on failure. Neither snapshot
        // aliases live state, and the hypothetical operation receipt exists only in the returned projection.
        return new(result.Success, result.Reason, request.Service is CraftingService.Extraction or CraftingService.DivineGrafting,
            before, projection.Capture());
    }
}
