@tool
class_name HBRoofGizmoPlugin
extends EditorNode3DGizmoPlugin

## Drag handles for HBRoof's footprint (width/depth) and, for sloped types,
## its pitch (ridge height).
##
## Unlike HBWall/HBFloorSlab, RoofMeshBuilder anchors the mesh at the node's
## local origin — its footprint spans local X:[0,width], Z:[0,depth] regardless
## of roof_type/direction (see RoofMeshBuilder's header comment: the rotation
## transform it applies is center-preserving, so this stays true for every
## type). The eave overhang renders outside that box, but it is not part of
## the footprint and no handle sizes it, so the math below is unaffected: the
## gizmo always outlines the structural footprint, and the eave follows it.
## So the "+width"/"+depth" handles never need to move the node —
## only the "-width"/"-depth" handles (the near corner) do, to keep the far
## corner fixed while the near one grows toward the mouse. Same underlying
## math as HBFloorSlabGizmoPlugin (HBGizmoResizeMath), just with anchor
## fractions of 0.0/1.0 instead of Floor's -0.5/0.5.
##
## The pitch handle sits above the footprint's centre; since its anchor is
## the base (y=0, anchor_frac 0.0), dragging it never moves the node either.
## It's only shown for non-flat roofs — FlatRoof always renders at a fixed
## thickness regardless of pitch, so a handle for it there would move with no
## visible feedback.
##
## Snaps to the 0.5m world grid by default, OFFSET outward by half a wall
## thickness — matching RoofBuilder's own placement footprint, not just its
## placement grid (SnapHelper.to_tile_center/grid_bounds plus the inflation
## _place_roof applies on top). See _footprint_offset: without that shift a
## roof you just placed and one you nudged with the gizmo disagree about where
## a roof edge belongs, and the dragged side ends up short against the facade.
## Hold Ctrl while dragging to size it continuously instead.

const _AXES := [Vector3.RIGHT, Vector3.RIGHT, Vector3.BACK, Vector3.BACK, Vector3.UP]
const _SIGNS := [1.0, -1.0, 1.0, -1.0, 1.0]
const _ANCHOR_FRACS := [0.0, 1.0, 0.0, 1.0, 0.0]
const _PROPS := ["width", "width", "depth", "depth", "pitch"]
const _NAMES := ["Ancho", "Ancho", "Fondo", "Fondo", "Inclinación"]
const _MIN_SIZE := [0.1, 0.1, 0.1, 0.1, 0.05]
const _GRID_STEP := 0.5

var _undo_redo: EditorUndoRedoManager

var _drag_handle_id := -1
var _drag_anchor_global := Vector3.ZERO
var _drag_dir_global := Vector3.ZERO
var _drag_start_position := Vector3.ZERO


func _init(undo_redo: EditorUndoRedoManager) -> void:
	_undo_redo = undo_redo
	create_handle_material("handles")
	create_material("lines", Color(1.0, 0.45, 0.45), false, true, false)


func _get_gizmo_name() -> String:
	return "HBRoof"


func _has_gizmo(for_node_3d: Node3D) -> bool:
	return for_node_3d is HBRoof


func _redraw(gizmo: EditorNode3DGizmo) -> void:
	gizmo.clear()
	var roof := gizmo.get_node_3d() as HBRoof
	if roof == null:
		return

	var w := roof.width
	var d := roof.depth
	var has_pitch := roof.roof_type != RoofMeshBuilder.RoofType.FLAT

	var lines := PackedVector3Array()
	_add_footprint_lines(lines, w, d, has_pitch, roof.pitch)
	gizmo.add_lines(lines, get_material("lines", gizmo), false)

	var handles := PackedVector3Array([
		Vector3(w, 0.0, d * 0.5), Vector3(0.0, 0.0, d * 0.5),
		Vector3(w * 0.5, 0.0, d), Vector3(w * 0.5, 0.0, 0.0),
	])
	if has_pitch:
		handles.append(Vector3(w * 0.5, roof.pitch, d * 0.5))
	gizmo.add_handles(handles, get_material("handles", gizmo), [])


static func _add_footprint_lines(lines: PackedVector3Array, w: float, d: float,
		has_pitch: bool, p: float) -> void:
	var corners := [
		Vector3(0.0, 0.0, 0.0), Vector3(w, 0.0, 0.0),
		Vector3(w, 0.0, d), Vector3(0.0, 0.0, d),
	]
	for i in 4:
		lines.append(corners[i])
		lines.append(corners[(i + 1) % 4])
	if has_pitch:
		var ridge := Vector3(w * 0.5, p, d * 0.5)
		for c in corners:
			lines.append(c)
			lines.append(ridge)


# ── Handle interaction ───────────────────────────────────────────────────────

