using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private WorldEnvironment _worldEnvironment = null!;
    private DirectionalLight3D _sun = null!;
    private MultiMeshInstance3D? _ambientMotes;
    private OpeningAtmosphere? _openingAtmosphere;
    private OpeningLighting? _openingLighting;
    private (float Width, float Depth) _openingLightingBounds;
    private (float Width, float Depth) _openingAtmosphereBounds;
    private VerdantAtmosphere? _verdantAtmosphere;
    private (float Width, float Depth) _verdantAtmosphereBounds;
    private string _environmentStyle = "";
    private double _atmosphereTime;
    private AudioStreamPlayer? _regionalAmbience;
    private string _ambienceCue = "", _motePlacementStyle = "";
    private (float Width, float Depth) _motePlacementBounds;

    public string EnvironmentStyle => _environmentStyle;
    public string AmbienceCue => _ambienceCue;
    public bool AmbiencePlaying => _regionalAmbience?.Playing ?? false;
    public bool AmbiencePaused => _regionalAmbience?.StreamPaused ?? false;
    public int AmbientMoteCount => _ambientMotes?.Multimesh.InstanceCount ?? 0;
    public OpeningAtmosphere? OpeningMotion => _openingAtmosphere;
    public OpeningLighting? OpeningLights => _openingLighting;
    public VerdantAtmosphere? VerdantMotion => _verdantAtmosphere;
    private CinderAtmosphere? _cinderAtmosphere;
    private (float Width, float Depth) _cinderAtmosphereBounds;
    public CinderAtmosphere? CinderMotion => _cinderAtmosphere;
    private SpineAtmosphere? _spineAtmosphere;
    private (float Width, float Depth) _spineAtmosphereBounds;
    public SpineAtmosphere? SpineMotion => _spineAtmosphere;
    private HollowAtmosphere? _hollowAtmosphere;
    private (float Width, float Depth) _hollowAtmosphereBounds;
    public HollowAtmosphere? HollowMotion => _hollowAtmosphere;

    public void SetEnvironmentStyle(string style)
    {
        if (_environmentStyle == style) return;
        _environmentStyle = style;
        _openingAudio.SetStyle(style);
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
        if (style == "crypt")
        {
            environment.BackgroundColor = new Color("171c28");
            environment.AmbientLightColor = new Color("a7b2ca");
            environment.AmbientLightEnergy = .4f;
            environment.FogLightColor = new Color("444d66");
            environment.FogLightEnergy = .2f;
            environment.FogDensity = .18f;
            environment.FogDepthBegin = 35;
            environment.FogDepthEnd = 64;
            _sun.LightColor = new Color("c4cfe6");
            _sun.LightEnergy = .68f;
            _sun.RotationDegrees = new(-62, -25, 0);
        }
        bool verdant = VerdantAmbience.CueForStyle(style).Length != 0;
        bool cinder = CinderAmbience.CueForStyle(style).Length != 0;
        bool spine = SpineAmbience.CueForStyle(style).Length != 0;
        bool hollow = HollowAmbience.CueForStyle(style).Length != 0;
        if (verdant)
        {
            bool village = style == "verdant_village", heart = style == "verdant_heart";
            environment.BackgroundColor = new Color(village ? "1e2b24" : heart ? "15221e" : "152723");
            environment.AmbientLightColor = new Color(village ? "b8b6a0" : heart ? "9cacb1" : "b0b9a8");
            environment.AmbientLightEnergy = village ? .40f : heart ? .35f : .37f;
            environment.FogLightColor = new Color(village ? "68765a" : heart ? "496451" : "526d59");
            environment.FogLightEnergy = .22f;
            environment.FogDensity = heart ? .19f : .16f;
            // The haze starts behind nearby actors and hazard tells, preserving their contrast.
            environment.FogDepthBegin = 35;
            environment.FogDepthEnd = 65;
            _sun.LightColor = new Color(village ? "ffe0ad" : heart ? "c6d7ca" : "fff0c9");
            _sun.LightEnergy = village ? .84f : heart ? .74f : .83f;
            _sun.RotationDegrees = new(-62, -25, 0);
        }
        if (cinder)
        {
            bool storm = style == "cinder_storm", furnace = style == "cinder_furnace";
            environment.BackgroundColor = new Color(storm ? "25272e" : "2b2524");
            environment.AmbientLightColor = new Color(storm ? "a6b8c9" : "aeb8c5");
            environment.AmbientLightEnergy = storm ? .42f : .38f;
            environment.FogLightColor = new Color(storm ? "646675" : "70594c");
            environment.FogLightEnergy = .2f;
            environment.FogDensity = storm ? .20f : .14f;
            environment.FogDepthBegin = 35;
            environment.FogDepthEnd = 65;
            _sun.LightColor = new Color(storm ? "dfdef0" : furnace ? "ffdfb9" : "ffdeba");
            _sun.LightEnergy = .78f;
            _sun.RotationDegrees = new(-58, -30, 0);
        }
        if (spine)
        {
            bool memory = style == "spine_memory", hall = style is "spine_hall" or "spine_archive";
            environment.BackgroundColor = new Color(memory ? "30312c" : "202833");
            environment.AmbientLightColor = new Color(memory ? "c6bda5" : "a9b8cb");
            environment.AmbientLightEnergy = memory ? .38f : .42f;
            environment.FogLightColor = new Color(memory ? "80745c" : "5b687b");
            environment.FogLightEnergy = .19f;
            environment.FogDensity = hall ? .13f : .17f;
            environment.FogDepthBegin = 35;
            environment.FogDepthEnd = 68;
            _sun.LightColor = new Color(memory ? "ffe2af" : "dae3f1");
            _sun.LightEnergy = memory ? .76f : .8f;
            _sun.RotationDegrees = new(-54, -32, 0);
        }
        if (hollow)
        {
            bool memory = style is "hollow_memory" or "hollow_vault", breach = style == "hollow_breach";
            environment.BackgroundColor = new Color(memory ? "242333" : "181e2c");
            environment.AmbientLightColor = new Color(memory ? "b6acc4" : breach ? "a7b6c9" : "a8afc3");
            environment.AmbientLightEnergy = .38f;
            environment.FogLightColor = new Color(memory ? "61576f" : "475568");
            environment.FogLightEnergy = .16f;
            environment.FogDensity = breach ? .17f : .13f;
            environment.FogDepthBegin = 35;
            environment.FogDepthEnd = 68;
            _sun.LightColor = new Color(memory ? "e0d7e9" : "ceddec");
            _sun.LightEnergy = .7f;
            _sun.RotationDegrees = new(-58, -32, 0);
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
            : cinder ? style == "cinder_storm" ? "aeb3c2" : "bc9172"
            : spine ? style == "spine_memory" ? "d0c6a4" : "a6b1c4"
            : hollow ? style is "hollow_memory" or "hollow_vault" ? "b6a8c5" : "9fbbcb" : hub ? "ffbe76" : "8aafb5");
        SetRegionalAmbience(style);
        ApplyGraphicsQuality();
        UpdateEnvironmentAtmosphere(0);
    }

    private void UpdateEnvironmentAtmosphere(double delta)
    {
        if (_regionalAmbience is not null && _ambienceCue.Length != 0)
        {
            _regionalAmbience.StreamPaused = _clock.Paused;
            // Scored regional beds sit behind the music and follow its warning ducking.
            // Per-player gain leaves the user's Music & ambience preference untouched.
            _regionalAmbience.VolumeDb = VerdantAmbience.CueForStyle(_environmentStyle).Length != 0 || CinderAmbience.CueForStyle(_environmentStyle).Length != 0 || SpineAmbience.CueForStyle(_environmentStyle).Length != 0
                ? -29 + Mathf.LinearToDb(Math.Max(.0001f, _openingAudio.DuckGain)) : -25;
        }
        float x = (_authoredBounds.Width > 0 ? _authoredBounds.Width : _content.Room.HalfWidth) * .001f;
        float z = (_authoredBounds.Depth > 0 ? _authoredBounds.Depth : _content.Room.HalfDepth) * .001f;
        bool hollowRoom = HollowAtmosphere.Supports(_environmentStyle);
        if (_hollowAtmosphere is not null && (!hollowRoom || _hollowAtmosphere.Style != _environmentStyle || _hollowAtmosphereBounds != (x, z)))
        {
            RemoveChild(_hollowAtmosphere); _hollowAtmosphere.QueueFree(); _hollowAtmosphere = null;
        }
        if (hollowRoom && _hollowAtmosphere is null)
        {
            _hollowAtmosphere = HollowAtmosphere.Create(_environmentStyle, x, z);
            _hollowAtmosphereBounds = (x, z); AddChild(_hollowAtmosphere);
        }
        _hollowAtmosphere?.Animate(delta, _clock.Paused, _reduceEffects, _graphicsQuality);
        bool spineRoom = SpineAtmosphere.Supports(_environmentStyle);
        if (_spineAtmosphere is not null && (!spineRoom || _spineAtmosphere.Style != _environmentStyle || _spineAtmosphereBounds != (x, z)))
        {
            RemoveChild(_spineAtmosphere); _spineAtmosphere.QueueFree(); _spineAtmosphere = null;
        }
        if (spineRoom && _spineAtmosphere is null)
        {
            _spineAtmosphere = SpineAtmosphere.Create(_environmentStyle, x, z);
            _spineAtmosphereBounds = (x, z); AddChild(_spineAtmosphere);
        }
        _spineAtmosphere?.Animate(delta, _clock.Paused, _reduceEffects, _graphicsQuality);
        bool cinderRoom = CinderAtmosphere.Supports(_environmentStyle);
        if (_cinderAtmosphere is not null && (!cinderRoom || _cinderAtmosphere.Style != _environmentStyle || _cinderAtmosphereBounds != (x, z)))
        {
            RemoveChild(_cinderAtmosphere); _cinderAtmosphere.QueueFree(); _cinderAtmosphere = null;
        }
        if (cinderRoom && _cinderAtmosphere is null)
        {
            _cinderAtmosphere = CinderAtmosphere.Create(_environmentStyle, x, z);
            _cinderAtmosphereBounds = (x, z); AddChild(_cinderAtmosphere);
        }
        _cinderAtmosphere?.Animate(delta, _clock.Paused, _reduceEffects, _graphicsQuality);
        bool verdantRoom = VerdantAtmosphere.Supports(_environmentStyle);
        if (_verdantAtmosphere is not null && (!verdantRoom || _verdantAtmosphere.Style != _environmentStyle || _verdantAtmosphereBounds != (x, z)))
        {
            RemoveChild(_verdantAtmosphere); _verdantAtmosphere.QueueFree(); _verdantAtmosphere = null;
        }
        if (verdantRoom && _verdantAtmosphere is null)
        {
            _verdantAtmosphere = VerdantAtmosphere.Create(_environmentStyle, x, z);
            _verdantAtmosphereBounds = (x, z); AddChild(_verdantAtmosphere);
        }
        _verdantAtmosphere?.Animate(delta, _clock.Paused, _reduceEffects, _graphicsQuality);
        bool litOpening = OpeningLighting.Supports(_environmentStyle);
        if (_openingLighting is not null && (!litOpening || _openingLighting.Style != _environmentStyle || _openingLightingBounds != (x, z)))
        {
            RemoveChild(_openingLighting);
            _openingLighting.QueueFree();
            _openingLighting = null;
        }
        if (litOpening && _openingLighting is null)
        {
            _openingLighting = OpeningLighting.Create(_environmentStyle, x, z);
            _openingLightingBounds = (x, z);
            AddChild(_openingLighting);
        }
        _openingLighting?.Animate(delta, _clock.Paused, _reduceEffects, _graphicsQuality);
        bool opening = _environmentStyle is "greyhaven" or "road" or "monastery" or "sanctum";
        if (_openingAtmosphere is not null && (!opening || _openingAtmosphere.Style != _environmentStyle || _openingAtmosphereBounds != (x, z)))
        {
            RemoveChild(_openingAtmosphere);
            _openingAtmosphere.QueueFree();
            _openingAtmosphere = null;
        }
        if (opening && _openingAtmosphere is null)
        {
            _openingAtmosphere = OpeningAtmosphere.Create(_environmentStyle, x, z);
            _openingAtmosphereBounds = (x, z);
            AddChild(_openingAtmosphere);
        }
        _openingAtmosphere?.Animate(delta, _clock.Paused, _reduceEffects);
        if (_ambientMotes is null) return;
        _ambientMotes.Visible = !_reduceEffects && _environmentStyle != "default";
        _worldEnvironment.Environment.FogEnabled = _ambientMotes.Visible;
        if (!_ambientMotes.Visible) return;
        bool verdant = VerdantAmbience.CueForStyle(_environmentStyle).Length != 0;
        bool cinder = CinderAmbience.CueForStyle(_environmentStyle).Length != 0, storm = _environmentStyle == "cinder_storm";
        bool spine = SpineAmbience.CueForStyle(_environmentStyle).Length != 0;
        bool hollow = HollowAmbience.CueForStyle(_environmentStyle).Length != 0;
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
            if (verdant || cinder || spine || hollow)
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
            if (cinder)
            {
                // Ash falls along the perimeter. It never sweeps across floor warnings or actors.
                position.Y = .4f + (1 - cycle) * (storm ? 4f : 3f);
                if (i / 8 == 0) position.X += (cycle - .5f) * (storm ? 1.6f : .6f);
                else position.Z += (cycle - .5f) * (storm ? 1.6f : .6f);
            }
            float scale = Mathf.Sin(cycle * Mathf.Pi);
            _ambientMotes.Multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), position));
        }
    }

    private void SetRegionalAmbience(string style)
    {
        bool cinder = CinderAmbience.CueForStyle(style).Length != 0;
        bool spine = SpineAmbience.CueForStyle(style).Length != 0;
        bool hollow = HollowAmbience.CueForStyle(style).Length != 0;
        string cue = cinder ? CinderAmbience.CueForStyle(style) : spine ? SpineAmbience.CueForStyle(style) : hollow ? HollowAmbience.CueForStyle(style) : VerdantAmbience.CueForStyle(style);
        if (cue == _ambienceCue) return;
        _ambienceCue = cue;
        if (_regionalAmbience is not null)
        { _regionalAmbience.Stop(); _regionalAmbience.StreamPaused = false; }
        if (cue.Length == 0)
        {
            if (_regionalAmbience is not null) _regionalAmbience.Stream = null;
            return;
        }
        if (_regionalAmbience is null)
        {
            // Environmental beds share Music & ambience; Master still governs the complete mix.
            ClientAudio.EnsureBuses();
            _regionalAmbience = new AudioStreamPlayer { Name = "RegionalAmbience", VolumeDb = -25, Bus = ClientAudio.MusicBus };
            AddChild(_regionalAmbience);
        }
        _regionalAmbience.Stream = cinder ? CinderAmbience.GetStream(cue) : spine ? SpineAmbience.GetStream(cue) : hollow ? HollowAmbience.GetStream(cue) : VerdantAmbience.GetStream(cue);
        _regionalAmbience.Play();
        _regionalAmbience.StreamPaused = _clock.Paused;
    }
}
