@tool
class_name HBGizmoResizeMath
extends RefCounted

## Shared math for "one-sided resize" gizmo handles: dragging a handle grows
## or shrinks the node along one local axis while the OPPOSITE side (the
## anchor) stays fixed in world space, shifting the node's position to
## compensate. Used by HBWallGizmoPlugin, HBFloorSlabGizmoPlugin and
## HBRoofGizmoPlugin.
##
## Where the anchor sits, as a fraction of the current size along the axis,
## depends on how the node's mesh is built:
##   - centered nodes (e.g. HBWall/HBFloorSlab, which span -size/2..+size/2):
##     anchor_frac is -0.5 (dragging the + side) or +0.5 (dragging the - side)
##   - corner-anchored nodes (e.g. HBRoof, which spans 0..size):
##     anchor_frac is 0.0 (dragging the far side) or 1.0 (dragging the near side)


## World position of the fixed anchor point for the current size.
static func anchor_global(node_transform: Transform3D, axis: Vector3,
		anchor_frac: float, current_size: float) -> Vector3:
	return node_transform * (axis * anchor_frac * current_size)


## World direction the dragged handle moves in.
static func dir_global(node_basis: Basis, axis: Vector3, sign: float) -> Vector3:
	return (node_basis * (axis * sign)).normalized()


## Closest point on the anchor→dir ray to the mouse ray (both extended to
## infinity in their given directions) — i.e. where the dragged handle
## "wants" to be before any snapping.
static func closest_point_on_line(anchor: Vector3, dir: Vector3,
		ray_from: Vector3, ray_dir: Vector3) -> Vector3:
	var closest := Geometry3D.get_closest_points_between_segments(
		anchor, anchor + dir * 4096.0,
		ray_from, ray_from + ray_dir * 4096.0
	)
	return closest[0]


## Distance from the anchor to the dragged point along dir — free dragging,
## no snapping — clamped to min_size.
static func drag_size(anchor: Vector3, dir: Vector3,
		ray_from: Vector3, ray_dir: Vector3, min_size: float) -> float:
	var point := closest_point_on_line(anchor, dir, ray_from, ray_dir)
	return maxf((point - anchor).dot(dir), min_size)


## Same as drag_size, but snaps the dragged point to the world grid — each
## axis rounded to the nearest multiple of step — BEFORE measuring its
## distance from the anchor, rather than snapping the distance itself.
##
## Snapping the distance directly only lands the dragged end on an actual
## grid intersection when dir is axis-aligned; for a diagonal wall or roof
## (dir with more than one non-zero component) the endpoint would drift off
## the grid by however non-round dir's components are. Snapping the point
## first and projecting back is exact for axis-aligned and 45° directions
## (the two angles this addon's builders actually place diagonals at — see
## WallBuilder.place_wall, which allows any two grid corners) and a
## reasonable best effort for any other angle.
static func drag_size_snapped(anchor: Vector3, dir: Vector3,
		ray_from: Vector3, ray_dir: Vector3, step: float, min_size: float) -> float:
	var point := closest_point_on_line(anchor, dir, ray_from, ray_dir)
	var snapped_point := Vector3(snap(point.x, step), snap(point.y, step), snap(point.z, step))
	return maxf((snapped_point - anchor).dot(dir), min_size)


## New node position that keeps the anchor fixed in world space once the
## axis's size becomes new_size.
static func node_position(anchor: Vector3, dir: Vector3,
		sign: float, anchor_frac: float, new_size: float) -> Vector3:
	return anchor - dir * (sign * anchor_frac * new_size)


## Rounds a value to the nearest multiple of step. step <= 0 disables
## snapping (returns value unchanged).
static func snap(value: float, step: float) -> float:
	if step <= 0.0:
		return value
	return roundf(value / step) * step
