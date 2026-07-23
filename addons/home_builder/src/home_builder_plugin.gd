@tool
extends EditorPlugin

enum BuildMode { NONE, FLOOR, WALLS, ROOF, DOORS, WINDOWS, STAIRS, FENCES }

## Read-only accessors used by the builders.
var active_floor: int:
	get: return _active_floor
var floor_base_y: float:
	get: return _active_floor * WallBuilder.height
var dock: HomeBuilderDock:
	get: return _dock

var _dock: HomeBuilderDock
var _active_mode := BuildMode.NONE
var _active_floor := 0

var _floor_builder: FloorBuilder
var _wall_builder: WallBuilder
var _opening_builder: OpeningBuilder
var _stairs_builder: StairsBuilder
var _roof_builder: RoofBuilder
var _fence_builder: FenceBuilder
var _bake_builder: BakeBuilder
var _wall_gizmo_plugin: HBWallGizmoPlugin
var _floor_slab_gizmo_plugin: HBFloorSlabGizmoPlugin
var _roof_gizmo_plugin: HBRoofGizmoPlugin
var _stairs_gizmo_plugin: HBStairsGizmoPlugin


func _enter_tree() -> void:
	_floor_builder = FloorBuilder.new(self)
	_wall_builder = WallBuilder.new(self)
	_opening_builder = OpeningBuilder.new(self)
	_stairs_builder = StairsBuilder.new(self)
	_roof_builder = RoofBuilder.new(self)
	_fence_builder = FenceBuilder.new(self)
	_bake_builder = BakeBuilder.new(self)

	_wall_gizmo_plugin = HBWallGizmoPlugin.new(get_undo_redo())
	add_node_3d_gizmo_plugin(_wall_gizmo_plugin)
	_floor_slab_gizmo_plugin = HBFloorSlabGizmoPlugin.new(get_undo_redo())
	add_node_3d_gizmo_plugin(_floor_slab_gizmo_plugin)
	_roof_gizmo_plugin = HBRoofGizmoPlugin.new(get_undo_redo())
	add_node_3d_gizmo_plugin(_roof_gizmo_plugin)
	_stairs_gizmo_plugin = HBStairsGizmoPlugin.new(get_undo_redo())
	add_node_3d_gizmo_plugin(_stairs_gizmo_plugin)

	var dock_scene: PackedScene = load("res://addons/home_builder/src/HomeBuilderDock.tscn")
	_dock = dock_scene.instantiate()
	add_control_to_bottom_panel(_dock, "Home Builder")

	_dock.mode_changed.connect(_on_mode_changed)
	_dock.floor_changed.connect(_on_floor_changed)
	_dock.bake_requested.connect(_on_bake_requested)
	_dock.opening_config_changed.connect(_on_opening_config_changed)
	_dock.building_config_changed.connect(_on_building_config_changed)

	scene_changed.connect(_on_scene_changed)

	# If a scene is already open when the plugin activates, scene_changed
	# will NOT fire. We must manually select the scene root so that Godot
	# routes _forward_3d_gui_input events to this plugin.
	_select_scene_root.call_deferred()


func _exit_tree() -> void:
	# Godot may have already destroyed the connection when _exit_tree runs
	# (e.g. when closing the editor); disconnecting blindly logs
	# "nonexistent connection".
	if scene_changed.is_connected(_on_scene_changed):
		scene_changed.disconnect(_on_scene_changed)

	_clear_all_previews()
	if _dock != null:
		# The dock's own signal connections die with the node.
		remove_control_from_bottom_panel(_dock)
		_dock.queue_free()
		_dock = null
	if _wall_gizmo_plugin != null:
		remove_node_3d_gizmo_plugin(_wall_gizmo_plugin)
		_wall_gizmo_plugin = null
	if _floor_slab_gizmo_plugin != null:
		remove_node_3d_gizmo_plugin(_floor_slab_gizmo_plugin)
		_floor_slab_gizmo_plugin = null
	if _roof_gizmo_plugin != null:
		remove_node_3d_gizmo_plugin(_roof_gizmo_plugin)
		_roof_gizmo_plugin = null
	if _stairs_gizmo_plugin != null:
		remove_node_3d_gizmo_plugin(_stairs_gizmo_plugin)
		_stairs_gizmo_plugin = null
	_active_mode = BuildMode.NONE
	_active_floor = 0


func _on_mode_changed(mode: String) -> void:
	_clear_all_previews()
	match mode:
		"floor":
			_active_mode = BuildMode.FLOOR
		"walls":
			_active_mode = BuildMode.WALLS
		"roof":
			_active_mode = BuildMode.ROOF
		"doors":
			_active_mode = BuildMode.DOORS
		"windows":
			_active_mode = BuildMode.WINDOWS
		"stairs":
			_active_mode = BuildMode.STAIRS
		"fences":
			_active_mode = BuildMode.FENCES
		_:  # "none", "bake" and anything unknown
			_active_mode = BuildMode.NONE
	_create_active_previews.call_deferred()


func _on_floor_changed(floor_number: int) -> void:
	_active_floor = floor_number
	_update_node_visibility()
	_clear_all_previews()
	_create_active_previews.call_deferred()


func _on_opening_config_changed() -> void:
	if _active_mode == BuildMode.DOORS or _active_mode == BuildMode.WINDOWS:
		_clear_all_previews()
		_create_active_previews.call_deferred()


func _on_scene_changed(_scene_root: Node) -> void:
	_clear_all_previews()
	_create_active_previews.call_deferred()
	# Re-select the new root so input routing is re-established.
	_select_scene_root.call_deferred()


