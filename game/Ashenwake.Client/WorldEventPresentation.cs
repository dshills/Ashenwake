using Ashenwake.Core.Expedition;
using Ashenwake.Core.Endgame;
using Godot;

namespace Ashenwake.Client;

public sealed record WorldEventWorldDisplay(int Act, bool InEvent, string Stage, string EventId,
    ExpeditionInteraction[] Interactions);

/// <summary>Optional encounter scenery. Core owns positions, warnings, choices, combat and rewards.</summary>
public partial class WorldEventPresentation : Node3D
{
    private readonly Dictionary<string, Node3D> _markers = [];
    private readonly List<(Node3D Node, Vector3 Origin, float Phase)> _drifting = [];
    private readonly List<CharacterVisual> _travelers = [];
    private Node3D? _scenery;
    private Sandbox? _sandbox;
    private string _key = "";
    private double _time;
    public string PresentedEvent { get; private set; } = "";
    public double MotionTime => _time;
    public int DecorationCount => _drifting.Count;
    public void Attach(Sandbox sandbox) => _sandbox = sandbox;
    public Node3D? GetInteractionVisual(string id) => _markers.GetValueOrDefault(id);

    public override void _Ready()
    {
        for (Node? parent = GetParent(); parent is not null && _sandbox is null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) _sandbox = sandbox;
    }

    public override void _Process(double delta)
    {
        bool reduced = _sandbox?.ReducedEffects == true;
        // Apply settings even while paused; ornamental drift never affects a hazard warning.
        foreach (var traveler in _travelers) traveler.SetReducedEffects(reduced);
        if (reduced)
        {
            _time = 0;
            foreach (var (node, rest, _) in _drifting) { node.Position = rest; node.Rotation = Vector3.Zero; }
            return;
        }
        if (_sandbox?.IsPaused == true || !IsVisibleInTree()) return;
        _time = (_time + Math.Clamp(delta, 0, .1)) % 3600;
        foreach (var (node, rest, phase) in _drifting)
        {
            node.Position = rest + new Vector3(MathF.Sin((float)_time * .35f + phase) * .075f,
                MathF.Sin((float)_time * .55f + phase) * .055f, 0);
            node.Rotation = new(0, MathF.Sin((float)_time * .2f + phase) * .08f, 0);
        }
        foreach (var traveler in _travelers) traveler.Animate(delta, Vector3.Zero, facing: new Vector3(-.5f, 0, -1));
    }

    public void Reset()
    {
        _drifting.Clear(); _travelers.Clear();
        foreach (var marker in _markers.Values) { RemoveChild(marker); marker.QueueFree(); }
        _markers.Clear();
        if (_scenery is not null) { RemoveChild(_scenery); _scenery.QueueFree(); _scenery = null; }
        _key = ""; _time = 0; PresentedEvent = "";
    }

