using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Endgame;

/// <summary>Explicit safe-hub upgrade. Reading and validating the old archive precedes any new content binding.</summary>
public static class EndgameRuntimeMigration
{
    public static EndgameRuntimeSession ImportPhaseFour(string sourceJson, string previousComposedCombatJson,
        string currentComposedCombatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame)
    {
        var sourceCampaign = OpeningCatalogMigration.CampaignForCombat(previousComposedCombatJson, campaign);
        var previous = CampaignRuntimeSaveStore.Read(previousComposedCombatJson, adventure, policy, sourceCampaign, sourceJson);
        if (!previous.InHub) throw new SaveCompatibilityException("Return to Greyhaven in Phase 4 before explicitly importing into the endgame build.");
        var source = previous.Capture();
        var rebased = OpeningCatalogMigration.Rebind(source, currentComposedCombatJson, adventure, EndgameProgression.Resolve(policy), campaign);
        return EndgameRuntimeSession.ImportCampaign(currentComposedCombatJson, adventure, policy, campaign, endgame, rebased);
    }
}
