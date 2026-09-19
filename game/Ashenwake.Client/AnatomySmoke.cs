using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>The shipping campaign earns its own fragment; inspection and surgery receive real viewport input.</summary>
public partial class AnatomySmoke : Node
{
    private static readonly string[] Slots = ["Mind", "Eyes", "Heart", "Spine", "Arms", "Legs"];
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly List<CombatCommand> _inputCommands = [];
    private readonly List<EndgameRuntimeReplay> _replays = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private CampaignHud _hud = null!;
    private Camera3D _camera = null!;
    private string _output = "", _savedHash = "";
    private bool _writeReport;
    private int _setupCommands;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private AdventureState Anatomy => Session.Campaign.Capture().Production.Expedition.Adventure;
    private AnatomyWorkbench Workbench => Descendants(_hud).OfType<AnatomyWorkbench>().Single();
    private CharacterPreview Preview => Descendants(Workbench).OfType<CharacterPreview>().Single();
    private CharacterVisual Hero => Descendants(_sandbox.GetNode<Node3D>("Actor1")).OfType<CharacterVisual>().Single();
    private CharacterVisual PreviewHero => Descendants(Preview).OfType<CharacterVisual>().Single();
    private CorePosition Player => Session.Combat.View.Actors.Single(a => a.Id == 1).Position;
    private List<string> WorldEvents => Field<List<string>>(_director, "_worldEvents");

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--anatomy-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Anatomy smoke requires --anatomy-smoke --discipline=Vanguard --output=<fresh-artifact-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = new(1280, 800); GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _director.SetProcess(false);
            _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false); _sandbox.AutomaticStep = false;
            _hud = Field<CampaignHud>(_director, "_campaignHud");
            _camera = _sandbox.GetChildren().OfType<Camera3D>().Single();
            var advance = _sandbox.AdvanceOverride!;
            _sandbox.AdvanceOverride = commands => { _inputCommands.AddRange(commands); return advance(commands); };
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Frames(8);
            if (_sandbox.IsPaused) await ClickText("Resume playing");
            await CloseJourney();
            await EarnHeart();
            await InspectRewardAway();
            await ReachMara();
            await InstallHeart();
            await ManifestationAndRemoval();
            await SaveLoadAndLayout();
            RecordReplay();
            Check("all_campaign_and_surgery_replays_match", _replays.Count > 0 && _replays.All(replay => EndgameRuntimeReplayRunner.Run(
                Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"),
                Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), replay).Success));
            System.IO.File.WriteAllText(Path.Combine(_output, "anatomy-replays.json"), JsonData.Write(_replays));
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("anatomy-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private async Task EarnHeart()
    {
        Check("new_character_has_not_earned_the_heart", !Anatomy.OwnedFragments.Contains("fragment.heart_serath"));
        // The ordinary policy installs the starting Spine and Arms fragments and a 40-point
        // form. It never installs the Heart. Stop on its actual Act I award, leaving Bell loot.
        for (int i = 0; !Anatomy.OwnedFragments.Contains("fragment.heart_serath") && i < 12000; i++)
        {
            EndgameRuntimeCommand command = Session.Campaign.ActiveEncounterId == "campaign.bell_saint" && !Session.EncounterCleared
                ? new(EndgameRuntimeAction.Tick, Commands: CampaignCombatSmoke.Commands(Session.Combat.View, Session.Room).Where(c => c.Kind != CombatCommandKind.Pickup).ToArray())
                : new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign));
            Execute(command, refresh: false);
            if (i % 120 == 0) { Refresh(); await Frames(); }
        }
        Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)]));
        await CloseJourney(); await Frames();
        Check("real_bell_victory_awards_owned_uninstalled_heart", Session.Campaign.ActiveEncounterId == "campaign.bell_saint" &&
            Session.EncounterCleared && Session.Campaign.Capture().Campaign.CompletedActs.Contains(1) &&
            Anatomy.OwnedFragments.Contains("fragment.heart_serath") && !Anatomy.Anatomy.ContainsKey("Heart"));
        Check("bell_victory_keeps_ground_rewards_for_explicit_review", Session.Combat.View.Loot.Count > 0);
        Check("earned_character_survives_the_route", Session.Campaign.Capture().Campaign.Deaths == 0 && Session.Combat.View.Actors.Single(a => a.Id == 1).Health > 0);
        Check("starting_surgery_is_real_and_qualifies_for_first_form", Session.Production.View.Resonance == 46 &&
            Session.Production.View.ActiveManifestations.Contains("manifestation.stone_memory"));
        Check("uninstalled_heart_has_no_world_mark", !HasMark(Hero, "AnatomyHeart"));
        await Capture("anatomy-earned-bell-reward.png");
    }

    private async Task InspectRewardAway()
    {
        string hash = Session.StateHash, hero = Hero.AppearanceKey;
        long[] loot = Session.Combat.View.Loot.Select(l => l.Id).ToArray();
        int commands = Session.CaptureReplay().Frames.Length;
        await ClickNamed("CampaignNextStep");
        if (!Workbench.IsVisibleInTree()) await ClickNamed("InspectAnatomyReward");
        await Frames();
        Check("victory_prompt_opens_owned_heart_inspection", Workbench.IsVisibleInTree() && Workbench.SelectedSlot == "Heart" && Workbench.Inspecting);
        var forecast = Forecast;
        Check("away_preview_projects_resonance_and_second_threshold", forecast.Success && forecast.Before.Resonance == 46 && forecast.After.Resonance == 64 &&
            forecast.After.Manifestations.Single(m => m.Threshold == 60).Available);
        Check("away_surgery_is_disabled", NamedButton("AnatomyApply").Disabled && NamedButton("AnatomyReturn").IsVisibleInTree());
        Check("reward_inspection_leaves_hash_loot_and_command_history_unchanged", hash == Session.StateHash && commands == Session.CaptureReplay().Frames.Length &&
            loot.SequenceEqual(Session.Combat.View.Loot.Select(l => l.Id)));
        Check("preview_heart_mark_does_not_modify_world_character", HasMark(PreviewHero, "AnatomyHeart") && Hero.AppearanceKey == hero && !HasMark(Hero, "AnatomyHeart"));
        await ModalInput("away");
        await Capture("anatomy-heart-preview-away.png");

        await ClickNamed("AnatomyBodyView");
        var cachedSlots = SlotIds();
        foreach (string slot in Slots)
        {
            await ClickNamed("AnatomySlot" + slot);
            Check("body_slot_selects_" + slot.ToLowerInvariant(), Workbench.SelectedSlot == slot && !Workbench.Inspecting);
        }
        Check("all_six_body_slots_are_cached", cachedSlots.Count == 6 && cachedSlots.SetEquals(SlotIds()));
        Check("empty_slots_explain_their_current_availability", !Anatomy.Anatomy.ContainsKey(Workbench.SelectedSlot) &&
            VisibleLabels(Workbench).Any(l => l.Text == "Installed: Empty") &&
            VisibleLabels(Workbench).Any(l => l.Text.Contains("OWNED", StringComparison.Ordinal) || l.Text.Contains("UNDISCOVERED", StringComparison.Ordinal) || l.Text.Contains("No fragments discovered", StringComparison.Ordinal)));
        await ClickNamed("AnatomySlotHeart");
        await Capture("anatomy-six-slot-body-map.png");
        await ClickNamed("AnatomyFragment_fragment.heart_serath");
        Check("fragment_card_opens_candidate_without_applying", Workbench.Inspecting && HasMark(PreviewHero, "AnatomyHeart") && hash == Session.StateHash);
        await ClickNamed("AnatomyReset");
        Check("reset_returns_to_installed_appearance", !Workbench.Inspecting && !HasMark(PreviewHero, "AnatomyHeart") && Hero.AppearanceKey == hero);
        await ClickNamed("AnatomyFragment_fragment.heart_serath");
        await ClickNamed("AnatomyReturn");
        Check("return_with_ground_loot_opens_review_without_travel", !Session.InHub && !Workbench.IsVisibleInTree() && hash == Session.StateHash &&
            loot.SequenceEqual(Session.Combat.View.Loot.Select(l => l.Id)) && Descendants(_hud).OfType<Button>().Any(b => b.IsVisibleInTree() && b.Text.Contains("uncollected drops", StringComparison.Ordinal)));
        Check("closing_inspection_disables_its_render_viewport", !Preview.Rendering && !Preview.IsProcessing());
        await Capture("anatomy-ground-reward-review.png");
        await ClickTextPrefix("Return to Greyhaven");
        Check("explicit_return_reaches_greyhaven_with_heart_owned", Session.InHub && Anatomy.OwnedFragments.Contains("fragment.heart_serath") && !Anatomy.Anatomy.ContainsKey("Heart"));
    }

    private async Task ReachMara()
    {
        await CloseJourney();
        // A real ground click gives the service button a distant, reachable approach to exercise.
        await Click(_camera.UnprojectPosition(World(new(-5000, 5000))));
        await WalkUntilStopped();
        var mara = Session.Interactions.Single(i => i.ActionId == "service.mara");
        Check("service_checkpoint_is_outside_mara_range", CorePosition.DistanceSquared(Player, mara.Position) > (long)mara.Range * mara.Range);
        await KeyPress(Key.J); await ClickText("Anatomy");
        await ClickNamed("AnatomyBodyView"); await ClickNamed("AnatomySlotHeart");
        await ClickNamed("AnatomyFragment_fragment.heart_serath");
        Check("distant_hub_inspection_requires_a_service_visit", NamedButton("AnatomyApply").Disabled && NamedButton("AnatomyVisitMara").IsVisibleInTree());
        int serviceEvents = WorldEvents.Count(e => e == "ServiceOpened:service.mara");
        await ClickNamed("AnatomyVisitMara");
        Check("visit_mara_button_starts_world_approach", _sandbox.PendingWorldActionId == "service.mara" && _sandbox.ClickMoveDestination is not null && !Workbench.IsVisibleInTree());
        await WalkUntilStopped();
        Check("service_arrival_uses_authoritative_range_and_opens_anatomy", CorePosition.DistanceSquared(Player, mara.Position) <= (long)mara.Range * mara.Range &&
            Workbench.IsVisibleInTree() && WorldEvents.Count(e => e == "ServiceOpened:service.mara") == serviceEvents + 1);
        Check("service_arrival_consumes_movement_intent", _sandbox.PendingWorldActionId is null && _sandbox.ClickMoveDestination is null);
    }

    private async Task InstallHeart()
    {
        await ClickNamed("AnatomyBodyView"); await ClickNamed("AnatomySlotHeart");
        await ClickNamed("AnatomyFragment_fragment.heart_serath");
        string hash = Session.StateHash, worldKey = Hero.AppearanceKey, previewKey = Preview.AppearanceKey;
        var forecast = Forecast;
        Check("in_range_heart_preview_can_be_applied", forecast.Success && forecast.CanApply && !NamedButton("AnatomyApply").Disabled);
        await ModalInput("at_mara");
        Check("first_heart_preview_shows_new_combination", forecast.After.QualifyingConcordances.Contains("concordance.funeral_flame") &&
            !forecast.Before.DiscoveredConcordances.Contains("concordance.funeral_flame"));
        float angle = Preview.RotationAngle;
        await ClickNamed("RotateRight", Preview);
        Check("preview_rotation_is_cosmetic", Preview.RotationAngle != angle && Session.StateHash == hash && Hero.AppearanceKey == worldKey);
        await ClickNamed("ResetRotation", Preview);
        await Capture("anatomy-mara-ready-to-implant.png");
        int installed = WorldEvents.Count(e => e == "FragmentInstalled:fragment.heart_serath");
        await ClickNamed("AnatomyApply");
        Check("one_apply_installs_exactly_one_heart", Anatomy.Anatomy.GetValueOrDefault("Heart") == "fragment.heart_serath" &&
            WorldEvents.Count(e => e == "FragmentInstalled:fragment.heart_serath") == installed + 1);
        Check("committed_resonance_and_combinations_match_preview", Session.Production.View.Resonance == forecast.After.Resonance &&
            Anatomy.Concordances.SequenceEqual(forecast.After.DiscoveredConcordances));
        bool appearanceMatches = Hero.AppearanceKey == previewKey && Preview.AppearanceKey == previewKey && HasMark(Hero, "AnatomyHeart") && HasMark(PreviewHero, "AnatomyHeart");
        if (!appearanceMatches) System.IO.File.WriteAllText(Path.Combine(_output, "anatomy-appearance-mismatch.json"), JsonData.Write(new
        {
            expected = previewKey,
            world = Hero.AppearanceKey,
            preview = Preview.AppearanceKey,
            projected = _sandbox.CurrentAppearance.Key,
            worldHeart = HasMark(Hero, "AnatomyHeart"),
            previewHeart = HasMark(PreviewHero, "AnatomyHeart"),
            equippedFragments = Session.Combat.View.Fragments.Where(f => f.Equipped).Select(f => f.Id).ToArray(),
            Anatomy.Anatomy
        }));
        Check("world_and_preview_show_the_committed_heart", appearanceMatches);
        Check("completed_apply_returns_to_installed_state", !Workbench.Inspecting && NamedButton("AnatomyApply").Disabled);
        string appliedHash = Session.StateHash;
        await Click(NamedButton("AnatomyApply").GetGlobalRect().GetCenter()); await Frames(4);
        Check("disabled_apply_does_not_repeat_the_transaction", Session.StateHash == appliedHash && WorldEvents.Count(e => e == "FragmentInstalled:fragment.heart_serath") == installed + 1);
        await Capture("anatomy-heart-installed.png");
    }

    private async Task ManifestationAndRemoval()
    {
        string hash = Session.StateHash, world = Hero.AppearanceKey;
        await ClickNamed("AnatomyManifestation_manifestation.whispering_shadow");
        var forecast = Forecast;
        Check("second_threshold_preview_selects_a_form_without_mutation", forecast.Success && forecast.After.Manifestations.Single(m => m.Threshold == 60) is
        { SelectedId: "manifestation.whispering_shadow", Active: true } && Session.StateHash == hash && Hero.AppearanceKey == world);
        string preview = Preview.AppearanceKey;
        await Capture("anatomy-manifestation-preview.png");
        await ClickNamed("AnatomyApply");
        Check("manifestation_apply_matches_preview_and_keeps_first_form", Session.Production.View.ActiveManifestations.Contains("manifestation.whispering_shadow") &&
            Session.Production.View.ActiveManifestations.Contains("manifestation.stone_memory") && Hero.AppearanceKey == preview);

        await ClickNamed("AnatomyBodyView"); await ClickNamed("AnatomySlotHeart");
        await ClickNamed("AnatomyEmptySlot");
        hash = Session.StateHash; world = Hero.AppearanceKey; preview = Preview.AppearanceKey; forecast = Forecast;
        Check("removal_preview_suppresses_second_form_but_remembers_choice", forecast.After.Resonance == 46 &&
            forecast.After.Manifestations.Single(m => m.Threshold == 60) is { SelectedId: "manifestation.whispering_shadow", Active: false, Available: false });
        Check("removal_distinguishes_historical_discovery_from_current_tags", forecast.After.DiscoveredConcordances.Contains("concordance.funeral_flame") &&
            !forecast.After.QualifyingConcordances.Contains("concordance.funeral_flame"));
        Check("removal_preview_changes_only_its_own_character", !HasMark(PreviewHero, "AnatomyHeart") && HasMark(Hero, "AnatomyHeart") && Session.StateHash == hash && Hero.AppearanceKey == world);
        await Capture("anatomy-removal-preview.png");
        await ClickNamed("AnatomyApply");
        Check("real_removal_matches_suppression_preview", !Anatomy.Anatomy.ContainsKey("Heart") && Hero.AppearanceKey == preview && !HasMark(Hero, "AnatomyHeart") &&
            !Session.Production.View.ActiveManifestations.Contains("manifestation.whispering_shadow") && Anatomy.Manifestations.GetValueOrDefault(60) == "manifestation.whispering_shadow");
        Check("removal_preserves_ownership_and_discovery", Anatomy.OwnedFragments.Contains("fragment.heart_serath") && Anatomy.Concordances.Contains("concordance.funeral_flame"));
        await ClickNamed("AnatomyFragment_fragment.heart_serath");
        Check("reinstallation_preview_reactivates_remembered_form", Forecast.After.Manifestations.Single(m => m.Threshold == 60) is { SelectedId: "manifestation.whispering_shadow", Active: true });
        preview = Preview.AppearanceKey;
        await ClickNamed("AnatomyApply");
        Check("reinstallation_restores_the_same_form_and_heart_mark", Session.Production.View.ActiveManifestations.Contains("manifestation.whispering_shadow") &&
            HasMark(Hero, "AnatomyHeart") && Hero.AppearanceKey == preview);
    }

    private async Task SaveLoadAndLayout()
    {
        await KeyPress(Key.F5);
        _savedHash = Session.StateHash;
        string key = Hero.AppearanceKey;
        string path = Path.Combine(_output, "endgame.save.json");
        Check("shipping_save_contains_the_completed_surgery", System.IO.File.Exists(path));
        byte[] savedBytes = System.IO.File.ReadAllBytes(path);
        await ClickNamed("AnatomyEmptySlot");
        Check("unsaved_inspection_does_not_change_saved_hash", Session.StateHash == _savedHash && Workbench.Inspecting);
        RecordReplay();
        await KeyPress(Key.F9);
        Check("shipping_load_retains_implant_form_and_world_appearance", Session.StateHash == _savedHash && Anatomy.Anatomy.GetValueOrDefault("Heart") == "fragment.heart_serath" &&
            Session.Production.View.ActiveManifestations.Contains("manifestation.whispering_shadow") && Hero.AppearanceKey == key && HasMark(Hero, "AnatomyHeart"));
        Check("shipping_load_reacquires_visible_anatomy_pause", Workbench.IsVisibleInTree() && HasAnatomyPause);
        Check("loading_discards_the_uncommitted_inspection", !Workbench.Inspecting);
        await ModalInput("restored");
        await KeyPress(Key.F5);
        Check("unchanged_surgery_save_remains_byte_identical", savedBytes.SequenceEqual(System.IO.File.ReadAllBytes(path)));
        await KeyPress(Key.F9);
        Check("byte_identical_load_reacquires_anatomy_pause", Workbench.IsVisibleInTree() && HasAnatomyPause && !Workbench.Inspecting && Session.StateHash == _savedHash);
        await ModalInput("identical_restore");
        await ClickNamed("AnatomyBodyView");
        var slots = SlotIds();
        await CheckLayout(new(1280, 720), "720p");
        await CheckLayout(new(780, 800), "narrow");
        Check("resizing_preserves_all_six_slot_controls", slots.SetEquals(SlotIds()));
        await ClickNamed("AnatomyCharacterView");
        Check("narrow_character_preview_matches_saved_appearance", Preview.AppearanceKey == key && Preview.Rendering);
        await Capture("anatomy-narrow-character.png");
        await CloseJourney(); await Frames(3);
        Check("closed_workbench_stops_preview_rendering_and_processing", !Preview.Rendering && !Preview.IsProcessing());
        Check("layout_and_inspection_leave_saved_gameplay_unchanged", Session.StateHash == _savedHash);
        GetWindow().Size = new(1280, 800); GetWindow().ContentScaleSize = new(1280, 800); await Frames(6);
        Ticks(2);
    }

    private async Task CheckLayout(Vector2I size, string suffix)
    {
        GetWindow().Size = size; GetWindow().ContentScaleSize = size; await Frames(6);
        var bounds = GetViewport().GetVisibleRect().Grow(1);
        var buttons = Slots.Select(slot => NamedButton("AnatomySlot" + slot)).Concat(new[] { NamedButton("AnatomyApply"), NamedButton("AnatomyReset"), NamedButton("AnatomyBodyView"), NamedButton("AnatomyCharacterView") }).ToArray();
        Check("workbench_fits_" + suffix, bounds.Encloses(Workbench.GetGlobalRect()) && buttons.All(button => button.IsVisibleInTree() && bounds.Encloses(button.GetGlobalRect())));
        var forecast = Descendants(Workbench).OfType<Label>().Single(l => l.Name == "AnatomyForecast");
        Check("forecast_and_apply_remain_visible_" + suffix, bounds.Encloses(forecast.GetGlobalRect()) && forecast.Size.Y >= forecast.GetMinimumSize().Y &&
            forecast.GetGlobalRect().End.Y <= NamedButton("AnatomyApply").GetGlobalRect().Position.Y);
        await Capture("anatomy-body-" + suffix + ".png");
    }

    private AnatomyPreviewResult Forecast => Field<AnatomyPreviewResult>(Workbench, "_forecast");
    private bool HasAnatomyPause => _sandbox.IsPaused && Field<HashSet<string>>(_sandbox, "_modalPauses").Contains("divine-anatomy");
    private async Task ModalInput(string suffix)
    {
        string hash = Session.StateHash, room = Session.Campaign.ActiveEncounterId;
        long tick = Session.Tick;
        int commands = Session.CaptureReplay().Frames.Length;
        bool inHub = Session.InHub;
        float cameraSize = _camera.Size;
        Check("anatomy_owns_modal_pause_" + suffix, HasAnatomyPause);
        await KeyPress(Key.F); await KeyPress(Key.Period); Ticks(8); await Frames();
        Check("modal_interact_and_single_step_do_not_reach_gameplay_" + suffix, Session.StateHash == hash && Session.Tick == tick &&
            Session.CaptureReplay().Frames.Length == commands && Session.Campaign.ActiveEncounterId == room && Session.InHub == inHub &&
            Workbench.IsVisibleInTree() && HasAnatomyPause);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Position = new(4, 4), Pressed = pressed }, true);
        await Frames();
        Check("modal_backdrop_wheel_does_not_zoom_world_" + suffix, _camera.Size == cameraSize && Session.StateHash == hash && Workbench.IsVisibleInTree());
    }
    private HashSet<ulong> SlotIds() => Descendants(Workbench).OfType<Button>().Where(b => Slots.Any(s => b.Name == "AnatomySlot" + s)).Select(b => b.GetInstanceId()).ToHashSet();
    private static bool HasMark(Node root, string name) => Descendants(root).OfType<Node3D>().Any(n => n.Name == name && n.IsVisibleInTree());
    private static IEnumerable<Label> VisibleLabels(Node root) => Descendants(root).OfType<Label>().Where(l => l.IsVisibleInTree());
    private void Execute(EndgameRuntimeCommand command, bool refresh = true)
    {
        var result = Session.Execute(command); _setupCommands++;
        if (!result.Success) throw new InvalidDataException("Anatomy setup command failed: " + result.Reason);
        Invoke(_director, "Observe", result);
        if (refresh) Refresh();
    }
    private void Refresh() { _sandbox.AdoptSession(Session.Combat); Invoke(_director, "Refresh"); }
    private void Ticks(int count = 1) { for (int i = 0; i < count; i++) _sandbox._Process(FixedStepClock.SecondsPerTick); }
    private async Task WalkUntilStopped()
    {
        for (int i = 0; i < 720 && (_sandbox.ClickMoveDestination is not null || _sandbox.PendingWorldActionId is not null); i++)
        {
            Ticks();
            if (Session.Room.Obstacles.Any(o => Player.X >= o.MinX && Player.X <= o.MaxX && Player.Z >= o.MinZ && Player.Z <= o.MaxZ))
                throw new InvalidDataException("Anatomy service approach entered an authoritative obstacle.");
            if (i % 30 == 0) await Frames();
        }
        if (_sandbox.ClickMoveDestination is not null || _sandbox.PendingWorldActionId is not null) throw new InvalidDataException("Anatomy service approach exceeded its bounded route.");
        Ticks(2); await Frames();
    }
    private Button NamedButton(string name, Node? root = null)
    {
        // Node.Name replaces the period in content IDs with an underscore.
        var visible = Descendants(root ?? _director).OfType<Button>().Where(b => b.IsVisibleInTree()).ToArray();
        string valid = name.Replace('.', '_');
        var found = visible.SingleOrDefault(b => b.Name == valid);
        return found ?? throw new InvalidDataException("Missing anatomy button " + valid + "; visible: " + string.Join(", ", visible.Select(b => b.Name.ToString())));
    }
    private async Task ClickNamed(string name, Node? root = null)
    {
        var button = NamedButton(name, root);
        if (button.Disabled) throw new InvalidDataException("Disabled anatomy button: " + name);
        await Reveal(button);
        await Click(button.GetGlobalRect().GetCenter());
    }
    private async Task ClickText(string text)
    {
        var button = Descendants(_director).OfType<Button>().Single(b => b.IsVisibleInTree() && b.Text == text);
        await Reveal(button); await Click(button.GetGlobalRect().GetCenter());
    }
    private async Task ClickTextPrefix(string text)
    {
        var button = Descendants(_hud).OfType<Button>().Single(b => b.IsVisibleInTree() && b.Text.StartsWith(text, StringComparison.Ordinal));
        await Reveal(button); await Click(button.GetGlobalRect().GetCenter());
    }
    private async Task Reveal(Control control)
    {
        // Scroll through the real card list with wheel input before clicking a clipped card.
        for (Node? ancestor = control.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is not ScrollContainer scroll) continue;
            for (int attempt = 0; attempt < 100 && !VerticallyShown(scroll, control); attempt++)
            {
                var button = control.GetGlobalRect(); var rect = scroll.GetGlobalRect();
                var direction = button.Position.Y < rect.Position.Y ? MouseButton.WheelUp : MouseButton.WheelDown;
                foreach (bool pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = direction, Position = rect.GetCenter(), Pressed = pressed }, true);
                await Frames();
            }
            if (!VerticallyShown(scroll, control)) throw new InvalidDataException("Anatomy card could not be scrolled into view: " + control.GetPath());
        }
        if (!GetViewport().GetVisibleRect().Encloses(control.GetGlobalRect()))
            throw new InvalidDataException("Anatomy button is outside viewport: " + control.GetPath() + " " + control.GetGlobalRect());
    }
    private static bool VerticallyShown(Control clip, Control control) => control.GetGlobalRect().Position.Y >= clip.GetGlobalRect().Position.Y - 1 &&
        control.GetGlobalRect().End.Y <= clip.GetGlobalRect().End.Y + 1;
    private async Task CloseJourney()
    {
        var close = Descendants(_hud).OfType<Button>().SingleOrDefault(b => b.Text == "Close" && b.IsVisibleInTree());
        if (close is not null) await Click(close.GetGlobalRect().GetCenter());
    }
    private async Task Click(Vector2 position)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = position }, true);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = position, Pressed = pressed }, true);
        await Frames();
    }
    private async Task KeyPress(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
        await Frames();
    }
    private async Task Frames(int count = 2) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void RecordReplay() { if (Session.CaptureReplay().Frames.Length > 0) _replays.Add(Session.CaptureReplay()); }
    private static Vector3 World(CorePosition point) => new(point.X * .001f, 0, point.Z * .001f);
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object owner, string name) => (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) ?? throw new MissingFieldException(name));
    private static void Invoke(object owner, string name, params object[] args) =>
        (owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(name)).Invoke(owner, args);
    private async Task Capture(string filename)
    {
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-anatomy") || DisplayServer.GetName() == "headless") return;
        await Frames(3); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok);
        _captures.Add(filename);
    }
    private void Check(string name, bool passed)
    {
        _checks[name] = passed;
        if (!passed) throw new InvalidDataException("Anatomy check failed: " + name);
    }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "AnatomyClientSmokePassed" : "AnatomyClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            setupCommands = _setupCommands,
            inputCommands = _inputCommands.Count,
            replayCount = _replays.Count,
            savedHash = _savedHash,
            error,
            scope = "The shipping EndgameDirector receives viewport clicks for the earned Heart reward, six body slots, fragment and Manifestation cards, reset/apply, explicit ground-loot review, Mara approach, rotation and save/load. Normal deterministic campaign commands prepare the starting loadout and earn the Bell victory and fragment; no state is fabricated. Preview purity, real removal/suppression/reinstallation, visible world marks, responsive layout and hidden viewport lifecycle are checked. All resulting runtime command branches replay. This is one real Act I upgrade journey, not a full campaign balance or performance benchmark."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "anatomy-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
