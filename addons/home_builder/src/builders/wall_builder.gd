@tool
class_name WallBuilder
extends RefCounted

static var height: float = 3.0
static var thickness: float = 0.1

## Name of the standalone junction-fill mesh this addon used to create
## before walls fanned their own wedges. Kept only so rebuild_junctions()
## can find and remove one left over in an old scene.
const _FILL_NODE_NAME := "__HB_JunctionFills__"

# Untyped: typing it as the plugin class would create a cyclic class_name
# reference between the plugin script and every builder.
var _plugin

var _point_marker: CSGBox3D
var _start = null  # Vector3 while a first point is set, null otherwise


func _init(plugin) -> void:
	_plugin = plugin


# ── Preview ──────────────────────────────────────────────────────────────────

func create_marker(scene: Node3D, floor_base_y: float) -> void:
	_point_marker = PreviewHelper.create_marker(
		scene,
		"__HB_WallPoint__",
		Vector3(0.2, 0.2, 0.2),
		Color(0.9, 0.5, 0.1, 0.9),
		Vector3(0.0, floor_base_y, 0.0)
	)


func clear_preview() -> void:
	PreviewHelper.free_marker(_point_marker)
	_point_marker = null
	_start = null


# ── Input ────────────────────────────────────────────────────────────────────

func handle_input(camera: Camera3D, event: InputEvent, floor_base_y: float) -> int:
	if event is InputEventMouseMotion:
		var pos = RaycastHelper.to_floor_plane(camera, event.position, floor_base_y)
		if pos != null and _point_marker != null and is_instance_valid(_point_marker):
			_point_marker.position = SnapHelper.to_grid_corner(pos, floor_base_y)
		return EditorPlugin.AFTER_GUI_INPUT_PASS

	if event is InputEventMouseButton \
			and event.button_index == MOUSE_BUTTON_LEFT \
			and event.pressed:
		var pos = RaycastHelper.to_floor_plane(camera, event.position, floor_base_y)
		if pos == null:
			return EditorPlugin.AFTER_GUI_INPUT_PASS

		var corner := SnapHelper.to_grid_corner(pos, floor_base_y)

		if _start == null:
			_start = corner
		else:
			if not _start.is_equal_approx(corner):
				place_wall(_start, corner, floor_base_y)
			_start = null

		return EditorPlugin.AFTER_GUI_INPUT_STOP

	return EditorPlugin.AFTER_GUI_INPUT_PASS


# ── Placement ────────────────────────────────────────────────────────────────

func place_wall(start: Vector3, end: Vector3, floor_base_y: float) -> void:
	var wall_parent: Node3D = _plugin.get_or_create_floor_node(_plugin.active_floor)
	if wall_parent == null:
		return

	var length := Vector2(end.x - start.x, end.z - start.z).length()
	if length < 0.01:
		return

	var center := Vector3(
		(start.x + end.x) * 0.5,
		floor_base_y + height * 0.5,
		(start.z + end.z) * 0.5
	)

	var dir_xz := (end - start).normalized()
	var basis_x := dir_xz
	var basis_y := Vector3.UP
	var basis_z := basis_y.cross(basis_x).normalized()

	# HBWall is a self-contained StaticBody3D: it owns its mesh and collision
	# as internal children and rebuilds them from these parameters. The level
	# designer only ever sees a single "HBWall_001" node.
	#
	# Thickness/height are frozen at placement (copied into the node) so later
	# dock edits don't retroactively rewrite walls that were built earlier.
	var body := HBWall.new()
	# Godot auto-increments the trailing number (HBWall_002, _003, …) whenever
	# this name collides with an existing sibling.
	body.name = "HBWall_001"
	body.position = center
	body.basis = Basis(basis_x, basis_y, basis_z)
	body.length = length
	body.height = height
	body.thickness = thickness
	apply_materials(body)

	# force_readable_name = true so name collisions increment the trailing
	# number (HBWall_002, _003, …) instead of falling back to "@StaticBody3D@NN".
	wall_parent.add_child(body, true)
	body.owner = wall_parent.owner

	var undo: EditorUndoRedoManager = _plugin.get_undo_redo()
	undo.create_action("Place Wall")
	undo.add_do_method(wall_parent, &"add_child", body)
	undo.add_undo_method(wall_parent, &"remove_child", body)
	undo.commit_action(false)

	# Re-solve junctions for this floor and rebuild every wall whose cap
	# offsets changed. This is what yields clean miter corners at L, T
	# and X junctions with arbitrary angles.
	rebuild_junctions(wall_parent)


# ── Junction rebuild ─────────────────────────────────────────────────────────
#
# Recomputes miter offsets across every wall under `wall_parent` and
# rebuilds any wall whose mesh should change. Cheap for realistic scenes
# (tens to hundreds of walls per floor) and avoids the need to track
# "dirty" neighbours incrementally.

static func rebuild_junctions(wall_parent: Node3D) -> void:
	if wall_parent == null:
		return

	# One-time migration: scenes saved before junction wedges existed may
	# still carry the old standalone fill mesh. Each wall now covers its own
	# slice of the gap directly, so drop the leftover node the first time
	# any wall on this floor is touched.
	_remove_legacy_fill_node(wall_parent)

	var solved := WallJunctionSolver.solve(wall_parent)
	for wall_body in solved.offsets:
		if wall_body is HBWall:
			var wall: HBWall = wall_body
			wall.set_join_offsets(WallJunctionSolver.to_mesh_joins(solved.offsets[wall_body]))


## Use case: [param wall] is being edited (gizmo drag). [param old_partners]
## are whoever WallJunctionSolver.find_corner_partners found for [param wall]
## BEFORE the edit started (captured by the caller at drag-begin, while the
## wall was still at its old position/angle).
##
## Two explicit passes, matching how a person reasons about it instead of
## trusting one opaque global solve:
##  1. Blank the old partners' cap offsets — as if [param wall] had been
##     removed from the scene entirely. Four walls in an X become three in a
##     T once the edited one leaves; a plain L-corner partner just goes flat.
##  2. Re-solve everyone (including [param wall] at its NEW position) from
##     scratch. This both forms whatever junction the new position creates
##     and re-solves the old partners against each other now that step 1's
##     blank state is the starting point, not their stale pre-edit offsets.
static func resolve_wall_edit(wall: HBWall, wall_parent: Node3D, old_partners: Array[HBWall]) -> void:
	if wall_parent == null:
		return

	for partner in old_partners:
		if partner == wall or not is_instance_valid(partner):
			continue
		partner.set_join_offsets(WallMeshBuilder.JoinOffsets.new())

	rebuild_junctions(wall_parent)


# ── Legacy junction fill cleanup ─────────────────────────────────────────────
#
# Superseded by per-wall wedges (WallJunctionSolver.solve() + WallMeshBuilder
# fanning each affected end to the junction centroid) — kept only so scenes
# saved before that change self-heal on their next edit.

static func _remove_legacy_fill_node(wall_parent: Node3D) -> void:
	for child in wall_parent.get_children():
		if child.name == _FILL_NODE_NAME:
			child.queue_free()
			return


# ── Materials ────────────────────────────────────────────────────────────────

## Copies the dock's currently-selected wall materials onto the node. They are
## stored on the HBWall itself, so changing the dock afterwards leaves existing
## walls untouched — and the designer can still override any slot per-wall from
## the Inspector.
func apply_materials(wall: HBWall) -> void:
	var dock = _plugin.dock
	wall.face_a_material = dock.wall_face_a_material if dock != null else null
	wall.face_b_material = dock.wall_face_b_material if dock != null else null
	wall.edges_material = dock.wall_edges_material if dock != null else null
