using Godot;

namespace Ashenwake.Client;

/// <summary>
/// A small physical clue at an authoritative interaction point. A previous known point supplies
/// the heading of a few nearby marks only; these marks never claim a route through obstacles.
/// </summary>
public partial class VerdantTrailVisual : Node3D
{
    private readonly Node3D _details = new() { Name = "FreshClueDetails" };
    private readonly Node3D _focus = new() { Name = "ClueFocus", Visible = false };
    private readonly StandardMaterial3D _focusMaterial = new()
    {
        AlbedoColor = new("d2d09d"),
        Roughness = .92f,
        EmissionEnabled = true,
        Emission = new("d2d09d"),
        EmissionEnergyMultiplier = .4f
    };
    private float _clock;
    public string ClueId { get; private set; } = "";
    public bool Focused { get; private set; }
    public bool Tracked { get; private set; }
    public int TrailMarkCount { get; private set; }
    public const float MaximumRadius = .99f;
    public const float MaximumPropHeight = .49f;

    public static VerdantTrailVisual Create(string clueId, Vector3 position, Vector3? previousPosition = null)
    {
        ArgumentNullException.ThrowIfNull(clueId);
        var visual = new VerdantTrailVisual { Name = "VerdantTrailVisual", ClueId = clueId, Position = position };
        Vector3 heading = Vector3.Forward;
        if (previousPosition.HasValue)
        {
            Vector3 from = previousPosition.Value - position;
            from.Y = 0;
            if (from.LengthSquared() > .0001f) heading = from.Normalized();
        }
        visual.Build(heading, previousPosition.HasValue);
        return visual;
    }

    public void SetFocused(bool focused)
    {
        Focused = focused && !Tracked;
        _focus.Visible = Focused;
        if (!Focused) _focus.Scale = Vector3.One;
    }

    public void MarkTracked()
    {
        if (Tracked) return;
        Tracked = true;
        _details.Visible = false;
        SetFocused(false);
    }

    public void Animate(double delta, bool paused, bool reducedEffects)
    {
        if (paused) return;
        if (reducedEffects || !Focused)
        {
            _focus.Scale = Vector3.One;
            _focusMaterial.EmissionEnergyMultiplier = .4f;
            return;
        }
        _clock += (float)Math.Clamp(delta, 0, .1);
        float breath = Mathf.Sin(_clock * 1.5f);
        _focus.Scale = new(1 + breath * .012f, 1, 1 + breath * .012f);
        _focusMaterial.EmissionEnergyMultiplier = .4f + breath * .04f;
    }

    private void Build(Vector3 heading, bool knownPrevious)
    {
        var clue = new EnvironmentBuilder(this, "ClueRemains");
        AddChild(_details);
        var fresh = new EnvironmentBuilder(_details, "ClueHighlights");
        float yaw = Mathf.RadToDeg(Mathf.Atan2(heading.X, heading.Z));
        switch (ClueId)
        {
            case "clue.shed_bark": BuildBark(clue, fresh); break;
            case "clue.reversed_tracks": BuildReversedTracks(clue, fresh, yaw); break;
            case "clue.heartwood_nest": BuildNest(clue, fresh); break;
            default: BuildBark(clue, fresh); break;
        }
        if (knownPrevious)
        {
            // Never connect two anchors with a false navigable line. Everything remains within
            // one metre of this clue, at Y <= .038 and beneath the combat-warning plane.
            Vector3 side = new(-heading.Z, 0, heading.X);
            for (int i = 0; i < 4; i++)
            {
                float distance = .56f + i * .115f;
                Vector3 at = heading * distance + side * (i % 2 == 0 ? .065f : -.065f);
                at.Y = .022f;
                clue.Box(new(.045f, .018f, .10f), at, "8b9471", new(0, yaw + (i % 2 == 0 ? 15 : -15), 0));
                TrailMarkCount++;
            }
        }
        clue.Flush(); fresh.Flush();
        AddChild(_focus);
        // A thin broken halo leaves warning fills and nearby floor texture readable.
        var focusArt = new EnvironmentBuilder(_focus, "BrokenHalo");
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.Tau / 8;
            focusArt.Box(new(.14f, .012f, .026f), new(Mathf.Sin(angle) * .64f, .031f, Mathf.Cos(angle) * .64f),
                "d2d09d", new(0, Mathf.RadToDeg(angle), 0));
        }
        focusArt.Flush();
        foreach (var group in _focus.GetChildren())
            foreach (var mesh in group.GetChildren().OfType<MeshInstance3D>())
            {
                mesh.MaterialOverride = _focusMaterial;
                mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
    }

