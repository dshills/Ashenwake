using System.Globalization;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Simulation;
using Godot;

namespace Ashenwake.Client;

/// <summary>One borrowed memory, projected from Core without advancing its timers or availability.</summary>
public partial class EchoesMemoryHud : Control
{
    private static readonly Color MemoryColor = new("a8d3ed"), RestoredColor = new("a9d8bf"), WarningColor = new("f2cc80"), DangerColor = new("f1a39b");
    private Panel _background = null!, _noticePanel = null!;
    private StyleBoxFlat _noticeStyle = null!, _timerFill = null!;
    private SkillIcon _icon = null!;
    private Label _title = null!, _source = null!, _state = null!, _mind = null!, _notice = null!, _noticeAction = null!, _outcome = null!;
    private ProgressBar _timer = null!;
    private Button _bind = null!, _cast = null!, _release = null!;
    private readonly List<ColorRect> _steps = [];
    private RenderKey? _lastRender;
    private BorrowedMemoryView? _memory;
    private bool _paused;

    public event Action? BindRequested, CastRequested, ReleaseRequested;
    public string MemoryStatus => _memory?.Status ?? "";
    public string StateText => _state?.Text ?? "";
    public string MindText => _mind?.Text ?? "";
    public string HazardText => _memory?.HazardStage is "Warning" or "Active" ? (_notice?.Text ?? "") + "\n" + (_noticeAction?.Text ?? "") : "";
    public string HazardStage => _memory?.HazardStage ?? "None";
    public int RemainingTicks => _memory?.RemainingTicks ?? 0;
    public int HazardRemainingTicks => _memory?.HazardRemainingTicks ?? 0;
    public bool BindEnabled => !_paused && _memory is { Status: "Offered", CanBind: true };
    public bool CastEnabled => !_paused && _memory is { Status: "Bound", RemainingTicks: > 0 } memory && memory.EchoSkillId.Length > 0;
    public bool ReleaseEnabled => !_paused && _memory?.CanRelease == true;

    public EchoesMemoryHud()
    {
        Name = "EchoesMemoryHud";
        CustomMinimumSize = new(270, 136);
        Size = new(280, 180);
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        Visible = false;
    }

    public override void _Ready() => EnsureChildren();

    public override void _Notification(int what)
    { if (what == NotificationResized && _background is not null) LayoutChildren(); }

