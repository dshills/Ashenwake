using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class ProductionHud
{
    public event Action<long, bool>? FavoriteRequested;
    public event Action<long, bool>? LockRequested;
    public event Action<long>? SalvageRequested;
    private ConfirmationDialog _salvageConfirmation = null!;
    private CombatSession? _salvageSession;
    private long _pendingSalvage, _salvageRevision;
    private string _salvageCharacter = "";
    private bool _salvagePauseHeld;

    public void ReportLootManagementResult(bool success, string reason)
        => Notice(reason.Length > 0 ? reason : success ? "Equipment updated." : "Equipment could not be changed.");

    private void BuildSalvageConfirmation()
    {
        _salvageConfirmation = new ConfirmationDialog
        {
            Name = "GearSalvageConfirmation",
            Title = "Permanently salvage item?",
            DialogAutowrap = true,
            OkButtonText = "Salvage permanently",
            CancelButtonText = "Keep item",
            Exclusive = true
        };
        AddChild(_salvageConfirmation);
        _salvageConfirmation.GetLabel().Name = "GearSalvageConfirmationPreview";
        _salvageConfirmation.Canceled += CancelSalvage;
        _salvageConfirmation.Confirmed += () =>
        {
            long id = _pendingSalvage;
            bool valid = id != 0 && SalvageContextMatches() && SalvageRestriction(id).Length == 0;
            CancelSalvage();
            if (valid) SalvageRequested?.Invoke(id);
        };
    }

    private void AddLootManagementControls(PermanentItem? candidate)
    {
        _rows.AddChild(new HSeparator());
        _rows.AddChild(Label("KEEP OR SALVAGE", 13));
        var protection = Label(candidate is null ? "Select an owned item to manage it." :
            $"{(candidate.IsFavorite ? "Favorite" : "Not favorited")} · {(candidate.IsLocked ? "Locked" : "Unlocked")}", 12);
        protection.Name = "GearItemProtection"; _rows.AddChild(protection);
        var row = new HBoxContainer(); _rows.AddChild(row);
        bool alive = _combat.Actors.Any(a => a.Id == 1 && a.Health > 0);
        var favorite = new Button
        {
            Name = "FavoriteItem",
            Text = candidate?.IsFavorite == true ? "Remove favorite" : "Favorite item",
            Disabled = candidate is null || !alive,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Favorites cannot be discarded, extracted, or salvaged. You can still equip and improve them."
        };
        var locked = new Button
        {
            Name = "LockItem",
            Text = candidate?.IsLocked == true ? "Unlock item" : "Lock item",
            Disabled = candidate is null || !alive,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Locked items cannot be discarded, extracted, or salvaged. Unlock and remove any favorite first."
        };
        favorite.AddThemeFontSizeOverride("font_size", 12); locked.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(favorite); row.AddChild(locked);
        favorite.Pressed += () => { if (candidate is not null) FavoriteRequested?.Invoke(candidate.Id, !candidate.IsFavorite); };
        locked.Pressed += () => { if (candidate is not null) LockRequested?.Invoke(candidate.Id, !candidate.IsLocked); };
        _rows.AddChild(Label("Favorite or lock valuable gear to protect it from destruction. Equipment changes and upgrades remain available.", 12));
        var presetNames = candidate is null ? [] : ProgressionSession.ItemPresetNames(_state, candidate.Id);
        var use = Label(presetNames.Length == 0 ? "Saved outfits: none." : "Saved outfits: " + string.Join(", ", presetNames), 12);
        use.Name = "GearItemPresetUsage"; _rows.AddChild(use);
        var preview = ProgressionSession.PreviewSalvage(_state, candidate?.Id ?? 0);
        string restriction = SalvageRestriction(candidate?.Id ?? 0);
        var yield = Label(preview.Success ? $"Salvage return: {preview.Materials} crafting materials. Affixes and upgrades do not increase this return." : preview.Reason, 12);
        yield.Name = "GearSalvagePreview"; _rows.AddChild(yield);
        var salvage = Button("Salvage selected item…", () => BeginSalvage(candidate!.Id));
        salvage.Name = "SalvageItem"; salvage.Disabled = restriction.Length > 0;
        salvage.TooltipText = restriction.Length > 0 ? restriction : $"Permanently exchange this item for exactly {preview.Materials} crafting materials.";
        if (restriction.Length > 0 && restriction != preview.Reason) _rows.AddChild(Label(restriction, 12));
    }

    private string SalvageRestriction(long id)
    {
        if (_state is null) return "Select an item you own.";
        if (!CanChangeGear) return "Stand near Torren in Greyhaven while alive to salvage an item.";
        var preview = ProgressionSession.PreviewSalvage(_state, id);
        return preview.Success ? "" : preview.Reason;
    }

    private static string PresetDestructionWarning(string[] names) => names.Length == 0 ? "" :
        "\n\nUsed by saved outfits: " + string.Join(", ", names) + ". Those outfits will be missing this item until you replace it and save them again.";

    private void BeginSalvage(long id)
    {
        CancelDiscard(); CancelSalvage();
        if (SalvageRestriction(id).Length > 0 || !IsVisibleInTree() || !_panel.Visible || _tab != "Gear") return;
        var item = _state.Character.Items.Single(i => i.Id == id);
        var preview = ProgressionSession.PreviewSalvage(_state, id);
        _pendingSalvage = id; _salvageRevision = _revision; _salvageCharacter = _state.Character.CharacterId;
        _salvageSession = _discardSandbox?.Session;
        _salvageConfirmation.DialogText = $"Salvage {ItemTitle(item)}?\n\nYou receive exactly {preview.Materials} crafting materials. " +
            "This permanently destroys the item, including its affixes and engraving. No property is learned. This cannot be undone." + PresetDestructionWarning(preview.PresetNames);
        _gearLoadout.CancelDrag();
        _discardSandbox?.SetModalPaused("gear-salvage", true); _salvagePauseHeld = _discardSandbox is not null;
        _salvageConfirmation.PopupCentered(new(Math.Min(600, (int)GetViewportRect().Size.X - 40), 330));
        _salvageConfirmation.GetCancelButton().GrabFocus();
    }

    private bool SalvageContextMatches() => IsVisibleInTree() && _panel.Visible && _tab == "Gear" &&
        _salvageCharacter == _state.Character.CharacterId && _salvageRevision == _revision && _gearItemId == _pendingSalvage &&
        ReferenceEquals(_salvageSession, _discardSandbox?.Session);

    private void SynchronizeSalvage()
    {
        if (_pendingSalvage != 0 && (!SalvageContextMatches() || SalvageRestriction(_pendingSalvage).Length > 0)) CancelSalvage();
    }

    private void CancelSalvage()
    {
        _pendingSalvage = 0; _salvageSession = null; _salvageConfirmation?.Hide();
        if (_salvagePauseHeld && _discardSandbox is not null && GodotObject.IsInstanceValid(_discardSandbox))
            _discardSandbox.SetModalPaused("gear-salvage", false);
        _salvagePauseHeld = false;
    }
}
