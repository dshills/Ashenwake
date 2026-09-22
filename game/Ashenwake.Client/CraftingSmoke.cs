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

/// <summary>Earned items, specialist visits and real UI requests validate the crafting workbench.</summary>
public partial class CraftingSmoke : Node3D
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly HashSet<CraftingService> _committed = [];
    private string _output = "", _combatJson = "";
    private bool _writeReport;
    private ProductionSession _session = null!;
    private Sandbox _sandbox = null!;
    private ProductionHud _hud = null!;
    private AdventureStage _stage = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private RoomDefinition _room = null!;
    private CraftingRequest? _lastRequest;
    private int _commands, _craftCalls, _dragGestures, _unhandledGameplay;
    private long _workingItem;
    private CraftingWorkbench Workbench => Find<CraftingWorkbench>("CraftingWorkbench");
    private ProgressionState Character => _session.Capture().Progression.Character;
    private HashSet<string> PauseOwners => (HashSet<string>)(typeof(Sandbox).GetField("_modalPauses", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_sandbox) ?? throw new MissingFieldException("_modalPauses"));

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--crafting-smoke") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Crafting smoke requires --crafting-smoke --output=<fresh-artifact-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _combatJson = Read("combat"); _adventure = AdventureContent.Parse(Read("adventure")); _progression = ProgressionContent.Parse(Read("progression"));
            _session = ProductionSession.Create(_combatJson, _adventure, _progression);
            _room = CombatContent.Parse(_combatJson).Room;
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson, AutomaticStep = false };
            AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetProcess(false); _sandbox.SetSession(_session.Combat); _sandbox.SetPaused(true);
            _stage = new AdventureStage(); AddChild(_stage);
            _hud = new ProductionHud { Catalog = TextCatalog.Parse(Read("text.en")) }; _sandbox.AddOverlay(_hud);
            _hud.CraftRequested += request =>
            {
                _craftCalls++; _lastRequest = request;
                var result = _session.Craft(request); _commands++;
                if (result.Success) { _committed.Add(request.Service); Refresh(); }
                _hud.ReportCraftResult(result.Success, result.Reason);
                if (!result.Success) throw new InvalidDataException("UI submitted rejected craft: " + result.Reason);
            };
            Refresh(); await Frames(8);
            _hud.Toggle(); await Frames(); await ClickText("Craft");
            await InitialGates();
            await EarnSpecialists();
            await WorkbenchFlow();
            await SaveReplay("crafted-character");
            await InsufficientMaterialsBranch();
            Check("five_services_committed_through_authoritative_ui_requests", _committed.SetEquals(new[] { CraftingService.Tempering, CraftingService.Rebinding, CraftingService.Engraving, CraftingService.Purification, CraftingService.Extraction }));
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("crafting-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private async Task InitialGates()
    {
        string hash = _session.StateHash; int history = _session.CaptureReplay().Frames.Length;
        await Service(CraftingService.Tempering);
        Check("locked_service_explains_unlock_and_disables_commit", Find<Button>("CraftCommit").Disabled && Text().Contains("locked", StringComparison.OrdinalIgnoreCase));
        Check("locked_service_inspection_preserves_core_and_history", _session.StateHash == hash && _session.CaptureReplay().Frames.Length == history && _craftCalls == 0);
        Check("workbench_owns_pause_without_releasing_session_owner", Workbench.IsVisibleInTree() && PauseOwners.Contains("crafting-workbench") && PauseOwners.Contains("session") && _sandbox.IsPaused);
        await ModalInput();
        await Capture("crafting-locked-service.png");
        _sandbox.SetSession(_session.Combat); Refresh(); await Frames();
        Check("visible_workbench_reacquires_pause_after_session_restore", Workbench.IsVisibleInTree() && PauseOwners.Contains("crafting-workbench") && PauseOwners.Contains("session") && _sandbox.IsPaused);
        await ClickText("Close character");
        Check("closing_workbench_releases_only_its_pause", !Workbench.IsVisibleInTree() && !PauseOwners.Contains("crafting-workbench") && PauseOwners.Contains("session") && _sandbox.IsPaused);
        await KeyPress(Key.F);
        Check("closed_workbench_allows_interaction_input_to_reach_game", _unhandledGameplay == 1);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, PhysicalKeycode: Key.F or Key.Period }) _unhandledGameplay++;
    }

    private async Task ModalInput()
    {
        string hash = _session.StateHash;
        await KeyPress(Key.F); await KeyPress(Key.Period);
        Check("workbench_blocks_interaction_and_single_step", _unhandledGameplay == 0 && _session.StateHash == hash);
        var search = Find<LineEdit>("CraftSearch"); search.GrabFocus();
        await KeyPress(Key.C, 'c'); await KeyPress(Key.F, 'f'); await KeyPress(Key.Period, '.');
        Check("search_accepts_game_shortcut_letters_without_world_input", search.Text == "cf." && _unhandledGameplay == 0 && _session.StateHash == hash);
        search.Text = ""; search.ReleaseFocus(); await Frames();
        var camera = Descendants(_sandbox).OfType<Camera3D>().First(c => c.GetViewport() == GetViewport());
        float size = camera.Size;
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { Position = new(1200, 30), ButtonIndex = MouseButton.WheelDown, Pressed = pressed }, true);
        await Frames();
        Check("workbench_backdrop_blocks_world_zoom", camera.Size == size);
    }

    private async Task KeyPress(Key key, char character = '\0')
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Unicode = character, Pressed = pressed }, true);
        await Frames();
    }

    private async Task EarnSpecialists()
    {
        for (int i = 0; i < ProductionSmoke.MaximumCommands && !ProductionSmoke.Complete(_session); i++)
        {
            Execute(ProductionSmoke.Next(_session));
            if (i % 180 == 0) await Frames(1);
        }
        Check("real_dungeon_returns_rewarded_character_to_mara", ProductionSmoke.Complete(_session) && _session.View.RoomId == "room.greyhaven");
        foreach (string specialist in new[] { "npc.torren", "npc.cael", "npc.oris", "npc.kesh", "hub.workshops" })
        {
            WalkTo(specialist); Require(_session.Interact(specialist)); _commands++;
        }
        Refresh();
        Check("all_six_services_unlocked_through_real_specialist_visits", Character.Services.SetEquals(Enum.GetValues<CraftingService>()));
        Check("extraction_target_was_earned_from_kesh", Character.Items.Any(item => item.Rarity == ItemRarity.Legendary && item.DefinitionId == "item.echo_ring"));
        _workingItem = Character.Items.OrderBy(item => item.Id)
            .FirstOrDefault(item => PreviewRecipe(CraftingService.Tempering, item.Id) is not null && PreviewRecipe(CraftingService.Rebinding, item.Id) is not null)?.Id
            ?? throw new InvalidDataException("Earned inventory has no item with valid Core tempering and rebinding previews.");
        Check("earned_working_item_has_core_validated_temper_and_rebind_recipes", _workingItem > 0);
    }

    private async Task WorkbenchFlow()
    {
        WalkTo("npc.mara"); Refresh(); _hud.PresentInteraction("ServiceOpened:npc.torren"); await Frames();
        await Service(CraftingService.Tempering); Workbench.SelectItem(_workingItem); await Frames();
        Check("away_service_explains_specialist_and_disables_commit", Find<Button>("CraftCommit").Disabled &&
            (Text().Contains("Torren", StringComparison.OrdinalIgnoreCase) || Text().Contains("specialist", StringComparison.OrdinalIgnoreCase)));
        WalkTo("service.torren"); Refresh(); await Frames();
        var content = _session.Content.Capture();
        await SelectItemByDrag(_workingItem);
        string affix = PreviewRecipe(CraftingService.Tempering, _workingItem)?.AffixId ?? throw new InvalidDataException("Earned item has no legal tempering preview.");
        await SelectChoice("CraftExistingAffix", affix);
        Check("eligible_tempering_preview_is_ready", Workbench.Preview is { Success: true } && !Find<Button>("CraftCommit").Disabled);
        await CheckLayouts();
        await Capture("crafting-tempering-preview.png");
        await Commit(CraftingService.Tempering);

        WalkTo("npc.oris"); Refresh(); await Service(CraftingService.Rebinding); Workbench.SelectItem(_workingItem); await Frames();
        var recipe = RebindingRecipe(_workingItem);
        await SelectChoice("CraftExistingAffix", recipe.AffixId); await SelectChoice("CraftReplacementAffix", recipe.ReplacementId);
        await Capture("crafting-rebinding-preview.png");
        await Commit(CraftingService.Rebinding);

        WalkTo("hub.workshops"); Refresh(); await Service(CraftingService.Engraving); Workbench.SelectItem(_workingItem); await Frames();
        await SelectChoice("CraftProperty", "rune.guard");
        await Commit(CraftingService.Engraving);
        Check("engraving_persists_on_selected_earned_item", Item(_workingItem).Engraving == "rune.guard");

        WalkTo("npc.cael"); Refresh(); await Service(CraftingService.Purification);
        string fragment = Character.OwnedFragments.First(id => !Character.PurifiedFragments.Contains(id));
        await SelectChoice("CraftFragment", fragment);
        await Capture("crafting-purification-preview.png");
        await Commit(CraftingService.Purification);
        Check("purification_persists_for_selected_owned_fragment", Character.PurifiedFragments.Contains(fragment));

        WalkTo("service.mara"); Refresh(); await Service(CraftingService.DivineGrafting);
        long godwrought = Character.Items.Single(i => i.DefinitionId == "item.ashcleaver").Id;
        Workbench.SelectItem(godwrought); await Frames(); await SelectChoice("CraftLineage", "Serath");
        string graftHash = _session.StateHash; int calls = _craftCalls;
        Check("unawakened_godwrought_explains_grafting_prerequisite", !Item(godwrought).Awakened && Find<Button>("CraftCommit").Disabled && Text().Contains("awaken", StringComparison.OrdinalIgnoreCase));
        await Capture("crafting-graft-prerequisite.png");
        Check("grafting_inspection_spends_nothing", _session.StateHash == graftHash && _craftCalls == calls);

        long legendary = Character.Items.Single(i => i.Rarity == ItemRarity.Legendary && i.DefinitionId == "item.echo_ring").Id;
        WalkTo("service.torren");
        var legendarySlot = content.Items.Single(d => d.Id == Item(legendary).DefinitionId).Slots.First();
        long previous = Character.Equipment.GetValueOrDefault(legendarySlot);
        Execute(new(ProductionAction.Equip, ItemId: legendary, Slot: legendarySlot));
        Execute(new(ProductionAction.SaveEquipmentPreset, Id: "preset.1", Value: "Kesh's Last Echo"));
        Execute(new(ProductionAction.Unequip, Slot: legendarySlot));
        if (previous != 0 && previous != legendary) Execute(new(ProductionAction.Equip, ItemId: previous, Slot: legendarySlot));
        Check("earned_legendary_recorded_in_outfit", _session.EquipmentPresets.Single().Equipment.Values.Contains(legendary));
        WalkTo("npc.kesh"); Refresh(); await Service(CraftingService.Extraction);
        Workbench.SelectItem(legendary); await Frames();
        foreach (var action in new[] { ProductionAction.SetItemFavorite, ProductionAction.SetItemLocked })
        {
            Execute(new(action, ItemId: legendary, Value: "true")); Refresh(); await Frames();
            Check("protected_extraction_preview_blocked_" + action, Workbench.Preview is { Success: false } && Find<Button>("CraftCommit").Disabled && Text().Contains("protect", StringComparison.OrdinalIgnoreCase));
            string protectedHash = _session.StateHash;
            var rejected = _session.Craft(new("", CraftingService.Extraction, legendary, ConfirmPermanent: true)); _commands++;
            Check("protected_extraction_atomic_" + action, !rejected.Success && _session.StateHash == protectedHash && Character.Items.Any(i => i.Id == legendary));
            await SaveReplay("protected-extraction-" + action);
            Execute(new(action, ItemId: legendary, Value: "false")); Refresh(); await Frames();
        }
        Check("extraction_preview_names_saved_outfit", Workbench.PreviewText.Contains("Kesh's Last Echo", StringComparison.Ordinal));
        Check("extraction_preview_discloses_permanent_destruction", Workbench.Preview is { Success: true, RequiresConfirmation: true } &&
            (Text().Contains("destroy", StringComparison.OrdinalIgnoreCase) || Text().Contains("consum", StringComparison.OrdinalIgnoreCase)));
        await CancelExtraction();
        await StaleConfirmation(legendary);
        await Commit(CraftingService.Extraction);
        Check("extraction_removes_exact_legendary_and_learns_real_property", !Character.Items.Any(i => i.Id == legendary) && Character.PropertyLibrary.Contains("property.summon_burst"));
        Check("crafting_keeps_owner_character_alive", _session.Combat.View.Actors.Single(a => a.Id == 1).Health > 0);
    }

    private async Task Commit(CraftingService service)
    {
        var preview = Workbench.Preview ?? throw new InvalidDataException("Missing crafting preview.");
        Check("preview_matches_current_authoritative_state_" + service, preview.Success && JsonData.Hash(preview.Before) == JsonData.Hash(_session.Capture().Progression));
        var before = _session.Capture().Progression; int calls = _craftCalls;
        await Click(Find<Button>("CraftCommit"));
        var confirmation = Find<ConfirmationDialog>("CraftConfirmation");
        if (preview.RequiresConfirmation)
        {
            Check("destructive_craft_waits_for_confirmation_" + service, confirmation.Visible && _craftCalls == calls && JsonData.Hash(before) == JsonData.Hash(_session.Capture().Progression));
            if (service == CraftingService.Extraction)
                Check("extraction_confirmation_names_saved_outfit", confirmation.DialogText.Contains("Kesh's Last Echo", StringComparison.Ordinal));
            Check("permanent_confirmation_shows_exact_selected_item_and_materials_" + service,
                confirmation.DialogText.Contains("#" + Workbench.SelectedItemId, StringComparison.Ordinal) &&
                confirmation.DialogText.Contains($"Materials: {preview.Before.Character.Materials} → {preview.After.Character.Materials}", StringComparison.Ordinal));
            await Capture("crafting-confirm-" + service.ToString().ToLowerInvariant() + ".png");
            // Modal button keyboard dispatch is a window boundary in Godot headless runs.
            // Exercise the native dialog's public confirmation signal after real commit input.
            confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed); confirmation.Hide(); await Frames();
        }
        var after = _session.Capture().Progression;
        Check("ui_submits_one_authoritative_recipe_" + service, _craftCalls == calls + 1 && _lastRequest?.Service == service);
        Check("exact_material_cost_matches_preview_" + service, before.Character.Materials - after.Character.Materials == preview.Before.Character.Materials - preview.After.Character.Materials &&
            before.Character.Materials > after.Character.Materials);
        Check("committed_items_and_permanent_effects_match_preview_" + service, JsonData.Hash(after.Character.Items) == JsonData.Hash(preview.After.Character.Items) &&
            JsonData.Hash(after.Character.Equipment) == JsonData.Hash(preview.After.Character.Equipment) && after.Character.PropertyLibrary.SetEquals(preview.After.Character.PropertyLibrary) &&
            after.Character.PurifiedFragments.SetEquals(preview.After.Character.PurifiedFragments));
        Check("success_feedback_follows_authoritative_commit_" + service, Find<Label>("CraftResult").IsVisibleInTree() && Find<Label>("CraftResult").Text.Contains(service.ToString(), StringComparison.OrdinalIgnoreCase) &&
            Find<Label>("CraftResult").Text.Contains("complete", StringComparison.OrdinalIgnoreCase));
        await Capture("crafting-result-" + service.ToString().ToLowerInvariant() + ".png");
    }

    private async Task CancelExtraction()
    {
        string hash = _session.StateHash; int calls = _craftCalls, history = _session.CaptureReplay().Frames.Length;
        await Click(Find<Button>("CraftCommit"));
        var confirmation = Find<ConfirmationDialog>("CraftConfirmation");
        Check("extraction_opens_native_confirmation_without_mutation", confirmation.Visible && _session.StateHash == hash && _craftCalls == calls);
        confirmation.EmitSignal(ConfirmationDialog.SignalName.Canceled); confirmation.Hide(); await Frames();
        Check("cancelled_extraction_keeps_item_materials_and_history", _session.StateHash == hash && _craftCalls == calls && _session.CaptureReplay().Frames.Length == history);
        Check("cancelling_confirmation_keeps_workbench_pause", Workbench.IsVisibleInTree() && PauseOwners.Contains("crafting-workbench") && _sandbox.IsPaused);
    }

    private async Task StaleConfirmation(long id)
    {
        var confirmation = Find<ConfirmationDialog>("CraftConfirmation");
        foreach (string change in new[] { "selection", "session", "protection" })
        {
            Workbench.SelectItem(id); await Frames(); await Click(Find<Button>("CraftCommit"));
            Check("pending_confirmation_exists_before_" + change, confirmation.Visible);
            if (change == "selection") Workbench.SelectItem(_workingItem);
            else if (change == "protection")
            { Execute(new(ProductionAction.SetItemFavorite, ItemId: id, Value: "true")); Refresh(); }
            else
            {
                var restored = ProductionSession.Restore(_combatJson, _adventure, _progression, _session.Capture());
                _sandbox.SetSession(restored.Combat); Workbench.SynchronizePause();
                _sandbox.SetSession(_session.Combat); Refresh();
            }
            await Frames();
            string hash = _session.StateHash; int calls = _craftCalls;
            Check("context_change_hides_confirmation_" + change, !confirmation.Visible);
            confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Frames();
            Check("stale_confirmation_cannot_extract_after_" + change, _session.StateHash == hash && _craftCalls == calls && Character.Items.Any(i => i.Id == id));
            if (change == "protection") { Execute(new(ProductionAction.SetItemFavorite, ItemId: id, Value: "false")); Refresh(); await Frames(); }
        }
        Workbench.SelectItem(id); await Frames();
    }

    private async Task SelectItemByDrag(long id)
    {
        var other = Character.Items.First(i => i.Id != id); Workbench.SelectItem(other.Id); await Frames();
        string hash = _session.StateHash, world = _sandbox.CurrentAppearance.Key; int history = _session.CaptureReplay().Frames.Length, calls = _craftCalls;
        var source = Find<Control>("CraftInventoryItem" + id); var target = Find<Control>("CraftTarget");
        await Reveal(target); await Reveal(source);
        var start = source.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = start }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = start, Pressed = true, ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
        for (int i = 1; i <= 3 && !GetViewport().GuiIsDragging(); i++)
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = start + new Vector2(i * 12, 0), Relative = new(12, 0), ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
        }
        _dragGestures++;
        Check("owned_item_native_drag_starts", GetViewport().GuiIsDragging());
        var destination = target.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = destination, ButtonMask = MouseButtonMask.Left }, true); await Frames();
        await Capture("crafting-drag-owned-target.png");
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = destination, Pressed = false, ButtonMask = 0 }, true); await Frames();
        Check("owned_item_drop_selects_exact_recipe_target", !GetViewport().GuiIsDragging() && Workbench.SelectedItemId == id);
        Check("target_drag_never_crafts_equips_or_spends", _session.StateHash == hash && _session.CaptureReplay().Frames.Length == history && _craftCalls == calls && _sandbox.CurrentAppearance.Key == world);
        var inventory = Find<CraftingInventory>("CraftingInventory");
        var payload = new Godot.Collections.Dictionary { ["craftOwner"] = inventory.DragOwner, ["epoch"] = inventory.Epoch, ["item"] = id };
        Check("owned_payload_validates_before_invalidation", inventory.TryReadDrag(payload, out long accepted) && accepted == id);
        payload["craftOwner"] = "foreign-workbench";
        Check("foreign_workbench_payload_rejected", !inventory.TryReadDrag(payload, out _));
        payload["craftOwner"] = inventory.DragOwner; inventory.CancelDrag();
        Check("stale_workbench_payload_rejected", !inventory.TryReadDrag(payload, out _));
    }

    private async Task InsufficientMaterialsBranch()
    {
        var snapshot = _session.Capture();
        try
        {
            WalkTo("npc.oris");
            int cost = _session.Content.Capture().CraftingCosts[CraftingService.Rebinding];
            for (int i = 0; i < 64 && Character.Materials >= cost; i++) { Require(_session.Craft(RebindingRecipe(_workingItem))); _commands++; }
            Check("insufficient_branch_spends_only_earned_materials_legally", Character.Materials < cost);
            WalkTo("npc.cael"); Refresh(); await Service(CraftingService.Purification);
            string fragment = Character.OwnedFragments.First(id => !Character.PurifiedFragments.Contains(id));
            await SelectChoice("CraftFragment", fragment);
            string hash = _session.StateHash; int calls = _craftCalls;
            Check("insufficient_recipe_explains_material_shortfall", Workbench.Preview is { Success: false } && Find<Button>("CraftCommit").Disabled && Text().Contains("Insufficient", StringComparison.OrdinalIgnoreCase));
            await Capture("crafting-insufficient-materials.png");
            Check("blocked_insufficient_recipe_cannot_spend_or_submit", _session.StateHash == hash && _craftCalls == calls);
            await SaveReplay("insufficient-branch");
        }
        finally { _session = ProductionSession.Restore(_combatJson, _adventure, _progression, snapshot); _sandbox.SetSession(_session.Combat); Refresh(); }
    }

    private CraftingRequest RebindingRecipe(long id) => PreviewRecipe(CraftingService.Rebinding, id)
        ?? throw new InvalidDataException("Earned item has no legal Core rebinding preview.");

    private CraftingRequest? PreviewRecipe(CraftingService service, long id)
    {
        var item = Item(id); var content = _session.Content.Capture();
        var projection = ProgressionSession.Restore(_session.Content, _session.Capture().Progression);
        foreach (var old in item.Affixes.Keys)
            foreach (string replacement in service == CraftingService.Tempering ? new[] { "" } : content.Affixes.Select(a => a.Id))
            {
                var recipe = new CraftingRequest("", service, id, old, replacement);
                if (projection.PreviewCraft(recipe).Success) return recipe;
            }
        return null;
    }
    private async Task CheckLayouts()
    {
        foreach (var size in new[] { new Vector2I(1280, 800), new Vector2I(1280, 720), new Vector2I(780, 800) })
        {
            GetWindow().Size = GetWindow().ContentScaleSize = size; await Frames(5);
            await Reveal(Find<Button>("CraftCommit"));
            var close = Descendants(_hud).OfType<Button>().Single(b => b.Text == "Close character");
            Check("crafting_controls_fit_" + size.X + "x" + size.Y, GetViewport().GetVisibleRect().Encloses(close.GetGlobalRect()) &&
                GetViewport().GetVisibleRect().Encloses(Find<Button>("CraftCommit").GetGlobalRect()));
            await Capture("crafting-layout-" + size.X + "x" + size.Y + ".png");
        }
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(5);
    }
    private async Task SaveReplay(string directoryName)
    {
        var directory = Path.Combine(_output, directoryName); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "character.save.json");
        ProductionSaveStore.Write(path, _combatJson, _adventure, _progression, _session.Capture());
        var loaded = ProductionSaveStore.Load(path, _combatJson, _adventure, _progression).Session;
        Check(directoryName + "_save_restores_all_crafting_results", loaded.StateHash == _session.StateHash);
        var replay = _session.CaptureReplay();
        Check(directoryName + "_commands_replay_exactly", ProductionReplayRunner.Run(_combatJson, _adventure, _progression, replay).Success);
        System.IO.File.WriteAllText(Path.Combine(directory, "replay.json"), JsonData.Write(replay)); await Frames();
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
        throw new InvalidDataException("Could not reach crafting specialist: " + service);
    }
    private void Execute(ProductionCommand command) { Require(_session.Execute(command)); _commands++; }
    private static void Require(ProductionResult result) { if (!result.Success) throw new InvalidDataException(result.Reason); }
    private PermanentItem Item(long id) => Character.Items.Single(i => i.Id == id);
    private string Text() => Workbench.PreviewText + "\n" + Find<Label>("CraftStatus").Text;
    private async Task Service(CraftingService service) { await Click(Find<Button>("CraftService" + service)); }
    private async Task SelectChoice(string name, string id)
    {
        var choice = Find<OptionButton>(name);
        int index = Enumerable.Range(0, choice.ItemCount).FirstOrDefault(i => choice.GetItemMetadata(i).VariantType == Variant.Type.String && choice.GetItemMetadata(i).AsString() == id, -1);
        if (index < 0) throw new InvalidDataException("Missing crafting choice " + name + "/" + id);
        // Exercise the widget selection contract; native popup keyboard routing is not
        // supplied by synthetic headless viewport input. This does not submit a recipe.
        choice.Select(index); choice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index); await Frames();
    }
    private async Task ClickText(string text) => await Click(Descendants(_hud).OfType<Button>().Single(b => b.Text == text && b.IsVisibleInTree()));
    private async Task Click(Control control)
    {
        await Reveal(control); var point = control.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, Pressed = pressed }, true);
        await Frames();
    }
    private async Task Reveal(Control control)
    {
        if (!control.IsVisibleInTree()) throw new InvalidDataException("Crafting control is hidden: " + control.Name);
        for (Node? ancestor = control.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is not ScrollContainer scroll) continue;
            for (int i = 0; i < 120 && !ContainsVertically(scroll, control); i++)
            {
                var direction = control.GetGlobalRect().Position.Y < scroll.GetGlobalRect().Position.Y ? MouseButton.WheelUp : MouseButton.WheelDown;
                foreach (bool pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = direction, Position = scroll.GetGlobalRect().GetCenter(), Pressed = pressed }, true);
                await Frames(1);
            }
            if (!ContainsVertically(scroll, control)) throw new InvalidDataException("Cannot reveal crafting control: " + control.Name);
        }
        if (!GetViewport().GetVisibleRect().Encloses(control.GetGlobalRect())) throw new InvalidDataException("Crafting control is outside viewport: " + control.Name);
    }
    private static bool ContainsVertically(Control parent, Control child) => child.GetGlobalRect().Position.Y >= parent.GetGlobalRect().Position.Y - 1 && child.GetGlobalRect().End.Y <= parent.GetGlobalRect().End.Y + 1;
    private T Find<T>(string name) where T : Node => Descendants(_hud).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://" + name + ".json");
    private async Task Frames(int count = 3) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string name)
    {
        await Frames();
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-crafting") || DisplayServer.GetName() == "headless") return;
        RenderingServer.ForceDraw(false); RenderingServer.ForceSync(); using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Crafting check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "CraftingClientSmokePassed" : "CraftingClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            commands = _commands,
            craftRequests = _craftCalls,
            dragGestures = _dragGestures,
            committedServices = _committed.Select(s => s.ToString()).Order().ToArray(),
            error,
            scope = "A fresh ProductionSession earns dungeon rewards, rescues specialists and funds the workshop through legal commands. Viewport service buttons, target drag and commit buttons drive the shipping HUD. Dropdowns use public Select/ItemSelected; native confirmation signals test cancel/confirm after the real commit click, because synthetic headless viewport events do not route popup window keyboard input. Five actual service commits, exact preview results and costs, save/replay, pause, layouts and a legally depleted-materials branch are checked. An ordinarily earned legendary is equipped at Torren to save an outfit, then protected and unprotected at Kesh to verify extraction protection, preset warnings and stale protection confirmation cancellation. Grafting is tested as an unawakened-item prerequisite only; no successful graft or endgame catalyst UI result is claimed."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "crafting-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
