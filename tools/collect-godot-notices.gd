extends SceneTree

# The pinned engine supplies its own complete license texts and copyright inventory.
func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1:
		push_error("Supply one notice output directory.")
		quit(2)
		return
	var destination: String = args[0]
	if DirAccess.make_dir_recursive_absolute(destination) != OK:
		push_error("Cannot create notice output directory.")
		quit(1)
		return
	var notice := {
		"version": Engine.get_version_info(),
		"engine_license": Engine.get_license_text(),
		"third_party_licenses": Engine.get_license_info(),
		"copyright": Engine.get_copyright_info()
	}
	var file := FileAccess.open(destination.path_join("Godot-notices.json"), FileAccess.WRITE)
	if file == null:
		push_error("Cannot write engine notices.")
		quit(1)
		return
	file.store_string(JSON.stringify(notice, "  ", true) + "\n")
	file.close()
	print("GodotNoticesCollected")
	quit(0)