    public void Present(WorldEventWorldDisplay view)
    {
        var targets = view.Interactions.Where(i => IsWorldEncounterAction(i.ActionId)).ToArray();
        string key = $"{view.Act}:{view.InEvent}:{view.Stage}:{view.EventId}:" +
            string.Join('|', targets.Select(i => $"{i.ActionId}:{i.Position.X}:{i.Position.Z}"));
        if (_key == key) return;
        Reset(); _key = key; PresentedEvent = view.InEvent ? view.EventId : "";
        if (view.InEvent)
        {
            _scenery = new Node3D { Name = "WorldEncounterScenery" }; AddChild(_scenery);
            // Tall silhouettes sit against the back edge, away from the center combat lanes.
            var landmark = new Node3D { Name = "EncounterLandmark", Position = new(3.3f, 0, -6.7f) };
            _scenery.AddChild(landmark);
            Landmark(landmark, view.EventId, view.Act, view.Stage);
            if (view.EventId.StartsWith("storm.", StringComparison.Ordinal)) StormPerimeter(_scenery, view.Act);
        }
        foreach (var target in targets)
        {
            string eventId = target.ActionId[..target.ActionId.LastIndexOf('.')];
            var marker = new Node3D
            {
                Name = "WorldEventMarker_" + target.ActionId.Replace('.', '_'),
                Position = new(target.Position.X * .001f, 0, target.Position.Z * .001f)
            };
            AddChild(marker); _markers.Add(target.ActionId, marker);
            string accent = Accent(view.Act, eventId);
            var b = new EnvironmentBuilder(marker, "EncounterInteraction");
            if (target.ActionId.EndsWith(".enter", StringComparison.Ordinal))
            {
                Entry(b, eventId, accent); Label(marker, EventName(eventId), new(0, 2.3f, 0), accent);
            }
            else if (target.ActionId.EndsWith(".choice", StringComparison.Ordinal))
            {
                if (eventId.Contains("lantern", StringComparison.Ordinal))
                { Lantern(b, new(.55f, 0, 0), 1.7f); Traveler(marker, new(-.3f, 0, 0)); }
                else if (eventId.Contains("caravan", StringComparison.Ordinal))
                { Candle(b, new(-.35f, 0, 0), .7f, accent); Candle(b, new(.35f, 0, 0), 1.05f, accent); }
                else if (eventId.Contains("shrine", StringComparison.Ordinal)) Shrine(b, accent, small: true);
                else StormAnchor(b, accent, 1.4f);
            }
            else if (target.ActionId.EndsWith(".treasure", StringComparison.Ordinal))
            { Treasure(b, accent, false); Label(marker, "Encounter treasure", new(0, 1.5f, 0), "e7cc91"); }
            else if (target.ActionId.EndsWith(".exit", StringComparison.Ordinal))
            {
                b.Branch(new(0, 0, 0), new(0, 1.6f, 0), .085f, .065f, "796950");
                b.Box(new(.85f, .19f, .12f), new(-.2f, 1.4f, 0), "c1b391", new(0, 0, -8), surface: SurfaceKind.Wood);
                Label(marker, "Return to the region", new(0, 2f, 0), "d3c6ac");
            }
            b.Flush();
        }
    }

    private void Landmark(Node3D parent, string eventId, int act, string stage)
    {
        bool complete = stage == "Claimed";
        string accent = Accent(act, eventId);
        var b = new EnvironmentBuilder(parent, "EncounterArchitecture");
        if (eventId.Contains("lantern", StringComparison.Ordinal))
        {
            Lantern(b, new(0, 0, 0), 2.9f);
            b.Box(new(1.2f, .14f, .7f), new(-1.6f, .1f, .5f), "79624e", surface: SurfaceKind.Wood);
            b.Cylinder(.35f, .32f, .7f, new(-1.6f, .52f, .5f), "7c7b68", surface: SurfaceKind.Cloth);
            b.Box(new(.85f, .5f, .6f), new(1.5f, .25f, .45f), "615348", new(0, 14, 0), surface: SurfaceKind.Wood);
            if (stage != "Foyer") Traveler(parent, new(-.75f, 0, -.3f));
        }
        else if (eventId.Contains("caravan", StringComparison.Ordinal))
        {
            Wagon(b);
            for (int i = 0; i < 5; i++) Candle(b, new(-2.8f + i * 1.4f, 0, 1.35f), .5f + (i % 2) * .25f, accent);
            for (int i = 0; i < 3; i++) Wisp(parent, new(-1.2f + i * 1.2f, 2.45f + i % 2 * .25f, -.2f), accent, i);
        }
        else if (eventId.Contains("shrine", StringComparison.Ordinal))
        {
            Shrine(b, accent, false);
            foreach (float side in new[] { -1f, 1f })
            {
                b.Branch(new(side * 1.25f, 0, .2f), new(side * .95f, 2.7f, .2f), .18f, .09f, "66544f", SurfaceKind.Stone);
                b.Branch(new(side * .95f, 2.7f, .2f), new(side * .36f, 2.92f, .2f), .09f, .018f, "958b79", SurfaceKind.Bone);
            }
        }
        else StormAnchor(b, accent, 2.8f);
        if (complete) Treasure(b, accent, true, new(2.8f, 0, .7f));
        b.Flush();
    }

    private void StormPerimeter(Node3D parent, int act)
    {
        string accent = Accent(act, "storm.");
        var b = new EnvironmentBuilder(parent, "StormPerimeter");
        for (int i = 0; i < 6; i++)
        {
            var p = new Vector3(-6.5f + i * 2.6f, 0, -7.4f);
            b.Cylinder(.28f, .2f, .36f, p + new Vector3(0, .18f, 0), "646770", surface: SurfaceKind.Stone);
            b.Torus(.2f, .26f, p + new Vector3(0, .4f, 0), accent, glow: true);
            Wisp(parent, p + new Vector3(0, 1.8f + i % 2 * .55f, 0), accent, i);
        }
        b.Flush();
    }

