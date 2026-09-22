using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Campaign;

/// <summary>Diagnostic-only public-input policy. It never grants progression or mutates snapshots.</summary>
public sealed class CampaignBalancePolicy(bool mainPath = false, bool managedBuild = false)
{
    public const string Version = "campaign-balance-policy.1";
    public bool MainPath => mainPath;
    public bool ManagedBuild => managedBuild;
    public string Name => managedBuild ? "earned-build" : "smoke-reference";
    public string Description => "Fresh character, no shared profile. Uses CampaignRuntimeSmoke public combat, loot, choice and route commands, including its starting Spine/Arms anatomy and Stone Memory manifestation. " +
        (mainPath ? "Main encounters only; optional events skipped. " : "All main and optional encounters. ") +
        (managedBuild ? "Return to Greyhaven between acts; spend earned passive points alternating Offense/Defense; equip owned compatible raw-stat upgrades at Torren. Gear score = damage*100 + armor + critical basis points + affix.damage*100 + affix.armor + affix.critical. Strict improvements only, lower item ID breaks ties; never move gear already equipped in another slot or displace an offhand for a two-handed weapon. No crafting, respec, mutation selection or material spending." :
        "Keep starting equipment and leave passive points unspent; no crafting, respec or mutation selection. This is the existing engineering reference route, not an optimized player build.");
    public string Hash => JsonData.Hash(new { Version, Name, MainPath, Description });
    public bool Complete(CampaignRuntimeSession session) => mainPath
        ? session.InHub && session.View.Ending is not null && session.Capture().Campaign.CompletedActs.Count == 5
        : CampaignRuntimeSmoke.Complete(session);

    public CampaignRuntimeCommand Next(CampaignRuntimeSession session)
    {
        if (managedBuild && session.InHub && ManagedCommand(session) is { } service) return service;
        var command = mainPath ? MainPathCommand(session) : CampaignRuntimeSmoke.Next(session);
        if (managedBuild && !session.InHub && command.Action == CampaignRuntimeAction.EnterAct)
            return new(CampaignRuntimeAction.ReturnToHub);
        return command;
    }

    private static CampaignRuntimeCommand? ManagedCommand(CampaignRuntimeSession session)
    {
        var view = session.Production.ProgressionView;
        if (view.AvailablePassivePoints > 0)
        {
            int spent = view.Level - 1 - view.AvailablePassivePoints;
            return CampaignRuntimeSmoke.AtInteraction(session, "service.mara", new(CampaignRuntimeAction.Production,
                Production: new(ProductionAction.Passive, Id: spent % 2 == 0 ? "Offense" : "Defense")));
        }
        if (!session.Interactions.Any(i => i.ActionId == "service.torren")) return null;
        var state = session.Production.Capture().Progression.Character;
        var definitions = session.Production.Content.Capture().Items.ToDictionary(i => i.Id);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var current = state.Equipment.TryGetValue(slot, out long currentId) ? state.Items.Single(i => i.Id == currentId) : null;
            var best = state.Items.Where(i => !state.Equipment.Values.Contains(i.Id)).Where(i =>
            {
                var definition = definitions[i.DefinitionId];
                return definition.Slots.Contains(slot) && (definition.Disciplines.Length == 0 || definition.Disciplines.Contains(view.Discipline)) &&
                    !(definition.Hands == 2 && state.Equipment.ContainsKey(EquipmentSlot.OffHand)) &&
                    !(slot == EquipmentSlot.OffHand && state.Equipment.TryGetValue(EquipmentSlot.MainHand, out long main) && definitions[state.Items.Single(item => item.Id == main).DefinitionId].Hands == 2);
            }).Where(i => current is null || GearScore(i) > GearScore(current)).OrderByDescending(GearScore).ThenBy(i => i.Id).FirstOrDefault();
            if (best is not null)
                return CampaignRuntimeSmoke.AtInteraction(session, "service.torren", new(CampaignRuntimeAction.Production,
                    Production: new(ProductionAction.Equip, ItemId: best.Id, Slot: slot)));
        }
        return null;
    }

    private static long GearScore(PermanentItem item) => item.BaseDamage * 100L + item.BaseArmor + item.BaseCriticalBasisPoints +
        item.Affixes.GetValueOrDefault("affix.damage") * 100L + item.Affixes.GetValueOrDefault("affix.armor") + item.Affixes.GetValueOrDefault("affix.critical");

    private static CampaignRuntimeCommand MainPathCommand(CampaignRuntimeSession session)
    {
        if (session.InHub || !session.EncounterCleared || session.Combat.View.Loot.Count > 0 && session.Combat.View.Inventory.Count < 512)
            return CampaignRuntimeSmoke.Next(session);
        var state = session.Capture().Campaign; var definition = session.Content.Capture(); var act = definition.Acts[state.CurrentAct - 1];
        var choice = definition.Choices.Single(c => c.Id == act.RequiredChoice);
        if (!state.Choices.ContainsKey(choice.Id) && state.CompletedEncounters.Contains(choice.RequiredEncounter))
            return new(CampaignRuntimeAction.Choose, Id: choice.Id, Value: choice.Outcomes[0].Id);
        if (state.CompletedActs.Contains(state.CurrentAct))
            return state.CurrentAct == 5 ? new(CampaignRuntimeAction.ReturnToHub) : new(CampaignRuntimeAction.EnterAct, Act: state.CurrentAct + 1);
        var passage = session.Interactions.FirstOrDefault(i => i.ActionId.Contains(".forward.", StringComparison.Ordinal));
        if (passage is null) return new(CampaignRuntimeAction.AdvanceEncounter); // The original Act I uses AdvanceEncounter for first entry.
        var action = state.CurrentAct switch
        {
            1 => CampaignRuntimeAction.InteractOpening,
            2 => CampaignRuntimeAction.InteractVerdant,
            3 => CampaignRuntimeAction.InteractCinder,
            4 => CampaignRuntimeAction.InteractSpine,
            _ => CampaignRuntimeAction.InteractHollow
        };
        return CampaignRuntimeSmoke.AtInteraction(session, passage.ActionId, new(action, Id: passage.ActionId));
    }
}
