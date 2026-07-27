@tool
class_name HBStairsGizmoPlugin
extends EditorNode3DGizmoPlugin

## Drag handles for HBStairs' width, height, and step count (its "depth").
##
## Local frame (see hb_stairs.gd / StairsMeshBuilder): X is centred
## (-width/2..+width/2, like HBWall); Y and Z are corner-anchored at the
## node's own origin (0..height, 0..count*run, like HBRoof's width/depth) —
## Y=0 is the ground the stairs sit on, Z=0 is the staircase's own start
## edge. So width gets two centred handles, height gets one (dragging the
## ground up doesn't make sense), and count gets two corner-style handles
## like HBRoof's footprint.
##
## count is an int (a fractional stair doesn't mean anything), so its handles
## always snap to whole steps — run's current value sets the step size —
## with no Ctrl-held free mode. Width/height snap to the usual 0.5m world
## grid by default; hold Ctrl to size them continuously, same as every other
## gizmo in this addon.

const _AXES := [Vector3.RIGHT, Vector3.RIGHT, Vector3.UP, Vector3.BACK, Vector3.BACK]
const _SIGNS := [1.0, -1.0, 1.0, 1.0, -1.0]
const _ANCHOR_FRACS := [-0.5, 0.5, 0.0, 0.0, 1.0]
const _PROPS := ["width", "width", "height", "count", "count"]
const _NAMES := ["Ancho", "Ancho", "Altura", "Peldaños", "Peldaños"]
const _MIN_SIZE := [0.2, 0.2, 0.1]  # only used for the width/height handles (ids 0-2)
const _GRID_STEP := 0.5

var _undo_redo: EditorUndoRedoManager

var _drag_handle_id := -1
var _drag_anchor_global := Vector3.ZERO
var _drag_dir_global := Vector3.ZERO
var _drag_start_position := Vector3.ZERO


func _init(undo_redo: EditorUndoRedoManager) -> void:
	_undo_redo = undo_redo
	create_handle_material("handles")
	create_material("lines", Color(0.6, 1.0, 0.4), false, true, false)


func _get_gizmo_name() -> String:
	return "HBStairs"


func _has_gizmo(for_node_3d: Node3D) -> bool:
	return for_node_3d is HBStairs


func _redraw(gizmo: EditorNode3DGizmo) -> void:
	gizmo.clear()
	var stairs := gizmo.get_node_3d() as HBStairs
	if stairs == null:
		return

	var half_w := stairs.width * 0.5
	var h := stairs.height
	var tr := stairs.count * stairs.run

	var lines := PackedVector3Array()
	_add_box_lines(lines, half_w, h, tr)
	gizmo.add_lines(lines, get_material("lines", gizmo), false)

	var handles := PackedVector3Array([
		Vector3(half_w, h * 0.5, tr * 0.5), Vector3(-half_w, h * 0.5, tr * 0.5),
		Vector3(0.0, h, tr * 0.5),
		Vector3(0.0, h * 0.5, tr), Vector3(0.0, h * 0.5, 0.0),
	])
	gizmo.add_handles(handles, get_material("handles", gizmo), [])


