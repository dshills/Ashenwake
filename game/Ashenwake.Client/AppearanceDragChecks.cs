using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class AppearanceSmoke
{
    private int _dragGestures;

    private async Task DragDropFlow()
    {
        WalkTo("service.torren"); Refresh();
        Check("drag_torren_opens_gear_board", _hud.PresentInteraction("ServiceOpened:service.torren"));
        await Frames();
        var inventory = OwnedItemIds();
        var original = new SortedDictionary<EquipmentSlot, long>(_session.Capture().Progression.Character.Equipment);
        var slots = EquipmentCardIds();
        Check("drag_board_has_twelve_cached_equipment_slots", slots.Count == 12);
        CheckInventoryProjection("initial");
        await InventoryPresentationChecks();
        string inspectionHash = _session.StateHash, inspectionWorld = _sandbox.CurrentAppearance.Key;
        int inspectionEquips = _equips, inspectionUnequips = _unequips;
        await ClickGearControl(Find<Control>("GearInventoryItem" + _initialMainHand));
        Check("drag_inventory_card_click_inspects_without_equipping", _session.StateHash == inspectionHash && _sandbox.CurrentAppearance.Key == inspectionWorld &&
            !ModelsAgree() && _equips == inspectionEquips && _unequips == inspectionUnequips);
        await ClickGearControl(Find<Button>("ResetGearPreview"));
        Check("drag_inventory_inspection_keeps_existing_reset_control", ModelsAgree() && _session.StateHash == inspectionHash);

        foreach (var slot in new[] { EquipmentSlot.MainHand, EquipmentSlot.Head, EquipmentSlot.Chest, EquipmentSlot.Shoulders, EquipmentSlot.Gloves, EquipmentSlot.Belt, EquipmentSlot.Legs, EquipmentSlot.Boots })
        {
            long id = EquipmentId(slot);
            int unequips = _unequips, equips = _equips;
            var audio = RewardAudio.Count(_hud);
            await DragCard("GearEquipment" + slot, "GearBackpack");
            Check("drag_unequips_" + slot.ToString().ToLowerInvariant() + "_once", EquipmentId(slot) == 0 && _unequips == unequips + 1 && _equips == equips);
            Check("drag_unequip_audio_once_" + slot.ToString().ToLowerInvariant(), RewardAudio.Count(_hud) == audio + 1 && RewardAudio.LastCue(_hud).EndsWith("_off", StringComparison.Ordinal));
            Check("drag_removes_world_and_preview_" + slot.ToString().ToLowerInvariant(), !HasEquipmentModule(_sandbox.GetNode("Actor1"), slot) &&
                !HasEquipmentModule(Find<CharacterPreview>("CharacterPreview"), slot) && ModelsAgree());
            CheckOwnedItems(inventory, "remove_" + slot.ToString().ToLowerInvariant());
            CheckInventoryProjection("remove_" + slot.ToString().ToLowerInvariant());
            if (slot == EquipmentSlot.Head) { InventoryEmptySlotCheck(); await Capture("drag-helmet-in-backpack.png"); }

            await DragCard("GearInventoryItem" + id, "GearEquipment" + slot);
            Check("drag_reequips_" + slot.ToString().ToLowerInvariant() + "_once", EquipmentId(slot) == id && _equips == equips + 1 && _unequips == unequips + 1);
            Check("drag_equip_audio_once_" + slot.ToString().ToLowerInvariant(), RewardAudio.Count(_hud) == audio + 2 && RewardAudio.LastCue(_hud).EndsWith("_on", StringComparison.Ordinal));
            Check("drag_restores_world_and_preview_" + slot.ToString().ToLowerInvariant(), HasEquipmentModule(_sandbox.GetNode("Actor1"), slot) &&
                HasEquipmentModule(Find<CharacterPreview>("CharacterPreview"), slot) && ModelsAgree());
            CheckOwnedItems(inventory, "restore_" + slot.ToString().ToLowerInvariant());
        }

        long firstMain = EquipmentId(EquipmentSlot.MainHand);
        int replacementEquips = _equips;
        await DragCard("GearInventoryItem" + _initialMainHand, "GearEquipmentMainHand", capture: "drag-weapon-over-compatible-slot.png");
        Check("drag_replacement_equips_once_and_returns_previous_item_to_backpack", EquipmentId(EquipmentSlot.MainHand) == _initialMainHand &&
            _equips == replacementEquips + 1 && HasInventoryCard(firstMain) && !HasInventoryCard(_initialMainHand));
        Check("drag_replacement_updates_world_and_preview", ModelsAgree() && _sandbox.CurrentAppearance.MainHand.DefinitionId ==
            _session.Capture().Progression.Character.Items.Single(i => i.Id == _initialMainHand).DefinitionId);
        await DragCard("GearInventoryItem" + firstMain, "GearEquipmentMainHand");
        Check("drag_replacement_can_restore_original_weapon", EquipmentId(EquipmentSlot.MainHand) == firstMain && ModelsAgree());

        await RejectDrag("GearEquipmentHead", "GearEquipmentMainHand", "armor_to_weapon");
        await RejectDrag("GearEquipmentMainHand", "GearEquipmentMainHand", "already_equipped_noop");
        await CancelDrag("GearEquipmentChest", escape: false, "outside_drop");
        await CancelDrag("GearEquipmentHead", escape: true, "escape_cancel");
        await CancelDragLifecycle("inventory_key");
        await CancelDragLifecycle("inventory_key_hidden");
        await CancelDragLifecycle("focus_out");
        await CancelDragLifecycle("panel_hide");
        await CancelDragDuringDrop();
        await NewDragBeforeDeferredCancellation();
        await StaleDrag();
        Check("drag_board_reuses_all_equipment_slot_controls", slots.SetEquals(EquipmentCardIds()));
        CheckOwnedItems(inventory, "all_valid_invalid_and_cancelled_drags");
        Check("drag_flow_restores_original_equipment", original.SequenceEqual(_session.Capture().Progression.Character.Equipment));
        CheckInventoryProjection("restored");
        await Capture("drag-loadout-restored.png");

        _hud.Toggle(); WalkTo("npc.mara"); Refresh();
        _hud.PresentInteraction("ServiceOpened:service.torren"); await Frames();
        await RejectDrag("GearEquipmentHead", "GearBackpack", "away_from_torren");
        Check("drag_away_inspection_preserves_all_items", inventory.SetEquals(OwnedItemIds()));
        _hud.Toggle(); WalkTo("service.torren"); Refresh();
        _hud.PresentInteraction("ServiceOpened:service.torren"); await Frames();
        await DragSaveReplay("drag-vanguard");
        await TwoHandedDrag();
        Check("drag_two_handed_branch_preserves_vanguard_equipment", original.SequenceEqual(_session.Capture().Progression.Character.Equipment) && inventory.SetEquals(OwnedItemIds()));
        _hud.Toggle(); await Frames();
    }

    private async Task StaleDrag()
    {
        var audio = RewardAudio.Count(Find<GearLoadout>("GearLoadout"));
        long head = EquipmentId(EquipmentSlot.Head);
        var source = Find<Control>("GearEquipmentHead");
        await RevealDragControl(source);
        Check("drag_stale_payload_starts_with_real_equipped_card", await BeginDrag(source));
        // Simulate another legitimate owner transaction while the pointer is held. The UI
        // must reject the now-stale payload rather than replaying an obsolete unequip request.
        Require(_session.Unequip(EquipmentSlot.Head)); _commands++; Refresh(); await Frames();
        string hash = _session.StateHash;
        int operations = _session.CaptureReplay().Frames.Length, unequips = _unequips, equips = _equips;
        await EndDrag(DropPoint(Find<Control>("GearBackpack")));
        Check("drag_stale_invalidated_payload_is_silent", RewardAudio.Count(Find<GearLoadout>("GearLoadout")) == audio);
        Check("drag_stale_payload_cannot_send_an_extra_transaction", _session.StateHash == hash && _session.CaptureReplay().Frames.Length == operations &&
            _unequips == unequips && _equips == equips && EquipmentId(EquipmentSlot.Head) == 0);
        await DragCard("GearInventoryItem" + head, "GearEquipmentHead");
        Check("drag_stale_payload_recovery_uses_current_inventory", EquipmentId(EquipmentSlot.Head) == head && ModelsAgree());
    }

    private async Task TwoHandedDrag()
    {
        var live = _session;
        try
        {
            _hud.Toggle();
            // The maintained completed-campaign archive owns earned Greatstaffs. Load it
            // with its original content and perform a normal paid retrain; do not mint an item.
            string fixtureCombat = CampaignCombatContent.Parse(Read("combat-phase4"), Read("campaign-combat-phase4")).CombatJson;
            var fixtureAdventure = AdventureContent.Parse(Read("adventure-phase4"));
            var fixtureProgression = ProgressionContent.Parse(Read("progression-phase4"));
            var fixtureCampaign = CampaignContent.Parse(Read("campaign-phase4"));
            string fixture = Read("phase4-campaign-complete");
            _session = CampaignRuntimeSaveStore.Read(fixtureCombat, fixtureAdventure, fixtureProgression, fixtureCampaign, fixture).Production;
            long staff = _session.Capture().Progression.Character.Items.First(i => i.DefinitionId == "item.greatstaff").Id;
            WalkTo("service.torren"); Refresh(); _hud.PresentInteraction("ServiceOpened:service.torren"); await Frames();
            Check("drag_wrong_discipline_uses_earned_staff_before_retrain", _session.ProgressionView.Discipline == "Vanguard");
            await RejectDrag("GearInventoryItem" + staff, "GearEquipmentMainHand", "wrong_discipline");
            await IncompatibleDiscardInspection(staff);
            _hud.Toggle();
            WalkTo("service.mara"); Require(_session.Retrain("Arcanist")); _commands++;
            WalkTo("service.torren"); Refresh(); _hud.PresentInteraction("ServiceOpened:service.torren"); await Frames();
            var inventory = OwnedItemIds();
            long offHand = EquipmentId(EquipmentSlot.OffHand), mainHand = EquipmentId(EquipmentSlot.MainHand);
            Check("drag_two_handed_fixture_is_a_legitimate_arcanist", _session.ProgressionView.Discipline == "Arcanist" && offHand > 0 && staff > 0);
            await ArchivedInventoryChecks();
            await RejectDrag("GearInventoryItem" + staff, "GearEquipmentMainHand", "two_handed_with_offhand");
            await DragCard("GearEquipmentOffHand", "GearBackpack");
            await DragCard("GearInventoryItem" + staff, "GearEquipmentMainHand");
            Check("drag_two_handed_weapon_equips_after_explicit_offhand_removal", EquipmentId(EquipmentSlot.MainHand) == staff && EquipmentId(EquipmentSlot.OffHand) == 0 &&
                _sandbox.CurrentAppearance.MainHand.DefinitionId == "item.greatstaff" && !HasEquipmentModule(_sandbox.GetNode("Actor1"), EquipmentSlot.OffHand) && ModelsAgree());
            await RejectDrag("GearInventoryItem" + offHand, "GearEquipmentOffHand", "offhand_with_two_handed");
            await Capture("drag-two-handed-equipped.png");
            await DragCard("GearInventoryItem" + mainHand, "GearEquipmentMainHand");
            await DragCard("GearInventoryItem" + offHand, "GearEquipmentOffHand");
            Check("drag_two_handed_replacement_allows_offhand_again", EquipmentId(EquipmentSlot.MainHand) == mainHand && EquipmentId(EquipmentSlot.OffHand) == offHand && ModelsAgree());
            CheckOwnedItems(inventory, "arcanist_two_handed_branch");
            await DragSaveReplay("drag-arcanist", fixtureCombat, fixtureAdventure, CampaignRuntimeSession.ResolvePolicy(fixtureProgression, fixtureCampaign));
            Check("drag_two_handed_fixture_source_is_unchanged", Read("phase4-campaign-complete") == fixture);
        }
        finally
        {
            _session = live; Refresh(); _hud.PresentInteraction("ServiceOpened:service.torren"); await Frames();
        }
    }

    private async Task DragSaveReplay(string prefix, string? combatJson = null, AdventureContent? adventure = null, ProgressionContent? progression = null)
    {
        string hash = _session.StateHash, key = _sandbox.CurrentAppearance.Key;
        // Each branch gets its own profile ledger; archived campaign discoveries must not
        // leak into the fresh Vanguard's later save/loot diagnostic.
        string directory = Path.Combine(_output, prefix); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "character.save.json");
        combatJson ??= Read("combat"); adventure ??= AdventureContent.Parse(Read("adventure")); progression ??= ProgressionContent.Parse(Read("progression"));
        ProductionSaveStore.Write(path, combatJson, adventure, progression, _session.Capture());
        var loaded = ProductionSaveStore.Load(path, combatJson, adventure, progression).Session;
        Check(prefix + "_save_preserves_equipment_inventory_and_appearance", loaded.StateHash == hash &&
            CharacterAppearance.FromProgression(loaded.Capture().Progression, loaded.View.ActiveManifestations,
                loaded.Capture().Expedition.Adventure.Anatomy.Values).Key == key &&
            loaded.Capture().Progression.Character.Equipment.SequenceEqual(_session.Capture().Progression.Character.Equipment));
        var replay = _session.CaptureReplay();
        Check(prefix + "_drag_commands_replay_exactly", ProductionReplayRunner.Run(combatJson, adventure, progression, replay).Success);
        System.IO.File.WriteAllText(Path.Combine(_output, prefix + ".replay.json"), JsonData.Write(replay));
        await Frames();
    }

    private async Task RejectDrag(string sourceName, string targetName, string name)
    {
        string hash = _session.StateHash;
        int equips = _equips, unequips = _unequips, operations = _session.CaptureReplay().Frames.Length;
        var audio = RewardAudio.Count(Find<GearLoadout>("GearLoadout"));
        await DragCard(sourceName, targetName);
        Check("drag_rejection_audio_once_" + name, RewardAudio.Count(Find<GearLoadout>("GearLoadout")) == audio + 1 && RewardAudio.LastCue(Find<GearLoadout>("GearLoadout")) == "gear_reject");
        Check("drag_rejects_" + name + "_without_transaction", _session.StateHash == hash && _session.CaptureReplay().Frames.Length == operations && _equips == equips && _unequips == unequips);
        await InventoryRejectedDropFeedback(name, targetName);
    }

    private async Task CancelDrag(string sourceName, bool escape, string name)
    {
        string hash = _session.StateHash;
        int operations = _session.CaptureReplay().Frames.Length, equips = _equips, unequips = _unequips;
        var audio = RewardAudio.Count(Find<GearLoadout>("GearLoadout"));
        var source = Find<Control>(sourceName); await RevealDragControl(source);
        Check("drag_" + name + "_starts_real_drag", await BeginDrag(source));
        if (escape)
        {
            foreach (bool pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = pressed }, true);
            await Frames();
            Check("drag_escape_ends_native_drag_before_release", !GetViewport().GuiIsDragging());
        }
        await EndDrag(new(8, GetViewport().GetVisibleRect().Size.Y - 8));
        Check("drag_" + name + "_audio_matches_release_or_cancel", RewardAudio.Count(Find<GearLoadout>("GearLoadout")) == audio + (escape ? 0 : 1));
        Check("drag_" + name + "_leaves_equipment_unchanged", !GetViewport().GuiIsDragging() && _session.StateHash == hash &&
            _session.CaptureReplay().Frames.Length == operations && _equips == equips && _unequips == unequips);
        _hud.PresentInteraction("ServiceOpened:service.torren"); await Frames();
    }

    private async Task DragCard(string sourceName, string targetName, bool requireStarted = true, string? capture = null)
    {
        var source = Find<Control>(sourceName); var target = Find<Control>(targetName);
        await RevealDragControl(target); await RevealDragControl(source);
        var audio = RewardAudio.Count(_hud) + RewardAudio.Count(Find<GearLoadout>("GearLoadout"));
        bool started = await BeginDrag(source);
        if (requireStarted && !started) throw new InvalidDataException("Viewport gesture did not begin dragging " + sourceName);
        Vector2 point = DropPoint(target);
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, Relative = point - source.GetGlobalRect().GetCenter(), ButtonMask = MouseButtonMask.Left }, true);
        await Frames();
        Check("drag_hover_is_silent_" + _dragGestures, RewardAudio.Count(_hud) + RewardAudio.Count(Find<GearLoadout>("GearLoadout")) == audio);
        if (capture is not null) await Capture(capture);
        await EndDrag(point);
        Check("drag_gesture_" + _dragGestures + "_clears_native_drag_state", !GetViewport().GuiIsDragging());
    }

    private async Task CancelDragLifecycle(string reason)
    {
        string hash = _session.StateHash;
        int operations = _session.CaptureReplay().Frames.Length, equips = _equips, unequips = _unequips;
        var audio = RewardAudio.Count(Find<GearLoadout>("GearLoadout"));
        var source = Find<Control>("GearEquipmentChest"); await RevealDragControl(source);
        Check("drag_" + reason + "_starts_real_drag", await BeginDrag(source));
        var board = Find<GearLoadout>("GearLoadout");
        switch (reason)
        {
            case "inventory_key":
            case "inventory_key_hidden":
                int menuCalls = 0;
                var previous = _sandbox.InventoryOverride;
                // Use the same public input hook as the shipping directors, counting actual
                // unhandled-input delivery after GearLoadout cancels its owned native drag.
                _sandbox.InventoryOverride = () => { menuCalls++; _hud.ToggleInventory(); };
                try
                {
                    foreach (bool pressed in new[] { true, false })
                        GetViewport().PushInput(new InputEventKey { Keycode = Key.I, PhysicalKeycode = Key.I, Pressed = pressed }, true);
                    // Change the owning view before the deferred menu event is dispatched.
                    // A stale cancellation event must not reopen a panel the user closed.
                    if (reason == "inventory_key_hidden") _hud.Toggle();
                    await Frames();
                    if (reason == "inventory_key_hidden")
                        Check("drag_inventory_key_hidden_discards_deferred_menu_press", menuCalls == 0 && !board.IsVisibleInTree());
                    else
                        Check("drag_inventory_key_continues_to_menu_once", menuCalls == 1 && !board.IsVisibleInTree());
                }
                finally { _sandbox.InventoryOverride = previous; }
                break;
            case "focus_out":
                // Deliver the engine's application-focus notification to the actual board.
                // The native pointer drag was started through viewport events above.
                board.Notification((int)NotificationApplicationFocusOut);
                await Frames();
                break;
            case "panel_hide":
                _hud.Toggle(); await Frames();
                Check("drag_panel_hide_hides_equipment_board", !board.IsVisibleInTree());
                break;
            default: throw new ArgumentOutOfRangeException(nameof(reason));
        }
        Check("drag_" + reason + "_cancels_before_pointer_release", !GetViewport().GuiIsDragging() &&
            !DragPauseOwners.Contains("equipment-drag") && DragPauseOwners.Contains("session") && _sandbox.IsPaused);
        await EndDrag(new(8, GetViewport().GetVisibleRect().Size.Y - 8));
        Check("drag_" + reason + "_cancellation_is_silent", RewardAudio.Count(board) == audio);
        Check("drag_" + reason + "_preserves_equipment_and_history", _session.StateHash == hash &&
            _session.CaptureReplay().Frames.Length == operations && _equips == equips && _unequips == unequips);
        _hud.PresentInteraction("ServiceOpened:service.torren"); await Frames();
    }

    private async Task CancelDragDuringDrop()
    {
        var audio = RewardAudio.Count(_hud) + RewardAudio.Count(Find<GearLoadout>("GearLoadout"));
        string hash = _session.StateHash;
        int operations = _session.CaptureReplay().Frames.Length, equips = _equips, unequips = _unequips;
        var source = Find<Control>("GearEquipmentHead"); await RevealDragControl(source);
        Check("drag_focus_during_drop_starts_real_drag", await BeginDrag(source));
        var board = Find<GearLoadout>("GearLoadout");
        var target = Find<GearDragCard>("GearBackpack");
        var receive = target.Receive;
        bool delivered = false, deferred = false;
        target.Receive = data =>
        {
            // Reproduce a focus notification inside native drop dispatch, before the
            // authoritative handler runs and while Godot still owns the drag preview.
            delivered = true;
            board.Notification((int)NotificationApplicationFocusOut);
            deferred = GetViewport().GuiIsDragging();
            receive?.Invoke(data);
        };
        try { await EndDrag(DropPoint(target)); }
        finally { target.Receive = receive; }
        Check("drag_focus_during_drop_defers_native_preview_teardown", delivered && deferred && !GetViewport().GuiIsDragging());
        Check("drag_focus_during_drop_cancellation_is_silent", RewardAudio.Count(_hud) + RewardAudio.Count(board) == audio);
        Check("drag_focus_during_drop_immediately_invalidates_transaction", _session.StateHash == hash &&
            _session.CaptureReplay().Frames.Length == operations && _equips == equips && _unequips == unequips);
    }

    private async Task NewDragBeforeDeferredCancellation()
    {
        foreach (bool menu in new[] { false, true })
        {
            string name = menu ? "drag_newer_than_deferred_menu" : "drag_newer_than_deferred_cancel";
            string hash = _session.StateHash;
            int operations = _session.CaptureReplay().Frames.Length, equips = _equips, unequips = _unequips;
            var source = Find<GearDragCard>("GearEquipmentHead"); await RevealDragControl(source);
            Check(name + "_starts_original_pointer_drag", await BeginDrag(source));
            var board = Find<GearLoadout>("GearLoadout");
            var viewport = GetViewport();
            long oldEpoch = viewport.GuiGetDragData().AsGodotDictionary()["epoch"].AsInt64();
            int menuCalls = 0;
            var previous = _sandbox.InventoryOverride;
            _sandbox.InventoryOverride = () => { menuCalls++; _hud.ToggleInventory(); };
            try
            {
                if (menu) board._Input(new InputEventKey { Keycode = Key.I, PhysicalKeycode = Key.I, Pressed = true });
                else board.CancelDrag();
                // Keep this sequence in one dispatch turn. ForceDrag uses the actual card's
                // fresh owner payload to isolate the narrow race before deferred work runs;
                // ordinary pointer-driven drag coverage surrounds this scheduling check.
                viewport.GuiCancelDrag();
                Variant fresh = source.DragDataRequested!();
                long freshEpoch = fresh.AsGodotDictionary()["epoch"].AsInt64();
                source.ForceDrag(fresh, new Control { CustomMinimumSize = new(24, 24), MouseFilter = Control.MouseFilterEnum.Ignore });
                _dragGestures++;
                Check(name + "_starts_new_native_drag_before_callbacks", freshEpoch != oldEpoch && viewport.GuiIsDragging());
                await Frames();
                Check(name + "_preserves_new_native_drag", viewport.GuiIsDragging() &&
                    viewport.GuiGetDragData().AsGodotDictionary()["epoch"].AsInt64() == freshEpoch && DragPauseOwners.Contains("equipment-drag"));
                Check(name + "_does_not_replay_menu", menuCalls == 0 && board.IsVisibleInTree());
                await EndDrag(new(8, viewport.GetVisibleRect().Size.Y - 8));
                Check(name + "_preserves_equipment_and_history", _session.StateHash == hash &&
                    _session.CaptureReplay().Frames.Length == operations && _equips == equips && _unequips == unequips);
            }
            finally { _sandbox.InventoryOverride = previous; }
        }
    }

    private async Task<bool> BeginDrag(Control source)
    {
        if (!source.IsVisibleInTree()) throw new InvalidDataException("Drag source is hidden: " + source.GetPath());
        Vector2 start = source.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = start }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = start, Pressed = true, ButtonMask = MouseButtonMask.Left }, true);
        await Frames(1);
        for (int step = 1; step <= 3 && !GetViewport().GuiIsDragging(); step++)
        {
            GetViewport().PushInput(new InputEventMouseMotion { Position = start + new Vector2(step * 12, 0), Relative = new(12, 0), ButtonMask = MouseButtonMask.Left }, true);
            await Frames(1);
        }
        _dragGestures++;
        bool dragging = GetViewport().GuiIsDragging();
        Check("drag_gesture_" + _dragGestures + "_owns_pause_only_when_active", DragPauseOwners.Contains("equipment-drag") == dragging &&
            DragPauseOwners.Contains("session") && _sandbox.IsPaused);
        return dragging;
    }

    private async Task EndDrag(Vector2 target)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = target, ButtonMask = MouseButtonMask.Left }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = target, Pressed = false, ButtonMask = 0 }, true);
        await Frames();
        Check("drag_gesture_" + _dragGestures + "_releases_only_its_pause", !DragPauseOwners.Contains("equipment-drag") && DragPauseOwners.Contains("session") && _sandbox.IsPaused);
    }

    private async Task ClickGearControl(Control control)
    {
        await RevealDragControl(control);
        Vector2 point = control.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, Pressed = pressed }, true);
        await Frames();
    }

    private async Task RevealDragControl(Control control)
    {
        for (Node? ancestor = control.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is not ScrollContainer scroll) continue;
            for (int attempt = 0; attempt < 120 && !DragVerticallyVisible(scroll, control); attempt++)
            {
                var button = control.GetGlobalRect(); var rect = scroll.GetGlobalRect();
                var direction = button.Position.Y < rect.Position.Y ? MouseButton.WheelUp : MouseButton.WheelDown;
                foreach (bool pressed in new[] { true, false })
                    GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = direction, Position = rect.GetCenter(), Pressed = pressed }, true);
                await Frames(1);
            }
            if (!DragVerticallyVisible(scroll, control)) throw new InvalidDataException("Cannot reveal drag control: " + control.GetPath());
        }
        if (!GetViewport().GetVisibleRect().Encloses(control.GetGlobalRect()))
            throw new InvalidDataException("Drag control is outside viewport: " + control.GetPath() + " " + control.GetGlobalRect());
    }

    private static bool DragVerticallyVisible(Control clip, Control control) => control.GetGlobalRect().Position.Y >= clip.GetGlobalRect().Position.Y - 1 &&
        control.GetGlobalRect().End.Y <= clip.GetGlobalRect().End.Y + 1;
    private HashSet<string> DragPauseOwners => (HashSet<string>)(typeof(Sandbox).GetField("_modalPauses", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_sandbox)
        ?? throw new MissingFieldException("_modalPauses"));
    private static Vector2 DropPoint(Control target) => target.Name == "GearBackpack"
        ? target.GetGlobalRect().Position + new Vector2(target.Size.X * .5f, Math.Min(16, target.Size.Y * .5f)) : target.GetGlobalRect().GetCenter();
    private long EquipmentId(EquipmentSlot slot) => _session.Capture().Progression.Character.Equipment.GetValueOrDefault(slot);
    private HashSet<long> OwnedItemIds() => _session.Capture().Progression.Character.Items.Select(i => i.Id).ToHashSet();
    private bool HasInventoryCard(long id) => Descendants(Find<Control>("GearLoadout")).Any(n => n.Name == "GearInventoryItem" + id);
    private HashSet<ulong> EquipmentCardIds() => Descendants(Find<Control>("GearLoadout")).OfType<Control>()
        .Where(n => Enum.GetValues<EquipmentSlot>().Any(slot => n.Name == "GearEquipment" + slot)).Select(n => n.GetInstanceId()).ToHashSet();
    private bool ModelsAgree() => Find<CharacterPreview>("CharacterPreview").AppearanceKey == _sandbox.CurrentAppearance.Key;
    private static bool HasEquipmentModule(Node root, EquipmentSlot slot) => Descendants(root).Any(n => n.Name == "Equipment" + slot);
    private void CheckOwnedItems(HashSet<long> expected, string suffix) => Check("drag_preserves_inventory_ids_" + suffix,
        expected.SetEquals(OwnedItemIds()) && _session.Capture().Progression.Character.Items.Length == expected.Count);
    private void CheckInventoryProjection(string suffix)
    {
        var state = _session.Capture().Progression.Character;
        var expected = state.Items.Where(i => !state.Equipment.Values.Contains(i.Id)).Select(i => "GearInventoryItem" + i.Id).ToHashSet();
        var actual = Descendants(Find<Control>("GearLoadout")).OfType<Control>().Where(n => n.Visible && n.Name.ToString().StartsWith("GearInventoryItem", StringComparison.Ordinal)).Select(n => n.Name.ToString()).ToArray();
        Check("drag_backpack_contains_only_unequipped_items_" + suffix, expected.SetEquals(actual) && actual.Length == expected.Count);
    }
}