func _get_handle_name(_gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool) -> String:
	return _NAMES[handle_id]


func _get_handle_value(gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool) -> Variant:
	var roof := gizmo.get_node_3d() as HBRoof
	return roof.get(_PROPS[handle_id])


func _set_handle(gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool,
		camera: Camera3D, screen_pos: Vector2) -> void:
	var roof := gizmo.get_node_3d() as HBRoof
	if roof == null:
		return

	if handle_id != _drag_handle_id:
		_begin_drag(roof, handle_id)

	var ray_from := camera.project_ray_origin(screen_pos)
	var ray_dir := camera.project_ray_normal(screen_pos)
	var new_size: float
	if Input.is_key_pressed(KEY_CTRL):
		new_size = HBGizmoResizeMath.drag_size(
			_drag_anchor_global, _drag_dir_global, ray_from, ray_dir, _MIN_SIZE[handle_id])
	elif _AXES[handle_id] == Vector3.UP:
		# Vertical handle (pitch): snap the height itself, not the absolute
		# world Y (see HBGizmoResizeMath.drag_size_snapped_relative).
		new_size = HBGizmoResizeMath.drag_size_snapped_relative(
			_drag_anchor_global, _drag_dir_global, ray_from, ray_dir,
			_GRID_STEP, _MIN_SIZE[handle_id])
	else:
		# Footprint edges do NOT sit on the world grid — see _footprint_offset.
		new_size = HBGizmoResizeMath.drag_size_snapped(
			_drag_anchor_global, _drag_dir_global, ray_from, ray_dir,
			_GRID_STEP, _MIN_SIZE[handle_id], _footprint_offset())

	roof.global_position = HBGizmoResizeMath.node_position(
		_drag_anchor_global, _drag_dir_global,
		_SIGNS[handle_id], _ANCHOR_FRACS[handle_id], new_size)
	roof.set(_PROPS[handle_id], new_size)


## How far outside the 0.5 m world grid the dragged edge belongs, as a world
## vector.
##
## RoofBuilder does not lay the footprint on the grid: it inflates it by half a
## wall thickness on every side, so the roof covers the outer FACE of the
## facade walls instead of stopping at their centre line (walls are centred on
## grid lines, so the exterior half of a boundary wall would otherwise stick
## out uncovered). Every edge of a placed roof therefore sits at
## `n × 0.5 ± half a wall`, never at a round `n × 0.5`.
##
## Snapping a dragged edge to the bare grid quietly undid that on whichever
## side was dragged — the edge landed on the wall's centre line and the roof
## came up short by half a wall against the facade below, while the three
## untouched sides stayed correct. Multiplying by the drag direction gives the
## shift its sign for free: the offset is always OUTWARD, which is +half on the
## +X/+Z handles and −half on the −X/−Z ones.
##
## Reads WallBuilder.thickness live, the same value RoofBuilder used, so a roof
## resized after the dock's wall thickness changed lines up with the walls that
## exist now rather than the ones that existed at placement.
func _footprint_offset() -> Vector3:
	return _drag_dir_global * (WallBuilder.thickness * 0.5)


func _begin_drag(roof: HBRoof, handle_id: int) -> void:
	_drag_handle_id = handle_id
	_drag_start_position = roof.position

	var axis: Vector3 = _AXES[handle_id]
	var sign: float = _SIGNS[handle_id]
	var current_size: float = roof.get(_PROPS[handle_id])
	var start_transform := roof.global_transform

	_drag_anchor_global = HBGizmoResizeMath.anchor_global(
		start_transform, axis, _ANCHOR_FRACS[handle_id], current_size)
	_drag_dir_global = HBGizmoResizeMath.dir_global(start_transform.basis, axis, sign)


func _commit_handle(gizmo: EditorNode3DGizmo, handle_id: int, _secondary: bool,
		restore: Variant, cancel: bool) -> void:
	var roof := gizmo.get_node_3d() as HBRoof
	if roof == null:
		_drag_handle_id = -1
		return

	var prop: String = _PROPS[handle_id]
	if cancel:
		roof.set(prop, restore)
		roof.position = _drag_start_position
		_drag_handle_id = -1
		return

	var new_value = roof.get(prop)
	var new_position := roof.position
	if is_equal_approx(new_value, restore) and new_position.is_equal_approx(_drag_start_position):
		_drag_handle_id = -1
		return

	_undo_redo.create_action("Cambiar %s de %s" % [_NAMES[handle_id], roof.name])
	_undo_redo.add_do_property(roof, prop, new_value)
	_undo_redo.add_do_property(roof, "position", new_position)
	_undo_redo.add_undo_property(roof, "position", _drag_start_position)
	_undo_redo.add_undo_property(roof, prop, restore)
	_undo_redo.commit_action(false)
	_drag_handle_id = -1
