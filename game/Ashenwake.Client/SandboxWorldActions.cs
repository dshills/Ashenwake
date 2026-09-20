using Ashenwake.Core.Combat;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public sealed record WorldInteractionTarget(string Id, string Name, CorePosition Position, int Range, Node3D? Visual);

/// <summary>One cancellable mouse action. Authoritative commands still validate range, availability and rewards.</summary>
public partial class Sandbox
{
    private enum WorldActionKind { Interaction, Loot, Mechanism }
    private sealed record WorldAction(WorldActionKind Kind, string Id, long ItemId = 0, int MechanismId = 0);
    private sealed record ResolvedAction(WorldAction Action, string Name, CorePosition Position, int Range, Node3D? Visual);
    private IReadOnlyList<WorldInteractionTarget> _worldInteractions = [];
    private Action<string>? _activateWorldInteraction;
    private Func<int, Node3D?>? _mechanismVisual;
    private WorldAction? _worldAction;
    private ResolvedAction? _worldActionReceipt;
    private string? _readyWorldInteraction;
    private ResolvedAction? _hoveredWorldAction;
    private PanelContainer? _worldHoverPanel;
    private Label? _worldHoverLabel;
    private MeshInstance3D? _worldHoverRing;
    private double _worldHoverAge;
    private Vector2 _worldPointer = new(-1, -1);
    private Window? _worldPointerWindow;
    public string? PendingWorldActionId => _worldAction?.Id ?? _readyWorldInteraction;
    public string? HoveredWorldActionId => _hoveredWorldAction?.Action.Id;
    public long HoveredLootId => _hoveredWorldAction?.Action.Kind == WorldActionKind.Loot ? _hoveredWorldAction.Action.ItemId : 0;

    public void SetWorldInteractions(IReadOnlyList<WorldInteractionTarget> targets, Action<string> activate)
    {
        _worldInteractions = targets;
        _activateWorldInteraction = activate;
    }
    public void SetMechanismVisuals(Func<int, Node3D?> visuals) => _mechanismVisual = visuals;
    public bool RequestWorldInteraction(string id) => BeginWorldAction(new(WorldActionKind.Interaction, id));

    private void InitializeWorldPointer()
    {
        _worldPointerWindow = GetWindow();
        _worldPointerWindow.MouseExited += ClearWorldPointer;
    }
    private void ObserveWorldPointer(InputEvent input)
    {
        if (input is InputEventMouse mouse) _worldPointer = mouse.Position;
    }
    private void ClearWorldPointer() { _worldPointer = new(-1, -1); ResetWorldHover(); }
    private void ReleaseWorldPointer()
    {
        if (_worldPointerWindow is not null && GodotObject.IsInstanceValid(_worldPointerWindow))
            _worldPointerWindow.MouseExited -= ClearWorldPointer;
    }

    private ResolvedAction? ResolveWorldAction(WorldAction action)
    {
        if (action.Kind == WorldActionKind.Interaction)
        {
            var target = _worldInteractions.FirstOrDefault(t => t.Id == action.Id);
            return target is null ? null : new(action, target.Name, target.Position, target.Range, target.Visual);
        }
        if (action.Kind == WorldActionKind.Loot)
        {
            var drop = _session.View.Loot.FirstOrDefault(l => l.Id == action.ItemId && IsLootVisible(l));
            return drop is null ? null : new(action, EquipmentNames.For(drop.Item), drop.Position, CombatSession.PickupRange, _lootVisuals.GetValueOrDefault(drop.Id));
        }
        var mechanism = _session.View.Endgame?.Mechanisms.FirstOrDefault(m => m.Id == action.MechanismId && m.Available);
        return mechanism is null ? null : new(action, mechanism.Prompt, mechanism.Position, mechanism.Radius, _mechanismVisual?.Invoke(mechanism.Id));
    }

    private bool BeginWorldAction(WorldAction action)
    {
        CancelMouseMovement(true);
        if (IsPaused) { NavigationNotice("Resume playing to approach that target."); return false; }
        if (_session.View.Actors.Single(a => a.Id == 1).Health <= 0) return false;
        var resolved = ResolveWorldAction(action);
        if (resolved is null) { NavigationNotice("That target is no longer available."); return false; }
        var player = _session.View.Actors.Single(a => a.Id == 1);
        _clickMove ??= new ClickMovePlanner(_session.Room);
        var occupied = _session.View.Actors.Where(a => a.Id != 1 && a.Health > 0).Select(a => a.Position).ToArray();
        if (!_clickMove.TrySetApproach(player.Position, resolved.Position, resolved.Range, occupied))
        { NavigationNotice("No clear route within reach of " + resolved.Name + "."); return false; }
        _worldAction = action;
        NavigationNotice((action.Kind == WorldActionKind.Loot ? "Collecting " : "Approaching ") + resolved.Name + " · X to cancel");
        ShowMoveDestination();
        return true;
    }

