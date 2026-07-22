@tool
class_name HBFloorSlabGizmoPlugin
extends EditorNode3DGizmoPlugin

## Drag handles for HBFloorSlab's footprint (cols/rows), one-sided like
## HBWall's: dragging a side keeps the opposite edge fixed in world space.
## Snaps to whole-metre tiles by default (matching FloorMeshBuilder's 1m UV
## tiling); hold Ctrl while dragging to size it continuously instead, same
## as HBWall/HBRoof already do by default.
##
## No handle for thickness: FloorMeshBuilder always draws the visual slab at
## a fixed 0.1m regardless of that property (it only sizes the physics box),
## so a draggable handle for it would move with no visible feedback. It stays
## an Inspector-only field.

const _AXES := [Vector3.RIGHT, Vector3.RIGHT, Vector3.BACK, Vector3.BACK]
const _SIGNS := [1.0, -1.0, 1.0, -1.0]
const _ANCHOR_FRACS := [-0.5, 0.5, -0.5, 0.5]
const _PROPS := ["cols", "cols", "rows", "rows"]
const _NAMES := ["Columnas", "Columnas", "Filas", "Filas"]
const _GRID_STEP := 1.0
const _MIN_SIZE := 0.1
const _VISUAL_HALF_Y := 0.05  # FloorMeshBuilder's fixed visual half-height

var _undo_redo: EditorUndoRedoManager

# Drag state, captured on the first _set_handle call of each drag and
# cleared again in _commit_handle (see HBWallGizmoPlugin for the same
# pattern, explained in more detail there).
var _drag_handle_id := -1
var _drag_anchor_global := Vector3.ZERO
var _drag_dir_global := Vector3.ZERO
var _drag_start_position := Vector3.ZERO


func _init(undo_redo: EditorUndoRedoManager) -> void:
	_undo_redo = undo_redo
	create_handle_material("handles")
	create_material("lines", Color(0.3, 0.75, 1.0), false, true, false)


func _get_gizmo_name() -> String:
	return "HBFloorSlab"


func _has_gizmo(for_node_3d: Node3D) -> bool:
	return for_node_3d is HBFloorSlab


func _redraw(gizmo: EditorNode3DGizmo) -> void:
	gizmo.clear()
	var slab := gizmo.get_node_3d() as HBFloorSlab
	if slab == null:
		return

	var hx := slab.cols * 0.5
	var hz := slab.rows * 0.5

	var lines := PackedVector3Array()
	_add_box_lines(lines, hx, _VISUAL_HALF_Y, hz)
	gizmo.add_lines(lines, get_material("lines", gizmo), false)

	var handles := PackedVector3Array([
		Vector3(hx, 0.0, 0.0), Vector3(-hx, 0.0, 0.0),
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
	var slab := gizmo.get_node_3d() as HBFloorSlab
	return slab.get(_PROPS[handle_id])


func _set_handle(gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool,
		camera: Camera3D, screen_pos: Vector2) -> void:
	var slab := gizmo.get_node_3d() as HBFloorSlab
	if slab == null:
		return

	if handle_id != _drag_handle_id:
		_begin_drag(slab, handle_id)

	var ray_from := camera.project_ray_origin(screen_pos)
	var ray_dir := camera.project_ray_normal(screen_pos)
	var raw := HBGizmoResizeMath.drag_size(
		_drag_anchor_global, _drag_dir_global, ray_from, ray_dir, _MIN_SIZE)

	var new_size := raw
	if not Input.is_key_pressed(KEY_CTRL):
		new_size = maxf(HBGizmoResizeMath.snap(raw, _GRID_STEP), _GRID_STEP)

	slab.global_position = HBGizmoResizeMath.node_position(
		_drag_anchor_global, _drag_dir_global,
		_SIGNS[handle_id], _ANCHOR_FRACS[handle_id], new_size)
	slab.set(_PROPS[handle_id], new_size)


func _begin_drag(slab: HBFloorSlab, handle_id: int) -> void:
	_drag_handle_id = handle_id
	_drag_start_position = slab.position

	var axis: Vector3 = _AXES[handle_id]
	var sign: float = _SIGNS[handle_id]
	var current_size: float = slab.get(_PROPS[handle_id])
	var start_transform := slab.global_transform

	_drag_anchor_global = HBGizmoResizeMath.anchor_global(
		start_transform, axis, _ANCHOR_FRACS[handle_id], current_size)
	_drag_dir_global = HBGizmoResizeMath.dir_global(start_transform.basis, axis, sign)


func _commit_handle(gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool,
		restore: Variant, cancel: bool) -> void:
	var slab := gizmo.get_node_3d() as HBFloorSlab
	if slab == null:
		_drag_handle_id = -1
		return

	var prop: String = _PROPS[handle_id]
	if cancel:
		slab.set(prop, restore)
		slab.position = _drag_start_position
		_drag_handle_id = -1
		return

	var new_value = slab.get(prop)
	var new_position := slab.position
	if is_equal_approx(new_value, restore) and new_position.is_equal_approx(_drag_start_position):
		_drag_handle_id = -1
		return

	_undo_redo.create_action("Cambiar %s de %s" % [_NAMES[handle_id], slab.name])
	_undo_redo.add_do_property(slab, prop, new_value)
	_undo_redo.add_do_property(slab, "position", new_position)
	_undo_redo.add_undo_property(slab, "position", _drag_start_position)
	_undo_redo.add_undo_property(slab, prop, restore)
	_undo_redo.commit_action(false)
	_drag_handle_id = -1
