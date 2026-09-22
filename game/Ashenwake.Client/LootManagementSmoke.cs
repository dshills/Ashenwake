using System.Reflection;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;
using Position = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Disposable earned-character verification of shipping loot controls and authoritative transactions.</summary>
public partial class LootManagementSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [], _skipped = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _case = "loot";
    private int _commands, _replays;
    private bool _writeReport;
    private EndgameRuntimeSession Journey => Field<EndgameRuntimeSession>(_director, "_session");
    private ProductionHud Gear => Field<ProductionHud>(_director, "_character");
    private ProgressionSnapshot Snapshot => Journey.Production.Capture().Progression;
    private PermanentItem Item(long id) => Snapshot.Character.Items.Single(i => i.Id == id);

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--loot-management-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Loot smoke requires --loot-management-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director);
            _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            var fresh = (EndgameRuntimeSession)Call(_director, "Fresh", "Vanguard", null)!;
            Call(_director, "Adopt", fresh, false); await Frames();
            Gear.ToggleInventory(); await Frames();
            var firstSlot = Snapshot.Character.Equipment.Keys.First();
            long firstId = Snapshot.Character.Equipment[firstSlot];
            Click("GearEquipment" + firstSlot); await Frames();
            Click("FavoriteItem"); await Frames();
            Check("favorite_before_torren_rescue", Item(firstId).IsFavorite && !Journey.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"));
            Click("LockItem"); await Frames(); Check("lock_before_torren_rescue", Item(firstId).IsLocked);
            Click("FavoriteItem"); await Frames(); Click("LockItem"); await Frames(); Gear.Close();
            for (int i = 0; i < 2000 && !Journey.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"); i++)
            {
                JourneyStep(EndgameRuntimeSmoke.Next(Journey));
                if (i % 150 == 0) await Frames(1);
            }
            Check("earned_torren_rescue", Journey.Campaign.Capture().Campaign.RescuedResidents.Contains("Torren Bale"));
            JourneyStep(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)));
            Walk(new(2000, -2000), 2000); Call(_director, "Refresh");
            await LootFlow(); VerifyJourney("loot-management");
            Gear.Close(); await Frames();
            // Let the native audio mixer release the diagnostic's active score
            // before immediate process shutdown; no game state is changed here.
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Frames();
            Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private async Task LootFlow()
    {
        var equipment = Snapshot.Character.Equipment;
        var slot = equipment.ContainsKey(EquipmentSlot.Head) ? EquipmentSlot.Head : equipment.Keys.First();
        long id = equipment[slot];
        Permanent(new(ProductionAction.SaveEquipmentPreset, Id: "preset.1", Value: "Ashkeeper's Vigil"));
        Permanent(new(ProductionAction.Unequip, Slot: slot));
        Gear.ToggleInventory(); await Frames();
        Check("inventory_above_world_navigation", Gear.GetIndex() == Gear.GetParent().GetChildCount() - 1);
        Click("GearInventoryItem" + id); await Frames();
        Check("inspect_owned_item", Field<long>(Gear, "_gearItemId") == id);
        Check("saved_outfit_preview_warning", ProgressionSession.PreviewSalvage(Snapshot, id).PresetNames.Contains("Ashkeeper's Vigil"));
        Click("FavoriteItem"); await Frames(); Check("favorite_enabled", Item(id).IsFavorite);
        Check("favorite_disables_destructive_controls", Find<Button>(Gear, "DiscardItem").Disabled && Find<Button>(Gear, "SalvageItem").Disabled);
        Reject(new(ProductionAction.Discard, ItemId: id, ConfirmPermanent: true), "favorite_discard");
        Reject(new(ProductionAction.Salvage, ItemId: id, ConfirmPermanent: true), "favorite_salvage");
        Click("LockItem"); await Frames(); Check("lock_enabled", Item(id).IsLocked);
        VerifyJourney("protected-equipment");
        await Filters(id);
        await Capture("loot-protected.png", "FavoriteItem");
        Click("FavoriteItem"); await Frames(); Check("favorite_removed_independently", !Item(id).IsFavorite && Item(id).IsLocked);
        Reject(new(ProductionAction.Discard, ItemId: id, ConfirmPermanent: true), "locked_discard");
        Reject(new(ProductionAction.Salvage, ItemId: id, ConfirmPermanent: true), "locked_salvage");
        Click("LockItem"); await Frames(); Check("lock_removed", !Item(id).IsLocked);
        string beforeDiscard = Journey.StateHash;
        Click("DiscardItem"); await Frames();
        var discard = Find<ConfirmationDialog>(Gear, "GearDiscardConfirmation");
        Check("discard_warns_about_saved_outfits", discard.Visible && discard.DialogText.Contains("Ashkeeper's Vigil", StringComparison.Ordinal));
        discard.EmitSignal(ConfirmationDialog.SignalName.Canceled); await Frames();
        Check("discard_cancel_preserves_state", !discard.Visible && Journey.StateHash == beforeDiscard);
        var preview = ProgressionSession.PreviewSalvage(Snapshot, id);
        Check("salvage_preview_positive", preview.Success && preview.Materials > 0 && preview.ItemId == id);
        string hash = Journey.StateHash;
        Click("SalvageItem"); await Frames();
        var dialog = Find<ConfirmationDialog>(Gear, "GearSalvageConfirmation");
        Check("preview_confirms_exact_materials_and_outfit", dialog.Visible && dialog.DialogText.Contains(preview.Materials.ToString(), StringComparison.Ordinal) && dialog.DialogText.Contains("Ashkeeper's Vigil", StringComparison.Ordinal));
        Check("opening_confirmation_is_read_only", Journey.StateHash == hash);
        await Capture("loot-salvage-review.png");
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); await Frames();
        Check("cancel_preserves_state", !dialog.Visible && Journey.StateHash == hash);
        Click("SalvageItem"); await Frames();
        Permanent(new(ProductionAction.SetItemFavorite, ItemId: id, Value: "true")); await Frames();
        Check("revision_invalidates_confirmation", !dialog.Visible);
        string changed = Journey.StateHash; dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Frames();
        Check("stale_confirmation_cannot_salvage", Journey.StateHash == changed && Item(id).IsFavorite);
        Click("FavoriteItem"); await Frames();
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8);
        await Capture("loot-minimum-inventory.png", "GearUsageFilter");
        await Capture("loot-minimum-controls.png", "SalvageItem");
        Click("SalvageItem"); await Frames();
        Check("minimum_confirmation_fits", new Rect2(Vector2.Zero, GetWindow().Size).Encloses(new Rect2(dialog.Position, dialog.Size)));
        await Capture("loot-minimum-confirmation.png");
        int materials = Snapshot.Character.Materials;
        var remainingIds = Snapshot.Character.Items.Where(i => i.Id != id).Select(i => i.Id).ToHashSet();
        preview = ProgressionSession.PreviewSalvage(Snapshot, id);
        Check("confirmed_preview_still_valid", preview.Success);
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Frames();
        Check("salvage_removes_exact_owned_item", Snapshot.Character.Items.All(i => i.Id != id) && remainingIds.SetEquals(Snapshot.Character.Items.Select(i => i.Id)));
        Check("salvage_grants_exact_preview_materials", Snapshot.Character.Materials == materials + preview.Materials);
        Check("preset_retains_missing_item_reference", !Journey.Production.PreviewEquipmentPreset("preset.1").Success && Journey.Production.EquipmentPresets.Single().Equipment.Values.Contains(id));
        Check("salvage_dialog_closed", !dialog.Visible);
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames();
        Gear.ShowEquipmentPresets(); await Frames(); await Capture("loot-missing-preset.png");
    }

    private async Task Filters(long protectedId)
    {
        var filters = Find<OptionButton>(Gear, "GearUsageFilter");
        string[] labels = ["All gear", "Favorites", "Locked", "Unused", "Saved outfits"];
        Check("usage_filter_options", Enumerable.Range(0, filters.ItemCount).Select(filters.GetItemText).SequenceEqual(labels));
        for (int index = 0; index < labels.Length; index++)
        {
            filters.Select(index); filters.EmitSignal(OptionButton.SignalName.ItemSelected, index); await Frames();
            var state = Snapshot.Character;
            var expected = state.Items.Where(i => !state.Equipment.Values.Contains(i.Id)).Where(i => index switch
            {
                1 => i.IsFavorite,
                2 => i.IsLocked,
                3 => !Journey.Production.EquipmentPresets.Any(p => p.Equipment.Values.Contains(i.Id)),
                4 => Journey.Production.EquipmentPresets.Any(p => p.Equipment.Values.Contains(i.Id)),
                _ => true
            }).Select(i => i.Id).ToHashSet();
            var actual = Descendants(Gear).OfType<GearDragCard>().Where(c => c.Name.ToString().StartsWith("GearInventoryItem", StringComparison.Ordinal) && c.IsVisibleInTree())
                .Select(c => long.Parse(c.Name.ToString()[17..])).ToHashSet();
            Check("filter_visible_" + index, expected.SetEquals(actual));
            if (index is 1 or 2 or 4) Check("filter_contains_protected_" + index, actual.Contains(protectedId));
        }
        filters.Select(0); filters.EmitSignal(OptionButton.SignalName.ItemSelected, 0); await Frames();
    }

    private void Permanent(ProductionCommand command) => JourneyStep(new(EndgameRuntimeAction.Production, Production: command));
    private void Reject(ProductionCommand command, string name)
    {
        string hash = Journey.StateHash;
        var result = Journey.Execute(new(EndgameRuntimeAction.Production, Production: command)); _commands++;
        Check(name + "_rejected_atomically", !result.Success && Journey.StateHash == hash);
    }
    private void JourneyStep(EndgameRuntimeCommand command)
    {
        var result = Journey.Execute(command); _commands++;
        if (!result.Success) throw new InvalidDataException(result.Reason);
        Call(_director, "Observe", result); Call(_director, "Refresh"); _sandbox.PresentCombatEvents(result.CombatEvents, Journey.Combat);
    }
    private void Walk(Position destination, int range)
    {
        Gear.Close(); Field<CampaignHud>(_director, "_campaignHud").SetOpen(false); Field<EndgameHud>(_director, "_board").SetOpen(false); Call(_director, "CloseExperimentPanel");
        for (int i = 0; i < 300; i++)
        {
            var player = Journey.Combat.View.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, destination) <= (long)(range - 300) * (range - 300))
            { JourneyStep(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); return; }
            var direction = CombatProductionSmoke.MovementDirection(player.Position, destination, Journey.Room);
            JourneyStep(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]));
        }
        throw new InvalidDataException("Could not approach the requested Greyhaven service.");
    }
    private void VerifyJourney(string label)
    {
        string combat = Field<string>(_director, "_combatJson");
        var adventure = Field<Ashenwake.Core.Adventure.AdventureContent>(_director, "_adventure");
        var progression = Field<ProgressionContent>(_director, "_progression");
        var campaign = Field<CampaignContent>(_director, "_campaign"); var endgame = Field<EndgameContent>(_director, "_endgame");
        var replay = Journey.CaptureReplay(); var result = EndgameRuntimeReplayRunner.Run(combat, adventure, progression, campaign, endgame, replay);
        Check("journey_replay", result.Success && result.FinalHash == Journey.StateHash); _replays++;
        File.WriteAllText(Path.Combine(_output, label + ".journey.json"), JsonData.Write(replay));
        string path = Path.Combine(_output, label, "endgame.save.json");
        EndgameRuntimeSaveStore.Write(path, combat, adventure, progression, campaign, endgame, Journey.Capture());
        Check("save_load_exact", EndgameRuntimeSaveStore.Load(path, combat, adventure, progression, campaign, endgame).Session.StateHash == Journey.StateHash);
    }
    private async Task Capture(string filename, string? reveal = null)
    {
        if (reveal is not null)
        {
            var control = Find<Control>(Gear, reveal);
            for (Node? ancestor = control.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
                if (ancestor is ScrollContainer scroll) { scroll.EnsureControlVisible(control); break; }
        }
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            Call(_sandbox, "ResumePlaying"); await Frames(5);
            if (Field<PanelContainer>(_sandbox, "_resumePanel").Visible) continue;
            if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-loot-management")) { _skipped.Add(filename); return; }
            RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
            using var image = GetViewport().GetTexture().GetImage();
            Check("capture." + filename, image.GetWidth() == GetWindow().Size.X && image.GetHeight() == GetWindow().Size.Y && image.SavePng(Path.Combine(_output, filename)) == Error.Ok);
            _captures.Add(filename); return;
        }
        throw new InvalidDataException("Native capture remained obscured by interruption pause.");
    }
    private void Click(string name)
    {
        var button = Find<Button>(_sandbox, name);
        Check("click." + name, button.IsVisibleInTree() && !button.Disabled);
        button.EmitSignal(Button.SignalName.Pressed);
    }
    private async Task Frames(int count = 4)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Keep normal HUD layout and current values fresh without advancing
            // the diagnostic's exclusively command-driven simulation clock.
            _sandbox._Process(0);
        }
    }
    private void Check(string key, bool okay) { _checks[_case + "." + key] = okay; if (!okay) throw new InvalidDataException("Loot management smoke failed: " + _case + "." + key); }
    private static T Field<T>(object instance, string name) => (T)(instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance))!;
    private static object? Call(object instance, string name, params object?[] args)
    {
        try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static T Find<T>(Node root, string name) where T : Node => Descendants(root).OfType<T>().Single(n => n.Name == name);
    private static T Find<T>(Node root, Func<T, bool> predicate) where T : Node => Descendants(root).OfType<T>().Single(predicate);
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "LootManagementClientSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            skipped = _skipped,
            commands = _commands,
            replays = _replays,
            error,
            scope = "Shipping director and gear UI; fresh Vanguard earns Torren through ordinary campaign commands. Owned starter equipment only. Button and dialog signals test favorite/lock independence, protection, actual visible card filters, preset warnings, cancel/stale confirmation, exact salvage proceeds and source save/replay. Default and minimum window captures do not claim physical mouse gestures. Legendary extraction protection is covered by Core tests."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "loot-management-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
