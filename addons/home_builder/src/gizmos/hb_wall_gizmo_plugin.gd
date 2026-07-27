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
##
## Holding Shift while dragging one of the two length handles switches that
## handle from "stretch along the wall's current axis" to "move this
## endpoint freely in the floor plane" — the opposite end stays put, but the
## dragged end (and therefore the wall's angle) can land anywhere on the
## floor plane, not just further out along the original direction. This is
## what lets an axis-aligned wall be turned diagonal after the fact instead
## of having to delete and replace it. Ctrl still toggles snapped/free
## within that mode.

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
var _drag_start_basis := Basis.IDENTITY
## Walls that formed a corner/T with the dragged wall BEFORE this drag
## started (see WallJunctionSolver.find_corner_partners) — captured once in
## _begin_drag so WallBuilder.resolve_wall_edit can reset them even after
## the wall's new position no longer comes anywhere near them.
var _drag_old_partners: Array[HBWall] = []
## Openings and length as they were before this drag touched them — used by
## _reposition_openings to keep each opening's distance to the anchor face
## fixed instead of letting it drift with the node's (moving) local origin.
## Captured once per drag so repeated per-frame recomputation doesn't drift.
var _drag_start_openings: Array[Dictionary] = []
var _drag_start_length := 0.0
## Local position of every externally-added child (door/window asset
## instances placed by OpeningBuilder — internal Mesh/Collision children are
## excluded automatically since get_children() defaults to
## include_internal = false) at the start of the drag. These aren't tracked
## by cx like openings are, so they'd otherwise stay put in the wall's local
## frame while the wall's node recentres under them — same drift bug as the
## openings themselves, just for the placed scene instead of the cut-out.
var _drag_start_children: Dictionary = {}


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

	if _AXES[handle_id] == Vector3.RIGHT and Input.is_key_pressed(KEY_SHIFT):
		_set_handle_endpoint(wall, handle_id, camera, screen_pos)
		return

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

	if _AXES[handle_id] == Vector3.RIGHT:
		new_length = _reposition_openings(wall, _ANCHOR_FRACS[handle_id], new_length)

	wall.global_position = HBGizmoResizeMath.node_position(
		_drag_anchor_global, _drag_dir_global,
		_SIGNS[handle_id], _ANCHOR_FRACS[handle_id], new_length)
	wall.set(_PROPS[handle_id], new_length)
	WallBuilder.resolve_wall_edit(wall, wall.get_parent(), _drag_old_partners)


## Moves a length handle's endpoint freely across the floor plane instead of
## along the wall's existing axis: the opposite end (_drag_anchor_global,
## captured in _begin_drag before the basis changes) stays fixed, the
## dragged end goes wherever the mouse ray crosses the plane at the wall's
## own height, and the basis is rebuilt each frame from anchor→point — the
## same convention WallBuilder.place_wall uses at initial placement, so a
## wall can be re-angled after the fact exactly as if it had been placed
## diagonally to begin with.
func _set_handle_endpoint(wall: HBWall, handle_id: int, camera: Camera3D, screen_pos: Vector2) -> void:
	var plane_y := _drag_anchor_global.y
	var point = RaycastHelper.to_floor_plane(camera, screen_pos, plane_y)
	if point == null:
		return
	if not Input.is_key_pressed(KEY_CTRL):
		point = SnapHelper.to_grid_corner(point, plane_y)

	var delta: Vector3 = point - _drag_anchor_global
	var horiz := Vector3(delta.x, 0.0, delta.z)
	var dist := horiz.length()

	var new_dir: Vector3
	if dist < 0.0001:
		new_dir = _drag_dir_global
	else:
		new_dir = horiz.normalized()
	dist = maxf(dist, _MIN_LENGTH[handle_id])
	dist = _reposition_openings(wall, _ANCHOR_FRACS[handle_id], dist)

	var basis_x := new_dir
	var basis_y := Vector3.UP
	var basis_z := basis_y.cross(basis_x).normalized()

	wall.basis = Basis(basis_x, basis_y, basis_z)
	wall.global_position = HBGizmoResizeMath.node_position(
		_drag_anchor_global, new_dir, _SIGNS[handle_id], _ANCHOR_FRACS[handle_id], dist)
	wall.length = dist
	WallBuilder.resolve_wall_edit(wall, wall.get_parent(), _drag_old_partners)


