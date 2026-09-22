using Godot;

namespace Ashenwake.Client;

/// <summary>Bounded scene checks for cosmetic opening lights. Detached fixtures do not alter a character or room.</summary>
public static class OpeningLightingChecks
{
    public sealed record Evidence(string Context, string Style, string Quality, int Capacity, int ActiveCount,
        int NativeLights, float MinimumEnergy, float MaximumEnergy, float MaximumRange, double MotionTime);
    private static readonly string[] Styles = ["greyhaven", "road", "crypt", "monastery", "sanctum"];

    public static void Detached(Action<string, bool> check)
    {
        check("opening_lighting_supports_exact_opening_styles", Styles.All(OpeningLighting.Supports) &&
            new[] { "", "default", "verdant_ruins", "hollow_breach", "GREYHAVEN" }.All(style => !OpeningLighting.Supports(style)));
        check("opening_lighting_rejects_unknown_style", Rejects(() => OpeningLighting.Create("unknown", 12, 10)));
        foreach (var (name, value) in new[] { ("zero", 0f), ("negative", -1f), ("nan", float.NaN), ("infinity", float.PositiveInfinity), ("negative_infinity", float.NegativeInfinity) })
        {
            check("opening_lighting_rejects_width_" + name, Rejects(() => OpeningLighting.Create("road", value, 10)));
            check("opening_lighting_rejects_depth_" + name, Rejects(() => OpeningLighting.Create("road", 12, value)));
        }
        foreach (string style in Styles)
        {
            var lighting = OpeningLighting.Create(style, 12, 10);
            try
            {
                void Check(string name, bool passed) => check("opening_lighting_" + style + "_" + name, passed);
                lighting.Animate(0, false, false, "High");
                var lights = Lights(lighting); var ids = lights.Select(light => light.GetInstanceId()).ToArray();
                var positions = lights.Select(light => light.Transform).ToArray();
                var phaseZeroEnergy = lights.Select(light => light.LightEnergy).ToArray();
                lighting.Animate(0, true, true, "High");
                var baseEnergy = lights.Select(light => light.LightEnergy).ToArray();
                lighting.Animate(0, true, false, "High");
                Check("fixed_bounded_physics_free_lights", lighting.Style == style && lighting.Capacity is > 0 and <= 6 && lights.Length == lighting.Capacity &&
                    lighting.ActiveCount == lighting.Capacity && lights.All(light => light.Visible && !light.ShadowEnabled) && PhysicsFree(lighting));
                Check("finite_authored_positions_ranges_and_energy", Valid(lights) && lights.All(light => Math.Abs(light.Position.X) <= 13.5f && Math.Abs(light.Position.Z) <= 15.5f));
                lighting.Animate(.25, false, false, "High");
                Check("gentle_motion_advances_without_moving_lights", lighting.MotionTime > 0 && SameTransforms(lights, positions) &&
                    lights.Where((light, i) => Math.Abs(light.LightEnergy - baseEnergy[i]) > .00001f).Any());
                double time = lighting.MotionTime; var movingEnergy = lights.Select(light => light.LightEnergy).ToArray();
                lighting.Animate(4, true, false, "High");
                Check("pause_freezes_phase_and_energy", lighting.MotionTime == time && SameEnergy(lights, movingEnergy) && SameTransforms(lights, positions));
                lighting.Animate(4, true, false, "Performance");
                Check("quality_change_applies_while_paused", lighting.MotionTime == time && lighting.ActiveCount == Math.Min(3, lighting.Capacity) &&
                    lights.Count(light => light.Visible) == lighting.ActiveCount && ids.SequenceEqual(Lights(lighting).Select(light => light.GetInstanceId())));
                lighting.Animate(4, true, false, "High");
                Check("high_restores_original_lights_without_reallocation", lighting.MotionTime == time && lighting.ActiveCount == lighting.Capacity &&
                    SameEnergy(lights, movingEnergy) && ids.SequenceEqual(Lights(lighting).Select(light => light.GetInstanceId())));
                lighting.Animate(4, true, true, "High");
                Check("reduced_effects_resets_phase_but_keeps_base_lighting", lighting.MotionTime == 0 && lighting.ActiveCount == lighting.Capacity &&
                    SameEnergy(lights, baseEnergy) && lights.All(light => light.Visible));
                lighting.Animate(60, false, true, "Performance");
                Check("reduced_effects_stays_stable_with_lower_quality", lighting.MotionTime == 0 && lighting.ActiveCount == Math.Min(3, lighting.Capacity) && SameEnergy(lights, baseEnergy));
                lighting.Animate(1, true, false, "High");
                Check("restoring_effects_does_not_advance_paused_phase", lighting.MotionTime == 0 && lighting.ActiveCount == lighting.Capacity && SameEnergy(lights, phaseZeroEnergy));
                lighting.Animate(.25, false, false, "High");
                Check("resume_uses_repeatable_phase", lighting.MotionTime == time && SameEnergy(lights, movingEnergy));
                bool valid = true, gentle = true;
                for (int frame = 0; frame < 6000; frame++)
                {
                    lighting.Animate(.1, false, false, frame % 200 < 100 ? "High" : "Performance");
                    valid &= double.IsFinite(lighting.MotionTime) && Valid(lights) && SameTransforms(lights, positions);
                    gentle &= lights.Select((light, i) => Math.Abs(light.LightEnergy - baseEnergy[i]) <= baseEnergy[i] * .061f + .0001f).All(value => value);
                }
                Check("ten_minutes_remain_finite_gentle_and_bounded", valid && gentle && lighting.ActiveCount <= 3 &&
                    lighting.Capacity == lights.Length && ids.SequenceEqual(Lights(lighting).Select(light => light.GetInstanceId())));
                Check("long_run_creates_no_physics_or_extra_lights", PhysicsFree(lighting) && Descendants(lighting).Count() == lights.Length);
            }
            finally { lighting.Free(); }
        }
    }

