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


# One StandardMaterial3D per texture path, reused across placements instead
# of reloading the texture and allocating a new material every rebuild.
static var _textured_material_cache: Dictionary[String, StandardMaterial3D] = {}


static func make_textured_material(texture_path: String) -> StandardMaterial3D:
	if not _textured_material_cache.has(texture_path):
		var material := StandardMaterial3D.new()
		material.albedo_texture = load(texture_path)
		material.uv1_triplanar = true
		material.uv1_offset.x = 0.5
		material.uv1_offset.y = 0.5
		_textured_material_cache[texture_path] = material
	return _textured_material_cache[texture_path]


## Returns [param material], or a default material textured with
## [param fallback_texture_path] when it is null.
static func or_default_textured(material: Material, fallback_texture_path: String) -> Material:
	return material if material != null else make_textured_material(fallback_texture_path)
