@tool
class_name HBWallGizmoPlugin
extends EditorNode3DGizmoPlugin

## Lets an HBWall's length/height/thickness be dragged directly in the 3D
## viewport, the same way a CollisionShape3D's box handles work — except
## each handle is one-sided: dragging it keeps the OPPOSITE face fixed in
## place and grows/shrinks only the side being dragged, shifting the node's
## position to compensate. This matches how you'd actually stretch a wall
## by one end without the other end also moving.
##
## The wall mesh itself always stays centred on the node's local origin (see
## WallMeshBuilder), so the one-sidedness is achieved purely by moving the
## node — the mesh/collision rebuild doesn't need to know about it.
##
## Snaps to the 0.5m world grid by default (see HBGizmoResizeMath — a wall
## placed diagonally, e.g. between two grid corners per WallBuilder, still
## lands its dragged end on an actual grid intersection, not just a rounded
## distance along the diagonal); hold Ctrl while dragging to size it
## continuously instead.

# Handle ids 0..5, two per axis (one on each side).
const _AXES := [Vector3.RIGHT, Vector3.RIGHT, Vector3.UP, Vector3.UP, Vector3.BACK, Vector3.BACK]
const _SIGNS := [1.0, -1.0, 1.0, -1.0, 1.0, -1.0]
const _ANCHOR_FRACS := [-0.5, 0.5, -0.5, 0.5, -0.5, 0.5]
const _PROPS := ["length", "length", "height", "height", "thickness", "thickness"]
const _NAMES := ["Longitud", "Longitud", "Altura", "Altura", "Grosor", "Grosor"]
const _MIN_LENGTH := [0.05, 0.05, 0.05, 0.05, 0.02, 0.02]
const _GRID_STEP := 0.5

var _undo_redo: EditorUndoRedoManager

# Drag state, captured the first _set_handle call of each drag and cleared
# again in _commit_handle. Kept in world space so the anchor face truly
# doesn't move on screen while the basis (rotation) can't change mid-drag.
var _drag_handle_id := -1
var _drag_anchor_global := Vector3.ZERO
var _drag_dir_global := Vector3.ZERO
var _drag_start_position := Vector3.ZERO


func _init(undo_redo: EditorUndoRedoManager) -> void:
	_undo_redo = undo_redo
	create_handle_material("handles")
	create_material("lines", Color(1.0, 0.85, 0.2), false, true, false)


func _get_gizmo_name() -> String:
	return "HBWall"


func _has_gizmo(for_node_3d: Node3D) -> bool:
	return for_node_3d is HBWall


func _redraw(gizmo: EditorNode3DGizmo) -> void:
	gizmo.clear()
	var wall := gizmo.get_node_3d() as HBWall
	if wall == null:
		return

	var hx := wall.length * 0.5
	var hy := wall.height * 0.5
	var hz := wall.thickness * 0.5

	var lines := PackedVector3Array()
	_add_box_lines(lines, hx, hy, hz)
	gizmo.add_lines(lines, get_material("lines", gizmo), false)

	var handles := PackedVector3Array([
		Vector3(hx, 0.0, 0.0), Vector3(-hx, 0.0, 0.0),
		Vector3(0.0, hy, 0.0), Vector3(0.0, -hy, 0.0),
		Vector3(0.0, 0.0, hz), Vector3(0.0, 0.0, -hz),
	])
	gizmo.add_handles(handles, get_material("handles", gizmo), [])


static func _add_box_lines(lines: PackedVector3Array, hx: float, hy: float, hz: float) -> void:
	var corners := [
		Vector3(-hx, -hy, -hz), Vector3(hx, -hy, -hz),
		Vector3(hx, -hy, hz), Vector3(-hx, -hy, hz),
		Vector3(-hx, hy, -hz), Vector3(hx, hy, -hz),
		Vector3(hx, hy, hz), Vector3(-hx, hy, hz),
	]
	var edges := [
		[0, 1], [1, 2], [2, 3], [3, 0],
		[4, 5], [5, 6], [6, 7], [7, 4],
		[0, 4], [1, 5], [2, 6], [3, 7],
	]
	for e in edges:
		lines.append(corners[e[0]])
		lines.append(corners[e[1]])


