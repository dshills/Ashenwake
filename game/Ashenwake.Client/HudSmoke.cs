using System.Globalization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Real opening encounters and earned endgame rewards supply the responsive combat HUD.</summary>
public partial class HudSmoke : Node3D
{
    private sealed record ReplaySegment(string File, int Commands, string Hash);
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly List<ReplaySegment> _replays = [];
    private readonly Dictionary<string, int> _events = [];
    private readonly HashSet<string> _earnedNotices = [];
    private readonly HashSet<string> _observedRewardKinds = [];
    private readonly Dictionary<string, Node> _hudNodes = [];
    private readonly List<CombatItem> _pickups = [];
    private EndgameRuntimeSession _session = null!;
    private Sandbox _sandbox = null!;
    private CampaignStage _stage = null!;
    private EndgamePresentation _effects = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private CampaignContent _campaign = null!;
    private EndgameContent _endgame = null!;
    private CombatActorView? _timedActor, _stackedActor;
    private ReplaySegment? _statusCombatReplay;
    private int _statusVenomCasts, _statusPoisonApplications;
    private string _output = "", _combatJson = "", _roomKey = "";
    private bool _writeReport, _sawBarrier, _sawDamagedHealth, _sawPotionCooldown, _sawDodgeCooldown, _sawSkillCooldown;
    private int _commands, _segmentCommands, _renderedViews;
    private long _xpEarned;

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--hud-smoke") || _output.Length == 0 || Directory.Exists(_output) &&
                Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("HUD smoke requires --hud-smoke --output=<fresh-artifact-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _adventure = AdventureContent.Parse(Read("adventure")); _progression = ProgressionContent.Parse(Read("progression"));
            _campaign = CampaignContent.Parse(Read("campaign")); _endgame = EndgameContent.Parse(Read("endgame"));
            _combatJson = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson, Read("endgame-combat"), _endgame).CombatJson;
            _session = EndgameRuntimeSession.Create(_combatJson, _adventure, _progression, _campaign, _endgame);
            // This route drives every command explicitly. Ignore desktop focus changes during
            // captures; real interruption/manual pause behavior is covered in InteractionSmoke.
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
            AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
            _stage = new CampaignStage(); AddChild(_stage);
            _effects = new EndgamePresentation(); AddChild(_effects); _effects.AttachOverlay(_sandbox);
            Refresh(); _sandbox.SetPaused(true); await Frames(6);
            Check("fresh_hud_shows_six_authored_skills_with_locked_ultimate", Enumerable.Range(1, 6).All(i => Find<SkillButton>("HotbarSkill" + i).SkillId == _session.Combat.View.Skills[i - 1].Id) &&
                Find<SkillButton>("HotbarSkill6").Disabled && _session.Production.ProgressionView.Level == 1);
            int lockedPresses = 0; Find<SkillButton>("HotbarSkill6").Pressed += () => lockedPresses++;
            await Click(Find<SkillButton>("HotbarSkill6"));
            Check("native_locked_ultimate_cannot_dispatch", lockedPresses == 0 && _session.CaptureReplay().Frames.Length == 0);
            CheckInitialRewardBaseline("fresh");
            await CheckLayouts("opening");
            _sandbox.SetPaused(false);
            await OpeningCampaign();
            await CheckSaveReset("opening");
            RecordReplay();
            await EarnedEndgame();
            await CheckActualStatusViews();
            await CheckRewardFeed();
            await CheckSaveReset("endgame");
            RecordReplay();
            Check("all_hud_commands_have_exact_verified_replay_coverage", _replays.Sum(r => r.Commands) == _commands);
            Check("hud_observed_real_health_barrier_and_cooldown_changes", _sawDamagedHealth && _sawBarrier && _sawPotionCooldown && _sawDodgeCooldown && _sawSkillCooldown);
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("hud-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private async Task OpeningCampaign()
    {
        bool capturedRoad = false, capturedBell = false;
        for (int i = 0; i < 9000 && !_session.Campaign.Capture().Campaign.CompletedActs.Contains(1); i++)
        {
            Execute(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(_session.Campaign)));
            if (!capturedRoad && _session.Campaign.ActiveEncounterId == "campaign.road")
            { capturedRoad = true; await Capture("hud-first-encounter.png"); }
            if (!capturedBell && _session.Campaign.ActiveEncounterId == "campaign.bell_saint")
            { capturedBell = true; await CheckLayouts("bell-saint"); }
            if (i % 120 == 0) await Frames(1);
        }
        Check("fresh_character_defeats_bell_saint_with_real_commands", _session.Campaign.Capture().Campaign.CompletedActs.Contains(1) && capturedRoad && capturedBell);
        Check("opening_progression_is_earned", _session.Production.ProgressionView.Level > 1 && _xpEarned > 0 && _earnedNotices.Contains("level"));
        Check("opening_loot_pickups_are_real_inventory_receipts", _pickups.Count > 0 && _events.GetValueOrDefault("LootPickedUp") >= _pickups.Count);
        // Exercise actual potion and dodge commands if the combat policy did not need them.
        if (_session.Combat.View.PotionCharges > 0 && _session.Combat.View.PotionCooldownTicks == 0)
            Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Potion)]));
        if (_session.Combat.View.DodgeCooldownTicks == 0)
            Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Dodge, X: 1)]));
        await Capture("hud-bell-saint-rewards.png");
        await CheckPausedTimers();
    }

    private void Execute(EndgameRuntimeCommand command)
    {
        if (_segmentCommands == 1800) { RecordReplay(); _segmentCommands = 0; }
        var before = _session.Production.ProgressionView;
        var oldLoot = _session.Combat.View.Loot.ToDictionary(l => l.Id, l => l.Item);
        var result = _session.Execute(command); _commands++; _segmentCommands++;
        if (!result.Success) throw new InvalidDataException("HUD route rejected " + command.Action + ": " + result.Reason);
        foreach (var e in result.CombatEvents)
        {
            _events[e.Kind] = _events.GetValueOrDefault(e.Kind) + 1;
            if (e.Kind == "LootPickedUp")
            {
                var item = _session.Combat.View.Inventory.FirstOrDefault(item => item.Id == e.Amount && item.DefinitionId == e.ContentId && oldLoot.ContainsKey(item.Id));
                if (item is not null) _pickups.Add(item);
            }
        }
        var after = _session.Production.ProgressionView;
        if (after.Experience > before.Experience) { _xpEarned += after.Experience - before.Experience; _earnedNotices.Add("experience"); }
        if (after.Level > before.Level) _earnedNotices.Add("level");
        if (after.MasteredSkills.Except(before.MasteredSkills).Any()) _earnedNotices.Add("mastery");
        if (after.UltimateSkills.Except(before.UltimateSkills).Any()) _earnedNotices.Add("skill");
        _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat); Refresh(); ObserveHud();
        ObserveRewardChanges(before, after, result.CombatEvents);
    }

    private void Refresh()
    {
        _sandbox.AdoptSession(_session.Combat);
        _sandbox.PresentProgression(_session.Production.ProgressionView, _session.Production.CurrentLevelExperience, _session.Combat.View.Skills,
            _session.Production.Capture().Progression.Character.UnlockedDisciplines);
        var view = _session.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
        _sandbox.SetWorldSubtitle(view.Endgame is null ? "CAMPAIGN / " + _session.Campaign.View.Region.ToUpperInvariant() : "FRACTURE / " + _session.View.Run?.Name.ToUpperInvariant());
        string key = view.Endgame?.ContextKey ?? _session.Campaign.ActiveEncounterId;
        key += ":" + view.BossPhase;
        if (_roomKey != key)
        {
            _roomKey = key;
            var campaign = _session.Campaign.Capture();
            if (view.Endgame is null)
            {
                _stage.Visible = true;
                _stage.Show(campaign.Campaign, _session.Campaign.View, _session.Room, _session.Interactions, _session.Production.View.ActiveManifestations,
                    _session.Production.ProgressionView.HubStage, player.Position, view.BossPhase, false, view, _session.Campaign.ActiveEncounterId);
            }
            else _stage.Visible = false;
            _sandbox.PresentAuthoredRoom(_session.Room, key, EnvironmentGround.Style(_session.InHub, _session.Campaign.ActiveEncounterId));
        }
        _effects.Show(view.Endgame, player.Position, false);
    }

    private void ObserveHud()
    {
        var view = _session.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
        _renderedViews++;
        _sawDamagedHealth |= player.Health < player.MaxHealth; _sawBarrier |= player.Barrier > 0;
        _sawPotionCooldown |= view.PotionCooldownTicks > 0; _sawDodgeCooldown |= view.DodgeCooldownTicks > 0;
        _sawSkillCooldown |= view.Skills.Any(s => s.RemainingTicks > 0);
        if (Find<ProgressBar>("HudHealthBar").Value != player.Health || Find<ProgressBar>("HudHealthBar").MaxValue != player.MaxHealth ||
            Find<ProgressBar>("HudResourceBar").Value != view.Resource || Find<ProgressBar>("HudResourceBar").MaxValue != view.MaxResource)
            throw new InvalidDataException("Combat vital bars differ from the actual Core view.");
        if (!Find<Label>("HudHealth").Text.Contains(player.Health + "/" + player.MaxHealth, StringComparison.Ordinal) ||
            view.Barrier > 0 && !Find<Label>("HudBarrier").Text.Contains(view.Barrier.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            throw new InvalidDataException("Combat vital labels differ from the actual Core view.");
        if (!Enumerable.Range(1, 6).All(i => Find<SkillButton>("HotbarSkill" + i).CooldownRemainingTicks == view.Skills[i - 1].RemainingTicks))
            throw new InvalidDataException("Hotbar cooldown differs from actual Core ticks.");
        var statuses = Find<CombatStatusStrip>("HudStatuses");
        var expected = (player.Health > 0 ? player.Statuses.Where(s => s.RemainingTicks > 0).Select(s => s.Id + ":" + s.SourceId) : []).ToList();
        if (player.Health > 0 && player.Barrier > 0) expected.Add("Barrier");
        if (player.Health > 0 && player.Guarded) expected.Add("Guarded");
        if (player.Health > 0 && player.Shielded) expected.Add("Shielded");
        if (!statuses.ActiveKeys.ToHashSet().SetEquals(expected)) throw new InvalidDataException("Player status strip differs from Core conditions.");
        var progression = _session.Production.ProgressionView;
        long floor = _session.Production.CurrentLevelExperience;
        if (_sandbox.ExperienceIntoLevel != progression.Experience - floor || _sandbox.ExperienceForNextLevel != progression.NextLevelExperience - floor ||
            _sandbox.ExperienceAtMaximumLevel != (progression.NextLevelExperience == floor))
            throw new InvalidDataException("Experience strip differs from actual Core level thresholds.");
        _timedActor ??= view.Actors.FirstOrDefault(a => a.Health > 0 && a.Statuses.Any(s => s.RemainingTicks > 0));
        _stackedActor ??= view.Actors.FirstOrDefault(a => a.Health > 0 && a.Statuses.Any(s => s.RemainingTicks > 0 && s.Stacks > 1));
    }

    private async Task CheckLayouts(string label)
    {
        string hash = _session.StateHash;
        foreach (var size in new[] { new Vector2I(1280, 800), new Vector2I(1000, 720), new Vector2I(780, 720) })
        {
            GetWindow().Size = GetWindow().ContentScaleSize = size; await Frames(6); Refresh(); await Frames(2);
            var viewport = GetViewport().GetVisibleRect();
            var names = new[] { "HudHealth", "HudHealthBar", "HudBarrier", "HudBarrierBar", "HudResource", "HudResourceBar", "HudPotion", "HudDodge", "HudDock", "HudExperience" };
            Check(label + "_vitals_fit_" + size.X + "x" + size.Y, names.All(name => viewport.Encloses(Find<Control>(name).GetGlobalRect())));
            var buttons = Enumerable.Range(1, 6).Select(i => Find<SkillButton>("HotbarSkill" + i)).ToArray();
            Check(label + "_six_skill_buttons_fit_without_overlap_" + size.X + "x" + size.Y, buttons.All(b => viewport.Encloses(b.GetGlobalRect())) &&
                buttons.SelectMany((a, index) => buttons.Skip(index + 1).Select(b => !a.GetGlobalRect().Intersects(b.GetGlobalRect()))).All(value => value));
            var utility = Descendants(_sandbox).OfType<Button>().Where(b => b.IsVisibleInTree() && new[] { "Inventory [I]", "Settings [Esc]", "Pause [P]" }.Contains(b.Text)).ToArray();
            var experience = Find<Control>("HudExperience");
            Check(label + "_utility_buttons_and_xp_fit_without_overlap_" + size.X + "x" + size.Y, utility.Length == 3 &&
                utility.All(b => viewport.Encloses(b.GetGlobalRect()) && !experience.GetGlobalRect().Intersects(b.GetGlobalRect())) &&
                utility.SelectMany((a, index) => utility.Skip(index + 1).Select(b => !a.GetGlobalRect().Intersects(b.GetGlobalRect()))).All(value => value) &&
                experience.GetGlobalRect().Encloses(Find<Control>("HudExperienceText").GetGlobalRect()) && experience.GetGlobalRect().Encloses(Find<Control>("HudExperienceBar").GetGlobalRect()));
            await Capture("hud-" + label + "-" + size.X + "x" + size.Y + ".png");
        }
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(6); Refresh();
        Check(label + "_resize_and_render_leave_simulation_unchanged", _session.StateHash == hash);
    }

    private async Task CheckPausedTimers()
    {
        _sandbox.SetPaused(true); await Frames();
        string hash = _session.StateHash;
        string before = TimerText();
        for (int i = 0; i < 18; i++) await Frames(1);
        Check("paused_hud_preserves_cooldowns_and_status_timers", _session.StateHash == hash && TimerText() == before && _sandbox.IsPaused);
        _sandbox.SetPaused(false);
    }

    private string TimerText() => string.Join('|', new[] { Find<Label>("HudPotion").Text, Find<Label>("HudDodge").Text, Find<CombatStatusStrip>("HudStatuses").DetailsText }
        .Concat(Enumerable.Range(1, 6).Select(i => Find<SkillButton>("HotbarSkill" + i).StateText)));

    private async Task CheckActualStatusViews()
    {
        Check("real_combat_supplies_timed_status_evidence", _timedActor is not null);
        if (_stackedActor is null) await EarnStackedPoisonEvidence();
        Check("real_combat_supplies_stacked_status_evidence", _stackedActor is not null);
        var gallery = new CombatStatusStrip { Name = "EarnedEnemyStatusEvidence", Position = new(22, 205), Size = new(600, 44) };
        _sandbox.AddOverlay(gallery);
        string hash = _session.StateHash;
        try
        {
            foreach (var (label, actor) in new[] { ("timed", _timedActor), ("stacked", _stackedActor) })
            {
                if (actor is null) continue;
                gallery.SetView(actor); await Frames();
                Check("actual_" + label + "_status_uses_exact_core_duration_and_stacks", actor.Statuses.Where(s => s.RemainingTicks > 0).All(s =>
                    gallery.ActiveKeys.Contains(s.Id + ":" + s.SourceId) && gallery.DetailsText.Contains((Math.Ceiling(s.RemainingTicks / 3d) / 10).ToString("0.0", CultureInfo.InvariantCulture) + "s", StringComparison.Ordinal) &&
                    (s.Stacks == 1 || gallery.DetailsText.Contains("×" + s.Stacks, StringComparison.Ordinal))));
                Check("actual_" + label + "_status_has_native_icon_and_text", Descendants(gallery).OfType<CombatStatusIcon>().Count(i => i.IsVisibleInTree()) == gallery.DisplayedCount &&
                    gallery.DisplayedCount + gallery.OverflowCount == gallery.ActiveCount);
                gallery.Size = new(100, 44); await Frames();
                Check("actual_" + label + "_status_narrow_projection_is_bounded", gallery.DisplayedCount <= 1 && gallery.DisplayedCount + gallery.OverflowCount == gallery.ActiveCount);
                gallery.Size = new(600, 44); await Frames(); await Capture("hud-earned-enemy-" + label + "-status.png");
            }
        }
        finally { gallery.QueueFree(); await Frames(); }
        Check("enemy_status_evidence_never_changes_live_character", _session.StateHash == hash);
    }

    private async Task EarnStackedPoisonEvidence()
    {
        // This independent authored encounter starts from an ordinary fresh Veilwalker.
        // No health, damage, equipment, enemy or status fields are edited for the example.
        string liveHash = _session.StateHash; int liveCommands = _commands;
        var character = ProductionSession.Create(_combatJson, _adventure, _progression, seed: 733, discipline: "Veilwalker");
        var combat = CombatSession.CreateEncounter(_combatJson, 733, "campaign.monastery", character.Combat.Capture());
        var recorder = new CombatRecorder(combat);
        int targetId = combat.View.Actors.Where(a => a.Faction == CombatFaction.Enemy).OrderByDescending(a => a.MaxHealth).ThenBy(a => a.Id).First().Id;
        for (int i = 0; i < 600 && _stackedActor is null; i++)
        {
            var view = combat.View; var player = view.Actors.Single(a => a.Id == 1);
            if (player.Health <= 0) break;
            var target = view.Actors.FirstOrDefault(a => a.Id == targetId && a.Health > 0) ??
                view.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).OrderByDescending(a => a.Health).ThenBy(a => a.Id).FirstOrDefault();
            if (target is null) break;
            targetId = target.Id;
            var commands = new List<CombatCommand>();
            if (player.Health < player.MaxHealth / 2 && view.PotionCharges > 0 && view.PotionCooldownTicks == 0) commands.Add(new(CombatCommandKind.Potion));
            if (CorePosition.DistanceSquared(player.Position, target.Position) > 2200L * 2200)
            {
                var direction = CombatProductionSmoke.MovementDirection(player.Position, target.Position, combat.Room,
                    view.Actors.Where(a => a.Id != player.Id && a.Id != target.Id && a.Health > 0).Select(a => a.Position).ToArray());
                commands.Add(new(CombatCommandKind.Move, X: direction.X, Z: direction.Z));
            }
            else
            {
                commands.Add(new(CombatCommandKind.Stop));
                if (view.Skills.Single(s => s.Id == "skill.venom_knife").RemainingTicks == 0)
                    commands.Add(new(CombatCommandKind.Cast, SkillId: "skill.venom_knife", TargetId: target.Id));
            }
            var events = recorder.Step(combat, commands.ToArray());
            _statusVenomCasts += events.Count(e => e.Kind == "AbilityStarted" && e.ActorId == 1 && e.ContentId == "skill.venom_knife");
            _statusPoisonApplications += events.Count(e => e.Kind == "StatusApplied" && e.ActorId == 1 && e.ContentId == "Poisoned");
            _stackedActor = combat.View.Actors.FirstOrDefault(a => a.Health > 0 && a.Statuses.Any(s => s.Id == "Poisoned" && s.SourceId == 1 && s.Stacks > 1 && s.RemainingTicks > 0));
            if (i % 60 == 0) await Frames(1);
        }
        Check("independent_veilwalker_earns_poison_stacks_by_repeated_real_casts", _stackedActor is not null && _statusVenomCasts >= 2 && _statusPoisonApplications >= 2);
        var replay = recorder.Capture(); var result = CombatReplayRunner.Run(_combatJson, replay);
        Check("independent_status_combat_replays_exactly", result.Success && result.FinalHash == combat.StateHash);
        const string file = "hud.status-poison.replay.json";
        System.IO.File.WriteAllText(Path.Combine(_output, file), JsonData.Write(replay));
        _statusCombatReplay = new(file, replay.Frames.Length, combat.StateHash);
        Check("independent_status_combat_preserves_live_character_and_command_history", _session.StateHash == liveHash && _commands == liveCommands);
    }

    private async Task EarnedEndgame()
    {
        string fixture = Read("phase4-campaign-complete");
        string previous = CampaignCombatContent.Parse(Read("combat-phase4"), Read("campaign-combat-phase4")).CombatJson;
        _session = EndgameRuntimeMigration.ImportPhaseFour(fixture, previous, _combatJson, _adventure, _progression, _campaign, _endgame);
        _segmentCommands = 0; _roomKey = ""; _sandbox.SetSession(_session.Combat); Refresh(); await Frames();
        CheckInitialRewardBaseline("imported_campaign");
        var initiallyMastered = _session.Production.ProgressionView.MasteredSkills.ToHashSet();
        for (int i = 0; i < 14000 && !(_session.InHub && _session.View.HighestClearedTier >= 2 && _session.Production.ProgressionView.MasteredSkills.Except(initiallyMastered).Any()); i++)
        {
            Execute(EndgameRuntimeSmoke.Next(_session, 2));
            if (i % 120 == 0) await Frames(1);
        }
        Check("earned_endgame_rooms_supply_real_xp_and_mastery", _session.InHub && _session.View.HighestClearedTier >= 2 &&
            _session.Production.ProgressionView.MasteredSkills.Except(initiallyMastered).Any() && _earnedNotices.Contains("mastery"));
        Check("fixture_import_does_not_change_source_archive", Read("phase4-campaign-complete") == fixture);
        await CheckLayouts("endgame-rewards");
        var retrain = new CampaignRuntimeCommand(CampaignRuntimeAction.Production, Production: new(ProductionAction.Retrain, Id: "Arcanist"));
        for (int i = 0; i < 500; i++)
        {
            var command = CampaignRuntimeSmoke.AtInteraction(_session.Campaign, "service.mara", retrain);
            Execute(new(EndgameRuntimeAction.Campaign, Campaign: command));
            if (command.Action == CampaignRuntimeAction.Production) break;
        }
        Check("real_mara_retraining_unlocks_new_discipline_skills", _session.Production.ProgressionView.Discipline == "Arcanist" &&
            _session.Production.ProgressionView.UltimateSkills.Contains("skill.starfall") && _earnedNotices.Contains("skill") &&
            _session.Combat.View.Skills.All(s => s.Available));
        await Capture("hud-earned-skill-unlock.png");
    }

    private async Task CheckSaveReset(string label)
    {
        RecordReplay();
        string path = Path.Combine(_output, "hud." + label + ".save.json");
        string hash = _session.StateHash;
        EndgameRuntimeSaveStore.Write(path, _combatJson, _adventure, _progression, _campaign, _endgame, _session.Capture());
        _session = EndgameRuntimeSaveStore.Load(path, _combatJson, _adventure, _progression, _campaign, _endgame).Session;
        _segmentCommands = 0; _sandbox.SetSession(_session.Combat); Refresh(); await Frames();
        Check(label + "_save_load_restores_exact_character_state", _session.StateHash == hash);
        CheckInitialRewardBaseline(label + "_save_load");
    }

    private void CheckInitialRewardBaseline(string label)
    {
        var feed = Find<CombatRewardFeed>("RewardFeed");
        Check(label + "_first_presentation_is_quiet", feed.AcceptedCount == 0 && feed.VisibleNotices.Count == 0 && feed.PendingCount == 0);
        long count = _sandbox.RewardNoticeCount; string hash = _session.StateHash;
        Refresh(); Refresh();
        Check(label + "_repeated_progression_projection_is_read_only", _sandbox.RewardNoticeCount == count && _session.StateHash == hash);
        Check(label + "_xp_strip_uses_authoritative_level_floor", _sandbox.ExperienceIntoLevel == _session.Production.ProgressionView.Experience - _session.Production.CurrentLevelExperience &&
            _sandbox.ExperienceForNextLevel == _session.Production.ProgressionView.NextLevelExperience - _session.Production.CurrentLevelExperience);
    }

    private void ObserveRewardChanges(ProgressionView before, ProgressionView after, IReadOnlyList<CombatEvent> events)
    {
        var feed = Find<CombatRewardFeed>("RewardFeed");
        var notices = feed.VisibleNotices.Concat(feed.PendingNotices).ToArray();
        foreach (var notice in notices) _observedRewardKinds.Add(notice.Kind);
        if (feed.VisibleNotices.Count > CombatRewardFeed.MaximumVisible || feed.PendingCount > CombatRewardFeed.MaximumPending)
            throw new InvalidDataException("Reward burst exceeded its bounded visible/pending capacity.");
        if (after.Level > before.Level && !notices.Any(n => n.Kind == "LevelUp" && n.Title.Contains(after.Level.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)))
            throw new InvalidDataException("Actual level gained without its reward notice.");
        foreach (string skill in after.MasteredSkills.Except(before.MasteredSkills))
            if (!notices.Any(n => n.Kind == "Mastery" && n.Key == "mastery:" + skill))
                throw new InvalidDataException("Actual skill mastery gained without its reward notice.");
        foreach (string skill in after.UltimateSkills.Except(before.UltimateSkills))
            if (!notices.Any(n => n.Kind == "AbilityUnlocked" && n.Key == "ability:" + skill))
                throw new InvalidDataException("Actual ultimate unlocked without its reward notice.");
        foreach (var picked in events.Where(e => e.Kind == "LootPickedUp" && e.ActorId == 1))
        {
            var item = _session.Combat.View.Inventory.Single(i => i.Id == picked.Amount);
            if (!notices.Any(n => n.Kind == "Loot" && n.DefinitionId == item.DefinitionId && n.Rarity == item.Rarity && n.Title == item.Name))
                throw new InvalidDataException("Actual loot pickup lacks its item name and rarity notice.");
        }
    }

    private async Task CheckRewardFeed()
    {
        var feed = Find<CombatRewardFeed>("RewardFeed");
        Check("actual_rewards_present_loot_level_mastery_and_new_ability_notices", new[] { "Loot", "LevelUp", "Mastery", "AbilityUnlocked" }.All(_observedRewardKinds.Contains));
        string hash = _session.StateHash; long count = _sandbox.RewardNoticeCount;
        _sandbox.SetPaused(true); await Frames();
        var paused = JsonData.Hash(feed.VisibleNotices); int pending = feed.PendingCount;
        feed.Advance(20, true); await Frames(10);
        Check("reward_notice_lifetimes_pause_with_gameplay", JsonData.Hash(feed.VisibleNotices) == paused && feed.PendingCount == pending && _session.StateHash == hash);
        Refresh(); Refresh();
        Check("reward_refresh_does_not_reannounce_earned_progress", _sandbox.RewardNoticeCount == count && _session.StateHash == hash);
        Check("actual_reward_bursts_stay_bounded", feed.AcceptedCount > CombatRewardFeed.MaximumVisible &&
            feed.VisibleNotices.Count <= CombatRewardFeed.MaximumVisible && feed.PendingCount <= CombatRewardFeed.MaximumPending);
        bool sawLootIcon = false, sawSkillIcon = false;
        for (int page = 0; page < 5 && (feed.VisibleNotices.Count > 0 || feed.PendingCount > 0); page++)
        {
            var notices = feed.VisibleNotices;
            for (int row = 0; row < notices.Count; row++)
            {
                var notice = notices[row];
                if (notice.Kind == "Loot")
                {
                    Check("loot_row_" + page + "_" + row + "_has_actual_name_rarity_and_icon", Find<Label>("RewardTitle" + row).Text == notice.Title &&
                        Find<Label>("RewardDetail" + row).Text.Contains(notice.Rarity, StringComparison.Ordinal) && Find<GearItemIcon>("RewardItemIcon" + row).IsVisibleInTree());
                    sawLootIcon = true;
                }
                if (notice.Kind is "Mastery" or "AbilityUnlocked")
                { Check("skill_reward_row_" + page + "_" + row + "_has_icon", Find<SkillIcon>("RewardSkillIcon" + row).IsVisibleInTree()); sawSkillIcon = true; }
            }
            await Capture("hud-reward-feed-page-" + page + ".png");
            feed.Advance(CombatRewardFeed.NoticeLifetime + 4, false); await Frames();
        }
        Check("reward_feed_has_actual_item_and_skill_icons", sawLootIcon && sawSkillIcon);
        Check("bounded_reward_queue_eventually_expires_without_touching_progress", feed.VisibleNotices.Count == 0 && feed.PendingCount == 0 && _session.StateHash == hash && feed.AcceptedCount == count);
        _sandbox.SetPaused(false);
    }

    private void RecordReplay()
    {
        if (_segmentCommands == 0) return;
        var replay = _session.CaptureReplay();
        var result = EndgameRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, _endgame, replay);
        string file = "hud.segment-" + (_replays.Count + 1) + ".replay.json";
        Check(file + "_matches_actual_state", result.Success && result.FinalHash == _session.StateHash && replay.Frames.Length == _segmentCommands);
        System.IO.File.WriteAllText(Path.Combine(_output, file), JsonData.Write(replay)); _replays.Add(new(file, replay.Frames.Length, result.FinalHash));
    }

    private T Find<T>(string name) where T : Node
    {
        if (!_hudNodes.TryGetValue(name, out var node)) { node = Descendants(_sandbox).OfType<T>().Single(n => n.Name == name); _hudNodes[name] = node; }
        return (T)node;
    }
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://" + name + ".json");
    private async Task Click(Control control)
    {
        var point = control.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, Pressed = pressed }, true);
        await Frames();
    }
    private async Task Frames(int count = 3) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string name)
    {
        await Frames();
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-hud") || DisplayServer.GetName() == "headless") return;
        RenderingServer.ForceDraw(false); RenderingServer.ForceSync(); using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("HUD check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "HudClientSmokePassed" : "HudClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            commands = _commands,
            renderedViews = _renderedViews,
            experienceEarned = _xpEarned,
            earnedTransitions = _earnedNotices,
            pickups = _pickups.Select(i => new { i.Id, i.Name, i.Rarity }).ToArray(),
            replaySegments = _replays,
            independentStatusReplay = _statusCombatReplay,
            statusVenomCasts = _statusVenomCasts,
            statusPoisonApplications = _statusPoisonApplications,
            events = _events,
            error,
            scope = "A fresh EndgameRuntimeSession fights the actual opening campaign through Bell Saint using public commands. A separate validated, unchanged phase-4 archive supplies the earned endgame character; ordinary Fracture combat earns additional rewards/mastery and real Mara retraining unlocks Arcanist skills. Every command is covered by exact segmented replay. The live HUD uses actual combat/progression views. Timed enemy-status evidence comes from those fights; if they produce no stacks, a separate authored Monastery encounter starts from a fresh Veilwalker and ordinary repeated Venom Knife casts earn actual poison stacks. Its independent CombatRecorder replay/counts are reported separately and no combat fields are edited. Checks cover three viewport sizes, vitals, cooldowns, icons, XP/reward receipts, bounded cosmetic queue, pause and quiet save/load baselines. No user save is accessed."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "hud-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