    private static void BuildBark(EnvironmentBuilder clue, EnvironmentBuilder fresh)
    {
        clue.Box(new(.57f, .035f, .34f), new(0, .025f, 0), "3d4c39", new(0, -24, 0));
        clue.Box(new(.19f, .055f, .44f), new(-.15f, .073f, .04f), "87906b", new(-7, -28, 12));
        clue.Box(new(.16f, .36f, .08f), new(.08f, .19f, -.11f), "687553", new(-18, 0, -22));
        clue.Box(new(.14f, .17f, .075f), new(.2f, .35f, -.15f), "a1a77b", new(-12, 0, -49));
        clue.Box(new(.075f, .045f, .17f), new(.27f, .025f, .17f), "84906b", new(0, 33, 0));
        // Pale torn interior and three amber sap beads distinguish bark from dropped equipment.
        fresh.Box(new(.045f, .29f, .025f), new(.062f, .205f, -.057f), "d3c49a", new(-18, 0, -22));
        for (int i = 0; i < 3; i++)
            fresh.Cylinder(.026f, .035f, .035f, new(-.045f + i * .066f, .068f, .14f + i * .025f), "d1a96a");
    }

    private static void BuildReversedTracks(EnvironmentBuilder clue, EnvironmentBuilder fresh, float yaw)
    {
        var basis = Basis.FromEuler(new(0, Mathf.DegToRad(yaw), 0));
        for (int i = 0; i < 3; i++)
        {
            Vector3 center = basis * new Vector3(i % 2 == 0 ? -.14f : .14f, 0, (i - 1) * .25f);
            // The split hoof points back toward the already-known clue, while soil drag marks
            // lie on the opposite side. This is readable reversed tracking, not a route arrow.
            foreach (float side in new[] { -.046f, .046f })
            {
                Vector3 hoof = center + basis * new Vector3(side, .013f, 0);
                clue.Box(new(.068f, .012f, .135f), hoof, "253a32", new(0, yaw + side * 85, 0));
                clue.Box(new(.027f, .014f, .075f), center + basis * new Vector3(side * 1.2f, .021f, -.11f), "92a17b", new(0, yaw, 0));
            }
            fresh.Box(new(.035f, .015f, .15f), center + basis * new Vector3(0, .03f, -.19f), "c0bb8c", new(0, yaw, 0));
        }
    }

    private static void BuildNest(EnvironmentBuilder clue, EnvironmentBuilder fresh)
    {
        for (int i = 0; i < 9; i++)
        {
            float angle = i * Mathf.Tau / 9;
            Vector3 at = new(Mathf.Sin(angle) * .29f, .08f + i % 2 * .025f, Mathf.Cos(angle) * .29f);
            clue.Box(new(.42f, .048f, .045f), at, i % 2 == 0 ? "667552" : "87926a", new(0, Mathf.RadToDeg(angle), i % 2 == 0 ? 7 : -7));
        }
        clue.Cylinder(.23f, .18f, .045f, new(0, .04f, 0), "3a4935");
        foreach (int side in new[] { -1, 1 })
        {
            Vector3 start = new(side * .09f, .09f, .06f);
            Vector3 elbow = new(side * .17f, .27f, -.015f);
            Vector3 tip = new(side * .3f, .425f, -.07f);
            clue.Beam(start, elbow, .065f, "aaa982");
            clue.Beam(elbow, tip, .045f, "d0c8a3");
            clue.Beam(elbow, new(side * .095f, .405f, -.04f), .035f, "d0c8a3");
        }
        fresh.Cylinder(.1f, .03f, .22f, new(0, .175f, .11f), "b4c187", new(0, 0, -15));
        fresh.Cylinder(.075f, .01f, .18f, new(.07f, .13f, .13f), "8fad77", new(0, 0, 38));
    }
}
