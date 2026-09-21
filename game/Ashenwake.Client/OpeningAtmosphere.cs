using Godot;

namespace Ashenwake.Client;

/// <summary>Room-owned, cosmetic opening-region motion. Geometry and materials are created once;
/// the caller advances the clock and frees this node with its room. No autonomous shader time is used.</summary>
public partial class OpeningAtmosphere : Node3D
{
    private const int WispsPerChimney = 3;
    private const int LeafCount = 8;
    private const double ClockPeriod = 168; // Whole smoke, leaf, drift and tumble cycles.
    private MultiMeshInstance3D? _visual;
    private MultiMesh? _instances;
    private Mesh? _mesh;
    private StandardMaterial3D? _material;
    private ImageTexture? _smokeTexture;
    private Vector3[] _smokeAnchors = [];
    private float _halfWidth, _halfDepth;
    private double _time;
    private bool _resourcesReleased;

    public string Style { get; private set; } = "";
    public int Capacity => _instances?.InstanceCount ?? 0;
    public int ActiveCount => Visible ? Capacity : 0;
    public double MotionTime => _time;

    public static OpeningAtmosphere Create(string style, float halfWidth, float halfDepth)
    {
        if (!float.IsFinite(halfWidth) || halfWidth <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!float.IsFinite(halfDepth) || halfDepth <= 0) throw new ArgumentOutOfRangeException(nameof(halfDepth));
        var atmosphere = new OpeningAtmosphere
        {
            Name = "OpeningAtmosphere",
            Style = style,
            _halfWidth = halfWidth,
            _halfDepth = halfDepth
        };
        if (style == "greyhaven") atmosphere.BuildSmoke();
        else if (style is "road" or "monastery" or "sanctum") atmosphere.BuildLeaves();
        atmosphere.Visible = atmosphere._instances is not null;
        atmosphere.ApplyPose();
        return atmosphere;
    }

    public void Animate(double delta, bool paused, bool reducedEffects)
    {
        if (_resourcesReleased) return;
        // Preference changes take effect even while a menu holds the simulation paused.
        Visible = _instances is not null && !reducedEffects;
        if (!Visible || paused || !double.IsFinite(delta) || delta <= 0) return;
        _time = (_time + Math.Min(delta, .1)) % ClockPeriod;
        ApplyPose();
    }

    public override void _Notification(int what)
    {
        // Unlike ExitTree, Predelete also runs for detached diagnostic nodes and does not
        // release resources merely because a live room is temporarily reparented.
        if (what != NotificationPredelete || _resourcesReleased) return;
        _resourcesReleased = true;
        if (_visual is not null && GodotObject.IsInstanceValid(_visual))
        {
            _visual.MaterialOverride = null;
            _visual.Multimesh = null;
        }
        if (_material is not null && GodotObject.IsInstanceValid(_material)) _material.AlbedoTexture = null;

        // These resources are created exclusively by this node. Shared surface textures
        // and shaders are never acquired here and are deliberately outside this cleanup.
        ReleaseOwned(_smokeTexture); ReleaseOwned(_material);
        // GLES3 can still resolve the MultiMesh AABB during its release; keep the
        // mesh attached until that owner is gone, then release our mesh wrapper.
        ReleaseOwned(_instances); ReleaseOwned(_mesh);
        _smokeTexture = null; _material = null; _mesh = null; _instances = null;
        _visual = null; _smokeAnchors = [];
    }

    private static void ReleaseOwned(Resource? resource)
    {
        if (resource is not null && GodotObject.IsInstanceValid(resource)) resource.Dispose();
    }

