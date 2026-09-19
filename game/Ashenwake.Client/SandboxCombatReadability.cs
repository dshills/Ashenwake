using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private const int CombatBarWidth = 96;
    private ImageTexture? _combatBarTexture;
    private PanelContainer? _targetDetail;
    private Label _targetName = null!, _targetHealth = null!, _targetConditions = null!;
    private int _hoveredCombatActor, _targetDetailActor;

    /// <summary>Focuses presentation only; mouse selection and commands remain owned by the input adapter.</summary>
    public void SetHoveredActor(int id)
    {
        _hoveredCombatActor = !IsPaused && ReadableCombatActor(id) is not null ? id : 0;
    }

    private ActorPresentation? ReadableCombatActor(int id)
        => _actors.TryGetValue(id, out var actor) && actor.Enemy && actor.AuthoredVisible && actor.Health > 0 ? actor : null;

    private (Node3D Root, Sprite3D Track, Sprite3D Fill) CreateCombatHealthBar(Node3D parent, Color color)
    {
        if (_combatBarTexture is null)
        {
            using var image = Image.CreateEmpty(CombatBarWidth, 8, false, Image.Format.Rgba8);
            image.Fill(Colors.White);
            _combatBarTexture = ImageTexture.CreateFromImage(image);
        }
        var root = new Node3D { Name = "ActorHealthBar" }; parent.AddChild(root);
        var track = CombatBarSprite("HealthTrack", new Color("131923"), 1);
        var fill = CombatBarSprite("HealthFill", color, 2);
        root.AddChild(track); root.AddChild(fill);
        return (root, track, fill);
    }

    private Sprite3D CombatBarSprite(string name, Color color, int priority) => new()
    {
        Name = name,
        Texture = _combatBarTexture,
        Modulate = color,
        PixelSize = .0125f,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        Shaded = false,
        RenderPriority = priority,
        RegionEnabled = true,
        RegionRect = new Rect2(0, 0, CombatBarWidth, 8)
    };

    private void SynchronizeCombatReadability(ActorPresentation actor, int id, string name, int health,
        int maxHealth, string status, bool telegraph, bool allied, string? mechanic)
    {
        actor.Enemy = !allied && id != 1;
        actor.Name = name.ToUpperInvariant();
        actor.HealthDetail = $"HEALTH  {health} / {maxHealth}";
        bool incoming = telegraph || status.Contains("ATTACK INCOMING", StringComparison.Ordinal);
        // Exact health and transient conditions live in one focus card. Required mechanics
        // remain attached to every anchor, channel and boss even when it is not selected.
        actor.Label.Text = actor.Name + (mechanic is null ? "" : "\n" + mechanic) + (incoming ? "\nATTACK INCOMING" : "");
        var conditions = status.Split(" / ", StringSplitOptions.RemoveEmptyEntries)
            .Where(value => value != mechanic && value != "ATTACK INCOMING")
            .Select(value => Readable(value).ToUpperInvariant()).Distinct().ToArray();
        string detail = string.Join(" · ", conditions);
        actor.ConditionDetail = string.Join("\n", new[] { incoming ? "ATTACK INCOMING" : mechanic ?? "", detail }.Where(value => value.Length > 0));
        actor.HealthBar.Position = actor.Label.Position - Vector3.Up * .14f;
        float ratio = maxHealth > 0 ? Math.Clamp((float)health / maxHealth, 0, 1) : 0;
        float width = CombatBarWidth * ratio;
        actor.HealthFill.RegionRect = new Rect2(0, 0, width, 4);
        // Sprite offsets are billboard-space pixels, so depletion stays left-aligned
        // regardless of camera rotation. The shared white texture never changes.
        actor.HealthFill.Offset = new Vector2((width - CombatBarWidth) * .5f, 0);
        actor.HealthBar.Visible = actor.Enemy && health > 0;
        actor.Label.Visible = health > 0 && actor.Enemy && (id == _target || id == _hoveredCombatActor || _mechanicLabels.Contains(id));
    }

    private void UpdateCombatReadability(int selected)
    {
        if (IsPaused || ReadableCombatActor(_hoveredCombatActor) is null) _hoveredCombatActor = 0;
        foreach (var pair in _actors)
        {
            var actor = pair.Value;
            bool alive = actor.Enemy && actor.AuthoredVisible && actor.Health > 0;
            bool focused = pair.Key == selected || pair.Key == _hoveredCombatActor;
            actor.HealthBar.Visible = alive;
            actor.Label.Visible = alive && (focused || _mechanicLabels.Contains(pair.Key));
            float pixelSize = focused ? .016f : .0125f;
            if (actor.HealthFill.PixelSize != pixelSize)
            { actor.HealthFill.PixelSize = pixelSize; actor.HealthTrack.PixelSize = pixelSize; }
            int fontSize = focused ? 44 : 40;
            if (actor.Label.FontSize != fontSize) actor.Label.FontSize = fontSize;
        }
        int focus = _hoveredCombatActor != 0 ? _hoveredCombatActor : selected;
        var target = ReadableCombatActor(focus);
        if (target is null || IsPaused)
        {
            if (_targetDetail is not null) _targetDetail.Visible = false;
            _targetDetailActor = 0;
            return;
        }
        EnsureCombatTargetDetail();
        _targetDetail!.Visible = true;
        if (_targetDetailActor != focus || _targetName.Text != target.Name) _targetName.Text = target.Name;
        if (_targetHealth.Text != target.HealthDetail) _targetHealth.Text = target.HealthDetail;
        if (_targetConditions.Text != target.ConditionDetail) _targetConditions.Text = target.ConditionDetail;
        _targetDetailActor = focus;
    }

    private void EnsureCombatTargetDetail()
    {
        if (_targetDetail is not null) return;
        _targetDetail = new PanelContainer
        {
            Name = "CombatTargetDetail",
            Position = new(615, 88),
            Size = new(286, 104),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color("15212a"),
            BorderColor = new Color("54636a"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 9,
            ContentMarginBottom = 9
        };
        _targetDetail.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 3); _targetDetail.AddChild(column);
        _targetName = TargetDetailLabel("TargetName", 14, new Color("f0dfc4"));
        _targetHealth = TargetDetailLabel("TargetHealth", 12, new Color("e9b990"));
        _targetConditions = TargetDetailLabel("TargetConditions", 11, new Color("c3d5d8"));
        _targetConditions.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _targetConditions.MaxLinesVisible = 2;
        // Clipped wrapping labels can report a one-pixel minimum height. Reserve
        // two readable lines even when the card was first created without conditions.
        _targetConditions.CustomMinimumSize = new(0, 32);
        column.AddChild(_targetName); column.AddChild(_targetHealth); column.AddChild(_targetConditions);
        _hud.AddChild(_targetDetail);
    }

    private static Label TargetDetailLabel(string name, int size, Color color)
    {
        var label = new Label { Name = name, ClipText = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private void ClearCombatReadability()
    {
        _hoveredCombatActor = 0; _targetDetailActor = 0;
        if (_targetDetail is not null) _targetDetail.Visible = false;
    }
}
