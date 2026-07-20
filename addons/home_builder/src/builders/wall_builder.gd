@tool
class_name WallBuilder
extends RefCounted

static var height: float = 3.0
static var thickness: float = 0.1

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
	var wall_parent: Node3D = _plugin.get_or_create_parent_node("Walls_%d" % _plugin.active_floor)
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

	# StaticBody3D is the root — holds position, basis and collision
	var body := StaticBody3D.new()
	body.name = "Wall"
	body.position = center
	body.basis = Basis(basis_x, basis_y, basis_z)

	# Store wall length as metadata so OpeningBuilder can read it later,
	# even after the collision shape is replaced by ConcavePolygonShape3D.
	# Thickness/height are frozen at placement so later dock edits don't
	# retroactively change this wall.
	var wall_thickness := thickness
	var wall_height := height
	body.set_meta(WallHelper.META_WALL_LENGTH, length)
	body.set_meta(WallHelper.META_WALL_THICKNESS, wall_thickness)
	body.set_meta(WallHelper.META_WALL_HEIGHT, wall_height)

	# Visual mesh as child
	var wall := MeshInstance3D.new()
	wall.mesh = WallMeshBuilder.build(length, wall_height, wall_thickness)
	apply_materials(wall)

	# Collision shape — BoxShape3D matches wall dimensions exactly
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(length, wall_height, wall_thickness)
	shape.shape = box

	wall_parent.add_child(body)
	body.owner = wall_parent.owner

	body.add_child(wall)
	wall.owner = wall_parent.owner

	body.add_child(shape)
	shape.owner = wall_parent.owner

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

	var solved := WallJunctionSolver.solve(wall_parent)
	for wall_body in solved.offsets:
		_rebuild_wall_mesh(wall_body, solved.offsets[wall_body])
	_rebuild_junction_fills(wall_parent, solved.fills)


# ── Junction fill mesh — covers the gap polygon at X/Y/multi-wall nodes ──────

static func _rebuild_junction_fills(wall_parent: Node3D, fills: Array) -> void:
	var fill_node: MeshInstance3D = null
	for child in wall_parent.get_children():
		if child.name == _FILL_NODE_NAME and child is MeshInstance3D:
			fill_node = child
			break

	if fills.is_empty():
		if fill_node != null:
			fill_node.queue_free()
		return

	if fill_node == null:
		fill_node = MeshInstance3D.new()
		fill_node.name = _FILL_NODE_NAME
		wall_parent.add_child(fill_node)
		fill_node.owner = wall_parent.owner

	# Position the fill node at the same Y centre as the walls so that
	# local Y = ±height/2 aligns with the wall top and bottom in world.
	var wall_centre_y := height * 0.5
	for child in wall_parent.get_children():
		if child is StaticBody3D:
			wall_centre_y = child.position.y
			break
	fill_node.position = Vector3(0.0, wall_centre_y, 0.0)

	fill_node.mesh = JunctionFillMeshBuilder.build(fills, height)

	# Reuse the edge material from the first available wall.
	if fill_node.get_surface_override_material(0) == null:
		var mat := _find_wall_edge_material(wall_parent)
		if mat != null:
			fill_node.set_surface_override_material(0, mat)


static func _find_wall_edge_material(wall_parent: Node3D) -> Material:
	for child in wall_parent.get_children():
		if not (child is StaticBody3D):
			continue
		for grandchild in child.get_children():
			if not (grandchild is MeshInstance3D):
				continue
			var mat: Material = grandchild.get_surface_override_material(WallMeshBuilder.SURFACE_EDGES)
			if mat != null:
				return mat
	return null


static func _rebuild_wall_mesh(wall_body: StaticBody3D, off: WallJunctionSolver.Offsets) -> void:
	var wall_len := WallHelper.get_wall_length(wall_body)
	if wall_len <= 0.0:
		return

	var wall_height := WallHelper.get_wall_height(wall_body)
	var wall_thickness := WallHelper.get_wall_thickness(wall_body)

	var openings := OpeningBuilder.load_openings(wall_body)
	var joins := WallJunctionSolver.to_mesh_joins(off)

	var new_mesh := WallMeshBuilder.build_with_openings_and_joins(
		wall_len, wall_height, wall_thickness, openings, joins)

	var mesh_instance: MeshInstance3D = null
	var collision_shape: CollisionShape3D = null
	for child in wall_body.get_children():
		if child is MeshInstance3D:
			mesh_instance = child
		if child is CollisionShape3D:
			collision_shape = child
	if mesh_instance == null:
		return

	mesh_instance.mesh = new_mesh
	if collision_shape != null:
		OpeningBuilder.update_collision_from_mesh(collision_shape, new_mesh)


# ── Materials ────────────────────────────────────────────────────────────────

func apply_materials(wall: MeshInstance3D) -> void:
	var dock = _plugin.dock
	wall.set_surface_override_material(WallMeshBuilder.SURFACE_FACE_A,
		MaterialHelper.or_default(
			dock.wall_face_a_material if dock != null else null, Color(0.9, 0.9, 0.85)))
	wall.set_surface_override_material(WallMeshBuilder.SURFACE_FACE_B,
		MaterialHelper.or_default(
			dock.wall_face_b_material if dock != null else null, Color(0.85, 0.85, 0.8)))
	wall.set_surface_override_material(WallMeshBuilder.SURFACE_EDGES,
		MaterialHelper.or_default(
			dock.wall_edges_material if dock != null else null, Color(0.7, 0.7, 0.65)))
