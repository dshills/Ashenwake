using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class ProductionHud
{
    private ConfirmationDialog _discardConfirmation = null!;
    private Sandbox? _discardSandbox;
    private CombatSession? _discardSession;
    private long _pendingDiscard, _discardRevision;
    private string _discardCharacter = "";
    private bool _discardPauseHeld;

    private void BuildDiscardConfirmation()
    {
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _discardSandbox = sandbox; break; }
        _discardConfirmation = new ConfirmationDialog
        {
            Name = "GearDiscardConfirmation",
            Title = "Permanently discard item?",
            DialogAutowrap = true,
            OkButtonText = "Discard permanently",
            CancelButtonText = "Keep item",
            Exclusive = true
        };
        AddChild(_discardConfirmation);
        _discardConfirmation.Canceled += CancelDiscard;
        _discardConfirmation.Confirmed += () =>
        {
            long id = _pendingDiscard;
            bool valid = id != 0 && DiscardContextMatches() && DiscardRestriction(id).Length == 0;
            CancelDiscard();
            if (valid) DiscardRequested?.Invoke(id);
        };
    }

    private void AddDiscardControls(PermanentItem? candidate)
    {
        int count = CharacterStash.BackpackCount(_state.Character);
        _rows.AddChild(Label(count >= CharacterStash.BackpackCapacity
            ? $"Inventory full · {count} carried. Store items in Greyhaven, or salvage or discard unequipped gear at Torren to collect more loot."
            : $"{count}/{CharacterStash.BackpackCapacity} items carried · stored gear does not use inventory space.", 12));
        string reason = DiscardRestriction(candidate?.Id ?? 0);
        var discard = Button("Discard selected item…", () => BeginDiscard(candidate!.Id));
        discard.Name = "DiscardItem"; discard.Disabled = reason.Length > 0;
        discard.TooltipText = reason.Length > 0 ? reason : "Permanently destroy the selected item. No materials or rewards are granted.";
        if (reason.Length > 0) _rows.AddChild(Label(reason, 12));
    }

    private string DiscardRestriction(long id)
    {
        if (_state is null) return "Select an item you own.";
        if (!CanChangeGear) return "Stand near Torren in Greyhaven while alive to discard an item.";
        return ProgressionSession.DiscardBlockedReason(_state, id);
    }

    private void BeginDiscard(long id)
    {
        CancelSalvage(); CancelDiscard();
        if (DiscardRestriction(id).Length > 0 || !IsVisibleInTree() || !_panel.Visible || _tab != "Gear") return;
        var item = _state.Character.Items.Single(i => i.Id == id);
        _pendingDiscard = id; _discardRevision = _revision; _discardCharacter = _state.Character.CharacterId;
        _discardSession = _discardSandbox?.Session;
        _discardConfirmation.DialogText = $"Discard {ItemTitle(item)}?\n\nThis permanently destroys this item, including its affixes and engraving. " +
            (item.Rarity == ItemRarity.Godwrought ? "This copy's burning kills and evolution will also be lost. " : "") +
            "You receive no materials or other rewards. This cannot be undone." + PresetDestructionWarning(ProgressionSession.ItemPresetNames(_state, id));
        _gearLoadout.CancelDrag();
        _discardSandbox?.SetModalPaused("gear-discard", true); _discardPauseHeld = _discardSandbox is not null;
        _discardConfirmation.PopupCentered(new(Math.Min(600, (int)GetViewportRect().Size.X - 40), 330));
        _discardConfirmation.GetCancelButton().GrabFocus();
    }

    private bool DiscardContextMatches() => IsVisibleInTree() && _panel.Visible && _tab == "Gear" &&
        _discardCharacter == _state.Character.CharacterId && _discardRevision == _revision &&
        ReferenceEquals(_discardSession, _discardSandbox?.Session);

    private void SynchronizeDiscard()
    {
        if (_pendingDiscard != 0 && (!DiscardContextMatches() || DiscardRestriction(_pendingDiscard).Length > 0)) CancelDiscard();
    }

    private void CancelDiscard()
    {
        _pendingDiscard = 0; _discardSession = null; _discardConfirmation?.Hide();
        if (_discardPauseHeld && _discardSandbox is not null && GodotObject.IsInstanceValid(_discardSandbox))
            _discardSandbox.SetModalPaused("gear-discard", false);
        _discardPauseHeld = false;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut && IsInsideTree()) { CancelDiscard(); CancelSalvage(); }
    }
}
