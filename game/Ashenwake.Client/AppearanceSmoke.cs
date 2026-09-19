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

/// <summary>Exercises real equipment transactions and cosmetic inspection in an isolated character.</summary>
public partial class AppearanceSmoke : Node3D
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private string _output = "";
    private bool _writeReport;
    private ProductionSession _session = null!;
    private Sandbox _sandbox = null!;
    private ProductionHud _hud = null!;
    private AdventureStage _stage = null!;
    private RoomDefinition _room = null!;
    private int _commands, _equips, _unequips;
    private long _initialMainHand;
    private static string Read(string name) => Godot.FileAccess.GetFileAsString($"res://{name}.json");

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--appearance-smoke") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Appearance smoke requires --appearance-smoke --output=<fresh-artifact-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            AppearanceVisualChecks.Run((passed, name) => Check(name, passed));
            _session = ProductionSession.Create(Read("combat"), AdventureContent.Parse(Read("adventure")), ProgressionContent.Parse(Read("progression")));
            _sandbox = new Sandbox(); AddChild(_sandbox); _sandbox.EnableCampaign();
            _room = CombatContent.Parse(Read("combat")).Room;
            _stage = new AdventureStage(); AddChild(_stage);
            _hud = new ProductionHud { Catalog = TextCatalog.Parse(Read("text.en")) }; _sandbox.AddOverlay(_hud);
            _hud.EquipRequested += (id, slot) => { Require(_session.Equip(id, slot)); _equips++; Refresh(); };
            _hud.UnequipRequested += slot => { Require(_session.Unequip(slot)); _unequips++; Refresh(); };
            _sandbox.SetSession(_session.Combat); _sandbox.SetPaused(true); Refresh();
            await EquipmentFlow();
            await DungeonFlow();
            await Gallery();
            Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private void Refresh()
    {
        _sandbox.AdoptSession(_session.Combat); _sandbox.SetPaused(true);
        var snapshot = _session.Capture(); var world = _session.View;
        var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        var interactions = _session.Interactions.Select(i => new InteractionDisplay(i.ActionId, i.Name,
            (int)Math.Sqrt(CorePosition.DistanceSquared(i.Position, player.Position)), i.Range)).ToArray();
        _hud.SetView(_session.ProgressionView, snapshot.Progression, _session.Content.Capture(), _session.Combat.View,
            interactions, world.RoomId == "room.greyhaven", snapshot.OperationSequence, _session.Combat.ProgressionBuild.UnlockedMutations);
        _hud.SetAppearance(CharacterAppearance.FromProgression(snapshot.Progression, world.ActiveManifestations,
            snapshot.Expedition.Adventure.Anatomy.Values));
        _sandbox.SetManifestationPresentation(world.ActiveManifestations);
        _stage.ShowRoom(world.RoomId, world.BellPhase, _room, world.ActiveManifestations,
            _session.Interactions.ToDictionary(i => i.ActionId, i => i.Position), snapshot.Expedition.Adventure.DestroyedAnchors, _session.ProgressionView.HubStage);
        _sandbox.PresentAuthoredRoom(_room, world.RoomId, EnvironmentGround.Style(world.RoomId == "room.greyhaven", world.RoomId));
    }

    private async Task EquipmentFlow()
    {
        WalkTo("service.torren"); Refresh();
        _initialMainHand = _session.Capture().Progression.Character.Equipment[EquipmentSlot.MainHand];
        Check("torren_opens_equipment", _hud.PresentInteraction("ServiceOpened:service.torren"));
        await Frames();
        var preview = Find<CharacterPreview>("CharacterPreview");
        string initial = _session.StateHash;
        Check("world_and_preview_use_equipped_state", preview.AppearanceKey == _sandbox.CurrentAppearance.Key && preview.Rendering);
        await Capture("equipment-equipped.png");
        SelectSlot(EquipmentSlot.MainHand);
        SelectItem(0);
        await Frames();
        Check("inspection_never_equips", _session.StateHash == initial && _equips == 0 && _unequips == 0);
        Check("empty_slot_preview_removes_weapon", !Descendants(preview).Any(n => n.Name == "EquipmentMainHand") &&
            Descendants(_sandbox.GetNode("Actor1")).Any(n => n.Name == "EquipmentMainHand"));
        float angle = preview.RotationAngle;
        Find<Button>("RotateRight").EmitSignal(Button.SignalName.Pressed);
        await Frames();
        Check("manual_rotation_works_while_paused_without_state_changes", _sandbox.IsPaused && preview.RotationAngle != angle && _session.StateHash == initial);
        var pose = Pose(preview); await Frames(8);
        Check("paused_preview_stays_still", Same(pose, Pose(preview)));
        Find<Button>("ResetRotation").EmitSignal(Button.SignalName.Pressed);
        var unequip = Find<Button>("UnequipItem"); Check("explicit_unequip_available_at_torren", !unequip.Disabled);
        unequip.EmitSignal(Button.SignalName.Pressed); await Frames();
        Check("unequip_updates_world_and_preview", _unequips == 1 && _sandbox.CurrentAppearance.MainHand.DefinitionId == "" && preview.AppearanceKey == _sandbox.CurrentAppearance.Key);
        var item = _session.Capture().Progression.Character.Items.First(i => i.DefinitionId == "item.ashcleaver");
        SelectItem(item.Id); await Frames();
        Check("ashcleaver_preview_is_distinct", preview.AppearanceKey != _sandbox.CurrentAppearance.Key && _equips == 0);
        await Capture("equipment-ashcleaver-preview.png");
        var equip = Find<Button>("EquipItem"); Check("explicit_equip_available_at_torren", !equip.Disabled);
        equip.EmitSignal(Button.SignalName.Pressed); await Frames();
        Check("equipping_ashcleaver_updates_both_models", _equips == 1 && _sandbox.CurrentAppearance.MainHand.DefinitionId == "item.ashcleaver" && preview.AppearanceKey == _sandbox.CurrentAppearance.Key);
        await Capture("equipment-ashcleaver-equipped.png");
        await AnatomyEvolutionPreview();
        var window = GetWindow();
        window.ContentScaleSize = new(1280, 720); window.Size = new(1280, 720); await Frames(5);
        var close = Descendants(_hud).OfType<Button>().Single(b => b.Text == "Close character");
        Check("equipment_panel_fits_720p", close.GetGlobalRect().End.Y <= _hud.GetViewportRect().Size.Y && preview.GetGlobalRect().End.X < _hud.GetViewportRect().Size.X);
        await Capture("equipment-720p.png");
        window.ContentScaleSize = new(780, 800); window.Size = new(780, 800); await Frames(5);
        Check("compact_equipment_panel_fits", close.GetGlobalRect().End.Y <= _hud.GetViewportRect().Size.Y && close.GetGlobalRect().End.X <= _hud.GetViewportRect().Size.X);
        await Capture("equipment-compact.png");
        window.ContentScaleSize = new(1280, 800); window.Size = new(1280, 800); await Frames(5);
        SelectSlot(EquipmentSlot.Head); SelectItem(0); await Frames();
        Check("head_inspection_removes_helmet", !Descendants(preview).Any(n => n.Name == "EquipmentHead"));
        Find<Button>("ResetGearPreview").EmitSignal(Button.SignalName.Pressed); await Frames();
        Check("reset_preview_restores_equipment", preview.AppearanceKey == _sandbox.CurrentAppearance.Key);
        string beforeClose = _session.StateHash;
        _hud.Toggle(); await Frames();
        Check("hidden_preview_stops_rendering", !preview.Rendering && !preview.IsProcessing() && _session.StateHash == beforeClose);
        WalkTo("npc.mara"); Refresh();
        _hud.PresentInteraction("ServiceOpened:service.torren"); SelectSlot(EquipmentSlot.MainHand); SelectItem(0); await Frames();
        Check("inspection_away_from_torren_cannot_commit", Find<Button>("UnequipItem").Disabled);
        _hud.Toggle();
    }

    private async Task AnatomyEvolutionPreview()
    {
        // Use the real equipped-item fixture and vary only the cosmetic evolution metadata supplied to the UI.
        // This is a presentation contract check, not a claim that this fresh character earned an awakening.
        var snapshot = _session.Capture(); var combat = _session.Combat.View;
        string hash = _session.StateHash; int operations = _session.CaptureReplay().Frames.Length;
        var workbench = new AnatomyWorkbench { Position = new(20, 20), Size = new(1000, 680) };
        _sandbox.AddOverlay(workbench);
        void Press(string name) => Descendants(workbench).OfType<Button>().Single(b => b.Name == name).EmitSignal(Button.SignalName.Pressed);
        try
        {
            foreach (string evolution in new[] { "Awakened", "Serath", "Orrun" })
            {
                workbench.SetView(_session.AdventureContent, snapshot.Expedition.Adventure, combat, _session.View.ActiveManifestations,
                    inHub: true, canApply: false, canReturn: false, groundLoot: 0, ashcleaverEvolution: evolution);
                var installed = CharacterAppearance.FromCombat(combat, _session.View.ActiveManifestations, evolution);
                Press("AnatomyCharacterView"); await Frames();
                Check("anatomy_preview_preserves_" + evolution.ToLowerInvariant() + "_equipped_weapon", workbench.PreviewAppearanceKey == installed.Key);
                Press("AnatomyBodyView"); Press("AnatomySlotEyes"); Press("AnatomyEmptySlot"); await Frames();
                var removed = installed with { AnatomyMask = installed.AnatomyMask & ~1 };
                Check("anatomy_removal_preview_preserves_" + evolution.ToLowerInvariant() + "_weapon", workbench.Inspecting && workbench.PreviewAppearanceKey == removed.Key);
                Press("AnatomyReset"); await Frames();
                Check("anatomy_reset_preserves_" + evolution.ToLowerInvariant() + "_weapon", !workbench.Inspecting && workbench.PreviewAppearanceKey == installed.Key);
            }
            Check("anatomy_evolution_inspection_never_changes_core_or_history", _session.StateHash == hash && _session.CaptureReplay().Frames.Length == operations);
            workbench.Hide(); await Frames();
            var preview = Descendants(workbench).OfType<CharacterPreview>().Single();
            Check("anatomy_evolution_preview_stops_rendering_when_hidden", !preview.Rendering && !preview.IsProcessing());
        }
        finally { workbench.GetParent().RemoveChild(workbench); workbench.QueueFree(); }
        await Frames();
    }

    private async Task DungeonFlow()
    {
        // Restore the normal smoke loadout so this cosmetic test does not rebalance its combat policy.
        WalkTo("service.torren");
        Require(_session.Equip(_initialMainHand, EquipmentSlot.MainHand));
        bool sawLoot = false, collected = false, stone = false; long lootId = 0;
        for (int i = 0; i < ProductionSmoke.MaximumCommands && !ProductionSmoke.Complete(_session); i++)
        {
            Require(_session.Execute(ProductionSmoke.Next(_session))); _commands++;
            if (!stone && _session.View.ActiveManifestations.Contains("manifestation.stone_memory"))
            {
                Refresh(); stone = true;
                Check("real_fragment_install_adds_stone_form", _sandbox.CurrentAppearance.ManifestationMask == 4 &&
                    Descendants(_sandbox.GetNode("Actor1")).Any(n => n.Name == "ManifestationStone"));
            }
            if (!sawLoot && _session.Combat.View.Loot.Count > 0)
            {
                Refresh(); var drop = _session.Combat.View.Loot[0]; lootId = drop.Id; sawLoot = true;
                Check("real_drop_uses_item_visual", _sandbox.GetNodeOrNull<LootVisual>($"Loot{drop.Id}")?.AppearanceKey == LootVisual.KeyFor(drop.Item));
                string lootState = _session.StateHash;
                var filter = Descendants(_sandbox).OfType<OptionButton>().Single(n => n.Name == "LootRarityFilter");
                filter.Select(5); filter.EmitSignal(OptionButton.SignalName.ItemSelected, 5);
                Check("loot_filter_updates_while_paused_without_deleting_items", _sandbox.GetChildren().OfType<LootVisual>().Count() == _session.Combat.View.Loot.Count(l => l.Item.Rarity == "Godwrought") && _session.StateHash == lootState);
                Input.ActionPress("aw_showloot"); await Frames();
                Check("show_all_reveals_filtered_loot_while_paused", _sandbox.GetChildren().OfType<LootVisual>().Count() == _session.Combat.View.Loot.Count && _session.StateHash == lootState);
                Input.ActionRelease("aw_showloot"); await Frames();
                Check("releasing_show_all_restores_filter", _sandbox.GetChildren().OfType<LootVisual>().Count() == _session.Combat.View.Loot.Count(l => l.Item.Rarity == "Godwrought"));
                filter.Select(0); filter.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
                await Capture("dungeon-loot.png");
            }
            if (sawLoot && !collected && !_session.Combat.View.Loot.Any(l => l.Id == lootId))
            {
                Refresh(); collected = true;
                Check("collected_drop_leaves_world", _sandbox.GetNodeOrNull<LootVisual>($"Loot{lootId}") is null);
            }
            if (i % 120 == 0) await Frames(1);
        }
        Check("dungeon_completed_with_manifestation_and_loot", ProductionSmoke.Complete(_session) && stone && sawLoot && collected);
        Refresh();
        Check("return_to_town_clears_dungeon_loot", !_sandbox.GetChildren().OfType<LootVisual>().Any());
        string save = Path.Combine(_output, "appearance.save.json");
        ProductionSaveStore.Write(save, Read("combat"), AdventureContent.Parse(Read("adventure")), ProgressionContent.Parse(Read("progression")), _session.Capture());
        var restored = ProductionSaveStore.Load(save, Read("combat"), AdventureContent.Parse(Read("adventure")), ProgressionContent.Parse(Read("progression"))).Session;
        Check("save_reload_preserves_state_and_appearance", restored.StateHash == _session.StateHash &&
            CharacterAppearance.FromProgression(restored.Capture().Progression, restored.View.ActiveManifestations,
                restored.Capture().Expedition.Adventure.Anatomy.Values).Key == _sandbox.CurrentAppearance.Key);
        var replay = ProductionReplayRunner.Run(Read("combat"), AdventureContent.Parse(Read("adventure")), ProgressionContent.Parse(Read("progression")), _session.CaptureReplay());
        Check("equipment_and_dungeon_replay_match", replay.Success);
    }

    private async Task Gallery()
    {
        RemoveChild(_sandbox); _sandbox.QueueFree(); RemoveChild(_stage); _stage.QueueFree();
        var gallery = new Node3D(); AddChild(gallery);
        gallery.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new("101925"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new("a8c0d0"),
                AmbientLightEnergy = .72f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        });
        gallery.AddChild(new DirectionalLight3D { RotationDegrees = new(-40, -30, 0), LightColor = new("ffe2bc"), LightEnergy = 1.15f });
        gallery.AddChild(new DirectionalLight3D { RotationDegrees = new(-20, 140, 0), LightColor = new("8cc9dd"), LightEnergy = .5f });
        var camera = new Camera3D { Current = true, Projection = Camera3D.ProjectionType.Orthogonal, Position = new(0, 4, -14), Size = 11.8f };
        gallery.AddChild(camera); camera.LookAt(new(0, 1.3f, 0));
        var canvas = new CanvasLayer(); AddChild(canvas);
        var title = new Label { Text = "DIVINE MANIFESTATIONS", Position = new(55, 42) }; title.AddThemeFontSizeOverride("font_size", 30); canvas.AddChild(title);
        var subtitle = new Label { Text = "Burning Blood · Whispering Shadow · Stone Memory · Voracious Renewal", Position = new(55, 89) }; canvas.AddChild(subtitle);
        var appearance = CharacterAppearance.FromProgression(_session.Capture().Progression);
        var actors = new Node3D(); gallery.AddChild(actors);
        for (int i = 0; i < 4; i++)
        {
            var visual = CharacterVisual.Create("player", "Player", appearance.Discipline, appearance: appearance with { ManifestationMask = 1 << i });
            visual.Position = new((1.5f - i) * 2.6f, 0, 0); actors.AddChild(visual);
            for (int j = 0; j < 90; j++) visual.Animate(1.0 / 60, Vector3.Zero, facing: new Vector3(.15f, 0, -1));
        }
        await Capture("manifestations.png");
        gallery.RemoveChild(actors); actors.QueueFree();
        title.Text = "EQUIPMENT / LOOT"; subtitle.Text = "Recognizable ground items · six rarity tiers indicated by color and counted pips";
        string[] ids = ["ash_axe", "cinder_edge", "oath_hammer", "pilgrim_pike", "greatstaff", "ashcleaver", "starter_head", "march_plate", "ash_weave", "starter_offhand", "echo_ring", "ember_lens"];
        string[] slots = ["MainHand", "MainHand", "MainHand", "MainHand", "MainHand", "MainHand", "Head", "Chest", "Chest", "OffHand", "Ring1", "Amulet"];
        string[] rarities = ["Common", "Tempered", "Rare", "Relic", "Legendary", "Godwrought"];
        camera.Position = new(0, 9, -6); camera.LookAt(Vector3.Zero); camera.Size = 9;
        for (int i = 0; i < ids.Length; i++)
        {
            var visual = LootVisual.Create(new(i + 1, "item." + ids[i], ids[i], slots[i], rarities[i % 6], 0, 0, 0));
            visual.Position = new((2.5f - i % 6) * 1.55f, 0, i < 6 ? 1 : -1); gallery.AddChild(visual);
            var label = new Label3D
            {
                Text = ids[i].Replace('_', ' ') + "\n" + rarities[i % 6],
                Position = visual.Position + new Vector3(0, .1f, .67f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 22,
                PixelSize = .008f,
                NoDepthTest = true
            };
            gallery.AddChild(label);
        }
        await Capture("loot-catalog.png");
    }

    private void WalkTo(string service)
    {
        var action = new ProductionCommand(ProductionAction.Expedition, new(ExpeditionAction.Interact, service));
        for (int i = 0; i < 300; i++)
        {
            var next = ProductionSmoke.AtInteraction(_session, service, action);
            if (next == action) return;
            Require(_session.Execute(next)); _commands++;
        }
        throw new InvalidDataException("Could not reach " + service);
    }
    private T Find<T>(string name) where T : Node => Descendants(_hud).OfType<T>().Single(n => n.Name == name);
    private void SelectSlot(EquipmentSlot slot)
    { var choice = Find<OptionButton>("GearSlot"); int index = choice.GetItemIndex((int)slot); choice.Select(index); choice.EmitSignal(OptionButton.SignalName.ItemSelected, index); }
    private void SelectItem(long id)
    {
        var choice = Find<OptionButton>("GearItem"); int index = Enumerable.Range(0, choice.ItemCount).Single(i => choice.GetItemMetadata(i).AsInt64() == id);
        choice.Select(index); choice.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static Transform3D[] Pose(Node root) => Descendants(root).OfType<Node3D>().Select(n => n.Transform).ToArray();
    private static bool Same(Transform3D[] a, Transform3D[] b) => a.Length == b.Length && a.Zip(b).All(p => p.First.IsEqualApprox(p.Second));
    private async Task Frames(int count = 3) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string name)
    {
        await Frames();
        if (!OS.GetCmdlineUserArgs().Contains("--capture-appearance") || DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private static void Require(ProductionResult result) { if (!result.Success) throw new InvalidDataException(result.Reason); }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Appearance check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "AppearanceClientSmokePassed" : "AppearanceClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            commands = _commands,
            error,
            scope = "Appearance geometry, cosmetic preview isolation, explicit permanent equipment, pause, real fragment manifestation, dungeon loot pickup, save and replay. Galleries show gameplay assets."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "appearance-smoke.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
