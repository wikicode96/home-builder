@tool
class_name RoofBuilder
extends RefCounted

# Untyped: typing it as the plugin class would create a cyclic class_name
# reference between the plugin script and every builder.
var _plugin

var _ghost: CSGBox3D
var _drag_start = null  # Vector3 while dragging, null otherwise


func _init(plugin) -> void:
	_plugin = plugin


## Roof base sits on top of the active floor's walls.
static func _roof_base_y(floor_base_y: float) -> float:
	return floor_base_y + WallBuilder.height


func create_ghost(scene: Node3D, floor_base_y: float) -> void:
	var base_y := _roof_base_y(floor_base_y)
	_ghost = PreviewHelper.create_marker(
		scene,
		"__HB_GhostRoof__",
		Vector3(0.5, 0.1, 0.5),
		Color(0.2, 0.5, 0.9, 0.4),
		Vector3(0.0, base_y + 0.05, 0.0)
	)


func clear_preview() -> void:
	PreviewHelper.free_marker(_ghost)
	_ghost = null
	_drag_start = null


func handle_input(camera: Camera3D, event: InputEvent, floor_base_y: float) -> int:
	var base_y := _roof_base_y(floor_base_y)

	if event is InputEventMouseMotion:
		var pos = RaycastHelper.to_floor_plane(camera, event.position, base_y)
		if pos == null:
			return EditorPlugin.AFTER_GUI_INPUT_PASS

		var cell := SnapHelper.to_half_tile_center(pos, base_y)
		cell.y = base_y + 0.05

		if _drag_start != null:
			_update_ghost_rect(_drag_start, cell, base_y)
		elif _ghost != null and is_instance_valid(_ghost):
			_ghost.size = Vector3(0.5, 0.1, 0.5)
			_ghost.position = cell
		return EditorPlugin.AFTER_GUI_INPUT_PASS

	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		var pos = RaycastHelper.to_floor_plane(camera, event.position, base_y)
		if pos == null:
			return EditorPlugin.AFTER_GUI_INPUT_PASS

		if event.pressed:
			_drag_start = SnapHelper.to_half_tile_center(pos, base_y)
			return EditorPlugin.AFTER_GUI_INPUT_STOP

		if _drag_start != null:
			var end_cell := SnapHelper.to_half_tile_center(pos, base_y)
			_place_roof(_drag_start, end_cell, base_y, _plugin.active_floor)
			_drag_start = null

			if _ghost != null and is_instance_valid(_ghost):
				_ghost.size = Vector3(0.5, 0.1, 0.5)
		return EditorPlugin.AFTER_GUI_INPUT_STOP

	return EditorPlugin.AFTER_GUI_INPUT_PASS


func _update_ghost_rect(a: Vector3, b: Vector3, base_y: float) -> void:
	if _ghost == null or not is_instance_valid(_ghost):
		return

	var bounds := SnapHelper.half_grid_bounds(a, b)
	var half_t := WallBuilder.thickness * 0.5
	var w := bounds.size.x * 0.5 + WallBuilder.thickness
	var d := bounds.size.y * 0.5 + WallBuilder.thickness

	_ghost.size = Vector3(w, 0.1, d)
	_ghost.position = Vector3(
		bounds.position.x * 0.5 - half_t + w * 0.5,
		base_y + 0.05,
		bounds.position.y * 0.5 - half_t + d * 0.5
	)


func _place_roof(a: Vector3, b: Vector3, base_y: float, active_floor: int) -> void:
	var bounds := SnapHelper.half_grid_bounds(a, b)

	# Extend the roof footprint by half the wall thickness on every side so
	# that the roof covers the outer face of facade walls, not just their
	# centre line. Walls are centred on grid lines, so the exterior half of
	# any boundary wall would otherwise be left uncovered.
	var half_t := WallBuilder.thickness * 0.5
	var w := bounds.size.x * 0.5 + WallBuilder.thickness
	var d := bounds.size.y * 0.5 + WallBuilder.thickness

	var dock = _plugin.dock
	var type: RoofMeshBuilder.RoofType = (
		dock.selected_roof_type if dock != null else RoofMeshBuilder.RoofType.FLAT
	)
	var dir: RoofMeshBuilder.RoofDirection = (
		dock.selected_roof_direction if dock != null else RoofMeshBuilder.RoofDirection.NORTH
	)
	var pitch: float = dock.roof_pitch if dock != null else 1.5

	var roof_parent: Node3D = _plugin.get_or_create_parent_node("Roof_%d" % active_floor)
	if roof_parent == null:
		return

	var undo: EditorUndoRedoManager = _plugin.get_undo_redo()
	undo.create_action("Place Roof")

	# HBRoof is self-contained: it owns its mesh and collision as internal
	# children. The designer only ever sees a single "HBRoof_001" node.
	var body := HBRoof.new()
	body.name = "HBRoof_001"
	body.position = Vector3(
		bounds.position.x * 0.5 - half_t,
		base_y,
		bounds.position.y * 0.5 - half_t
	)
	body.roof_type = type
	body.direction = dir
	body.width = w
	body.depth = d
	body.pitch = pitch

	body.top_material = dock.roof_top_material if dock != null else null
	body.bottom_material = dock.roof_bottom_material if dock != null else null
	body.sides_material = dock.roof_sides_material if dock != null else null

	# force_readable_name = true so name collisions increment the trailing
	# number (HBRoof_002, …) instead of "@StaticBody3D@NN".
	roof_parent.add_child(body, true)
	body.owner = roof_parent.owner

	undo.add_do_method(roof_parent, &"add_child", body)
	undo.add_undo_method(roof_parent, &"remove_child", body)
	undo.commit_action(false)
