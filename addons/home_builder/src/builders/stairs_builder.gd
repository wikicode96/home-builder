@tool
class_name StairsBuilder
extends RefCounted

static var stair_count: int = 12
static var stair_run: float = 0.25
static var stair_width: float = 1.0

const _GHOST_COLOR := Color(0.9, 0.8, 0.1, 0.4)

# Untyped: typing it as the plugin class would create a cyclic class_name
# reference between the plugin script and every builder.
var _plugin

var _ghost: Node3D
var _ghost_is_staircase := false
var _start = null  # Vector3 while a first point is set, null otherwise


func _init(plugin) -> void:
	_plugin = plugin


static func _stair_rise() -> float:
	return WallBuilder.height / stair_count


# ── Preview ──────────────────────────────────────────────────────────────────

## floor_base_y is unused at creation: the ghost is moved into place on the
## first mouse-motion event. The parameter is kept for symmetry with the
## other builder ghosts.
func create_ghost(scene: Node3D, _floor_base_y: float) -> void:
	_ghost = Node3D.new()
	_ghost.name = "__HB_StairsGhost__"
	scene.add_child(_ghost)
	_ghost_is_staircase = false
	_add_tile_child()


func clear_preview() -> void:
	if _ghost != null and is_instance_valid(_ghost):
		_ghost.free()
	_ghost = null
	_ghost_is_staircase = false
	_start = null


# ── Input ────────────────────────────────────────────────────────────────────

func handle_input(camera: Camera3D, event: InputEvent, floor_base_y: float) -> int:
	if event is InputEventMouseMotion:
		var pos = RaycastHelper.to_floor_plane(camera, event.position, floor_base_y)
		if pos == null:
			return EditorPlugin.AFTER_GUI_INPUT_PASS

		var snapped_pos := SnapHelper.to_tile_center(pos, floor_base_y)

		if _start != null:
			_update_ghost(_start, snapped_pos, floor_base_y)
		elif _ghost != null and is_instance_valid(_ghost):
			_ghost.position = snapped_pos

		return EditorPlugin.AFTER_GUI_INPUT_PASS

	if event is InputEventMouseButton \
			and event.button_index == MOUSE_BUTTON_LEFT \
			and event.pressed:
		var pos = RaycastHelper.to_floor_plane(camera, event.position, floor_base_y)
		if pos == null:
			return EditorPlugin.AFTER_GUI_INPUT_PASS

		var corner := SnapHelper.to_tile_center(pos, floor_base_y)

		if _start == null:
			_start = corner
		else:
			if not _start.is_equal_approx(corner):
				_place_stairs(_start, corner, floor_base_y)
			_start = null
			_reset_ghost_to_tile()

		return EditorPlugin.AFTER_GUI_INPUT_STOP

	return EditorPlugin.AFTER_GUI_INPUT_PASS


# ── Ghost helpers ────────────────────────────────────────────────────────────

func _add_tile_child() -> void:
	var tile := CSGBox3D.new()
	tile.size = Vector3(1.0, WallBuilder.height, 1.0)
	tile.position = Vector3(0.0, WallBuilder.height * 0.5, 0.0)
	tile.material_override = PreviewHelper.make_material(_GHOST_COLOR)
	tile.use_collision = false
	_ghost.add_child(tile)


func _reset_ghost_to_tile() -> void:
	if _ghost == null or not is_instance_valid(_ghost):
		return
	for child in _ghost.get_children():
		child.free()
	_ghost_is_staircase = false
	_add_tile_child()


# ── Ghost update ─────────────────────────────────────────────────────────────

