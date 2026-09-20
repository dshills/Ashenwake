extends Node

var output = ""
var checks = {}

func _ready():
    Engine.max_fps = 60
    for arg in OS.get_cmdline_user_args():
        if arg.begins_with("--output="): output = arg.trim_prefix("--output=")
    if "--interaction-smoke" not in OS.get_cmdline_user_args() or output.is_empty():
        push_error("Interaction smoke requires --interaction-smoke --output=<isolated-directory>.")
        get_tree().quit(2)
        return
    DirAccess.make_dir_recursive_absolute(output)
    run.call_deferred()

func nodes(node):
    var result = []
    for child in node.get_children():
        result.append(child)
        result.append_array(nodes(child))
    return result

func button_containing(text):
    for node in nodes(self):
        if node is Button and text in node.text and node.is_visible_in_tree(): return node
    return null

func button_named(name):
    for node in nodes(self):
        if node is Button and node.name == name and node.is_visible_in_tree(): return node
    return null

func journey_close_button():
    for panel in nodes(self):
        if panel.name != "CampaignPanel": continue
        for node in nodes(panel):
            if node is Button and node.text == "Close" and node.is_visible_in_tree(): return node
    return null

func visible_label(prefix):
    for node in nodes(self):
        if node is Label and node.text.begins_with(prefix) and node.is_visible_in_tree(): return true
    return false

func settle():
    for i in range(3): await get_tree().process_frame

func key(code):
    var event = InputEventKey.new()
    event.keycode = code
    event.physical_keycode = code
    event.pressed = true
    get_viewport().push_input(event, true)
    await settle()
    event = InputEventKey.new()
    event.keycode = code
    event.physical_keycode = code
    event.pressed = false
    get_viewport().push_input(event, true)
    await settle()

func click(button):
    if button == null or not button.is_visible_in_tree(): return
    var position = button.get_global_rect().get_center()
    for pressed in [true, false]:
        var event = InputEventMouseButton.new()
        event.button_index = MOUSE_BUTTON_LEFT
        event.position = position
        event.pressed = pressed
        get_viewport().push_input(event, true)
        await settle()

func saved_tick():
    await key(KEY_F5)
    return JSON.parse_string(FileAccess.get_file_as_string(output.path_join("endgame.save.json"))).state.tick

func expedition_is_open():
    for node in nodes(self):
        if node.name == "ExpeditionPanel" and node is Control: return node.is_visible_in_tree()
    return false

func check_expedition_navigation():
    await key(KEY_B)
    checks["expedition_key_opens_locked_board"] = expedition_is_open() and visible_label("The seals have not yet broken") and button_named("ExpeditionEnter") == null
    var paused_tick = await saved_tick()
    await key(KEY_F)
    await key(KEY_PERIOD)
    await key(KEY_W)
    for i in range(12): await get_tree().process_frame
    checks["expedition_board_blocks_world_input_and_simulation"] = expedition_is_open() and await saved_tick() == paused_tick
    await key(KEY_B)
    checks["expedition_key_closes_board"] = not expedition_is_open()
    for i in range(12): await get_tree().process_frame
    checks["expedition_close_releases_its_pause"] = await saved_tick() > paused_tick
    await key(KEY_P)
    paused_tick = await saved_tick()
    await key(KEY_B)
    await key(KEY_ESCAPE)
    checks["expedition_escape_preserves_independent_manual_pause"] = not expedition_is_open() and button_containing("Resume playing") != null and await saved_tick() == paused_tick
    await click(button_containing("Resume playing"))
    await key(KEY_B)
    await key(KEY_C)
    checks["expedition_character_shortcut_hands_off_to_character"] = not expedition_is_open() and button_containing("Close character") != null
    await click(button_containing("Close character"))
    await key(KEY_B)
    await key(KEY_I)
    checks["expedition_inventory_shortcut_hands_off_to_gear"] = not expedition_is_open() and button_containing("Close character") != null and visible_label("EQUIPPED ·")
    await click(button_containing("Close character"))
    await key(KEY_B)
    await key(KEY_J)
    checks["expedition_journey_shortcut_hands_off_to_map"] = not expedition_is_open() and visible_label("EDRATH · REGIONS")
    await key(KEY_B)
    checks["journey_expedition_shortcut_hands_off_to_board"] = expedition_is_open() and not visible_label("EDRATH · REGIONS")
    await key(KEY_H)
    checks["expedition_echoes_shortcut_hands_off_to_echoes"] = not expedition_is_open() and visible_label("ECHOES: BORROWED MEMORY")
    await key(KEY_ESCAPE)

