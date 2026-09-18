using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private WorldEnvironment _worldEnvironment = null!;
    private DirectionalLight3D _sun = null!;
    private MultiMeshInstance3D? _ambientMotes;
    private string _environmentStyle = "";
    private double _atmosphereTime;

    public void SetEnvironmentStyle(string style)
    {
        if (_environmentStyle == style) return;
        _environmentStyle = style;
        bool hub = style == "greyhaven", sanctum = style == "sanctum";
        var environment = _worldEnvironment.Environment;
        environment.BackgroundColor = new Color(hub ? "182727" : sanctum ? "202831" : "18272d");
        environment.AmbientLightColor = new Color(hub ? "9dadac" : "96aeba");
        environment.AmbientLightEnergy = hub ? .36f : .38f;
        environment.FogMode = Godot.Environment.FogModeEnum.Depth;
        environment.FogLightColor = new Color(hub ? "425653" : "3b535f");
        environment.FogLightEnergy = .32f;
        environment.FogDensity = .25f;
        environment.FogDepthBegin = 35;
        environment.FogDepthEnd = 60;
        environment.FogEnabled = !_reduceEffects && style != "default";
        _sun.LightColor = new Color(hub ? "ffe0b7" : sanctum ? "dde3ee" : "dce5e2");
        _sun.LightEnergy = hub ? .85f : .78f;
        _sun.RotationDegrees = new(-58, -35, 0);
        if (_ambientMotes is null)
        {
            _ambientMotes = new MultiMeshInstance3D
            {
                Name = "AmbientMotes",
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = new SphereMesh { Radius = .028f, Height = .056f, RadialSegments = 6, Rings = 3 },
                    InstanceCount = 24
                },
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }
            };
            AddChild(_ambientMotes);
        }
        ((StandardMaterial3D)_ambientMotes.MaterialOverride).AlbedoColor = new Color(hub ? "ffbe76" : "8aafb5");
        UpdateEnvironmentAtmosphere(0);
    }

    private void UpdateEnvironmentAtmosphere(double delta)
    {
        if (_ambientMotes is null) return;
        _ambientMotes.Visible = !_reduceEffects && _environmentStyle != "default";
        _worldEnvironment.Environment.FogEnabled = _ambientMotes.Visible;
        if (!_ambientMotes.Visible || _clock.Paused) return;
        _atmosphereTime = (_atmosphereTime + delta) % 120;
        float x = _content.Room.HalfWidth * .001f, z = _content.Room.HalfDepth * .001f;
        for (int i = 0; i < 24; i++)
        {
            float cycle = (float)((_atmosphereTime * .12 + i * .6180339) % 1);
            // Only the distant perimeter receives ambient motes; the combat floor stays clear.
            var position = new Vector3(-x + i * (x * 2 / 23) + Mathf.Sin((float)_atmosphereTime + i) * .2f,
                .35f + cycle * 2.8f, -z - 1.1f - i % 3 * .55f);
            float scale = Mathf.Sin(cycle * Mathf.Pi);
            _ambientMotes.Multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), position));
        }
    }
}
