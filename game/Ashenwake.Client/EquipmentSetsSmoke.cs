using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Godot;
using Position = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Earns six campaign drops, then uses shipping collection, comparison and equipment controls.</summary>
public partial class EquipmentSetsSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _lastInput = "";
    private bool _writeReport;
    private int _commands, _clicks, _drags;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private LegendaryCollectionPanel Journal => Field<LegendaryCollectionPanel>(_director, "_collection");
    private LegendaryCollectionDisplay Collection => Field<LegendaryCollectionDisplay>(Journal, "_view");
    private ProductionHud Character => Field<ProductionHud>(_director, "_character");
    private ProgressionSnapshot Progression => Session.Production.Capture().Progression;
    private ProgressionDefinition Definition => Field<ProgressionContent>(_director, "_progression").Capture();
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs(); _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--equipment-sets-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Equipment set smoke requires --equipment-sets-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            await Frames(); CloseMenus(); Call(_sandbox, "ResumePlaying");
            AppearanceVisualChecks.CheckEquipmentSets((passed, name) => Check(name, passed));
            foreach (string id in EquipmentSets.Catalog.SelectMany(set => set.PieceIds))
                Check(id + "_authored_equipment_text", EquipmentNames.Message("equipment." + id[5..]).Length > 0 && EquipmentDetails.Lore(id).Length > 0 && EquipmentDetails.Source(id).Length > 0);
            await InspectMissing(); await EarnEquipment();
            foreach (var set in EquipmentSets.Catalog) await EquipPair(set);
            Check("three_complete_equipped_sets", EquipmentSets.Catalog.All(set => EquipmentSets.CountEquipped(set.Id, Progression.Character) == 2));
            string readiness = Sandbox.EquipmentSetReadiness(Session.Combat.View.EquipmentSets);
            Check("all_three_hud_triggers_explained", readiness.Contains("VIGIL · ABSORB DAMAGE", StringComparison.Ordinal) && readiness.Contains("BRIAR · POISON KILL", StringComparison.Ordinal) && readiness.Contains("ASHRUNNER · EVADE A HIT", StringComparison.Ordinal));
            RoundTrip("complete_sets_save");
            await InspectComplete(); await CheckCombatPresentationFixture();
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); await Finish(false, ex.Message); }
    }
    private async Task InspectMissing()
    {
        string hash = Session.StateHash; OpenJournal(); await Frames(); await Click("CollectionFilterSets");
        Check("sets_filter_exact_six_missing", Journal.Filter == "Sets" && Collection.Cards.Count(c => EquipmentSets.IsItem(c.Entry.ItemId)) == 6 &&
            Collection.Cards.Where(c => EquipmentSets.IsItem(c.Entry.ItemId)).All(c => !c.Collected && c.Owned == 0));
        Check("fresh_sets_are_not_equipped", Collection.EquippedSets is not null && Collection.EquippedSets.Values.All(v => v == 0));
        foreach (var set in EquipmentSets.Catalog)
        {
            await Click("CollectionItem_" + set.PieceIds[0][5..]);
            Check(set.Id + "_inactive_description", JournalText().Contains(set.Name + " · 0/2 equipped", StringComparison.Ordinal) && JournalText().Contains("(2) " + set.Bonus, StringComparison.Ordinal));
            await Click("CollectionSetPiece_" + set.PieceIds[1][5..]); Check(set.Id + "_partner_navigation", Journal.SelectedItem == set.PieceIds[1]);
        }
        await Click("CollectionTrack"); Check("missing_piece_tracks_without_grant", Collection.TrackedItem == EquipmentSets.AshBoots && Session.StateHash == hash);
        await Capture("missing-sets-wide.png"); await Compact("missing-sets");
        CloseMenus();
    }
    private async Task EarnEquipment()
    {
        bool OwnAll() => EquipmentSets.Catalog.SelectMany(s => s.PieceIds).All(id => Progression.Character.Items.Any(i => i.DefinitionId == id));
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !(OwnAll() && Session.InHub); i++)
        {
            Step(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), false);
            if (i % 150 == 0) await Frames(1);
        }
        Refresh(); Call(_director, "ClearDeathRecap"); CloseMenus();
        Check("six_pieces_earned_in_campaign", OwnAll() && Session.InHub);
        Check("earned_act_three_clear", Session.Campaign.Capture().Campaign.CompletedEncounters.Contains("campaign.furnace_spindle"));
        var torren = Session.Interactions.Single(i => i.ActionId == "service.torren"); Walk(torren.Position);
        foreach (var set in EquipmentSets.Catalog)
            foreach (string id in set.PieceIds)
            {
                var slot = Definition.Items.Single(i => i.Id == id).Slots[0];
                if (Progression.Character.Equipment.ContainsKey(slot)) Step(new(EndgameRuntimeAction.Production, Production: new(ProductionAction.Unequip, Slot: slot)));
            }
        OpenJournal(); await Frames(); Journal.SetFilter("Sets");
        Check("owned_discovered_separate_from_equipped", Collection.Cards.Where(c => EquipmentSets.IsItem(c.Entry.ItemId)).All(c => c.Collected && c.Owned == 1) && Collection.EquippedSets!.Values.All(v => v == 0));
        RoundTrip("earned_six_save"); OpenJournal(); await Frames();
        Check("six_collection_discoveries_survive_load", Collection.Cards.Where(c => EquipmentSets.IsItem(c.Entry.ItemId)).All(c => c.Collected && c.Owned == 1));
        await Capture("earned-sets-not-equipped.png"); CloseMenus();
    }
    private async Task EquipPair(EquipmentSetDefinition set)
    {
        CloseMenus(); Character.ToggleInventory(); await Frames();
        var first = Owned(set.PieceIds[0]); var second = Owned(set.PieceIds[1]);
        var firstSlot = Definition.Items.Single(i => i.Id == first.DefinitionId).Slots[0];
        var secondSlot = Definition.Items.Single(i => i.Id == second.DefinitionId).Slots[0];
        await Drag("GearInventoryItem" + first.Id, "GearEquipment" + firstSlot);
        Check(set.Id + "_one_piece_inactive", EquipmentSets.CountEquipped(set.Id, Progression.Character) == 1 && !EquipmentSets.Active(set.Id, Progression.Character) && !CombatSetActive(set.Id));
        await Compare(second.Id, set, "1/2 → 2/2", "completes");
        await Drag("GearInventoryItem" + second.Id, "GearEquipment" + secondSlot);
        Check(set.Id + "_two_distinct_pieces_activate", EquipmentSets.CountEquipped(set.Id, Progression.Character) == 2 && EquipmentSets.Active(set.Id, Progression.Character) && CombatSetActive(set.Id));
        Check(set.Id + "_inspection_explains_bonus", EquipmentDetails.Set(first.DefinitionId, Progression.Character).Contains(set.Name + " · 2/2 equipped", StringComparison.Ordinal));
        var replacement = Progression.Character.Items.First(i => i.DefinitionId == "item.starter_" + firstSlot.ToString().ToLowerInvariant());
        await Compare(replacement.Id, set, "2/2 → 1/2", "breaks");
        await Drag("GearInventoryItem" + replacement.Id, "GearEquipment" + firstSlot);
        Check(set.Id + "_replacement_disables_set", !EquipmentSets.Active(set.Id, Progression.Character) && !CombatSetActive(set.Id));
        await Drag("GearInventoryItem" + first.Id, "GearEquipment" + firstSlot);
        Check(set.Id + "_reequip_restores_pair", EquipmentSets.Active(set.Id, Progression.Character) && CombatSetActive(set.Id));
        CloseMenus(); OpenJournal(); Journal.SetFilter("Sets"); Journal.SelectItem(first.DefinitionId); await Frames();
        Check(set.Id + "_collection_equipped_count", Collection.EquippedSets!.GetValueOrDefault(set.Id) == 2 && JournalText().Contains(set.Name + " · 2/2 equipped", StringComparison.Ordinal));
        CheckLayout(set.Id + "_wide"); await Capture(set.Id + "-wide.png"); await Compact(set.Id); CloseMenus();
    }
    private async Task Compare(long id, EquipmentSetDefinition set, string transition, string suffix)
    {
        string hash = Session.StateHash; var card = Find<Control>("GearInventoryItem" + id); await Reveal(card);
        var comparison = Find<GearComparison>("GearComparison");
        await HoverComparison(card, comparison, id);
        Check(set.Id + "_" + suffix + "_comparison", comparison.IsVisibleInTree() && comparison.ComparisonText.Contains(set.Name + " · " + transition, StringComparison.Ordinal) && comparison.ComparisonText.Contains("(2) " + set.Bonus, StringComparison.Ordinal));
        Check(set.Id + "_" + suffix + "_comparison_read_only", Session.StateHash == hash);
        Check(set.Id + "_" + suffix + "_comparison_fits", GetViewport().GetVisibleRect().Encloses(comparison.GetGlobalRect()));
        await Capture(set.Id + "-" + suffix + "-comparison.png");
        GetViewport().PushInput(new InputEventMouseMotion { Position = new(8, 8) }, true); await Frames();
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); await Reveal(card);
        await HoverComparison(card, comparison, id);
        Check(set.Id + "_" + suffix + "_compact_comparison_fits", comparison.IsVisibleInTree() && GetViewport().GetVisibleRect().Encloses(comparison.GetGlobalRect()));
        Check(set.Id + "_" + suffix + "_compact_comparison_still_describes_transition", comparison.ComparisonText.Contains(set.Name + " · " + transition, StringComparison.Ordinal) && Session.StateHash == hash);
        await Capture(set.Id + "-" + suffix + "-comparison-compact.png");
        GetViewport().PushInput(new InputEventMouseMotion { Position = new(8, 8) }, true); await Frames();
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(8);
    }
    private async Task HoverComparison(Control card, GearComparison comparison, long id)
    {
        if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
        // Native focus notifications and post-drop grid layout may finish a frame after the release.
        // Retry bounded real pointer enter/exit inputs against the current rectangle, never call tooltip internals.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            await Reveal(card);
            GetViewport().PushInput(new InputEventMouseMotion { Position = new(8, 8) }, true); await Frames(2);
            GetViewport().PushInput(new InputEventMouseMotion { Position = card.GetGlobalRect().GetCenter() }, true); await Frames(3);
            if (comparison.IsVisibleInTree() && comparison.ComparisonKey.StartsWith(id + "/", StringComparison.Ordinal)) return;
        }
        _lastInput = "Hover " + id + ": visible=" + comparison.IsVisibleInTree() + ", key=" + comparison.ComparisonKey;
    }
    private async Task InspectComplete()
    {
        OpenJournal(); Journal.SetFilter("Sets"); Journal.SelectItem(EquipmentSets.VigilHead); await Frames();
        Check("all_three_collection_set_bonuses_active", Collection.EquippedSets!.Count == 3 && Collection.EquippedSets.Values.All(v => v == 2));
        var preview = Descendants(Journal).OfType<CharacterPreview>().Single();
        foreach (string discipline in new[] { "Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden" })
        {
            preview.SetAppearance(_sandbox.CurrentAppearance with { Discipline = discipline });
            preview.SetCaption("Cosmetic set preview · " + discipline); await Frames(); await Capture("all-sets-" + discipline + ".png");
        }
        CloseMenus(); Character.ToggleInventory(); await Frames(); await Capture("all-sets-equipped.png");
    }
    private async Task CheckCombatPresentationFixture()
    {
        // These targeted hit/status arrangements exercise rendering, not the earned reward proof above.
        string earnedHash = Session.StateHash, style = Field<string>(_sandbox, "_environmentStyle");
        int earnedFrames = Session.CaptureReplay().Frames.Length;
        var caption = new Label { Name = "EquipmentSetFixtureCaption", Text = "SET FEEDBACK · DETACHED PRESENTATION FIXTURE", Position = new(340, 20), MouseFilter = Control.MouseFilterEnum.Ignore };
        caption.AddThemeFontSizeOverride("font_size", 14); _sandbox.AddOverlay(caption);
        var reduced = Find<CheckButton>("SettingsReducedEffects"); bool originalReduced = reduced.ButtonPressed;
        CloseMenus(); _sandbox.SetModalPaused("set-presentation-fixture", true);
        try
        {
            string combatJson = Field<string>(_director, "_combatJson");
            CombatSnapshot Arrange()
            {
                var state = CombatSession.CreateEncounter(combatJson, 42, "encounter.ossuary", Session.Combat.Capture(), restoreAtAnchor: true).Capture();
                state.Fragments.Clear(); state.Momentum = 100; state.Actors[0].Position = new(-4500, 0);
                int index = 0;
                foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy).ToArray())
                    state.Actors[state.Actors.IndexOf(enemy)] = enemy with
                    { Position = new(-2500 + index++ * 800, 0), Health = 10000, MaxHealth = 10000, Armor = 0, Resistance = 0, RecoveryUntil = state.Tick + 500, Hidden = false };
                return state;
            }
            void Incoming(CombatSnapshot state)
            {
                int enemy = state.Actors.First(a => a.Faction == CombatFaction.Enemy).Id;
                state.Areas.Add(new(state.NextObjectId++, enemy, enemy, state.Actors[0].Position, 5000, "enemy.detonate", 30,
                    DamageFamily.Fire, state.Tick, state.Tick + 1, state.NextActionId++, 0));
            }
            async Task Show(CombatSession fixture)
            {
                _sandbox.AdoptSession(fixture); _sandbox.SetEnvironmentStyle("crypt");
                // Camera settling while explicitly paused cannot advance either combat session.
                _sandbox._Process(.5); await Frames();
            }
            CombatEvent[] Tick(CombatSession fixture, params CombatCommand[] commands)
            {
                var events = fixture.Step(commands).ToArray(); _sandbox.PresentCombatEvents(events, fixture); _sandbox._Process(0); return events;
            }
            CombatEvent[] Attack(CombatSession fixture)
            {
                var result = new List<CombatEvent>(Tick(fixture, new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.shield_breaker", TargetId: fixture.View.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0).Id)));
                for (int i = 0; i < 12; i++) result.AddRange(Tick(fixture));
                return result.ToArray();
            }
            var vigilState = Arrange(); vigilState.Actors[0].Barrier = 50; Incoming(vigilState);
            var vigil = CombatSession.Restore(combatJson, vigilState); await Show(vigil);
            var readied = Tick(vigil); await Frames();
            Check("fixture_vigil_actual_readiness_event", readied.Any(e => e.Kind == "EquipmentSetReadied" && e.ContentId == EquipmentSets.LastVigil));
            Check("fixture_vigil_ready_hud", _sandbox.LegendaryReadinessText.Contains("COUNTER READY", StringComparison.Ordinal) && Find<Label>("LegendaryTrigger").Text.Contains("spectral counter readied", StringComparison.Ordinal));
            await Capture("fixture-vigil-ready.png");
            var strike = Attack(vigil); await Frames();
            Check("fixture_vigil_actual_counter_event", strike.Any(e => e.Kind == "EquipmentSetTriggered" && e.ContentId == EquipmentSets.LastVigil));
            Check("fixture_vigil_counter_hud", Find<Label>("LegendaryTrigger").IsVisibleInTree() && Find<Label>("LegendaryTrigger").Text == "LAST VIGIL · spectral counter");
            await Capture("fixture-vigil-counter.png");

            var runnerState = Arrange(); Incoming(runnerState); var runner = CombatSession.Restore(combatJson, runnerState); await Show(runner);
            var evaded = Tick(runner, new CombatCommand(CombatCommandKind.Dodge, Z: 1)); await Frames();
            Check("fixture_ashrunner_actual_evade_readiness", evaded.Any(e => e.Kind == "EquipmentSetReadied" && e.ContentId == EquipmentSets.Ashrunner) && _sandbox.LegendaryReadinessText.Contains("TRAIL READY", StringComparison.Ordinal));
            await Capture("fixture-ashrunner-ready.png");
            runnerState = runner.Capture(); runnerState.Actors[0].Position = new(-4500, 0); runner = CombatSession.Restore(combatJson, runnerState); _sandbox.AdoptSession(runner);
            for (int i = 0; i < 6; i++) Tick(runner);
            var trail = Attack(runner); await Frames();
            var trailAreas = runner.View.Areas.Where(a => a.ContentId == "effect.set_ashrunner_trail").ToArray();
            Check("fixture_ashrunner_actual_trigger_three_patches", trail.Any(e => e.Kind == "EquipmentSetTriggered" && e.ContentId == EquipmentSets.Ashrunner && e.Amount == 3) && trailAreas.Length == 3);
            Check("fixture_ashrunner_orange_geometry", trailAreas.All(a => Descendants(_sandbox).OfType<MeshInstance3D>().Any(n => n.Name == "PyreTrail_" + a.Id && n.GetNodeOrNull<MeshInstance3D>("FireSeam") is not null)));
            Check("fixture_ashrunner_trigger_hud", Find<Label>("LegendaryTrigger").Text.Contains("3 ember patches", StringComparison.Ordinal));
            await Capture("fixture-ashrunner-trail.png");
            string runnerHash = runner.StateHash; reduced.ButtonPressed = true; await Frames();
            Check("fixture_reduced_effects_preserve_trails_and_state", runner.StateHash == runnerHash && trailAreas.All(a => Descendants(_sandbox).OfType<MeshInstance3D>().Any(n => n.Name == "PyreTrail_" + a.Id)));
            await Capture("fixture-ashrunner-reduced.png"); reduced.ButtonPressed = originalReduced;
            for (int i = 0; i < 65; i++) Tick(runner); await Frames();
            Check("fixture_ashrunner_expiry_removes_geometry_and_trigger", !runner.View.Areas.Any(a => a.ContentId == "effect.set_ashrunner_trail") &&
                !Descendants(_sandbox).OfType<MeshInstance3D>().Any(n => n.Name.ToString().StartsWith("PyreTrail_", StringComparison.Ordinal)) && !Find<Label>("LegendaryTrigger").Visible);

            var briarState = Arrange(); var target = briarState.Actors.First(a => a.Faction == CombatFaction.Enemy); target.Health = 1;
            target.Statuses.Add(new() { Id = "Poisoned", SourceId = 1, OwnerId = 1, OriginSkill = "skill.venom_knife", ActionId = briarState.NextActionId++, NextTick = briarState.Tick, ExpiresTick = briarState.Tick + 90 });
            int allyId = briarState.NextActorId++;
            briarState.Actors.Add(new()
            {
                Id = allyId,
                DefinitionId = "summon.companion",
                Faction = CombatFaction.Ally,
                Role = "Companion",
                Position = new(-2600, 1400),
                Health = 5,
                MaxHealth = 30,
                OwnerId = 1,
                ExpiresTick = briarState.Tick + 500,
                RecoveryUntil = briarState.Tick + 500
            });
            var briar = CombatSession.Restore(combatJson, briarState); await Show(briar);
            var grown = Tick(briar); await Frames();
            var patch = briar.View.Areas.Single(a => a.ContentId == "effect.set_briar_thorns");
            Check("fixture_briar_actual_poison_kill_heals_companion", grown.Any(e => e.Kind == "EquipmentSetTriggered" && e.ContentId == EquipmentSets.Briarbound) && briar.View.Actors.Single(a => a.Id == allyId).Health == 25);
            Check("fixture_briar_green_thorn_geometry", Descendants(_sandbox).OfType<MeshInstance3D>().Any(n => n.Name == "BriarPatch_" + patch.Id && n.GetNodeOrNull<Node3D>("Thorns")?.GetChildCount() == 6));
            Check("fixture_briar_trigger_hud", Find<Label>("LegendaryTrigger").Text.Contains("BRIARBOUND · thorns grow", StringComparison.Ordinal));
            await Capture("fixture-briar-thorns.png");
            string briarHash = briar.StateHash; reduced.ButtonPressed = true; await Frames();
            Check("fixture_reduced_effects_preserve_briar_and_state", briar.StateHash == briarHash && Descendants(_sandbox).OfType<MeshInstance3D>().Any(n => n.Name == "BriarPatch_" + patch.Id));
            await Capture("fixture-briar-reduced.png"); reduced.ButtonPressed = originalReduced;
            for (int i = 0; i < 65; i++) Tick(briar); await Frames();
            Check("fixture_briar_expiry_removes_geometry_and_trigger", !briar.View.Areas.Any(a => a.ContentId == "effect.set_briar_thorns") && !Descendants(_sandbox).OfType<MeshInstance3D>().Any(n => n.Name.ToString().StartsWith("BriarPatch_", StringComparison.Ordinal)) && !Find<Label>("LegendaryTrigger").Visible);
        }
        finally
        {
            reduced.ButtonPressed = originalReduced; caption.QueueFree(); _sandbox.AdoptSession(Session.Combat); _sandbox.SetEnvironmentStyle(style);
            _sandbox.SetModalPaused("set-presentation-fixture", false); Refresh(); await Frames();
        }
        Check("fixture_never_mutated_earned_session_or_history", Session.StateHash == earnedHash && Session.CaptureReplay().Frames.Length == earnedFrames);
        Check("fixture_transition_clears_set_ground_visuals", !Descendants(_sandbox).OfType<MeshInstance3D>().Any(n => n.Name.ToString().StartsWith("BriarPatch_", StringComparison.Ordinal) || n.Name.ToString().StartsWith("PyreTrail_", StringComparison.Ordinal)));
        RoundTrip("earned_save_after_detached_fixture");
    }
    private async Task Compact(string name)
    {
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout(name + "_compact"); await Capture(name + "-compact.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(8);
    }
    private void CheckLayout(string name)
    {
        var viewport = GetViewport().GetVisibleRect();
        foreach (string target in new[] { "LegendaryCollection", "CollectionCatalog", "CollectionDetailsScroll", "CollectionClose", "CollectionFilterSets" })
        { var control = Find<Control>(target); Check(name + "_" + target + "_fits", control.IsVisibleInTree() && viewport.Encloses(control.GetGlobalRect())); }
    }
    private bool CombatSetActive(string id) => Session.Combat.View.EquipmentSets is { } sets && (id switch
    { EquipmentSets.LastVigil => sets.LastVigilActive, EquipmentSets.Briarbound => sets.BriarboundActive, EquipmentSets.Ashrunner => sets.AshrunnerActive, _ => false });
    private PermanentItem Owned(string id) => Progression.Character.Items.Single(i => i.DefinitionId == id);
    private string JournalText() => string.Join('\n', Descendants(Journal).OfType<Label>().Select(l => l.Text));
    private void OpenJournal() => Call(_director, "OpenCollection");
    private void Refresh() { _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh"); }
    private void CloseMenus()
    {
        Journal.SetOpen(false); Character.Close(); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false);
        Field<EndgameHud>(_director, "_board").SetOpen(false); Field<OpeningGuidancePanel>(_director, "_openingGuide").SetOpen(false);
    }
    private void Step(EndgameRuntimeCommand command, bool refresh = true)
    {
        var result = Session.Execute(command); _commands++;
        if (!result.Success) throw new InvalidDataException(result.Reason + " / " + command.Action);
        Call(_director, "Observe", result); if (refresh) Refresh();
    }
    private void Walk(Position target)
    {
        for (int i = 0; i < 700; i++)
        {
            var player = Session.Combat.View.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, target) <= 1200L * 1200) { Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); return; }
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target, Session.Room);
            Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]));
        }
        throw new InvalidDataException("Could not approach Torren.");
    }
    private bool Replay() => EndgameRuntimeReplayRunner.Run(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), Session.CaptureReplay()).Success;
    private void RoundTrip(string name) { string hash = Session.StateHash; Check(name + "_replay", Replay()); Call(_director, "Save"); Call(_director, "Load"); Check(name, Session.StateHash == hash); }
    private async Task Drag(string from, string to)
    {
        var source = Find<Control>(from); var target = Find<Control>(to); await Reveal(source);
        var start = source.GetGlobalRect().GetCenter(); _lastInput = from + " → " + to;
        GetViewport().PushInput(new InputEventMouseMotion { Position = start }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = start, Pressed = true, ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
        for (int i = 1; i <= 3 && !GetViewport().GuiIsDragging(); i++)
        { GetViewport().PushInput(new InputEventMouseMotion { Position = start + new Vector2(i * 12, 0), Relative = new(12, 0), ButtonMask = MouseButtonMask.Left }, true); await Frames(1); }
        _drags++; Check("drag_started_" + _drags, GetViewport().GuiIsDragging() && _sandbox.IsPaused);
        var destination = target.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = destination, ButtonMask = MouseButtonMask.Left }, true); await Frames();
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = destination, Pressed = false, ButtonMask = 0 }, true); await Frames();
        Check("drag_ended_" + _drags, !GetViewport().GuiIsDragging());
    }
    private async Task Reveal(Control control)
    {
        for (Node? parent = control.GetParent(); parent is not null; parent = parent.GetParent()) if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(control);
        await Frames(); Check("target_visible_" + control.Name, control.IsVisibleInTree() && GetViewport().GetVisibleRect().HasPoint(control.GetGlobalRect().GetCenter()));
    }
    private async Task Click(string name)
    {
        var button = Find<Button>(name); await Reveal(button); _lastInput = name;
        var point = button.GetGlobalRect().GetCenter(); bool received = false; void Receipt() => received = true; button.Pressed += Receipt;
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
        _clicks++; await Frames(); if (GodotObject.IsInstanceValid(button)) button.Pressed -= Receipt; Check("click_received_" + _clicks, received);
    }
    private async Task Frames(int count = 4) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox?._Process(0); } }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-equipment-sets")) return;
        GetWindow().GrabFocus(); await Frames(); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage(); Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Equipment set check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private async Task Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "EquipmentSetsSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            clicks = _clicks,
            drags = _drags,
            commands = _commands,
            error,
            lastInput = _lastInput,
            scope = "Earned six campaign drops with ordinary Vanguard commands; real viewport collection/partner/tracking and equipment drags; comparison completes/breaks all three sets; save/replay; 130 cosmetic checks across five disciplines; 1280/780 layouts. Additional explicitly detached rendering fixtures arrange incoming hits and poison to exercise actual set-trigger events, HUD, ground geometry, reduced effects and cleanup; these fixtures do not advance the earned session. No injected reward proof."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "equipment-sets-review.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report));
        if (_director is not null && GodotObject.IsInstanceValid(_director)) _director.QueueFree(); _director = null!; _sandbox = null!;
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GC.Collect();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GetTree().Quit(passed ? 0 : 1);
    }
}
