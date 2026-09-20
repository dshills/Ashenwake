extends Node

var output = ""
var checks = {}

func _ready():
    Engine.max_fps = 60
    for arg in OS.get_cmdline_user_args():
        if arg.begins_with("--output="): output = arg.trim_prefix("--output=")
    if "--service-interaction-smoke" not in OS.get_cmdline_user_args() or output.is_empty() or FileAccess.file_exists(output.path_join("current-save.txt")):
        push_error("Service interaction smoke requires --service-interaction-smoke --output=<fresh-isolated-directory>.")
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

func journey_is_open():
    for node in nodes(self):
        if node.name == "CampaignPanel" and node is Control: return node.is_visible_in_tree()
    return false

func settle():
    for i in range(3): await get_tree().process_frame

func key(code):
    for pressed in [true, false]:
        var event = InputEventKey.new()
        event.physical_keycode = code
        event.pressed = pressed
        get_viewport().push_input(event, true)
        await settle()

func click(button):
    if button == null or not button.is_visible_in_tree(): return
    for pressed in [true, false]:
        var event = InputEventMouseButton.new()
        event.button_index = MOUSE_BUTTON_LEFT
        event.position = button.get_global_rect().get_center()
        event.pressed = pressed
        get_viewport().push_input(event, true)
        await settle()

func selected_service(character, expected):
    for node in nodes(character):
        if node is Button and node.is_visible_in_tree() and node.name == "CraftService" + expected:
            if node.text.begins_with("◆ "): return true
    return false

func saved_progression():
    var filename = FileAccess.get_file_as_string(output.path_join("current-save.txt")).strip_edges()
    var path = output.path_join(filename)
    if not FileAccess.file_exists(path): return null
    return JSON.parse_string(FileAccess.get_file_as_string(path)).state.campaign.production.progression

func run():
    add_child(load("res://Endgame.tscn").instantiate())
    await settle()
    await click(button_named("FrontNew"))
    await click(button_named("FrontDisciplineVanguard"))
    await click(button_named("FrontBegin"))
    checks["discipline_selection_accepts_mouse"] = button_named("FrontBegin") == null and FileAccess.file_exists(output.path_join("current-save.txt"))
    checks["journey_starts_closed_after_discipline_selection"] = not journey_is_open()
    if journey_is_open(): await key(KEY_J)
    var character
    for node in nodes(self):
        if node.get_script() != null and node.get_script().resource_path.ends_with("/ProductionHud.cs"):
            character = node
            break
    checks["character_hud_available"] = character != null
    if character == null:
        finish()
        return
    await key(KEY_F5)
    var before = saved_progression()
    checks["baseline_saved"] = before != null
    checks["rejected_event_ignored"] = not character.call("PresentInteraction", "CommandRejected:ServiceOpened:npc.torren")
    checks["unknown_service_ignored"] = not character.call("PresentInteraction", "ServiceOpened:unknown")
    checks["mara_keeps_anatomy_route"] = not character.call("PresentInteraction", "ServiceOpened:service.mara")
    checks["ignored_events_leave_character_closed"] = button_containing("Close character") == null
    # These successful-service events are presentation fixtures, not a simulated rescue.
    # Locked controls must remain locked; opening a panel must not craft or equip anything.
    for route in [
        ["service.torren", "Gear", ""],
        ["npc.torren", "Craft", "Tempering"],
        ["npc.cael", "Craft", "Purification"],
        ["npc.oris", "Craft", "Rebinding"],
        ["npc.kesh", "Craft", "Extraction"],
        ["hub.workshops", "Craft", "Engraving"]
    ]:
        var handled = character.call("PresentInteraction", "ServiceOpened:" + route[0])
        await settle()
        var focused = get_viewport().gui_get_focus_owner()
        checks[route[0] + "_opens_expected_tab"] = handled and button_containing("Close character") != null and focused is Button and focused.text == route[1]
        checks[route[0] + "_selects_expected_service"] = route[2].is_empty() or selected_service(character, route[2])
        await click(button_containing("Close character"))
    await key(KEY_F5)
    checks["opening_services_preserves_progression"] = before != null and saved_progression() == before
    finish()

func finish():
    var passed = checks.size() == 21
    for value in checks.values(): passed = passed and value
    var report = {"kind": "ServiceInteractionClientSmokePassed" if passed else "ServiceInteractionClientSmokeFailed", "passed": passed, "checks": checks, "scope": "Successful ServiceOpened presentation fixtures exercise actual Godot HUD routing and focus. No rescue route, crafting action, or specialist unlock is simulated; permanent progression must remain unchanged."}
    var file = FileAccess.open(output.path_join("service-interaction-review.json"), FileAccess.WRITE)
    file.store_string(JSON.stringify(report, "  "))
    print(JSON.stringify(report))
    get_tree().quit(0 if passed else 1)