    public void SetView(BorrowedMemoryView? memory, ExperimentRules rules, string sourceName, string mindName, string echoKey, bool paused)
    {
        EnsureChildren(); _memory = memory; _paused = paused; Visible = memory is not null;
        if (memory is null) { _lastRender = null; return; }
        bool hostile = memory.HazardStage is "Warning" or "Active";
        _timer.Visible = hostile || memory.Status == "Bound";
        _timer.MaxValue = Math.Max(1, hostile ? memory.HazardStage == "Warning" ? rules.WarningTicks : rules.HazardTicks : rules.EchoLifetimeTicks);
        _timer.Value = Math.Max(0, hostile ? memory.HazardRemainingTicks : memory.RemainingTicks);
        var render = new RenderKey(memory.Status, memory.SuppressedMindId, memory.SourceActorId, memory.CanBind, memory.CanRelease,
            memory.EchoSkillId, Tenths(memory.RemainingTicks), memory.HazardStage, Tenths(memory.HazardRemainingTicks),
            sourceName, mindName, echoKey, paused);
        if (_lastRender == render) return;
        _lastRender = render;

        string source = sourceName.Length > 0 ? sourceName : "Elite memory";
        _source.Text = memory.SourceActorId > 0 ? "FROM " + source : "Seek an elite within this Fracture";
        _state.Text = memory.Status switch
        {
            "Pending" => "01 · SEEK AN ELITE",
            "Offered" => "02 · MEMORY REVEALED",
            "Bound" => "03 · ECHO BOUND · " + Seconds(memory.RemainingTicks),
            "Spent" => "04 · ECHO USED",
            "Released" => "MEMORY RELEASED",
            "Expired" => "MEMORY FADED",
            "Lost" => "MEMORY LOST",
            _ => "BORROWED MEMORY"
        };
        _mind.Text = memory.SuppressedMindId.Length > 0
            ? "SUPPRESSED · " + (mindName.Length > 0 ? mindName : "Owned Mind effect")
            : memory.CanRelease ? "MIND SOCKET · On loan" : "MIND RESTORED · " + (mindName.Length > 0 ? mindName : "Owned anatomy active");
        _mind.AddThemeColorOverride("font_color", memory.CanRelease ? WarningColor : RestoredColor);
        _state.AddThemeColorOverride("font_color", memory.Status is "Lost" or "Expired" ? DangerColor : MemoryColor);

        var guidance = memory.Status switch
        {
            "Pending" => ("Defeat an elite.", "Approach its memory to bind an Echo."),
            "Offered" => memory.CanBind ? ("Memory in reach.", "Bind its Echo or release the loan.") : ("Approach the marked memory.", "Finish your current action to bind."),
            "Bound" => memory.EchoSkillId.Length > 0 ? ("One borrowed cast.", "Then leave the hostile Storm circle.") : ("Echo bound.", "Wait for a casting opportunity."),
            "Spent" => ("Echo used.", "Finish the Fracture to record it."),
            "Released" => ("The memory loan ended.", "The Echo was not used."),
            "Expired" => ("The memory expired.", "The Echo was not used."),
            "Lost" => ("Death ended this memory.", "Continue your Fracture."),
            _ => ("", "")
        };
        _notice.Text = memory.HazardStage switch
        {
            "Warning" => "HOSTILE STORM · " + Seconds(memory.HazardRemainingTicks),
            "Active" => "HOSTILE STORM ACTIVE · " + Seconds(memory.HazardRemainingTicks),
            _ => guidance.Item1
        };
        _noticeAction.Text = memory.HazardStage switch
        {
            "Warning" => "Move or dodge out before it strikes.",
            "Active" => "Leave the circle. It can damage you.",
            _ => guidance.Item2
        };
        Color accent = memory.HazardStage == "Active" ? DangerColor : memory.HazardStage == "Warning" ? WarningColor : MemoryColor;
        _noticeStyle.BgColor = hostile ? new("34242bec") : new("142b3ae8");
        _noticeStyle.BorderColor = accent.Darkened(.35f);
        _notice.AddThemeColorOverride("font_color", accent);
        _noticeAction.AddThemeColorOverride("font_color", accent);
        _timerFill.BgColor = accent;
        _icon.Modulate = memory.Status is "Lost" or "Expired" or "Released" ? new(.6f, .64f, .69f) : Colors.White;

        int stage = memory.Status switch { "Pending" => 1, "Offered" => 2, "Bound" => 3, "Spent" => 4, _ => 0 };
        for (int index = 0; index < _steps.Count; index++) _steps[index].Color = index < stage ? MemoryColor : new("314755");
        _bind.Visible = memory.Status == "Offered"; _bind.Disabled = !BindEnabled;
        _cast.Visible = memory.Status == "Bound"; _cast.Disabled = !CastEnabled;
        _release.Visible = memory.CanRelease; _release.Disabled = !ReleaseEnabled;
        _cast.Text = "Cast Echo [" + echoKey + "]";
        _bind.TooltipText = paused ? "Resume play to bind this memory." : memory.CanBind ? "Bind the nearby elite memory." : "Move within reach of the marked memory and finish your current action.";
        _cast.TooltipText = paused ? "Resume play to cast the borrowed Echo." : CastEnabled
            ? "Cast Echo Storm once. A hostile Storm circle follows beneath you; move or dodge away."
            : "The borrowed Echo is not currently available.";
        _release.TooltipText = paused ? "Resume play to release the memory." : "Release this loan and restore your owned Mind effect without casting the Echo.";
        _outcome.Visible = !memory.CanRelease;
        _outcome.Text = memory.Status == "Spent" ? "Continue the Fracture · outcome pending" : "Continue your Fracture";
        TooltipText = "BORROWED MEMORY\n" + _source.Text + "\n" + _state.Text + "\n" + _mind.Text + "\n" + _notice.Text + "\n" + _noticeAction.Text;
    }