    private void Wisp(Node3D parent, Vector3 at, string accent, float phase)
    {
        var node = new Node3D { Name = "EncounterWisp", Position = at }; parent.AddChild(node);
        var b = new EnvironmentBuilder(node, "WispMesh");
        b.Cylinder(.08f, .01f, .22f, Vector3.Zero, accent, glow: true);
        b.Torus(.14f, .16f, Vector3.Zero, accent, new(25, 0, 12), glow: true);
        b.Flush(); _drifting.Add((node, at, phase));
    }

    private void Traveler(Node3D parent, Vector3 at)
    {
        var traveler = CharacterVisual.CreateNpc("npc.lantern_traveler");
        traveler.Name = "LanternTraveler"; traveler.Position = at; traveler.SetAccent(new("c59c5e"));
        traveler.SetReducedEffects(_sandbox?.ReducedEffects == true);
        parent.AddChild(traveler); _travelers.Add(traveler);
    }

    private static void Entry(EnvironmentBuilder b, string eventId, string accent)
    {
        if (eventId.Contains("lantern", StringComparison.Ordinal)) Lantern(b, Vector3.Zero, 1.75f);
        else if (eventId.Contains("caravan", StringComparison.Ordinal))
        {
            b.Torus(.46f, .56f, new(0, .58f, 0), "746757", new(82, 0, 15), surface: SurfaceKind.Wood);
            Candle(b, new(.55f, 0, 0), .65f, accent);
        }
        else if (eventId.Contains("shrine", StringComparison.Ordinal)) Shrine(b, accent, true);
        else StormAnchor(b, accent, 1.4f);
    }

    private static void Lantern(EnvironmentBuilder b, Vector3 p, float height)
    {
        b.Branch(p, p + new Vector3(0, height, 0), .075f, .065f, "6e5b42");
        b.Beam(p + new Vector3(0, height, 0), p + new Vector3(.48f, height, 0), .08f, "867b67", SurfaceKind.Metal);
        var lamp = p + new Vector3(.45f, height - .3f, 0);
        b.Cylinder(.15f, .15f, .27f, lamp, "e9bc70", glow: true);
        b.Cylinder(.21f, .04f, .15f, lamp + new Vector3(0, .2f, 0), "81715b", surface: SurfaceKind.Metal);
        b.Cylinder(.19f, .19f, .055f, lamp - new Vector3(0, .16f, 0), "81715b", surface: SurfaceKind.Metal);
        foreach (float x in new[] { -.14f, .14f })
            b.Box(new(.035f, .31f, .035f), lamp + new Vector3(x, 0, -.1f), "4a433b", surface: SurfaceKind.Metal);
    }

    private static void Candle(EnvironmentBuilder b, Vector3 p, float height, string accent)
    {
        b.Cylinder(.13f, .1f, height, p + new Vector3(0, height * .5f, 0), "c4bba0");
        b.Cylinder(.055f, .005f, .19f, p + new Vector3(0, height + .09f, 0), accent, glow: true);
        b.Cylinder(.23f, .21f, .065f, p + new Vector3(0, .04f, 0), "6b6969", surface: SurfaceKind.Metal);
    }

    private static void Wagon(EnvironmentBuilder b)
    {
        b.Box(new(2.5f, .24f, 1.55f), new(0, .86f, 0), "685442", surface: SurfaceKind.Wood);
        foreach (float z in new[] { -.78f, .78f })
        {
            b.Box(new(2.6f, .55f, .1f), new(0, 1.26f, z), "83705a", surface: SurfaceKind.Wood);
            foreach (float x in new[] { -.85f, .85f })
            {
                b.Torus(.44f, .57f, new(x, .58f, z * 1.23f), "594c40", new(90, 0, 0), surface: SurfaceKind.Wood);
                for (int spoke = 0; spoke < 4; spoke++)
                    b.Box(new(.045f, 1.0f, .05f), new(x, .58f, z * 1.23f), "9b8b70", new(0, 0, spoke * 45), surface: SurfaceKind.Wood);
            }
        }
        b.Box(new(1.5f, .48f, 1.0f), new(.15f, 1.34f, 0), "494b61", surface: SurfaceKind.Cloth);
        foreach (float z in new[] { -.46f, .46f })
            b.Branch(new(-1.1f, .9f, z), new(-2.45f, .28f, z), .05f, .05f, "83705a");
    }