    private void UpdateWorldAction(CombatActorView player, bool held)
    {
        if (_worldAction is null) return;
        var target = ResolveWorldAction(_worldAction);
        if (target is null)
        { CancelMouseMovement(true); NavigationNotice("That target is no longer available."); return; }
        if (held) return;
        if (DistanceSquared(player.Position, target.Position) <= (long)target.Range * target.Range)
        {
            var action = _worldAction;
            CancelMouseMovement(true);
            // Stop reaches Core before the action. A conversation is opened after this tick,
            // after checking that combat has not killed the player or replaced the room.
            if (action.Kind == WorldActionKind.Loot) Enqueue(new(CombatCommandKind.Pickup, ItemId: action.ItemId));
            else if (action.Kind == WorldActionKind.Mechanism) Enqueue(new(CombatCommandKind.InteractMechanism, TargetId: action.MechanismId));
            else _readyWorldInteraction = action.Id;
            if (action.Kind != WorldActionKind.Interaction) _worldActionReceipt = target;
            if (_navigationNotice is not null) _navigationNotice.Text = "";
        }
        else if (_clickMove?.Destination is null)
        { CancelMouseMovement(true); NavigationNotice("The approach is blocked. Choose another route."); }
    }

    private void CompleteMouseInteraction(IReadOnlyList<CombatEvent> events)
    {
        if (_worldActionReceipt is { } receipt)
        {
            _worldActionReceipt = null;
            if (receipt.Action.Kind == WorldActionKind.Loot)
                NavigationNotice(events.Any(e => e.Kind == "LootPickedUp" && e.ActorId == 1) && !_view.Loot.Any(l => l.Id == receipt.Action.ItemId)
                    ? "Collected " + receipt.Name + "." : "Couldn't collect " + receipt.Name + ". Check your inventory space and reach.");
            else if (!events.Any(e => e.Kind == "HuntMechanismUsed" && e.TargetId == receipt.Action.MechanismId))
                NavigationNotice("That mechanism cannot be used right now.");
        }
        string? id = _readyWorldInteraction;
        _readyWorldInteraction = null;
        if (id is null || IsPaused) return;
        var target = _worldInteractions.FirstOrDefault(t => t.Id == id);
        var player = _session.View.Actors.Single(a => a.Id == 1);
        if (target is not null && player.Health > 0 && DistanceSquared(player.Position, target.Position) <= (long)target.Range * target.Range)
            _activateWorldInteraction?.Invoke(id);
    }

    private void NavigationNotice(string message)
    {
        if (_navigationNotice is not null) _navigationNotice.Text = message;
        _navigationNoticeAge = 3;
    }