func _update_ghost(start: Vector3, cursor: Vector3, floor_base_y: float) -> void:
	if _ghost == null or not is_instance_valid(_ghost):
		return

	var dir := cursor - start
	if dir.length_squared() < 0.001:
		return

	var dir_xz := Vector3(dir.x, 0.0, dir.z).normalized()
	var step_basis := Basis(Vector3.UP.cross(dir_xz).normalized(), Vector3.UP, dir_xz)
	var z_fighting := 0.001
	var origin := Vector3(start.x, floor_base_y + z_fighting, start.z)

	if not _ghost_is_staircase:
		# Switch from single tile to full staircase preview
		_ghost.position = Vector3.ZERO
		for child in _ghost.get_children():
			child.free()

		var step_mesh := StairsMeshBuilder.build(stair_width, _stair_rise(), stair_run)
		var mat := PreviewHelper.make_material(_GHOST_COLOR)

		for i in stair_count:
			var step := MeshInstance3D.new()
			step.mesh = step_mesh
			step.set_surface_override_material(StairsMeshBuilder.SURFACE_TOP, mat)
			step.set_surface_override_material(StairsMeshBuilder.SURFACE_BOTTOM, mat)
			step.set_surface_override_material(StairsMeshBuilder.SURFACE_SIDES, mat)
			_ghost.add_child(step)

		_ghost_is_staircase = true

	# Update each step position/orientation (world space, container is at zero)
	var children := _ghost.get_children()
	for i in stair_count:
		var run_offset := i * stair_run + stair_run * 0.5 - 0.5
		var rise_offset := (i + 0.5) * _stair_rise()

		if children[i] is MeshInstance3D:
			var step: MeshInstance3D = children[i]
			step.position = origin + dir_xz * run_offset + Vector3.UP * rise_offset
			step.basis = step_basis


# ── Placement ────────────────────────────────────────────────────────────────

func _place_stairs(start: Vector3, dir_hint: Vector3, floor_base_y: float) -> void:
	var stairs_parent: Node3D = _plugin.get_or_create_parent_node("Stairs_%d" % _plugin.active_floor)
	if stairs_parent == null:
		return

	var scene := EditorInterface.get_edited_scene_root() as Node3D
	if scene == null:
		return

	var diff := dir_hint - start
	var dir_xz := Vector3(diff.x, 0.0, diff.z).normalized()
	var basis_z := dir_xz
	var basis_x := Vector3.UP.cross(basis_z).normalized()
	var basis_y := Vector3.UP
	var step_basis := Basis(basis_x, basis_y, basis_z)

	# Offset by the same z_fighting margin used in FloorBuilder so step
	# bottoms sit flush with the top surface of the floor slab (not the
	# raw floor plane), avoiding Z-fighting with the wall below.
	var z_fighting := 0.001
	var origin := Vector3(start.x, floor_base_y + z_fighting, start.z)

	# HBStairs is self-contained: it owns every step mesh and the ramp
	# collision as internal children. The designer only ever sees a single
	# "HBStairs_001" node.
	var body := HBStairs.new()
	body.name = "HBStairs_001"
	body.position = origin
	body.basis = step_basis
	body.count = stair_count
	body.width = stair_width
	body.run = stair_run
	body.height = WallBuilder.height

	var dock = _plugin.dock
	body.top_material = MaterialHelper.or_default(
		dock.stair_top_material if dock != null else null, Color(0.8, 0.7, 0.5))
	body.bottom_material = MaterialHelper.or_default(
		dock.stair_bottom_material if dock != null else null, Color(0.6, 0.6, 0.6))
	body.sides_material = MaterialHelper.or_default(
		dock.stair_sides_material if dock != null else null, Color(0.5, 0.5, 0.5))

	var undo: EditorUndoRedoManager = _plugin.get_undo_redo()
	undo.create_action("Place Stairs")

	# force_readable_name = true so name collisions increment the trailing
	# number (HBStairs_002, …) instead of "@StaticBody3D@NN".
	stairs_parent.add_child(body, true)
	body.owner = scene

	undo.add_do_method(stairs_parent, &"add_child", body)
	undo.add_undo_method(stairs_parent, &"remove_child", body)
	undo.commit_action(false)
