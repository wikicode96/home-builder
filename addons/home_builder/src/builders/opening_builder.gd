@tool
class_name OpeningBuilder
extends RefCounted

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

	var wall := wall_body as HBWall
	if wall == null:
		return

	# ── 1. Read wall length ──────────────────────────────────────────────────
	var wall_len := wall.length
	if wall_len == 0.0:
		return

	# ── 2. Build the new Opening ─────────────────────────────────────────────
	var new_opening := WallMeshBuilder.Opening.new(
		local_center,
		_door_width() if is_door else _win_width(),
		0.0 if is_door else _win_sill(),
		_door_height() if is_door else _win_height()
	)

	# ── 3. Guard: reject if this opening overlaps an existing one ────────────
	for existing in wall.get_openings():
		if new_opening.left() < existing.right() and new_opening.right() > existing.left():
			push_error("[HomeBuilder] Opening overlaps an existing one — skipped.")
			return

	# ── 4. Guard: reject if opening would go outside the wall ────────────────
	var hx := wall_len * 0.5
	if new_opening.left() < -hx or new_opening.right() > hx:
		push_error("[HomeBuilder] Opening is outside wall bounds — skipped.")
		return

	# ── 5. Hand the opening to the node, which rebuilds mesh and collision
	#       while keeping its current junction miter offsets. ─────────────────
	wall.add_opening(new_opening)
