using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class ProductionHud
{
    public event Action? OpeningEquipmentReturnRequested, OpeningEquipmentVisitRequested;
    private bool _openingRewards;

    public void InspectOpeningReward()
    {
        var item = _state?.Character.Items.FirstOrDefault(i => i.DefinitionId == LegendaryEquipment.Pyre);
        if (item is null) return;
        _gearLoadout.CancelDrag(); _gearSlot = EquipmentSlot.Boots; _gearItemId = item.Id; _gearInspecting = true;
        _tab = "Gear"; Visible = true; _panel.Show(); Rebuild(true); _scroll.ScrollVertical = 0; _tabs["Gear"].GrabFocus();
        if (_gearLoadout.RevealOwnedItem(item.Id)) Notice("Inventory filters cleared to show Pyrebound Treads.");
    }

    private void OpeningEquipmentLesson()
    {
        if (!_openingRewards || !_state.Character.Items.Any(i => i.DefinitionId == LegendaryEquipment.Pyre)) return;
        bool equipped = _state.Character.Items.Any(i => i.DefinitionId == LegendaryEquipment.Pyre && _state.Character.Equipment.Values.Contains(i.Id));
        string instructions = equipped
            ? "Try Dodge in an open direction, then draw enemies through the burning patches. A dodge blocked by scenery leaves no trail."
            : CanChangeGear ? "Drag Pyrebound Treads from inventory onto Boots. Drag them back to inventory to remove them."
            : _inTown ? "Walk to Torren to equip these boots. You can inspect their power here."
            : "Return to Torren in Greyhaven to equip these boots. Continuing your journey is also available.";
        var lesson = Label("PYREBOUND TREADS · " + (equipped ? "EQUIPPED" : "OPTIONAL UPGRADE") + "\n" +
            EquipmentDetails.Power(LegendaryEquipment.PyrePower) + "\n" + instructions, 12);
        lesson.Name = "OpeningGearLesson"; _rows.AddChild(lesson);
        if (equipped || CanChangeGear) return;
        bool alive = _combat.Actors.Any(a => a.Id == 1 && a.Health > 0);
        var action = Button(_inTown ? "Walk to Torren · equip the boots" : "Return to Greyhaven · equip the boots", () =>
        {
            if (!_combat.Actors.Any(a => a.Id == 1 && a.Health > 0)) return;
            Close();
            if (_inTown) OpeningEquipmentVisitRequested?.Invoke();
            else OpeningEquipmentReturnRequested?.Invoke();
        });
        action.Name = _inTown ? "OpeningGearVisitTorren" : "OpeningGearReturn"; action.Disabled = !alive;
    }
}
