using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private EndgamePresentation? _endgamePresentation;
    private Control? _endgameRulePanel;

    internal void AttachEndgameCues(EndgamePresentation presentation, Control panel)
    { _endgamePresentation = presentation; _endgameRulePanel = panel; }

    internal void DetachEndgameCues(EndgamePresentation presentation)
    {
        if (!ReferenceEquals(_endgamePresentation, presentation)) return;
        _endgamePresentation = null; _endgameRulePanel = null;
    }

    private string? EndgameCombatLabel(CombatActorView actor)
    {
        if (_view.Endgame is not { } endgame || actor.Health <= 0 || actor.Faction != CombatFaction.Enemy) return null;
        var cue = endgame.BossCue;
        if (cue?.BossId == actor.Id)
        {
            if (cue.Shielded) return cue.PriorityMechanismIds.Length > 0 ? "PROTECTED · USE GOLD MARKER" : "PROTECTED · BREAK GOLD TARGET";
            if (cue.GuardedTicks > 0) return "GUARDED · " + CombatSeconds(cue.GuardedTicks);
            string condition = cue.VulnerableTicks > 0 ? "VULNERABLE " + CombatSeconds(cue.VulnerableTicks) : cue.PermanentlyVulnerable ? "SHIELD BROKEN" : "";
            if (cue.RecoveryTicks > 0) condition += (condition.Length > 0 ? "\n" : "") + "RECOVERING " + CombatSeconds(cue.RecoveryTicks);
            return condition;
        }
        if (cue?.PriorityActorIds.Contains(actor.Id) == true) return "◆ BREAK TO EXPOSE";
        if (endgame.HastedActorIds?.Contains(actor.Id) == true) return "FEVERED · MOVING +25%";
        return EndgameActorLabel(actor.State);
    }

    internal IEnumerable<Rect2> EndgameHudObstacles()
    {
        foreach (var control in new Control?[] { _targetDetail, _campaignObjective, _expeditionObjective, _endgameRulePanel,
            _localMap, _statusStrip, _legendaryReadiness, _legendaryTrigger })
            if (control?.IsVisibleInTree() == true) yield return control.GetGlobalRect().Grow(4);
    }

    private void PresentEndgamePriority(CombatActorView actor)
    {
        string key = "endgame-priority-" + actor.Id; _visibleEffects.Add(key);
        if (!_effects.TryGetValue(key, out var ring))
        {
            ring = new MeshInstance3D
            {
                Name = "EndgamePriority_" + actor.Id,
                Mesh = new TorusMesh { InnerRadius = .78f, OuterRadius = .9f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new("ffe297"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(ring); _effects[key] = ring;
        }
        ring.Position = PositionOf(actor.Position.X, actor.Position.Z) + Vector3.Up * .09f;
    }

    private void LayoutEndgameCombatLabels()
    {
        if (_view.Endgame is not { } endgame || _endgamePresentation is null || !GodotObject.IsInstanceValid(_endgamePresentation)) return;
        _endgamePresentation.LayoutOverlay(_expeditionObjective?.IsVisibleInTree() == true ? _expeditionObjective.GetGlobalRect().End.Y : 192);
        LayoutCombatTarget();
        var viewport = GetViewport().GetVisibleRect();
        var safe = new Rect2(viewport.Position + new Vector2(8, 82), new Vector2(viewport.Size.X - 16, Math.Max(1, viewport.Size.Y - 290)));
        var occupied = EndgameHudObstacles().ToList();
        // Required targets are placed first. Labels never move actor geometry or
        // health bars, and presentation returns to its authored anchor every frame.
        foreach (var pair in _actors.Where(p => p.Value.Label.IsVisibleInTree())
            .OrderBy(p => endgame.BossCue?.PriorityActorIds.Contains(p.Key) == true ? 0 : 1).ThenBy(p => p.Key))
        {
            var actor = pair.Value;
            CombatLabelLayout.Place(_camera, actor.Label, actor.Root.GlobalPosition + Vector3.Up * (actor.Body.Height + .52f), safe, occupied);
        }
        _endgamePresentation.LayoutLabels(_camera, safe, occupied);
        foreach (var hazard in (_view.CampaignHazards ?? []).OrderBy(h => h.RemainingTicks).ThenBy(h => h.Id))
            if (_warningLabelAnchors.TryGetValue(hazard.Id, out var entry))
                CombatLabelLayout.Place(_camera, entry.Label, entry.Anchor, safe, occupied);
    }
}
