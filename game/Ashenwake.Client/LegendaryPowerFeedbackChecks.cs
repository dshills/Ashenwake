using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class CombatFeedbackSmoke
{
    private async Task LegendaryPowerFeedback()
    {
        string content = Godot.FileAccess.GetFileAsString("res://combat.json");
        var definitions = CombatContent.Parse(content);
        // Isolated, explicitly equipped diagnostic fixtures. Reward acquisition is verified separately.
        CombatSession Fixture(CombatProgressionBuild build, bool incomingStrike = false)
        {
            var hub = CombatSession.CreateEncounter(content, 42, "hub"); hub.ApplyProgressionBuild(build);
            var state = CombatSession.CreateEncounter(content, 42, "encounter.ossuary", hub.Capture()).Capture();
            state.Fragments.Clear(); state.Equipment.Clear(); state.Actors[0].Position = new(-4500, 0);
            int index = 0;
            foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy))
            { enemy.Position = new(-2600 + 1600 * index++, 0); enemy.RecoveryUntil = 1500; enemy.Hidden = false; }
            foreach (string id in new[] { build.PyreTrail ? LegendaryEquipment.Pyre : "", build.OathReprisal ? LegendaryEquipment.Oath : "", build.WidowEcho ? LegendaryEquipment.Widow : "" }.Where(id => id.Length > 0))
            {
                var definition = definitions.Items.Single(i => i.Id == id);
                long instance = state.NextObjectId++;
                state.Inventory.Add(new(instance, id, definition.Name, definition.Slot, "Legendary", definition.Damage, definition.Armor, definition.CriticalBasisPoints));
                state.Equipment[definition.Slot] = instance;
            }
            if (incomingStrike)
            {
                state.Actors[0].Barrier = 200; state.Actors[0].InvulnerableUntil = state.Tick;
                var enemy = state.Actors[1]; enemy.Position = new(-3900, 0);
                enemy.Pending = new("enemy.strike", 1, state.Actors[0].Position, state.Tick, state.NextActionId++);
            }
            return CombatSession.Restore(content, state);
        }
        var sandbox = new Sandbox { ContentJsonOverride = content, AutomaticStep = false };
        AddChild(sandbox); sandbox.EnableCampaign(); sandbox.SetPaused(true);
        var session = Fixture(new(Discipline: "Arcanist") { PyreTrail = true, WidowEcho = true });
        sandbox.SetSession(session); sandbox.SetPaused(true);
        sandbox.SetWorldSubtitle("LEGENDARY EQUIPMENT · ISOLATED COMBAT FIXTURE");
        bool untouched = true;
        void Step(params CombatCommand[] commands)
        {
            var events = session.Step(commands); string hash = session.StateHash;
            sandbox.PresentCombatEvents(events, session); untouched &= session.StateHash == hash;
        }
        Step(new CombatCommand(CombatCommandKind.Dodge, Z: 1));
        Check("legendary_pyre_core_areas_have_distinct_ground_seams", session.View.Areas.Count(a => a.ContentId == "effect.pyre_trail") == 3 &&
            Descendants(sandbox).Count(n => n.Name.ToString().StartsWith("PyreTrail_", StringComparison.Ordinal)) == 3);
        Check("legendary_widow_dodge_readiness_uses_authoritative_view", session.View.Legendary?.WidowRemainingTicks > 0 && sandbox.LegendaryReadinessText.Contains("WIDOW READY", StringComparison.Ordinal));
        var readiness = Descendants(sandbox).OfType<Label>().Single(n => n.Name == "LegendaryReadiness");
        Check("legendary_readiness_has_a_dedicated_hud_position", GetViewport().GetVisibleRect().Encloses(readiness.GetGlobalRect()) && readiness.Position.Y < GetViewport().GetVisibleRect().Size.Y - 200);
        Check("legendary_pyre_trigger_reaches_feedback", sandbox.LegendaryTriggerCueCount == 1 && sandbox.LastLegendaryTriggerCue == "legendary_pyre");
        string readyText = sandbox.LegendaryReadinessText; long tick = session.Tick;
        await Capture("legendary-pyre-and-readiness.png");
        Check("legendary_readiness_pauses_with_combat", session.Tick == tick && sandbox.LegendaryReadinessText == readyText);
        for (int i = 0; i < 5; i++) Step();
        Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.fire_lance", TargetId: session.View.Actors[1].Id));
        for (int i = 0; i < 20 && !session.View.Projectiles.Any(p => p.ContentId == "effect.widow_echo"); i++) Step();
        Check("legendary_widow_delayed_projectile_has_silk_wake", session.View.Projectiles.Any(p => p.ContentId == "effect.widow_echo") &&
            Descendants(sandbox).Any(n => n.Name == "SilkWake"));
        Check("legendary_widow_trigger_consumes_visible_readiness", sandbox.LastLegendaryTriggerCue == "legendary_widow" &&
            sandbox.LegendaryTriggerCueCount == 2 && !sandbox.LegendaryReadinessText.Contains("WIDOW READY", StringComparison.Ordinal));
        await Capture("legendary-widow-projectile.png");
        session.ApplyProgressionBuild(new(Discipline: "Arcanist")); sandbox.AdoptSession(session);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check("legendary_unequip_clears_authoritative_visuals", sandbox.LegendaryReadinessText.Length == 0 &&
            !Descendants(sandbox).Any(n => n.Name.ToString().StartsWith("PyreTrail_", StringComparison.Ordinal) || n.Name.ToString().StartsWith("WidowEcho_", StringComparison.Ordinal)));
        session = Fixture(new() { OathReprisal = true }, incomingStrike: true);
        sandbox.SetSession(session); sandbox.SetPaused(true); Step();
        Check("legendary_oath_charge_has_persistent_readiness", session.View.Legendary?.OathCharge > 0 && sandbox.LegendaryReadinessText.Contains("REPRISAL", StringComparison.Ordinal));
        await Capture("legendary-oath-charged.png");
        Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: session.View.Actors[1].Id));
        for (int i = 0; i < 8; i++) Step();
        Check("legendary_oath_trigger_reaches_feedback_and_clears_charge", sandbox.LastLegendaryTriggerCue == "legendary_oath" && sandbox.LegendaryReadinessText.Length == 0);
        Check("legendary_presentation_never_mutates_core", untouched);
        await Capture("legendary-oath-reprisal.png");
    }
}
