using Godot;

namespace Ashenwake.Client;

/// <summary>An isolated companion inspection viewport, isolated from the combat world's cameras, lights and input.</summary>
public partial class PetPreview : VBoxContainer
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
    private PetVisual? _model;
    private string _petId = "", _appearanceId = "", _modelKey = "";
    private float _height = 3, _radius = 1;
    private MeshInstance3D _floor = null!;
    private Sandbox? _sandbox;
    private Label _caption = null!;
    private bool _dragging, _reducedEffects;
    private float _yaw;
    private string _captionText = "Drag to rotate · preview only";
    public string VisualId => _model is null ? "" : _petId;
    public float RotationAngle => _yaw;
    public bool Rendering => _viewport is not null && _viewport.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled;

    public override void _Ready()
    {
        Name = "PetPreview";
        CustomMinimumSize = new(270, 0);
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 8);
        var heading = new Label { Text = "YOUR TRAVEL COMPANION", HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 15); AddChild(heading);
        _surface = new TextureRect
        {
            Name = "PetPreviewSurface",
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
            Name = "PetViewport",
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
        _floor = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1.13f, BottomRadius = 1.2f, Height = .08f, RadialSegments = 48 },
            Position = new(0, -.08f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new("243440"), Roughness = .9f }
        };
        _viewport.AddChild(_floor);
        _turntable = new Node3D { Name = "PetTurntable" }; _viewport.AddChild(_turntable);
        _caption = new Label { Text = _captionText, AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
        _caption.AddThemeFontSizeOverride("font_size", 12); AddChild(_caption);
        var controls = new HBoxContainer(); AddChild(controls);
        AddRotationButton(controls, "PetRotateLeft", "↶ Left", () => Rotate(-Mathf.Pi / 6));
        AddRotationButton(controls, "PetResetRotation", "Front", () => { _yaw = 0; _turntable.Rotation = Vector3.Zero; });
        AddRotationButton(controls, "PetRotateRight", "Right ↷", () => Rotate(Mathf.Pi / 6));
        _surface.GuiInput += SurfaceInput;
        VisibilityChanged += UpdateActivity;
        for (Node? ancestor = GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
            if (ancestor is Sandbox sandbox) { _sandbox = sandbox; break; }
        UpdateActivity();
    }

    public void SetPet(string petId, string appearanceId)
    {
        if (_petId == petId && _appearanceId == appearanceId) return;
        _petId = petId; _appearanceId = appearanceId; _yaw = 0;
        if (_turntable is not null) _turntable.Rotation = Vector3.Zero;
        if (IsNodeReady())
        {
            if (_model is not null) { _turntable!.RemoveChild(_model); _model.QueueFree(); _model = null; }
            _modelKey = "";
            if (IsVisibleInTree()) RefreshModel();
        }
    }

    public void SetCaption(string caption)
    { _captionText = caption; if (_caption is not null) _caption.Text = caption; }

    public void SetReducedEffects(bool reducedEffects)
    { _reducedEffects = reducedEffects; }

    public void SetCompact(bool compact, float availableHeight = 420)
    {
        // Reserve the title, two-line caption, buttons and container gaps before assigning the render surface.
        if (_surface is not null) _surface.CustomMinimumSize = new(270, Math.Clamp(availableHeight - 110, 64, compact ? 190 : 300));
        FitCamera();
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
        if (_viewport.Size != target) { _viewport.Size = target; FitCamera(); }
        float renderScale = quality == "High" ? _sandbox?.RenderScale ?? 1.25f : 1f;
        if (_appliedGraphics != quality || _appliedReducedEffects != _reducedEffects || !Mathf.IsEqualApprox(_appliedRenderScale, renderScale))
        {
            GraphicsProfile.Apply(_viewport, _environment, _keyLight, quality, _reducedEffects, renderScale);
            _appliedGraphics = quality; _appliedReducedEffects = _reducedEffects; _appliedRenderScale = renderScale;
        }
        _model?.Animate(delta, false, _reducedEffects);
    }

    private void RefreshModel()
    {
        string key = _petId + "/" + _appearanceId;
        if (_petId.Length == 0 || _modelKey == key) return;
        if (_model is not null) { _turntable!.RemoveChild(_model); _model.QueueFree(); }
        _model = PetVisual.Create(_petId, _appearanceId);
        _model.Name = "PreviewPet"; _turntable.AddChild(_model);
        for (int i = 0; i < 8; i++) _model.Animate(.1, false, _reducedEffects);
        var points = new List<Vector3>(); Bounds(_model, Transform3D.Identity, points);
        if (points.Count > 0)
        {
            float low = Math.Min(0, points.Min(p => p.Y)), high = points.Max(p => p.Y);
            float x = (points.Min(p => p.X) + points.Max(p => p.X)) * .5f;
            float z = (points.Min(p => p.Z) + points.Max(p => p.Z)) * .5f;
            _model.Position = new(-x, -low, -z);
            _height = Math.Max(1, high - low);
            _radius = Math.Max(.6f, points.Max(p => new Vector2(p.X - x, p.Z - z).Length()));
            _floor.Scale = new(_radius, 1, _radius);
        }
        _modelKey = key; FitCamera();
    }

    private static void Bounds(Node3D node, Transform3D parent, List<Vector3> points)
    {
        var transform = parent * node.Transform;
        if (node is MeshInstance3D { Mesh: not null } mesh && mesh.Visible)
            for (int i = 0; i < 8; i++) points.Add(transform * mesh.Mesh.GetAabb().GetEndpoint(i));
        foreach (var child in node.GetChildren().OfType<Node3D>()) Bounds(child, transform, points);
    }

    private void FitCamera()
    {
        if (_camera is null || _viewport is null) return;
        float aspect = (float)_viewport.Size.X / Math.Max(1, _viewport.Size.Y);
        // Fit the entire turning radius, including the fox tail and open moth wings.
        _camera.Size = Math.Max(_height * 1.3f + _radius * .3f, _radius * 2.35f) / Math.Min(1, aspect);
        Vector3 center = new(0, _height * .5f, 0);
        _camera.Position = center + new Vector3(0, Math.Max(1, _height * .22f), Math.Max(8, _radius * 4));
        _camera.LookAt(center);
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
