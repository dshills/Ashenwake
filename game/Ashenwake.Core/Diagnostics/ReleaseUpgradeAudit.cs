using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Diagnostics;

/// <summary>Runs maintained prior-build hub archives through every supported upgrade without writing their sources.</summary>
public static class ReleaseUpgradeAudit
{
    public static void Run(string root, string kind, string original)
    {
        string Read(string path) => File.ReadAllText(ReleaseManifests.Resolve(root, path));
        string previousCombat = CampaignCombatContent.Parse(Read("fixtures/combat-phase4.json"), Read("fixtures/campaign-combat-phase4.json")).CombatJson;
        var adventure = AdventureContent.Parse(Read("content/adventure.json"));
        var progression = ProgressionContent.Parse(Read("content/progression.json"));
        var campaign = CampaignContent.Parse(Read("content/campaign.json"));
        var previousCampaign = CampaignContent.Parse(Read("fixtures/campaign-phase4.json"));
        var endgame = EndgameContent.Parse(Read("content/endgame.json"));
        string currentCombat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("content/combat.json"),
            Read("content/campaign-combat.json")).CombatJson, Read("content/endgame-combat.json"), endgame).CombatJson;
        if (kind is "PhaseFive" or "Endgame")
        {
            var originalSave = JsonData.Read<EndgameRuntimeSave>(original);
            var previousSession = EndgameRuntimeSaveStore.Read(currentCombat, adventure, progression, campaign, endgame, original);
            // The loader authenticates the old catalog and state before the additive upgrade.
            // Compare every logical field after replacing only the catalog identities.
            var rebound = OpeningCatalogMigration.Rebind(originalSave.State, currentCombat, adventure, progression, campaign);
            if (originalSave.StateHash != JsonData.Hash(originalSave.State) || previousSession.StateHash != JsonData.Hash(rebound) ||
                kind == "PhaseFive" && (!previousSession.InHub || previousSession.View.HighestClearedTier != 10 || previousSession.View.CompletedGodHunts != 5))
                throw new InvalidDataException("Prior exported endgame state changed during upgrade.");
            var restored = EndgameRuntimeSaveStore.Read(currentCombat, adventure, progression, campaign, endgame,
                JsonData.Write(new EndgameRuntimeSave(1, previousSession.StateHash, previousSession.Capture())));
            if (restored.StateHash != previousSession.StateHash) throw new InvalidDataException("Prior exported endgame round trip changed state.");
            return;
        }
        string campaignSave;
        if (kind is "PhaseTwo" or "PhaseThree")
        {
            string baseCombat = Read("fixtures/combat-phase3.json");
            var oldAdventure = AdventureContent.Parse(Read("fixtures/adventure-phase3.json"));
            var oldProgression = ProgressionContent.Parse(Read("fixtures/progression-phase3.json"));
            string productionSave = original;
            if (kind == "PhaseTwo")
            {
                var production = PhaseTwoMigration.Read(original, baseCombat, oldAdventure, oldProgression);
                productionSave = JsonData.Write(new ProductionSave(1, production.StateHash, production.Capture()));
            }
            var imported = CampaignRuntimeMigration.ImportPhaseThree(productionSave, baseCombat, previousCombat,
                oldAdventure, oldProgression, previousCampaign);
            campaignSave = JsonData.Write(new CampaignRuntimeSave(1, imported.StateHash, imported.Capture()));
        }
        else if (kind == "PhaseFour") campaignSave = original;
        else throw new InvalidDataException("Unsupported maintained application upgrade.");

        var before = CampaignRuntimeSaveStore.Read(previousCombat, adventure, progression, previousCampaign, campaignSave);
        var session = EndgameRuntimeMigration.ImportPhaseFour(campaignSave, previousCombat, currentCombat, adventure, progression, campaign, endgame);
        var previous = before.Production.Capture().Progression.Character;
        var current = session.Production.Capture().Progression.Character;
        if (JsonData.Hash(previous.Items) != JsonData.Hash(current.Items) || previous.Materials != current.Materials ||
            previous.Experience != current.Experience || JsonData.Hash(before.Capture().Campaign with { ContentHash = campaign.Hash }) != JsonData.Hash(session.Campaign.Capture().Campaign))
            throw new InvalidDataException("Upgrade changed earned inventory, materials, experience, or story.");
        var state = session.Capture();
        var roundTrip = EndgameRuntimeSaveStore.Read(currentCombat, adventure, progression, campaign, endgame,
            JsonData.Write(new EndgameRuntimeSave(1, session.StateHash, state)));
        if (roundTrip.StateHash != session.StateHash) throw new InvalidDataException("Current application archive changed on round trip.");
    }
}
