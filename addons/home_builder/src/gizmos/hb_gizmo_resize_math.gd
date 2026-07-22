@tool
class_name HBGizmoResizeMath
extends RefCounted

## Shared math for "one-sided resize" gizmo handles: dragging a handle grows
## or shrinks the node along one local axis while the OPPOSITE side (the
## anchor) stays fixed in world space, shifting the node's position to
## compensate. Used by HBFloorSlabGizmoPlugin and HBRoofGizmoPlugin.
##
## Where the anchor sits, as a fraction of the current size along the axis,
## depends on how the node's mesh is built:
##   - centered nodes (e.g. HBFloorSlab, which spans -size/2..+size/2):
##     anchor_frac is -0.5 (dragging the + side) or +0.5 (dragging the - side)
##   - corner-anchored nodes (e.g. HBRoof, which spans 0..size):
##     anchor_frac is 0.0 (dragging the far side) or 1.0 (dragging the near side)
##
## HBWallGizmoPlugin predates this helper and keeps its own inline copy of
## the same math; no need to touch it.


## World position of the fixed anchor point for the current size.
static func anchor_global(node_transform: Transform3D, axis: Vector3,
		anchor_frac: float, current_size: float) -> Vector3:
	return node_transform * (axis * anchor_frac * current_size)


## World direction the dragged handle moves in.
static func dir_global(node_basis: Basis, axis: Vector3, sign: float) -> Vector3:
	return (node_basis * (axis * sign)).normalized()


## Projects the mouse ray onto the anchor→direction line and returns the new
## size (distance from the anchor to the dragged point), clamped to min_size.
static func drag_size(anchor: Vector3, dir: Vector3,
		ray_from: Vector3, ray_dir: Vector3, min_size: float) -> float:
	var closest := Geometry3D.get_closest_points_between_segments(
		anchor, anchor + dir * 4096.0,
		ray_from, ray_from + ray_dir * 4096.0
	)
	var t: float = (closest[0] - anchor).dot(dir)
	return maxf(t, min_size)


## New node position that keeps the anchor fixed in world space once the
## axis's size becomes new_size.
static func node_position(anchor: Vector3, dir: Vector3,
		sign: float, anchor_frac: float, new_size: float) -> Vector3:
	return anchor - dir * (sign * anchor_frac * new_size)


## Rounds a value to the nearest multiple of step. step <= 0 disables
## snapping (returns value unchanged). Used to implement the Ctrl-held
## grid-snap toggle each gizmo plugin offers on top of its default behaviour.
static func snap(value: float, step: float) -> float:
	if step <= 0.0:
		return value
	return roundf(value / step) * step