    private ResolvedAction? PickWorldAction(Vector2 screen)
    {
        var candidates = new List<ResolvedAction>();
        foreach (var target in _worldInteractions)
            if (target.Visual is not null && MouseHitsNode(target.Visual, screen))
                candidates.Add(new(new(WorldActionKind.Interaction, target.Id), target.Name, target.Position, target.Range, target.Visual));
        foreach (var mechanism in _view.Endgame?.Mechanisms ?? [])
        {
            var visual = _mechanismVisual?.Invoke(mechanism.Id);
            if (mechanism.Available && visual is not null && MouseHitsNode(visual, screen))
                candidates.Add(new(new(WorldActionKind.Mechanism, "mechanism:" + mechanism.Id, MechanismId: mechanism.Id), mechanism.Prompt, mechanism.Position, mechanism.Radius, visual));
        }
        // Target only the visible item or rarity marker; filtered drops never consume a floor click.
        foreach (var drop in _view.Loot.Where(IsLootVisible))
            if (_lootVisuals.TryGetValue(drop.Id, out var visual) && MouseHitsNode(visual, screen))
                candidates.Add(new(new(WorldActionKind.Loot, "loot:" + drop.Id, ItemId: drop.Id), EquipmentNames.For(drop.Item), drop.Position, CombatSession.PickupRange, visual));
        return candidates.OrderBy(t => _camera.UnprojectPosition(PositionOf(t.Position.X, t.Position.Z) + Vector3.Up * .4f).DistanceSquaredTo(screen))
            .ThenBy(t => t.Action.Kind).ThenBy(t => t.Action.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private int PickEnemy(Vector2 screen) => _view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.Visible)
        .Where(a => _actors.TryGetValue(a.Id, out var actor) && MouseHitsBody(actor, screen))
        .OrderBy(a => _camera.UnprojectPosition(PositionOf(a.Position.X, a.Position.Z) + Vector3.Up).DistanceSquaredTo(screen)).ThenBy(a => a.Id)
        .Select(a => a.Id).FirstOrDefault();

    private bool MouseHitsNode(Node3D node, Vector2 screen)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || !node.IsVisibleInTree()) return false;
        // NPCs, item markers and usable mechanisms fit within this generous four-metre
        // sphere. One projection rejects distant targets before visiting their mesh trees.
        float pixelsPerMetre = GetViewport().GetVisibleRect().Size.Y / _camera.Size;
        float broadRadius = pixelsPerMetre * 4;
        if (_camera.UnprojectPosition(node.GlobalPosition + Vector3.Up).DistanceSquaredTo(screen) > broadRadius * broadRadius) return false;
        return Meshes(node).Any(mesh =>
        {
            if (!mesh.IsVisibleInTree() || mesh.Mesh is null) return false;
            var bounds = mesh.Mesh.GetAabb();
            var rectangle = new Rect2(_camera.UnprojectPosition(mesh.GlobalTransform * bounds.Position), Vector2.Zero);
            for (int i = 0; i < 8; i++) rectangle = rectangle.Expand(_camera.UnprojectPosition(mesh.GlobalTransform * bounds.GetEndpoint(i)));
            return rectangle.Grow(3).HasPoint(screen);
        });
    }

    private void UpdateWorldHover(double delta)
    {
        _worldHoverAge += delta;
        if (_worldHoverAge < .05 && !IsPaused) return;
        _worldHoverAge = 0;
        // Input-event positions also work for viewport-fed input and headless verification;
        // the OS cursor may be outside this window while those events are delivered.
        Vector2 screen = _worldPointer;
        bool blocked = IsPaused || !GetViewport().GetVisibleRect().HasPoint(screen) || GetViewport().GuiGetHoveredControl() is not null;
        int enemy = blocked ? 0 : PickEnemy(screen);
        SetHoveredActor(enemy);
        _hoveredWorldAction = blocked || enemy != 0 ? null : PickWorldAction(screen);
        if (_worldHoverPanel is null)
        {
            _worldHoverPanel = new PanelContainer { Name = "WorldHover", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _worldHoverLabel = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
            _worldHoverLabel.AddThemeFontSizeOverride("font_size", 14);
            _worldHoverPanel.AddChild(_worldHoverLabel); AddOverlay(_worldHoverPanel);
            _worldHoverRing = new MeshInstance3D
            {
                Name = "WorldHoverRing",
                Mesh = new TorusMesh { InnerRadius = .61f, OuterRadius = .67f, Rings = 16, RingSegments = 8 },
                Scale = new(1, .2f, 1),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new("fff0b0"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                Visible = false
            };
            AddChild(_worldHoverRing);
        }
        _worldHoverPanel.Visible = _worldHoverRing!.Visible = _hoveredWorldAction is not null;
        if (_hoveredWorldAction is { } target)
        {
            string verb = target.Action.Kind == WorldActionKind.Loot ? "Collect" : target.Action.Kind == WorldActionKind.Mechanism ? "Use" : "Interact";
            _worldHoverLabel!.Text = target.Name + "\nClick to " + verb.ToLowerInvariant();
            _worldHoverPanel.ResetSize();
            var size = GetViewport().GetVisibleRect().Size;
            _worldHoverPanel.Position = new(Math.Clamp(screen.X + 18, 8, Math.Max(8, size.X - _worldHoverPanel.Size.X - 8)),
                Math.Clamp(screen.Y + 20, 8, Math.Max(8, size.Y - _worldHoverPanel.Size.Y - 8)));
            _worldHoverRing.Position = PositionOf(target.Position.X, target.Position.Z) + Vector3.Up * .03f;
        }
        RefreshLootHover();
    }

    private void RefreshLootHover()
    {
        foreach (var pair in _lootVisuals)
        {
            bool nearest = _nearbyLootLabel is { Visible: true } && pair.Value.Position.DistanceSquaredTo(_nearbyLootLabel.Position - Vector3.Up * .8f) < .001f;
            pair.Value.SetHighlighted(pair.Key == HoveredLootId || pair.Key == _inspectedLoot || nearest);
        }
    }

    private void ClearWorldAction()
    {
        _worldAction = null; _readyWorldInteraction = null; _worldActionReceipt = null;
    }
    private void ResetWorldHover()
    {
        _hoveredWorldAction = null; SetHoveredActor(0);
        if (_worldHoverPanel is not null) _worldHoverPanel.Visible = false;
        if (_worldHoverRing is not null) _worldHoverRing.Visible = false;
    }
}
