using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private WorldEnvironment _worldEnvironment = null!;
    private DirectionalLight3D _sun = null!;
    private MultiMeshInstance3D? _ambientMotes;
    private string _environmentStyle = "";
    private double _atmosphereTime;
    private AudioStreamPlayer? _verdantAmbience;
    private string _ambienceCue = "", _motePlacementStyle = "";
    private (float Width, float Depth) _motePlacementBounds;

    public string EnvironmentStyle => _environmentStyle;
    public string AmbienceCue => _ambienceCue;
    public bool AmbiencePlaying => _verdantAmbience?.Playing ?? false;
    public bool AmbiencePaused => _verdantAmbience?.StreamPaused ?? false;
    public int AmbientMoteCount => _ambientMotes?.Multimesh.InstanceCount ?? 0;

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
        bool verdant = VerdantAmbience.CueForStyle(style).Length != 0;
        if (verdant)
        {
            bool village = style == "verdant_village", heart = style == "verdant_heart";
            environment.BackgroundColor = new Color(village ? "1e2b24" : heart ? "15221e" : "152723");
            environment.AmbientLightColor = new Color(village ? "afb99b" : heart ? "92aa95" : "a5b79a");
            environment.AmbientLightEnergy = village ? .42f : heart ? .36f : .4f;
            environment.FogLightColor = new Color(village ? "68765a" : heart ? "496451" : "526d59");
            environment.FogLightEnergy = .22f;
            environment.FogDensity = heart ? .19f : .16f;
            // The haze starts behind nearby actors and hazard tells, preserving their contrast.
            environment.FogDepthBegin = 35;
            environment.FogDepthEnd = 65;
            _sun.LightColor = new Color(village ? "efdfae" : heart ? "bfd0a0" : "dde8be");
            _sun.LightEnergy = village ? .86f : heart ? .74f : .8f;
            _sun.RotationDegrees = new(-62, -25, 0);
        }
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
        ((StandardMaterial3D)_ambientMotes.MaterialOverride).AlbedoColor = new Color(verdant
            ? style == "verdant_heart" ? "90b68a" : "b9ca86"
            : hub ? "ffbe76" : "8aafb5");
        SetVerdantAmbience(style);
        UpdateEnvironmentAtmosphere(0);
    }

    private void UpdateEnvironmentAtmosphere(double delta)
    {
        if (_verdantAmbience is not null && _ambienceCue.Length != 0)
            _verdantAmbience.StreamPaused = _clock.Paused;
        if (_ambientMotes is null) return;
        _ambientMotes.Visible = !_reduceEffects && _environmentStyle != "default";
        _worldEnvironment.Environment.FogEnabled = _ambientMotes.Visible;
        if (!_ambientMotes.Visible) return;
        bool verdant = VerdantAmbience.CueForStyle(_environmentStyle).Length != 0;
        float x = _content.Room.HalfWidth * .001f, z = _content.Room.HalfDepth * .001f;
        if (verdant && _authoredBounds.Width > 0 && _authoredBounds.Depth > 0)
        { x = _authoredBounds.Width * .001f; z = _authoredBounds.Depth * .001f; }
        // A newly shown room still receives its initial layout when entered through a paused menu.
        bool layoutChanged = _motePlacementStyle != _environmentStyle || _motePlacementBounds != (x, z);
        if (_clock.Paused && !layoutChanged) return;
        if (!_clock.Paused) _atmosphereTime = (_atmosphereTime + delta) % 120;
        _motePlacementStyle = _environmentStyle;
        _motePlacementBounds = (x, z);
        for (int i = 0; i < 24; i++)
        {
            float cycle = (float)((_atmosphereTime * (verdant ? .1 : .12) + i * .6180339) % 1);
            // Only the distant perimeter receives ambient motes; the combat floor stays clear.
            var position = new Vector3(-x + i * (x * 2 / 23) + Mathf.Sin((float)_atmosphereTime + i) * .2f,
                .35f + cycle * 2.8f, -z - 1.1f - i % 3 * .55f);
            if (verdant)
            {
                float drift = Mathf.Sin((float)(_atmosphereTime * Math.Tau / 10) + i) * .18f;
                float spread = (i % 8 + .5f) / 8;
                position = (i / 8) switch
                {
                    0 => new(-x + spread * x * 2 + drift, .5f + cycle * 3, -z - 1.3f),
                    1 => new(-x - 1.3f, .5f + cycle * 3, -z + spread * z * 2 + drift),
                    _ => new(x + 1.3f, .5f + cycle * 3, -z + spread * z * 2 + drift)
                };
            }
            float scale = Mathf.Sin(cycle * Mathf.Pi);
            _ambientMotes.Multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), position));
        }
    }

    private void SetVerdantAmbience(string style)
    {
        string cue = VerdantAmbience.CueForStyle(style);
        if (cue == _ambienceCue) return;
        _ambienceCue = cue;
        if (_verdantAmbience is not null)
        { _verdantAmbience.Stop(); _verdantAmbience.StreamPaused = false; }
        if (cue.Length == 0)
        {
            if (_verdantAmbience is not null) _verdantAmbience.Stream = null;
            return;
        }
        if (_verdantAmbience is null)
        {
            // Master bus volume/muting remains authoritative for both combat and ambient audio.
            _verdantAmbience = new AudioStreamPlayer { Name = "VerdantAmbience", VolumeDb = -25, Bus = "Master" };
            AddChild(_verdantAmbience);
        }
        _verdantAmbience.Stream = VerdantAmbience.GetStream(cue);
        _verdantAmbience.Play();
        _verdantAmbience.StreamPaused = _clock.Paused;
    }
}
