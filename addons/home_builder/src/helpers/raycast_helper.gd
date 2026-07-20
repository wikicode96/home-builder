@tool
class_name RaycastHelper
extends RefCounted


class HitInfo:
	var position: Vector3
	var collider: StaticBody3D

	func _init(p_position := Vector3.ZERO, p_collider: StaticBody3D = null) -> void:
		position = p_position
		collider = p_collider


# ── Floor plane intersection ─────────────────────────────────────────────────

## Returns the Vector3 where the mouse ray crosses the horizontal plane
## y = floor_base_y, or null when the ray is parallel or points away.
static func to_floor_plane(camera: Camera3D, screen_pos: Vector2, floor_base_y: float) -> Variant:
	var origin := camera.project_ray_origin(screen_pos)
	var direction := camera.project_ray_normal(screen_pos)

	if is_zero_approx(direction.y):
		return null

	var t := -(origin.y - floor_base_y) / direction.y
	if t < 0.0:
		return null

	return origin + direction * t


# ── Wall intersection — geometric OBB test, no physics engine required ───────
#
# This works reliably in the Godot editor where DirectSpaceState may not
# reflect freshly-modified collision shapes (e.g. after replacing
# BoxShape3D with ConcavePolygonShape3D on the first opening).
#
# Each wall StaticBody3D is an OBB of size (length × height × thickness).
# We transform the ray into the body's local space and do a standard
# ray-vs-AABB slab test there, then transform the hit back to world space.

## Returns the closest HitInfo among the walls under [param wall_parent],
## or null when nothing is hit.
static func to_walls(camera: Camera3D, screen_pos: Vector2, wall_parent: Node3D) -> HitInfo:
	if wall_parent == null:
		return null

	var origin := camera.project_ray_origin(screen_pos)
	var direction := camera.project_ray_normal(screen_pos)
	var max_dist := 1000.0

	var best_t := INF
	var best_hit: HitInfo = null

	for child in wall_parent.get_children():
		if not (child is StaticBody3D):
			continue
		var body: StaticBody3D = child

		# Wall half-extents in local space
		var half_len := WallHelper.get_wall_half_length(body)
		if half_len <= 0.0:
			continue

		var half_h := WallHelper.get_wall_height(body) * 0.5
		var half_t := WallHelper.get_wall_thickness(body) * 0.5

		# Transform ray to body local space
		var inv_transform := body.global_transform.affine_inverse()
		var local_origin := inv_transform * origin
		var local_dir := inv_transform.basis * direction  # direction only, no translation

		# Slab test against [-half_len..half_len, -half_h..half_h, -half_t..half_t]
		var t_range = _slab_test(local_origin, local_dir, Vector3(half_len, half_h, half_t))
		if t_range == null:
			continue

		var t_min: float = t_range.x
		var t_max: float = t_range.y
		if t_min < 0.0:
			t_min = t_max  # ray starts inside — use exit point
		if t_min < 0.0 or t_min > max_dist:
			continue

		if t_min < best_t:
			best_t = t_min
			best_hit = HitInfo.new(origin + direction * t_min, body)

	return best_hit


# ── Ray vs AABB slab test in local space ─────────────────────────────────────

## Returns Vector2(t_min, t_max) — entry/exit distances along the ray — or
## null when the ray misses the box of the given half extents.
static func _slab_test(origin: Vector3, dir: Vector3, half_extents: Vector3) -> Variant:
	var t_min := -INF
	var t_max := INF

	for axis in 3:
		var o := origin[axis]
		var d := dir[axis]
		var slab_min := -half_extents[axis]
		var slab_max := half_extents[axis]

		if is_zero_approx(d):
			# Ray is parallel to slab — check if origin is inside
			if o < slab_min or o > slab_max:
				return null
		else:
			var t1 := (slab_min - o) / d
			var t2 := (slab_max - o) / d
			if t1 > t2:
				var tmp := t1
				t1 = t2
				t2 = tmp
			t_min = maxf(t_min, t1)
			t_max = minf(t_max, t2)
			if t_max < t_min:
				return null

	return Vector2(t_min, t_max)
