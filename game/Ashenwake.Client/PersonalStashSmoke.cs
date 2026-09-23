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

/// <summary>Earned equipment, real viewport drag/drop and ordinary save/load through the live director.</summary>
public partial class PersonalStashSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly List<object> _dragEvidence = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _lastInput = "";
    private bool _writeReport;
    private int _commands, _clicks, _drags;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private PersonalStashPanel Panel => Field<PersonalStashPanel>(_director, "_stashPanel");
    private ProductionHud Hud => Field<ProductionHud>(_director, "_character");
    private ProgressionState Character => Session.Capture().Campaign.Production.Progression.Character;
    private PersonalStashDisplay Display => Field<PersonalStashDisplay>(Panel, "_view");

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--personal-stash-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Stash smoke requires --personal-stash-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            await Frames(); CloseMenus(); Call(_sandbox, "ResumePlaying");
            Open(); await Frames(); Check("fresh_stash_is_locked", !Display.CanTransfer && Display.AccessReason.Length > 0);
            CheckLayout("locked"); CloseMenus();
            for (int i = 0; i < 5000 && !Session.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"); i++)
            { Step(EndgameRuntimeSmoke.Next(Session), false); if (i % 150 == 0) await Frames(1); }
            Check("torren_earned", Session.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"));
            Step(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)));
            Walk(new(2000, -2000), 1200);
            var equipment = Character.Equipment; var slot = equipment.ContainsKey(EquipmentSlot.Head) ? EquipmentSlot.Head : equipment.Keys.First(); long id = equipment[slot];
            Change(new(ProductionAction.SaveEquipmentPreset, Id: "preset.1", Value: "Ashkeeper's Keepsakes"));
            Walk(new(-4500, -1800), 1200);
            Change(new(ProductionAction.SaveBuildLoadout, Id: "loadout.1", Value: "Iron Remembrance"));
            Walk(new(2000, -2000), 1200);
            Change(new(ProductionAction.Unequip, Slot: slot));
            Change(new(ProductionAction.SetItemFavorite, ItemId: id, Value: "true"));
            Change(new(ProductionAction.SetItemLocked, ItemId: id, Value: "true"));
            string itemJson = JsonData.Write(Character.Items.Single(i => i.Id == id));
            Walk(new(-4500, -1800), 600); Open(); await Frames();
            Check("remote_inspection_read_only", !Display.CanTransfer && Display.CanApproach);
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames();
            await Click("StashApproach");
            for (int i = 0; i < 500 && !Panel.IsOpen; i++) { _sandbox._Process(1d / 30); if (i % 10 == 0) await Frames(1); }
            Check("actual_mouse_chest_approach", Panel.IsOpen && Display.AtChest && Display.CanTransfer && _sandbox.IsPaused);
            Check("four_named_tabs", Display.Tabs.Length == 4 && Display.Tabs.Sum(t => t.Capacity) == 512);
            Panel.SetOpen(false); await Capture("personal-stash-chest.png"); Open(); await Frames();
            var cardsBeforeSelection = Descendants(Panel).OfType<GearDragCard>().Where(c => c.Name.ToString().StartsWith("StashItem_", StringComparison.Ordinal)).Select(c => c.GetInstanceId()).ToArray();
            await Click("StashItem_" + id);
            Check("same_tab_selection_preserves_cards", Panel.SelectedItem == id && cardsBeforeSelection.SequenceEqual(Descendants(Panel).OfType<GearDragCard>().Where(c => c.Name.ToString().StartsWith("StashItem_", StringComparison.Ordinal)).Select(c => c.GetInstanceId())));
            await Drag(id, "StashStorageDrop");
            Check("native_deposit", CharacterStash.TabForItem(Character, id) == "stash.1");
            Check("metadata_preserved_on_deposit", JsonData.Write(Character.Items.Single(i => i.Id == id)) == itemJson);
            Check("saved_references_preserved", Display.Items.Single(i => i.Id == id).References.Length == 2);
            Check("stored_not_combat_inventory", Session.Combat.View.Inventory.All(i => i.Id != id));
            CheckLayout("wide"); await Capture("personal-stash-protected.png");
            await Drag(id, "StashTab_stash_4");
            Check("native_tab_move", CharacterStash.TabForItem(Character, id) == "stash.4"); Panel.SelectTab("stash.4"); await Frames();
            var rename = Find<LineEdit>("StashTabName"); rename.GrabFocus(); rename.Text = "Ashkeeper's Keepsakes"; rename.EmitSignal(LineEdit.SignalName.TextChanged, rename.Text);
            string beforeTyping = Session.StateHash;
            foreach (Key key in new[] { Key.C, Key.I, Key.F, Key.J, Key.B, Key.H })
                foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Unicode = pressed ? (uint)char.ToLowerInvariant((char)key) : 0, Pressed = pressed }, true);
            await Frames(); Check("typing_never_opens_game_menus", Panel.IsOpen && Session.StateHash == beforeTyping && !Field<EndgameHud>(_director, "_board").IsOpen);
            rename.Text = "Ashkeeper's Keepsakes"; rename.EmitSignal(LineEdit.SignalName.TextChanged, rename.Text); await Click("StashRename");
            Check("tab_rename_saved", CharacterStash.TabName(Character, "stash.4") == "Ashkeeper's Keepsakes");
            var search = Find<LineEdit>("StashSearch"); search.Text = "Iron Remembrance"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text); await Frames();
            Check("search_saved_build_reference", VisibleCards().Contains(id) && VisibleCards().All(x => Display.Items.Single(i => i.Id == x).References.Any(r => r.Contains("Iron Remembrance", StringComparison.Ordinal))));
            search.Text = "No Such Relic"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text); await Frames(); Check("search_empty", !VisibleCards().Any());
            await Click("StashClearFilters"); var usage = Find<OptionButton>("StashUsageFilter"); usage.Select(2); usage.EmitSignal(OptionButton.SignalName.ItemSelected, 2L); await Frames();
            Check("locked_filter", VisibleCards().Contains(id) && VisibleCards().All(x => Character.Items.Single(i => i.Id == x).IsLocked));
            await Click("StashClearFilters"); Panel.RevealItem(id); await Frames();
            GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout("minimum"); await Capture("personal-stash-minimum.png");
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(8);
            await BeginDrag(id); Variant stale = GetViewport().GuiGetDragData(); Panel.CancelDrag(); await Frames();
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = Vector2.Zero, Pressed = false }, true); await Frames();
            Check("cancel_stale_drag_rejected", !Find<GearDragCard>("StashBackpackDrop")._CanDropData(Vector2.Zero, stale));
            RoundTrip("stored_save"); Open(); await Frames();
            Check("load_stale_drag_rejected", !Find<GearDragCard>("StashBackpackDrop")._CanDropData(Vector2.Zero, stale));
            Check("metadata_after_save", JsonData.Write(Character.Items.Single(i => i.Id == id)) == itemJson && CharacterStash.TabForItem(Character, id) == "stash.4");
            await ExistingMenus(id);
            CloseMenus(); Step(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.EnterAct, Act: 1)));
            Check("stored_excluded_after_enter_campaign", Session.Combat.View.Inventory.All(i => i.Id != id));
            RoundTrip("campaign_with_stored_item");
            Check("stored_excluded_after_campaign_load", Session.Combat.View.Inventory.All(i => i.Id != id) && CharacterStash.IsStored(Character, id));
            Step(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)));
            Walk(PersonalStashCatalog.Position, 700); Open(); Panel.RevealItem(id); await Frames();
            await Click("StashTransfer"); Check("button_retrieval", !CharacterStash.IsStored(Character, id) && JsonData.Write(Character.Items.Single(i => i.Id == id)) == itemJson);
            await Drag(id, "StashStorageDrop"); Check("second_native_deposit", CharacterStash.IsStored(Character, id));
            await Drag(id, "StashBackpackDrop"); Check("native_retrieve", !CharacterStash.IsStored(Character, id) && JsonData.Write(Character.Items.Single(i => i.Id == id)) == itemJson);
            CloseMenus(); Walk(new(2000, -2000), 1000); Change(new(ProductionAction.ApplyEquipmentPreset, Id: "preset.1"));
            Check("retrieved_outfit_applies", Character.Equipment.GetValueOrDefault(slot) == id);
            RoundTrip("final_save"); Check("complete_replay", Replay());
            Open(); await Frames(); await Capture("personal-stash-complete.png");
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Frames(); Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }
    private async Task ExistingMenus(long id)
    {
        CloseMenus(); Hud.ToggleInventory(); await Frames();
        Check("stored_excluded_from_backpack_ui", !Descendants(Hud).Any(n => n.Name == "GearInventoryItem" + id && n is Control c && c.IsVisibleInTree()));
        Hud.Close(); Hud.ShowEquipmentPresets(); await Frames();
        Check("stored_outfit_blocked", !Session.Production.PreviewEquipmentPreset("preset.1").Success && Find<Button>("EquipmentPresetApply").Disabled);
        Check("stored_outfit_retrieve_action", Find<Button>("EquipmentPresetStash").IsVisibleInTree());
        await Click("EquipmentPresetStash"); Check("outfit_opens_stash", Panel.IsOpen); CloseMenus();
        Hud.ShowBuildLoadouts(); await Frames();
        Check("stored_build_blocked", !Session.PreviewBuildLoadout("loadout.1").Success && Find<Button>("BuildLoadoutApply").Disabled);
        Check("stored_build_retrieve_action", Find<Button>("BuildLoadoutStash").IsVisibleInTree());
        await Click("BuildLoadoutStash"); Check("build_opens_stash", Panel.IsOpen);
    }
    private IEnumerable<long> VisibleCards() => Descendants(Panel).OfType<GearDragCard>().Where(c => c.IsVisibleInTree() && c.Name.ToString().StartsWith("StashItem_", StringComparison.Ordinal)).Select(c => long.Parse(c.Name.ToString()[10..])).Order();
    private async Task BeginDrag(long id)
    {
        Panel.RevealItem(id); await Frames(); var source = Find<Control>("StashItem_" + id); var start = source.GetGlobalRect().GetCenter();
        _lastInput = "Drag " + id + " from " + source.GetGlobalRect();
        GetViewport().PushInput(new InputEventMouseMotion { Position = start }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = start, Pressed = true, ButtonMask = MouseButtonMask.Left }, true); await Frames(1);
        for (int i = 1; i <= 4 && !GetViewport().GuiIsDragging(); i++)
        { GetViewport().PushInput(new InputEventMouseMotion { Position = start + new Vector2(i * 12, 0), Relative = new(12, 0), ButtonMask = MouseButtonMask.Left }, true); await Frames(1); }
        _drags++; Check("native_drag_started_" + _drags, GetViewport().GuiIsDragging());
    }
    private async Task Drag(long id, string target)
    {
        await BeginDrag(id); var zone = Find<GearDragCard>(target); var destination = zone.GetGlobalRect().GetCenter(); _lastInput += " to " + target + " at " + destination;
        Variant payload = GetViewport().GuiGetDragData();
        bool beforeValid = zone._CanDropData(Vector2.Zero, payload); long beforeEpoch = Field<long>(Panel, "_epoch");
        GetViewport().PushInput(new InputEventMouseMotion { Position = destination, ButtonMask = MouseButtonMask.Left }, true); await Frames();
        _dragEvidence.Add(new
        {
            drag = _drags,
            item = id,
            target,
            beforeValid,
            beforeEpoch,
            afterValid = zone._CanDropData(Vector2.Zero, payload),
            afterEpoch = Field<long>(Panel, "_epoch"),
            dragging = GetViewport().GuiIsDragging(),
            hovered = GetViewport().GuiGetHoveredControl()?.GetPath().ToString(),
            mouse = GetViewport().GetMousePosition().ToString(),
            destination = destination.ToString(),
            focused = GetWindow().HasFocus(),
            dirty = Field<bool>(Panel, "_dirty"),
            sessionKey = Display.SessionKey,
            itemTab = Display.Items.Single(i => i.Id == id).TabId
        });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = destination, Pressed = false }, true); await Frames();
        Check("native_drag_ended_" + _drags, !GetViewport().GuiIsDragging());
    }
    private void Open() => Call(_director, "OpenPersonalStash");
    private void Refresh() { _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh"); }
    private void CloseMenus() { Panel.SetOpen(false); Hud.Close(); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false); Field<EndgameHud>(_director, "_board").SetOpen(false); }
    private void Change(ProductionCommand command) => Step(new(EndgameRuntimeAction.Production, Production: command));
    private void Step(EndgameRuntimeCommand command, bool refresh = true)
    {
        var result = Session.Execute(command); _commands++; if (!result.Success) throw new InvalidDataException(result.Reason + " / " + command.Action);
        Call(_director, "Observe", result); if (refresh) Refresh();
    }
    private void Walk(Position target, int range)
    {
        CloseMenus();
        for (int i = 0; i < 600; i++)
        {
            var player = Session.Combat.View.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, target) <= (long)(range - 100) * (range - 100)) { Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); return; }
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target, Session.Room);
            Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]));
        }
        throw new InvalidDataException("Could not approach stash diagnostic location.");
    }
    private bool Replay() => EndgameRuntimeReplayRunner.Run(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), Session.CaptureReplay()).Success;
    private void RoundTrip(string name) { string hash = Session.StateHash; Check(name + "_replay", Replay()); Call(_director, "Save"); Call(_director, "Load"); Check(name, Session.StateHash == hash); }
    private async Task Click(string name)
    {
        await Frames(); var button = Find<Button>(name);
        for (Node? parent = button.GetParent(); parent is not null; parent = parent.GetParent()) if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(button);
        await Frames(); Check("clickable_" + name + "_" + _clicks, button.IsVisibleInTree() && !button.Disabled && GetViewport().GetVisibleRect().HasPoint(button.GetGlobalRect().GetCenter()));
        var point = button.GetGlobalRect().GetCenter(); _lastInput = name + " " + button.GetGlobalRect(); bool received = false; void Receipt() => received = true; button.Pressed += Receipt;
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
        _clicks++; await Frames(); if (GodotObject.IsInstanceValid(button)) button.Pressed -= Receipt; Check("click_received_" + name + "_" + _clicks, received);
    }
    private void CheckLayout(string suffix)
    {
        var viewport = GetViewport().GetVisibleRect();
        foreach (string name in new[] { "PersonalStashPanel", "StashTabs", "StashContents", "StashBackpackDrop", "StashStorageDrop", "StashDetailsScroll", "StashClose" })
        { var control = Find<Control>(name); if (!viewport.Encloses(control.GetGlobalRect())) GD.Print(name + "=" + control.GetGlobalRect() + " minimum=" + control.GetCombinedMinimumSize()); Check(name + "_fits_" + suffix, viewport.Encloses(control.GetGlobalRect())); }
    }
    private async Task Frames(int count = 4) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox?._Process(0); } }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-personal-stash")) return;
        GetWindow().GrabFocus(); await Frames(); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage(); Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Stash check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "PersonalStashSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            clicks = _clicks,
            drags = _drags,
            dragEvidence = _dragEvidence,
            commands = _commands,
            error,
            lastInput = _lastInput,
            notice = _director is null ? "" : Field<string>(_director, "_stashNotice"),
            stored = _director is null ? null : Character.Stash,
            scope = "Earned Torren rescue; real chest mouse approach and native deposit/move/retrieve gestures; protected metadata, saved outfit/build references, filters, tab rename, keyboard isolation, stale payload rejection, campaign projections, save/load/replay, and 1280/780 layouts."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "personal-stash-review.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
