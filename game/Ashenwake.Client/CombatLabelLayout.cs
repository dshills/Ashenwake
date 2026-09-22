using Godot;

namespace Ashenwake.Client;

/// <summary>Bounded cosmetic placement; every shifted label retains its original world anchor.</summary>
internal static class CombatLabelLayout
{
    public static void Place(Camera3D camera, Label3D label, Vector3 anchor, Rect2 safe, List<Rect2> occupied)
    {
        label.GlobalPosition = anchor;
        Rect2 original = Sandbox.CampaignLabelScreenRect(camera, label), chosen = original;
        var fit = new Vector2(Math.Clamp(original.Position.X, safe.Position.X, Math.Max(safe.Position.X, safe.End.X - original.Size.X)),
            Math.Clamp(original.Position.Y, safe.Position.Y, Math.Max(safe.Position.Y, safe.End.Y - original.Size.Y))) - original.Position;
        chosen = new Rect2(original.Position + fit, original.Size);
        float best = float.MaxValue;
        Vector2 offset = fit;
        // 169 candidates, including the clamped anchor. Crowded scenes choose the
        // least obstructed position without hiding a required warning.
        for (int row = -6; row <= 6; row++)
            for (int column = -6; column <= 6; column++)
            {
                var delta = fit + new Vector2(column * 36, row * 30);
                var candidate = new Rect2(original.Position + delta, original.Size);
                if (!safe.Encloses(candidate)) continue;
                float overlap = 0;
                foreach (var rect in occupied) overlap += candidate.Intersection(rect).Area;
                float score = (overlap > 0 ? 1_000_000_000 + overlap * 1000 : 0) + delta.LengthSquared();
                if (score >= best) continue;
                best = score; offset = delta; chosen = candidate;
            }
        float pixelsPerUnit = camera.UnprojectPosition(anchor + camera.GlobalBasis.X).DistanceTo(camera.UnprojectPosition(anchor));
        if (pixelsPerUnit > .001f) label.GlobalPosition = anchor + (camera.GlobalBasis.X * offset.X - camera.GlobalBasis.Y * offset.Y) / pixelsPerUnit;
        occupied.Add(chosen);
        var leader = label.GetNodeOrNull<MeshInstance3D>("WarningLeader");
        if (leader is null && offset.LengthSquared() > 1)
        {
            leader = new MeshInstance3D
            {
                Name = "WarningLeader",
                Mesh = new BoxMesh(),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new("e5cfa6"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            label.AddChild(leader);
        }
        if (leader is null) return;
        leader.Visible = offset.LengthSquared() > 1;
        if (!leader.Visible) return;
        var direction = label.GlobalPosition - anchor;
        ((BoxMesh)leader.Mesh).Size = new(.018f, .018f, direction.Length());
        leader.GlobalPosition = (label.GlobalPosition + anchor) * .5f;
        // The displacement is a combination of camera X/Y above, so camera Z
        // is perpendicular to it even when the camera looks straight down.
        leader.GlobalBasis = Basis.LookingAt(direction, camera.GlobalBasis.Z);
    }
}
