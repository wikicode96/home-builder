@tool
class_name FloorBuilder
extends RefCounted

static var slab_thickness: float = 0.1

# Untyped: typing it as the plugin class would create a cyclic class_name
# reference between the plugin script and every builder.
var _plugin

var _ghost: CSGBox3D
var _drag_start = null  # Vector3 while dragging, null otherwise


func _init(plugin) -> void:
	_plugin = plugin


# ── Preview ──────────────────────────────────────────────────────────────────

func create_ghost(scene: Node3D, floor_base_y: float) -> void:
	var ht := slab_thickness
	_ghost = PreviewHelper.create_marker(
		scene,
		"__HB_GhostFloor__",
		Vector3(0.5, ht, 0.5),
		Color(0.2, 0.9, 0.3, 0.4),
		Vector3(0.0, floor_base_y - ht * 0.5, 0.0)
	)


func clear_preview() -> void:
	PreviewHelper.free_marker(_ghost)
	_ghost = null
	_drag_start = null


# ── Input ────────────────────────────────────────────────────────────────────

func handle_input(camera: Camera3D, event: InputEvent, floor_base_y: float) -> int:
	if event is InputEventMouseMotion:
		var pos = RaycastHelper.to_floor_plane(camera, event.position, floor_base_y)
		if pos == null:
			return EditorPlugin.AFTER_GUI_INPUT_PASS

		var cell := SnapHelper.to_tile_center(pos, floor_base_y)

		if _drag_start != null:
			_update_ghost_rect(_drag_start, cell, floor_base_y)
		elif _ghost != null and is_instance_valid(_ghost):
			_ghost.size = Vector3(0.5, slab_thickness, 0.5)
			_ghost.position = cell

		return EditorPlugin.AFTER_GUI_INPUT_PASS

	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		var pos = RaycastHelper.to_floor_plane(camera, event.position, floor_base_y)
		if pos == null:
			return EditorPlugin.AFTER_GUI_INPUT_PASS

		if event.pressed:
			_drag_start = SnapHelper.to_tile_center(pos, floor_base_y)
			return EditorPlugin.AFTER_GUI_INPUT_STOP
		else:
			if _drag_start != null:
				var end_cell := SnapHelper.to_tile_center(pos, floor_base_y)
				_fill_floor_rect(_drag_start, end_cell, floor_base_y, _plugin.active_floor)
				_drag_start = null

				if _ghost != null and is_instance_valid(_ghost):
					_ghost.size = Vector3(0.5, slab_thickness, 0.5)
			return EditorPlugin.AFTER_GUI_INPUT_STOP

	return EditorPlugin.AFTER_GUI_INPUT_PASS


# ── Ghost rect resize ────────────────────────────────────────────────────────

func _update_ghost_rect(a: Vector3, b: Vector3, floor_base_y: float) -> void:
	if _ghost == null or not is_instance_valid(_ghost):
		return

	var bounds := SnapHelper.grid_bounds(a, b)
	var cols := bounds.size.x * 0.5
	var rows := bounds.size.y * 0.5

	var ht := slab_thickness
	_ghost.size = Vector3(cols, ht, rows)
	_ghost.position = Vector3(
		bounds.position.x * 0.5 + cols * 0.5,
		floor_base_y - ht * 0.5,
		bounds.position.y * 0.5 + rows * 0.5
	)


# ── Placement — single MeshInstance3D for the whole rectangle ────────────────

func _fill_floor_rect(a: Vector3, b: Vector3, floor_base_y: float, active_floor: int) -> void:
	var bounds := SnapHelper.grid_bounds(a, b)
	var min_x := bounds.position.x * 0.5
	var min_z := bounds.position.y * 0.5
	var cols := bounds.size.x * 0.5
	var rows := bounds.size.y * 0.5

	var floor_parent: Node3D = _plugin.get_or_create_floor_node(active_floor)
	if floor_parent == null:
		return

	var ht := slab_thickness
	var z_fighting := 0.001

	# HBFloorSlab is self-contained: it owns its mesh and collision as internal
	# children. The designer only ever sees a single "HBFloorSlab_001" node.
	var body := HBFloorSlab.new()
	body.name = "HBFloorSlab_001"
	body.position = Vector3(
		min_x + cols * 0.5,
		floor_base_y - ht * 0.5 + z_fighting,
		min_z + rows * 0.5
	)
	body.cols = cols
	body.rows = rows
	body.thickness = ht

	var dock = _plugin.dock
	body.top_material = dock.tile_top_material if dock != null else null
	body.bottom_material = dock.tile_bottom_material if dock != null else null
	body.sides_material = dock.tile_sides_material if dock != null else null

	# force_readable_name = true so name collisions increment the trailing
	# number (HBFloorSlab_002, …) instead of "@StaticBody3D@NN".
	floor_parent.add_child(body, true)
	body.owner = floor_parent.owner

	var undo: EditorUndoRedoManager = _plugin.get_undo_redo()
	undo.create_action("Fill Floor Rect")
	undo.add_do_method(floor_parent, &"add_child", body)
	undo.add_undo_method(floor_parent, &"remove_child", body)
	undo.commit_action(false)
