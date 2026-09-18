using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
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
        var previous = CampaignRuntimeSaveStore.Read(previousComposedCombatJson, adventure, policy, campaign, sourceJson);
        if (!previous.InHub) throw new SaveCompatibilityException("Return to Greyhaven in Phase 4 before explicitly importing into the endgame build.");
        var source = previous.Capture();
        var resolved = ProductionContent.Resolve(currentComposedCombatJson, CampaignRuntimeSession.ResolvePolicy(EndgameProgression.Resolve(policy), campaign), adventure);
        string identity = CombatContent.Parse(currentComposedCombatJson).Identity;
        string adventureHash = ProductionContent.ResolveAdventure(currentComposedCombatJson, adventure).Hash;
        var production = source.Production with
        {
            Expedition = source.Production.Expedition with
            {
                AdventureHash = adventureHash,
                Combat = source.Production.Expedition.Combat with { ContentHash = identity }
            },
            Progression = source.Production.Progression with
            {
                Character = source.Production.Progression.Character with { ContentHash = resolved.Hash }
            }
        };
        var rebased = source with { Production = production, Combat = source.Combat with { ContentHash = identity } };
        return EndgameRuntimeSession.ImportCampaign(currentComposedCombatJson, adventure, policy, campaign, endgame, rebased);
    }
}
