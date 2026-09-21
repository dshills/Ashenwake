using System.Globalization;
using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private Label? _legendaryReadiness;
    private long _lastOathChargeCue = -30;
    internal int LegendaryTriggerCueCount { get; private set; }
    internal string LastLegendaryTriggerCue { get; private set; } = "";
    internal string LegendaryReadinessText => _legendaryReadiness?.Visible == true ? _legendaryReadiness.Text : "";

    private void PresentLegendaryEvent(CombatEvent e, ActorPresentation? actor, Vector3 direction)
    {
        if (actor is null || actor.Health <= 0) return;
        if (e.Kind == "LegendaryReadied")
        {
            _combatEffects.Emit("legendary_ready", actor.Current, direction, new("c9b5ef"), _reduceEffects);
            return;
        }
        if (e.Kind == "LegendaryCharged")
        {
            if (e.Tick - _lastOathChargeCue < 15) return;
            _lastOathChargeCue = e.Tick;
            _combatEffects.Emit("block", actor.Current, direction, new("e0c181"), _reduceEffects);
            return;
        }
        string cue = e.ContentId switch
        {
            LegendaryEquipment.PyrePower => "legendary_pyre",
            LegendaryEquipment.OathPower => "legendary_oath",
            LegendaryEquipment.WidowPower => "legendary_widow",
            _ => ""
        };
        if (cue.Length == 0) return;
        Color color = cue == "legendary_pyre" ? new("ffa457") : cue == "legendary_oath" ? new("e0c181") : new("c9b5ef");
        _combatEffects.Emit(cue, actor.Current, direction, color, _reduceEffects);
        PlayTone(cue); LegendaryTriggerCueCount++; LastLegendaryTriggerCue = cue;
    }

    private void PresentLegendaryReadiness()
    {
        var legendary = _view.Legendary;
        if (legendary is null || !_actors.TryGetValue(1, out var player) || player.Health <= 0)
        { if (_legendaryReadiness is not null) _legendaryReadiness.Visible = false; return; }
        static string Seconds(long ticks) => (Math.Ceiling(ticks / 3d) / 10).ToString("F1", CultureInfo.InvariantCulture) + "s";
        string text = legendary.OathCharge > 0 && legendary.OathRemainingTicks > 0 ? $"REPRISAL {legendary.OathCharge} · {Seconds(legendary.OathRemainingTicks)}" : "";
        if (legendary.WidowRemainingTicks > 0)
            text += (text.Length > 0 ? "   ·   " : "") + "WIDOW READY · " + Seconds(legendary.WidowRemainingTicks);
        if (text.Length == 0) { if (_legendaryReadiness is not null) _legendaryReadiness.Visible = false; return; }
        if (_legendaryReadiness is null)
        {
            _legendaryReadiness = new Label
            {
                Name = "LegendaryReadiness",
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _legendaryReadiness.AddThemeFontSizeOverride("font_size", 14);
            _legendaryReadiness.AddThemeConstantOverride("outline_size", 4);
            _legendaryReadiness.AddThemeColorOverride("font_outline_color", new("101820"));
            _hud.AddChild(_legendaryReadiness);
        }
        _legendaryReadiness.Text = text;
        _legendaryReadiness.Modulate = legendary.OathCharge > 0 ? new("e0c181") : new("c9b5ef");
        // Keep persistent readiness clear of world-space damage and target labels.
        _legendaryReadiness.Position = _hudDock.Position + new Vector2(12, -94);
        _legendaryReadiness.Size = new(_hudDock.Size.X - 24, 20);
        _legendaryReadiness.Visible = player.AuthoredVisible;
    }

    private void PresentCombatProjectile(CombatProjectileView projectile)
    {
        string key = $"p{projectile.Id}";
        bool widow = projectile.ContentId == "effect.widow_echo";
        PresentEffect(key, projectile.Position.X, projectile.Position.Z, .15f, widow ? new("c9b5ef") : new("ffc178"), true);
        if (!widow) return;
        var mesh = _effects[key];
        mesh.Name = "WidowEcho_" + projectile.Id;
        mesh.Scale = new(.75f, .75f, 1.8f);
        Vector3 direction = PositionOf(projectile.Target.X - projectile.Position.X, projectile.Target.Z - projectile.Position.Z);
        mesh.Rotation = new(0, Mathf.Atan2(direction.X, direction.Z), 0);
        if (mesh.GetNodeOrNull<MeshInstance3D>("SilkWake") is null)
            mesh.AddChild(new MeshInstance3D
            {
                Name = "SilkWake",
                Mesh = new BoxMesh { Size = new(.045f, .045f, .7f) },
                Position = new(0, 0, -.30f),
                MaterialOverride = Material(new Color("c9b5ef99"), true),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
    }

    private void PresentCombatArea(CombatAreaView area)
    {
        string key = $"a{area.Id}";
        bool pyre = area.ContentId == "effect.pyre_trail";
        PresentEffect(key, area.Position.X, area.Position.Z, area.Radius * .001f, pyre ? new("d96b354d") : new Color(1, .38f, .13f, .24f));
        if (!pyre) return;
        var mesh = _effects[key];
        mesh.Name = "PyreTrail_" + area.Id;
        if (mesh.GetNodeOrNull<MeshInstance3D>("FireSeam") is null)
            mesh.AddChild(new MeshInstance3D
            {
                Name = "FireSeam",
                Mesh = new TorusMesh { InnerRadius = area.Radius * .00082f, OuterRadius = area.Radius * .001f, Rings = 16, RingSegments = 6 },
                Scale = new(1, .28f, 1),
                Position = Vector3.Up * .025f,
                MaterialOverride = Material(new Color("ffa457aa"), true),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
    }
}
