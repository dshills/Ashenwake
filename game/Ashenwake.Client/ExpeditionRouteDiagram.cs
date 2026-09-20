using Godot;

namespace Ashenwake.Client;

public sealed record ExpeditionRouteNode(string Title, string Subtitle, string State);

/// <summary>Read-only expedition progress with native text and non-color state indicators.</summary>
public partial class ExpeditionRouteDiagram : Control
{
    private ExpeditionRouteNode[] _nodes = [];
    private readonly List<Label> _titles = [], _subtitles = [], _states = [], _numbers = [];
    private bool _hunt;

    public ExpeditionRouteDiagram()
    {
        Name = "ExpeditionRouteDiagram";
        CustomMinimumSize = new(300, 142);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        ClipContents = true;
    }

    public void SetRoute(IReadOnlyList<ExpeditionRouteNode> nodes, bool hunt = false)
    {
        if (_hunt == hunt && _nodes.SequenceEqual(nodes)) return;
        _hunt = hunt; _nodes = nodes.ToArray();
        while (_titles.Count > nodes.Count)
        {
            RemoveLast(_titles); RemoveLast(_subtitles); RemoveLast(_states); RemoveLast(_numbers);
        }
        while (_titles.Count < nodes.Count)
        {
            int index = _titles.Count;
            AddCaption(_titles, "ExpeditionRouteTitle" + index, 11, 2);
            AddCaption(_subtitles, "ExpeditionRouteSubtitle" + index, 10, 2);
            AddCaption(_states, "ExpeditionRouteState" + index, 9, 1);
            AddCaption(_numbers, "ExpeditionRouteNumber" + index, 11, 1);
        }
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i]; Color accent = StateColor(node.State);
            string state = StateText(node.State);
            _titles[i].Text = node.Title;
            _subtitles[i].Text = node.Subtitle;
            _states[i].Text = state;
            _numbers[i].Text = node.State == "completed" ? "✓" : node.State == "failed" ? "×" : (i + 1).ToString();
            _titles[i].AddThemeColorOverride("font_color", node.State == "upcoming" ? new Color("b7c4ca") : new("e5ece9"));
            _subtitles[i].AddThemeColorOverride("font_color", new("9fb2ba"));
            _states[i].AddThemeColorOverride("font_color", accent);
            _numbers[i].AddThemeColorOverride("font_color", accent);
            string description = $"{(hunt ? "Phase" : "Room")} {i + 1}: {node.Title}\n{node.Subtitle}\n{state}";
            foreach (var label in new[] { _titles[i], _subtitles[i], _states[i], _numbers[i] }) label.TooltipText = description;
        }
        CustomMinimumSize = new(Math.Max(300, nodes.Count * 72), 142);
        LayoutLabels(); QueueRedraw();
    }

    public override void _Ready() => LayoutLabels();

    public override void _Notification(int what)
    {
        if (what == NotificationResized) { LayoutLabels(); QueueRedraw(); }
    }

    private void LayoutLabels()
    {
        if (_nodes.Length == 0) return;
        float cellWidth = Size.X / _nodes.Length;
        for (int i = 0; i < _nodes.Length; i++)
        {
            float left = cellWidth * i + 4, width = Math.Max(1, cellWidth - 8);
            _numbers[i].Position = new(cellWidth * (i + .5f) - 13, 16); _numbers[i].Size = new(26, 26);
            _titles[i].Position = new(left, 49); _titles[i].Size = new(width, 34);
            _subtitles[i].Position = new(left, 86); _subtitles[i].Size = new(width, 30);
            _states[i].Position = new(left, 121); _states[i].Size = new(width, 16);
        }
    }

    public override void _Draw()
    {
        if (_nodes.Length == 0 || Size.X <= 0 || Size.Y <= 0) return;
        float cellWidth = Size.X / _nodes.Length;
        for (int i = 0; i < _nodes.Length - 1; i++)
        {
            Vector2 from = new(cellWidth * (i + .5f) + 15, 29), to = new(cellWidth * (i + 1.5f) - 15, 29);
            Color line = _nodes[i].State == "completed" ? new("80a794") : new("3b515d");
            DrawLine(from, to, line, 1.5f, true);
            Vector2 mid = (from + to) * .5f;
            DrawPolyline([mid + new Vector2(-3, -3), mid + new Vector2(0, 0), mid + new Vector2(-3, 3)], line, 1.5f, true);
        }
        for (int i = 0; i < _nodes.Length; i++)
        {
            Vector2 at = new(cellWidth * (i + .5f), 29);
            Color accent = StateColor(_nodes[i].State);
            DrawCircle(at, 13, new("102028"));
            if (_hunt)
                DrawPolyline([at + new Vector2(0, -14), at + new Vector2(14, 0), at + new Vector2(0, 14), at + new Vector2(-14, 0), at + new Vector2(0, -14)], accent, 1.5f, true);
            else DrawArc(at, 13, 0, Mathf.Tau, 32, accent, 1.5f, true);
            if (_nodes[i].State == "current")
            {
                DrawArc(at, 17, 0, Mathf.Tau, 32, accent.Darkened(.35f), 1, true);
                DrawPolygon([at + new Vector2(-3, -23), at + new Vector2(3, -23), at + new Vector2(0, -19)], [accent]);
            }
        }
    }

    private static string StateText(string state) => state switch
    {
        "completed" => "CLEARED",
        "current" => "CURRENT",
        "failed" => "FAILED",
        _ => "UPCOMING"
    };

    private static Color StateColor(string state) => state switch
    {
        "completed" => new("a4d3b5"),
        "current" => new("f0d8a6"),
        "failed" => new("e4a18b"),
        _ => new("8197a6")
    };

    private void AddCaption(List<Label> target, string name, int fontSize, int lines)
    {
        var label = new Label
        {
            Name = name,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MaxLinesVisible = lines,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Pass,
            FocusMode = FocusModeEnum.None
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        AddChild(label); target.Add(label);
    }

    private void RemoveLast(List<Label> target)
    {
        var label = target[^1]; target.RemoveAt(target.Count - 1);
        RemoveChild(label); label.QueueFree();
    }
}
