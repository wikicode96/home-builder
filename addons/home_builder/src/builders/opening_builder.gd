@tool
class_name OpeningBuilder
extends RefCounted

## Metadata key stored on each StaticBody3D wall node to persist openings.
const _META_KEY := "hb_openings"

# Untyped: typing it as the plugin class would create a cyclic class_name
# reference between the plugin script and every builder.
var _plugin

var _marker: CSGBox3D


func _init(plugin) -> void:
	_plugin = plugin


func _door_width() -> float:
	var dock = _plugin.dock
	return dock.door_width if dock != null else 1.0


func _door_height() -> float:
	var dock = _plugin.dock
	return dock.door_height if dock != null else 2.0


func _win_width() -> float:
	var dock = _plugin.dock
	return dock.win_width if dock != null else 1.0


func _win_height() -> float:
	var dock = _plugin.dock
	return dock.win_height if dock != null else 1.0


func _win_sill() -> float:
	var dock = _plugin.dock
	return dock.win_sill if dock != null else 0.9


# ── Preview ──────────────────────────────────────────────────────────────────

func create_marker(scene: Node3D, is_door: bool) -> void:
	var w := _door_width() if is_door else _win_width()
	var h := _door_height() if is_door else _win_height()

	_marker = PreviewHelper.create_marker(
		scene,
		"__HB_OpeningMarker__",
		Vector3(w, h, WallBuilder.thickness + 0.05),
		Color(0.2, 0.6, 1.0, 0.5),
		Vector3.ZERO
	)


func clear_preview() -> void:
	PreviewHelper.free_marker(_marker)
	_marker = null


# ── Input ────────────────────────────────────────────────────────────────────

func handle_input(camera: Camera3D, event: InputEvent, is_door: bool, wall_parent: Node3D) -> int:
	if event is InputEventMouseMotion:
		var hit := RaycastHelper.to_walls(camera, event.position, wall_parent)
		if hit != null:
			var wall_body := hit.collider
			var opening_width := _door_width() if is_door else _win_width()
			var snapped_x := SnapHelper.to_wall(wall_body, hit.position, opening_width)

			if _marker != null and is_instance_valid(_marker):
				var wall_h := WallHelper.get_wall_height(wall_body)
				var wall_t := WallHelper.get_wall_thickness(wall_body)

				var marker_local_y := (
					_door_height() * 0.5 - wall_h * 0.5 if is_door
					else _win_sill() + _win_height() * 0.5 - wall_h * 0.5
				)

				_marker.size = Vector3(_marker.size.x, _marker.size.y, wall_t + 0.05)

				var axis_x := wall_body.global_transform.basis.x.normalized()
				var axis_y := wall_body.global_transform.basis.y.normalized()
				_marker.global_position = (
					wall_body.global_position + axis_x * snapped_x + axis_y * marker_local_y
				)
				_marker.basis = wall_body.global_transform.basis
		return EditorPlugin.AFTER_GUI_INPUT_PASS

	if event is InputEventMouseButton \
			and event.button_index == MOUSE_BUTTON_LEFT \
			and event.pressed:
		var hit := RaycastHelper.to_walls(camera, event.position, wall_parent)
		if hit != null:
			var wall_body := hit.collider
			var opening_width := _door_width() if is_door else _win_width()
			var snapped_x := SnapHelper.to_wall(wall_body, hit.position, opening_width)

			_cut_opening(wall_body, snapped_x, is_door)
			return EditorPlugin.AFTER_GUI_INPUT_STOP

	return EditorPlugin.AFTER_GUI_INPUT_PASS


# ── Cut / accumulate opening ─────────────────────────────────────────────────

