@tool
class_name MaterialHelper
extends RefCounted

## Fallback materials for surfaces the user has not configured in the dock.
## Creating one per placement is cheap for an editor tool, so no cache is kept.


static func make_default_material(color: Color) -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.albedo_color = color
	return material


## Returns [param material], or a default material tinted [param fallback_color]
## when it is null.
static func or_default(material: Material, fallback_color: Color) -> Material:
	return material if material != null else make_default_material(fallback_color)


# One StandardMaterial3D per texture path *and* mapping mode, reused across
# placements instead of reloading the texture and allocating a new material
# every rebuild.
static var _textured_material_cache: Dictionary[String, StandardMaterial3D] = {}


## Triplanar projects the texture down all three world axes and blends the
## three by the normal, which is what gives every builder a metre-scaled look
## without them having to agree on a UV convention (walls and stairs emit 0..1,
## floors emit grid cells, roofs emit metres).
##
## The blend is only invisible while a face is roughly axis-aligned, i.e. while
## one projection dominates. On a slope two projections come through at
## comparable weight and the texture reads twice, offset and at different
## angles — so anything with sloped faces must pass [param triplanar] = false
## and carry its own metre-scaled UVs instead.
static func make_textured_material(texture_path: String,
		triplanar: bool = true) -> StandardMaterial3D:
	var key := texture_path if triplanar else texture_path + "#uv"
	if not _textured_material_cache.has(key):
		var material := StandardMaterial3D.new()
		material.albedo_texture = load(texture_path)
		if triplanar:
			material.uv1_triplanar = true
			# Centres the projection on the world origin, so a 1 m texture lands
			# on the grid lines rather than straddling them.
			material.uv1_offset.x = 0.5
			material.uv1_offset.y = 0.5
		_textured_material_cache[key] = material
	return _textured_material_cache[key]


## Returns [param material], or a default material textured with
## [param fallback_texture_path] when it is null.
static func or_default_textured(material: Material, fallback_texture_path: String,
		triplanar: bool = true) -> Material:
	return material if material != null \
		else make_textured_material(fallback_texture_path, triplanar)
