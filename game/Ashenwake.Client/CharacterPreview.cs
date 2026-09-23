using Godot;

namespace Ashenwake.Client;

/// <summary>A cosmetic wardrobe viewport, isolated from the combat world's cameras, lights and input.</summary>
public partial class CharacterPreview : VBoxContainer
{
    private SubViewport _viewport = null!;
    private TextureRect _surface = null!;
    private Camera3D _camera = null!;
    private Godot.Environment _environment = null!;
    private DirectionalLight3D _keyLight = null!;
    private string _appliedGraphics = "";
    private bool _appliedReducedEffects;
    private float _appliedRenderScale;
    private Node3D _turntable = null!;
    private CharacterVisual? _model;
    private CharacterAppearance? _appearance;
    private Sandbox? _sandbox;
    private Label _caption = null!;
    private bool _dragging, _reducedEffects;
    private float _yaw;
    private string _captionText = "Equipped appearance";
    public string AppearanceKey => _model?.AppearanceKey ?? "";
    public float RotationAngle => _yaw;
    public bool Rendering => _viewport is not null && _viewport.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled;

    public override void _Ready()
    {
        Name = "CharacterPreview";
        CustomMinimumSize = new(270, 0);
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 8);
        var heading = new Label { Text = "YOUR WANDERER", HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 15); AddChild(heading);
        _surface = new TextureRect
        {
            Name = "PreviewSurface",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            CustomMinimumSize = new(270, 300),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.Drag,
            TooltipText = "Drag to rotate. This changes only the preview."
        };
        AddChild(_surface);
        _viewport = new SubViewport
        {
            Name = "WardrobeViewport",
            Size = new(270, 300),
            OwnWorld3D = true,
            GuiDisableInput = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        _surface.AddChild(_viewport);
        _surface.Texture = _viewport.GetTexture();
        var lighting = new WorldEnvironment
        {
            Environment = _environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new("101b24"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new("a5b8c5"),
                AmbientLightEnergy = .4f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        };
        GraphicsProfile.TrackEnvironment(lighting);
        _viewport.AddChild(lighting);
        _camera = new Camera3D { Name = "PreviewCamera", Projection = Camera3D.ProjectionType.Orthogonal, Size = 3.65f, Position = new(0, 2.05f, 5.5f), Current = true };
        _viewport.AddChild(_camera); _camera.LookAt(new(0, 1.32f, 0));
        _keyLight = new DirectionalLight3D { RotationDegrees = new(-34, -32, 0), LightColor = new("ffe2b6"), LightEnergy = 1.1f, ShadowEnabled = true };
        _viewport.AddChild(_keyLight);
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new(-18, 135, 0), LightColor = new("89c7dd"), LightEnergy = .35f });
        var floor = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1.13f, BottomRadius = 1.2f, Height = .08f, RadialSegments = 48 },
            Position = new(0, -.08f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new("243440"), Roughness = .9f }
        };
        _viewport.AddChild(floor);
        _turntable = new Node3D { Name = "PreviewTurntable" }; _viewport.AddChild(_turntable);
        _caption = new Label { Text = _captionText, AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
        _caption.AddThemeFontSizeOverride("font_size", 12); AddChild(_caption);
        var controls = new HBoxContainer(); AddChild(controls);
        AddRotationButton(controls, "RotateLeft", "↶ Left", () => Rotate(-Mathf.Pi / 6));
        AddRotationButton(controls, "ResetRotation", "Front", () => { _yaw = 0; _turntable.Rotation = Vector3.Zero; });
        AddRotationButton(controls, "RotateRight", "Right ↷", () => Rotate(Mathf.Pi / 6));
        _surface.GuiInput += SurfaceInput;
        VisibilityChanged += UpdateActivity;
        for (Node? ancestor = GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
            if (ancestor is Sandbox sandbox) { _sandbox = sandbox; break; }
        UpdateActivity();
    }

    public void SetAppearance(CharacterAppearance appearance)
    {
        _appearance = appearance;
        if (IsNodeReady() && IsVisibleInTree()) RefreshModel();
    }

    public void SetCaption(string caption)
    { _captionText = caption; if (_caption is not null) _caption.Text = caption; }

    public void SetReducedEffects(bool reducedEffects)
    { _reducedEffects = reducedEffects; _model?.SetReducedEffects(reducedEffects); }

    public void SetCompact(bool compact, float availableHeight = 420)
    {
        // Reserve the title, two-line caption, buttons and container gaps before assigning the render surface.
        if (_surface is not null) _surface.CustomMinimumSize = new(270, Math.Clamp(availableHeight - 110, 64, compact ? 190 : 300));
        if (_camera is not null) _camera.Size = compact ? 4.1f : 3.65f;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) { UpdateActivity(); return; }
        if (_sandbox is not null) SetReducedEffects(_sandbox.ReducedEffects);
        string quality = _sandbox?.GraphicsQuality ?? "High";
        // Render into actual screen pixels before the UI composites this texture.
        // Supersampling a logical-size texture alone would still enlarge a small image.
        Vector2 screenScale = _surface.GetScreenTransform().Scale.Abs();
        Vector2 pixels = _surface.Size * screenScale;
        pixels *= Math.Min(1f, 2048f / Math.Max(1f, Math.Max(pixels.X, pixels.Y)));
        Vector2I target = new(Math.Max(2, (int)Math.Ceiling(pixels.X)), Math.Max(2, (int)Math.Ceiling(pixels.Y)));
        if (_viewport.Size != target) _viewport.Size = target;
        float renderScale = quality == "High" ? _sandbox?.RenderScale ?? 1.25f : 1f;
        if (_appliedGraphics != quality || _appliedReducedEffects != _reducedEffects || !Mathf.IsEqualApprox(_appliedRenderScale, renderScale))
        {
            GraphicsProfile.Apply(_viewport, _environment, _keyLight, quality, _reducedEffects, renderScale);
            _appliedGraphics = quality; _appliedReducedEffects = _reducedEffects; _appliedRenderScale = renderScale;
        }
        _model?.Animate(delta, Vector3.Zero, paused: _sandbox?.IsPaused ?? false, facing: Vector3.Back);
    }

    private void RefreshModel()
    {
        if (_appearance is null || _model?.AppearanceKey == _appearance.Key) return;
        if (_model is not null) { _turntable.RemoveChild(_model); _model.QueueFree(); }
        _model = CharacterVisual.Create("player", "Player", _appearance.Discipline, appearance: _appearance);
        _model.Name = "PreviewCharacter"; _model.SetReducedEffects(_reducedEffects); _turntable.AddChild(_model);
        // Settle the cosmetic facing before first display, including when the menu opens while paused.
        // The preview camera looks along -Z; the character's front should point toward +Z.
        for (int i = 0; i < 8; i++) _model.Animate(.1, Vector3.Zero, facing: Vector3.Back);
    }

    private void UpdateActivity()
    {
        if (_viewport is null) return;
        bool active = IsVisibleInTree();
        _viewport.RenderTargetUpdateMode = active ? SubViewport.UpdateMode.WhenVisible : SubViewport.UpdateMode.Disabled;
        SetProcess(active);
        if (active) RefreshModel(); else _dragging = false;
    }

    private void SurfaceInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) _dragging = button.Pressed;
        if (input is InputEventMouseMotion motion && _dragging)
        {
            if ((motion.ButtonMask & MouseButtonMask.Left) == 0) _dragging = false;
            else Rotate(motion.Relative.X * .012f);
        }
        _surface.AcceptEvent();
    }

    private void Rotate(float amount)
    { _yaw = Mathf.Wrap(_yaw + amount, -Mathf.Pi, Mathf.Pi); _turntable.Rotation = new(0, _yaw, 0); }

    private static void AddRotationButton(HBoxContainer controls, string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += action; controls.AddChild(button);
    }
}
