using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Real rescue, follower, UI customization and canonical material pickup through ordinary play.</summary>
public partial class PetsSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _lastInput = "";
    private bool _writeReport;
    private int _commands, _clicks;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private PetPanel Panel => Field<PetPanel>(_director, "_pets");
    private PetDisplay Display => Field<PetDisplay>(Panel, "_view");
    private PetPresentation WorldPets => Field<PetPresentation>(_director, "_petPresentation");
    private PetPreview Preview => Descendants(Panel).OfType<PetPreview>().Single();
    private CampaignHud Journey => Field<CampaignHud>(_director, "_campaignHud");
    private string PetHash => JsonData.Hash(Session.Capture().Pets);
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs(); _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--pets-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Pets smoke requires --pets-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            await Frames(); CloseMenus(); Call(_sandbox, "ResumePlaying");
            await Rescue(); await Customize(); await Gather(); await CharacterIsolation(); await VisualGallery();
            Check("final_replay", Replay());
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); await Finish(false, ex.Message); }
    }
    private async Task Rescue()
    {
        Check("fresh_pet_feature_enabled_without_companion", Session.Pets.Enabled && Session.Pets.Entries.All(e => !e.Rescued) && !WorldPets.FollowerVisible && !Session.Pets.AutoGather);
        await Open(); string hash = Session.StateHash;
        await Click("PetCatalog_pet_gloammoth"); Check("future_companion_hidden", !Preview.Rendering && Display.Entries.Single(e => e.Id == "pet.gloammoth").Name == "");
        CheckLayout("unknown"); await Capture("pets-undiscovered.png"); CloseMenus(); Check("inspection_preserves_core", Session.StateHash == hash);
        for (int i = 0; i < 3500 && !(Session.Campaign.ActiveEncounterId == "campaign.road" && Session.EncounterCleared); i++)
        { CampaignStep(); if (i % 100 == 0) await Frames(1); }
        Check("genuine_road_clear", Session.Campaign.ActiveEncounterId == "campaign.road" && Session.EncounterCleared);
        Check("rescuer_is_world_interaction", Session.Interactions.Any(i => i.ActionId == "pet.ashfox.rescue") && WorldPets.GetInteractionVisual("pet.ashfox.rescue") is not null);
        await Approach(PetCatalog.RescuePosition(Session.Room)); CloseMenus();
        int actors = Session.Combat.View.Actors.Count; string combat = JsonData.Hash(Session.Combat.Capture());
        await KeyPress(Key.F);
        Check("world_F_rescues_and_selects_first_pet", Session.Pets.SelectedPetId == "pet.ashfox" && Session.Pets.Entries.Single(e => e.Id == "pet.ashfox").Rescued);
        Check("rescue_does_not_spawn_combat_actor", actors == Session.Combat.View.Actors.Count && combat == JsonData.Hash(Session.Combat.Capture()));
        Check("selected_pet_visible_in_world", WorldPets.FollowerVisible && WorldPets.SelectedPetId == "pet.ashfox");
        await Capture("pets-rescued-in-world.png"); await RoundTrip("rescued");
    }
    private async Task Customize()
    {
        await Open(); await Click("PetCatalog_pet_ashfox");
        string hash = Session.StateHash; int frames = Session.CaptureReplay().Frames.Length;
        var edit = Find<LineEdit>("PetName"); await Reveal(edit); PushClick(edit.GetGlobalRect().GetCenter()); await Frames(); edit.SelectAll();
        await KeyPress(Key.Backspace);
        foreach (char c in "Moonbeam")
        {
            foreach (bool down in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = (Key)char.ToUpperInvariant(c), PhysicalKeycode = (Key)char.ToUpperInvariant(c), Unicode = down ? c : 0u, Pressed = down }, true);
        }
        await Frames(); Check("name_typing_does_not_trigger_map_or_commands", edit.Text == "Moonbeam" && !_sandbox.LocalMapOpen && Panel.IsOpen && Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frames);
        await Click("PetRename"); Check("name_saved", Session.Pets.Entries.Single(e => e.Id == "pet.ashfox").Name == "Moonbeam");
        edit = Find<LineEdit>("PetName"); await Reveal(edit); PushClick(edit.GetGlobalRect().GetCenter()); await Frames();
        Check("rename_field_focused", edit.HasFocus()); await KeyPress(Key.Escape);
        Check("escape_closes_focused_name_field", !Panel.IsOpen); await Open();
        await Click("PetAppearance_ivory"); Check("appearance_saved", Session.Pets.Entries.Single(e => e.Id == "pet.ashfox").AppearanceId == "ivory");
        await Click("PetRotateRight"); Check("preview_rotation", Math.Abs(Preview.RotationAngle) > .01f);
        await Click("PetResetRotation"); Check("preview_rotation_reset", Math.Abs(Preview.RotationAngle) < .001f);
        var surface = Find<Control>("PetPreviewSurface"); await Reveal(surface); var start = surface.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseButton { Position = start, ButtonIndex = MouseButton.Left, Pressed = true }, true);
        GetViewport().PushInput(new InputEventMouseMotion { Position = start + new Vector2(50, 0), Relative = new(50, 0), ButtonMask = MouseButtonMask.Left }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = start + new Vector2(50, 0), ButtonIndex = MouseButton.Left, Pressed = false }, true);
        Check("preview_drag_rotates", Math.Abs(Preview.RotationAngle) > .01f);
        CheckLayout("wide"); await Capture("pets-wide.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout("compact"); await Capture("pets-compact.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(8);
        await Click("PetDismiss"); Check("dismiss_hides_follower", Session.Pets.SelectedPetId == "" && !WorldPets.FollowerVisible);
        await Click("PetSelect"); Check("reselect_restores_follower", WorldPets.FollowerVisible && Session.Pets.SelectedPetId == "pet.ashfox");
        long tick = Session.Tick; var position = WorldPets.FollowerPosition; await Frames(10);
        Check("modal_pauses_pet_and_game", Session.Tick == tick && position == WorldPets.FollowerPosition);
        CloseMenus(); await RoundTrip("customized");
    }
    private async Task Gather()
    {
        int materials = Session.Production.ProgressionView.Materials;
        var before = WorldPets.FollowerPosition;
        await Approach(PetCatalog.CachePosition(Session.Room)); await Frames(30);
        Check("follower_moves_with_player", WorldPets.FollowerPosition.DistanceTo(before) > .25f);
        var player = Session.Combat.View.Actors.Single(a => a.Id == 1).Position;
        Check("follower_remains_close", WorldPets.FollowerPosition.DistanceTo(new(player.X * .001f, 0, player.Z * .001f)) < 10);
        Step(new(EndgameRuntimeAction.Tick)); Check("gather_off_preserves_cache", Session.Production.ProgressionView.Materials == materials && Session.PetInteractions.Any(i => i.ActionId.StartsWith("pet.materials.", StringComparison.Ordinal)));
        await Open(); await Click("PetGather"); Check("gather_toggle_saved", Session.Pets.AutoGather); CloseMenus();
        Step(new(EndgameRuntimeAction.Tick)); Check("gather_adds_exact_five_materials", Session.Production.ProgressionView.Materials == materials + 5);
        Check("cache_consumed", Session.Capture().Pets!.CollectedMaterials.Length == 1 && !Session.PetInteractions.Any(i => i.ActionId.StartsWith("pet.materials.", StringComparison.Ordinal)));
        await RoundTrip("gathered"); Step(new(EndgameRuntimeAction.Tick)); Check("restore_cannot_collect_twice", Session.Production.ProgressionView.Materials == materials + 5);
        Check("pet_replay_valid", Replay());
        Step(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub))); await Frames();
        Check("pet_follows_between_scenes", Session.InHub && WorldPets.FollowerVisible);
        await Capture("pets-home.png"); await RoundTrip("home");
    }
    private async Task CharacterIsolation()
    {
        string filename = Field<string>(_director, "_saveName"), hash = Session.StateHash, pets = PetHash;
        Call(_director, "ShowFrontMenu"); await Frames(); Check("front_menu_hides_follower", !WorldPets.FollowerVisible);
        await Click("FrontNew"); await Click("FrontDisciplineWarden"); await Click("FrontBegin"); CloseMenus();
        Check("new_character_has_no_pets", Field<string>(_director, "_saveName") != filename && Session.Pets.Entries.All(e => !e.Rescued) && !WorldPets.FollowerVisible);
        Call(_director, "ShowFrontMenu"); await Frames(); var menu = Field<FrontMenu>(_director, "_frontMenu"); ulong deadline = Time.GetTicksMsec() + 10000;
        while (menu.CatalogLoading && Time.GetTicksMsec() < deadline) await Frames();
        await Click("FrontCharacters"); var slotsField = menu.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Single(f => f.FieldType == typeof(CharacterSlot[]));
        var slots = (CharacterSlot[])slotsField.GetValue(menu)!; int index = Array.FindIndex(slots, s => s.Filename == filename);
        Check("original_character_found", index >= 0); await Click("FrontSlot" + index); await Click("FrontPlay"); CloseMenus(); await Frames();
        Check("original_pet_restored", Session.StateHash == hash && PetHash == pets && WorldPets.FollowerVisible);
    }
    private async Task VisualGallery()
    {
        string hash = Session.StateHash; CloseMenus(); _sandbox.SetModalPaused("pet-gallery", true);
        var backdrop = new ColorRect { Color = new("101b24"), Position = new(16, 16), Size = new(1248, 724) }; _sandbox.AddOverlay(backdrop);
        var row = new HBoxContainer { Position = new(30, 80), Size = new(1220, 620) }; _sandbox.AddOverlay(row);
        var heading = new Label { Text = "COMPANION MODEL CHECK · DETACHED VISUAL SAMPLES", Position = new(32, 26) }; _sandbox.AddOverlay(heading);
        foreach (var d in PetCatalog.Definitions)
        {
            var frame = new Control { CustomMinimumSize = new(390, 600) }; row.AddChild(frame);
            var preview = new PetPreview(); frame.AddChild(preview); preview.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); preview.SetCompact(false, 570); preview.SetPet(d.Id, d.Appearances[0].Id);
        }
        await Frames(10); await Capture("pets-model-gallery.png");
        row.QueueFree(); backdrop.QueueFree(); heading.QueueFree(); await Frames(); _sandbox.SetModalPaused("pet-gallery", false);
        Check("detached_gallery_preserves_ownership", Session.StateHash == hash);
    }
    private async Task Approach(CorePosition target)
    {
        for (int i = 0; i < 800; i++)
        {
            var player = Session.Combat.View.Actors.Single(a => a.Id == 1).Position;
            if (CorePosition.DistanceSquared(player, target) <= 400L * 400 && new SpatialWorld(Session.Room).HasLineOfSight(player, target)) return;
            var direction = CombatProductionSmoke.MovementDirection(player, target, Session.Room);
            Step(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: direction.X, Z: direction.Z)]));
            if (i % 10 == 0) await Frames(1);
        }
        throw new InvalidDataException("Pet approach stalled.");
    }
    private async Task RoundTrip(string name)
    {
        string hash = Session.StateHash, pets = PetHash; Call(_director, "Save"); Call(_director, "Load"); await Frames(); CloseMenus();
        Check(name + "_save_load", hash == Session.StateHash && pets == PetHash && Replay());
    }
    private async Task KeyPress(Key key)
    { foreach (bool down in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = down }, true); await Frames(); }
    private async Task Open() { CloseMenus(); Journey.Visible = true; Journey.SetOpen(true); await Frames(); await Click("OpenPets"); Check("pet_panel_paused", Panel.IsOpen && _sandbox.IsPaused); }
    private void CloseMenus()
    {
        Panel.SetOpen(false); Field<BestiaryPanel>(_director, "_bestiary").SetOpen(false); Field<AppearanceWardrobePanel>(_director, "_wardrobe").SetOpen(false); Field<LegendaryCollectionPanel>(_director, "_collection").SetOpen(false);
        Field<ProductionHud>(_director, "_character").Close(); Journey.SetOpen(false); Field<EndgameHud>(_director, "_board").SetOpen(false); Field<OpeningGuidancePanel>(_director, "_openingGuide").SetOpen(false);
        Call(_sandbox, "ResumePlaying");
    }
    private void CheckLayout(string name)
    {
        var viewport = GetViewport().GetVisibleRect();
        foreach (string id in new[] { "PetPreviewFrame", "PetClose", "PetGather", "PetDetailsScroll" })
        { var control = Find<Control>(id); Check(name + "_" + id + "_fits", control.IsVisibleInTree() && viewport.Encloses(control.GetGlobalRect())); }
    }
    private void CampaignStep() => Step(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)));
    private void Step(EndgameRuntimeCommand command)
    {
        var result = (EndgameRuntimeResult)Call(_director, "ExecuteActive", command)!; _commands++;
        if (!result.Success) throw new InvalidDataException(result.Reason + " / " + command.Action);
        Call(_director, "Observe", result); _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh");
    }
    private bool Replay() => EndgameRuntimeReplayRunner.Run(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"), Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), Session.CaptureReplay()).Success;
    private async Task Reveal(Control control)
    {
        for (Node? parent = control.GetParent(); parent is not null; parent = parent.GetParent()) if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(control);
        await Frames(); if (!GodotObject.IsInstanceValid(control)) return;
        Check("target_visible_" + control.Name, control.IsVisibleInTree() && GetViewport().GetVisibleRect().HasPoint(control.GetGlobalRect().GetCenter()));
    }
    private void PushClick(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
    }
    private async Task Click(string name)
    {
        name = name.Replace('.', '_');
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var button = Find<BaseButton>(name); await Reveal(button);
            if (!GodotObject.IsInstanceValid(button) || !button.IsInsideTree()) continue;
            _lastInput = name; bool received = false; void Receipt() => received = true; button.Pressed += Receipt; PushClick(button.GetGlobalRect().GetCenter());
            _clicks++; await Frames(); if (GodotObject.IsInstanceValid(button)) button.Pressed -= Receipt; Check("click_received_" + _clicks, received); return;
        }
        throw new InvalidDataException("UI target did not settle: " + name);
    }
    private async Task Frames(int count = 4) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox?._Process(0); } }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-pets")) return;
        GetWindow().GrabFocus(); await Frames(); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage(); Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Pet check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private async Task Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "PetsSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            clicks = _clicks,
            commands = _commands,
            error,
            lastInput = _lastInput,
            scope = "Real early Vanguard campaign, F rescue, pet naming/appearance/dismiss/select UI, canonical material cache gathering, persistence and character isolation. Gallery shows detached samples of all three models; later rescues are covered by Core tests."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "pets-review.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report));
        if (_director is not null && GodotObject.IsInstanceValid(_director)) _director.QueueFree(); _director = null!; _sandbox = null!;
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GC.Collect();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GetTree().Quit(passed ? 0 : 1);
    }
}
