using Ashenwake.Core.Combat;
using Ashenwake.Core.Exploration;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Map input uses the same cancellable movement planner as ground clicks.</summary>
public partial class Sandbox
{
    private LocalExplorationMap? _localMap;
    private ColorRect? _localMapBackdrop;
    private Func<bool>? _localMapAvailable;
    private LocalMapView? _presentedLocalMap;
    private Func<LocalMapView?>? _currentLocalMap;
    private string _localMapTitle = "";
    private CorePosition _localMapPlayer;
    private LocalMapMarker[] _localMapMarkers = [];
    private (int Rarity, bool Compatible, bool ShowAll) _localMapLootFilters;
    private (int, bool, bool) CurrentMapLootFilters => (_minimumLootRarity, _compatibleLootOnly, Input.IsActionPressed("aw_showloot"));
    public LocalExplorationMap? LocalMapControl => _localMap;
    public bool LocalMapOpen => _localMap?.Expanded == true;

    public void ConfigureLocalMap(Func<bool> available, Func<LocalMapView?> currentMap)
    {
        _localMapAvailable = available; _currentLocalMap = currentMap;
        if (_localMap is not null) return;
        _localMapBackdrop = new ColorRect
        {
            Name = "LocalMapBackdrop",
            Color = new("050b14df"),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseForcePassScrollEvents = false
        };
        _hud.AddChild(_localMapBackdrop); _localMapBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _localMap = new LocalExplorationMap { Visible = false }; _hud.AddChild(_localMap);
        _localMap.ToggleRequested += ToggleLocalMap; _localMap.CloseRequested += CloseLocalMap;
        _localMap.DestinationRequested += point => RequestLocalMapDestination(point);
    }

    public void PresentLocalMap(LocalMapView? view, string title, CombatView combat)
    {
        if (_presentedLocalMap?.RoomId != view?.RoomId || _presentedLocalMap?.LayoutHash != view?.LayoutHash)
        { CloseLocalMap(); CancelMouseMovement(true); }
        _presentedLocalMap = view; _localMapTitle = title;
        _localMapPlayer = combat.Actors.Single(a => a.Id == 1).Position;
        RefreshLocalMapMarkers(combat); RefreshLocalMapPresentation();
    }

    private void RefreshLocalMapMarkers(CombatView combat)
    {
        _localMapLootFilters = CurrentMapLootFilters;
        var markers = _worldInteractions.Select(target => new LocalMapMarker(target.Id, target.Name, target.Position,
            target.Id.Contains("treasure", StringComparison.Ordinal) ? LocalMapMarkerKind.Treasure :
            target.Id.Contains("exit", StringComparison.Ordinal) || target.Id.Contains("return", StringComparison.Ordinal) ||
            target.Id.Contains("enter", StringComparison.Ordinal) || target.Id.StartsWith("opening.back.", StringComparison.Ordinal) ||
            target.Id.StartsWith("opening.forward.", StringComparison.Ordinal) || target.Id.StartsWith("verdant.back.", StringComparison.Ordinal) ||
            target.Id.StartsWith("verdant.forward.", StringComparison.Ordinal) || target.Id.StartsWith("cinder.back.", StringComparison.Ordinal) ||
            target.Id.StartsWith("cinder.forward.", StringComparison.Ordinal) || target.Id.StartsWith("spine.back.", StringComparison.Ordinal) ||
            target.Id.StartsWith("spine.forward.", StringComparison.Ordinal) || target.Id.StartsWith("hollow.back.", StringComparison.Ordinal) ||
            target.Id.StartsWith("hollow.forward.", StringComparison.Ordinal) || target.Id == "journey.next" ? LocalMapMarkerKind.Exit : LocalMapMarkerKind.Service)).ToList();
        markers.AddRange(combat.Loot.Where(IsLootVisible).Select(loot => new LocalMapMarker("loot." + loot.Id, "Uncollected loot", loot.Position, LocalMapMarkerKind.Loot)));
        if (combat.Endgame is { } endgame)
            markers.AddRange(endgame.Mechanisms.Where(m => m.Available).Select(m => new LocalMapMarker("mechanism." + m.Id, "Mechanism", m.Position, LocalMapMarkerKind.Service)));
        _localMapMarkers = markers.Where(marker => _presentedLocalMap?.IsExplored(marker.Position) == true).ToArray();
    }