func run():
    add_child(load("res://Endgame.tscn").instantiate())
    await settle()
    await click(button_containing("Vanguard ·"))
    checks["discipline_selection_accepts_mouse"] = not visible_label("CHOOSE YOUR FIRST DISCIPLINE")
    checks["journey_starts_closed_after_discipline_selection"] = not visible_label("EDRATH · REGIONS")
    await key(KEY_J)
    checks["journey_key_opens_map"] = visible_label("EDRATH · REGIONS")
    await key(KEY_J)
    checks["journey_key_closes_map"] = not visible_label("EDRATH · REGIONS")
    await check_expedition_navigation()
    var resident = null
    for stage in nodes(self):
        if stage.get_script() != null and stage.get_script().resource_path.ends_with("/AdventureStage.cs"):
            for node in nodes(stage):
                if node.get_script() != null and node.get_script().resource_path.ends_with("/CharacterVisual.cs"):
                    resident = node
                    break
            if resident != null: break
    checks["resident_has_idle_animation"] = false
    checks["resident_idle_respects_pause"] = false
    if resident != null:
        var body = resident.get_child(0)
        var origin = resident.transform
        var idle_pose = body.transform
        for i in range(12): await get_tree().process_frame
        checks["resident_has_idle_animation"] = not body.transform.is_equal_approx(idle_pose) and resident.transform.is_equal_approx(origin)
        await key(KEY_P)
        idle_pose = body.transform
        for i in range(12): await get_tree().process_frame
        checks["resident_idle_respects_pause"] = body.transform.is_equal_approx(idle_pose)
        await click(button_containing("Resume playing"))
    await key(KEY_F)
    checks["interact_shows_mara_dialogue"] = visible_label("Mara: The wound will hold.")
    checks["interact_exposes_campaign_entry"] = visible_label("EDRATH · REGIONS")
    var journey = button_containing("[J]")
    var fractures = button_containing("[B]")
    var echoes = button_containing("[H]")
    checks["navigation_buttons_do_not_overlap"] = not journey.get_global_rect().intersects(echoes.get_global_rect()) and not journey.get_global_rect().intersects(fractures.get_global_rect()) and not fractures.get_global_rect().intersects(echoes.get_global_rect())
    # The modal has its own Close button; the top navigation toggle sits behind its backdrop.
    if not visible_label("EDRATH · REGIONS"): await key(KEY_J)
    await click(journey_close_button())
    checks["journey_close_button_is_clickable"] = not visible_label("EDRATH · REGIONS")
    # Esc closes an accidentally opened Echoes panel on the old, overlapping layout.
    if visible_label("ECHOES: BORROWED MEMORY"): await key(KEY_ESCAPE)
    if not visible_label("EDRATH · REGIONS"): await key(KEY_J)
    await click(button_containing("Mara · Divine Anatomy"))
    checks["mara_service_opens_anatomy"] = visible_label("DIVINE ANATOMY ·")
    await click(button_containing("Map"))
    if "--capture-interaction" in OS.get_cmdline_user_args() and DisplayServer.get_name() != "headless":
        await RenderingServer.frame_post_draw
        get_viewport().get_texture().get_image().save_png(output.path_join("mara-map.png"))
    await click(button_named("JourneyRegion1"))
    await click(button_named("JourneyTravel"))
    await key(KEY_F5)
    var path = output.path_join("endgame.save.json")
    checks["campaign_entry_saves"] = FileAccess.file_exists(path)
    if FileAccess.file_exists(path):
        var saved = JSON.parse_string(FileAccess.get_file_as_string(path))
        var campaign = saved.state.campaign.campaign
        checks["map_enters_act_one"] = not campaign.inHub and campaign.currentAct == 1
    await key(KEY_P)
    var paused_tick = await saved_tick()
    await key(KEY_H)
    checks["echoes_hides_manual_pause_card"] = visible_label("ECHOES: BORROWED MEMORY") and button_containing("Resume playing") == null
    await key(KEY_ESCAPE)
    checks["echoes_escape_closes_panel"] = not visible_label("ECHOES: BORROWED MEMORY")
    checks["echoes_preserves_manual_pause"] = button_containing("Resume playing") != null and await saved_tick() == paused_tick
    await click(button_containing("Resume playing"))
    await key(KEY_H)
    for node in nodes(self):
        if node.get_script() != null and node.get_script().resource_path.ends_with("/Sandbox.cs"):
            node.notification(Node.NOTIFICATION_APPLICATION_FOCUS_OUT)
            break
    await key(KEY_ESCAPE)
    checks["echoes_escape_closes_after_focus_loss"] = not visible_label("ECHOES: BORROWED MEMORY")
    paused_tick = await saved_tick()
    for i in range(12): await get_tree().process_frame
    checks["echoes_preserves_focus_pause"] = button_containing("Resume playing") != null and await saved_tick() == paused_tick
    await click(button_containing("Resume playing"))
    for i in range(12): await get_tree().process_frame
    checks["echoes_requires_explicit_resume"] = await saved_tick() > paused_tick
    var passed = checks.size() == 29
    for value in checks.values(): passed = passed and value
    var report = {"kind": "InteractionClientSmokePassed" if passed else "InteractionClientSmokeFailed", "passed": passed, "checks": checks}
    var file = FileAccess.open(output.path_join("interaction-review.json"), FileAccess.WRITE)
    file.store_string(JSON.stringify(report, "  "))
    print(JSON.stringify(report))
    get_tree().quit(0 if passed else 1)
