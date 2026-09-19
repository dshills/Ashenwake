using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Authoring;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Real expedition rewards and native skill-screen controls exercise permanent build changes.</summary>
public partial class SkillsSmoke : Node3D
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly Dictionary<string, int> _earnedCasts = [];
    private string _output = "", _combatJson = "";
    private bool _writeReport;
    private ProductionSession _session = null!;
    private Sandbox _sandbox = null!;
    private ProductionHud _hud = null!;
    private AdventureStage _stage = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private RoomDefinition _room = null!;
    private int _commands, _requests, _expeditions;
    private ProductionAction _lastAction;
    private SkillsPanel Panel => Find<SkillsPanel>("SkillsPanel");
    private ProgressionState Character => _session.Capture().Progression.Character;
    private HashSet<string> PauseOwners => (HashSet<string>)(typeof(Sandbox).GetField("_modalPauses", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_sandbox) ?? throw new MissingFieldException("_modalPauses"));

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--skills-smoke") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Skills smoke requires --skills-smoke --output=<fresh-artifact-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _combatJson = Read("combat"); _adventure = AdventureContent.Parse(Read("adventure")); _progression = ProgressionContent.Parse(Read("progression"));
            _session = ProductionSession.Create(_combatJson, _adventure, _progression);
            _room = CombatContent.Parse(_combatJson).Room;
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson, AutomaticStep = false };
            AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetProcess(false); _sandbox.SetSession(_session.Combat); _sandbox.SetPaused(true);
            _stage = new AdventureStage(); AddChild(_stage);
            _hud = new ProductionHud { Catalog = TextCatalog.Parse(Read("text.en")) }; _sandbox.AddOverlay(_hud);
            _hud.PassiveRequested += id => CommitRequested(new(ProductionAction.Passive, Id: id));
            _hud.RespecRequested += () => CommitRequested(new(ProductionAction.Respec));
            _hud.MutationRequested += (skill, mutation) => CommitRequested(new(ProductionAction.Mutation, Id: skill, Value: mutation));
            WalkTo("service.mara"); Refresh(); await Frames(8);
            await OpenSkills(); await InitialGates();
            await EarnBuild();
            Refresh(); await OpenSkills(); await BuildFlow();
            await SaveReplay();
            await Capture("skills-combat-bar.png");
            await CheckSkillVisuals();
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("skills-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private void CommitRequested(ProductionCommand command)
    {
        _requests++; _lastAction = command.Action;
        var result = _session.Execute(command); _commands++;
        if (result.Success) Refresh();
        _hud.ReportBuildResult(result.Success, result.Reason);
        if (!result.Success) throw new InvalidDataException("Skill UI submitted rejected build: " + result.Reason);
    }

    private async Task InitialGates()
    {
        string hash = _session.StateHash; int history = _session.CaptureReplay().Frames.Length;
        Check("six_discipline_skills_are_inspectable", Enumerable.Range(0, 6).All(i => Find<Button>("SkillCard" + i).IsVisibleInTree()));
        await Click(Find<Button>("SkillCard5"));
        Check("fresh_character_ultimate_is_locked", !_session.Combat.View.Skills[5].Available &&
            Find<Button>("SkillCard5").Text.Contains("10", StringComparison.Ordinal));
        await Click(Find<Button>("SkillCard0")); await Click(Find<Button>("SkillMutation1"));
        Check("unmastered_variant_is_inspectable_but_cannot_apply", Panel.SelectedMutationId == "mutation.reaping_arc" && Find<Button>("SkillApplyMutation").Disabled &&
            Find<Label>("SkillStatus").Text.Contains("master", StringComparison.OrdinalIgnoreCase));
        await Click(Find<Button>("SkillPassiveOffense"));
        Check("fresh_character_cannot_allocate_unearned_points", _session.ProgressionView.AvailablePassivePoints == 0 && Find<Button>("SkillApplyPassive").Disabled);
        Check("locked_inspection_preserves_state_history_and_requests", _session.StateHash == hash && _session.CaptureReplay().Frames.Length == history && _requests == 0);
        Check("skills_screen_holds_its_own_pause", PauseOwners.Contains("skills-panel") && PauseOwners.Contains("session") && _sandbox.IsPaused);
        long tick = _session.Combat.Tick;
        KeyPress(Key.F); KeyPress(Key.Period); await Frames();
        Check("modal_interact_and_single_step_do_not_reach_gameplay", _session.StateHash == hash && _session.Combat.Tick == tick && _session.CaptureReplay().Frames.Length == history);
        await Capture("skills-locked-character.png");
        await ClickText("Close character");
        Check("closing_skills_releases_only_its_pause", !Panel.IsVisibleInTree() && !PauseOwners.Contains("skills-panel") && PauseOwners.Contains("session") && _sandbox.IsPaused);
    }

    private async Task EarnBuild()
    {
        for (int i = 0; i < ProductionSmoke.MaximumCommands && !ProductionSmoke.Complete(_session); i++)
        {
            Execute(ProductionSmoke.Next(_session));
            if (i % 180 == 0) await Frames(1);
        }
        Check("first_real_expedition_returns_earned_points", ProductionSmoke.Complete(_session) && _session.ProgressionView.AvailablePassivePoints >= 3);
        _expeditions = 1;
        while (Character.Mastery.GetValueOrDefault("skill.cleave") < 100 && _expeditions < 6)
        {
            WalkTo("dungeon.replay"); Execute(new(ProductionAction.Expedition, new(ExpeditionAction.Interact, "dungeon.replay")));
            for (int i = 0; i < ProductionSmoke.MaximumCommands && !CurrentExpeditionRewarded(); i++)
            {
                Execute(RepeatExpeditionCommand());
                if (i % 180 == 0) await Frames(1);
            }
            if (!CurrentExpeditionRewarded()) throw new InvalidDataException("Repeated authored expedition exceeded its command bound.");
            Execute(new(ProductionAction.Expedition, new(ExpeditionAction.Travel, "room.greyhaven")));
            WalkTo("npc.mara"); Execute(new(ProductionAction.Expedition, new(ExpeditionAction.Interact, "npc.mara")));
            _expeditions++;
        }
        WalkTo("service.mara");
        Check("mutation_mastery_earned_by_real_successful_casts", Character.Mastery.GetValueOrDefault("skill.cleave") >= 100 &&
            Character.Mastery.All(pair => pair.Value == Math.Min(1000, _earnedCasts.GetValueOrDefault(pair.Key))));
        Check("earned_mutation_projects_into_combat_unlocks", _session.Combat.ProgressionBuild.UnlockedMutations?.Contains("mutation.reaping_arc") == true);
        Check("earned_character_remains_alive_at_mara", _session.View.RoomId == "room.greyhaven" && _session.Combat.View.Actors.Single(a => a.Id == 1).Health > 0);
    }

    private bool CurrentExpeditionRewarded()
    {
        var state = _session.Capture().Expedition.Adventure;
        return state.RewardedExpedition == state.Expedition;
    }

    private ProductionCommand RepeatExpeditionCommand()
    {
        var view = _session.View;
        if (view.RoomId == "room.greyhaven") return new(ProductionAction.Expedition, new(ExpeditionAction.Travel, "room.ossuary"));
        if (view.EncounterId is not null) return new(ProductionAction.Expedition, new(ExpeditionAction.Tick, Commands: CombatProductionSmoke.Commands(_session.Combat.View)));
        var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        var loot = _session.Combat.View.Loot.OrderBy(l => CorePosition.DistanceSquared(l.Position, player.Position)).ThenBy(l => l.Id).FirstOrDefault();
        if (loot is not null) return new(ProductionAction.Expedition, new(ExpeditionAction.Tick, Commands:
            [new(CombatCommandKind.Move, X: Math.Sign(loot.Position.X - player.Position.X), Z: Math.Sign(loot.Position.Z - player.Position.Z)), new(CombatCommandKind.Pickup, ItemId: loot.Id)]));
        return new(ProductionAction.Expedition, new(ExpeditionAction.Travel, view.RoomId == "room.ossuary" ? "room.cloister" : "room.bell_sanctum"));
    }

    private async Task BuildFlow()
    {
        WalkTo("service.torren"); Refresh(); await Click(Find<Button>("SkillPassiveOffense"));
        Check("away_from_mara_inspection_cannot_apply", Panel.Preview is { Success: true } && Find<Button>("SkillApplyPassive").Disabled && Find<Label>("SkillStatus").Text.Contains("Mara", StringComparison.OrdinalIgnoreCase));
        WalkTo("service.mara"); Refresh(); await Frames();
        await CheckLayouts();
        foreach (string passive in new[] { "Offense", "Defense", "Resource" })
        {
            await Click(Find<Button>("SkillPassive" + passive));
            await Apply("passive_" + passive, ProductionAction.Passive, "SkillApplyPassive");
        }
        Check("all_three_passive_choices_apply_to_earned_character", new[] { "Offense", "Defense", "Resource" }.All(id => Character.Passives.GetValueOrDefault(id) == 1));
        await Click(Find<Button>("SkillRespec")); await CancelRespec(); await InvalidateRespec();
        await Click(Find<Button>("SkillRespec"));
        int materials = Character.Materials, points = _session.ProgressionView.AvailablePassivePoints, spent = Character.Passives.Values.Sum();
        await Apply("respec", ProductionAction.Respec, "SkillApplyRespec");
        Check("respec_refunds_all_points_and_charges_exact_material_cost", Character.Passives.Count == 0 &&
            _session.ProgressionView.AvailablePassivePoints == points + spent && materials - Character.Materials == _session.Content.Capture().RespecCost);
        await Click(Find<Button>("SkillCard0")); await Click(Find<Button>("SkillMutation1"));
        Check("mastered_variant_can_be_applied", Panel.SelectedSkillId == "skill.cleave" && Panel.SelectedMutationId == "mutation.reaping_arc" && !Find<Button>("SkillApplyMutation").Disabled);
        await Apply("mutation_select", ProductionAction.Mutation, "SkillApplyMutation");
        Check("selected_mutation_reaches_authoritative_combat_skill", Character.SelectedMutations.GetValueOrDefault("skill.cleave") == "mutation.reaping_arc" &&
            _session.Combat.View.Skills.Single(s => s.Id == "skill.cleave").Mutation == "mutation.reaping_arc");
        await Click(Find<Button>("SkillMutation0")); await Apply("mutation_remove", ProductionAction.Mutation, "SkillApplyMutation");
        Check("base_variant_removes_selected_mutation_and_restores_combat", !Character.SelectedMutations.ContainsKey("skill.cleave") && _session.Combat.View.Skills.Single(s => s.Id == "skill.cleave").Mutation == "");
        await Click(Find<Button>("SkillMutation1")); await Apply("mutation_reselect", ProductionAction.Mutation, "SkillApplyMutation");
        await Capture("skills-mastered-build.png");
        await ClickText("Close character");
    }

    private async Task Apply(string label, ProductionAction expectedAction, string buttonName)
    {
        var preview = Panel.Preview ?? throw new InvalidDataException("Missing build preview.");
        string before = _session.StateHash, appearance = _sandbox.CurrentAppearance.Key;
        int history = _session.CaptureReplay().Frames.Length, requests = _requests;
        Check(label + "_preview_is_authoritative_and_read_only", preview.Success && JsonData.Hash(preview.Before) == JsonData.Hash(_session.Capture().Progression) && Panel.PreviewText.Length > 0);
        await Capture("skills-preview-" + label + ".png");
        if (_session.StateHash != before || _session.CaptureReplay().Frames.Length != history || _requests != requests || _sandbox.CurrentAppearance.Key != appearance)
            throw new InvalidDataException("Build preview or render changed live state.");
        await Click(Find<Button>(buttonName));
        if (expectedAction == ProductionAction.Respec)
        {
            var dialog = Find<ConfirmationDialog>("SkillConfirmRespec");
            Check("respec_waits_for_explicit_confirmation", dialog.Visible && _session.StateHash == before && _requests == requests);
            Check("respec_confirmation_fits_viewport", dialog.Size.X <= GetViewport().GetVisibleRect().Size.X && dialog.Size.Y <= GetViewport().GetVisibleRect().Size.Y);
            await Capture("skills-respec-confirmation.png");
            // Native popup windows do not receive synthetic headless viewport keys.
            // The real Apply click opens the dialog; its public signal exercises confirmation.
            dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); dialog.Hide(); await Frames();
        }
        Check(label + "_commits_once_with_exact_preview", _requests == requests + 1 && _lastAction == expectedAction &&
            EquivalentBuild(_session.Capture().Progression, preview.After));
        Check(label + "_reports_authoritative_result", Find<Label>("SkillResult").IsVisibleInTree() && Find<Label>("SkillResult").Text.Length > 0);
    }

    private static bool EquivalentBuild(ProgressionSnapshot actual, ProgressionSnapshot expected)
    {
        // Preview and command receipts intentionally use different operation identities.
        var left = JsonData.Copy(actual); var right = JsonData.Copy(expected);
        left.Character.OperationReceipts.Clear(); right.Character.OperationReceipts.Clear();
        return JsonData.Hash(left) == JsonData.Hash(right);
    }

    private async Task CancelRespec()
    {
        string hash = _session.StateHash; int requests = _requests, history = _session.CaptureReplay().Frames.Length;
        await Click(Find<Button>("SkillApplyRespec"));
        var dialog = Find<ConfirmationDialog>("SkillConfirmRespec");
        Check("respec_cancel_starts_from_visible_dialog", dialog.Visible);
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); dialog.Hide(); await Frames();
        Check("respec_cancel_preserves_points_materials_and_history", _session.StateHash == hash && _requests == requests && _session.CaptureReplay().Frames.Length == history && PauseOwners.Contains("skills-panel"));
    }

    private async Task InvalidateRespec()
    {
        await Click(Find<Button>("SkillRespec")); await Click(Find<Button>("SkillApplyRespec"));
        var dialog = Find<ConfirmationDialog>("SkillConfirmRespec");
        string hash = _session.StateHash; int requests = _requests;
        Panel.SelectPassive("Defense"); await Frames();
        bool canceled = !dialog.Visible;
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Frames();
        Check("selection_change_invalidates_pending_respec", canceled && _session.StateHash == hash && _requests == requests);
        await Click(Find<Button>("SkillRespec")); await Click(Find<Button>("SkillApplyRespec"));
        var beforeRestoreReplay = _session.CaptureReplay();
        Check("allocations_and_earned_mastery_replay_before_restore", ProductionReplayRunner.Run(_combatJson, _adventure, _progression, beforeRestoreReplay).Success);
        System.IO.File.WriteAllText(Path.Combine(_output, "skills.before-restore.replay.json"), JsonData.Write(beforeRestoreReplay));
        _session = ProductionSession.Restore(_combatJson, _adventure, _progression, _session.Capture());
        _sandbox.SetSession(_session.Combat); Refresh(); await Frames();
        canceled = !dialog.Visible;
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Frames();
        Check("session_restore_invalidates_respec_and_reacquires_pause", canceled && _session.StateHash == hash && _requests == requests && PauseOwners.Contains("skills-panel") && PauseOwners.Contains("session") && _sandbox.IsPaused);
    }

    private async Task CheckLayouts()
    {
        foreach (var size in new[] { new Vector2I(1280, 800), new Vector2I(1280, 720), new Vector2I(780, 800) })
        {
            GetWindow().Size = GetWindow().ContentScaleSize = size; await Frames(5);
            await Reveal(Find<Button>("SkillApplyPassive"));
            var close = Descendants(_hud).OfType<Button>().Single(b => b.Text == "Close character");
            Check("skills_controls_fit_" + size.X + "x" + size.Y, GetViewport().GetVisibleRect().Encloses(close.GetGlobalRect()) && GetViewport().GetVisibleRect().Encloses(Find<Button>("SkillApplyPassive").GetGlobalRect()));
            await Capture("skills-layout-" + size.X + "x" + size.Y + ".png");
        }
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(5);
    }

    private async Task SaveReplay()
    {
        string path = Path.Combine(_output, "skills.save.json");
        ProductionSaveStore.Write(path, _combatJson, _adventure, _progression, _session.Capture());
        var loaded = ProductionSaveStore.Load(path, _combatJson, _adventure, _progression).Session;
        Check("skills_save_restores_earned_mastery_points_and_mutation", loaded.StateHash == _session.StateHash);
        var replay = _session.CaptureReplay();
        Check("skills_commands_replay_exactly", ProductionReplayRunner.Run(_combatJson, _adventure, _progression, replay).Success);
        System.IO.File.WriteAllText(Path.Combine(_output, "skills.replay.json"), JsonData.Write(replay)); await Frames();
    }

    private void Refresh()
    {
        _sandbox.AdoptSession(_session.Combat); _sandbox.SetPaused(true);
        var snapshot = _session.Capture(); var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        var interactions = _session.Interactions.Select(i => new InteractionDisplay(i.ActionId, i.Name, (int)Math.Sqrt(CorePosition.DistanceSquared(i.Position, player.Position)), i.Range)).ToArray();
        _hud.SetView(_session.ProgressionView, snapshot.Progression, _session.Content.Capture(), _session.Combat.View, interactions,
            _session.View.RoomId == "room.greyhaven", snapshot.OperationSequence, _session.Combat.ProgressionBuild.UnlockedMutations);
        _hud.SetAppearance(CharacterAppearance.FromProgression(snapshot.Progression, _session.View.ActiveManifestations, snapshot.Expedition.Adventure.Anatomy.Values));
        _stage.ShowRoom(_session.View.RoomId, _session.View.BellPhase, _room, _session.View.ActiveManifestations,
            _session.Interactions.ToDictionary(i => i.ActionId, i => i.Position), snapshot.Expedition.Adventure.DestroyedAnchors, _session.ProgressionView.HubStage);
        _sandbox.PresentAuthoredRoom(_room, _session.View.RoomId, EnvironmentGround.Style(_session.View.RoomId == "room.greyhaven", _session.View.RoomId));
    }

    private void WalkTo(string service)
    {
        var action = new ProductionCommand(ProductionAction.Expedition, new(ExpeditionAction.Interact, service));
        for (int i = 0; i < 360; i++)
        {
            var command = ProductionSmoke.AtInteraction(_session, service, action);
            if (command == action) { Execute(new(ProductionAction.Expedition, new(ExpeditionAction.Tick, Commands: [new(CombatCommandKind.Stop)]))); return; }
            Execute(command);
        }
        throw new InvalidDataException("Could not reach build specialist: " + service);
    }

    private void Execute(ProductionCommand command)
    {
        var result = _session.Execute(command); _commands++;
        if (!result.Success) throw new InvalidDataException(result.Reason);
        foreach (var started in result.CombatEvents.Where(e => e.Kind == "AbilityStarted" && e.ActorId == 1))
            _earnedCasts[started.ContentId] = _earnedCasts.GetValueOrDefault(started.ContentId) + 1;
    }

    private async Task OpenSkills() { _hud.Toggle(); await Frames(); await ClickText("Skills"); }
    private void KeyPress(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
    }
    private async Task ClickText(string text) => await Click(Descendants(_hud).OfType<Button>().Single(b => b.Text == text && b.IsVisibleInTree()));
    private async Task Click(Control control)
    {
        await Reveal(control); var point = control.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, Pressed = pressed }, true);
        await Frames();
    }
    private async Task Reveal(Control control)
    {
        if (!control.IsVisibleInTree()) throw new InvalidDataException("Skill control is hidden: " + control.Name);
        for (Node? ancestor = control.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is not ScrollContainer scroll) continue;
            for (int i = 0; i < 120 && !ContainsVertically(scroll, control); i++)
            {
                var direction = control.GetGlobalRect().Position.Y < scroll.GetGlobalRect().Position.Y ? MouseButton.WheelUp : MouseButton.WheelDown;
                foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = direction, Position = scroll.GetGlobalRect().GetCenter(), Pressed = pressed }, true);
                await Frames(1);
            }
            if (!ContainsVertically(scroll, control)) throw new InvalidDataException("Cannot reveal skill control: " + control.Name);
        }
        if (!GetViewport().GetVisibleRect().Encloses(control.GetGlobalRect())) throw new InvalidDataException("Skill control is outside viewport: " + control.Name);
    }
    private static bool ContainsVertically(Control parent, Control child) => child.GetGlobalRect().Position.Y >= parent.GetGlobalRect().Position.Y - 1 && child.GetGlobalRect().End.Y <= parent.GetGlobalRect().End.Y + 1;
    private T Find<T>(string name) where T : Node => Descendants(_hud).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://" + name + ".json");
    private async Task Frames(int count = 3) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string name)
    {
        await Frames();
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-skills") || DisplayServer.GetName() == "headless") return;
        RenderingServer.ForceDraw(false); RenderingServer.ForceSync(); using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Skills check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "SkillsClientSmokePassed" : "SkillsClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            commands = _commands,
            requests = _requests,
            expeditions = _expeditions,
            earnedCasts = _earnedCasts,
            error,
            scope = "A fresh ProductionSession completes authored expeditions with ordinary combat, pickup, travel and replay commands. AbilityStarted events account for earned mastery; no experience, mastery or equipment is fabricated. Native viewport buttons select skill and passive previews, allocate each passive, reset points, and select/remove a mastered mutation. Actual detached Core previews are compared with authoritative commits, costs, refunds and save/replay. Dialog cancel/confirm uses the public native-window signal after a viewport Apply click because synthetic headless input does not route popup keyboard events. Pause, modal keys, selection/session invalidation, locked/away states and three viewport sizes are checked. A separate UI fixture gallery checks all thirty icons and native hotbar state/dispatch; its Heat-cap example passes a max-resource argument only to the UI adapter. Gallery fixtures never change earned character state or history."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "skills-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
