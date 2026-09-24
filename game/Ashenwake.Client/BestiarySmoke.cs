using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Real Act I encounters, the Bell-Torn Pilgrim, viewport inspection and character-isolated records.</summary>
public partial class BestiarySmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "", _lastInput = "";
    private bool _writeReport;
    private int _commands, _clicks;
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private BestiaryPanel Panel => Field<BestiaryPanel>(_director, "_bestiary");
    private BestiaryMemory Memory => Field<BestiaryMemory>(_director, "_bestiaryMemory");
    private BestiaryDisplay Display => Field<BestiaryDisplay>(Panel, "_view");
    private CreaturePreview Preview => Descendants(Panel).OfType<CreaturePreview>().Single();
    private CampaignHud Journey => Field<CampaignHud>(_director, "_campaignHud");

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs(); _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--bestiary-smoke") || !args.Contains("--discipline=Vanguard") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Bestiary smoke requires --bestiary-smoke --discipline=Vanguard --output=<fresh-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _director = new EndgameDirector(); AddChild(_director); _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false);
            await Frames(); CloseMenus(); Call(_sandbox, "ResumePlaying");
            await UnknownAndFirstEncounter(); GD.Print("Bestiary discovery and defeat checks passed.");
            await ChampionAndBoss(); GD.Print("Bestiary genuine champion and boss records earned.");
            await Inspection(); await CharacterIsolation();
            Check("final_replay", Replay());
            foreach (var player in _sandbox.AudioDirector.MusicPlayers) { player.Stop(); player.Stream = null; }
            await Finish(true, "");
        }
        catch (Exception ex) { try { await Capture("failure.png"); } catch { } GD.PushError(ex.ToString()); await Finish(false, ex.Message); }
    }

    private async Task UnknownAndFirstEncounter()
    {
        await Open(); string hash = Session.StateHash; int frames = Session.CaptureReplay().Frames.Length;
        Check("fresh_character_has_no_records", Memory.DiscoveredEntries.Length == 0 && Memory.Defeats.Length == 0);
        Check("unknown_entries_hide_spoilers", Display.Entries.Length > 0 && Display.Entries.All(e => !e.Seen && e.VisualId.Length == 0 && e.Lore.Length == 0 && e.Region.Length == 0 && e.CombatFacts.Length == 0 && e.Rewards.Length == 0));
        Check("unknown_creature_not_rendered", !Preview.Rendering && Preview.VisualId.Length == 0);
        await EnterSearch("Bell"); Check("unknown_name_search_has_no_results", EntryButtons().Length == 0);
        await EnterSearch(""); await Click("BestiaryFilterSeen"); Check("fresh_seen_filter_empty", EntryButtons().Length == 0);
        await Click("BestiaryFilterAll"); CheckLayout("unknown_wide"); await Capture("bestiary-undiscovered.png");
        await Click("BestiaryClose"); Check("inspection_preserves_core", Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frames);
        for (int i = 0; i < 1200 && Memory.DiscoveredEntries.Length == 0; i++) { CampaignStep(); if (i % 100 == 0) await Frames(1); }
        Check("first_encounter_discovered_before_kill", Memory.DiscoveredEntries.Length > 0 && Memory.Defeats.Length == 0);
        string seen = Memory.DiscoveredEntries[0]; await Open(); await Click("BestiaryEntry_" + seen);
        var before = Display.Entries.Single(e => e.Id == seen);
        Check("seen_entry_has_lore_and_model", before.Seen && before.Lore.Length > 0 && before.VisualId.Length > 0 && Preview.VisualId == before.VisualId);
        Check("seen_entry_hides_unearned_combat_notes", before.Kills == 0 && before.CombatFacts.Length == 0);
        await EnterSearch("m");
        Check("map_shortcut_is_search_text", Find<LineEdit>("BestiarySearch").Text == "m" && Panel.IsOpen && !_sandbox.LocalMapOpen);
        await EnterSearch(""); await Click("BestiaryEntry_" + seen);
        await Capture("bestiary-first-sighting.png"); CloseMenus();
        // Keep the most recent real pre-defeat save, then replay that short command prefix
        // through the same controller after Load. The already-earned sidecar record must
        // survive rollback without counting the identical death event a second time.
        var replayCommands = new List<EndgameRuntimeCommand>(); string beforeDeathHash = "";
        for (int i = 0; i < 2000 && Memory.Defeats.Length == 0; i++)
        {
            if (i % 100 == 0) { Call(_director, "Save"); beforeDeathHash = Session.StateHash; replayCommands.Clear(); }
            var command = new EndgameRuntimeCommand(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign));
            replayCommands.Add(command); Step(command); if (i % 100 == 0) await Frames(1);
        }
        Check("ordinary_combat_records_first_defeat", Memory.Defeats.Length > 0);
        string afterDeathHash = Session.StateHash, earnedMemory = JsonData.Hash(Memory);
        Call(_director, "Load"); await Frames(); CloseMenus();
        Check("pre_defeat_save_load_retains_earned_record", Session.StateHash == beforeDeathHash && JsonData.Hash(Memory) == earnedMemory);
        foreach (var command in replayCommands) Step(command);
        Check("same_death_replay_preserves_state_and_record_tokens", Session.StateHash == afterDeathHash && Session.CaptureReplay().Frames.Length == replayCommands.Count && Replay() && JsonData.Hash(Memory) == earnedMemory);
        string defeated = Memory.Defeats[0].EntryId;
        await Open(); await Click("BestiaryEntry_" + defeated);
        var after = Display.Entries.Single(e => e.Id == defeated);
        Check("defeat_reveals_combat_knowledge", after.Kills > 0 && after.CombatFacts.Length > 0);
        string memory = JsonData.Hash(Memory); for (int i = 0; i < 4; i++) Refresh();
        Check("refresh_does_not_inflate_kills", JsonData.Hash(Memory) == memory);
        CloseMenus(); await RoundTrip("first_defeat");
        await Open(); await Click("BestiaryEntry_" + defeated); Check("reload_preserves_defeat_notes", Display.Entries.Single(e => e.Id == defeated).Kills == after.Kills);
        CloseMenus();
    }

    private async Task ChampionAndBoss()
    {
        const string champion = "champion.pilgrim", boss = "boss.bell_saint";
        for (int i = 0; i < 8000 && !Session.RoamingChampions.Entries.Any(e => e.Id == champion && e.Here); i++)
        { CampaignStep(); if (i % 100 == 0) await Frames(1); }
        Check("ordinary_route_reaches_seeded_champion", Session.RoamingChampions.Entries.Any(e => e.Id == champion && e.Here));
        CloseMenus();
        for (int i = 0; i < 5000 && Session.RoamingChampions.Run?.Stage != "Victory"; i++)
        { Step(RoamingChampionSmoke.Next(Session, champion)); if (i % 100 == 0) await Frames(1); }
        Check("genuine_champion_victory", Session.RoamingChampions.Run?.Stage == "Victory" && Memory.Defeats.Any(d => d.EntryId == champion));
        await Open(); await Click("BestiaryFilterChampions"); await Click("BestiaryEntry_" + champion);
        Check("champion_uses_own_identity", Preview.VisualId == champion && Display.Entries.Single(e => e.Id == champion).Kills == 1);
        await Capture("bestiary-earned-champion.png"); CloseMenus();
        for (int i = 0; i < 1200 && Session.InRoamingChampion; i++) { Step(RoamingChampionSmoke.Next(Session, champion)); if (i % 100 == 0) await Frames(1); }
        Check("champion_claim_and_return", !Session.InRoamingChampion && Session.RoamingChampions.Entries.Any(e => e.Id == champion && e.Claimed));
        for (int i = 0; i < 8000 && !Session.Campaign.Capture().Campaign.CompletedEncounters.Contains("campaign.bell_saint"); i++)
        { CampaignStep(); if (i % 100 == 0) await Frames(1); }
        Check("genuine_bell_saint_victory", Session.Campaign.Capture().Campaign.CompletedEncounters.Contains("campaign.bell_saint") && Memory.Defeats.Any(d => d.EntryId == boss));
        await Open(); await Click("BestiaryFilterBosses"); await Click("BestiaryEntry_" + boss);
        Check("boss_preview_matches_record", Preview.VisualId == boss && Display.Entries.Single(e => e.Id == boss).Kills == 1);
        await Capture("bestiary-earned-boss.png"); CloseMenus();
        Step(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub)));
        Check("public_return_to_hub", Session.InHub); await RoundTrip("earned_records");
    }

    private async Task Inspection()
    {
        await Open(); string hash = Session.StateHash, memory = JsonData.Hash(Memory); int frames = Session.CaptureReplay().Frames.Length;
        await Click("BestiaryFilterSeen"); Check("seen_filter_contains_only_discovered", EntryButtons().All(b => Memory.DiscoveredEntries.Contains(b.GetMeta("bestiary_id").AsString())));
        await Click("BestiaryFilterDefeated"); Check("defeated_filter_contains_only_records", EntryButtons().All(b => Memory.Defeats.Any(d => d.EntryId == b.GetMeta("bestiary_id").AsString())));
        await Click("BestiaryFilterAll"); await EnterSearch("Bell");
        Check("search_matches_discovered_names", EntryButtons().Length >= 2 && EntryButtons().All(b => Display.Entries.Single(e => e.Id == b.GetMeta("bestiary_id").AsString()).Name.Contains("Bell", StringComparison.OrdinalIgnoreCase)));
        await Click("BestiaryEntry_boss.bell_saint");
        float initial = Preview.RotationAngle; await Click("CreatureRotateRight"); Check("rotation_button_changes_angle", Preview.RotationAngle != initial);
        await Click("CreatureResetRotation"); Check("rotation_reset", Math.Abs(Preview.RotationAngle) < .001f);
        var surface = Find<Control>("CreaturePreviewSurface"); await Reveal(surface); Vector2 start = surface.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = start }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = start, ButtonIndex = MouseButton.Left, Pressed = true }, true);
        GetViewport().PushInput(new InputEventMouseMotion { Position = start + new Vector2(60, 0), Relative = new(60, 0), ButtonMask = MouseButtonMask.Left }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = start + new Vector2(60, 0), ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await Frames(); Check("preview_drag_rotates", Math.Abs(Preview.RotationAngle) > .01f);
        CheckLayout("earned_wide"); await Capture("bestiary-wide.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(780, 720); await Frames(8); CheckLayout("earned_compact"); await Capture("bestiary-compact.png");
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(8);
        await EnterSearch(""); await Click("BestiaryEntry_champion.pilgrim");
        var reward = Display.Entries.Single(e => e.Id == "champion.pilgrim").Rewards.First();
        await Click("BestiaryReward_" + reward.ItemId + "_" + reward.SourceKind);
        Check("reward_source_opens_inspection", !Panel.IsOpen && Field<RoamingChampionPanel>(_director, "_championPanel").IsOpen);
        Check("source_navigation_does_not_travel_spend_or_rewrite_records", Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frames && JsonData.Hash(Memory) == memory);
        CloseMenus();
    }

    private async Task RoundTrip(string label)
    {
        string hash = Session.StateHash, memory = JsonData.Hash(Memory);
        Call(_director, "Save"); Call(_director, "Load"); await Frames(); CloseMenus();
        Check(label + "_save_load_preserves_core_and_records", Session.StateHash == hash && JsonData.Hash(Memory) == memory && Replay());
    }

    private async Task CharacterIsolation()
    {
        string filename = Field<string>(_director, "_saveName"), character = Memory.CharacterId, hash = Session.StateHash, memory = JsonData.Hash(Memory);
        Call(_director, "ShowFrontMenu"); await Frames(); await Click("FrontNew"); await Click("FrontDisciplineWarden"); await Click("FrontBegin"); CloseMenus();
        await Open(); Check("new_character_has_empty_bestiary", Field<string>(_director, "_saveName") != filename && Memory.DiscoveredEntries.Length == 0 && Memory.Defeats.Length == 0 && Display.Entries.All(e => !e.Seen)); CloseMenus();
        Call(_director, "ShowFrontMenu"); await Frames();
        var menu = Field<FrontMenu>(_director, "_frontMenu"); ulong deadline = Time.GetTicksMsec() + 10000;
        while (menu.CatalogLoading && Time.GetTicksMsec() < deadline) await Frames();
        await Click("FrontCharacters");
        var slotsField = menu.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Single(f => f.FieldType == typeof(CharacterSlot[]));
        var slots = (CharacterSlot[])slotsField.GetValue(menu)!; int index = Array.FindIndex(slots, s => s.Filename == filename);
        Check("first_character_found_in_browser", index >= 0); await Click("FrontSlot" + index); await Click("FrontPlay"); CloseMenus();
        Check("switch_back_restores_own_records", Session.StateHash == hash && Memory.CharacterId == character && JsonData.Hash(Memory) == memory);
    }

    private async Task Open() { CloseMenus(); Journey.Visible = true; Journey.SetOpen(true); await Frames(); await Click("OpenBestiary"); Check("bestiary_open_and_paused", Panel.IsOpen && _sandbox.IsPaused); }
    private void CloseMenus()
    {
        Panel.SetOpen(false); Field<AppearanceWardrobePanel>(_director, "_wardrobe").SetOpen(false); Field<LegendaryCollectionPanel>(_director, "_collection").SetOpen(false);
        Field<RoamingChampionPanel>(_director, "_championPanel").SetOpen(false); Field<ProductionHud>(_director, "_character").Close();
        Journey.SetOpen(false); Field<EndgameHud>(_director, "_board").SetOpen(false); Field<OpeningGuidancePanel>(_director, "_openingGuide").SetOpen(false);
    }
    private Button[] EntryButtons() => Descendants(Panel).OfType<Button>().Where(b => b.Name.ToString().StartsWith("BestiaryEntry_", StringComparison.Ordinal) && b.IsVisibleInTree()).ToArray();
    private async Task EnterSearch(string text)
    {
        var edit = Find<LineEdit>("BestiarySearch"); await Reveal(edit); PushClick(edit.GetGlobalRect().GetCenter()); await Frames();
        string hash = Session.StateHash; int frames = Session.CaptureReplay().Frames.Length; edit.SelectAll();
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = Key.Backspace, PhysicalKeycode = Key.Backspace, Pressed = pressed }, true);
        foreach (char letter in text)
        {
            var key = (Key)char.ToUpperInvariant(letter);
            foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = pressed ? letter : 0u, Pressed = pressed }, true);
        }
        await Frames(); Check("search_keyboard_input", edit.Text == text && Panel.IsOpen);
        Check("search_shortcuts_do_not_change_core", Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frames);
    }
    private void CheckLayout(string name)
    {
        var viewport = GetViewport().GetVisibleRect();
        foreach (string id in new[] { "BestiarySearch", "BestiaryCatalog", "BestiaryClose", "BestiaryPreviewFrame" })
        { var control = Find<Control>(id); Check(name + "_" + id + "_fits", control.IsVisibleInTree() && viewport.Encloses(control.GetGlobalRect())); }
    }
    private void CampaignStep() => Step(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(Session.Campaign)));
    private void Refresh() { _sandbox.AdoptSession(Session.Combat); Call(_director, "Refresh"); }
    private void Step(EndgameRuntimeCommand command)
    {
        var result = (EndgameRuntimeResult)Call(_director, "ExecuteActive", command)!; _commands++;
        if (!result.Success) throw new InvalidDataException(result.Reason + " / " + command.Action);
        Call(_director, "Observe", result); Refresh();
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
        if (DisplayServer.GetName() == "headless" || !OS.GetCmdlineUserArgs().Contains("--capture-bestiary")) return;
        GetWindow().GrabFocus(); await Frames(); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage(); Check("capture_" + name, image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Bestiary check failed: " + name); }
    private T Find<T>(string name) where T : Node => Descendants(_sandbox).OfType<T>().Single(n => n.Name == name);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object?[] args)
    { try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private async Task Finish(bool passed, string error)
    {
        var report = new
        {
            kind = "BestiarySmoke",
            passed,
            checks = _checks,
            captures = _captures,
            clicks = _clicks,
            commands = _commands,
            error,
            lastInput = _lastInput,
            scope = "Fresh Vanguard; public campaign commands earn first sightings, defeats, Bell Saint and Bell-Torn Pilgrim records. Actual viewport search, filters, creature rotation and reward-source inspection; compact/wide screenshots. Save/load, replaying the same genuine death from its pre-defeat save, repeated refresh and character switching preserve exact isolated records. Inspection preserves authoritative state and replay frames. No discovery, kill, reward or preview fixtures are injected."
        };
        if (_writeReport) File.WriteAllText(Path.Combine(_output, "bestiary-review.json"), JsonData.Write(report)); GD.Print(JsonData.Write(report));
        if (_director is not null && GodotObject.IsInstanceValid(_director)) _director.QueueFree(); _director = null!; _sandbox = null!;
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GC.Collect();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); GetTree().Quit(passed ? 0 : 1);
    }
}
