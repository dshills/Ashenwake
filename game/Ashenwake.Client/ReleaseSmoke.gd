extends Node
var frames = 0
var checks = {}
var output = ""
var sandbox
var paused_tick = 0
var paused_position
var _finished = false
func _ready():
    Engine.max_fps = 60
    var custom_preferences = false
    for arg in OS.get_cmdline_user_args():
        if arg.begins_with("--output="): output = arg.trim_prefix("--output=")
        if arg.begins_with("--preferences="): custom_preferences = true
    if "--release-smoke" not in OS.get_cmdline_user_args() or output.is_empty() or custom_preferences:
        push_error("Release smoke requires --release-smoke --output=<isolated-directory> and uses its own settings.")
        get_tree().quit(2)
        return
    DirAccess.make_dir_recursive_absolute(output)
    var primary = FileAccess.open(output.path_join("settings.json"), FileAccess.WRITE)
    primary.store_string(JSON.stringify({"reducedEffects":false,"reducedShake":false,"keys":null}))
    primary.close()
    var backup = FileAccess.open(output.path_join("settings.json.bak"), FileAccess.WRITE)
    backup.store_string(JSON.stringify({"reducedEffects":true,"reducedShake":true,"keys":{"left":KEY_A},"minimumLootRarity":4,"compatibleLootOnly":true}))
    backup.close()
    sandbox = load("res://Sandbox.tscn").instantiate()
    add_child.call_deferred(sandbox)
func nodes(node):
    var result = []
    for child in node.get_children():
        result.append(child)
        result.append_array(nodes(child))
    return result
func button(prefix):
    for node in nodes(get_tree().root):
        if node is Button and node.text.begins_with(prefix): return node
    return null
func saved():
    button("Save character").pressed.emit()
    return JSON.parse_string(FileAccess.get_file_as_string(output.path_join("sandbox.save.json"))).state
func key(code):
    for pressed in [true, false]:
        var event = InputEventKey.new()
        event.physical_keycode = code
        event.pressed = pressed
        get_viewport().push_input(event, true)
