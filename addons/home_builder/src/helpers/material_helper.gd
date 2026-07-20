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
