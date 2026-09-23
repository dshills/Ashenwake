using System.Reflection;
using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Shipping journal controls and earned pickup observation, with separate historical archive and corruption fixtures.</summary>
public partial class LegendaryCollectionSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "";
    private bool _writeReport;
    private int _clicks, _commands;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private LegendaryCollectionPanel Journal => Field<LegendaryCollectionPanel>(_director, "_collection");
    private LegendaryCollectionMemory Memory => Field<LegendaryCollectionMemory>(_director, "_collectionMemory");
    private CampaignHud Journey => Field<CampaignHud>(_director, "_campaignHud");
    private EndgameHud Board => Field<EndgameHud>(_director, "_board");
    private string SavePath => Path.Combine(_output, Field<string>(_director, "_saveName"));

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs(); _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--legendary-collection-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Collection smoke requires --legendary-collection-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); await Frames(); Call(_sandbox, "ResumePlaying");
            await InspectAndTrack(); await EarnAndRemember(); await CharacterIsolation(); await EndgameRoutes();
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Frames(); Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private async Task InspectAndTrack()
    {
        string hash = Session.StateHash; Journey.SetOpen(true); await Frames(); await Click("JourneyCollection");
        Check("journey_opens_paused_collection", Journal.IsOpen && _sandbox.IsPaused);
        Check("fresh_collection_is_empty", Memory.DiscoveredItems.Length == 0 && Find<Label>("CollectionSummary").Text.StartsWith("0 / 9", StringComparison.Ordinal));
        Check("opening_does_not_write_sidecar", !File.Exists(LegendaryCollectionStore.PathFor(SavePath)));
        foreach (var entry in LegendaryCollectionCatalog.Entries)
        {
            await Click("CollectionItem_" + entry.ItemId[5..]);
            Check("preview_" + entry.ItemId, Descendants(Journal).OfType<CharacterPreview>().Single().AppearanceKey.Contains(entry.ItemId, StringComparison.Ordinal));
        }
        string text = string.Join('\n', Descendants(Journal).OfType<Label>().Select(l => l.Text));
        Check("future_story_and_lore_hidden", !text.Contains("Breach Heart", StringComparison.Ordinal) && !text.Contains("A thief stole", StringComparison.Ordinal) && text.Contains("Undiscovered campaign encounter", StringComparison.Ordinal));
        Check("secret_hunt_cannot_navigate", Find<Button>("CollectionSourceGodHunt").Disabled);
        await Capture("collection-all-1280.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames();
        CheckLayout("minimum"); await Capture("collection-all-780.png");
        await Click("CollectionTrack");
        Check("tracking_undiscovered_does_not_grant_it", Memory.TrackedItem == LegendaryEquipment.Hour && Memory.DiscoveredItems.Length == 0 && Session.StateHash == hash);
        Check("tracking_persists", LegendaryCollectionStore.Load(SavePath, Memory.CharacterId).Memory.TrackedItem == LegendaryEquipment.Hour);
        await ClickSource("CollectionSourceCampaign");
        Check("campaign_source_selects_region_without_travel", !Journal.IsOpen && Journey.SelectedJourneyRegion == 5 && Session.InHub && Session.StateHash == hash);
        Check("journey_shows_tracked_item", Find<Button>("JourneyTrackedRelic").Text.Contains("Stolen Hour", StringComparison.Ordinal));
        await Capture("collection-journey-target.png"); await Click("JourneyTrackedRelic");
        await ClickSource("CollectionSourceFracture");
        Check("locked_fracture_link_inspects_without_spend", Board.IsOpen && !Session.View.Unlocked && Session.StateHash == hash);
        Check("board_shows_tracked_item", Find<Button>("ExpeditionTrackedRelic").Text.Contains("Stolen Hour", StringComparison.Ordinal));
        await Capture("collection-locked-fracture.png"); await Click("ExpeditionCollection");
        await Click("CollectionFilterCollected"); Check("empty_filter_is_explicit", Find<Button>("CollectionTrack").Disabled);
        await Capture("collection-empty-filter.png"); await Click("CollectionFilterAll");
        _sandbox.SetModalPaused("collection-test-owner", true); await Click("CollectionClose");
        Check("close_preserves_other_pause_owner", _sandbox.IsPaused && !Descendants(Journal).OfType<CharacterPreview>().Single().Rendering);
        _sandbox.SetModalPaused("collection-test-owner", false);
        Call(_director, "OpenCollection"); await Frames(); string beforeTransfer = Session.StateHash;
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.H, Keycode = Key.H, Pressed = pressed }, true);
        await Frames(); Check("h_transfers_to_echoes_panel_without_gameplay", !Journal.IsOpen && Field<EchoesBoard>(_director, "_echoesBoard").IsOpen && Session.StateHash == beforeTransfer);
        Call(_director, "CloseExperimentPanel");
        Call(_director, "Save"); string save = File.ReadAllText(SavePath), memory = File.ReadAllText(LegendaryCollectionStore.PathFor(SavePath));
        Call(_director, "Load"); await Frames(); Call(_director, "OpenCollection"); await Frames();
        Check("load_remembers_tracking_without_rewriting_files", Memory.TrackedItem == LegendaryEquipment.Hour && File.ReadAllText(SavePath) == save && File.ReadAllText(LegendaryCollectionStore.PathFor(SavePath)) == memory);
        Journal.SetOpen(false);
    }

    private async Task EarnAndRemember()
    {
        for (int i = 0; i < 5000 && !Session.Production.Capture().Progression.Character.Items.Any(item => item.DefinitionId == LegendaryEquipment.Pyre); i++)
        {
            var result = Session.Execute(EndgameRuntimeSmoke.Next(Session)); _commands++;
            if (!result.Success) throw new InvalidDataException(result.Reason);
            Call(_director, "Observe", result);
        }
        _sandbox.SetSession(Session.Combat); Call(_director, "Refresh"); Call(_director, "OpenCollection"); await Frames();
        Check("actual_pickup_records_discovery", Memory.DiscoveredItems.Contains(LegendaryEquipment.Pyre) && Session.Production.Capture().Progression.Character.Items.Any(i => i.DefinitionId == LegendaryEquipment.Pyre));
        Check("pickup_memory_is_durable", LegendaryCollectionStore.Load(SavePath, Memory.CharacterId).Memory.DiscoveredItems.Contains(LegendaryEquipment.Pyre));
        await Click("CollectionFilterCollected");
        Check("collected_filter_selects_earned_item", Journal.SelectedItem == LegendaryEquipment.Pyre);
        Check("owned_count_and_lore_reveal_after_pickup", string.Join('\n', Descendants(Journal).OfType<Label>().Select(l => l.Text)).Contains("Owned copies: 1", StringComparison.Ordinal));
        await Capture("collection-earned-pyre.png");
        string hash = Session.StateHash; await Click("CollectionTrack"); Check("tracking_owned_item_changes_no_gameplay", Session.StateHash == hash);
        Journal.SetOpen(false); Call(_director, "Save");
        var replay = Session.CaptureReplay(); var resultReplay = EndgameRuntimeReplayRunner.Run(Field<string>(_director, "_combatJson"), Field<AdventureContent>(_director, "_adventure"),
            Field<ProgressionContent>(_director, "_progression"), Field<CampaignContent>(_director, "_campaign"), Field<EndgameContent>(_director, "_endgame"), replay);
        Check("earned_route_replay_exact", resultReplay.Success && resultReplay.FinalHash == Session.StateHash);
        File.WriteAllText(Path.Combine(_output, "earned-pyre.replay.json"), JsonData.Write(replay));
        Call(_director, "Load"); Check("earned_discovery_survives_load", Memory.DiscoveredItems.Contains(LegendaryEquipment.Pyre) && Memory.TrackedItem == LegendaryEquipment.Pyre);
    }

    private async Task CharacterIsolation()
    {
        string originalName = Field<string>(_director, "_saveName"), originalPath = SavePath;
        Call(_director, "CreateCharacter", "Arcanist"); Call(_director, "OpenCollection"); await Frames();
        Check("new_character_has_separate_memory", Memory.DiscoveredItems.Length == 0 && Memory.TrackedItem.Length == 0 && SavePath != originalPath);
        await Capture("collection-separate-character.png");
        Journal.SelectItem(LegendaryEquipment.Crown); await Frames(); await Click("CollectionTrack");
        string other = File.ReadAllText(LegendaryCollectionStore.PathFor(SavePath));
        Call(_director, "PlayCharacter", originalName); Call(_director, "OpenCollection"); await Frames();
        Check("switch_restores_original_target_and_discovery", Memory.TrackedItem == LegendaryEquipment.Pyre && Memory.DiscoveredItems.Contains(LegendaryEquipment.Pyre));
        // Detached corrupt-sidecar fixture verifies non-destructive UI fallback; it does not fabricate a gameplay item.
        string sidecar = LegendaryCollectionStore.PathFor(SavePath);
        var future = JsonNode.Parse(File.ReadAllText(sidecar))!; future["schemaVersion"] = 999; File.WriteAllText(sidecar, future.ToJsonString());
        string protectedBytes = File.ReadAllText(sidecar); Journal.SetOpen(false); Call(_director, "Load"); Call(_director, "OpenCollection"); await Frames();
        Journal.SelectItem(LegendaryEquipment.Crown); await Frames(); await Click("CollectionTrack");
        Check("future_memory_protected_while_tracking_is_ephemeral", Memory.TrackedItem == LegendaryEquipment.Crown && File.ReadAllText(sidecar) == protectedBytes && Find<Label>("CollectionNotice").Text.Contains("preserved", StringComparison.Ordinal));
        await Capture("collection-protected-memory.png");
        Check("other_character_journal_untouched", Directory.GetFiles(_output, "*.legendaries.json").Where(p => p != sidecar).Any(p => File.ReadAllText(p) == other));
        Journal.SetOpen(false);
    }

    private async Task EndgameRoutes()
    {
        string path = Path.Combine(_output, "historical-campaign.json");
        File.WriteAllText(path, Godot.FileAccess.GetFileAsString("res://phase4-campaign-complete.json"));
        Call(_director, "Import", path);
        for (int i = 0; i < 300 && Session.View.AvailableSigils.Length == 0; i++)
        {
            var result = Session.Execute(EndgameRuntimeSmoke.AtGate(Session, new(EndgameRuntimeAction.ClaimRecoverySigil))); _commands++;
            if (!result.Success) throw new InvalidDataException(result.Reason); Call(_director, "Observe", result);
        }
        Call(_director, "Refresh"); Call(_director, "OpenCollection"); await Frames();
        var entry = LegendaryCollectionCatalog.Entries.First(e => Session.View.AvailableSigils.Any(s => s.Region == e.FractureRegionId));
        Journal.SelectItem(entry.ItemId); await Frames(); string before = Session.StateHash;
        var source = LegendaryCollectionSources.For(Session, entry.ItemId).Single(s => s.Kind == LegendaryCollectionSourceKind.Fracture);
        await ClickSource("CollectionSourceFracture");
        Check("fracture_link_selects_owned_matching_sigil", Board.IsOpen && Field<long>(Board, "_selectedSigil") == source.SigilId && Session.StateHash == before);
        await Capture("collection-owned-sigil-route.png"); await Click("ExpeditionCollection");
        Journal.SelectItem(LegendaryEquipment.Crown); await Frames();
        await ClickSource("CollectionSourceGodHunt");
        Check("known_hunt_link_selects_exact_hunt_without_start", Field<string>(Board, "_selectedHunt") == "hunt.orrun_without_oath" && Session.StateHash == before);
        await Capture("collection-known-hunt-route.png");
    }

    private void CheckLayout(string suffix)
    {
        var viewport = GetViewport().GetVisibleRect(); var panel = Find<PanelContainer>("LegendaryCollection");
        Check("layout_" + suffix, viewport.Encloses(panel.GetGlobalRect()) && viewport.Encloses(Find<Button>("CollectionTrack").GetGlobalRect()) && viewport.Encloses(Find<ScrollContainer>("CollectionDetailsScroll").GetGlobalRect()));
    }
    private async Task ClickSource(string name) { Find<ScrollContainer>("CollectionDetailsScroll").EnsureControlVisible(Find<Button>(name)); await Frames(); await Click(name); }
    private async Task Click(string name)
    {
        var button = Find<Button>(name); Check("clickable_" + name + "_" + _clicks, button.IsVisibleInTree() && !button.Disabled);
        var point = button.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
        _clicks++; await Frames();
    }
    private async Task Frames(int count = 4) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); _sandbox?._Process(0); } }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-collection")) return;
        if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus(); Call(_sandbox, "ResumePlaying"); await Frames();
        RenderingServer.ForceDraw(false); RenderingServer.ForceSync(); using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool value) { _checks[name] = value; if (!value) throw new InvalidDataException("Collection check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "LegendaryCollectionSmoke",
            passed,
            checks = _checks,
            captures = _captures,
            clicks = _clicks,
            commands = _commands,
            error,
            scope = "Shipping director and native pointer controls; fresh Vanguard earns Pyrebound Treads through ordinary commands with exact replay. Tracking and previews do not mutate gameplay. Separate real character slots test journal isolation. Historical completed-campaign import tests source navigation; future-version sidecar is an explicit corruption fixture. Core tests cover extraction/salvage retention and archive guards."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "collection-review.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
