@tool
class_name SnapHelper
extends RefCounted

## Single source of truth for the world grid's cell size. Anything that needs
## to know "how big is a grid tile" (builders, ghost previews) should read
## this instead of hardcoding 0.5 — that drift is exactly what caused stale
## grid constants in HBStairs and the floor ghost preview before.
const GRID_STEP := 0.5


## Centre of the 0.5m grid cell hit lands in.
static func to_tile_center(hit: Vector3, floor_base_y: float) -> Vector3:
	return Vector3(
		floor(hit.x * 2.0) * 0.5 + 0.25,
		floor_base_y - 0.05,
		floor(hit.z * 2.0) * 0.5 + 0.25
	)


## Nearest 0.5m grid corner to hit.
static func to_grid_corner(hit: Vector3, floor_base_y: float) -> Vector3:
	return Vector3(round(hit.x * 2.0) * 0.5, floor_base_y, round(hit.z * 2.0) * 0.5)


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


## Inclusive bounds of the rectangle spanned by two tile-center points, in
## HALF-METRE units (multiply position/size by 0.5 to get world metres) —
## Rect2i can't hold the 0.5 fractions directly, so callers do that scaling
## themselves (see RoofBuilder/FloorBuilder).
static func grid_bounds(a: Vector3, b: Vector3) -> Rect2i:
	var x0 := roundi(a.x * 2.0 - 0.5)
	var z0 := roundi(a.z * 2.0 - 0.5)
	var x1 := roundi(b.x * 2.0 - 0.5)
	var z1 := roundi(b.z * 2.0 - 0.5)
	return Rect2i(mini(x0, x1), mini(z0, z1), absi(x1 - x0) + 1, absi(z1 - z0) + 1)
