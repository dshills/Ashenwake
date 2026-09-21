using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class AppearanceSmoke
{
    private async Task EquipmentDescriptionChecks()
    {
        var ids = CombatContent.Parse(Read("combat")).Items.Select(i => i.Id).ToArray();
        Check("every_equipment_definition_has_distinct_lore", ids.All(id => EquipmentDetails.Lore(id).Length > 0) &&
            ids.Select(EquipmentDetails.Lore).Distinct(StringComparer.Ordinal).Count() == ids.Length);
        var content = _session.Content.Capture();
        Check("every_authored_property_has_a_power_explanation", content.Properties.All(p => EquipmentDetails.Power(p.Id).Contains('\n')));
        Check("ground_loot_uses_authored_item_properties", content.Items.All(i => EquipmentDetails.InnateProperty(i.Id) == i.Property));
        string hash = _session.StateHash;
        // Detached UI fixtures exercise the longest supported comparisons, without awakening
        // a real item, learning an engraving, or changing the character's progression.
        var state = _session.Capture().Progression;
        var original = state.Character.Items.Single(i => i.Id == state.Character.Equipment[EquipmentSlot.MainHand]);
        var current = original with { BurningKills = 1000, Evolution = "Serath", Engraving = "rune.guard" };
        var candidate = current with { Id = 990001, Evolution = "Orrun" };
        var projection = state with { Character = state.Character with { Items = state.Character.Items.Select(i => i.Id == current.Id ? current : i).Append(candidate).ToArray() } };
        var comparison = new GearComparison { Position = new(16, 16) }; _sandbox.AddOverlay(comparison);
        try
        {
            GetWindow().ContentScaleSize = GetWindow().Size = new(780, 720);
            comparison.SetItems(projection, content, candidate, EquipmentSlot.MainHand, ""); await Frames(6);
            // The shipping inventory sizes its overlay from the settled minimum each frame.
            // This detached overlay needs that same step after multiline labels finish wrapping.
            comparison.Size = comparison.GetCombinedMinimumSize(); await Frames(3);
            await Capture("equipment-evolved-power-descriptions.png");
            Check("evolved_and_engraved_comparison_fits_720p", GetViewport().GetVisibleRect().Encloses(comparison.GetGlobalRect()));
            Check("evolution_and_engraving_show_trigger_and_effect", comparison.ComparisonText.Contains(EquipmentDetails.Power("rune.guard"), StringComparison.Ordinal) &&
                comparison.ComparisonText.Contains(EquipmentDetails.Evolution("Serath"), StringComparison.Ordinal) && comparison.ComparisonText.Contains(EquipmentDetails.Evolution("Orrun"), StringComparison.Ordinal));
            Check("equipment_lore_inspection_keeps_saved_state", _session.StateHash == hash);
            foreach (var definition in content.Items.Where(i => LegendaryEquipment.IsItem(i.Id)))
            {
                var legendary = original with
                {
                    Id = 990002,
                    DefinitionId = definition.Id,
                    Rarity = ItemRarity.Legendary,
                    Evolution = "",
                    Engraving = "rune.guard",
                    BurningKills = 0,
                    Affixes = [],
                    BaseDamage = definition.BaseDamage,
                    BaseArmor = definition.BaseArmor,
                    BaseCriticalBasisPoints = definition.BaseCriticalBasisPoints
                };
                comparison.SetItems(state, content, legendary, definition.Slots[0], ""); await Frames(6);
                comparison.Size = comparison.GetCombinedMinimumSize(); await Frames(3);
                Check("legendary_description_fits_720p_" + definition.Id, GetViewport().GetVisibleRect().Encloses(comparison.GetGlobalRect()));
                Check("legendary_description_explains_power_and_source_" + definition.Id,
                    comparison.ComparisonText.Contains(EquipmentDetails.Power(definition.Property), StringComparison.Ordinal) &&
                    EquipmentDetails.Source(definition.Id).Length > 0 && comparison.ComparisonText.Contains(EquipmentDetails.Source(definition.Id), StringComparison.Ordinal));
                await Capture("equipment-" + definition.Id[5..] + "-description.png");
            }
        }
        finally { comparison.GetParent().RemoveChild(comparison); comparison.QueueFree(); }
        var beforeGraft = state with { Character = state.Character with { Materials = 200, Items = state.Character.Items.Select(i => i.Id == current.Id ? current with { Evolution = "" } : i).ToArray() } };
        var afterGraft = beforeGraft with { Character = beforeGraft.Character with { Materials = 200 - content.CraftingCosts[CraftingService.DivineGrafting], Items = beforeGraft.Character.Items.Select(i => i.Id == current.Id ? i with { Evolution = "Orrun" } : i).ToArray() } };
        var preview = new CraftingPreview(true, "", true, beforeGraft, afterGraft);
        var request = new CraftingRequest("", CraftingService.DivineGrafting, current.Id, Lineage: "Orrun");
        var dialog = new ConfirmationDialog
        {
            Title = "Evolution confirmation · presentation fixture",
            DialogAutowrap = true,
            DialogText = CraftingWorkbench.ConfirmationText(preview, request, content),
            OkButtonText = "Commit craft",
            CancelButtonText = "Keep current item"
        };
        AddChild(dialog);
        try
        {
            dialog.PopupCentered(new(560, 320)); await Frames(6);
            Check("permanent_evolution_confirmation_fits_720p", GetViewport().GetVisibleRect().Encloses(new Rect2(dialog.Position, dialog.Size)));
            Check("permanent_evolution_confirmation_explains_power_and_exact_cost", dialog.DialogText.Contains(EquipmentDetails.Evolution("Orrun"), StringComparison.Ordinal) &&
                dialog.DialogText.Contains($"200 → {afterGraft.Character.Materials}", StringComparison.Ordinal));
            await Capture("equipment-evolution-confirmation.png");
            Check("evolution_confirmation_fixture_does_not_submit_a_craft", _session.StateHash == hash);
        }
        finally { dialog.Hide(); dialog.Free(); }
        GetWindow().ContentScaleSize = GetWindow().Size = new(1280, 800); await Frames(5);
    }
}
