using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class CombatFeedbackSmoke
{
    private async Task MidgameLegendaryPowerFeedback()
    {
        string content = Godot.FileAccess.GetFileAsString("res://combat.json");
        var definitions = CombatContent.Parse(content);
        // Detached combat fixtures exercise presentation; campaign acquisition is verified separately.
        CombatSnapshot Fixture(CombatProgressionBuild build)
        {
            var hub = CombatSession.CreateEncounter(content, 42, "hub"); hub.ApplyProgressionBuild(build);
            var state = CombatSession.CreateEncounter(content, 42, "encounter.ossuary", hub.Capture()).Capture();
            state.Fragments.Clear(); state.Equipment.Clear(); state.Actors[0].Position = new(-4500, 0);
            int index = 0;
            foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy).ToArray())
                state.Actors[state.Actors.IndexOf(enemy)] = enemy with
                { Position = new(-2500 + index++ * 1500, 0), Health = 1000, MaxHealth = 1000, Resistance = 0, Armor = 0, RecoveryUntil = 2500, Hidden = false };
            foreach (var (id, equipped) in new[] { (LegendaryEquipment.Rotwake, build.VirulentWake), (LegendaryEquipment.Mourning, build.RallyingChorus), (LegendaryEquipment.Furnace, build.CinderCycle), (LegendaryEquipment.Oath, build.OathReprisal), (LegendaryEquipment.Widow, build.WidowEcho) })
            {
                if (!equipped) continue;
                var definition = definitions.Items.Single(i => i.Id == id); long instance = state.NextObjectId++;
                state.Inventory.Add(new(instance, id, definition.Name, definition.Slot, "Legendary", definition.Damage, definition.Armor, definition.CriticalBasisPoints));
                state.Equipment[definition.Slot] = instance;
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
            sandbox.SetWorldSubtitle("MIDGAME RELICS · ISOLATED COMBAT FIXTURE");
        }
        void Step(params CombatCommand[] commands)
        {
            var batch = session.Step(commands); events.AddRange(batch); string hash = session.StateHash;
            sandbox.PresentCombatEvents(batch, session); sandbox._Process(0); untouched &= session.StateHash == hash;
        }
        void Wait(int ticks) { for (int tick = 0; tick < ticks; tick++) Step(); }
        bool Triggered(string id) => events.Any(e => e.Kind == "LegendaryTriggered" && e.ContentId == id);
        bool Shows(string text) => sandbox.LegendaryReadinessText.Contains(text, StringComparison.Ordinal);
        CombatCommand Cast(string skill, int target = 0) => new(CombatCommandKind.Cast, SkillId: skill, TargetId: target);

        var all = Fixture(new() { VirulentWake = true, RallyingChorus = true, CinderCycle = true, OathReprisal = true, WidowEcho = true });
        all.Legendary = new() { OathCharge = 60, OathUntil = 240, WidowUntil = 150, VirulentReadyTick = 90, ChorusReadyTick = 90, CinderUntil = 180 };
        Load(all);
        foreach (Vector2I size in new[] { new Vector2I(1280, 800), new Vector2I(1024, 720), new Vector2I(780, 720) })
        {
            GetWindow().Size = GetWindow().ContentScaleSize = size;
            await Capture("midgame-readiness-" + size.X + ".png");
            var label = Descendants(sandbox).OfType<Label>().Single(n => n.Name == "LegendaryReadiness");
            GD.Print($"Midgame readiness layout {size}: viewport={GetViewport().GetVisibleRect()} rect={label.GetGlobalRect()} min={label.GetCombinedMinimumSize()} lines={label.GetLineCount()} text={label.Text}");
            Check("midgame_all_equipped_readiness_fits_" + size.X, GetViewport().GetVisibleRect().Encloses(label.GetGlobalRect()) &&
                label.GetCombinedMinimumSize().X <= GetViewport().GetVisibleRect().Size.X - 32 && label.GetLineCount() <= 2);
        }
        Check("midgame_cooldowns_and_token_use_authoritative_view", Shows("ROTWAKE 3.0s") && Shows("CHOIR 3.0s") && Shows("CINDER · GENERATE · 6.0s"));
        string text = sandbox.LegendaryReadinessText, hash = session.StateHash;
        var reduced = Descendants(sandbox).OfType<CheckButton>().Single(n => n.Name == "SettingsReducedEffects");
        reduced.ButtonPressed = true; sandbox.SetGraphicsQuality("Performance");
        await Capture("midgame-readiness-reduced.png");
        Check("midgame_readiness_survives_pause_and_reduced_effects", sandbox.LegendaryReadinessText == text && session.StateHash == hash && sandbox.ReducedEffects);
        reduced.ButtonPressed = false; sandbox.SetGraphicsQuality("High");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);

        var poison = Fixture(new(Discipline: "Veilwalker") { VirulentWake = true });
        poison.Actors[1].Health = 31; poison.Actors[2].Position = new(-500, 800);
        Load(poison); Check("rotwake_equipped_readiness_appears_before_first_trigger", Shows("ROTWAKE READY"));
        Step(Cast("skill.venom_knife", poison.Actors[1].Id));
        for (int tick = 0; tick < 90 && !Triggered(LegendaryEquipment.RotwakePower); tick++) Step();
        Check("rotwake_poison_kill_reaches_its_distinct_feedback", Triggered(LegendaryEquipment.RotwakePower) &&
            sandbox.LastLegendaryTriggerCue == "legendary_rotwake" && sandbox.LegendaryTriggerText.Contains("VIRULENT WAKE", StringComparison.Ordinal) && Shows("ROTWAKE 3.0s"));
        Check("rotwake_visual_can_originate_at_a_dead_foe", session.View.Actors.Single(a => a.Id == poison.Actors[1].Id).Health == 0 &&
            Descendants(sandbox).OfType<CombatEffects>().Single().Count > 0);
        await Capture("midgame-rotwake-spread.png");
        string poisonCue = sandbox.LegendaryTriggerText; hash = session.StateHash;
        await Capture("midgame-rotwake-paused.png");
        Check("midgame_trigger_caption_pauses_with_core", sandbox.LegendaryTriggerText == poisonCue && session.StateHash == hash);
        Wait(91); Check("rotwake_cooldown_completes_and_caption_expires", Shows("ROTWAKE READY") && sandbox.LegendaryTriggerText.Length == 0);

        var chorus = Fixture(new(Discipline: "Warden") { RallyingChorus = true }); chorus.Momentum = 40; Load(chorus);
        Check("chorus_explains_missing_summons", Shows("CHOIR · NO READY SUMMON") && session.View.Legendary?.ChorusSummons == 0);
        Step(Cast("skill.feral_companion")); Wait(16);
        Check("chorus_readiness_updates_after_actual_summon", Shows("CHOIR READY") && session.View.Legendary?.ChorusSummons > 0);
        Step(Cast("skill.thorn_shot", chorus.Actors[1].Id));
        for (int tick = 0; tick < 60 && !Triggered(LegendaryEquipment.MourningPower); tick++) Step();
        Check("chorus_direct_hit_reaches_distinct_feedback", Triggered(LegendaryEquipment.MourningPower) && sandbox.LastLegendaryTriggerCue == "legendary_chorus" &&
            sandbox.LegendaryTriggerText.Contains("1 summon rallied", StringComparison.Ordinal) && Shows("CHOIR 3.0s"));
        await Capture("midgame-chorus-strike.png");

        foreach (var (discipline, spender, generator) in new[] {
            ("Vanguard", "skill.shield_breaker", "skill.cleave"), ("Veilwalker", "skill.terror", "skill.venom_knife"),
            ("Warden", "skill.barkskin", "skill.thorn_shot"), ("Arcanist", "skill.frost_nova", "skill.vent"),
            ("Gravecaller", "skill.raise_ancestor", "") })
        {
            var state = Fixture(new(Discipline: discipline) { CinderCycle = true }); state.Momentum = discipline == "Arcanist" ? 60 : 40;
            if (discipline == "Gravecaller")
                foreach (var corpse in state.Actors.Skip(1).Take(2)) { corpse.Health = 0; corpse.DeathProcessed = true; corpse.Position = new(-3500, corpse.Id * 100); }
            Load(state); Check("cinder_unprepared_instruction_" + discipline, Shows("CINDER · CAST 20+"));
            Step(Cast(spender, state.Actors[1].Id)); Wait(25);
            string action = discipline == "Arcanist" ? "VENT" : discipline == "Gravecaller" ? "HARVEST" : "GENERATE";
            Check("cinder_ready_instruction_matches_discipline_" + discipline, Shows("CINDER · " + action) && session.View.Legendary?.CinderRemainingTicks > 0);
            await Capture("midgame-cinder-ready-" + discipline + ".png");
            if (discipline == "Gravecaller")
            {
                int corpse = session.View.Actors.First(a => a.Health == 0 && !a.CorpseConsumed).Id;
                Step(new CombatCommand(CombatCommandKind.ConsumeCorpse, TargetId: corpse));
            }
            else
            {
                if (discipline == "Veilwalker")
                {
                    // Terror displaces the target. Close the gap with ordinary movement before using the knife.
                    for (int tick = 0; tick < 80; tick++)
                    {
                        var player = session.View.Actors.Single(a => a.Id == 1);
                        var target = session.View.Actors.Single(a => a.Id == state.Actors[1].Id);
                        int x = target.Position.X - player.Position.X, z = target.Position.Z - player.Position.Z;
                        if ((long)x * x + (long)z * z <= 2200L * 2200) break;
                        Step(new CombatCommand(CombatCommandKind.Move, X: Math.Sign(x), Z: Math.Sign(z)));
                    }
                    Step(new CombatCommand(CombatCommandKind.Stop));
                }
                Step(Cast(generator, state.Actors[1].Id));
            }
            for (int tick = 0; tick < 30 && !Triggered(LegendaryEquipment.FurnacePower); tick++) Step();
            var trigger = events.SingleOrDefault(e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.FurnacePower);
            string expected = "CINDER CYCLE · " + (discipline == "Arcanist" ? "−" : "+") + (trigger?.Amount ?? -1) + " " + session.View.ResourceName;
            GD.Print($"Midgame Cinder {discipline}: trigger={trigger} cue={sandbox.LastLegendaryTriggerCue} text={sandbox.LegendaryTriggerText} readiness={sandbox.LegendaryReadinessText} events={string.Join(",", events.Where(e => e.Kind is "CommandRejected" or "AbilityStarted" or "LegendaryTriggered"))}");
            Check("cinder_trigger_shows_actual_resource_change_" + discipline, trigger is not null && trigger.Amount == 12 && sandbox.LastLegendaryTriggerCue == "legendary_cinder" &&
                sandbox.LegendaryTriggerText == expected && Shows("CINDER · CAST 20+"));
            await Capture("midgame-cinder-trigger-" + discipline + ".png");
            session.ApplyProgressionBuild(new(Discipline: discipline)); sandbox.AdoptSession(session); sandbox._Process(0);
            Check("midgame_unequip_removes_readiness_and_trigger_" + discipline, sandbox.LegendaryReadinessText.Length == 0 && sandbox.LegendaryTriggerText.Length == 0);
        }
        Check("midgame_presentation_never_mutates_combat", untouched);
    }
}
