using Ashenwake.Core.Endgame;
using Ashenwake.Core.Expedition;
using Godot;

namespace Ashenwake.Client;

public sealed record SecretChamberWorldDisplay(int Act, bool InChamber, string Stage, string LandmarkId,
    ExpeditionInteraction[] Interactions);

/// <summary>Cosmetic evidence and chamber landmarks; positions and availability come from Core.</summary>
public partial class SecretChamberPresentation : Node3D
{
    private readonly Dictionary<string, Node3D> _markers = [];
    private Node3D? _scenery;
    private string _key = "";
    public Node3D? GetInteractionVisual(string id) => _markers.GetValueOrDefault(id);
    public void Reset()
    {
        foreach (var marker in _markers.Values) { RemoveChild(marker); marker.QueueFree(); }
        _markers.Clear();
        if (_scenery is not null) { RemoveChild(_scenery); _scenery.QueueFree(); _scenery = null; }
        _key = "";
    }
    public void Present(SecretChamberWorldDisplay view)
    {
        var targets = view.Interactions.Where(i => i.ActionId.StartsWith("secret.", StringComparison.Ordinal)).ToArray();
        string key = $"{view.Act}:{view.InChamber}:{view.Stage}:{view.LandmarkId}:" +
            string.Join('|', targets.Select(i => $"{i.ActionId}:{i.Position.X}:{i.Position.Z}"));
        if (_key == key) return;
        Reset(); _key = key;
        var definition = SecretChamberCatalog.Find(view.LandmarkId) ??
            SecretChamberCatalog.Definitions.FirstOrDefault(d => targets.Any(i => i.ActionId.StartsWith(d.Id + ".", StringComparison.Ordinal)));
        if (view.InChamber)
        {
            _scenery = new Node3D { Name = "SecretChamberLandmark" }; AddChild(_scenery);
            Interior(new EnvironmentBuilder(_scenery, "ChamberArchitecture"), view.Act);
            if (view.Stage == "Claimed")
            {
                var spent = new Node3D { Name = "OpenedSecretTreasure", Position = new(SecretChamberCatalog.TreasurePosition.X * .001f, 0, SecretChamberCatalog.TreasurePosition.Z * .001f) };
                _scenery.AddChild(spent); var chest = new EnvironmentBuilder(spent, "SpentReliquary"); Treasure(chest, view.Act, true); chest.Flush();
            }
        }
        else if (definition is not null && targets.Any(i => i.ActionId.StartsWith(definition.Id + ".", StringComparison.Ordinal)))
        {
            _scenery = new Node3D { Name = "SecretWall", Position = new(definition.EntrancePosition.X * .001f, 0, definition.EntrancePosition.Z * .001f) };
            AddChild(_scenery);
            var wall = new EnvironmentBuilder(_scenery, "ConcealedArchitecture");
            Entrance(wall, view.Act, targets.Any(i => i.ActionId.EndsWith(".enter", StringComparison.Ordinal))); wall.Flush();
        }
        foreach (var target in targets)
        {
            var marker = new Node3D { Name = "SecretMarker_" + target.ActionId.Replace('.', '_'), Position = new(target.Position.X * .001f, 0, target.Position.Z * .001f) };
            AddChild(marker); _markers.Add(target.ActionId, marker);
            var builder = new EnvironmentBuilder(marker, "SecretProps");
            if (target.ActionId.Contains(".puzzle.", StringComparison.Ordinal)) Clue(builder, view.Act, target.ActionId[^1] - '0');
            else if (target.ActionId.EndsWith(".enter", StringComparison.Ordinal))
            {
                builder.Torus(.58f, .64f, new(0, .025f, -.35f), Accent(view.Act), glow: true);
                Label(marker, definition?.Name ?? "Revealed passage", new(0, 3.3f, 0), Accent(view.Act));
            }
            else if (target.ActionId.EndsWith(".challenge", StringComparison.Ordinal))
            {
                Threshold(builder, view.Act); Label(marker, "Guardian's threshold", new(0, 1.95f, 0), Accent(view.Act));
            }
            else if (target.ActionId.EndsWith(".treasure", StringComparison.Ordinal))
            {
                Treasure(builder, view.Act, false); Label(marker, "Sealed reliquary", new(0, 1.5f, 0), "dfc790");
            }
            else if (target.ActionId.EndsWith(".exit", StringComparison.Ordinal))
            {
                Exit(builder, view.Act); Label(marker, "Return to the region", new(0, 2.5f, 0), "c3c4b8");
            }
            builder.Flush();
        }
    }
    private static string Accent(int act) => act switch { 2 => "9cb57c", 3 => "9cced3", _ => "b5b0cb" };
    private static void Clue(EnvironmentBuilder b, int act, int step)
    {
        // Ordinary regional objects, without a glowing ring or a spoiler-bearing world label.
        if (act == 1)
        {
            if (step == 2)
            {
                b.Box(new(1.05f, .12f, .4f), new(0, 1.05f, 0), "625749");
                foreach (float x in new[] { -.45f, .45f }) b.Box(new(.12f, 1.05f, .16f), new(x, .52f, 0), "625749");
                b.Cylinder(.22f, .11f, .32f, new(0, .85f, 0), "968a65");
                b.Box(new(.25f, .07f, .09f), new(.36f, .06f, -.26f), "5e5c62");
            }
            else
            {
                b.Box(new(.9f, .16f, .64f), new(0, .14f, 0), "767479", new(-20, 0, 0));
                for (int i = 0; i < 3; i++) b.Box(new(.54f - i * .08f, .015f, .022f), new(0, .244f + i * .025f, -.14f + i * .11f), "c5bbae", new(-20, 0, 0));
                b.Box(new(.025f, .024f, .42f), new(.14f, .255f, .01f), "363b43", new(-20, 18, 0));
                if (step == 3) b.Cylinder(.07f, .07f, .34f, new(-.5f, .17f, -.1f), "b7afa0");
            }
        }
        else if (act == 2)
        {
            b.Cylinder(.5f, .42f, .12f, new(0, .06f, 0), "4d6147");
            if (step == 2)
            {
                b.Cylinder(.26f, .37f, .22f, new(0, .21f, 0), "78766a");
                b.Cylinder(.29f, .29f, .018f, new(0, .325f, 0), "7ba1a0");
            }
            else
            {
                for (int i = 0; i < 3; i++) b.Branch(new(-.5f + i * .34f, .08f, .16f), new(.1f + i * .13f, .25f + i * .15f, -.16f), .06f, .025f, "6d7552");
                if (step == 3) b.Cylinder(.2f, .11f, .34f, new(0, .25f, 0), "a6a383");
                else b.Box(new(.35f, .55f, .13f), new(0, .31f, 0), "738565", new(0, 0, -10));
            }
        }
        else
        {
            b.Cylinder(.16f, .16f, .95f, new(0, .5f, 0), "637478");
            b.Box(new(.52f, .11f, .32f), new(0, .075f, 0), "535c63");
            if (step == 2) b.Torus(.23f, .3f, new(0, .77f, -.16f), "9ba9a4", new(90, 0, 0));
            else
            {
                b.Cylinder(.25f, .25f, .08f, new(0, .93f, -.06f), "a3b9ba", new(90, 0, 0));
                b.Box(new(.025f, .18f, .02f), new(.03f, .96f, -.115f), "354953", new(0, 0, step == 1 ? 32 : -60));
            }
            b.Box(new(.22f, .04f, .12f), new(.26f, .62f, -.02f), "a5c6c8");
        }
    }
    private static void Entrance(EnvironmentBuilder b, int act, bool open)
    {
        string stone = act == 2 ? "58604d" : act == 3 ? "58626a" : "666571";
        foreach (float x in new[] { -.93f, .93f })
        {
            b.Box(new(.48f, 2.65f, .65f), new(x, 1.32f, .26f), stone);
            b.Box(new(.65f, .18f, .8f), new(x, 2.64f, .26f), "8d8b7c");
        }
        b.Box(new(2.5f, .32f, .72f), new(0, 2.83f, .26f), stone);
        if (!open)
        {
            b.Box(new(1.42f, 2.47f, .4f), new(0, 1.24f, .34f), stone);
            b.Box(new(.028f, 1.4f, .03f), new(.12f, 1.3f, .12f), "343c42", new(0, 0, -9));
            if (act == 2)
                for (int i = 0; i < 4; i++) b.Branch(new(-.85f + i * .4f, .02f, -.05f), new(.5f - i * .32f, 2.45f, .02f), .1f, .04f, "53674a");
            if (act == 3) b.Cylinder(.11f, .11f, 2.3f, new(.56f, 1.18f, -.02f), "8da4a8");
        }
        else
        {
            b.Box(new(1.38f, 2.44f, .08f), new(0, 1.24f, .59f), "1b252e");
            foreach (float x in new[] { -.67f, .67f }) b.Box(new(.032f, 2.24f, .04f), new(x, 1.21f, .04f), Accent(act), glow: true);
        }
    }
    private static void Threshold(EnvironmentBuilder b, int act)
    {
        foreach (float z in new[] { -1.1f, 1.1f })
        {
            b.Cylinder(.28f, .2f, 1.25f, new(0, .62f, z), "68696a");
            b.Cylinder(.19f, .16f, .08f, new(0, 1.3f, z), Accent(act), glow: true);
        }
        b.Box(new(.075f, .012f, 2.2f), new(0, .025f, 0), Accent(act), glow: true);
    }
    private static void Treasure(EnvironmentBuilder b, int act, bool open)
    {
        b.Box(new(1.15f, .2f, .8f), new(0, .1f, 0), "67676a");
        b.Box(new(.92f, .5f, .61f), new(0, .44f, 0), act == 2 ? "556b4d" : "5a5364");
        b.Box(new(.98f, .14f, .66f), new(0, open ? .88f : .73f, open ? .3f : 0), "a59570", open ? new(-65, 0, 0) : Vector3.Zero);
        foreach (float x in new[] { -.31f, .31f }) b.Box(new(.065f, .52f, .63f), new(x, .45f, 0), "ac9870");
        if (!open) b.Cylinder(.065f, .065f, .07f, new(0, .6f, -.345f), Accent(act), new(90, 0, 0), glow: true);
    }
    private static void Exit(EnvironmentBuilder b, int act)
    {
        foreach (float z in new[] { -.75f, .75f }) b.Box(new(.32f, 2.05f, .32f), new(0, 1.02f, z), "777875");
        b.Box(new(.4f, .25f, 1.95f), new(0, 2.15f, 0), "8e8e82");
        b.Box(new(.04f, 1.85f, 1.3f), new(-.08f, 1.02f, 0), "34404a");
        b.Torus(.48f, .54f, new(.35f, .025f, 0), Accent(act), glow: true);
    }
    private static void Interior(EnvironmentBuilder b, int act)
    {
        // Tall regional landmarks stay along the far edge, leaving the combat lanes readable.
        var center = new Vector3(4.8f, 0, -6.5f);
        if (act == 1)
        {
            foreach (float x in new[] { -1.6f, 1.6f }) b.Box(new(.48f, 3.8f, .6f), center + new Vector3(x, 1.9f, 0), "666375");
            b.Box(new(3.8f, .35f, .72f), center + new Vector3(0, 3.95f, 0), "82788b");
            b.Cylinder(.95f, .47f, 1.45f, center + new Vector3(0, 2.65f, 0), "a29776");
            b.Torus(.8f, .96f, center + new Vector3(0, 1.96f, 0), "b4aa8c");
            for (int i = 0; i < 5; i++) b.Box(new(.5f, .15f, .62f), new(-2.5f + i * 1.4f, .075f, 6.7f), "797583");
        }
        else if (act == 2)
        {
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * .68f;
                b.Branch(center + new Vector3(x, .05f, .2f), center + new Vector3(x * .52f, 3.7f - Math.Abs(x) * .5f, -.12f), .24f, .07f, "5a6b4e");
                b.Leaf(1.25f, .55f, .16f, center + new Vector3(x, 2.1f, 0), "789466", new(-35, i * 40, 25));
            }
            for (int i = 0; i < 5; i++) b.Cylinder(.34f, .19f, .7f, center + new Vector3((i - 2) * .63f, .35f, 1 + i % 2 * .4f), "b0ae89");
        }
        else
        {
            b.Cylinder(1.35f, 1.12f, 2.8f, center + new Vector3(0, 1.4f, 0), "56616d");
            b.Cylinder(1.43f, 1.43f, .18f, center + new Vector3(0, 2.77f, 0), "a0b9bc");
            b.Box(new(1.3f, 1.1f, .12f), center + new Vector3(0, .95f, 1.25f), "21333f");
            for (int i = 0; i < 4; i++) b.Box(new(.04f, .9f, .15f), center + new Vector3(-.45f + i * .3f, .95f, 1.33f), "9ab5b8");
            foreach (float x in new[] { -1.8f, 1.8f }) b.Cylinder(.16f, .16f, 3.5f, center + new Vector3(x, 1.75f, 0), "91a9ac");
        }
        foreach (float z in new[] { -7.8f, 7.8f })
            for (int i = -3; i <= 3; i++) b.Box(new(.55f, .08f, .32f), new(i * 2.5f, .05f, z), act == 2 ? "6c7d59" : "828480");
        b.Flush();
    }
    private static void Label(Node3D parent, string text, Vector3 position, string color)
        => parent.AddChild(new Label3D
        {
            Text = text,
            Position = position,
            FontSize = 27,
            PixelSize = .008f,
            OutlineSize = 4,
            Modulate = new(color),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled
        });
}
