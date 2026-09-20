using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class AppearanceSmoke
{
    private int _discards, _discardDialogClicks;

    private async Task IncompatibleDiscardInspection(long id)
    {
        await ClickGearControl(Find<Control>("GearInventoryItem" + id)); Refresh(); await Frames();
        var choice = Find<OptionButton>("GearItem");
        Check("discard_can_select_wrong_discipline_item_without_enabling_equip", choice.GetItemMetadata(choice.Selected).AsInt64() == id &&
            !Find<Button>("DiscardItem").Disabled && Find<Button>("EquipItem").Disabled);
    }

    private async Task DiscardFlow()
    {
        WalkTo("service.torren"); Refresh(); _hud.PresentInteraction("ServiceOpened:service.torren"); await Frames();
        var character = _session.Capture().Progression.Character;
        var item = character.Items.First(i => i.Id != _initialMainHand && i.Rarity != ItemRarity.Godwrought && !character.Equipment.Values.Contains(i.Id));
        await ClickGearControl(Find<Control>("GearInventoryItem" + item.Id));
        string before = _session.StateHash; int history = _session.CaptureReplay().Frames.Length;
        await ClickGearControl(Find<Button>("DiscardItem"));
        var confirmation = Find<ConfirmationDialog>("GearDiscardConfirmation");
        Check("discard_dialog_discloses_selected_item_permanent_loss_and_no_reward", confirmation.Visible &&
            confirmation.DialogText.Contains("#" + item.Id) && confirmation.DialogText.Contains("cannot be undone") && confirmation.DialogText.Contains("no materials"));
        Check("discard_confirmation_holds_only_its_own_pause", DragPauseOwners.Contains("gear-discard") && DragPauseOwners.Contains("session") && _sandbox.IsPaused);
        Check("discard_confirmation_defaults_to_keep_item", confirmation.GuiGetFocusOwner() == confirmation.GetCancelButton());
        await Capture("discard-confirmation.png");
        await ClickDiscardDialog(confirmation.GetCancelButton());
        GD.Print(JsonData.Write(new
        {
            kind = "DiscardCancelDiagnostic",
            visible = confirmation.Visible,
            sameState = _session.StateHash == before,
            historyBefore = history,
            historyAfter = _session.CaptureReplay().Frames.Length,
            discards = _discards,
            pauseOwners = DragPauseOwners.ToArray()
        }));
        Check("discard_cancel_closes_dialog", !confirmation.Visible);
        Check("discard_cancel_preserves_item_and_history", _session.StateHash == before && _session.CaptureReplay().Frames.Length == history && _discards == 0);
        Check("discard_cancel_releases_only_its_pause", !DragPauseOwners.Contains("gear-discard") && DragPauseOwners.Contains("session"));

        await ClickGearControl(Find<Button>("DiscardItem"));
        long head = EquipmentId(EquipmentSlot.Head);
        Require(_session.Unequip(EquipmentSlot.Head)); Refresh(); await Frames();
        Check("discard_other_transaction_cancels_stale_confirmation", !confirmation.Visible && _discards == 0 && !DragPauseOwners.Contains("gear-discard"));
        Require(_session.Equip(head, EquipmentSlot.Head)); Refresh(); await Frames();

        await ClickGearControl(Find<Control>("GearInventoryItem" + item.Id));
        await ClickGearControl(Find<Button>("DiscardItem")); _hud.Close(); await Frames();
        Check("discard_close_cancels_confirmation_and_preserves_session_pause", !confirmation.Visible && _discards == 0 &&
            !DragPauseOwners.Contains("gear-discard") && DragPauseOwners.Contains("session"));
        _hud.PresentInteraction("ServiceOpened:service.torren"); await Frames();
        await ClickGearControl(Find<Control>("GearInventoryItem" + item.Id));
        int count = _session.Capture().Progression.Character.Items.Length, materials = _session.ProgressionView.Materials;
        await ClickGearControl(Find<Button>("DiscardItem")); await ClickDiscardDialog(confirmation.GetOkButton());
        Check("discard_native_confirm_commits_once_and_removes_card", !confirmation.Visible && _discards == 1 &&
            _session.Capture().Progression.Character.Items.Length == count - 1 && !HasInventoryCard(item.Id) &&
            !_session.Combat.View.Inventory.Any(owned => owned.Id == item.Id) && _session.ProgressionView.Materials == materials);
        Check("discard_commit_preserves_equipped_appearance_and_other_pause_owner", ModelsAgree() &&
            !DragPauseOwners.Contains("gear-discard") && DragPauseOwners.Contains("session") && _sandbox.IsPaused);
        await Capture("discard-complete.png");
        await DragSaveReplay("discard-character");
        _hud.Close();
    }

    private async Task ClickDiscardDialog(Button button)
    {
        var window = button.GetWindow(); var viewport = button.GetViewport(); Vector2 point = button.GetGlobalRect().GetCenter();
        bool embedded = window.IsEmbedded();
        if (embedded)
        {
            // Use the embedder's actual window routing, which supplies mouse entry and
            // transforms coordinates before handing the input to the dialog's viewport.
            viewport = window.GetParent().GetViewport(); point += window.Position;
        }
        else viewport.NotifyMouseEntered(); // Required for manually forwarded viewport mouse input.
        int guiButtons = 0, activated = 0;
        void OnGuiInput(InputEvent input) { if (input is InputEventMouseButton) guiButtons++; }
        void OnPressed() => activated++;
        button.GuiInput += OnGuiInput; button.Pressed += OnPressed;
        try
        {
            viewport.PushInput(new InputEventMouseMotion { Position = point }, true); await Frames(1);
            string hovered = window.GuiGetHoveredControl()?.Name.ToString() ?? "none";
            foreach (bool pressed in new[] { true, false })
                viewport.PushInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Position = point,
                    Pressed = pressed,
                    ButtonMask = pressed ? MouseButtonMask.Left : 0
                }, true);
            await Frames();
            GD.Print(JsonData.Write(new
            {
                kind = "DiscardDialogClickDiagnostic",
                button = button.Text,
                embedded,
                point = point.ToString(),
                hovered,
                guiButtons,
                activated,
                dialogVisible = window.Visible
            }));
            Check("discard_dialog_click_" + ++_discardDialogClicks + "_reaches_selected_button", guiButtons == 2 && activated == 1);
        }
        finally { button.GuiInput -= OnGuiInput; button.Pressed -= OnPressed; }
    }
}
