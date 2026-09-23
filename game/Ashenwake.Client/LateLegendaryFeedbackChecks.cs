using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class CombatFeedbackSmoke
{
    private async Task LateLegendaryPowerFeedback()
    {
        string content = Godot.FileAccess.GetFileAsString("res://combat.json");
        var definitions = CombatContent.Parse(content);
        // Detached, labelled combat fixtures verify presentation only. Reward acquisition is a separate diagnostic.
        CombatSnapshot Fixture(CombatProgressionBuild build)
        {
            var hub = CombatSession.CreateEncounter(content, 42, "hub"); hub.ApplyProgressionBuild(build);
            var state = CombatSession.CreateEncounter(content, 42, "encounter.ossuary", hub.Capture()).Capture();
            state.Fragments.Clear(); state.Equipment.Clear(); state.Actors[0].Position = new(-4500, 0); state.Momentum = 100;
            int index = 0;
            foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy).ToArray())
                state.Actors[state.Actors.IndexOf(enemy)] = enemy with
                { Position = new(-2500 + index++ * 1500, 0), Health = 3000, MaxHealth = 3000, Resistance = 0, Armor = 0, RecoveryUntil = 2500, Hidden = false };
            foreach (var (id, equipped) in new[] { (LegendaryEquipment.Crown, build.UnspokenVerdict), (LegendaryEquipment.Witness, build.WitnessVow), (LegendaryEquipment.Hour, build.BorrowedHour),
                (LegendaryEquipment.Rotwake, build.VirulentWake), (LegendaryEquipment.Mourning, build.RallyingChorus), (LegendaryEquipment.Furnace, build.CinderCycle), (LegendaryEquipment.Oath, build.OathReprisal), (LegendaryEquipment.Widow, build.WidowEcho) })
            {
                if (!equipped) continue;
                var item = definitions.Items.Single(i => i.Id == id); long instance = state.NextObjectId++;
                state.Inventory.Add(new(instance, id, item.Name, item.Slot, "Legendary", item.Damage, item.Armor, item.CriticalBasisPoints));
                state.Equipment[item.Slot] = instance;
            }
            return state;
        }
        var sandbox = new Sandbox { ContentJsonOverride = content, AutomaticStep = false };
        AddChild(sandbox); sandbox.EnableCampaign(); sandbox.SetPaused(true);
        CombatSession session = null!; bool untouched = true;
        var events = new List<CombatEvent>();
        void Load(CombatSnapshot state)
        {
            session = CombatSession.Restore(content, state); events.Clear(); sandbox.SetSession(session); sandbox.SetPaused(true);
            sandbox.SetWorldSubtitle("LATE RELICS · ISOLATED COMBAT FIXTURE"); sandbox._Process(0);
        }
        void Step(params CombatCommand[] commands)
        {
            var batch = session.Step(commands); events.AddRange(batch); string hash = session.StateHash;
            sandbox.PresentCombatEvents(batch, session); sandbox._Process(0); untouched &= hash == session.StateHash;
        }
        void Wait(int ticks) { for (int i = 0; i < ticks; i++) Step(); }
        bool Shows(string text) => sandbox.LegendaryReadinessText.Contains(text, StringComparison.Ordinal);
        bool Triggered(string id) => events.Any(e => e.Kind == "LegendaryTriggered" && e.ContentId == id);
        CombatCommand Cast(string id, int target = 0) => new(CombatCommandKind.Cast, SkillId: id, TargetId: target);

        var all = Fixture(new() { UnspokenVerdict = true, WitnessVow = true, BorrowedHour = true, VirulentWake = true, RallyingChorus = true, CinderCycle = true, OathReprisal = true, WidowEcho = true });
        all.Legendary = new()
        {
            OathCharge = 60,
            OathUntil = 240,
            WidowUntil = 150,
            VirulentReadyTick = 90,
            ChorusReadyTick = 90,
            CinderUntil = 180,
            VerdictReadyTick = 90,
            WitnessStacks = 3,
            WitnessTargetId = all.Actors[1].Id,
            WitnessUntil = 120,
            HourCharges = 3,
            HourUntil = 240
        };
        Load(all);
        foreach (var size in new[] { new Vector2I(1280, 800), new Vector2I(780, 720) })
        {
            GetWindow().Size = GetWindow().ContentScaleSize = size;
            await Capture("late-all-readiness-" + size.X + ".png");
            var label = Descendants(sandbox).OfType<Label>().Single(n => n.Name == "LegendaryReadiness");
            var status = Descendants(sandbox).OfType<Control>().Single(n => n.Name == "HudStatuses");
            Check("late_all_readiness_fits_" + size.X, GetViewport().GetVisibleRect().Encloses(label.GetGlobalRect()) &&
                label.GetCombinedMinimumSize().X <= GetViewport().GetVisibleRect().Size.X - 32 && label.GetLineCount() == 3 && !label.GetGlobalRect().Intersects(status.GetGlobalRect()));
        }
        Check("late_readiness_uses_authoritative_state", Shows("VERDICT 3.0s") && Shows("WITNESS 3/4 · 4.0s") && Shows("HOUR 3 · 8.0s"));
        string before = sandbox.LegendaryReadinessText, original = session.StateHash;
        var reduced = Descendants(sandbox).OfType<CheckButton>().Single(n => n.Name == "SettingsReducedEffects");
        reduced.ButtonPressed = true; sandbox.SetGraphicsQuality("Performance");
        await Capture("late-all-readiness-reduced.png");
        Check("late_readiness_survives_pause_reduced_effects", sandbox.LegendaryReadinessText == before && session.StateHash == original && sandbox.ReducedEffects);
        // An actual ultimate lights the trigger above the full readiness stack at minimum size.
        Step(Cast("skill.cataclysm"));
        await Capture("late-all-trigger-reduced.png");
        var readiness = Descendants(sandbox).OfType<Label>().Single(n => n.Name == "LegendaryReadiness");
        var trigger = Descendants(sandbox).OfType<Label>().Single(n => n.Name == "LegendaryTrigger");
        Check("late_trigger_is_visible_without_effects_and_clear_of_readiness", trigger.IsVisibleInTree() && sandbox.LegendaryTriggerText.Contains("BORROWED HOUR", StringComparison.Ordinal) &&
            GetViewport().GetVisibleRect().Encloses(trigger.GetGlobalRect()) && !trigger.GetGlobalRect().Intersects(readiness.GetGlobalRect()) &&
            trigger.GetCombinedMinimumSize().X <= GetViewport().GetVisibleRect().Size.X - 32);
        reduced.ButtonPressed = false; sandbox.SetGraphicsQuality("High"); GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);

        var crown = Fixture(new() { UnspokenVerdict = true });
        crown.Actors[1].Pending = new("enemy.strike", 1, crown.Actors[0].Position, 60, crown.NextActionId++);
        Load(crown); Check("verdict_ready_before_interrupt", Shows("VERDICT READY"));
        Step(Cast("skill.shield_breaker", crown.Actors[1].Id));
        for (int tick = 0; tick < 30 && !Triggered(LegendaryEquipment.CrownPower); tick++) Step();
        Check("actual_interrupt_reaches_verdict_feedback", Triggered(LegendaryEquipment.CrownPower) && sandbox.LastLegendaryTriggerCue == "legendary_verdict" &&
            sandbox.LegendaryTriggerText == "UNSPOKEN VERDICT · +40 barrier" && session.View.Barrier == 40 && Shows("VERDICT 3.0s"));
        await Capture("late-verdict-interrupt.png");
        Wait(91); Check("verdict_cooldown_and_caption_expire", Shows("VERDICT READY") && sandbox.LegendaryTriggerText.Length == 0);

        var witness = Fixture(new() { WitnessVow = true }); Load(witness);
        for (int hit = 1; hit <= 4; hit++)
        {
            Step(Cast("skill.cleave", witness.Actors[1].Id)); Wait(5);
            if (hit < 4) Check("witness_actual_action_progress_" + hit, session.View.Legendary?.WitnessStacks == hit && Shows("WITNESS " + hit + "/4"));
            if (hit == 3) await Capture("late-witness-three-strikes.png");
            if (hit < 4) Wait(10);
        }
        Check("four_direct_actions_reach_witness_burst_feedback", Triggered(LegendaryEquipment.WitnessPower) && sandbox.LastLegendaryTriggerCue == "legendary_witness" &&
            sandbox.LegendaryTriggerText == "WITNESS VOW · 24 Void burst" && Shows("WITNESS 0/4"));
        await Capture("late-witness-burst.png");

        var hour = Fixture(new() { BorrowedHour = true }); Load(hour);
        Step(Cast("skill.cataclysm"));
        Check("actual_ultimate_prepares_hour_feedback", session.View.Legendary?.HourCharges == 3 && sandbox.LastLegendaryTriggerCue == "legendary_hour" &&
            Shows("HOUR 3") && sandbox.LegendaryTriggerText.Contains("3 half-cooldown casts", StringComparison.Ordinal));
        await Capture("late-hour-ultimate.png"); Wait(30);
        foreach (int remaining in new[] { 2, 1, 0 })
        {
            Step(Cast("skill.cleave", hour.Actors[1].Id));
            Check("hour_actual_cast_feedback_" + remaining, session.View.Legendary?.HourCharges == remaining &&
                sandbox.LegendaryTriggerText == $"BORROWED HOUR · cooldown halved · {remaining} left");
            Wait(10);
        }
        Check("hour_spent_charges_explain_rearm", Shows("HOUR · ULTIMATE"));
        await Capture("late-hour-spent.png");
        session.ApplyProgressionBuild(new()); sandbox.AdoptSession(session); sandbox._Process(0);
        Check("late_unequip_removes_readiness_and_trigger", sandbox.LegendaryReadinessText.Length == 0 && sandbox.LegendaryTriggerText.Length == 0);
        Check("late_presentation_never_mutates_combat", untouched);
    }
}
