using System.Reflection;
using System.Text.Json.Nodes;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Simulation;
using Godot;

namespace Ashenwake.Client;

/// <summary>Shipping settings input, persistence and pause behavior in a fresh, isolated directory.</summary>
public partial class SettingsSmoke : Node3D
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly List<object> _displaySamples = [];
    private readonly List<AudioMixMeasurement> _audioMixSamples = [];
    private EndgameDirector _director = null!;
    private Sandbox _sandbox = null!;
    private string _output = "";
    private bool _writeReport;
    private int _restarts;
    private int _worldTeardowns;
    private static readonly string[] Tabs = ["Controls", "Graphics", "Audio", "Accessibility", "Gameplay"];
    private static readonly string[] Channels = ["Master", "Music", "Effects", "UI"];
    private static readonly string[] VolumeProperties = ["masterVolume", "musicVolume", "effectsVolume", "interfaceVolume"];
    private Dictionary<string, Key> Keys => Field<Dictionary<string, Key>>(_sandbox, "_keys");
    private EndgameRuntimeSession Session => Field<EndgameRuntimeSession>(_director, "_session");
    private FrontMenu Menu => Field<FrontMenu>(_director, "_frontMenu");
    private string SettingsPath => Path.Combine(_output, "settings.json");
    private Label Status => Find<Label>("SettingsStatus");

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--settings-smoke") || args.Any(a => a.StartsWith("--preferences=", StringComparison.Ordinal) || a.StartsWith("--discipline=", StringComparison.Ordinal)) ||
                _output.Length == 0 || Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Settings smoke requires --settings-smoke --output=<fresh-artifact-directory>, without custom preferences or discipline.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            // Simulate a previous scene's enabled profile: legacy preferences must
            // actively restore their default rather than inherit global audio state.
            ClientAudio.ApplyQuietMode(true);
            SeedRecovery(); Resize(1280, 800); await StartDirector();
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await RecoveryAndLayout();
            await BindingAndRestore();
            await AudioAndPreferences();
            await GraphicsAndPreferences();
            await BlockedSettingsWrite();
            await RestartPersistenceAndInvalidRecovery();
            await InGamePause();
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { if (_director is not null) await Capture("settings-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private void SeedRecovery()
    {
        var backup = new JsonObject
        {
            ["reducedEffects"] = true,
            ["reducedShake"] = true,
            ["keys"] = new JsonObject { ["left"] = (long)Key.A },
            ["minimumLootRarity"] = 4,
            ["compatibleLootOnly"] = true,
            ["masterVolume"] = .8,
            ["musicVolume"] = .65,
            ["effectsVolume"] = .55,
            ["interfaceVolume"] = .45
        };
        System.IO.File.WriteAllText(SettingsPath + ".bak", backup.ToJsonString());
        backup["masterVolume"] = -1;
        System.IO.File.WriteAllText(SettingsPath, backup.ToJsonString());
    }

    private async Task RecoveryAndLayout()
    {
        string primary = ReadSettings(), backup = System.IO.File.ReadAllText(SettingsPath + ".bak"), hash = Session.StateHash;
        await Click("FrontSettings");
        Check("front_settings_opens_paused_with_controls_keyboard_focus", _sandbox.FrontSettingsVisible && Menu.IsOpen && _sandbox.IsPaused &&
            GetViewport().GuiGetFocusOwner() == Find<Button>("SettingsTabControls"));
        Check("invalid_volume_recovers_valid_backup_with_visible_notice", Status.Text.Contains("recovered", StringComparison.OrdinalIgnoreCase) &&
            Value<bool>(_sandbox, "_reduceEffects") && Value<bool>(_sandbox, "_reduceShake") && Near(Value<float>(_sandbox, "_masterVolume"), .8));
        Check("recovery_preserves_primary_and_backup_until_deliberate_change", ReadSettings() == primary && System.IO.File.ReadAllText(SettingsPath + ".bak") == backup);
        Check("legacy_partial_key_map_keeps_defaults_for_other_actions", Keys["left"] == Key.A && Keys["right"] == Key.D && Keys["interact"] == Key.F);
        Check("legacy_settings_default_to_quiet_mode_off", !Value<bool>(_sandbox, "_quietMode") && !ClientAudio.QuietModeEnabled && !Find<CheckButton>("SettingsQuietMode").ButtonPressed);
        Check("legacy_settings_default_to_high_graphics", _sandbox.GraphicsQuality == "High" && Find<OptionButton>("SettingsGraphicsQuality").Selected == 0);
        foreach (var size in new[] { (1280, 800), (1000, 720), (780, 720) })
        {
            Resize(size.Item1, size.Item2); await Frames(5);
            foreach (string tab in Tabs)
            {
                await Click("SettingsTab" + tab);
                AssertLayout(tab.ToLowerInvariant() + "_" + size.Item1);
                if (tab == "Audio")
                {
                    var percentages = Channels.Select(c => Find<Label>("SettingsVolumeValue" + c)).ToArray();
                    Check("audio_" + size.Item1 + "_percentages_remain_readable_on_one_line", percentages.All(label =>
                        label.AutowrapMode == TextServer.AutowrapMode.Off && label.Size.X >= 60 && label.Size.Y <= 32 && label.IsVisibleInTree()));
                }
                await Capture("settings-" + tab.ToLowerInvariant() + "-" + size.Item1 + ".png");
            }
        }
        foreach (string text in new[] { "Save character", "Load character", "Verify & save replay", "Inspect ground loot" })
            Check("front_menu_disables_" + text.Replace(' ', '_'), Descendants(_sandbox).OfType<Button>().Single(b => b.Text.StartsWith(text, StringComparison.Ordinal)).Disabled);
        foreach (Key key in new[] { Key.W, Key.F, Key.C, Key.J, Key.B, Key.H, Key.P, Key.F5, Key.F9, Key.F6 }) await KeyPress(key);
        AdvanceWhilePaused();
        Check("settings_world_shortcuts_leave_preview_and_archives_unchanged", Session.StateHash == hash && Session.CaptureReplay().Frames.Length == 0 &&
            !Directory.EnumerateFiles(_output, "*.save.json").Any() && _sandbox.IsPaused);
        Check("tab_browsing_and_blocked_shortcuts_do_not_write_preferences", ReadSettings() == primary && System.IO.File.ReadAllText(SettingsPath + ".bak") == backup);
        await Click("SettingsClose");
        Check("settings_close_returns_to_paused_main_menu", !_sandbox.FrontSettingsVisible && Menu.IsOpen && _sandbox.IsPaused);
        Resize(1280, 800); await Frames(); await Click("FrontSettings");
    }

    private async Task BindingAndRestore()
    {
        await Click("SettingsTabControls");
        var gamepadEvents = InputMap.ActionGetEvents("aw_left").Where(e => e is not InputEventKey).Select(e => e.AsText()).ToArray();
        await Click("SettingsBind_left"); await KeyPress(Key.D);
        Check("duplicate_binding_is_rejected_with_visible_explanation", Keys["left"] == Key.A && Keys["right"] == Key.D &&
            Field<string>(_sandbox, "_awaitingKey") == "left" && Status.Text.Length > 0);
        await Capture("settings-duplicate-binding.png");
        await KeyPress(Key.O);
        Check("unused_key_updates_action_and_actual_input_map", Keys["left"] == Key.O &&
            InputMap.ActionGetEvents("aw_left").OfType<InputEventKey>().Single().PhysicalKeycode == Key.O && OptionalField<string>(_sandbox, "_awaitingKey") is null);
        Check("successful_rebinding_persists_readable_binding", JsonNode.Parse(ReadSettings())!["keys"]!["left"]!.GetValue<long>() == (long)Key.O &&
            Find<Button>("SettingsBind_left").Text.Contains('O'));
        await Click("SettingsBind_left"); await KeyPress(Key.Escape);
        Check("escape_cancels_pending_binding_without_closing_settings", Keys["left"] == Key.O && OptionalField<string>(_sandbox, "_awaitingKey") is null && _sandbox.FrontSettingsVisible);
        await Click("SettingsBind_left"); await Click("SettingsTabAudio"); await KeyPress(Key.L);
        Check("switching_tabs_cancels_pending_binding", Keys["left"] == Key.O && OptionalField<string>(_sandbox, "_awaitingKey") is null);
        await Click("SettingsTabControls"); await Click("SettingsBind_left"); await KeyPress(Key.Up);
        Check("navigation_key_is_reserved_and_does_not_replace_movement", Keys["left"] == Key.O && Status.Text.Length > 0);
        await KeyPress(Key.Escape); await Click("SettingsRestore");
        Check("controls_restore_defaults_and_preserve_controller_events", Keys["left"] == Key.A && Keys["right"] == Key.D && Keys.Values.Distinct().Count() == Keys.Count &&
            InputMap.ActionGetEvents("aw_left").Where(e => e is not InputEventKey).Select(e => e.AsText()).SequenceEqual(gamepadEvents));
        Check("controls_restore_does_not_reset_other_tabs", Value<bool>(_sandbox, "_reduceEffects") && Near(Value<float>(_sandbox, "_masterVolume"), .8) && Value<int>(_sandbox, "_minimumLootRarity") == 4);
        await KeyPress(Key.Tab);
        var focused = GetViewport().GuiGetFocusOwner();
        Check("keyboard_navigation_stays_inside_visible_settings", focused is not null && focused.IsVisibleInTree() && Find<Control>("SettingsPanel").IsAncestorOf(focused));
        foreach (var boundary in new[] { ("SettingsClose", Key.Tab, false, "tab_after_close"),
            ("SettingsTabControls", Key.Tab, true, "shift_tab_before_controls"),
            ("SettingsTabControls", Key.Up, false, "up_from_tabs"),
            ("SettingsClose", Key.Down, false, "down_from_footer") })
        {
            Find<Control>(boundary.Item1).GrabFocus(); PushKey(boundary.Item2, boundary.Item3); await Frames(3);
            var owner = GetViewport().GuiGetFocusOwner();
            Check(boundary.Item4 + "_keeps_focus_inside_visible_settings", owner is not null && owner.IsVisibleInTree() && Find<Control>("SettingsPanel").IsAncestorOf(owner));
            if (boundary.Item2 == Key.Tab)
                Check(boundary.Item4 + "_wraps_to_opposite_edge", owner == Find<Control>(boundary.Item3 ? "SettingsClose" : "SettingsTabControls"));
            if (boundary.Item2 == Key.Tab && !boundary.Item3)
            {
                await KeyPress(Key.Enter);
                Check("enter_after_boundary_wrap_activates_settings_without_underlying_menu", _sandbox.FrontSettingsVisible && Menu.Page == "Main" &&
                    GetViewport().GuiGetFocusOwner() == Find<Control>("SettingsTabControls"));
            }
        }
        await Click("SettingsBind_left"); await KeyPress(Key.O);
    }

    private async Task AudioAndPreferences()
    {
        await Click("SettingsTabAudio");
        foreach (string channel in Channels)
        {
            string node = "SettingsVolume" + channel;
            await SetSlider(node, 0);
            int bus = AudioServer.GetBusIndex(channel);
            Check(channel.ToLowerInvariant() + "_zero_volume_mutes_actual_bus", bus >= 0 && AudioServer.IsBusMute(bus));
            await SetSlider(node, channel == "Master" ? 72 : channel == "Music" ? 41 : channel == "Effects" ? 63 : 54);
            double expected = Find<HSlider>(node).Value / 100;
            Check(channel.ToLowerInvariant() + "_slider_unmutes_and_changes_actual_bus", !AudioServer.IsBusMute(bus) && Near(Mathf.DbToLinear(AudioServer.GetBusVolumeDb(bus)), expected));
            Check(channel.ToLowerInvariant() + "_slider_persists_fraction", Near(JsonNode.Parse(ReadSettings())![VolumeProperties[Array.IndexOf(Channels, channel)]]!.GetValue<double>(), expected));
        }
        Check("category_buses_feed_master", Channels.Skip(1).All(c => AudioServer.GetBusSend(AudioServer.GetBusIndex(c)) == "Master"));
        var voices = Descendants(_sandbox).OfType<AudioStreamPlayer>().Where(n => n.Name.ToString().StartsWith("CombatVoice", StringComparison.Ordinal)).ToArray();
        Check("all_existing_combat_voices_route_to_effects", voices.Length == 8 && voices.All(n => n.Bus == "Effects"));
        Check("hub_without_regional_bed_keeps_selected_music_gain", Field<string>(_sandbox, "_ambienceCue").Length == 0 &&
            !Descendants(_sandbox).OfType<AudioStreamPlayer>().Any(n => n.Name == "RegionalAmbience") && Near(Mathf.DbToLinear(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music"))), .41));
        Check("interface_actions_have_their_own_audio_channel", Descendants(_director).OfType<AudioStreamPlayer>().Any(n => n.Name == "InterfaceAudio" && n.Bus == "UI"));
        await Click("SettingsQuietMode");
        Check("quiet_mode_applies_and_persists_without_changing_levels", ClientAudio.QuietModeEnabled &&
            Value<bool>(_sandbox, "_quietMode") && JsonNode.Parse(ReadSettings())!["quietMode"]!.GetValue<bool>() &&
            Channels.All(c => Near(ClientAudio.GetVolume(c), Find<HSlider>("SettingsVolume" + c).Value / 100)));
        int master = AudioServer.GetBusIndex(ClientAudio.MasterBus);
        int count = AudioServer.GetBusEffectCount(master);
        for (int repeat = 0; repeat < 10; repeat++) { ClientAudio.EnsureBuses(); ClientAudio.ApplyQuietMode(true); }
        Check("repeated_scene_audio_setup_keeps_exactly_one_ordered_master_chain", count == 2 && AudioServer.GetBusEffectCount(master) == count &&
            AudioServer.GetBusEffect(master, 0) is AudioEffectCompressor && AudioServer.GetBusEffect(master, 1) is AudioEffectHardLimiter &&
            AudioServer.IsBusEffectEnabled(master, 0) && AudioServer.IsBusEffectEnabled(master, 1));
        foreach (string channel in Channels)
        {
            float volume = ClientAudio.GetVolume(channel);
            ClientAudio.SetVolume(channel, 0); ClientAudio.ApplyQuietMode(false); ClientAudio.ApplyQuietMode(true);
            Check("quiet_toggle_preserves_" + channel.ToLowerInvariant() + "_true_mute", AudioServer.IsBusMute(AudioServer.GetBusIndex(channel)) && ClientAudio.GetVolume(channel) == 0);
            ClientAudio.SetVolume(channel, volume);
        }
        if (DisplayServer.GetName() != "headless") _audioMixSamples.AddRange(await AudioMixProbe.Run(this, Check));
        await Capture("settings-audio-adjusted.png");
        await Click("SettingsRestore");
        Check("audio_restore_resets_all_four_buses_and_widgets", Channels.All(c => Near(Find<HSlider>("SettingsVolume" + c).Value, 100) && !AudioServer.IsBusMute(AudioServer.GetBusIndex(c)) && Near(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(c)), 0)));
        Check("audio_restore_disables_quiet_compression_but_preserves_peak_guard", !ClientAudio.QuietModeEnabled &&
            !Value<bool>(_sandbox, "_quietMode") && !Find<CheckButton>("SettingsQuietMode").ButtonPressed &&
            !AudioServer.IsBusEffectEnabled(master, 0) && AudioServer.IsBusEffectEnabled(master, 1));
        Check("audio_restore_keeps_rebound_controls_and_accessibility", Keys["left"] == Key.O && Value<bool>(_sandbox, "_reduceEffects"));
        foreach (var pair in new[] { ("Master", 72), ("Music", 41), ("Effects", 63), ("UI", 54) }) await SetSlider("SettingsVolume" + pair.Item1, pair.Item2);
        await Click("SettingsQuietMode");
        await Click("SettingsTabAccessibility"); await Click("SettingsRestore");
        Check("accessibility_restore_updates_live_effect_flags_and_widgets", !Value<bool>(_sandbox, "_reduceEffects") && !Value<bool>(_sandbox, "_reduceShake") &&
            !Find<CheckButton>("SettingsReducedEffects").ButtonPressed && !Find<CheckButton>("SettingsReducedShake").ButtonPressed);
        Check("accessibility_restore_preserves_audio_and_bindings", Near(Value<float>(_sandbox, "_masterVolume"), .72) && Keys["left"] == Key.O);
        await Click("SettingsReducedEffects"); await Click("SettingsReducedShake");
        Check("native_accessibility_toggles_update_live_effect_flags", Value<bool>(_sandbox, "_reduceEffects") && Value<bool>(_sandbox, "_reduceShake"));
        await Click("SettingsTabGameplay"); await Click("SettingsRestore");
        Check("gameplay_restore_resets_loot_filters_only", Value<int>(_sandbox, "_minimumLootRarity") == 0 && !Value<bool>(_sandbox, "_compatibleLootOnly") &&
            Value<bool>(_sandbox, "_reduceEffects") && Keys["left"] == Key.O && Near(Value<float>(_sandbox, "_masterVolume"), .72));
        var rarity = Rarity(); rarity.Select(3); rarity.EmitSignal(OptionButton.SignalName.ItemSelected, 3L); await Frames();
        await Click("SettingsCompatibleLoot");
        Check("gameplay_controls_change_actual_loot_preferences", Value<int>(_sandbox, "_minimumLootRarity") == 3 && Value<bool>(_sandbox, "_compatibleLootOnly"));
    }

    private async Task BlockedSettingsWrite()
    {
        await Click("SettingsTabAudio");
        string bytes = ReadSettings(), held = SettingsPath + ".held";
        System.IO.File.Move(SettingsPath, held); Directory.CreateDirectory(SettingsPath);
        try
        {
            await SetSlider("SettingsVolumeMaster", 29);
            Check("failed_settings_write_keeps_live_value_and_actual_audio", Near(Value<float>(_sandbox, "_masterVolume"), .29) && Near(Mathf.DbToLinear(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Master"))), .29));
            Check("failed_settings_write_is_visible_without_losing_previous_file", Status.Text.Contains("could not be saved", StringComparison.OrdinalIgnoreCase) && System.IO.File.ReadAllText(held) == bytes && _sandbox.FrontSettingsVisible);
            await Click("SettingsTabControls");
            Check("save_failure_notice_remains_visible_after_tab_switch", Status.Text.Contains("could not be saved", StringComparison.OrdinalIgnoreCase));
            await Capture("settings-write-failure.png");
        }
        finally { Directory.Delete(SettingsPath); System.IO.File.Move(held, SettingsPath); }
        await Click("SettingsTabAudio"); await SetSlider("SettingsVolumeMaster", 72);
        Check("next_successful_change_persists_and_clears_failure", Near(JsonNode.Parse(ReadSettings())!["masterVolume"]!.GetValue<double>(), .72) && !Status.Text.Contains("could not be saved", StringComparison.OrdinalIgnoreCase));
    }

    private void RecordDisplaySample(string mode)
    {
        var size = GetWindow().Size;
        _displaySamples.Add(new
        {
            requested = mode,
            actual = GetWindow().Mode.ToString(),
            width = size.X,
            height = size.Y,
            renderScale = GetViewport().Scaling3DScale,
            headless = DisplayServer.GetName() == "headless"
        });
    }

    private async Task SelectGraphicsOption(string name, int index)
    {
        var selector = Find<OptionButton>(name); await EnsureVisible(selector);
        selector.Select(index); selector.EmitSignal(OptionButton.SignalName.ItemSelected, index); await Frames(6);
    }

    private async Task GraphicsAndPreferences()
    {
        string hash = Session.StateHash; int frames = Session.CaptureReplay().Frames.Length;
        await Click("SettingsTabGraphics");
        Check("graphics_selector_offers_high_and_performance", Find<OptionButton>("SettingsGraphicsQuality").ItemCount == 2 &&
            Find<OptionButton>("SettingsGraphicsQuality").GetItemText(0) == "High" && Find<OptionButton>("SettingsGraphicsQuality").GetItemText(1) == "Performance");
        Check("resolution_defaults_offer_three_bounded_choices", Find<OptionButton>("SettingsRenderScale").ItemCount == 3 && Near(_sandbox.RenderScale, 1.25));
        Check("window_fit_preserves_aspect_and_stays_inside_small_and_large_displays", Sandbox.FitWindowSize(new(1920, 1080)) == new Vector2I(1568, 980) &&
            Sandbox.FitWindowSize(new(3840, 2160)) == new Vector2I(1600, 1000) && Sandbox.FitWindowSize(new(800, 600)) == new Vector2I(720, 450));
        await SelectGraphicsOption("SettingsRenderScale", 2);
        Check("maximum_resolution_applies_and_persists", Near(_sandbox.RenderScale, 1.5) && Near(GetViewport().Scaling3DScale, 1.5) &&
            GetViewport().Scaling3DMode == Viewport.Scaling3DModeEnum.Bilinear && Near(JsonNode.Parse(ReadSettings())!["renderScale"]!.GetValue<double>(), 1.5));
        await SelectGraphicsOption("SettingsRenderScale", 0);
        Check("native_resolution_removes_supersampling", Near(GetViewport().Scaling3DScale, 1));
        await SelectGraphicsOption("SettingsRenderScale", 2);
        await SelectGraphics("Performance");
        Check("performance_uses_native_but_remembers_high_resolution", Near(GetViewport().Scaling3DScale, 1) && Near(_sandbox.RenderScale, 1.5));
        await SelectGraphicsOption("SettingsDisplayMode", 1);
        Check("fullscreen_selection_persists", _sandbox.DisplayMode == "Fullscreen" && JsonNode.Parse(ReadSettings())!["displayMode"]!.GetValue<string>() == "Fullscreen" &&
            (DisplayServer.GetName() == "headless" || GetWindow().Mode == Window.ModeEnum.Fullscreen));
        RecordDisplaySample("fullscreen"); await Capture("settings-graphics-fullscreen.png");
        await SelectGraphicsOption("SettingsDisplayMode", 0);
        RecordDisplaySample("windowed");
        Check("windowed_selection_restores_a_visible_window", _sandbox.DisplayMode == "Windowed" && (DisplayServer.GetName() == "headless" || GetWindow().Mode == Window.ModeEnum.Windowed));
        Check("graphics_selection_applies_and_persists", _sandbox.GraphicsQuality == "Performance" &&
            JsonNode.Parse(ReadSettings())!["graphicsQuality"]!.GetValue<string>() == "Performance");
        var environment = Field<WorldEnvironment>(_sandbox, "_worldEnvironment").Environment;
        Check("performance_reduces_actual_rendering_work", GetViewport().Msaa3D == Viewport.Msaa.Msaa2X &&
            !environment.SsaoEnabled && !environment.GlowEnabled);
        await Capture("settings-graphics-performance.png");
        await Click("SettingsRestore");
        Check("graphics_restore_updates_live_quality_and_selector", _sandbox.GraphicsQuality == "High" && Find<OptionButton>("SettingsGraphicsQuality").Selected == 0 &&
            JsonNode.Parse(ReadSettings())!["graphicsQuality"]!.GetValue<string>() == "High");
        Check("graphics_restore_resets_resolution_and_display", Near(_sandbox.RenderScale, 1.25) && Near(GetViewport().Scaling3DScale, 1.25) &&
            _sandbox.DisplayMode == "Windowed" && Find<OptionButton>("SettingsRenderScale").Selected == 1 && Find<OptionButton>("SettingsDisplayMode").Selected == 0);
        Check("high_enables_antialiasing_and_contact_shadows_but_respects_reduced_effects", GetViewport().Msaa3D == Viewport.Msaa.Msaa8X &&
            environment.SsaoEnabled && !environment.GlowEnabled && environment.Sky is not null);
        Check("graphics_restore_preserves_all_other_preferences", Keys["left"] == Key.O && Value<bool>(_sandbox, "_reduceEffects") && Value<bool>(_sandbox, "_reduceShake") &&
            Near(Value<float>(_sandbox, "_masterVolume"), .72) && Near(Value<float>(_sandbox, "_musicVolume"), .41) &&
            Near(Value<float>(_sandbox, "_effectsVolume"), .63) && Near(Value<float>(_sandbox, "_interfaceVolume"), .54) &&
            Value<int>(_sandbox, "_minimumLootRarity") == 3 && Value<bool>(_sandbox, "_compatibleLootOnly"));
        await Click("SettingsTabAccessibility"); await Click("SettingsReducedEffects");
        Check("high_restores_gentle_bloom_when_reduced_effects_is_disabled", environment.GlowEnabled && environment.SsaoEnabled);
        await Click("SettingsReducedEffects");
        Check("reduced_effects_disables_bloom_immediately", !environment.GlowEnabled && environment.SsaoEnabled);
        await Click("SettingsTabGraphics");
        await SelectGraphics("Performance");
        AdvanceWhilePaused();
        Check("graphics_selection_and_restore_do_not_change_gameplay", Session.StateHash == hash && Session.CaptureReplay().Frames.Length == frames &&
            !Directory.EnumerateFiles(_output, "*.save.json").Any() && _sandbox.IsPaused);
    }

    private async Task RestartPersistenceAndInvalidRecovery()
    {
        string saved = ReadSettings(); await RestartDirector(); await Click("FrontSettings");
        Check("restart_reads_saved_controls_accessibility_and_loot", Keys["left"] == Key.O && Value<bool>(_sandbox, "_reduceEffects") && Value<bool>(_sandbox, "_reduceShake") &&
            Value<int>(_sandbox, "_minimumLootRarity") == 3 && Value<bool>(_sandbox, "_compatibleLootOnly"));
        Check("restart_applies_all_persisted_audio_values", Near(Value<float>(_sandbox, "_masterVolume"), .72) && Near(Value<float>(_sandbox, "_musicVolume"), .41) &&
            Near(Value<float>(_sandbox, "_effectsVolume"), .63) && Near(Value<float>(_sandbox, "_interfaceVolume"), .54) &&
            Near(Mathf.DbToLinear(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music"))), .41));
        Check("restart_restores_quiet_mode_once_with_saved_volumes", Value<bool>(_sandbox, "_quietMode") && ClientAudio.QuietModeEnabled &&
            Find<CheckButton>("SettingsQuietMode").ButtonPressed && AudioServer.GetBusEffectCount(AudioServer.GetBusIndex(ClientAudio.MasterBus)) == 2);
        Check("restart_loads_persisted_graphics_quality_and_selector", _sandbox.GraphicsQuality == "Performance" && Find<OptionButton>("SettingsGraphicsQuality").Selected == 1);
        Check("restart_applies_persisted_quality_to_renderer", GetViewport().Msaa3D == Viewport.Msaa.Msaa2X &&
            !Field<WorldEnvironment>(_sandbox, "_worldEnvironment").Environment.SsaoEnabled);
        Check("restart_loads_resolution_and_display_preferences", Near(_sandbox.RenderScale, 1.25) && _sandbox.DisplayMode == "Windowed" && Near(GetViewport().Scaling3DScale, 1));
        Check("restart_is_read_only_for_valid_preferences", ReadSettings() == saved);
        await Click("SettingsTabAudio"); await Capture("settings-restored-after-restart.png");
        await RemoveDirector();
        foreach (string? quality in new string?[] { "UnsupportedQuality", null })
        {
            var normalized = JsonNode.Parse(saved)!.AsObject(); normalized["graphicsQuality"] = quality;
            System.IO.File.WriteAllText(SettingsPath, normalized.ToJsonString());
            string bytes = ReadSettings(); await StartDirector(); _restarts++; await Click("FrontSettings");
            Check("invalid_graphics_" + (quality is null ? "null" : "name") + "_normalizes_without_resetting_or_rewriting_other_preferences",
                _sandbox.GraphicsQuality == "High" && Find<OptionButton>("SettingsGraphicsQuality").Selected == 0 &&
                Keys["left"] == Key.O && Value<bool>(_sandbox, "_reduceEffects") && Near(Value<float>(_sandbox, "_masterVolume"), .72) &&
                Value<int>(_sandbox, "_minimumLootRarity") == 3 && ReadSettings() == bytes);
            await RemoveDirector();
        }
        var unsupportedDisplay = JsonNode.Parse(saved)!.AsObject(); unsupportedDisplay["renderScale"] = 100; unsupportedDisplay["displayMode"] = "unsupported";
        System.IO.File.WriteAllText(SettingsPath, unsupportedDisplay.ToJsonString());
        string unsupportedBytes = ReadSettings(); await StartDirector(); _restarts++; await Click("FrontSettings");
        Check("unsupported_resolution_and_display_normalize_without_rewriting", Near(_sandbox.RenderScale, 1.25) && _sandbox.DisplayMode == "Windowed" && Keys["left"] == Key.O && ReadSettings() == unsupportedBytes);
        await RemoveDirector();
        var invalid = JsonNode.Parse(saved)!.AsObject(); invalid["musicVolume"] = 1.01;
        System.IO.File.WriteAllText(SettingsPath, invalid.ToJsonString());
        System.IO.File.WriteAllText(SettingsPath + ".bak", saved);
        string invalidBytes = ReadSettings(); await StartDirector(); _restarts++; await Click("FrontSettings");
        Check("out_of_range_volume_recovers_backup_without_clamping_or_rewriting_archive", Near(Value<float>(_sandbox, "_musicVolume"), .41) &&
            ReadSettings() == invalidBytes && System.IO.File.ReadAllText(SettingsPath + ".bak") == saved && Status.Text.Contains("recovered", StringComparison.OrdinalIgnoreCase));
        await RemoveDirector();
        System.IO.File.WriteAllText(SettingsPath, "{\"keys\":{},\"masterVolume\":1e999}");
        System.IO.File.WriteAllText(SettingsPath + ".bak", "{broken");
        string nonfiniteBytes = ReadSettings(); await StartDirector(); _restarts++; await Click("FrontSettings");
        Check("nonfinite_volume_and_invalid_backup_leave_safe_defaults", Near(Value<float>(_sandbox, "_masterVolume"), 1) && Near(Value<float>(_sandbox, "_musicVolume"), 1) &&
            !Value<bool>(_sandbox, "_reduceEffects") && !ClientAudio.QuietModeEnabled && Keys["left"] == Key.A && ReadSettings() == nonfiniteBytes && Status.Text.Length > 0);
        await RemoveDirector();
        var legacy = JsonNode.Parse(saved)!.AsObject(); foreach (string key in VolumeProperties) legacy.Remove(key); legacy.Remove("graphicsQuality"); legacy.Remove("renderScale"); legacy.Remove("displayMode"); legacy.Remove("quietMode");
        System.IO.File.WriteAllText(SettingsPath, legacy.ToJsonString());
        string legacyBytes = ReadSettings(); await StartDirector(); _restarts++; await Click("FrontSettings");
        Check("legacy_settings_without_audio_fields_keep_options_and_default_volume", Keys["left"] == Key.O && Value<bool>(_sandbox, "_reduceEffects") &&
            Value<int>(_sandbox, "_minimumLootRarity") == 3 && Channels.All(c => Near(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(c)), 0)) && ReadSettings() == legacyBytes);
        Check("legacy_settings_without_quiet_mode_load_off_read_only", !ClientAudio.QuietModeEnabled && !Value<bool>(_sandbox, "_quietMode") && ReadSettings() == legacyBytes);
        Check("legacy_settings_without_graphics_field_load_high_read_only", _sandbox.GraphicsQuality == "High" && Find<OptionButton>("SettingsGraphicsQuality").Selected == 0 && ReadSettings() == legacyBytes);
        Check("legacy_settings_default_to_enhanced_and_windowed_without_rewrite", Near(_sandbox.RenderScale, 1.25) && _sandbox.DisplayMode == "Windowed" && ReadSettings() == legacyBytes);
        Check("all_settings_restarts_leave_character_archives_absent", !Directory.EnumerateFiles(_output, "*.save.json").Any() && !System.IO.File.Exists(Path.Combine(_output, "current-character.txt")));
        await Click("SettingsClose");
    }

    private async Task InGamePause()
    {
        await Click("FrontNew"); await Click("FrontDisciplineVanguard"); await Click("FrontBegin");
        Check("native_new_character_starts_play_after_settings_restarts", !Menu.IsOpen && !_sandbox.IsPaused);
        string hash = Session.StateHash, replayHash = JsonData.Hash(Session.CaptureReplay());
        var files = Directory.EnumerateFiles(_output, "*.save.json").ToDictionary(p => Path.GetFileName(p)!, System.IO.File.ReadAllText);
        await KeyPress(Key.Escape);
        Check("in_game_escape_opens_settings_and_pauses", Find<Control>("SettingsPanel").Visible && _sandbox.IsPaused);
        await Click("SettingsTabGraphics"); await SelectGraphics("Performance"); await Click("SettingsRestore");
        Check("in_game_graphics_changes_preserve_character_and_replay", Session.StateHash == hash && JsonData.Hash(Session.CaptureReplay()) == replayHash &&
            files.All(p => System.IO.File.ReadAllText(Path.Combine(_output, p.Key!)) == p.Value) && _sandbox.GraphicsQuality == "High");
        await Click("SettingsTabAudio");
        foreach (Key key in new[] { Key.O, Key.F, Key.C, Key.J, Key.B, Key.H, Key.P, Key.F5, Key.F9, Key.F6 }) await KeyPress(key);
        AdvanceWhilePaused();
        Check("in_game_settings_prevent_world_ticks_and_shortcuts", Session.StateHash == hash && JsonData.Hash(Session.CaptureReplay()) == replayHash &&
            files.All(p => System.IO.File.ReadAllText(Path.Combine(_output, p.Key!)) == p.Value));
        await Capture("settings-in-game-paused.png"); await Click("SettingsClose");
        Check("closing_in_game_settings_resumes_when_no_other_pause_owner", !Find<Control>("SettingsPanel").Visible && !_sandbox.IsPaused);
        await KeyPress(Key.P); await ClickText("Settings"); await Click("SettingsClose");
        Check("closing_settings_preserves_manual_pause_and_resume_focus", _sandbox.IsPaused && GetViewport().GuiGetFocusOwner() is Button { Text: "Resume playing" });
        await ClickText("Resume playing");
        Check("explicit_resume_releases_manual_pause", !_sandbox.IsPaused);
        await KeyPress(Key.Escape); await Click("SettingsTabControls"); await Click("SettingsBind_left");
        _sandbox.AutomaticStep = false;
        try { _sandbox.Notification((int)NotificationApplicationFocusOut); }
        finally { _sandbox.AutomaticStep = true; }
        await KeyPress(Key.L);
        Check("focus_loss_cancels_pending_rebind_without_capturing_return_key", OptionalField<string>(_sandbox, "_awaitingKey") is null && Keys["left"] == Key.O &&
            Find<Control>("SettingsPanel").Visible && _sandbox.IsPaused);
        await Capture("settings-focus-interruption.png"); await Click("SettingsClose");
        Check("closing_after_focus_loss_requires_explicit_resume", _sandbox.IsPaused && GetViewport().GuiGetFocusOwner() is Button { Text: "Resume playing" });
        await ClickText("Resume playing");
        Check("explicit_resume_after_settings_focus_loss_resumes", !_sandbox.IsPaused);
    }

    private async Task StartDirector()
    {
        _director = new EndgameDirector(); AddChild(_director);
        _sandbox = Field<Sandbox>(_director, "_sandbox"); _sandbox.SetProcess(false); _sandbox.AutomaticStep = true;
        await Frames(8);
    }
    private async Task RemoveDirector()
    {
        // Hold the managed wrappers deliberately: GPU cleanup must not depend on GC timing.
        var environments = Descendants(_director).OfType<WorldEnvironment>().Select(world => world.Environment).ToArray();
        _director.QueueFree(); await Frames(5);
        Check("scene_teardown_" + ++_worldTeardowns + "_releases_owned_sky_resources", environments.Length > 0 && environments.All(environment => environment.Sky is null));
    }
    private async Task RestartDirector() { await RemoveDirector(); await StartDirector(); _restarts++; }
    private void AdvanceWhilePaused() { for (int i = 0; i < 12; i++) _sandbox._Process(FixedStepClock.SecondsPerTick); }
    private OptionButton Rarity() => Descendants(_sandbox).OfType<OptionButton>().Single(n => n.Name == "SettingsMinimumRarity" || n.Name == "LootRarityFilter");
    private string ReadSettings() => System.IO.File.ReadAllText(SettingsPath);
    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < .011;
    private void AssertLayout(string key)
    {
        var panel = Find<Control>("SettingsPanel").GetGlobalRect();
        Check(key + "_panel_fits_viewport", GetViewport().GetVisibleRect().Grow(2).Encloses(panel));
        var actions = Tabs.Select(t => Find<Control>("SettingsTab" + t)).Concat(new[] { Find<Control>("SettingsClose"), Find<Control>("SettingsRestore"), Status }).ToArray();
        Check(key + "_tabs_and_footer_remain_visible_inside_panel", actions.All(c => c.IsVisibleInTree() && panel.Grow(2).Encloses(c.GetGlobalRect())));
        Check(key + "_tabs_and_footer_do_not_overlap", actions.All(a => actions.All(b => a == b || !a.GetGlobalRect().Intersects(b.GetGlobalRect()))));
    }
    private async Task SetSlider(string name, int value)
    {
        var slider = Find<HSlider>(name); await EnsureVisible(slider); slider.GrabFocus();
        PushKey(Key.Home);
        for (int i = 0; i < value; i++) PushKey(Key.Right);
        await Frames(3);
        if (!Near(slider.Value, value)) throw new InvalidDataException($"Native slider {name} expected {value}, received {slider.Value}.");
    }
    private async Task SelectGraphics(string quality)
    {
        var selector = Find<OptionButton>("SettingsGraphicsQuality"); await EnsureVisible(selector);
        int index = quality == "Performance" ? 1 : 0;
        // Popup-window keyboard dispatch is unavailable in headless; use the same public selection contract as loot rarity.
        selector.Select(index); selector.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index); await Frames(3);
    }
    private async Task Click(string name) => await ClickControl(Find<Control>(name));
    private async Task ClickText(string text) => await ClickControl(Descendants(_director).OfType<Button>().Single(b => b.Text == text && b.IsVisibleInTree()));
    private async Task ClickControl(Control control)
    {
        await EnsureVisible(control);
        var point = control.GetGlobalRect().GetCenter(); GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, Pressed = pressed }, true);
        await Frames(3);
    }
    private async Task EnsureVisible(Control control)
    {
        for (Node? parent = control.GetParent(); parent is not null; parent = parent.GetParent())
            if (parent is ScrollContainer scroll) { scroll.EnsureControlVisible(control); await Frames(3); }
        if (!control.IsVisibleInTree()) throw new InvalidDataException("Native settings control is hidden: " + control.Name);
    }
    private void PushKey(Key key, bool shift = false)
    {
        foreach (bool pressed in new[] { true, false }) GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, ShiftPressed = shift, Pressed = pressed }, true);
    }
    private async Task KeyPress(Key key) { PushKey(key); await Frames(3); }
    private void Resize(int width, int height) { GetWindow().Size = new(width, height); GetWindow().ContentScaleSize = new(width, height); }
    private async Task Frames(int count = 2)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // The diagnostic owns stepping; preserve the layout refresh normally performed by Sandbox._Process.
            if (_sandbox is not null && GodotObject.IsInstanceValid(_sandbox))
                typeof(Sandbox).GetMethod("LayoutSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_sandbox, null);
        }
    }
    private async Task Capture(string filename)
    {
        if (!_writeReport || !OS.GetCmdlineUserArgs().Contains("--capture-settings") || DisplayServer.GetName() == "headless") return;
        await Frames(3); RenderingServer.ForceDraw(false); RenderingServer.ForceSync();
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok); _captures.Add(filename);
    }
    private static T Field<T>(object owner, string name) where T : class => OptionalField<T>(owner, name) ?? throw new MissingFieldException(name);
    private static T? OptionalField<T>(object owner, string name) where T : class => (T?)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner);
    private static T Value<T>(object owner, string name) => (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) ?? throw new MissingFieldException(name));
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private T Find<T>(string name) where T : Node => Descendants(_director).OfType<T>().Single(n => n.Name == name);
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Settings check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "SettingsClientSmokePassed" : "SettingsClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            displaySamples = _displaySamples,
            audioMixSamples = _audioMixSamples,
            restarts = _restarts,
            error,
            scope = "Shipping EndgameDirector ordinary startup and native viewport settings clicks/keys at 1280x800, 1000x720 and 780x720; duplicate and cancelled bindings, tab restores, High/Performance selection and persistence, invalid graphics normalization, saved Quiet mode, single Master compressor/peak guard, native-only captured aggregate and representative audio mix measurements, actual bus gain/mute and existing combat/interface voice routing (regional bed routing is covered by the regional diagnostics), session-only write failure, fresh director preference reloads, invalid-volume backup/default recovery, legacy settings, and in-game/manual pause isolation. The graphics and rarity OptionButtons use their public Select/ItemSelected contract because headless input does not dispatch popup-window keyboard events; sliders and all other changes use viewport input. Graphics preferences are checked for gameplay-state isolation; rendered quality is covered by the graphics diagnostic. Settings fixtures and one new native character are confined to this fresh artifact directory. No existing player saves are edited; audible quality and physical controllers are not certified."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "settings-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
