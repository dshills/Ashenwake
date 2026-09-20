using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private CharacterAppearance _playerAppearance = null!;
    private int _manifestationMask;
    private readonly Dictionary<long, LootVisual> _lootVisuals = [];
    private Label3D _nearbyLootLabel = null!;
    public CharacterAppearance CurrentAppearance => _playerAppearance;

    private void RefreshAppearance()
    {
        var build = _session.Build;
        string evolution = build.AshcleaverEvolution.Length > 0 ? build.AshcleaverEvolution : build.AshcleaverAwakened ? "Awakened" : "";
        _playerAppearance = CharacterAppearance.FromCombat(_view, _manifestations, evolution);
    }

    public void SetManifestationPresentation(IReadOnlyList<string> ids, IEnumerable<string>? fragments = null)
    {
        _manifestations = ids;
        int mask = CharacterAppearance.Manifestations(ids);
        int anatomy = fragments is null ? _playerAppearance?.AnatomyMask ?? 0 : CharacterAppearance.AnatomyFragments(fragments);
        // A service can update the existing CombatSession while the world is paused, without changing forms.
        // Refresh its projection immediately when an implant's cosmetic identity changes.
        if (_manifestationMask == mask && (_playerAppearance?.AnatomyMask ?? 0) == anatomy) return;
        _manifestationMask = mask;
        if (_session is null) return;
        _view = _session.View; SynchronizeWorld();
    }

    private void SynchronizeLootVisuals()
    {
        var visible = _view.Loot.Where(IsLootVisible).ToArray();
        var live = visible.Select(l => l.Id).ToHashSet();
        foreach (long id in _lootVisuals.Keys.Where(id => !live.Contains(id)).ToArray())
        { var old = _lootVisuals[id]; RemoveChild(old); old.QueueFree(); _lootVisuals.Remove(id); }
        foreach (var drop in visible)
        {
            if (!_lootVisuals.TryGetValue(drop.Id, out var visual))
            {
                visual = LootVisual.Create(drop.Item); visual.Name = $"Loot{drop.Id}";
                AddChild(visual); _lootVisuals[drop.Id] = visual;
            }
            visual.Position = PositionOf(drop.Position.X, drop.Position.Z);
        }
        if (_nearbyLootLabel is null)
        {
            _nearbyLootLabel = new Label3D
            {
                Name = "NearbyLootLabel",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 40,
                PixelSize = .008f,
                OutlineSize = 7,
                NoDepthTest = true,
                Visible = false
            };
            AddChild(_nearbyLootLabel);
        }
        var player = _view.Actors.FirstOrDefault(a => a.Id == 1);
        var nearest = player is null ? null : visible.Where(l => DistanceSquared(l.Position, player.Position) <= 2500L * 2500)
            .OrderBy(l => DistanceSquared(l.Position, player.Position)).ThenBy(l => l.Id).FirstOrDefault();
        foreach (var pair in _lootVisuals) pair.Value.SetHighlighted(pair.Key == nearest?.Id || pair.Key == _inspectedLoot);
        _nearbyLootLabel.Visible = nearest is not null;
        if (nearest is not null)
        {
            _nearbyLootLabel.Position = PositionOf(nearest.Position.X, nearest.Position.Z) + Vector3.Up * .8f;
            _nearbyLootLabel.Text = $"{EquipmentNames.For(nearest.Item)} · {nearest.Item.Rarity}";
            _nearbyLootLabel.Modulate = LootVisual.RarityColor(nearest.Item.Rarity);
        }
    }

    private void ClearLootVisuals()
    {
        foreach (var visual in _lootVisuals.Values) { RemoveChild(visual); visual.QueueFree(); }
        _lootVisuals.Clear();
        if (_nearbyLootLabel is not null) _nearbyLootLabel.Visible = false;
    }
}
