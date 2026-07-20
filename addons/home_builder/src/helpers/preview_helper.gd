@tool
class_name PreviewHelper
extends RefCounted


static func create_marker(scene: Node3D, marker_name: String, size: Vector3,
		color: Color, position: Vector3) -> CSGBox3D:
	if scene == null:
		return null

	var marker := CSGBox3D.new()
	marker.name = marker_name
	marker.size = size
	marker.position = position
	marker.material_override = make_material(color)
	# Disable collision so the marker never intercepts raycasts
	# (either physics-based or geometric OBB tests).
	marker.use_collision = false
	scene.add_child(marker)
	return marker


## Frees the marker immediately if it is still valid. The caller is
## responsible for clearing its own reference afterwards.
static func free_marker(marker: Node) -> void:
	if marker != null and is_instance_valid(marker):
		marker.free()


static func make_material(color: Color) -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	material.albedo_color = color
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	return material
