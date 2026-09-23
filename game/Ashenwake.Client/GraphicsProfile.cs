using Godot;

namespace Ashenwake.Client;

/// <summary>Presentation-only quality options supported by the Compatibility renderer.</summary>
internal static class GraphicsProfile
{
    private static Shader? _reflectionShader;
    private static bool _flushingSkyTeardown;

    public static string Normalize(string? quality) => quality == "Performance" ? "Performance" : "High";

    public static float NormalizeRenderScale(float scale) => scale is 1f or 1.25f or 1.5f ? scale : 1.25f;

    public static void TrackEnvironment(WorldEnvironment owner)
    {
        // C# wrappers may outlive scene nodes until GC. Release GPU-backed sky
        // resources with their world, and recreate them if that world is reattached.
        var environment = owner.Environment;
        var enteredFrame = Engine.GetFramesDrawn();
        owner.TreeEntered += () => { ApplyBackground(environment); enteredFrame = Engine.GetFramesDrawn(); };
        owner.TreeExited += () =>
        {
            if (environment.Sky is not { } sky) return;
            // In pinned GLES3, a newly created Sky can still be on dirty_sky_list.
            // free(RID) does not unlink that list. Process it before releasing the
            // Sky, including when a scene is created and removed in one frame.
            if (!_flushingSkyTeardown && enteredFrame == Engine.GetFramesDrawn() && DisplayServer.GetName() != "headless" &&
                RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility")
            {
                _flushingSkyTeardown = true;
                try { RenderingServer.ForceDraw(false); RenderingServer.ForceSync(); }
                finally { _flushingSkyTeardown = false; }
            }
            var material = sky.SkyMaterial;
            environment.Sky = null;
            sky.Dispose();
            material?.Dispose();
        };
    }

    public static void Apply(Viewport viewport, Godot.Environment environment, DirectionalLight3D sun,
        string quality, bool reducedEffects, float renderScale = 1f)
    {
        bool high = Normalize(quality) == "High";
        viewport.Msaa3D = high ? Viewport.Msaa.Msaa8X : Viewport.Msaa.Msaa2X;
        // Bilinear supersampling is supported by pinned Godot 4.6.2 GLES3.
        // Keep text/UI at native resolution and spend the extra samples on the 3D world.
        viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = high ? NormalizeRenderScale(renderScale) : 1f;
        environment.SsaoEnabled = high;
        environment.SsaoRadius = .65f;
        environment.SsaoIntensity = 1.1f;
        environment.GlowEnabled = high && !reducedEffects;
        environment.GlowIntensity = .22f;
        environment.GlowBloom = 0;
        environment.GlowHdrThreshold = 1.2f;
        environment.GlowHdrScale = 1.1f;
        environment.TonemapMode = Godot.Environment.ToneMapper.Filmic;
        environment.TonemapExposure = 1;
        environment.TonemapWhite = 3;
        ApplyBackground(environment);
        environment.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
        environment.AmbientLightSkyContribution = .32f;
        environment.ReflectedLightSource = Godot.Environment.ReflectionSource.Sky;
        sun.DirectionalShadowMode = high ? DirectionalLight3D.ShadowMode.Parallel4Splits : DirectionalLight3D.ShadowMode.Parallel2Splits;
        sun.DirectionalShadowMaxDistance = 60;
        sun.DirectionalShadowFadeStart = 1;
        sun.ShadowBias = .1f;
        sun.ShadowNormalBias = 2;
        sun.ShadowBlur = high ? 1.3f : 1;
    }

    private static void ApplyBackground(Godot.Environment environment)
    {
        // Godot 4.6.2 GLES3 clears BG_COLOR without glow's luminance multiplier,
        // then expands it in post.glsl. The sky pass applies that multiplier correctly.
        // Keep the authored solid background separate from the cubemap's reflections.
        // Source: godot/4.6.2-stable/drivers/gles3/{rasterizer_scene_gles3.cpp,shaders/sky.glsl}.
        var shader = _reflectionShader ??= new Shader
        {
            Code = """
                shader_type sky;
                uniform vec4 background_color : source_color;
                uniform vec4 sky_top : source_color;
                uniform vec4 sky_horizon : source_color;
                uniform vec4 ground_bottom : source_color;
                uniform vec4 ground_horizon : source_color;
                void sky() {
                    if (AT_CUBEMAP_PASS) {
                        float blend = sqrt(clamp(abs(EYEDIR.y), 0.0, 1.0));
                        COLOR = EYEDIR.y >= 0.0
                            ? mix(sky_horizon.rgb, sky_top.rgb, blend) * 0.65
                            : mix(ground_horizon.rgb, ground_bottom.rgb, blend) * 0.35;
                    } else {
                        COLOR = background_color.rgb;
                    }
                }
                """
        };
        environment.Sky ??= new Sky();
        if (environment.Sky.SkyMaterial is not ShaderMaterial material || material.Shader != shader)
        {
            material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("sky_top", new Color("647d9a"));
            material.SetShaderParameter("sky_horizon", new Color("a3abb3"));
            material.SetShaderParameter("ground_bottom", new Color("222932"));
            material.SetShaderParameter("ground_horizon", new Color("737c84"));
            environment.Sky.SkyMaterial = material;
        }
        material.SetShaderParameter("background_color", environment.BackgroundColor);
        environment.BackgroundMode = Godot.Environment.BGMode.Sky;
    }
}
