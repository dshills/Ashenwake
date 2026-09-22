using System.Globalization;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private static bool IsMidgameSupport(string id) => id is "campaign.spore_mend" or "campaign.forge_bellows";
    private static bool IsSupportWarning(CombatHazardView? hazard) => hazard is not null && CombatSession.IsSupportHazard(hazard.ContentId);
    private static string CombatSeconds(long ticks) => (Math.Ceiling(ticks * FixedStepClock.SecondsPerTick * 10) / 10).ToString("F1", CultureInfo.InvariantCulture) + "s";
    private static string? MidgameCombatLabel(CombatActorView actor, CombatHazardView? hazard)
    {
        if (actor.Health <= 0 || actor.Faction != CombatFaction.Enemy) return null;
        if (hazard?.ContentId == "campaign.spore_mend") return "SPORE MEND · INTERRUPT";
        if (hazard?.ContentId == "campaign.forge_bellows") return "BELLOWS · INTERRUPT";
        if (actor.DefinitionId is not ("boss.rootheart" or "boss.furnace_spindle")) return null;
        if (actor.Shielded) return "PROTECTED · SEVER A ROOT";
        if (actor.BossGuardedTicks > 0) return "CORE GUARDED · " + CombatSeconds(actor.BossGuardedTicks);
        if (actor.BossRecoveryTicks > 0) return "EXPOSED · RECOVERING " + CombatSeconds(actor.BossRecoveryTicks);
        return null;
    }

    private void PresentMidgameSupport(long id, int x, int z, int radius, long ticks, string contentId)
    {
        string key = $"warning{id}-support"; _visibleEffects.Add(key);
        float outer = radius * .001f;
        if (!_effects.TryGetValue(key, out var rim))
        {
            Color color = contentId == "campaign.spore_mend" ? new("bbf58b") : new("f5d5a2");
            rim = new MeshInstance3D
            {
                Name = "MidgameSupport_" + id,
                Mesh = new TorusMesh { InnerRadius = Math.Max(.02f, outer - .075f), OuterRadius = outer },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            _effects[key] = rim; AddChild(rim);
            var label = MidgameWarningLabel("SupportCountdown", color);
            label.Position = new(0, .4f, outer * .68f); rim.AddChild(label);
        }
        rim.Position = PositionOf(x, z) + Vector3.Up * .075f;
        rim.GetNode<Label3D>("SupportCountdown").Text = (contentId == "campaign.spore_mend" ? "ENEMY HEAL" : "ENEMY POWER") + "\n" + CombatSeconds(ticks);
    }

    private static string MidgameHazardName(string contentId) => contentId switch
    {
        "campaign.root_tangle" => "ROOT TANGLE",
        "campaign.root_spores" => "SPORE BURST",
        "campaign.furnace_vent" => "FURNACE VENT",
        "campaign.slag" => "FALLING SLAG",
        _ => ""
    };

    private static Label3D MidgameWarningLabel(string name, Color color) => new()
    {
        Name = name,
        FontSize = 40,
        PixelSize = .011f,
        OutlineSize = 10,
        Modulate = color,
        OutlineModulate = new("171e25"),
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true
    };

    private static void PresentMidgameHazardLabel(MeshInstance3D parent, long id, string contentId, long ticks, Vector3 position)
    {
        string title = MidgameHazardName(contentId);
        if (title.Length == 0) return;
        string name = "MidgameHazard_" + id;
        var label = parent.GetNodeOrNull<Label3D>(name);
        if (label is null) { label = MidgameWarningLabel(name, new("fff3d4")); parent.AddChild(label); }
        label.GlobalPosition = position;
        label.Text = title + "\n" + CombatSeconds(ticks);
    }

    private void SynchronizeForgeOvercharge(CombatActorView actor)
    {
        var presentation = _actors[actor.Id];
        var strip = presentation.HealthBar.GetNodeOrNull<Sprite3D>("ForgePowerStrip");
        bool active = actor.Faction == CombatFaction.Enemy && actor.Health > 0 && actor.ForgeOverchargeTicks > 0;
        if (strip is null && !active) return;
        if (strip is null)
        {
            strip = CombatBarSprite("ForgePowerStrip", new("f5b565"), 4);
            presentation.HealthBar.AddChild(strip);
        }
        strip.Visible = active;
        if (!active) return;
        float width = CombatBarWidth * Math.Clamp(actor.ForgeOverchargeTicks / 90f, 0, 1);
        strip.RegionRect = new(0, 0, width, 3);
        strip.Offset = new((width - CombatBarWidth) * .5f, 6);
        strip.PixelSize = presentation.HealthFill.PixelSize;
        // One compact strip per buffed actor; exact effect and time belong to the focus card.
        string detail = "OVERCHARGED +20% DAMAGE · " + CombatSeconds(actor.ForgeOverchargeTicks);
        presentation.ConditionDetail = detail + (presentation.ConditionDetail.Length == 0 ? "" : "\n" + presentation.ConditionDetail);
    }
}
