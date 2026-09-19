using Ashenwake.Core.Adventure;
using Godot;

namespace Ashenwake.Client;

public partial class CampaignHud
{
    private int _anatomySiblingIndex = -1;
    private Vector2 _anatomyLayoutViewport = new(-1, -1);
    private bool _anatomyLayoutActive;
    // A contextual invitation during the opening act, never a new travel/progression gate.
    private bool FirstHeartAvailable => _state.HighestActVisited <= 1 && _state.CompletedActs.Contains(1) &&
        _anatomy.OwnedFragments.Contains(_anatomyContent.RewardFragment) && !_anatomy.Anatomy.Values.Contains(_anatomyContent.RewardFragment);

    public void OpenAnatomyReward()
    { OpenTab("Anatomy"); _anatomyWorkbench.InspectReward(); }

    public void AnatomySessionRestored()
    {
        RefreshAnatomy(); _anatomyWorkbench.DiscardInspection();
        // SetSession resets Sandbox pause owners. Reacquire this visible modal's owner,
        // even when a save restores byte-identical anatomy and the panel stayed open.
        _anatomyPaused = false; UpdateAnatomyModal();
    }

    public override void _Process(double delta)
    { if (_panel is not null) UpdateAnatomyLayout(); }

    public override void _Input(InputEvent input)
    {
        if (_tab != "Anatomy" || !_panel.Visible || !IsVisibleInTree()) return;
        if (input.IsActionPressed("ui_cancel")) { SetOpen(false); GetViewport().SetInputAsHandled(); }
        else if (new[] { "aw_inventory", "aw_character", "aw_endgame", "aw_experiment" }.Any(action => InputMap.HasAction(action) && input.IsActionPressed(action))) SetOpen(false);
        else if (input is InputEventKey or InputEventJoypadButton &&
            !new[] { "ui_up", "ui_down", "ui_left", "ui_right", "ui_accept", "ui_focus_next", "ui_focus_prev", "aw_save", "aw_load", "aw_journey" }.Any(action => input.IsAction(action)))
            GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        // A navigation key that no UI control accepted must not reach world shortcuts.
        if (_tab == "Anatomy" && _panel.Visible && IsVisibleInTree() && input is InputEventKey or InputEventJoypadButton &&
            !new[] { "aw_save", "aw_load", "aw_journey" }.Any(action => input.IsAction(action))) GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        if (_anatomyPaused && _anatomySandbox is not null && GodotObject.IsInstanceValid(_anatomySandbox))
            _anatomySandbox.SetModalPaused("divine-anatomy", false);
    }

    private void RefreshAnatomy()
    {
        if (_view is null || _tab != "Anatomy" || !_panel.Visible) return;
        if (!ReferenceEquals(_previewDefinition, _anatomyContent))
        { _previewDefinition = _anatomyContent; _previewContent = AdventureContent.Create(_anatomyContent); }
        bool alive = _combat.Actors.Any(a => a.Id == 1 && a.Health > 0);
        bool atMara = _state.InHub && alive && _interactions.Any(i => i.Id == "service.mara" && i.Distance <= i.Range);
        var build = _anatomySandbox?.Session.Build;
        string evolution = build is null ? "" : build.AshcleaverEvolution.Length > 0 ? build.AshcleaverEvolution : build.AshcleaverAwakened ? "Awakened" : "";
        _anatomyWorkbench.SetView(_previewContent!, _anatomy, _combat, _anatomyView.ActiveManifestations,
            _state.InHub, atMara, alive && !_engaged, _combat.Loot.Count, evolution);
    }

    private void UpdateAnatomyLayout()
    {
        bool anatomy = _tab == "Anatomy";
        var viewport = GetViewportRect().Size;
        if (_anatomyLayoutViewport != viewport || _anatomyLayoutActive != anatomy)
        {
            _anatomyLayoutViewport = viewport; _anatomyLayoutActive = anatomy;
            _journeyScroll.Visible = !anatomy; _anatomyWorkbench.Visible = anatomy;
            if (anatomy) _anatomyWorkbench.SetAvailableHeight(Math.Min(690, viewport.Y - 44) - 100);
        }
        Vector2 size = anatomy ? new(Math.Min(980, viewport.X - 44), Math.Min(690, viewport.Y - 44)) : new(371, 482);
        Vector2 position = anatomy ? (viewport - size) / 2 : new(876, 140);
        // Containers can temporarily enlarge the panel while swapping the diagram and preview.
        // Restore the requested bounds after that layout settles, without reapplying child minimum sizes.
        if (_panel.Position != position) _panel.Position = position;
        if (_panel.Size != size) _panel.Size = size;
        UpdateAnatomyModal();
    }

    private void UpdateAnatomyModal()
    {
        if (_anatomyBackdrop is null || _panel is null) return;
        bool open = IsVisibleInTree() && _panel.Visible && _tab == "Anatomy";
        _anatomyBackdrop.Visible = open;
        _panel.MouseForcePassScrollEvents = !open;
        if (open && _anatomySiblingIndex < 0)
        {
            _anatomySiblingIndex = GetIndex();
            GetParent().MoveChild(this, GetParent().GetChildCount() - 1);
        }
        else if (!open && _anatomySiblingIndex >= 0)
        {
            GetParent().MoveChild(this, Math.Min(_anatomySiblingIndex, GetParent().GetChildCount() - 1));
            _anatomySiblingIndex = -1;
        }
        if (_anatomyPaused == open) return;
        _anatomyPaused = open; _anatomySandbox?.SetModalPaused("divine-anatomy", open);
    }

    private void AnatomyRewardMapAction()
    {
        if (!FirstHeartAvailable) return;
        _rows.AddChild(Label("HEART OF SERATH · DIVINE FRAGMENT SECURED", 15));
        _rows.AddChild(Label("The Bell Saint's heart is already in your anatomy collection. Inspect its effect, then visit Mara to implant it. Ground equipment remains separate.", 12));
        var inspect = Button("Inspect the Heart of Serath", OpenAnatomyReward); inspect.Name = "InspectAnatomyReward";
        if (_state.InHub) Button("Walk to Mara · implant the heart", () => RequestInteraction("service.mara"));
        _rows.AddChild(new HSeparator());
    }
}