static func _add_box_lines(lines: PackedVector3Array, half_w: float, h: float, tr: float) -> void:
	var corners := [
		Vector3(-half_w, 0.0, 0.0), Vector3(half_w, 0.0, 0.0),
		Vector3(half_w, 0.0, tr), Vector3(-half_w, 0.0, tr),
		Vector3(-half_w, h, 0.0), Vector3(half_w, h, 0.0),
		Vector3(half_w, h, tr), Vector3(-half_w, h, tr),
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
	var stairs := gizmo.get_node_3d() as HBStairs
	return stairs.get(_PROPS[handle_id])


func _set_handle(gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool,
		camera: Camera3D, screen_pos: Vector2) -> void:
	var stairs := gizmo.get_node_3d() as HBStairs
	if stairs == null:
		return

	if handle_id != _drag_handle_id:
		_begin_drag(stairs, handle_id)

	var ray_from := camera.project_ray_origin(screen_pos)
	var ray_dir := camera.project_ray_normal(screen_pos)

	if _PROPS[handle_id] == "count":
		# No Ctrl-free mode here: count is an int, so it always snaps to
		# whole steps (multiples of the current run).
		var raw := HBGizmoResizeMath.drag_size(
			_drag_anchor_global, _drag_dir_global, ray_from, ray_dir, stairs.run * 0.5)
		var new_count := maxi(int(round(raw / stairs.run)), 1)
		var actual_size := new_count * stairs.run

		stairs.global_position = HBGizmoResizeMath.node_position(
			_drag_anchor_global, _drag_dir_global,
			_SIGNS[handle_id], _ANCHOR_FRACS[handle_id], actual_size)
		stairs.set("count", new_count)
	else:
		var new_size: float
		if Input.is_key_pressed(KEY_CTRL):
			new_size = HBGizmoResizeMath.drag_size(
				_drag_anchor_global, _drag_dir_global, ray_from, ray_dir, _MIN_SIZE[handle_id])
		elif _AXES[handle_id] == Vector3.UP:
			# Vertical handle (height): snap the height itself, not the
			# absolute world Y. HBStairs' base carries a small deliberate
			# z-fighting offset (see StairsBuilder), so its anchor isn't
			# exactly on the world grid — point-snapping there would turn a
			# clean 4m height into 3.999m (see
			# HBGizmoResizeMath.drag_size_snapped_relative).
			new_size = HBGizmoResizeMath.drag_size_snapped_relative(
				_drag_anchor_global, _drag_dir_global, ray_from, ray_dir,
				_GRID_STEP, _MIN_SIZE[handle_id])
		else:
			new_size = HBGizmoResizeMath.drag_size_snapped(
				_drag_anchor_global, _drag_dir_global, ray_from, ray_dir,
				_GRID_STEP, _MIN_SIZE[handle_id])

		stairs.global_position = HBGizmoResizeMath.node_position(
			_drag_anchor_global, _drag_dir_global,
			_SIGNS[handle_id], _ANCHOR_FRACS[handle_id], new_size)
		stairs.set(_PROPS[handle_id], new_size)


func _begin_drag(stairs: HBStairs, handle_id: int) -> void:
	_drag_handle_id = handle_id
	_drag_start_position = stairs.position

	var axis: Vector3 = _AXES[handle_id]
	var sign: float = _SIGNS[handle_id]
	# count isn't itself a length — anchor/direction math needs metres, so
	# use count*run (the actual current run distance) instead of the raw
	# step count.
	var current_size: float = (
		stairs.count * stairs.run if _PROPS[handle_id] == "count"
		else stairs.get(_PROPS[handle_id])
	)
	var start_transform := stairs.global_transform

	_drag_anchor_global = HBGizmoResizeMath.anchor_global(
		start_transform, axis, _ANCHOR_FRACS[handle_id], current_size)
	_drag_dir_global = HBGizmoResizeMath.dir_global(start_transform.basis, axis, sign)


func _commit_handle(gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool,
		restore: Variant, cancel: bool) -> void:
	var stairs := gizmo.get_node_3d() as HBStairs
	if stairs == null:
		_drag_handle_id = -1
		return

	var prop: String = _PROPS[handle_id]
	if cancel:
		stairs.set(prop, restore)
		stairs.position = _drag_start_position
		_drag_handle_id = -1
		return

	var new_value = stairs.get(prop)
	var new_position := stairs.position
	if is_equal_approx(float(new_value), float(restore)) \
			and new_position.is_equal_approx(_drag_start_position):
		_drag_handle_id = -1
		return

	_undo_redo.create_action("Cambiar %s de %s" % [_NAMES[handle_id], stairs.name])
	_undo_redo.add_do_property(stairs, prop, new_value)
	_undo_redo.add_do_property(stairs, "position", new_position)
	_undo_redo.add_undo_property(stairs, "position", _drag_start_position)
	_undo_redo.add_undo_property(stairs, prop, restore)
	_undo_redo.commit_action(false)
	_drag_handle_id = -1
