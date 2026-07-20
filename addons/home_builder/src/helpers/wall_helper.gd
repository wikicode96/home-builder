@tool
class_name WallHelper
extends RefCounted

## Metadata keys written by WallBuilder when the wall is first created.
## They are read here because after the first opening the CollisionShape3D
## becomes a ConcavePolygonShape3D and BoxShape3D is no longer available.
const META_WALL_LENGTH := "hb_wall_length"
const META_WALL_THICKNESS := "hb_wall_thickness"
const META_WALL_HEIGHT := "hb_wall_height"


static func get_wall_length(wall_body: StaticBody3D) -> float:
	# Preferred: stored metadata (survives collision shape replacement).
	if wall_body.has_meta(META_WALL_LENGTH):
		return float(wall_body.get_meta(META_WALL_LENGTH))

	# Fallback: read from BoxShape3D (only valid before the first opening).
	for child in wall_body.get_children():
		if child is CollisionShape3D and child.shape is BoxShape3D:
			return child.shape.size.x
	return 0.0


static func get_wall_half_length(body: StaticBody3D) -> float:
	return get_wall_length(body) * 0.5


## Per-wall thickness and height — frozen at placement time so changing the
## dock value afterwards does not rewrite walls that were built earlier.
static func get_wall_thickness(wall_body: StaticBody3D) -> float:
	if wall_body != null and wall_body.has_meta(META_WALL_THICKNESS):
		return float(wall_body.get_meta(META_WALL_THICKNESS))
	return WallBuilder.thickness


static func get_wall_height(wall_body: StaticBody3D) -> float:
	if wall_body != null and wall_body.has_meta(META_WALL_HEIGHT):
		return float(wall_body.get_meta(META_WALL_HEIGHT))
	return WallBuilder.height
