using System.Globalization;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private static bool IsDirge(CombatHazardView? hazard) => hazard?.ContentId == "elite.dirgebound";

    private static string? OpeningCombatLabel(CombatActorView actor, CombatHazardView? hazard)
    {
        if (actor.Health <= 0 || actor.Faction != CombatFaction.Enemy) return null;
        return IsDirge(hazard) ? "DIRGE · INTERRUPT" : null;
    }

    private void PresentDirgeWarning(long id, int x, int z, int radius, long ticks)
    {
        // A hollow cyan perimeter and explicit ENEMY WARD label distinguish support
        // geometry from the filled amber/red damaging warnings. Neither is cosmetic.
        string key = $"warning{id}-dirge";
        _visibleEffects.Add(key);
        float outer = radius * .001f;
        if (!_effects.TryGetValue(key, out var rim))
        {
            rim = new MeshInstance3D
            {
                Name = "DirgeWarning_" + id,
                Mesh = new TorusMesh { InnerRadius = Math.Max(.02f, outer - .075f), OuterRadius = outer },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new("a1ecda"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            _effects[key] = rim; AddChild(rim);
            var label = new Label3D
            {
                Name = "DirgeCountdown",
                FontSize = 40,
                PixelSize = .011f,
                OutlineSize = 10,
                Modulate = new("d5fff1"),
                OutlineModulate = new("102522"),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                Position = new(0, .4f, outer * .68f)
            };
            rim.AddChild(label);
        }
        rim.Position = PositionOf(x, z) + Vector3.Up * .075f;
        double seconds = Math.Ceiling(ticks * FixedStepClock.SecondsPerTick * 10) / 10;
        rim.GetNode<Label3D>("DirgeCountdown").Text = "ENEMY WARD\n" + seconds.ToString("F1", CultureInfo.InvariantCulture) + "s";
    }
}