func _cut_opening(wall_body: StaticBody3D, local_center: float, is_door: bool) -> void:
	if not (EditorInterface.get_edited_scene_root() is Node3D):
		return

	# ── 1. Read wall length ──────────────────────────────────────────────────
	var wall_len := WallHelper.get_wall_length(wall_body)
	if wall_len == 0.0:
		return

	# ── 2. Build the new Opening ─────────────────────────────────────────────
	var new_opening := WallMeshBuilder.Opening.new(
		local_center,
		_door_width() if is_door else _win_width(),
		0.0 if is_door else _win_sill(),
		_door_height() if is_door else _win_height()
	)

	# ── 3. Load existing openings from node metadata ─────────────────────────
	var openings := load_openings(wall_body)

	# ── 4. Guard: reject if this opening overlaps an existing one ────────────
	for existing in openings:
		if new_opening.left() < existing.right() and new_opening.right() > existing.left():
			push_error("[HomeBuilder] Opening overlaps an existing one — skipped.")
			return

	# ── 5. Guard: reject if opening would go outside the wall ────────────────
	var hx := wall_len * 0.5
	if new_opening.left() < -hx or new_opening.right() > hx:
		push_error("[HomeBuilder] Opening is outside wall bounds — skipped.")
		return

	openings.append(new_opening)

	# ── 6. Save updated list back to metadata ────────────────────────────────
	_save_openings(wall_body, openings)

	# ── 7. Rebuild mesh, keeping the current junction miter offsets ──────────
	var joins := _solve_joins_for(wall_body)
	var new_mesh := WallMeshBuilder.build_with_openings_and_joins(
		wall_len,
		WallHelper.get_wall_height(wall_body),
		WallHelper.get_wall_thickness(wall_body),
		openings,
		joins
	)

	# ── 8. Apply to MeshInstance3D ───────────────────────────────────────────
	var wall_mesh := _get_mesh_instance(wall_body)
	if wall_mesh == null:
		return

	wall_mesh.mesh = new_mesh

	# ── 9. Rebuild collision ─────────────────────────────────────────────────
	_update_collision(wall_body, new_mesh)


## Re-solves junctions for the wall's parent and returns this wall's
## current mesh join offsets (or zeros if it has no neighbours).
static func _solve_joins_for(wall_body: StaticBody3D) -> WallMeshBuilder.JoinOffsets:
	var parent := wall_body.get_parent() as Node3D
	if parent == null:
		return WallMeshBuilder.JoinOffsets.new()
	var solved := WallJunctionSolver.solve(parent)
	if solved.offsets.has(wall_body):
		return WallJunctionSolver.to_mesh_joins(solved.offsets[wall_body])
	return WallMeshBuilder.JoinOffsets.new()


# ── Metadata helpers — openings stored as an Array of Dictionaries ───────────

## Returns an Array of WallMeshBuilder.Opening.
static func load_openings(wall: StaticBody3D) -> Array:
	var result: Array = []

	if not wall.has_meta(_META_KEY):
		return result

	var arr: Array = wall.get_meta(_META_KEY)
	for item in arr:
		var d: Dictionary = item
		result.append(WallMeshBuilder.Opening.new(d["cx"], d["w"], d["by"], d["h"]))

	return result


static func _save_openings(wall: StaticBody3D, openings: Array) -> void:
	var arr: Array = []
	for op in openings:
		arr.append({
			"cx": op.center_x,
			"w": op.width,
			"by": op.bottom_y,
			"h": op.height,
		})
	wall.set_meta(_META_KEY, arr)


# ── Wall helpers ─────────────────────────────────────────────────────────────

static func _get_mesh_instance(wall_body: StaticBody3D) -> MeshInstance3D:
	for child in wall_body.get_children():
		if child is MeshInstance3D:
			return child
	return null


# ── Collision rebuild ────────────────────────────────────────────────────────

static func _update_collision(wall_body: StaticBody3D, new_mesh: ArrayMesh) -> void:
	for child in wall_body.get_children():
		if child is CollisionShape3D:
			update_collision_from_mesh(child, new_mesh)
			return


static func update_collision_from_mesh(collision_shape: CollisionShape3D, new_mesh: ArrayMesh) -> void:
	if collision_shape == null or new_mesh == null:
		return

	var vertex_list := PackedVector3Array()
	for i in new_mesh.get_surface_count():
		var mesh_data := new_mesh.surface_get_arrays(i)
		if mesh_data == null or mesh_data.is_empty():
			continue

		var verts: PackedVector3Array = mesh_data[Mesh.ARRAY_VERTEX]
		vertex_list.append_array(verts)

	if vertex_list.is_empty():
		return

	var concave_shape := ConcavePolygonShape3D.new()
	concave_shape.set_faces(vertex_list)
	collision_shape.shape = concave_shape
