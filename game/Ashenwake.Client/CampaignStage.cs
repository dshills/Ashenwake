using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Reusable region silhouettes and colors; every large landmark stands outside the authoritative play space.</summary>
public partial class CampaignStage : Node3D
{
    private AdventureStage _hub = null!;
    private Node3D _region = null!;
    private readonly Dictionary<string, Node3D> _markers = [];
    private string _signature = "";
    private string _markerSignature = "";
    private BellSanctuaryVisual? _bell;
    private VerdantHeartVisual? _heart;
    private FurnaceSpindleVisual? _furnace;
    private CovenantWardenVisual? _warden;
    private BreachHeartVisual? _breach;
    private readonly Dictionary<string, VerdantTrailVisual> _trails = [];
    private Vector3? _lastTrailPoint;
    public string PresentedEncounter { get; private set; } = "";
    private Sandbox? _sandbox;

    public Node3D? GetInteractionVisual(string id)
    {
        if (_hub.Visible) return _hub.GetInteractionVisual(id);
        if (!_markers.TryGetValue(id, out var marker)) return null;
        // Tracked remains persist independently of the current label marker. Native picking
        // needs the active clue's mesh tree, not its meshless label container.
        return _trails.TryGetValue(id, out var trail) ? trail : marker;
    }
    public override void _Ready()
    {
        _hub = new AdventureStage(); AddChild(_hub); _region = new Node3D(); AddChild(_region);
        for (Node? parent = GetParent(); parent is not null && _sandbox is null; parent = parent.GetParent())
            _sandbox = parent.GetChildren().OfType<Sandbox>().FirstOrDefault();
    }
    public override void _Process(double delta)
    {
        if (!_region.IsVisibleInTree()) return;
        bool paused = _sandbox?.IsPaused == true, reduced = _sandbox?.ReducedEffects == true;
        _bell?.Animate(delta, paused, reduced);
        _heart?.Animate(delta, paused, reduced);
        _furnace?.Animate(delta, paused, reduced);
        _warden?.Animate(delta, paused, reduced);
        _breach?.Animate(delta, paused, reduced);
        foreach (var trail in _trails.Values) trail.Animate(delta, paused, reduced);
    }
    public void Show(CampaignState state, CampaignView view, RoomDefinition room, IReadOnlyList<ExpeditionInteraction> interactions,
        IReadOnlyList<string> manifestations, int hubStage, CorePosition player, int bellPhase = 0, bool bossDefeated = false,
        CombatView? combat = null, string? activeEncounterId = null)
    {
        _hub.Visible = state.InHub; _region.Visible = !state.InHub;
        if (state.InHub)
        {
            _signature = ""; PresentedEncounter = "hub";
            _hub.ShowRoom("room.greyhaven", 0, room, manifestations, interactions.ToDictionary(i => i.ActionId, i => i.Position), new HashSet<string>(), hubStage);
            _hub.FocusNearestInteraction(player); return;
        }
        // StoryView points to the next objective as soon as a fight is won. Scenery belongs
        // to the actual occupied arena, including its loot and finite victory sequence.
        string encounter = state.Exploration?.Id switch
        {
            "event.widow_crypt" => "exploration.widow_crypt",
            "event.briar_shrine" => "exploration.briar_shrine",
            "event.wake_hunt" => "exploration.antler_hunt",
            "event.resonance_storm" => "exploration.burning_rain",
            "event.sealed_foundry" => "exploration.sealed_foundry",
            "event.divine_memory" => "exploration.first_oath",
            "event.oathkeeper_archive" => "exploration.oathkeeper_archive",
            "event.unremembered_vault" => "exploration.unremembered_vault",
            _ => activeEncounterId ?? view.EncounterId ?? ""
        };
        PresentedEncounter = encounter;
        int roots = combat?.Actors.Count(a => a.DefinitionId == "enemy.feeding_root" && a.Health > 0) ?? 3;
        bool heartDefeated = state.CompletedEncounters.Contains("campaign.rootheart") || combat?.Actors.Any(a => a.DefinitionId == "boss.rootheart" && a.Health <= 0) == true;
        string signature = state.CurrentAct + ":" + encounter + ":" + state.Deaths + ":" + room.HalfWidth + ":" + room.HalfDepth;
        if (_signature != signature)
        {
            _signature = signature;
            foreach (var child in _region.GetChildren()) { _region.RemoveChild(child); child.QueueFree(); }
            _markers.Clear(); _trails.Clear(); _lastTrailPoint = null; _markerSignature = ""; _bell = null; _heart = null; _furnace = null; _warden = null; _breach = null;
            BuildRegion(state.CurrentAct, room.HalfWidth * .001f, room.HalfDepth * .001f, encounter, bellPhase, bossDefeated);
            if (state.CurrentAct == 2 && encounter == "campaign.rootheart")
            { _heart = VerdantHeartVisual.Create(room.HalfWidth * .001f, room.HalfDepth * .001f, roots, heartDefeated); _region.AddChild(_heart); }
            if (state.CurrentAct == 3 && encounter == "campaign.furnace_spindle" && combat is not null)
            {
                _furnace = FurnaceSpindleVisual.Create(room.HalfWidth * .001f, room.HalfDepth * .001f, combat,
                    state.CompletedEncounters.Contains("campaign.furnace_spindle"));
                _region.AddChild(_furnace);
            }
            if (state.CurrentAct == 4 && encounter == "campaign.covenant_warden" && combat is not null)
            {
                _warden = CovenantWardenVisual.Create(room.HalfWidth * .001f, room.HalfDepth * .001f, combat,
                    state.CompletedEncounters.Contains("campaign.covenant_warden"));
                _region.AddChild(_warden);
            }
            if (state.CurrentAct == 5 && encounter == "campaign.breach_heart" && combat is not null)
            {
                _breach = BreachHeartVisual.Create(room.HalfWidth * .001f, room.HalfDepth * .001f, combat,
                    state.CompletedEncounters.Contains("campaign.breach_heart"));
                _region.AddChild(_breach);
            }
        }
        _bell?.SetPhase(bellPhase, bossDefeated);
        _heart?.SetState(roots, heartDefeated);
        if (combat is not null) _furnace?.SetState(combat, state.CompletedEncounters.Contains("campaign.furnace_spindle"));
        if (combat is not null) _warden?.SetState(combat, state.CompletedEncounters.Contains("campaign.covenant_warden"));
        if (combat is not null) _breach?.SetState(combat, state.CompletedEncounters.Contains("campaign.breach_heart"));
        string markerSignature = string.Join('|', interactions.Select(i => $"{i.ActionId}:{i.Position.X}:{i.Position.Z}:{i.Name}"));
        if (_markerSignature != markerSignature)
        {
            _markerSignature = markerSignature;
            foreach (var marker in _markers.Values) { _region.RemoveChild(marker); marker.QueueFree(); }
            _markers.Clear();
            foreach (var pair in _trails)
                if (!interactions.Any(i => i.ActionId == pair.Key)) pair.Value.MarkTracked();
            var points = new HashSet<CorePosition>();
            foreach (var interaction in interactions)
            {
                if (!points.Add(interaction.Position)) continue;
                var marker = new Node3D { Position = new(interaction.Position.X * .001f, 0, interaction.Position.Z * .001f) };
                _region.AddChild(marker); _markers[interaction.ActionId] = marker;
                if (state.Exploration?.Id == "event.wake_hunt" && interaction.ActionId.StartsWith("clue.", StringComparison.Ordinal))
                {
                    if (!_trails.ContainsKey(interaction.ActionId))
                    {
                        var trail = VerdantTrailVisual.Create(interaction.ActionId, marker.Position, _lastTrailPoint);
                        _trails[interaction.ActionId] = trail; _region.AddChild(trail); _lastTrailPoint = marker.Position;
                    }
                }
                else if (GreyMarchExplorationArt.SupportsMarker(interaction.ActionId))
                    GreyMarchExplorationArt.BuildMarker(marker, interaction.ActionId, encounter == "exploration.widow_crypt");
                else if (VerdantExplorationArt.SupportsMarker(interaction.ActionId))
                    VerdantExplorationArt.BuildMarker(marker, interaction.ActionId);
                else if (CinderExplorationArt.SupportsMarker(interaction.ActionId))
                    CinderExplorationArt.BuildMarker(marker, interaction.ActionId);
                else if (SpineExplorationArt.SupportsMarker(interaction.ActionId))
                    SpineExplorationArt.BuildMarker(marker, interaction.ActionId);
                else if (HollowExplorationArt.SupportsMarker(interaction.ActionId))
                    HollowExplorationArt.BuildMarker(marker, interaction.ActionId);
                else
                {
                    Mesh(new TorusMesh { InnerRadius = .47f, OuterRadius = .58f }, new(0, .07f, 0), new("d8c790"), marker);
                    Mesh(new CylinderMesh { TopRadius = .16f, BottomRadius = .3f, Height = .5f }, new(0, .25f, 0), new("93d6c9"), marker);
                }
                Label(HollowExplorationArt.MarkerLabel(interaction.ActionId, SpineExplorationArt.MarkerLabel(interaction.ActionId, CinderExplorationArt.MarkerLabel(interaction.ActionId, VerdantExplorationArt.MarkerLabel(interaction.ActionId, GreyMarchExplorationArt.MarkerLabel(interaction.ActionId, interaction.Name))))), new(0, 1.3f, 0), new("eee0b8"), marker);
            }
        }
        if (_markers.Count > 0)
        {
            Vector3 position = new(player.X * .001f, 0, player.Z * .001f);
            string nearest = _markers.MinBy(pair => pair.Value.Position.DistanceSquaredTo(position)).Key;
            foreach (var pair in _markers) foreach (var label in pair.Value.GetChildren().OfType<Label3D>()) label.Visible = pair.Key == nearest;
            foreach (var pair in _trails) pair.Value.SetFocused(pair.Key == nearest);
        }
    }
    private void BuildRegion(int act, float x, float z, string encounterId, int bellPhase, bool bossDefeated)
    {
        if (act == 1) { _bell = GreyMarchArt.Build(_region, x, z, encounterId, bellPhase, bossDefeated); return; }
        if (act == 2) { VerdantMawArt.Build(_region, x, z, encounterId); return; }
        if (act == 3) { CinderReachArt.Build(_region, x, z, encounterId); return; }
        if (act == 4) { ShatteredSpineArt.Build(_region, x, z, encounterId); return; }
        if (act == 5) HollowNightArt.Build(_region, x, z, encounterId);
    }
    private MeshInstance3D Mesh(Mesh mesh, Vector3 position, Color color, Node? parent = null)
    { var item = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = .95f } }; (parent ?? _region).AddChild(item); return item; }
    private void Label(string text, Vector3 position, Color color, Node? parent = null)
    { (parent ?? _region).AddChild(new Label3D { Text = text, Position = position, FontSize = 54, PixelSize = .012f, OutlineSize = 5, Modulate = color, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true }); }
}
