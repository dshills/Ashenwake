using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Godot;

namespace Ashenwake.Client;

public partial class MouseActionsSmoke
{
    private async Task CombatReadability()
    {
        var live = Session.Combat;
        string hash = Session.StateHash;
        string content = Field<string>(_director, "_combatJson");
        var enemies = live.View.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.Visible).ToArray();
        Check("readability_checkpoint_has_multiple_living_enemies", enemies.Length >= 2);
        try
        {
            _sandbox.SetHoveredActor(0); PaintReadability();
            Check("readability_visible_health_bars_match_living_enemies", Descendants(_sandbox).OfType<Node3D>()
                .Count(n => n.Name == "ActorHealthBar" && n.IsVisibleInTree()) == enemies.Length);
            Check("readability_bars_share_one_texture", enemies.SelectMany(a => HealthBar(a.Id).GetChildren().OfType<Sprite3D>())
                .Select(s => s.Texture.GetInstanceId()).Distinct().Count() == 1);
            CheckHealthFills(live.View, "initial");
            int selected = Field<int>(_sandbox, "_target");
            CheckFocusCard(live.View.Actors.Single(a => a.Id == selected), "selected");

            bool hovered = false;
            foreach (var actor in enemies.Where(a => a.Id != selected))
            {
                foreach (var point in PickPoints(ActorRoot(actor.Id)))
                {
                    await Hover(point); PaintReadability();
                    if (Field<int>(_sandbox, "_hoveredCombatActor") != actor.Id) continue;
                    CheckFocusCard(actor, "hovered"); hovered = true; break;
                }
                if (hovered) break;
            }
            Check("readability_real_mouse_hover_overrides_selected_detail", hovered);
            int hover = Field<int>(_sandbox, "_hoveredCombatActor");
            var mechanics = Field<HashSet<int>>(_sandbox, "_mechanicLabels");
            Check("readability_ordinary_unfocused_names_are_hidden", enemies.Where(a => a.Id != selected && a.Id != hover && !mechanics.Contains(a.Id))
                .All(a => !ActorLabelForReadability(a.Id).Visible));
            Check("readability_world_labels_omit_repeated_health_numbers", enemies.All(a => !ActorLabelForReadability(a.Id).Text.Contains(a.Health + "/" + a.MaxHealth, StringComparison.Ordinal)));
            var cached = ReadabilityNodeIds();
            for (int i = 0; i < 8; i++) { _sandbox.AdoptSession(live); _sandbox.SetHoveredActor(hover); PaintReadability(); }
            Check("readability_repeated_projection_reuses_actor_and_card_nodes", cached.SetEquals(ReadabilityNodeIds()));
            Check("readability_focus_card_is_noninteractive_and_within_viewport", Descendants(TargetCard()).OfType<Control>().Append(TargetCard())
                .All(c => c.MouseFilter == Control.MouseFilterEnum.Ignore) && GetViewport().GetVisibleRect().Encloses(TargetCard().GetGlobalRect()));
            await Capture("mouse-combat-readable.png");
            await KeyPress(Key.P); PaintReadability();
            Check("readability_pause_hides_focus_and_clears_hover", _sandbox.IsPaused && !TargetCard().Visible && Field<int>(_sandbox, "_hoveredCombatActor") == 0);
            await KeyPress(Key.P); PaintReadability();
            Check("readability_resume_restores_current_selected_detail", !_sandbox.IsPaused && TargetCard().Visible);

            // These are isolated authored combats. Actual commands produce damage, status,
            // death and the protected phase; none of them grants campaign rewards here.
            var branch = CombatSession.CreateEncounter(content, 42, "campaign.bell_saint", live.Capture(), restoreAtAnchor: true);
            var recorder = new CombatRecorder(branch);
            bool damage = false, condition = false, protection = false, death = false;
            for (int tick = 0; tick < 1600 && !(damage && condition && protection && death); tick++)
            {
                recorder.Step(branch, CampaignCombatSmoke.Commands(branch.View, branch.Room));
                var view = branch.View;
                _sandbox.AdoptSession(branch);
                var damaged = view.Actors.FirstOrDefault(a => a.Faction == CombatFaction.Enemy && a.Visible && a.Health > 0 && a.Health < a.MaxHealth);
                if (!damage && damaged is not null)
                {
                    _sandbox.SetHoveredActor(damaged.Id); PaintReadability(); CheckFocusCard(damaged, "damaged");
                    CheckHealthFills(view, "damaged"); damage = true;
                }
                var status = view.Actors.FirstOrDefault(a => a.Faction == CombatFaction.Enemy && a.Visible && a.Health > 0 && a.Statuses.Count > 0);
                if (!condition && status is not null)
                {
                    _sandbox.SetHoveredActor(status.Id); PaintReadability(); await Frames();
                    CheckCondition("readability_target_conditions_include_actual_core_status", status.Statuses.Any(s => TargetConditions().Text.Contains(s.Id.Replace('_', ' ').Replace('.', ' ').ToUpperInvariant(), StringComparison.Ordinal)));
                    CheckCondition("readability_status_line_has_visible_height", TargetConditions().IsVisibleInTree() && TargetConditions().Size.Y >= 10);
                    condition = true;
                }
                var boss = view.Actors.FirstOrDefault(a => a.DefinitionId == "boss.bell_saint" && a.Health > 0);
                if (!protection && view.BossPhase == 2 && boss is not null && view.Actors.Any(a => a.DefinitionId == "enemy.ritual_anchor" && a.Health > 0))
                {
                    _sandbox.SetHoveredActor(boss.Id); PaintReadability(); await Frames();
                    CheckCondition("readability_protected_boss_card_matches_world_mechanic", ActorLabelForReadability(boss.Id).Text.Contains("PROTECTED", StringComparison.Ordinal) &&
                        TargetConditions().Text.Contains("PROTECTED", StringComparison.Ordinal));
                    CheckCondition("readability_protected_boss_condition_is_visibly_laid_out", TargetConditions().IsVisibleInTree() && TargetConditions().Size.Y >= 10);
                    await Capture("mouse-protected-boss-detail.png"); protection = true;
                }
                if (!death && view.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health <= 0))
                {
                    PaintReadability();
                    Check("readability_dead_actors_have_no_health_bars_or_labels", view.Actors.Where(a => a.Health <= 0)
                        .All(a => !HealthBar(a.Id).IsVisibleInTree() && !ActorLabelForReadability(a.Id).IsVisibleInTree())); death = true;
                }
            }
            Check("readability_observes_actual_damage_status_protection_and_death", damage && condition && protection && death);
            Check("readability_combat_branch_replays_exactly", CombatReplayRunner.Run(content, recorder.Capture()).Success);
            if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "mouse-readability-replay.json"), JsonData.Write(recorder.Capture()));

            var shadows = CombatSession.CreateEncounter(content, 42, "campaign.repeating_rooms");
            _sandbox.AdoptSession(shadows); PaintReadability();
            var hidden = shadows.View.Actors.Where(a => a.Health > 0 && !a.Visible).ToArray();
            Check("readability_authored_hidden_actors_are_present", hidden.Length > 0);
            foreach (var actor in hidden) { _sandbox.SetHoveredActor(actor.Id); PaintReadability(); }
            Check("readability_hidden_actors_have_no_bar_label_or_hover", hidden.All(a => !HealthBar(a.Id).IsVisibleInTree() && !ActorLabelForReadability(a.Id).IsVisibleInTree()) && Field<int>(_sandbox, "_hoveredCombatActor") == 0);
        }
        finally
        {
            _sandbox.SetPaused(false); Refresh(); _sandbox.SetHoveredActor(0); PaintReadability(); await Frames();
        }
        Check("readability_branches_leave_live_campaign_unchanged", Session.StateHash == hash && ReferenceEquals(_sandbox.Session, live));
    }

    private void PaintReadability() => Invoke(_sandbox, "AnimatePresentation", 1d / 60, 1d, Field<int>(_sandbox, "_target"));
    private Node3D ActorRoot(int id) => _sandbox.GetNode<Node3D>("Actor" + id);
    private Node3D HealthBar(int id) => ActorRoot(id).GetNode<Node3D>("ActorHealthBar");
    private Label3D ActorLabelForReadability(int id) => ActorRoot(id).GetChildren().OfType<Label3D>().Single();
    private PanelContainer TargetCard() => Descendants(_sandbox).OfType<PanelContainer>().Single(n => n.Name == "CombatTargetDetail");
    private Label TargetConditions() => Descendants(TargetCard()).OfType<Label>().Single(n => n.Name == "TargetConditions");
    private HashSet<ulong> ReadabilityNodeIds() => Descendants(_sandbox).OfType<Node3D>().Where(n => n.Name.ToString().StartsWith("Actor", StringComparison.Ordinal))
        .SelectMany(n => Descendants(n).Prepend(n)).Concat(Descendants(TargetCard()).Prepend(TargetCard())).Select(n => n.GetInstanceId()).ToHashSet();
    private void CheckHealthFills(CombatView view, string suffix) => Check("readability_health_fill_matches_core_" + suffix,
        view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.Visible).All(a =>
        {
            var bar = HealthBar(a.Id); var fill = bar.GetNode<Sprite3D>("HealthFill"); var track = bar.GetNode<Sprite3D>("HealthTrack");
            return bar.IsVisibleInTree() && Math.Abs(fill.RegionRect.Size.X / track.RegionRect.Size.X - (float)a.Health / a.MaxHealth) < .0001f &&
                Math.Abs(fill.Offset.X - (fill.RegionRect.Size.X - track.RegionRect.Size.X) * .5f) < .0001f;
        }));
    private void CheckFocusCard(CombatActorView actor, string suffix)
    {
        var card = TargetCard(); var labels = Descendants(card).OfType<Label>().ToArray();
        Check("readability_target_card_matches_core_" + suffix, card.IsVisibleInTree() && Field<int>(_sandbox, "_targetDetailActor") == actor.Id &&
            labels.Single(n => n.Name == "TargetName").Text == ActorLabelForReadability(actor.Id).Text.Split('\n')[0] &&
            labels.Single(n => n.Name == "TargetHealth").Text == $"HEALTH  {actor.Health} / {actor.MaxHealth}");
    }
    private void CheckCondition(string name, bool passed)
    {
        _checks[name] = passed;
        if (passed) return;
        var label = TargetConditions();
        throw new InvalidDataException($"Mouse actions check failed: {name}; text={JsonData.Write(label.Text)}; size={label.Size}; visible={label.IsVisibleInTree()}; visibleRatio={label.VisibleRatio}; lines={label.GetLineCount()}; maxLines={label.MaxLinesVisible}; position={label.GlobalPosition}.");
    }
}