## Recomputes each opening's cx so its distance to the anchor face (the end
## NOT being dragged) stays exactly fixed. Without this, cx (stored relative
## to the wall's local origin, i.e. its center) would silently drift in world
## space whenever length changes, because resizing re-centers the node to
## keep the anchor face fixed — dragging one end would visibly shift every
## opening instead of leaving the fixed side untouched and growing/shrinking
## purely on the dragged side.
##
## Derivation: opening distance-to-anchor = cx0 - anchor_frac * L0 must equal
## new_cx - anchor_frac * new_length, so new_cx = cx0 + anchor_frac * (new_length - L0).
##
## Also clamps new_length upward if shrinking would push an opening past the
## dragged end (openings never scale or get shoved past the moving face).
## Returns the (possibly clamped) length to use.
##
## The same shift also gets applied to any door/window asset instance placed
## in the opening (see _drag_start_children) — those live as plain child
## nodes with their own local position.x, not as a cx inside wall.openings,
## so they need moving explicitly or they'd drift exactly like the openings
## used to before this fix.
func _reposition_openings(wall: HBWall, anchor_frac: float, new_length: float) -> float:
	if _drag_start_openings.is_empty() and _drag_start_children.is_empty():
		return new_length

	var clamped_length := new_length
	for d in _drag_start_openings:
		var cx0: float = d["cx"]
		var hw: float = d["w"] * 0.5
		# Minimum length so this opening's edge on the dragged side still
		# fits within the new wall bounds (its edge on the anchor side never
		# moves, so it never constrains the minimum).
		var min_length: float
		if anchor_frac < 0.0:
			min_length = cx0 + _drag_start_length * 0.5 + hw
		else:
			min_length = -cx0 + _drag_start_length * 0.5 + hw
		clamped_length = maxf(clamped_length, min_length)

	var delta := clamped_length - _drag_start_length

	if not _drag_start_openings.is_empty():
		var new_openings: Array[Dictionary] = []
		for d in _drag_start_openings:
			var nd := d.duplicate()
			nd["cx"] = d["cx"] + anchor_frac * delta
			new_openings.append(nd)
		wall.openings = new_openings

	var shift := anchor_frac * delta
	for child in _drag_start_children:
		if is_instance_valid(child):
			var base: Vector3 = _drag_start_children[child]
			child.position = Vector3(base.x + shift, base.y, base.z)

	return clamped_length


## Captures the opposite face's world position (the anchor) and the drag
## axis's world direction, both from the wall's state right before the drag
## touches it — these stay constant for the whole drag even as the node's
## position and size change frame to frame.
func _begin_drag(wall: HBWall, handle_id: int) -> void:
	_drag_handle_id = handle_id
	_drag_start_position = wall.position
	_drag_start_basis = wall.basis
	_drag_start_length = wall.length
	_drag_start_openings = wall.openings.duplicate(true)
	_drag_start_children.clear()
	for child in wall.get_children():
		if child is Node3D:
			_drag_start_children[child] = child.position
	_drag_old_partners = WallJunctionSolver.find_corner_partners(wall, wall.get_parent())

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
	var wall_parent := wall.get_parent()
	if cancel:
		wall.set(prop, restore)
		wall.position = _drag_start_position
		wall.basis = _drag_start_basis
		wall.openings = _drag_start_openings.duplicate(true)
		for child in _drag_start_children:
			if is_instance_valid(child):
				child.position = _drag_start_children[child]
		# Cancelling puts the wall back exactly where it started, so a plain
		# global rebuild is enough here — there's no "new position" for
		# resolve_wall_edit's old-partners pass to matter.
		WallBuilder.rebuild_junctions(wall_parent)
		_drag_handle_id = -1
		return

	var new_value = wall.get(prop)
	var new_position := wall.position
	var new_basis := wall.basis
	var new_openings := wall.openings
	if is_equal_approx(new_value, restore) and new_position.is_equal_approx(_drag_start_position) \
			and new_basis.is_equal_approx(_drag_start_basis):
		_drag_handle_id = -1
		return

	# rebuild_junctions itself isn't part of the undo/redo action (consistent
	# with WallBuilder.place_wall, which doesn't record it either): it's a
	# pure function of the walls' current geometry, so simply calling it
	# again after the property undo/redo applies is enough to leave every
	# wall's corners consistent with whichever state was just restored.
	_undo_redo.create_action("Cambiar %s de %s" % [_NAMES[handle_id], wall.name])
	_undo_redo.add_do_property(wall, prop, new_value)
	_undo_redo.add_do_property(wall, "position", new_position)
	_undo_redo.add_do_property(wall, "basis", new_basis)
	_undo_redo.add_do_property(wall, "openings", new_openings)
	for child in _drag_start_children:
		if is_instance_valid(child):
			_undo_redo.add_do_property(child, "position", child.position)
	_undo_redo.add_undo_property(wall, "openings", _drag_start_openings)
	_undo_redo.add_undo_property(wall, "basis", _drag_start_basis)
	_undo_redo.add_undo_property(wall, "position", _drag_start_position)
	_undo_redo.add_undo_property(wall, prop, restore)
	for child in _drag_start_children:
		if is_instance_valid(child):
			_undo_redo.add_undo_property(child, "position", _drag_start_children[child])
	_undo_redo.commit_action(false)
	WallBuilder.resolve_wall_edit(wall, wall_parent, _drag_old_partners)
	_drag_handle_id = -1