    public static Evidence Inspect(Sandbox sandbox, string context, string expectedStyle, Action<string, bool> check)
    {
        string hash = sandbox.Session.StateHash;
        var root = sandbox.OpeningLights;
        check("opening_lighting_live_" + context + "_matches_room", root is not null && root.Style == expectedStyle &&
            Descendants(sandbox).OfType<OpeningLighting>().Count() == 1);
        if (root is null) throw new InvalidDataException("Missing live opening lighting.");
        var lights = Lights(root);
        int expectedCount = sandbox.GraphicsQuality == "Performance" ? Math.Min(3, root.Capacity) : root.Capacity;
        check("opening_lighting_live_" + context + "_bounded_visible_quality", root.Capacity is > 0 and <= 6 && lights.Length == root.Capacity &&
            root.ActiveCount == expectedCount && lights.Count(light => light.IsVisibleInTree()) == expectedCount && lights.All(light => !light.ShadowEnabled));
        check("opening_lighting_live_" + context + "_finite_and_cosmetic", Valid(lights) && PhysicsFree(root) && sandbox.Session.StateHash == hash);
        return new(context, root.Style, sandbox.GraphicsQuality, root.Capacity, root.ActiveCount, lights.Length,
            lights.Min(light => light.LightEnergy), lights.Max(light => light.LightEnergy), lights.Max(light => light.OmniRange), root.MotionTime);
    }

    public static float[] Energies(OpeningLighting lighting) => Lights(lighting).Select(light => light.LightEnergy).ToArray();
    public static bool SameEnergy(OpeningLighting lighting, IReadOnlyList<float> expected) => SameEnergy(Lights(lighting), expected);
    private static OmniLight3D[] Lights(Node root) => Descendants(root).OfType<OmniLight3D>().ToArray();
    private static bool SameEnergy(IReadOnlyList<OmniLight3D> lights, IReadOnlyList<float> expected)
        => lights.Count == expected.Count && lights.Select((light, i) => Math.Abs(light.LightEnergy - expected[i]) < .000001f).All(value => value);
    private static bool SameTransforms(IReadOnlyList<OmniLight3D> lights, IReadOnlyList<Transform3D> transforms)
        => lights.Count == transforms.Count && lights.Select((light, i) => light.Transform.IsEqualApprox(transforms[i])).All(value => value);
    private static bool Valid(IEnumerable<OmniLight3D> lights) => lights.All(light => light.Position.IsFinite() && light.Transform.Basis.X.IsFinite() &&
        light.Transform.Basis.Y.IsFinite() && light.Transform.Basis.Z.IsFinite() && float.IsFinite(light.LightEnergy) && light.LightEnergy is > 0 and < 3 &&
        float.IsFinite(light.OmniRange) && light.OmniRange is >= 4 and <= 6.5f &&
        float.IsFinite(light.LightColor.R) && float.IsFinite(light.LightColor.G) && float.IsFinite(light.LightColor.B));
    private static bool PhysicsFree(Node root) => !Descendants(root).Any(node => node is CollisionObject3D or CollisionShape3D or CollisionPolygon3D or NavigationRegion3D or NavigationLink3D);
    private static bool Rejects(Func<OpeningLighting> create)
    {
        OpeningLighting? value = null;
        try { value = create(); return false; }
        catch (ArgumentException) { return true; }
        finally { value?.Free(); }
    }
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(node => new[] { node }.Concat(Descendants(node)));
}
