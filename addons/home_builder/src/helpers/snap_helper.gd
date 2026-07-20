@tool
class_name SnapHelper
extends RefCounted


static func to_tile_center(hit: Vector3, floor_base_y: float) -> Vector3:
	return Vector3(floor(hit.x) + 0.5, floor_base_y - 0.05, floor(hit.z) + 0.5)


static func to_half_tile_center(hit: Vector3, floor_base_y: float) -> Vector3:
	return Vector3(
		floor(hit.x * 2.0) * 0.5 + 0.25,
		floor_base_y - 0.05,
		floor(hit.z * 2.0) * 0.5 + 0.25
	)


static func to_grid_corner(hit: Vector3, floor_base_y: float) -> Vector3:
	return Vector3(round(hit.x), floor_base_y, round(hit.z))


## Snaps a world-space hit on a wall to a half-metre step along the wall's
## local X axis, clamped so an opening of [param opening_width] stays inside
## the wall. Returns the snapped local X coordinate.
static func to_wall(wall_body: StaticBody3D, world_hit: Vector3, opening_width: float) -> float:
	var local_hit := wall_body.global_transform.affine_inverse() * world_hit

	# Read wall length — prefer stored metadata so this keeps working after
	# the first opening replaces BoxShape3D with ConcavePolygonShape3D.
	var wall_len := WallHelper.get_wall_length(wall_body)

	if wall_len == 0.0:
		return local_hit.x

	var half_len := wall_len * 0.5
	var snapped_x := floorf(local_hit.x) + 0.5
	return clampf(snapped_x, -half_len + opening_width * 0.5, half_len - opening_width * 0.5)


## Inclusive tile bounds of the rectangle spanned by two tile-center points.
## position = (min_x, min_z), size = (cols, rows).
static func grid_bounds(a: Vector3, b: Vector3) -> Rect2i:
	var x0 := roundi(a.x - 0.5)
	var z0 := roundi(a.z - 0.5)
	var x1 := roundi(b.x - 0.5)
	var z1 := roundi(b.z - 0.5)
	return Rect2i(mini(x0, x1), mini(z0, z1), absi(x1 - x0) + 1, absi(z1 - z0) + 1)


## Same as [method grid_bounds] but on the half-metre grid.
static func half_grid_bounds(a: Vector3, b: Vector3) -> Rect2i:
	var x0 := roundi(a.x * 2.0 - 0.5)
	var z0 := roundi(a.z * 2.0 - 0.5)
	var x1 := roundi(b.x * 2.0 - 0.5)
	var z1 := roundi(b.z * 2.0 - 0.5)
	return Rect2i(mini(x0, x1), mini(z0, z1), absi(x1 - x0) + 1, absi(z1 - z0) + 1)