    private void EnsureChildren()
    {
        if (_background is not null) return;
        _background = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        _background.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("102031f5"),
            BorderColor = new("7399b4"),
            BorderWidthLeft = 2,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        });
        AddChild(_background);
        _icon = new SkillIcon { Name = "EchoesMemoryIcon", Position = new(10, 8), Size = new(30, 30) };
        _icon.SetSkill("skill.echo_storm", "Arcanist", "Area"); AddChild(_icon);
        _title = Caption("EchoesMemoryTitle", 12); _title.Text = "BORROWED MEMORY";
        _source = Caption("EchoesMemorySource", 10); _source.AddThemeColorOverride("font_color", new Color("b8c7d2"));
        for (int index = 0; index < 4; index++)
        {
            var step = new ColorRect { Name = "EchoesMemoryStep" + (index + 1), Color = new("314755"), MouseFilter = MouseFilterEnum.Ignore };
            AddChild(step); _steps.Add(step);
        }
        _state = Caption("EchoesMemoryState", 12); _mind = Caption("EchoesMemoryMind", 10);
        _noticePanel = new Panel { Name = "EchoesMemoryNoticePanel", MouseFilter = MouseFilterEnum.Ignore };
        _noticeStyle = new StyleBoxFlat { BgColor = new("142b3ae8"), BorderColor = new("527b92"), BorderWidthLeft = 2 };
        _noticePanel.AddThemeStyleboxOverride("panel", _noticeStyle); AddChild(_noticePanel);
        // Separate fixed lines keep the counterplay instruction visible even when
        // the compact card cannot fit Godot's wrapped-label ellipsis layout.
        _notice = Caption("EchoesMemoryNotice", 11);
        _noticeAction = Caption("EchoesMemoryCounterplay", 11);
        foreach (var line in new[] { _notice, _noticeAction })
        {
            line.AutowrapMode = TextServer.AutowrapMode.Off;
            line.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            line.AddThemeConstantOverride("line_spacing", 0);
        }
        _timer = new ProgressBar { Name = "EchoesMemoryTimer", MouseFilter = MouseFilterEnum.Ignore, ShowPercentage = false, MinValue = 0, Step = 1 };
        _timer.AddThemeFontSizeOverride("font_size", 1);
        _timer.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new("233b4c") });
        _timerFill = new StyleBoxFlat { BgColor = MemoryColor };
        _timer.AddThemeStyleboxOverride("fill", _timerFill); AddChild(_timer);
        _bind = ActionButton("EchoesBind", "Bind memory [H]", () => { if (BindEnabled) BindRequested?.Invoke(); });
        _cast = ActionButton("EchoesCast", "Cast Echo", () => { if (CastEnabled) CastRequested?.Invoke(); });
        _release = ActionButton("EchoesRelease", "Release", () => { if (ReleaseEnabled) ReleaseRequested?.Invoke(); });
        _outcome = Caption("EchoesMemoryOutcome", 10); _outcome.AddThemeColorOverride("font_color", new Color("9fb4c4"));
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        if (_outcome is null) return;
        float width = Size.X, inside = width - 20;
        bool compact = Size.Y < 170;
        _background.Size = Size;
        _source.AddThemeFontSizeOverride("font_size", compact ? 9 : 10);
        _state.AddThemeFontSizeOverride("font_size", compact ? 11 : 12);
        _mind.AddThemeFontSizeOverride("font_size", compact ? 9 : 10);
        _notice.AddThemeFontSizeOverride("font_size", compact ? 10 : 11);
        _noticeAction.AddThemeFontSizeOverride("font_size", compact ? 10 : 11);
        foreach (var button in new[] { _bind, _cast, _release }) button.AddThemeFontSizeOverride("font_size", compact ? 10 : 11);
        Place(_title, new(46, 7), new(width - 56, 17)); Place(_source, new(46, compact ? 23 : 25), new(width - 56, compact ? 12 : 15));
        float stepWidth = (inside - 9) / 4;
        for (int index = 0; index < _steps.Count; index++)
        { _steps[index].Visible = !compact; Place(_steps[index], new(10 + index * (stepWidth + 3), 45), new(stepWidth, 3)); }
        Place(_state, new(10, compact ? 39 : 53), new(inside, compact ? 15 : 17));
        Place(_mind, new(10, compact ? 55 : 75), new(inside, compact ? 12 : 15));
        Place(_noticePanel, new(10, compact ? 70 : 95), new(inside, compact ? 28 : 32));
        float noticeTop = compact ? 70 : 95, noticeLineHeight = compact ? 14 : 16;
        Place(_notice, new(16, noticeTop), new(inside - 12, noticeLineHeight));
        Place(_noticeAction, new(16, noticeTop + noticeLineHeight), new(inside - 12, noticeLineHeight));
        Place(_timer, new(10, compact ? 100 : 131), new(inside, compact ? 2 : 3));
        float secondaryWidth = 82;
        float actionY = compact ? 104 : 140;
        Place(_bind, new(10, actionY), new(inside - secondaryWidth - 6, 28));
        Place(_cast, new(10, actionY), new(inside - secondaryWidth - 6, 28));
        Place(_release, new(width - 10 - secondaryWidth, actionY), new(secondaryWidth, 28));
        Place(_outcome, new(10, actionY + 4), new(inside, 18));
    }

    private Label Caption(string name, int fontSize)
    {
        var label = new Label { Name = name, ClipText = true, MouseFilter = MouseFilterEnum.Ignore, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        label.AddThemeFontSizeOverride("font_size", fontSize); label.AddThemeColorOverride("font_color", new Color("e2e9ed"));
        AddChild(label); return label;
    }

    private Button ActionButton(string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, ClipText = true, Visible = false };
        button.AddThemeFontSizeOverride("font_size", 11);
        button.AddThemeStyleboxOverride("normal", ButtonStyle(new("223e52"), new("769cb7")));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(new("31536b"), new("b6d5ec")));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(new("172d40"), new("d2e5f2")));
        button.AddThemeStyleboxOverride("disabled", ButtonStyle(new("182936"), new("445967")));
        button.Pressed += action; AddChild(button); return button;
    }

    private static StyleBoxFlat ButtonStyle(Color fill, Color border) => new()
    {
        BgColor = fill,
        BorderColor = border,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 3,
        CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3,
        CornerRadiusBottomRight = 3,
        ContentMarginLeft = 5,
        ContentMarginRight = 5,
        ContentMarginTop = 2,
        ContentMarginBottom = 2
    };
    private static void Place(Control control, Vector2 position, Vector2 size) { control.Position = position; control.Size = size; }
    private static int Tenths(int ticks) => (int)Math.Ceiling(Math.Max(0, ticks) * FixedStepClock.SecondsPerTick * 10 - 1e-9);
    private static string Seconds(int ticks) => (Tenths(ticks) / 10d).ToString("0.0", CultureInfo.InvariantCulture) + "s";
    private readonly record struct RenderKey(string Status, string SuppressedMind, int SourceActor, bool CanBind, bool CanRelease,
        string EchoSkill, int Tenths, string HazardStage, int HazardTenths, string SourceName, string MindName, string EchoKey, bool Paused);
}
