namespace Ashenwake.Core.Progression;

public sealed partial class ProgressionSession
{
    internal ProgressionResult SpendEndgameMaterials(string operationId, int amount, object purpose)
        => Change(operationId, new { Action = "EndgameSpend", amount, purpose }, (next, events) =>
        {
            if (next.Character.Endgame is null || amount is < 1 or > 10000 || next.Character.Materials < amount)
                return "Insufficient permanent materials for this endgame transaction.";
            next.Character.Materials -= amount; events.Add("EndgameMaterialsSpent:" + amount); return null;
        });

    internal ProgressionResult GrantEndgameCatalyst(string operationId, string id, int amount)
        => Change(operationId, new { Action = "EndgameCatalyst", id, amount }, (next, events) =>
        {
            if (next.Character.Endgame is null || !EndgameProgression.CatalystIds.Contains(id) || amount is < 1 or > 100)
                return "Unknown catalyst or invalid permanent reward.";
            var inventory = next.Character.Endgame.Catalysts;
            inventory[id] = Math.Min(1000000, inventory.GetValueOrDefault(id) + amount);
            events.Add("EndgameCatalystGranted:" + id + ":" + amount); return null;
        });

    private static string? SpendEndgameCraftCatalyst(ProgressionState state, PermanentItem? item, CraftingRequest request, ref int cost)
    {
        if (state.Endgame is null) return request.CatalystId is null ? null : "This character has not entered the endgame crafting policy.";
        string? required = request.Service == CraftingService.DivineGrafting ? request.Lineage switch
        {
            "Serath" => "material.serath_memory",
            "Orrun" => "material.orrun_oath",
            _ => null
        } : null;
        if (required is not null && request.CatalystId != required) return "This permanent graft requires the explicitly selected " + required + ".";
        if (request.CatalystId is null) return null;
        bool temper = request.Service == CraftingService.Tempering && item?.Rarity == ItemRarity.Godwrought && request.AffixId == "affix.damage";
        if (!temper && required is null) return "This recipe does not accept an endgame catalyst.";
        if (!EndgameProgression.CatalystIds.Contains(request.CatalystId) || !state.Endgame.Catalysts.TryGetValue(request.CatalystId, out int count) || count < 1)
            return "The selected catalyst is not owned.";
        if (state.Endgame.SpentCatalysts.GetValueOrDefault(request.CatalystId) >= 1000000) return "Catalyst ledger capacity reached.";
        if (count == 1) state.Endgame.Catalysts.Remove(request.CatalystId); else state.Endgame.Catalysts[request.CatalystId] = count - 1;
        state.Endgame.SpentCatalysts[request.CatalystId] = state.Endgame.SpentCatalysts.GetValueOrDefault(request.CatalystId) + 1;
        if (temper) cost = 0;
        return null;
    }
}
