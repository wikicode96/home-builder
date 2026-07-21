@tool
class_name HBRoof
extends StaticBody3D

## Self-contained roof node. Owns its mesh and collision as INTERNAL children
## and regenerates them from the exported parameters, so the designer only ever
## sees a single "HBRoof_001" node.

@export var roof_type: RoofMeshBuilder.RoofType = RoofMeshBuilder.RoofType.FLAT:
	set(value):
		roof_type = value
		_rebuild()
@export var direction: RoofMeshBuilder.RoofDirection = RoofMeshBuilder.RoofDirection.NORTH:
	set(value):
		direction = value
		_rebuild()
@export var width: float = 1.0:
	set(value):
		width = value
		_rebuild()
@export var depth: float = 1.0:
	set(value):
		depth = value
		_rebuild()
@export var pitch: float = 1.5:
	set(value):
		pitch = value
		_rebuild()

@export_group("Materials")
@export var top_material: Material:
	set(value):
		top_material = value
		_apply_materials()
@export var bottom_material: Material:
	set(value):
		bottom_material = value
		_apply_materials()
@export var sides_material: Material:
	set(value):
		sides_material = value
		_apply_materials()

var _mesh: MeshInstance3D
var _shape: CollisionShape3D


func _ready() -> void:
	_rebuild()


func _rebuild() -> void:
	if not is_inside_tree():
		return
	_ensure_children()
	var mesh := RoofMeshBuilder.build(roof_type, width, depth, pitch, direction)
	_mesh.mesh = mesh
	_apply_materials()
	_shape.shape = mesh.create_trimesh_shape()


func _ensure_children() -> void:
	if _mesh != null and is_instance_valid(_mesh) \
			and _shape != null and is_instance_valid(_shape):
		return
	for c in get_children(true):
		if _mesh == null and c is MeshInstance3D:
			_mesh = c as MeshInstance3D
		elif _shape == null and c is CollisionShape3D:
			_shape = c as CollisionShape3D
	if _mesh == null:
		_mesh = MeshInstance3D.new()
		_mesh.name = "Mesh"
		add_child(_mesh, false, Node.INTERNAL_MODE_BACK)
	if _shape == null:
		_shape = CollisionShape3D.new()
		_shape.name = "Collision"
		add_child(_shape, false, Node.INTERNAL_MODE_BACK)


func _apply_materials() -> void:
	if _mesh == null or not is_instance_valid(_mesh):
		return
	_mesh.set_surface_override_material(RoofMeshBuilder.SURFACE_TOP,
		MaterialHelper.or_default(top_material, Color(0.65, 0.25, 0.2)))
	_mesh.set_surface_override_material(RoofMeshBuilder.SURFACE_BOTTOM,
		MaterialHelper.or_default(bottom_material, Color(0.5, 0.5, 0.5)))
	_mesh.set_surface_override_material(RoofMeshBuilder.SURFACE_SIDES,
		MaterialHelper.or_default(sides_material, Color(0.85, 0.82, 0.75)))
