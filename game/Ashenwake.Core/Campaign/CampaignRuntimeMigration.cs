using System.Text.Json;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;

namespace Ashenwake.Core.Campaign;

/// <summary>Explicit safe-hub import; returns a new campaign character and never writes the source production save or profile.</summary>
public static class CampaignRuntimeMigration
{
    public static CampaignRuntimeSession ImportPhaseThree(string json, string baseCombatJson, string composedCombatJson,
        AdventureContent adventure, ProgressionContent policy, CampaignContent campaign)
    {
        if (json.Length > 64 * 1024 * 1024) throw new InvalidDataException("Production import exceeds its bounded size.");
        using var document = JsonDocument.Parse(json); ArchiveHeaders.Require(document.RootElement, 1);
        var state = ArchiveHeaders.Object(document.RootElement, "state"); ArchiveHeaders.Require(state, 1, "production.1");
        var expedition = ArchiveHeaders.Object(state, "expedition"); ArchiveHeaders.Require(expedition, 1, "expedition.1");
        var combat = ArchiveHeaders.Object(expedition, "combat"); ArchiveHeaders.Require(combat, 1, "combat.1");
        var character = ArchiveHeaders.Object(ArchiveHeaders.Object(state, "progression"), "character"); ArchiveHeaders.Require(character, 1);
        string sourceCombat = baseCombatJson;
        var sourcePolicy = policy;
        var catalog = ProductionContent.Resolve(sourceCombat, sourcePolicy, adventure);
        if (character.TryGetProperty("contentHash", out var identity) && identity.ValueKind == JsonValueKind.String && identity.GetString() != catalog.Hash &&
            LegendaryCatalogMigration.TryPrevious(baseCombatJson, policy, out var previousCombat, out var previousPolicy))
        {
            sourceCombat = previousCombat; sourcePolicy = previousPolicy;
            catalog = ProductionContent.Resolve(sourceCombat, sourcePolicy, adventure);
        }
        ArchiveHeaders.Identity(character, "contentHash", catalog.Hash);
        ArchiveHeaders.Identity(expedition, "adventureHash", ProductionContent.ResolveAdventure(sourceCombat, adventure).Hash);
        ArchiveHeaders.Identity(combat, "contentHash", Ashenwake.Core.Combat.CombatContent.Parse(sourceCombat).Identity);
        if (!document.RootElement.TryGetProperty("stateHash", out var hash) || hash.ValueKind != JsonValueKind.String || hash.GetString() != JsonData.Hash(state))
            throw new InvalidDataException("Original production import checksum mismatch.");
        // Authenticate the original field shape before new optional combat-rule fields receive their inactive defaults.
        var typed = JsonData.Read<ProductionSave>(json);
        var validated = ProductionSaveStore.Read(sourceCombat, adventure, sourcePolicy, JsonData.Write(new ProductionSave(1, JsonData.Hash(typed.State), typed.State)));
        var checkpoint = validated.Capture();
        if (checkpoint.Expedition.EncounterId != "hub" || checkpoint.Expedition.Adventure.RoomId != "room.greyhaven" || checkpoint.Expedition.Combat.EncounterId != "hub")
            throw new SaveCompatibilityException("Return to Greyhaven in the production build before importing this character.");
        return CampaignRuntimeSession.ImportProduction(composedCombatJson, adventure, policy, campaign, checkpoint);
    }
}