    private void RefreshLocalMapPresentation()
    {
        if (_localMap is null) return;
        bool available = _presentedLocalMap is not null && _localMapAvailable?.Invoke() == true;
        if (!available) CloseLocalMap();
        if (available && _localMapLootFilters != CurrentMapLootFilters) RefreshLocalMapMarkers(_session.View);
        _localMap.Visible = available && (LocalMapOpen || !IsPaused);
        _localMap.SetOpenKeyLabel(KeyDisplay(_keys["localmap"]));
        var viewport = GetViewport().GetVisibleRect().Size;
        if (LocalMapOpen)
        {
            _localMap.Size = new(Math.Min(760, viewport.X - 40), Math.Min(620, viewport.Y - 40));
            _localMap.Position = (viewport - _localMap.Size) / 2;
        }
        else
        {
            _localMap.Size = viewport.Y < 760 ? new(224, 166) : new(256, 194);
            _localMap.Position = new(viewport.X - _localMap.Size.X - 22, 182);
        }
        _localMap.SetView(_presentedLocalMap, _localMapTitle, _localMapPlayer, _localMapMarkers, ClickMoveDestination);
        // The reward feed retains all three cards, including on short windows and Echoes runs.
        if (_rewardFeed is not null)
        {
            float feedWidth = available && viewport.X < 900 ? 240 : 280;
            _rewardFeed.Size = new(feedWidth, 190);
            var feedPosition = available ? new Vector2(viewport.X - (viewport.Y < 760 ? 224 : 256) - 32 - feedWidth, 204) : new(viewport.X - 302, 182);
            if (available)
                foreach (var objective in new[] { _campaignObjective, _expeditionObjective })
                    if (objective is not null && objective.IsVisibleInTree() && objective.GetGlobalRect().End.X > feedPosition.X)
                        feedPosition.Y = Math.Max(feedPosition.Y, objective.GetGlobalRect().End.Y + 10);
            _rewardFeed.Position = feedPosition;
        }
    }

    public void ToggleLocalMap()
    {
        if (LocalMapOpen) { CloseLocalMap(); return; }
        if (_localMap is null || _presentedLocalMap is null || _localMapAvailable?.Invoke() != true || HasModalPause) return;
        _localMap.SetExpanded(true); _localMapBackdrop!.Visible = true;
        SetModalPaused("local-map", true);
        _hud.MoveChild(_localMapBackdrop, _hud.GetChildCount() - 1); _hud.MoveChild(_localMap, _hud.GetChildCount() - 1);
        RefreshLocalMapPresentation(); FocusFirstAction(_localMap);
    }

    public void CloseLocalMap()
    {
        if (!LocalMapOpen) return;
        var focus = GetViewport().GuiGetFocusOwner();
        bool mapHadFocus = focus is not null && _localMap!.IsAncestorOf(focus);
        _localMap!.SetExpanded(false); _localMapBackdrop!.Visible = false;
        SetModalPaused("local-map", false);
        if (mapHadFocus) FocusResumeOrRelease();
    }

    public bool RequestLocalMapDestination(CorePosition destination)
    {
        if (_presentedLocalMap is null || _localMapAvailable?.Invoke() != true || !ReferenceEquals(_presentedLocalMap, _currentLocalMap?.Invoke()) ||
            !_presentedLocalMap.IsExplored(destination) || !new SpatialWorld(_session.Room).CanOccupy(destination, CombatSession.ActorRadius)) return false;
        CloseLocalMap();
        if (IsPaused) return false;
        CancelMouseMovement(true);
        return TryBeginGroundMovement(destination);
    }

    private bool HandleLocalMapInput(InputEvent input)
    {
        if (_settingsPanel is { Visible: true } || _awaitingKey is not null) return false;
        if (!LocalMapOpen)
        {
            if (!input.IsActionPressed("aw_localmap")) return false;
            ToggleLocalMap(); GetViewport().SetInputAsHandled(); return true;
        }
        if (input.IsActionPressed("aw_localmap") || input.IsActionPressed("aw_settings") || input.IsActionPressed("ui_cancel") ||
            input is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape })
            CloseLocalMap();
        else if (input.IsActionPressed("aw_save")) { CloseLocalMap(); Save(); }
        else if (input.IsActionPressed("aw_load")) { CloseLocalMap(); Load(); }
        else if (input.IsActionPressed("aw_replay")) { CloseLocalMap(); VerifyReplay(); }
        else if (new[] { "aw_inventory", "aw_character", "aw_journey", "aw_endgame" }.Any(action => input.IsActionPressed(action)))
        { CloseLocalMap(); return false; }
        else if (input.IsActionPressed("aw_experiment")) return false;
        else if (input is not (InputEventKey or InputEventJoypadButton) ||
            new[] { "ui_focus_next", "ui_focus_prev", "ui_accept" }.Any(action => input.IsAction(action))) return false;
        GetViewport().SetInputAsHandled(); return true;
    }
}
