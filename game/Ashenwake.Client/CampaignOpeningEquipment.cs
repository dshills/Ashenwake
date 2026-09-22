using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Optional opening guidance follows actual loot ownership and equipment, with no tutorial save state.</summary>
public partial class CampaignHud
{
    public event Action? OpeningEquipmentRequested;
    private ProgressionSnapshot? _openingEquipment;
    private bool OpeningRewardContext => _state.HighestActVisited <= 1;
    private PermanentItem? OpeningPyreItem => OpeningRewardContext
        ? _openingEquipment?.Character.Items.FirstOrDefault(i => i.DefinitionId == LegendaryEquipment.Pyre) : null;
    private bool OpeningPyreEquipped => OpeningRewardContext && _openingEquipment?.Character.Items.Any(i =>
        i.DefinitionId == LegendaryEquipment.Pyre && _openingEquipment.Character.Equipment.Values.Contains(i.Id)) == true;
    private bool OpeningPyreOnGround => OpeningRewardContext && _combat.Loot.Any(l => l.Item.DefinitionId == LegendaryEquipment.Pyre);

    private void InspectOpeningEquipment()
    {
        if (OpeningPyreItem is null) return;
        SetOpen(false); OpeningEquipmentRequested?.Invoke();
    }

    public void ReturnForOpeningEquipment() => RequestJourneyTravel(new(JourneyTravelKind.Hub));

    private void OpeningEquipmentMapAction()
    {
        if (!OpeningPyreOnGround && OpeningPyreItem is null) return;
        _rows.AddChild(Label("PYREBOUND TREADS · OPTIONAL BUILD UPGRADE", 15));
        var lesson = Label(EquipmentDetails.Power(LegendaryEquipment.PyrePower), 12);
        lesson.Name = "OpeningPyrePower"; _rows.AddChild(lesson);
        if (OpeningPyreOnGround)
            _rows.AddChild(Label("These boots are still on the ground. Close the map and click the drop to collect it. " +
                (RoomLootRetained ? "This cleared room keeps remaining drops when you travel." : "Travel leaves remaining drops behind."), 12));
        if (OpeningPyreItem is not null)
        {
            _rows.AddChild(Label(OpeningPyreEquipped
                ? "Equipped: use Dodge in an open direction to leave burning ground, then draw enemies through the patches. A dodge blocked by scenery leaves no trail."
                : "Inspect the power, then visit Torren in Greyhaven. Drag the boots from inventory onto the Boots slot to equip them. This upgrade is optional.", 12));
            var inspect = Button("Inspect Pyrebound Treads", InspectOpeningEquipment); inspect.Name = "InspectOpeningPyre";
        }
        _rows.AddChild(new HSeparator());
    }
}