# ── Handle interaction ───────────────────────────────────────────────────────

func _get_handle_name(_gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool) -> String:
	return _NAMES[handle_id]


func _get_handle_value(gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool) -> Variant:
	var wall := gizmo.get_node_3d() as HBWall
	return wall.get(_PROPS[handle_id])


func _set_handle(gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool,
		camera: Camera3D, screen_pos: Vector2) -> void:
	var wall := gizmo.get_node_3d() as HBWall
	if wall == null:
		return

	if handle_id != _drag_handle_id:
		_begin_drag(wall, handle_id)

	var ray_from := camera.project_ray_origin(screen_pos)
	var ray_dir := camera.project_ray_normal(screen_pos)

	var new_length: float
	if Input.is_key_pressed(KEY_CTRL):
		new_length = HBGizmoResizeMath.drag_size(
			_drag_anchor_global, _drag_dir_global, ray_from, ray_dir, _MIN_LENGTH[handle_id])
	elif _AXES[handle_id] == Vector3.UP:
		# Vertical handle: snap the height itself, not the absolute world Y
		# (see HBGizmoResizeMath.drag_size_snapped_relative).
		new_length = HBGizmoResizeMath.drag_size_snapped_relative(
			_drag_anchor_global, _drag_dir_global, ray_from, ray_dir,
			_GRID_STEP, _MIN_LENGTH[handle_id])
	else:
		new_length = HBGizmoResizeMath.drag_size_snapped(
			_drag_anchor_global, _drag_dir_global, ray_from, ray_dir,
			_GRID_STEP, _MIN_LENGTH[handle_id])

	wall.global_position = HBGizmoResizeMath.node_position(
		_drag_anchor_global, _drag_dir_global,
		_SIGNS[handle_id], _ANCHOR_FRACS[handle_id], new_length)
	wall.set(_PROPS[handle_id], new_length)


## Captures the opposite face's world position (the anchor) and the drag
## axis's world direction, both from the wall's state right before the drag
## touches it — these stay constant for the whole drag even as the node's
## position and size change frame to frame.
func _begin_drag(wall: HBWall, handle_id: int) -> void:
	_drag_handle_id = handle_id
	_drag_start_position = wall.position

	var axis: Vector3 = _AXES[handle_id]
	var sign: float = _SIGNS[handle_id]
	var current_size: float = wall.get(_PROPS[handle_id])
	var start_transform := wall.global_transform

	_drag_anchor_global = HBGizmoResizeMath.anchor_global(
		start_transform, axis, _ANCHOR_FRACS[handle_id], current_size)
	_drag_dir_global = HBGizmoResizeMath.dir_global(start_transform.basis, axis, sign)


func _commit_handle(gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool,
		restore: Variant, cancel: bool) -> void:
	var wall := gizmo.get_node_3d() as HBWall
	if wall == null:
		_drag_handle_id = -1
		return

	var prop: String = _PROPS[handle_id]
	if cancel:
		wall.set(prop, restore)
		wall.position = _drag_start_position
		_drag_handle_id = -1
		return

	var new_value = wall.get(prop)
	var new_position := wall.position
	if is_equal_approx(new_value, restore) and new_position.is_equal_approx(_drag_start_position):
		_drag_handle_id = -1
		return

	_undo_redo.create_action("Cambiar %s de %s" % [_NAMES[handle_id], wall.name])
	_undo_redo.add_do_property(wall, prop, new_value)
	_undo_redo.add_do_property(wall, "position", new_position)
	_undo_redo.add_undo_property(wall, "position", _drag_start_position)
	_undo_redo.add_undo_property(wall, prop, restore)
	_undo_redo.commit_action(false)
	_drag_handle_id = -1
