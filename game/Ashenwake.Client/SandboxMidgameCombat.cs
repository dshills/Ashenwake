using System.Globalization;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private static bool IsMidgameSupport(string id) => id is "campaign.spore_mend" or "campaign.forge_bellows" or "campaign.oath_ward";
    private static bool IsSupportWarning(CombatHazardView? hazard) => hazard is not null && CombatSession.IsSupportHazard(hazard.ContentId);
    private static string CombatSeconds(long ticks) => (Math.Ceiling(ticks * FixedStepClock.SecondsPerTick * 10) / 10).ToString("F1", CultureInfo.InvariantCulture) + "s";
    private static string? MidgameCombatLabel(CombatActorView actor, CombatHazardView? hazard)
    {
        if (actor.Health <= 0 || actor.Faction != CombatFaction.Enemy) return null;
        if (hazard?.ContentId == "campaign.spore_mend") return "SPORE MEND · INTERRUPT";
        if (hazard?.ContentId == "campaign.forge_bellows") return "BELLOWS · INTERRUPT";
        if (hazard?.ContentId == "campaign.oath_ward") return "OATH WARD · INTERRUPT";
        if (actor.DefinitionId is not ("boss.rootheart" or "boss.furnace_spindle" or "boss.covenant_warden" or "boss.breach_heart")) return null;
        if (actor.Shielded) return actor.DefinitionId == "boss.breach_heart" ? "PROTECTED · BREAK A SEAL" : "PROTECTED · SEVER A ROOT";
        if (actor.BossGuardedTicks > 0) return (actor.DefinitionId == "boss.covenant_warden" ? "OATH GUARDED · " : "CORE GUARDED · ") + CombatSeconds(actor.BossGuardedTicks);
        if (actor.BossRecoveryTicks > 0) return "EXPOSED · RECOVERING " + CombatSeconds(actor.BossRecoveryTicks);
        return null;
    }

    private void PresentMidgameSupport(long id, int x, int z, int radius, long ticks, string contentId)
    {
        string key = $"warning{id}-support"; _visibleEffects.Add(key);
        float outer = radius * .001f;
        if (!_effects.TryGetValue(key, out var rim))
        {
            Color color = contentId switch { "campaign.spore_mend" => new("bbf58b"), "campaign.oath_ward" => new("a1ecda"), _ => new("f5d5a2") };
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
        rim.GetNode<Label3D>("SupportCountdown").Text = (contentId switch { "campaign.spore_mend" => "ENEMY HEAL", "campaign.oath_ward" => "ENEMY WARD", _ => "ENEMY POWER" }) + "\n" + CombatSeconds(ticks);
    }

    private static string MidgameHazardName(string contentId) => contentId switch
    {
        "campaign.root_tangle" => "ROOT TANGLE",
        "campaign.root_spores" => "SPORE BURST",
        "campaign.furnace_vent" => "FURNACE VENT",
        "campaign.slag" => "FALLING SLAG",
        "campaign.oathmark" or "campaign.oath_mark" => "OATH MARK",
        "campaign.covenant_fault" => "COVENANT FAULT",
        "campaign.breach_echo" => "FIRST ECHO",
        "campaign.returning_echo" => "RETURNING ECHO",
        "campaign.seal_sweep" => "SEAL SWEEP",
        "campaign.causalecho" => "CAUSAL ECHO",
        "rule.causalechoes" => "ROOM ECHO",
        "campaign.memoryarrow" => "MEMORY ARROW",
        "campaign.shadowdouble" => "SHADOW STRIKE",
        "elite.stormbound" => "LIGHTNING LINK",
        "elite.null" => "NULL FIELD · MOVE AWAY",
        "elite.riftborn" => "RIFT STRIKE",
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

    private readonly Dictionary<long, (Label3D Label, Vector3 Anchor)> _warningLabelAnchors = [];

    private void PresentMidgameHazardLabel(MeshInstance3D parent, CombatHazardView hazard, Vector3 position)
    {
        string title = MidgameHazardName(hazard.ContentId);
        if (title.Length == 0) return;
        bool echo = hazard.ContentId is "campaign.breach_echo" or "campaign.returning_echo" or "campaign.causalecho" or "rule.causalechoes";
        string name = (echo ? "EchoWarning_" : "MidgameHazard_") + hazard.Id;
        var label = parent.GetNodeOrNull<Label3D>(name);
        if (label is null) { label = MidgameWarningLabel(name, new("fff3d4")); parent.AddChild(label); }
        label.GlobalPosition = position;
        _warningLabelAnchors[hazard.Id] = (label, position);
        string sequence = hazard.SequenceCount > 0 ? $" {hazard.SequenceIndex}/{hazard.SequenceCount}" : "";
        label.Text = title + sequence + "\n" + CombatSeconds(hazard.RemainingTicks);
    }

    private Vector3 CampaignCircleLabelPosition(CombatHazardView hazard)
    {
        // Delayed strikes often share the player's captured position. Stack their
        // labels in resolution order, leaving each exact circle and deadline intact.
        int rank = (_view.CampaignHazards ?? []).Where(other => other.Kind == "Circle" && other.Position == hazard.Position &&
            !CombatSession.IsSupportHazard(other.ContentId) && MidgameHazardName(other.ContentId).Length > 0)
            .OrderBy(other => other.RemainingTicks).ThenBy(other => other.Id).TakeWhile(other => other.Id != hazard.Id).Count();
        return PositionOf(hazard.Position.X, hazard.Position.Z) + new Vector3(0, .4f + rank * 1.35f, hazard.Radius * .00068f);
    }

    internal static Rect2 CampaignLabelScreenRect(Camera3D camera, Label3D label)
    {
        var font = label.Font ?? ThemeDB.FallbackFont;
        var size = font.GetMultilineStringSize(label.Text, HorizontalAlignment.Left, -1, label.FontSize);
        size += Vector2.One * label.OutlineSize * 2;
        float scale = camera.UnprojectPosition(label.GlobalPosition + camera.GlobalBasis.X).DistanceTo(camera.UnprojectPosition(label.GlobalPosition));
        size *= label.PixelSize * scale;
        var center = camera.UnprojectPosition(label.GlobalPosition);
        return new Rect2(center - new Vector2(size.X * .5f, label.VerticalAlignment == VerticalAlignment.Bottom ? size.Y : size.Y * .5f), size).Grow(3);
    }

    private void LayoutCampaignWarningLabels()
    {
        var hazards = _view.CampaignHazards ?? [];
        var live = hazards.Select(hazard => hazard.Id).ToHashSet();
        foreach (long stale in _warningLabelAnchors.Keys.Where(id => !live.Contains(id)).ToArray()) _warningLabelAnchors.Remove(stale);
        if (_warningLabelAnchors.Count == 0) return;
        // Billboard text can collide even when its ground circles do not. Reserve
        // visible actor instructions and earlier warnings before placing later ones.
        var occupied = _actors.Values.Where(actor => actor.Label.IsVisibleInTree()).Select(actor => CampaignLabelScreenRect(_camera, actor.Label)).ToList();
        if (_targetDetail?.IsVisibleInTree() == true) occupied.Add(_targetDetail.GetGlobalRect().Grow(4));
        var viewport = GetViewport().GetVisibleRect();
        var safe = new Rect2(viewport.Position + new Vector2(8, 82), new Vector2(viewport.Size.X - 16, Math.Max(1, viewport.Size.Y - 290)));
        foreach (var hazard in hazards.OrderBy(hazard => hazard.RemainingTicks).ThenBy(hazard => hazard.Id))
        {
            if (!_warningLabelAnchors.TryGetValue(hazard.Id, out var entry)) continue;
            var label = entry.Label;
            label.GlobalPosition = entry.Anchor;
            Rect2 original = CampaignLabelScreenRect(_camera, label), chosen = original;
            float best = float.MaxValue;
            Vector2 offset = Vector2.Zero;
            // At most 81 candidates per label and 32 authoritative hazards. Keep
            // the nearest clear placement, or the least obstructed bounded one.
            for (int row = -4; row <= 4; row++)
                for (int column = -4; column <= 4; column++)
                {
                    var delta = new Vector2(column * 36, row * 30);
                    var candidate = new Rect2(original.Position + delta, original.Size);
                    if (!safe.Encloses(candidate)) continue;
                    float overlap = occupied.Sum(rect => candidate.Intersection(rect).Area);
                    float score = overlap * 1000 + delta.LengthSquared();
                    if (score >= best) continue;
                    best = score; offset = delta; chosen = candidate;
                }
            float pixelsPerUnit = _camera.UnprojectPosition(entry.Anchor + _camera.GlobalBasis.X).DistanceTo(_camera.UnprojectPosition(entry.Anchor));
            if (pixelsPerUnit > .001f) label.GlobalPosition = entry.Anchor + (_camera.GlobalBasis.X * offset.X - _camera.GlobalBasis.Y * offset.Y) / pixelsPerUnit;
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
            if (leader is null) continue;
            leader.Visible = offset.LengthSquared() > 1;
            if (!leader.Visible) continue;
            var direction = label.GlobalPosition - entry.Anchor;
            ((BoxMesh)leader.Mesh).Size = new(.018f, .018f, direction.Length());
            leader.GlobalPosition = (label.GlobalPosition + entry.Anchor) * .5f;
            leader.GlobalBasis = Basis.LookingAt(direction, Vector3.Up);
        }
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
