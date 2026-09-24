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

/// <summary>Exercises the shipping wardrobe with viewport input and genuinely earned armor.</summary>
public partial class WardrobeSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly Dictionary<EquipmentSlot, string> _initialArmor = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _lastInput = "";
    private bool _writeReport;
    private int _commands, _clicks;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private ProductionHud Character => Field<ProductionHud>(_director, "_character");
    private AppearanceWardrobePanel Panel => Field<AppearanceWardrobePanel>(_director, "_wardrobe");
    private AppearanceWardrobeMemory Memory => Field<AppearanceWardrobeMemory>(_director, "_wardrobeMemory");
    private AppearanceWardrobeMemory Draft => Field<AppearanceWardrobeMemory>(_director, "_wardrobeDraft");
    private ProgressionSnapshot Progression => Session.Production.Capture().Progression;
    private ProgressionDefinition Definition => Field<ProgressionDefinition>(_director, "_productionDefinition");
    private CharacterAppearance Equipped => CharacterAppearance.FromProgression(Progression, Session.Production.View.ActiveManifestations, Session.Production.Capture().Expedition.Adventure.Anatomy.Values);
    private CharacterPreview Preview => Descendants(Panel).OfType<CharacterPreview>().Single();

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs(); _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--wardrobe-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Wardrobe smoke requires --wardrobe-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            await Frames(); CloseMenus(); Call(_sandbox, "ResumePlaying");
            await Starter(); GD.Print("Wardrobe starter input checks passed.");
            await EarnPair(); GD.Print("Wardrobe equipment earned through campaign.");
            await DraftAndLooks(); GD.Print("Wardrobe draft and saved-look checks passed.");
            await SalvageAndReload(); await CharacterIsolation();
            Check("final_replay", Replay());
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); await Finish(false, ex.Message); }
    }

    private async Task Starter()
    {
        await Open(); string hash = Session.StateHash, saved = JsonData.Hash(Memory); int frames = Session.CaptureReplay().Frames.Length;
        foreach (var slot in AppearanceWardrobe.ArmorSlots)
        {
            long equippedId = Progression.Character.Equipment[slot];
            _initialArmor[slot] = Progression.Character.Items.Single(i => i.Id == equippedId).DefinitionId;
        }
        Check("seven_owned_initial_armor_unlocks", _initialArmor.Count == 7 && _initialArmor.Values.All(id => Memory.Unlocks.Any(u => u.ItemId == id)));
        Check("weapon_appearances_excluded", Memory.Unlocks.All(u => Definition.Items.Single(i => i.Id == u.ItemId).Slots.All(AppearanceWardrobe.ArmorSlots.Contains)));
        await Click("WardrobeSlotHead"); await Click("WardrobeItem_" + _initialArmor[EquipmentSlot.Head][5..]); await Click("WardrobeHideHelmet");
        Check("helmet_draft_preview_only", Draft.HideHelmet && Preview.AppearanceKey == WardrobeAppearance.Project(Equipped, Draft).Key && !Memory.HideHelmet && _sandbox.CurrentAppearance.Head.DefinitionId.Length > 0);
        await Click("WardrobeCancel");
        Check("cancel_discards_draft", !Panel.IsOpen && JsonData.Hash(Memory) == saved);
        // Exercise popup input before the campaign earns its rewards, so routing failures fail quickly.
        await Open(); await EnterName("Lantern Pilgrim"); await Click("WardrobeSaveLook"); await Click("WardrobeApply");
        await SelectSavedLook(); await Click("WardrobeUseLook"); await Click("WardrobeDeleteLook"); await Click("WardrobeApply");
        Check("starter_named_look_round_trip", Memory.Looks.Length == 0 && Memory.Overrides.Count == 0 && !Memory.HideHelmet);
        await Click("WardrobeCancel");
        Check("starter_actions_do_not_change_core_or_replay", Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frames);
    }

    private async Task EarnPair()
    {
        bool OwnPair() => new[] { EquipmentSets.VigilHead, EquipmentSets.VigilChest }.All(id => Progression.Character.Items.Any(i => i.DefinitionId == id));
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !(OwnPair() && Session.InHub); i++)
        {
            Step(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)), false);
            if (i % 150 == 0) await Frames(1);
        }
        Refresh(); Call(_director, "ClearDeathRecap"); CloseMenus();
        Check("act_one_pair_earned_normally", OwnPair() && Session.InHub && Session.Campaign.Capture().Campaign.CompletedEncounters.Contains("campaign.bell_saint"));
        Walk(Session.Interactions.Single(i => i.ActionId == "service.torren").Position);
        foreach (var slot in AppearanceWardrobe.ArmorSlots)
        {
            string id = slot == EquipmentSlot.Head ? EquipmentSets.VigilHead : slot == EquipmentSlot.Chest ? EquipmentSets.VigilChest : _initialArmor[slot];
            var item = Progression.Character.Items.First(i => i.DefinitionId == id);
            if (Progression.Character.Equipment.GetValueOrDefault(slot) != item.Id) Step(new(EndgameRuntimeAction.Production, Production: new(ProductionAction.Equip, ItemId: item.Id, Slot: slot)));
        }
        Check("earned_appearances_observed", Memory.Unlocks.Any(u => u.ItemId == EquipmentSets.VigilHead) && Memory.Unlocks.Any(u => u.ItemId == EquipmentSets.VigilChest));
    }

    private async Task DraftAndLooks()
    {
        string hash = Session.StateHash; int frames = Session.CaptureReplay().Frames.Length; string actual = Equipped.Key;
        await Open();
        foreach (var slot in AppearanceWardrobe.ArmorSlots)
        {
            await Click("WardrobeSlot" + slot); await Click("WardrobeItem_" + _initialArmor[slot][5..]);
            Check("draft_" + slot, Draft.Overrides.GetValueOrDefault(slot) == _initialArmor[slot]);
        }
        Check("all_seven_armor_drafts_preview", Draft.Overrides.Count == 7 && Preview.AppearanceKey == WardrobeAppearance.Project(Equipped, Draft).Key && Memory.Overrides.Count == 0);
        Check("draft_does_not_change_world", _sandbox.CurrentAppearance.Key == actual);
        foreach (string discipline in new[] { "Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden" })
        {
            var source = Equipped with { Discipline = discipline }; var projected = WardrobeAppearance.Project(source, Draft);
            Check("seven_slot_projection_" + discipline, projected.Head.DefinitionId == _initialArmor[EquipmentSlot.Head] && projected.Chest.DefinitionId == _initialArmor[EquipmentSlot.Chest] && projected.Shoulders.DefinitionId == _initialArmor[EquipmentSlot.Shoulders] && projected.Gloves.DefinitionId == _initialArmor[EquipmentSlot.Gloves] && projected.Belt.DefinitionId == _initialArmor[EquipmentSlot.Belt] && projected.Legs.DefinitionId == _initialArmor[EquipmentSlot.Legs] && projected.Boots.DefinitionId == _initialArmor[EquipmentSlot.Boots] && projected.MainHand == source.MainHand && projected.OffHand == source.OffHand);
            Preview.SetAppearance(projected); Preview.SetCaption("Owned armor preview · " + discipline); await Frames(); await Capture("wardrobe-" + discipline + ".png");
        }
        await Click("WardrobeSlotHead"); await Click("WardrobeHideHelmet");
        Check("hidden_helmet_preview", Draft.HideHelmet && Preview.AppearanceKey == WardrobeAppearance.Project(Equipped, Draft).Key);
        CheckLayout("wide"); await Capture("wardrobe-wide.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout("compact"); await Capture("wardrobe-compact.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(8);
        await Click("WardrobeApply"); Check("apply_publishes_all_armor", Memory.Overrides.Count == 7 && Memory.HideHelmet && _sandbox.CurrentAppearance.Key == WardrobeAppearance.Project(Equipped, Memory).Key);
        await EnterName("Lantern Pilgrim"); await Click("WardrobeSaveLook");
        Check("named_look_is_staged", Draft.Looks.Length == 1 && Memory.Looks.Length == 0);
        await Click("WardrobeApply"); Check("named_look_saved", Memory.Looks.Single().Name == "Lantern Pilgrim");
        string committed = JsonData.Hash(Memory); await Click("WardrobeReset"); await Click("WardrobeCancel");
        Check("reset_cancel_retains_applied_look", JsonData.Hash(Memory) == committed);
        await Open(); await Click("WardrobeReset"); await Click("WardrobeApply");
        Check("global_reset_restores_actual_gear", Memory.Overrides.Count == 0 && !Memory.HideHelmet && _sandbox.CurrentAppearance.Key == actual);
        await SelectSavedLook(); await Click("WardrobeUseLook"); Check("use_saved_look_previews_only", Draft.Overrides.Count == 7 && Memory.Overrides.Count == 0);
        await Click("WardrobeApply"); Check("saved_look_restored", Memory.Overrides.Count == 7 && Memory.HideHelmet);
        await Click("WardrobeSlotHead"); await Click("WardrobeEquipped"); Check("single_slot_reset_preserves_other_slots", Draft.Overrides.Count == 6 && !Draft.Overrides.ContainsKey(EquipmentSlot.Head));
        await Click("WardrobeCancel"); await Open(); await SelectSavedLook(); await Click("WardrobeDeleteLook");
        Check("look_deletion_staged", Draft.Looks.Length == 0 && Memory.Looks.Length == 1);
        await Click("WardrobeCancel"); await Open(); Check("look_deletion_cancelled", Draft.Looks.Length == 1);
        await SelectSavedLook(); await Click("WardrobeDeleteLook"); await Click("WardrobeApply"); Check("look_deletion_committed", Memory.Looks.Length == 0);
        Check("wardrobe_actions_preserve_core_and_history", Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frames && Equipped.Key == actual);
        CloseMenus();
    }

    private async Task SalvageAndReload()
    {
        var starter = Progression.Character.Items.First(i => i.DefinitionId == _initialArmor[EquipmentSlot.Head]);
        Step(new(EndgameRuntimeAction.Production, Production: new(ProductionAction.Equip, ItemId: starter.Id, Slot: EquipmentSlot.Head)));
        await Open(); if (Draft.HideHelmet) await Click("WardrobeHideHelmet");
        await Click("WardrobeSlotHead"); await Click("WardrobeItem_" + EquipmentSets.VigilHead[5..]); await Click("WardrobeApply"); CloseMenus();
        var earned = Progression.Character.Items.Single(i => i.DefinitionId == EquipmentSets.VigilHead);
        Step(new(EndgameRuntimeAction.Production, Production: new(ProductionAction.Salvage, ItemId: earned.Id, ConfirmPermanent: true)));
        Check("actual_salvage_retains_unlocked_appearance", Progression.Character.Items.All(i => i.Id != earned.Id) && Memory.Unlocks.Any(u => u.ItemId == EquipmentSets.VigilHead) && _sandbox.CurrentAppearance.Head.DefinitionId == EquipmentSets.VigilHead);
        string hash = Session.StateHash, memory = JsonData.Hash(Memory); Check("earned_and_salvaged_replay", Replay());
        Call(_director, "Save"); Call(_director, "Load"); await Frames();
        Check("save_load_preserves_core_and_wardrobe", Session.StateHash == hash && JsonData.Hash(Memory) == memory && _sandbox.CurrentAppearance.Head.DefinitionId == EquipmentSets.VigilHead);
        await Open(); Check("salvaged_appearance_remains_selectable", !Find<Button>("WardrobeItem_" + EquipmentSets.VigilHead[5..]).Disabled); await Capture("wardrobe-retained-after-salvage.png"); CloseMenus();
    }

    private async Task CharacterIsolation()
    {
        string filename = Field<string>(_director, "_saveName"), character = Memory.CharacterId, hash = Session.StateHash, memory = JsonData.Hash(Memory);
        Call(_director, "ShowFrontMenu"); await Frames(); await Click("FrontNew"); await Click("FrontDisciplineWarden"); await Click("FrontBegin"); CloseMenus();
        Check("new_character_has_separate_wardrobe", Field<string>(_director, "_saveName") != filename && Memory.Overrides.Count == 0 && Memory.Looks.Length == 0 && !Memory.Unlocks.Any(u => u.ItemId == EquipmentSets.VigilHead));
        Call(_director, "ShowFrontMenu"); await Frames();
        var menu = Field<FrontMenu>(_director, "_frontMenu"); ulong deadline = Time.GetTicksMsec() + 10000;
        while (menu.CatalogLoading && Time.GetTicksMsec() < deadline) await Frames();
        await Click("FrontCharacters");
        var slotsField = menu.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Single(f => f.FieldType == typeof(CharacterSlot[]));
        var slots = (CharacterSlot[])slotsField.GetValue(menu)!; int index = Array.FindIndex(slots, s => s.Filename == filename);
        Check("first_character_found_in_browser", index >= 0); await Click("FrontSlot" + index); await Click("FrontPlay"); CloseMenus();
        Check("switch_back_restores_own_appearance", Session.StateHash == hash && Memory.CharacterId == character && JsonData.Hash(Memory) == memory && _sandbox.CurrentAppearance.Head.DefinitionId == EquipmentSets.VigilHead);
    }

    private async Task Open() { CloseMenus(); Character.ToggleInventory(); await Frames(); await Click("OpenAppearanceWardrobe"); Check("wardrobe_open_and_paused", Panel.IsOpen && _sandbox.IsPaused); }
    private void CloseMenus()
    {
        Panel.SetOpen(false); Field<LegendaryCollectionPanel>(_director, "_collection").SetOpen(false); Character.Close();
        Field<CampaignHud>(_director, "_campaignHud").SetOpen(false); Field<EndgameHud>(_director, "_board").SetOpen(false); Field<OpeningGuidancePanel>(_director, "_openingGuide").SetOpen(false);
    }
    private async Task SelectSavedLook()
    {
        var options = Find<OptionButton>("WardrobeSavedLooks"); await Click("WardrobeSavedLooks");
        var popup = options.GetPopup();
        popup.GrabFocus(); await Frames();
        Check("saved_look_popup_has_expected_rows", popup.Visible && popup.ItemCount == 2 && popup.GetItemText(1) == "Lantern Pilgrim");
        // The root viewport forwards embedded-window input through PopupMenu's
        // window handler; pushing into the popup viewport itself skips that handler.
        for (int i = 0; i < 2 && popup.GetFocusedItem() != 1; i++)
        {
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = Key.Down, PhysicalKeycode = Key.Down, Pressed = pressed }, true);
            await Frames();
        }
        Check("saved_look_keyboard_focus", popup.GetFocusedItem() == 1);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = pressed }, true);
        await Frames();
        Check("saved_look_keyboard_selection", Panel.SelectedLook == "Lantern Pilgrim");
        Check("saved_look_keyboard_closes_popup", !popup.Visible);
        CheckLayout("saved_preview");
    }
    private async Task EnterName(string name)
    {
        var edit = Find<LineEdit>("WardrobeLookName"); await Reveal(edit); PushClick(edit.GetGlobalRect().GetCenter()); await Frames();
        string hash = Session.StateHash; int frames = Session.CaptureReplay().Frames.Length;
        edit.SelectAll();
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = Key.Backspace, PhysicalKeycode = Key.Backspace, Pressed = pressed }, true);
        foreach (char letter in name)
        {
            var key = (Key)char.ToUpperInvariant(letter);
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = pressed ? letter : 0u, Pressed = pressed }, true);
        }
        await Frames(); Check("look_name_keyboard_input", edit.Text == name && Panel.IsOpen);
        Check("look_name_shortcuts_do_not_change_core", Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frames);
    }
    private void CheckLayout(string name)
    {
        var viewport = GetViewport().GetVisibleRect();
        foreach (string id in new[] { "AppearanceWardrobe", "WardrobeCatalog", "WardrobeApply", "WardrobeCancel", "WardrobeSlotHead", "WardrobePreviewFrame", "WardrobeSaveLook", "WardrobeLookName", "WardrobeSavedLooks" })
        { var control = Find<Control>(id); Check(name + "_" + id + "_fits", control.IsVisibleInTree() && viewport.Encloses(control.GetGlobalRect())); }
    }
    private void Refresh() { _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh"); }
    private void Step(EndgameRuntimeCommand command, bool refresh = true)
    {
        var result = Session.Execute(command); _commands++; if (!result.Success) throw new InvalidDataException(result.Reason + " / " + command.Action);
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
    private async Task Reveal(Control control)
    {
        for (Node? parent = control.GetParent(); parent is not null; parent = parent.GetParent()) if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(control);
        await Frames();
        if (!GodotObject.IsInstanceValid(control)) return;
        Check("target_visible_" + control.Name, control.IsVisibleInTree() && GetViewport().GetVisibleRect().HasPoint(control.GetGlobalRect().GetCenter()));
    }
    private void PushClick(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
    }
    private async Task Click(string name)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var button = Find<BaseButton>(name); await Reveal(button);
            // The async character catalog may replace a button while scrolling/layout settles.
            if (!GodotObject.IsInstanceValid(button) || !button.IsInsideTree()) continue;
            _lastInput = name;
            bool received = false; void Receipt() => received = true; button.Pressed += Receipt; PushClick(button.GetGlobalRect().GetCenter());
            _clicks++; await Frames(); if (GodotObject.IsInstanceValid(button)) button.Pressed -= Receipt; Check("click_received_" + _clicks, received);
            return;
        }
        throw new InvalidDataException("UI target did not settle: " + name);
    }
    private async Task Frames(int count = 4) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox?._Process(0); } }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-wardrobe")) return;
        GetWindow().GrabFocus(); await Frames(); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage(); Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Wardrobe check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private async Task Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "WardrobeSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            clicks = _clicks,
            commands = _commands,
            error,
            lastInput = _lastInput,
            scope = "Actual viewport wardrobe controls; seven actually equipped initial armor slots and earned Act I Vigil pair; draft/apply/cancel, helmet/reset, staged named looks, salvage retention, sidecar save/load and character isolation. Five discipline previews are explicitly detached cosmetic projections. Cosmetic actions preserve authoritative Core hash and replay history; earned campaign/equip/salvage commands replay normally."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "wardrobe-review.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report));
        if (_director is not null && GodotObject.IsInstanceValid(_director)) _director.QueueFree(); _director = null!; _sandbox = null!;
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GC.Collect();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GetTree().Quit(passed ? 0 : 1);
    }
}