    private void ApplyPose()
    {
        if (_instances is null) return;
        if (Style == "greyhaven")
        {
            for (int chimney = 0; chimney < _smokeAnchors.Length; chimney++)
                for (int wisp = 0; wisp < WispsPerChimney; wisp++)
                {
                    int index = chimney * WispsPerChimney + wisp;
                    float age = Cycle(_time / 8 + wisp / 3f + chimney * .17);
                    float curl = Mathf.Sin((float)(_time * Math.Tau / 12) + wisp * 1.4f + chimney);
                    var position = _smokeAnchors[chimney] + new Vector3(age * .58f + curl * age * .12f,
                        age * 1.8f, -age * .2f);
                    var scale = new Vector3(.36f + age * .48f, .82f + age * .55f, 1);
                    _instances.SetInstanceTransform(index, new Transform3D(Basis.Identity.Scaled(scale), position));
                    _instances.SetInstanceColor(index, new Color(1, 1, 1, Mathf.Sin(age * Mathf.Pi) * .8f));
                }
            return;
        }

        for (int i = 0; i < LeafCount; i++)
        {
            float age = Cycle(_time / 14 + i * .137);
            float drift = Mathf.Sin((float)(_time * Math.Tau / 12) + i * 1.7f);
            // Northern and western margins frame the scene without crossing a floor warning.
            var position = i < 4
                ? new Vector3(-_halfWidth + (i + .5f) * _halfWidth * .5f + age * .9f - .45f,
                    .35f + (1 - age) * 2.3f, -_halfDepth - 1.9f + drift * .23f)
                : new Vector3(-_halfWidth - 1.8f + drift * .2f,
                    .35f + (1 - age) * 2.3f, -_halfDepth * .7f + (i - 4) * _halfDepth * .4f + age * .8f - .4f);
            float tumble = (float)(_time * Math.Tau / 7);
            var rotation = new Vector3(tumble + i * .8f, -tumble + i * 1.7f, drift * .7f);
            float scale = Mathf.Sin(age * Mathf.Pi) * (1 + i % 3 * .18f);
            _instances.SetInstanceTransform(i, new Transform3D(Basis.FromEuler(rotation).Scaled(Vector3.One * scale), position));
        }
    }

    private void BuildSmoke()
    {
        _smokeAnchors = GreyhavenArt.SmokeAnchors(_halfWidth, _halfDepth);
        _smokeTexture = SmokeTexture();
        var material = new StandardMaterial3D
        {
            ResourceName = "OpeningChimneySmoke",
            AlbedoColor = new Color("929c95") { A = .19f },
            AlbedoTexture = _smokeTexture,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            DisableReceiveShadows = true,
            VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
            BillboardKeepScale = true,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            TextureRepeat = false
        };
        AddInstances("ChimneyWisps", RibbonMesh(), material, _smokeAnchors.Length * WispsPerChimney);
    }

    private void BuildLeaves()
    {
        var material = new StandardMaterial3D
        {
            ResourceName = "OpeningFuneralLeaves",
            AlbedoColor = new("888776"),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DisableReceiveShadows = true,
            VertexColorUseAsAlbedo = true,
            Roughness = 1
        };
        AddInstances("FuneralLeaves", LeafMesh(), material, LeafCount);
        for (int i = 0; i < LeafCount; i++)
        {
            float shade = .68f + i % 3 * .12f;
            _instances!.SetInstanceColor(i, new Color(shade, shade, shade));
        }
    }

    private void AddInstances(string name, Mesh mesh, StandardMaterial3D material, int count)
    {
        _mesh = mesh; _material = material;
        _instances = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = count
        };
        _visual = new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = _instances,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(_visual);
    }

    private static ArrayMesh RibbonMesh()
    {
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetNormal(Vector3.Back);
        surface.SetUV(new(0, 0)); surface.AddVertex(new(-.5f, 0, 0));
        surface.SetUV(new(1, 0)); surface.AddVertex(new(.5f, 0, 0));
        surface.SetUV(new(1, 1)); surface.AddVertex(new(.5f, 1, 0));
        surface.SetUV(new(0, 1)); surface.AddVertex(new(-.5f, 1, 0));
        foreach (int index in new[] { 0, 1, 2, 0, 2, 3 }) surface.AddIndex(index);
        return surface.Commit();
    }

    private static ArrayMesh LeafMesh()
    {
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] points = [new(-.055f, 0, 0), new(0, 0, -.13f), new(.05f, 0, .015f), new(0, -.01f, .11f)];
        for (int i = 0; i < points.Length; i++)
        {
            surface.AddVertex(new(0, .025f, 0));
            surface.AddVertex(points[i]);
            surface.AddVertex(points[(i + 1) % points.Length]);
        }
        surface.GenerateNormals(); surface.Index();
        return surface.Commit();
    }

    private static ImageTexture SmokeTexture()
    {
        const int width = 64, height = 128;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1), v = y / (float)(height - 1);
                float center = .5f + Mathf.Sin(v * Mathf.Tau * 1.3f) * .085f;
                float across = Math.Abs(u - center) / (.13f + v * .14f);
                float alpha = MathF.Exp(-across * across * 3) * Mathf.SmoothStep(0, .12f, v) *
                    (1 - Mathf.SmoothStep(.45f, 1, v));
                int offset = (y * width + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 255;
                pixels[offset + 3] = (byte)Math.Clamp((int)MathF.Round(alpha * 255), 0, 255);
            }
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
        image.GenerateMipmaps();
        var texture = ImageTexture.CreateFromImage(image);
        texture.ResourceName = "OpeningSmokeRibbon";
        return texture;
    }

    private static float Cycle(double value) => (float)(value - Math.Floor(value));
}
