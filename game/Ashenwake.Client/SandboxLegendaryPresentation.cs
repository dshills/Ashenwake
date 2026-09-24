using System.Globalization;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private Label? _legendaryReadiness;
    private long _lastOathChargeCue = -30;
    private Label? _legendaryTrigger;
    private string _legendaryTriggerPower = "", _legendaryTriggerText = "";
    private long _legendaryTriggerUntil;
    internal string LegendaryTriggerText => _legendaryTrigger?.Visible == true ? _legendaryTrigger.Text : "";
    internal int LegendaryTriggerCueCount { get; private set; }
    internal string LastLegendaryTriggerCue { get; private set; } = "";
    internal string LegendaryReadinessText => _legendaryReadiness?.Visible == true ? _legendaryReadiness.Text : "";

    private void PresentLegendaryEvent(CombatEvent e, ActorPresentation? actor, ActorPresentation? target, Vector3 direction)
    {
        if (actor is null || actor.Health <= 0) return;
        if (e.Kind == "LegendaryReadied" && e.ContentId != LegendaryEquipment.HourPower)
        {
            _combatEffects.Emit("legendary_ready", actor.Current, direction, new(e.ContentId == LegendaryEquipment.FurnacePower ? "ffc06c" : "c9b5ef"), _reduceEffects);
            return;
        }
        if (e.Kind == "LegendaryCharged")
        {
            if (e.ContentId == LegendaryEquipment.WitnessPower) return;
            if (e.Tick - _lastOathChargeCue < 15) return;
            _lastOathChargeCue = e.Tick;
            _combatEffects.Emit("block", actor.Current, direction, new("e0c181"), _reduceEffects);
            return;
        }
        string cue = e.ContentId switch
        {
            LegendaryEquipment.GriefPower => "legendary_verdict",
            LegendaryEquipment.WidowthornPower => "legendary_rotwake",
            LegendaryEquipment.EmberwakePower => "legendary_cinder",
            LegendaryEquipment.PyrePower => "legendary_pyre",
            LegendaryEquipment.OathPower => "legendary_oath",
            LegendaryEquipment.WidowPower => "legendary_widow",
            LegendaryEquipment.RotwakePower => "legendary_rotwake",
            LegendaryEquipment.MourningPower => "legendary_chorus",
            LegendaryEquipment.FurnacePower => "legendary_cinder",
            LegendaryEquipment.CrownPower => "legendary_verdict",
            LegendaryEquipment.WitnessPower => "legendary_witness",
            LegendaryEquipment.HourPower => "legendary_hour",
            _ => ""
        };
        if (cue.Length == 0) return;
        Color color = cue switch
        {
            "legendary_pyre" => new("ffa457"),
            "legendary_oath" => new("e0c181"),
            "legendary_rotwake" => new("b4d879"),
            "legendary_chorus" => new("99d9cf"),
            "legendary_cinder" => new("ffc06c"),
            "legendary_verdict" => new("ffe2a0"),
            "legendary_witness" => new("beafff"),
            "legendary_hour" => new("97eee6"),
            _ => new("c9b5ef")
        };
        // Wake and chorus happen at the struck foe, including the corpse that releases a wake.
        // Cosmetic visibility never reveals a hidden actor; all targeting remains in Core.
        var origin = cue is "legendary_rotwake" or "legendary_chorus" or "legendary_witness" ? target : actor;
        if (origin?.AuthoredVisible == true) _combatEffects.Emit(cue, origin.Current, direction, color, _reduceEffects);
        string message = e.ContentId switch
        {
            LegendaryEquipment.GriefPower => $"GRIEF’S REPRIEVE · +{e.Amount} health",
            LegendaryEquipment.WidowthornPower => $"WIDOWTHORN · {e.Amount} foes rooted",
            LegendaryEquipment.EmberwakePower => "EMBERWAKE · +20% direct skill damage",
            _ => cue switch
            {
                "legendary_verdict" => $"UNSPOKEN VERDICT · +{e.Amount} barrier",
                "legendary_witness" => $"WITNESS VOW · {e.Amount} Void burst",
                "legendary_hour" => e.Kind == "LegendaryReadied" ? "BORROWED HOUR · 3 half-cooldown casts ready" : $"BORROWED HOUR · cooldown halved · {e.Amount} left",
                "legendary_rotwake" => $"VIRULENT WAKE · {e.Amount} {(e.Amount == 1 ? "foe" : "foes")} poisoned",
                "legendary_chorus" => $"MOURNING CHOIR · {e.Amount} {(e.Amount == 1 ? "summon" : "summons")} rallied",
                "legendary_cinder" => "CINDER CYCLE · " + (_view.Discipline == "Arcanist" ? "−" : "+") + e.Amount + " " + _view.ResourceName,
                _ => ""
            }
        };
        if (message.Length > 0)
        {
            _legendaryTriggerText = message; _legendaryTriggerPower = e.ContentId;
            _legendaryTriggerUntil = e.Tick + 45;
        }
        PlayTone(cue); LegendaryTriggerCueCount++; LastLegendaryTriggerCue = cue;
    }

    private void PresentLegendaryReadiness()
    {
        var legendary = _view.Legendary;
        PresentLegendaryTrigger();
        if (!_actors.TryGetValue(1, out var player) || player.Health <= 0)
        { if (_legendaryReadiness is not null) _legendaryReadiness.Visible = false; return; }
        string text = CombinedEquipmentReadiness();
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
        _legendaryReadiness.Modulate = legendary?.OathCharge > 0 ? new("e0c181") : new("c9b5ef");
        // Keep persistent readiness clear of world-space damage and target labels.
        int lines = text.Count(c => c == '\n') + 1;
        float height = Math.Max(42, lines * 22);
        _legendaryReadiness.Position = _hudDock.Position + new Vector2(12, -78 - height);
        _legendaryReadiness.Size = new(_hudDock.Size.X - 24, height);
        _legendaryReadiness.Visible = player.AuthoredVisible;
    }

    internal static string LegendaryReadiness(CombatLegendaryView legendary, string discipline)
    {
        static string Seconds(long ticks) => (Math.Ceiling(ticks / 3d) / 10).ToString("F1", CultureInfo.InvariantCulture) + "s";
        var first = new List<string>(); var second = new List<string>(); var third = new List<string>();
        if (legendary.OathCharge > 0 && legendary.OathRemainingTicks > 0)
            first.Add($"REPRISAL {legendary.OathCharge} · {Seconds(legendary.OathRemainingTicks)}");
        if (legendary.WidowRemainingTicks > 0) first.Add("WIDOW READY · " + Seconds(legendary.WidowRemainingTicks));
        if (legendary.VirulentEquipped) second.Add(legendary.VirulentRemainingTicks > 0 ? "ROTWAKE " + Seconds(legendary.VirulentRemainingTicks) : "ROTWAKE READY");
        if (legendary.ChorusEquipped) second.Add(legendary.ChorusRemainingTicks > 0 ? "CHOIR " + Seconds(legendary.ChorusRemainingTicks) : legendary.ChorusSummons > 0 ? "CHOIR READY" : "CHOIR · NO READY SUMMON");
        if (legendary.CinderEquipped)
            second.Add(legendary.CinderRemainingTicks > 0
                ? "CINDER · " + (discipline == "Arcanist" ? "VENT" : discipline == "Gravecaller" ? "HARVEST" : "GENERATE") + " · " + Seconds(legendary.CinderRemainingTicks)
                : "CINDER · CAST 20+");
        if (legendary.VerdictEquipped) third.Add(legendary.VerdictRemainingTicks > 0 ? "VERDICT " + Seconds(legendary.VerdictRemainingTicks) : "VERDICT READY");
        if (legendary.WitnessEquipped) third.Add(legendary.WitnessStacks > 0 ? $"WITNESS {legendary.WitnessStacks}/4 · {Seconds(legendary.WitnessRemainingTicks)}" : "WITNESS 0/4");
        if (legendary.HourEquipped) third.Add(legendary.HourCharges > 0 ? $"HOUR {legendary.HourCharges} · {Seconds(legendary.HourRemainingTicks)}" : "HOUR · ULTIMATE");
        var fourth = new List<string>();
        if (legendary.GriefEquipped) fourth.Add(legendary.GriefRemainingTicks > 0 ? "REPRIEVE " + Seconds(legendary.GriefRemainingTicks) : "REPRIEVE · INTERRUPT");
        if (legendary.WidowthornEquipped) fourth.Add(legendary.WidowthornRemainingTicks > 0 ? "WIDOWTHORN " + Seconds(legendary.WidowthornRemainingTicks) : "WIDOWTHORN · POISON KILL");
        if (legendary.EmberwakeEquipped) fourth.Add(legendary.EmberwakeRemainingTicks > 0 ? "EMBERWAKE +20% · " + Seconds(legendary.EmberwakeRemainingTicks) : legendary.EmberwakeCooldownTicks > 0 ? "EMBERWAKE " + Seconds(legendary.EmberwakeCooldownTicks) : "EMBERWAKE · DODGE FIRE");
        return string.Join("\n", new[] { string.Join("   ·   ", first), string.Join("   ·   ", second), string.Join("   ·   ", third), string.Join("   ·   ", fourth) }.Where(line => line.Length > 0));
    }

    private void PresentLegendaryTrigger()
    {
        var legendary = _view.Legendary;
        bool equipped = _legendaryTriggerPower switch
        {
            EquipmentSets.LastVigil => _view.EquipmentSets?.LastVigilActive == true,
            EquipmentSets.Briarbound => _view.EquipmentSets?.BriarboundActive == true,
            EquipmentSets.Ashrunner => _view.EquipmentSets?.AshrunnerActive == true,
            LegendaryEquipment.GriefPower => legendary?.GriefEquipped == true,
            LegendaryEquipment.WidowthornPower => legendary?.WidowthornEquipped == true,
            LegendaryEquipment.EmberwakePower => legendary?.EmberwakeEquipped == true,
            LegendaryEquipment.RotwakePower => legendary?.VirulentEquipped == true,
            LegendaryEquipment.MourningPower => legendary?.ChorusEquipped == true,
            LegendaryEquipment.FurnacePower => legendary?.CinderEquipped == true,
            LegendaryEquipment.CrownPower => legendary?.VerdictEquipped == true,
            LegendaryEquipment.WitnessPower => legendary?.WitnessEquipped == true,
            LegendaryEquipment.HourPower => legendary?.HourEquipped == true,
            _ => false
        };
        if (!equipped || _view.Tick >= _legendaryTriggerUntil || !_actors.TryGetValue(1, out var player) || player.Health <= 0)
        { if (_legendaryTrigger is not null) _legendaryTrigger.Visible = false; return; }
        if (_legendaryTrigger is null)
        {
            _legendaryTrigger = new Label { Name = "LegendaryTrigger", MouseFilter = Control.MouseFilterEnum.Ignore };
            _legendaryTrigger.AddThemeFontSizeOverride("font_size", 13);
            _legendaryTrigger.AddThemeConstantOverride("outline_size", 4);
            _legendaryTrigger.AddThemeColorOverride("font_outline_color", new("101820"));
            _hud.AddChild(_legendaryTrigger);
        }
        _legendaryTrigger.Text = _legendaryTriggerText;
        _legendaryTrigger.Modulate = new("e0c181");
        int lines = CombinedEquipmentReadiness().Count(c => c == '\n') + 1;
        _legendaryTrigger.Position = _hudDock.Position + new Vector2(12, -100 - Math.Max(42, lines * 22));
        _legendaryTrigger.Size = new(_hudDock.Size.X - 24, 20);
        _legendaryTrigger.Visible = player.AuthoredVisible;
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
        bool briar = area.ContentId == "effect.set_briar_thorns";
        bool pyre = area.ContentId is "effect.pyre_trail" or "effect.set_ashrunner_trail";
        PresentEffect(key, area.Position.X, area.Position.Z, area.Radius * .001f, briar ? new("83b66560") : pyre ? new("d96b354d") : new Color(1, .38f, .13f, .24f));
        if (briar)
        {
            PresentBriarPatch(_effects[key], area);
            return;
        }
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
