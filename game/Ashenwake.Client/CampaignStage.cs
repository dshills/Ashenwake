using Ashenwake.Core.Campaign;
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
    public override void _Ready() { _hub = new AdventureStage(); AddChild(_hub); _region = new Node3D(); AddChild(_region); }
    public void Show(CampaignState state, CampaignView view, RoomDefinition room, IReadOnlyList<ExpeditionInteraction> interactions,
        IReadOnlyList<string> manifestations, int hubStage, CorePosition player)
    {
        _hub.Visible = state.InHub; _region.Visible = !state.InHub;
        if (state.InHub)
        {
            _hub.ShowRoom("room.greyhaven", 0, room, manifestations, interactions.ToDictionary(i => i.ActionId, i => i.Position), new HashSet<string>(), hubStage);
            _hub.FocusNearestInteraction(player); return;
        }
        string signature = state.CurrentAct + ":" + view.EncounterId + ":" + string.Join('|', interactions.Select(i => i.ActionId));
        if (_signature != signature)
        {
            _signature = signature;
            foreach (var child in _region.GetChildren()) { _region.RemoveChild(child); child.QueueFree(); }
            _markers.Clear(); BuildRegion(state.CurrentAct, room.HalfWidth * .001f, room.HalfDepth * .001f);
            var points = new HashSet<CorePosition>();
            foreach (var interaction in interactions)
            {
                if (!points.Add(interaction.Position)) continue;
                var marker = new Node3D { Position = new(interaction.Position.X * .001f, 0, interaction.Position.Z * .001f) };
                _region.AddChild(marker); _markers[interaction.ActionId] = marker;
                Mesh(new TorusMesh { InnerRadius = .47f, OuterRadius = .58f }, new(0, .07f, 0), new("d8c790"), marker);
                Mesh(new CylinderMesh { TopRadius = .16f, BottomRadius = .3f, Height = .5f }, new(0, .25f, 0), new("93d6c9"), marker);
                Label(interaction.Name, new(0, 1.3f, 0), new("eee0b8"), marker);
            }
        }
        if (_markers.Count > 0)
        {
            Vector3 position = new(player.X * .001f, 0, player.Z * .001f);
            string nearest = _markers.MinBy(pair => pair.Value.Position.DistanceSquaredTo(position)).Key;
            foreach (var pair in _markers) foreach (var label in pair.Value.GetChildren().OfType<Label3D>()) label.Visible = pair.Key == nearest;
        }
    }
    private void BuildRegion(int act, float x, float z)
    {
        Color stone = new(new[] { "727b83", "416956", "66544a", "b1aa91", "514c72" }[act - 1]);
        Color accent = new(new[] { "c0be9a", "9bb662", "e69a55", "dfcea1", "a38acc" }[act - 1]);
        // These perimeter bands visually separate regions without adding navigational obstacles.
        Box(new(x * 2, .035f, .25f), new(0, .027f, -z + .2f), accent);
        Box(new(.25f, .035f, z * 2), new(-x + .2f, .027f, 0), accent);
        Box(new(x * 2, .035f, .25f), new(0, .027f, z - .2f), accent);
        for (int i = -2; i <= 2; i++)
        {
            float px = i * 4.6f;
            switch (act)
            {
                case 1:
                    Box(new(1.1f, 4.1f, 1.2f), new(px, 2, -z - 1.7f), stone);
                    Box(new(1.7f, .45f, 1.7f), new(px, 4.2f, -z - 1.7f), accent);
                    if (i < 2) Box(new(3.5f, .5f, .65f), new(px + 2.3f, 3.3f, -z - 1.7f), stone);
                    break;
                case 2:
                    var trunk = Mesh(new CylinderMesh { TopRadius = .27f, BottomRadius = .8f, Height = 4.5f }, new(px, 2.2f, -z - 1.6f), stone);
                    trunk.RotationDegrees = new(0, 0, i * 4);
                    Mesh(new SphereMesh { Radius = 1.75f, Height = 2 }, new(px + .35f, 4.3f, -z - 1.6f), accent);
                    Mesh(new SphereMesh { Radius = .8f, Height = 1.2f }, new(-x - 1.4f, 1.8f, i * 3.5f), accent);
                    break;
                case 3:
                    Box(new(2.5f, 2.7f, 2), new(px, 1.35f, -z - 1.7f), stone);
                    Mesh(new CylinderMesh { TopRadius = .35f, BottomRadius = .35f, Height = 3.8f }, new(px + .55f, 3.1f, -z - 1.7f), new("414f58"));
                    Box(new(1.3f, .6f, .08f), new(px, 1.2f, -z - .66f), accent);
                    break;
                case 4:
                    var bone = Box(new(.65f, 5.5f, .85f), new(px, 2.65f, -z - 1.7f), stone); bone.RotationDegrees = new(0, 0, i * 6);
                    Mesh(new SphereMesh { Radius = .65f, Height = 1 }, new(px, 5.5f, -z - 1.7f), accent);
                    if (i < 2) Box(new(3.9f, .45f, .65f), new(px + 2.3f, 4.6f, -z - 1.7f), stone);
                    break;
                default:
                    var crystal = Box(new(1.35f, 3.2f, 1.35f), new(px, 2.7f + Math.Abs(i) * .3f, -z - 1.7f), stone); crystal.RotationDegrees = new(0, 35, 25);
                    Mesh(new TorusMesh { InnerRadius = .65f, OuterRadius = .78f }, new(px, .3f, -z - 1.7f), accent);
                    break;
            }
        }
        string region = new[] { "THE GREY MARCH", "THE VERDANT MAW", "THE CINDER REACH", "THE SHATTERED SPINE", "THE HOLLOW NIGHT" }[act - 1];
        Label(region, new(0, 6.3f, -z - 2), accent);
    }
    private MeshInstance3D Box(Vector3 size, Vector3 position, Color color) => Mesh(new BoxMesh { Size = size }, position, color);
    private MeshInstance3D Mesh(Mesh mesh, Vector3 position, Color color, Node? parent = null)
    { var item = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = .95f } }; (parent ?? _region).AddChild(item); return item; }
    private void Label(string text, Vector3 position, Color color, Node? parent = null)
    { (parent ?? _region).AddChild(new Label3D { Text = text, Position = position, FontSize = 54, PixelSize = .012f, OutlineSize = 5, Modulate = color, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true }); }
}