    private static void Shrine(EnvironmentBuilder b, string accent, bool small)
    {
        float size = small ? .7f : 1;
        b.Cylinder(.85f * size, .8f * size, .18f, new(0, .09f, 0), "736c69", surface: SurfaceKind.Stone);
        b.Cylinder(.55f * size, .42f * size, .85f * size, new(0, .18f + .425f * size, 0), "54444a", surface: SurfaceKind.Stone);
        b.Cylinder(.67f * size, .75f * size, .15f, new(0, .25f + .85f * size, 0), "958777", surface: SurfaceKind.Stone);
        b.Torus(.34f * size, .4f * size, new(0, .34f + .85f * size, 0), accent, glow: true);
        b.Cylinder(.14f, .015f, .45f * size, new(0, .52f + .85f * size, 0), accent, glow: true);
    }

    private static void StormAnchor(EnvironmentBuilder b, string accent, float height)
    {
        b.Cylinder(.47f, .37f, .2f, new(0, .1f, 0), "6f747b", surface: SurfaceKind.Stone);
        b.Cylinder(.2f, .07f, height, new(0, height * .5f + .1f, 0), "545b68", surface: SurfaceKind.Stone);
        b.Torus(.33f, .39f, new(0, height * .65f, 0), accent, new(22, 0, 0), glow: true);
        b.Torus(.22f, .28f, new(0, height * .85f, 0), accent, new(-18, 0, 0), glow: true);
    }

    private static void Treasure(EnvironmentBuilder b, string accent, bool open, Vector3 p = default)
    {
        b.Box(new(.96f, .5f, .65f), p + new Vector3(0, .25f, 0), "635445", surface: SurfaceKind.Wood);
        b.Box(new(1.02f, .13f, .71f), p + new Vector3(0, open ? .7f : .55f, open ? .29f : 0), "9e895e", new(open ? -60 : 0, 0, 0), surface: SurfaceKind.Metal);
        foreach (float x in new[] { -.32f, .32f }) b.Box(new(.05f, .52f, .69f), p + new Vector3(x, .26f, 0), "baa06b", surface: SurfaceKind.Metal);
        if (!open) b.Cylinder(.06f, .06f, .05f, p + new Vector3(0, .4f, -.36f), accent, new(90, 0, 0), glow: true);
    }

    private static string Accent(int act, string id) => id.Contains("lantern", StringComparison.Ordinal) ? "edc67e" :
        id.Contains("caravan", StringComparison.Ordinal) ? "b6c7f1" : id.Contains("shrine", StringComparison.Ordinal) ? "e9a28a" :
        act switch { 2 => "a3da8c", 3 => "f2a66f", 4 => "aedfe7", 5 => "c7a5f3", _ => "c7bddf" };
    private static string EventName(string id) => WorldEncounterCatalog.Find(id)?.Name ?? "Optional encounter";
    private static bool IsWorldEncounterAction(string id) => id.StartsWith("event.lantern.", StringComparison.Ordinal) ||
        id.StartsWith("event.caravan.", StringComparison.Ordinal) || id.StartsWith("event.shrine.", StringComparison.Ordinal) ||
        id.StartsWith("storm.grey_march.", StringComparison.Ordinal) || id.StartsWith("storm.verdant.", StringComparison.Ordinal) ||
        id.StartsWith("storm.cinder.", StringComparison.Ordinal) || id.StartsWith("storm.spine.", StringComparison.Ordinal) ||
        id.StartsWith("storm.hollow.", StringComparison.Ordinal);
    private static void Label(Node3D parent, string text, Vector3 position, string color) => parent.AddChild(new Label3D
    {
        Text = text,
        Position = position,
        FontSize = 26,
        PixelSize = .008f,
        OutlineSize = 4,
        Modulate = new(color),
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled
    });
}
