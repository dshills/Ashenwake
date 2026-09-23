using Ashenwake.Core.Expedition;
using Godot;

namespace Ashenwake.Client;

/// <summary>Cosmetic board and evidence markers. Positions and interaction ranges come from Core.</summary>
public partial class RegionalHuntPresentation : Node3D
{
    private readonly Dictionary<string, Node3D> _markers = [];
    private string _key = "";
    public Node3D? GetInteractionVisual(string id) => _markers.GetValueOrDefault(id);

    public void Present(IReadOnlyList<ExpeditionInteraction> interactions, int act)
    {
        var targets = interactions.Where(i => i.ActionId.StartsWith("regional-hunts.", StringComparison.Ordinal) || i.ActionId.StartsWith("hunt.regional.", StringComparison.Ordinal)).ToArray();
        string key = act + ":" + string.Join('|', targets.Select(t => t.ActionId + ":" + t.Position.X + ":" + t.Position.Z));
        if (_key == key) return; _key = key;
        foreach (var node in _markers.Values) { RemoveChild(node); node.QueueFree(); }
        _markers.Clear();
        foreach (var target in targets)
        {
            var root = new Node3D { Name = "HuntMarker_" + target.ActionId.Replace('.', '_'), Position = new(target.Position.X * .001f, 0, target.Position.Z * .001f) };
            AddChild(root); _markers.Add(target.ActionId, root);
            var builder = new EnvironmentBuilder(root, "HuntProps");
            if (target.ActionId == "regional-hunts.board")
            {
                foreach (float x in new[] { -1.05f, 1.05f })
                { builder.Box(new(.18f, 2.3f, .2f), new(x, 1.15f, .12f), "564b40"); builder.Cylinder(.25f, .22f, .14f, new(x, .07f, .12f), "7d8076"); }
                builder.Box(new(2.55f, 1.25f, .18f), new(0, 1.65f, .12f), "453e38");
                builder.Box(new(2.75f, .15f, .44f), new(0, 2.34f, .12f), "88724d");
                string[] colors = ["a49a84", "8eaa70", "c37e4a"];
                for (int i = 0; i < 3; i++)
                {
                    float x = (i - 1) * .76f;
                    builder.Box(new(.62f, .83f, .025f), new(x, 1.65f, -.005f), colors[i]);
                    builder.Cylinder(.065f, .065f, .04f, new(x, 1.34f, -.04f), "803e36", new(90, 0, 0));
                    builder.Box(new(.35f, .06f, .015f), new(x, 1.5f, -.024f), "383333");
                    if (i == 0) { builder.Cylinder(.16f, .08f, .24f, new(x, 1.86f, -.05f), "47413c"); }
                    else if (i == 1)
                    { builder.Box(new(.32f, .06f, .015f), new(x, 1.84f, -.028f), "384a30", new(0, 0, 35)); builder.Box(new(.32f, .06f, .015f), new(x, 1.84f, -.029f), "384a30", new(0, 0, -35)); }
                    else builder.Box(new(.22f, .27f, .025f), new(x, 1.85f, -.03f), "63391e", new(0, 0, 35));
                }
                Label(root, "GREYHAVEN HUNT BOARD", new(0, 2.85f, 0), "ead1a0");
            }
            else
            {
                string color = act switch { 2 => "8fb876", 3 => "e59859", _ => "b9b6c8" };
                builder.Torus(.65f, .71f, new(0, .025f, 0), color, glow: true);
                for (int i = 0; i < 3; i++)
                {
                    float x = (i - 1) * .27f;
                    if (act == 2) builder.Box(new(.1f, .65f + i * .12f, .1f), new(x, .25f, 0), "637b45", new(20, 0, (i - 1) * 25));
                    else if (act == 3) builder.Box(new(.3f, .15f, .33f), new(x, .1f, (i % 2) * .3f), "965d36", new(0, i * 27, 0));
                    else builder.Box(new(.11f, .13f, .58f), new(x, .1f, 0), "c1b9a4", new(0, (i - 1) * 20, 0));
                }
                Label(root, target.Name, new(0, 1.25f, 0), color);
            }
            builder.Flush();
        }
    }

    private static void Label(Node3D parent, string text, Vector3 position, string color)
        => parent.AddChild(new Label3D
        {
            Text = text,
            Position = position,
            FontSize = 34,
            PixelSize = .009f,
            OutlineSize = 5,
            Modulate = new(color),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true
        });
}
