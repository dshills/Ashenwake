using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;
using Godot;

namespace Ashenwake.Client;

public sealed record RoamingChampionWorldDisplay(int Act, bool InArena, string Stage, string ChampionId,
    ExpeditionInteraction[] Interactions);

/// <summary>Cosmetic sightings and regional arena landmarks, with no collision or independent gameplay.</summary>
public partial class RoamingChampionPresentation : Node3D
{
    private readonly Dictionary<string, Node3D> _markers = [];
    private Node3D? _scenery;
    private CharacterVisual? _sighting;
    private Vector3 _sightingOrigin;
    private Sandbox? _sandbox;
    private string _key = "";
    private double _time;
    public void Attach(Sandbox sandbox) => _sandbox = sandbox;
    public Node3D? GetInteractionVisual(string id) => _markers.GetValueOrDefault(id);
    public override void _Ready()
    {
        for (Node? parent = GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is Sandbox sandbox) { _sandbox = sandbox; break; }
    }
    public override void _Process(double delta)
    {
        if (_sighting is null || !GodotObject.IsInstanceValid(_sighting) || _sandbox?.IsPaused == true) return;
        double elapsed = Math.Clamp(delta, 0, .1);
        _time += elapsed;
        float drift = MathF.Sin((float)_time * .55f) * .36f;
        var previous = _sighting.Position; _sighting.Position = _sightingOrigin + new Vector3(drift, 0, 0);
        var tickMovement = elapsed > 0 ? (_sighting.Position - previous) * (float)(FixedStepClock.SecondsPerTick / elapsed) : Vector3.Zero;
        _sighting.Animate(delta, tickMovement, facing: new Vector3(MathF.Cos((float)_time * .55f) * .25f, 0, -1));
    }
    public void Reset()
    {
        _sighting = null;
        foreach (var marker in _markers.Values) { RemoveChild(marker); marker.QueueFree(); }
        _markers.Clear();
        if (_scenery is not null) { RemoveChild(_scenery); _scenery.QueueFree(); _scenery = null; }
        _key = ""; _time = 0;
    }
    public void Present(RoamingChampionWorldDisplay view)
    {
        var targets = view.Interactions.Where(i => i.ActionId.StartsWith("champion.", StringComparison.Ordinal)).ToArray();
        string key = $"{view.Act}:{view.InArena}:{view.Stage}:{view.ChampionId}:" + string.Join('|', targets.Select(i => $"{i.ActionId}:{i.Position.X}:{i.Position.Z}"));
        if (_key == key) return;
        Reset(); _key = key;
        string champion = view.ChampionId.Length > 0 ? view.ChampionId :
            targets.Length > 0 ? targets[0].ActionId[..targets[0].ActionId.LastIndexOf('.')] : "";
        if (view.InArena)
        {
            _scenery = new Node3D { Name = "RoamingChampionArenaLandmarks" }; AddChild(_scenery);
            var arena = new EnvironmentBuilder(_scenery, "RegionalArenaDressing"); Arena(arena, view.Act); arena.Flush();
            if (view.Stage == "Foyer") AddSighting(_scenery, champion, new(4.5f, 0, 0));
            if (view.Stage == "Claimed")
            {
                var spent = new Node3D { Name = "ClaimedChampionCache", Position = new(4.5f, 0, 0) }; _scenery.AddChild(spent);
                var cache = new EnvironmentBuilder(spent, "OpenedCache"); Treasure(cache, view.Act, true); cache.Flush();
            }
        }
        foreach (var target in targets)
        {
            var marker = new Node3D { Name = "ChampionMarker_" + target.ActionId.Replace('.', '_'), Position = new(target.Position.X * .001f, 0, target.Position.Z * .001f) };
            AddChild(marker); _markers.Add(target.ActionId, marker);
            var props = new EnvironmentBuilder(marker, "ChampionLandmark");
            if (target.ActionId.EndsWith(".sighting", StringComparison.Ordinal))
            {
                AddSighting(marker, target.ActionId[..target.ActionId.LastIndexOf('.')], Vector3.Zero);
                SightingProps(props, view.Act);
                Label(marker, ChampionName(champion), new(0, (_sighting?.Height ?? 3) + .4f, 0), Accent(view.Act));
            }
            else if (target.ActionId.EndsWith(".challenge", StringComparison.Ordinal))
            {
                props.Box(new(.78f, .1f, .52f), new(0, .05f, 0), "716958");
                props.Cylinder(.09f, .07f, 1.65f, new(0, .87f, 0), "87735d");
                props.Box(new(.58f, .8f, .06f), new(.25f, 1.2f, 0), Accent(view.Act), new(0, 0, -7), surface: SurfaceKind.Cloth);
                Label(marker, "Challenge standard", new(0, 2f, 0), Accent(view.Act));
            }
            else if (target.ActionId.EndsWith(".treasure", StringComparison.Ordinal))
            { Treasure(props, view.Act, false); Label(marker, "Champion's spoils", new(0, 1.5f, 0), "dfc58e"); }
            else if (target.ActionId.EndsWith(".exit", StringComparison.Ordinal))
            {
                props.Cylinder(.14f, .1f, 1.5f, new(0, .75f, 0), "7a6b59");
                props.Box(new(.85f, .18f, .16f), new(-.27f, 1.28f, 0), "b3a889", new(0, 0, -9));
                props.Box(new(.13f, .04f, .75f), new(-.7f, .035f, 0), Accent(view.Act));
                Label(marker, "Return to the region", new(0, 1.95f, 0), "d3c6ac");
            }
            props.Flush();
        }
    }
    private void AddSighting(Node3D parent, string champion, Vector3 position)
    {
        if (champion is not ("champion.pilgrim" or "champion.rootwidow" or "champion.tithekeeper")) return;
        _sighting = CharacterVisual.Create(champion, champion == "champion.rootwidow" ? "Rusher" : "Armored");
        _sighting.Name = "RoamingChampionSighting"; _sightingOrigin = position; _sighting.Position = position; parent.AddChild(_sighting);
    }
    private static string ChampionName(string id) => id switch
    { "champion.pilgrim" => "Bell-Torn Pilgrim", "champion.rootwidow" => "Widow of the Root", "champion.tithekeeper" => "Cinder Tithekeeper", _ => "Roaming champion" };
    private static string Accent(int act) => act switch { 2 => "af8bad", 3 => "dfac69", _ => "c4b68d" };
    private static void SightingProps(EnvironmentBuilder b, int act)
    {
        // The creature is the sighting. Small tracks suggest its nature without adding a quest billboard.
        if (act == 1)
        {
            for (int i = 0; i < 6; i++) b.Torus(.075f, .115f, new(-.7f + i * .22f, .04f, .85f), "9b947f", new(i % 2 * 35, 0, 0));
            b.Cylinder(.3f, .13f, .32f, new(.82f, .16f, .7f), "9c8b6a", new(12, 0, 25));
        }
        else if (act == 2)
        {
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.Tau / 4;
                var at = new Vector3(Mathf.Cos(angle) * 1.55f, .05f, Mathf.Sin(angle) * 1.1f);
                b.Branch(at, at + new Vector3(.3f, .28f, -.15f), .09f, .025f, "6e7555");
                b.Cylinder(.14f, .08f, .24f, at + new Vector3(.18f, .12f, 0), "a36b83");
            }
        }
        else
        {
            b.Box(new(.65f, .2f, .45f), new(-.9f, .1f, .7f), "625e57", new(0, 18, 0));
            for (int i = 0; i < 3; i++) b.Box(new(.5f, .035f, .08f), new(-.9f, .21f, .57f + i * .12f), "c18b59");
            b.Cylinder(.2f, .15f, .4f, new(.85f, .2f, .85f), "847b6a");
        }
    }
    private static void Treasure(EnvironmentBuilder b, int act, bool opened)
    {
        b.Box(new(1.08f, .48f, .68f), new(0, .3f, 0), act == 2 ? "59664b" : "645a4d");
        b.Box(new(1.14f, .15f, .72f), new(0, opened ? .85f : .62f, opened ? .25f : 0), "a9946b", opened ? new(-65, 0, 0) : Vector3.Zero);
        foreach (float x in new[] { -.34f, .34f }) b.Box(new(.07f, .49f, .71f), new(x, .31f, 0), "b2a17d");
        if (!opened) b.Box(new(.16f, .18f, .035f), new(0, .44f, -.365f), Accent(act), glow: true);
    }
    private static void Arena(EnvironmentBuilder b, int act)
    {
        // Dressing stays at the far rim. The campaign stage owns floor, terrain and collision.
        if (act == 1)
        {
            foreach (float x in new[] { -1.2f, 1.2f }) b.Box(new(.37f, 3.1f, .4f), new(x + 3.5f, 1.55f, -7.2f), "6d6570");
            b.Box(new(3.1f, .25f, .49f), new(3.5f, 3.2f, -7.2f), "938875");
            b.Cylinder(.6f, .28f, .88f, new(3.5f, 2.47f, -7.2f), "a2916d");
            for (int i = 0; i < 4; i++) b.Box(new(.62f, .35f + i % 2 * .18f, .28f), new(-6.3f + i * 1.5f, .18f, -7.2f), "767175", new(0, i * 11, -8));
        }
        else if (act == 2)
        {
            for (int i = 0; i < 6; i++)
            {
                float x = -6 + i * 2.2f;
                b.Branch(new(x, 0, -7.3f), new(x + .6f, 2.3f + i % 3 * .32f, -7.1f), .22f, .07f, "5c6950");
                b.Branch(new(x + .35f, 1.2f, -7.2f), new(x - .5f, 2.5f, -7.1f), .1f, .018f, "738260");
                b.Cylinder(.36f, .18f, .5f, new(x, .27f, -6.8f), "9b687d");
            }
        }
        else
        {
            foreach (float x in new[] { -4.8f, 4.8f })
            {
                b.Box(new(1.5f, 2.5f, .9f), new(x, 1.25f, -7.2f), "605d59");
                for (int i = 0; i < 4; i++) b.Box(new(1.15f, .07f, .025f), new(x, .45f + i * .36f, -6.72f), "c7844f", glow: true);
                b.Cylinder(.23f, .23f, 1.5f, new(x + .48f, 3.1f, -7.2f), "7c817c");
            }
            b.Box(new(5, .16f, .5f), new(0, .09f, -7.2f), "8e8269");
        }
        foreach (float z in new[] { -7.6f, 7.6f })
            for (int i = -3; i <= 3; i++) b.Box(new(.45f, .075f, .3f), new(i * 2, .045f, z), act == 2 ? "71835f" : "8b8476");
    }
    private static void Label(Node3D parent, string text, Vector3 position, string color)
        => parent.AddChild(new Label3D
        {
            Text = text,
            Position = position,
            FontSize = 25,
            PixelSize = .008f,
            OutlineSize = 4,
            Modulate = new(color),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled
        });
}
