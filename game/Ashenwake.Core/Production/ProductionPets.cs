using Ashenwake.Core.Endgame;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    internal bool CanCollectPetMaterials => ExternalReceiptCapacityAvailable && progression.CharacterState.Materials <= 1000000 - PetCatalog.MaterialsPerCache;
    internal string[] GrantPetMaterials(string id)
    {
        if (PetCatalog.MaterialSource(id) is null || !CanCollectPetMaterials || progression.CharacterState.OperationReceipts.ContainsKey(id))
            throw new InvalidDataException("Pet material cache is unavailable.");
        var result = progression.EarnExperience(id, 0, PetCatalog.MaterialsPerCache);
        Require(result); ProjectPermanentInventory();
        return ["PetMaterialsCollected:" + id + ":" + PetCatalog.MaterialsPerCache];
    }
    internal void ValidatePetMaterialReceipts(string[] ids)
    {
        var receipts = progression.CharacterState.OperationReceipts;
        if (!ids.ToHashSet(StringComparer.Ordinal).SetEquals(receipts.Keys.Where(k => k.StartsWith("pet.materials.", StringComparison.Ordinal))) ||
            ids.Any(id => !HasCampaignReward(id, 0, PetCatalog.MaterialsPerCache)))
            throw new InvalidDataException("Pet material cache ledger differs from permanent receipts.");
    }
}
