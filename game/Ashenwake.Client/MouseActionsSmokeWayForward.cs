using Ashenwake.Core.Combat;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public partial class MouseActionsSmoke
{
    private async Task WayForwardAction()
    {
        var target = Field<IReadOnlyList<WorldInteractionTarget>>(_sandbox, "_worldInteractions").Single(t => t.Id == "journey.next");
        var visual = target.Visual;
        Check("cleared_road_has_visible_way_forward", visual is not null && visual.IsVisibleInTree() &&
            Descendants(_stage).OfType<Node3D>().Any(n => n.Name == "WayForward" && n.IsVisibleInTree()));
        string encounter = Session.Campaign.ActiveEncounterId;
        var rewards = Session.Combat.View.Loot.Select(l => l.Id).ToHashSet();
        Check("way_forward_branch_has_real_uncollected_rewards", rewards.Count > 0);
        await ReachVisibleGroundCheckpoint(p => CorePosition.DistanceSquared(p, target.Position), target.Range, "way_forward");
        Check("way_forward_checkpoint_outside_interaction_range", CorePosition.DistanceSquared(Player, target.Position) > (long)target.Range * target.Range);
        var panel = Field<PanelContainer>(_hud, "_panel");
        int openings = 0;
        void Opened() { if (panel.Visible) openings++; }
        panel.VisibilityChanged += Opened;
        try
        {
            await KeyPress(Key.F); Ticks(2);
            Check("distant_keyboard_interact_cannot_activate_way_forward", openings == 0 && !panel.Visible &&
                Session.Campaign.ActiveEncounterId == encounter && Session.Combat.View.Loot.Select(l => l.Id).ToHashSet().SetEquals(rewards));
            var point = await FindInteractionPoint("journey.next");
            Check("way_forward_hover_identifies_exit_action", _sandbox.HoveredWorldActionId == "journey.next");
            await Capture("mouse-way-forward-hover.png");
            await Click(point);
            Check("way_forward_click_starts_specific_approach", _sandbox.PendingWorldActionId == "journey.next" && _sandbox.ClickMoveDestination is not null);
            await KeyPress(Key.X); Ticks(12);
            Check("cancelled_exit_approach_does_not_open_map_or_travel", NoIntent && openings == 0 && !panel.Visible && Session.Campaign.ActiveEncounterId == encounter && Session.Combat.View.Loot.Select(l => l.Id).ToHashSet().SetEquals(rewards));
            point = await FindInteractionPoint("journey.next"); int commands = _commands.Count;
            await Click(point); await WalkUntilStopped();
            Check("clicked_exit_opens_reward_map_exactly_once", openings == 1 && panel.IsVisibleInTree() &&
                Descendants(_hud).OfType<Button>().Any(b => b.IsVisibleInTree() && b.Text.StartsWith("Continue onward", StringComparison.Ordinal) && b.Text.Contains("uncollected drops", StringComparison.Ordinal)));
            Check("exit_reward_review_keeps_scene_and_all_drops", Session.Campaign.ActiveEncounterId == encounter && Session.Combat.View.Loot.Select(l => l.Id).ToHashSet().SetEquals(rewards));
            Check("exit_approach_never_attacks_or_collects_rewards", !_commands.Skip(commands).Any(c => c.Kind is CombatCommandKind.Cast or CombatCommandKind.Pickup));
            Check("exit_arrival_finishes_inside_projected_range", NoIntent && CorePosition.DistanceSquared(Player, target.Position) <= (long)target.Range * target.Range);
            await Capture("mouse-way-forward-rewards.png");
            await CloseJourney(); Ticks(24);
            Check("completed_exit_intent_does_not_reopen_or_travel", NoIntent && openings == 1 && !panel.Visible && Session.Campaign.ActiveEncounterId == encounter && Session.Combat.View.Loot.Select(l => l.Id).ToHashSet().SetEquals(rewards));
            await KeyPress(Key.P);
            string pausedHash = Session.StateHash;
            await KeyPress(Key.F); Ticks(2);
            Check("paused_keyboard_interact_cannot_open_way_forward", _sandbox.IsPaused && openings == 1 &&
                !panel.Visible && Session.StateHash == pausedHash);
            await KeyPress(Key.P);
            await KeyPress(Key.F);
            Check("nearby_keyboard_interact_opens_same_reward_review_once", openings == 2 && panel.IsVisibleInTree() &&
                Session.Campaign.ActiveEncounterId == encounter && Session.Combat.View.Loot.Select(l => l.Id).ToHashSet().SetEquals(rewards));
            await Capture("keyboard-way-forward-rewards.png");
            await CloseJourney(); Ticks(12);
            Check("keyboard_exit_keeps_rewards_and_does_not_repeat", openings == 2 && !panel.Visible && NoIntent &&
                Session.Campaign.ActiveEncounterId == encounter && Session.Combat.View.Loot.Select(l => l.Id).ToHashSet().SetEquals(rewards));
        }
        finally { panel.VisibilityChanged -= Opened; }
    }
}