## Selects the scene root node in the editor so that _forward_3d_gui_input
## is routed to this plugin. Without a selection that _handles() accepts,
## Godot 4 does not forward 3D viewport input events to editor plugins.
func _select_scene_root() -> void:
	var root := EditorInterface.get_edited_scene_root()
	if root == null:
		return
	EditorInterface.get_selection().clear()
	EditorInterface.get_selection().add_node(root)


func _handles(_object: Object) -> bool:
	return true


# ── Input ────────────────────────────────────────────────────────────────────

func _forward_3d_gui_input(viewport_camera: Camera3D, event: InputEvent) -> int:
	match _active_mode:
		BuildMode.FLOOR:
			return _floor_builder.handle_input(viewport_camera, event, floor_base_y)
		BuildMode.WALLS:
			return _wall_builder.handle_input(viewport_camera, event, floor_base_y)
		BuildMode.ROOF:
			return _roof_builder.handle_input(viewport_camera, event, floor_base_y)
		BuildMode.DOORS:
			return _opening_builder.handle_input(
				viewport_camera, event, true,
				get_or_create_parent_node("Walls_%d" % _active_floor))
		BuildMode.WINDOWS:
			return _opening_builder.handle_input(
				viewport_camera, event, false,
				get_or_create_parent_node("Walls_%d" % _active_floor))
		BuildMode.STAIRS:
			return _stairs_builder.handle_input(viewport_camera, event, floor_base_y)
		BuildMode.FENCES:
			return _fence_builder.handle_input(viewport_camera, event, floor_base_y)
	return AFTER_GUI_INPUT_PASS


# ── Previews ─────────────────────────────────────────────────────────────────

func _create_active_previews() -> void:
	var scene := EditorInterface.get_edited_scene_root() as Node3D
	if scene == null:
		return

	match _active_mode:
		BuildMode.FLOOR:
			_floor_builder.create_ghost(scene, floor_base_y)
		BuildMode.WALLS:
			_wall_builder.create_marker(scene, floor_base_y)
		BuildMode.ROOF:
			_roof_builder.create_ghost(scene, floor_base_y)
		BuildMode.DOORS:
			_opening_builder.create_marker(scene, true)
		BuildMode.WINDOWS:
			_opening_builder.create_marker(scene, false)
		BuildMode.STAIRS:
			_stairs_builder.create_ghost(scene, floor_base_y)
		BuildMode.FENCES:
			_fence_builder.create_marker(scene, floor_base_y)


func _clear_all_previews() -> void:
	if _floor_builder != null:
		_floor_builder.clear_preview()
	if _wall_builder != null:
		_wall_builder.clear_preview()
	if _opening_builder != null:
		_opening_builder.clear_preview()
	if _stairs_builder != null:
		_stairs_builder.clear_preview()
	if _roof_builder != null:
		_roof_builder.clear_preview()
	if _fence_builder != null:
		_fence_builder.clear_preview()


# ── Node visibility ──────────────────────────────────────────────────────────

func _update_node_visibility() -> void:
	var scene := EditorInterface.get_edited_scene_root() as Node3D
	if scene == null:
		return

	for child in scene.get_children():
		if not (child is Node3D):
			continue

		var node_index = _parse_node_index(String(child.name))
		if node_index == null:
			continue

		child.visible = node_index <= _active_floor


## Returns the floor index encoded in a builder-created node name
## ("Walls_1", "Floor_0", ...), or null for any other node.
static func _parse_node_index(node_name: String) -> Variant:
	for prefix in ["Floor_", "Walls_", "Stairs_", "Roof_", "Fences_"]:
		if node_name.begins_with(prefix):
			var suffix := node_name.substr(prefix.length())
			if suffix.is_valid_int():
				return suffix.to_int()
	return null


# ── Bake ─────────────────────────────────────────────────────────────────────

func _on_building_config_changed() -> void:
	if _dock == null:
		return

	WallBuilder.height = _dock.wall_height
	WallBuilder.thickness = _dock.wall_thickness
	StairsBuilder.stair_count = _dock.stair_count
	StairsBuilder.stair_width = _dock.stair_width
	StairsBuilder.stair_run = _dock.stair_run
	FloorBuilder.slab_thickness = _dock.floor_thickness
	FenceBuilder.module_length = _dock.fence_module_length

	if _active_mode != BuildMode.NONE:
		_clear_all_previews()
		_create_active_previews.call_deferred()


func _on_bake_requested() -> void:
	var scene := EditorInterface.get_edited_scene_root() as Node3D
	if scene == null:
		return

	_clear_all_previews()

	if _dock == null:
		push_error("[HomeBuilder] Dock no disponible al bakear")
		return

	_bake_builder.bake(
		scene,
		_dock.bake_output_path,
		_dock.bake_lod0_end,
		_dock.bake_lod1_begin,
		_dock.bake_fade_mode,
		_dock.bake_build_nav_mesh,
		_dock.bake_build_occlusion
	)

	_create_active_previews.call_deferred()


# ── Helpers used by builders ─────────────────────────────────────────────────

func get_or_create_parent_node(parent_name: String) -> Node3D:
	var scene := EditorInterface.get_edited_scene_root() as Node3D
	if scene == null:
		return null

	var parent := scene.get_node_or_null(parent_name) as Node3D
	if parent == null:
		parent = Node3D.new()
		parent.name = parent_name
		scene.add_child(parent)
		parent.owner = scene
	return parent