func _process(_delta):
    if _finished: return
    frames += 1
    match frames:
        10: Input.action_press("aw_right")
        25:
            sandbox.notification(Node.NOTIFICATION_APPLICATION_FOCUS_OUT)
            var state = saved()
            paused_tick = state.tick
            paused_position = state.actors[0].position
        55:
            var state = saved()
            checks["focus_loss_pauses_ticks"] = state.tick == paused_tick
            checks["focus_loss_freezes_position"] = state.actors[0].position == paused_position
            checks["explicit_resume_is_visible"] = button("Resume playing").is_visible_in_tree()
            button("Resume playing").pressed.emit()
        75:
            var state = saved()
            checks["resume_advances_ticks"] = state.tick > paused_tick
            checks["resume_clears_movement"] = state.actors[0].position == paused_position
            Input.joy_connection_changed.emit(0, false)
            paused_tick = saved().tick
        95:
            checks["disconnect_pauses_ticks"] = saved().tick == paused_tick
            Input.joy_connection_changed.emit(0, true)
        110:
            checks["reconnect_does_not_auto_resume"] = saved().tick == paused_tick
            button("Resume playing").pressed.emit()
        125:
            button("Settings").pressed.emit()
            var focused = get_viewport().gui_get_focus_owner()
            checks["settings_receives_keyboard_focus"] = focused is Button and focused.name == "SettingsTabControls"
            paused_tick = saved().tick
            for node in nodes(get_tree().root):
                if node is CheckButton and node.text == "Reduced visual effects": checks["backup_restores_reduced_effects"] = node.button_pressed
                if node is CheckButton and node.text == "Reduced camera shake": checks["backup_restores_reduced_shake"] = node.button_pressed
                if node is Label and "Settings recovered from" in node.text: checks["backup_recovery_visible"] = true
            get_tree().root.find_child("SettingsTabGameplay", true, false).pressed.emit()
            button("Export local diagnostics").pressed.emit()
        150:
            checks["settings_pause_does_not_advance_ticks"] = saved().tick == paused_tick
            var files = DirAccess.get_files_at(output.path_join("diagnostics"))
            checks["diagnostic_export_created"] = files.size() == 1
            if files.size() == 1:
                var report = JSON.parse_string(FileAccess.get_file_as_string(output.path_join("diagnostics").path_join(files[0])))
                checks["diagnostic_excludes_replay"] = report.optInReplay == null
                checks["diagnostic_has_identity"] = report.buildId.length() == 32 and report.contentHash.length() == 64
                checks["diagnostic_event_ring_bounded"] = report.recentEvents.size() <= 256
                checks["diagnostic_events_exist"] = report.recentEvents.size() > 0
            key(KEY_P)
        175:
            checks["pause_key_cannot_resume_settings"] = saved().tick == paused_tick
            button("Close settings").pressed.emit()
        190:
            key(KEY_P)
            paused_tick = saved().tick
        205:
            if "--capture-release" in OS.get_cmdline_user_args() and DisplayServer.get_name() != "headless": capture_pause.call_deferred()
        210:
            checks["manual_pause_stops_ticks"] = saved().tick == paused_tick
            checks["manual_pause_shows_resume"] = button("Resume playing").is_visible_in_tree()
            checks["manual_pause_focuses_resume"] = get_viewport().gui_get_focus_owner() == button("Resume playing")
            button("Settings").pressed.emit()
        230:
            checks["settings_hides_manual_resume"] = not button("Resume playing").is_visible_in_tree()
            checks["manual_pause_settings_stops_ticks"] = saved().tick == paused_tick
            button("Close settings").pressed.emit()
        250:
            checks["settings_close_preserves_manual_pause"] = saved().tick == paused_tick and button("Resume playing").is_visible_in_tree()
            checks["settings_close_focuses_resume"] = get_viewport().gui_get_focus_owner() == button("Resume playing")
            key(KEY_P)
        270:
            checks["pause_key_resumes_manual_pause"] = saved().tick > paused_tick and not button("Resume playing").is_visible_in_tree()
            button("Settings").pressed.emit()
            sandbox.notification(Node.NOTIFICATION_APPLICATION_FOCUS_OUT)
            paused_tick = saved().tick
        290:
            checks["focus_interruption_keeps_settings_active"] = not button("Resume playing").is_visible_in_tree() and saved().tick == paused_tick
            button("Close settings").pressed.emit()
        310:
            checks["settings_close_preserves_interruption_pause"] = saved().tick == paused_tick and button("Resume playing").is_visible_in_tree()
            button("Resume playing").pressed.emit()
            button("Settings").pressed.emit()
            get_tree().root.find_child("SettingsTabGameplay", true, false).pressed.emit()
            button("Inspect ground loot").pressed.emit()
            sandbox.notification(Node.NOTIFICATION_APPLICATION_FOCUS_OUT)
            paused_tick = saved().tick
        330:
            checks["interruption_keeps_loot_inspector_active"] = not button("Resume playing").is_visible_in_tree() and saved().tick == paused_tick
            button("Close inspection").pressed.emit()
        350:
            checks["loot_close_preserves_interruption_pause"] = saved().tick == paused_tick and button("Resume playing").is_visible_in_tree()
            checks["loot_close_focuses_resume"] = get_viewport().gui_get_focus_owner() == button("Resume playing")
            button("Resume playing").pressed.emit()
            sandbox.SetModalPaused("smoke-a", true)
            sandbox.SetModalPaused("smoke-b", true)
            sandbox.SetModalPaused("smoke-a", false)
            key(KEY_P)
            paused_tick = saved().tick
        370:
            checks["overlapping_modal_owners_keep_pause"] = saved().tick == paused_tick and not button("Resume playing").is_visible_in_tree()
            sandbox.SetModalPaused("smoke-b", false)
        390:
            checks["last_modal_owner_releases_pause"] = saved().tick > paused_tick
            button("Settings").pressed.emit()
            get_tree().root.find_child("SettingsTabGameplay", true, false).pressed.emit()
            var ok = checks.size() == 33
            for value in checks.values(): ok = ok and value
            var report = {"kind":"ReleaseClientSmokePassed" if ok else "ReleaseClientSmokeFailed", "passed":ok,"frames":frames,"checks":checks,"note":"Godot engine notifications and connection signals exercised in software. No physical-controller certification."}
            var file = FileAccess.open(output.path_join("release-ui-review.json"),FileAccess.WRITE)
            file.store_string(JSON.stringify(report,"  "))
            print(JSON.stringify(report))
            _finished = true
            finish.call_deferred(ok)
    return

func capture_pause():
    await RenderingServer.frame_post_draw
    get_viewport().get_texture().get_image().save_png(output.path_join("manual-pause.png"))

func finish(ok):
    if "--capture-release" in OS.get_cmdline_user_args() and DisplayServer.get_name() != "headless":
        var target = button("Export local diagnostics")
        for node in nodes(get_tree().root):
            if node.name == "DiagnosticStatus": target = node
        var parent = target.get_parent()
        while parent != null:
            if parent is ScrollContainer:
                parent.ensure_control_visible(target)
                break
            parent = parent.get_parent()
        await get_tree().process_frame
        await RenderingServer.frame_post_draw
        get_viewport().get_texture().get_image().save_png(output.path_join("release-settings.png"))
    get_tree().quit(0 if ok else 1)
