@tool
class_name HBFloorSlab
extends StaticBody3D

## Self-contained floor-slab node. Like HBWall, it owns its mesh and collision
## as INTERNAL children and regenerates them from the exported parameters, so
## the designer only ever sees a single "HBFloorSlab_001" node.

@export var cols: int = 1:
	set(value):
		cols = value
		_rebuild()
@export var rows: int = 1:
	set(value):
		rows = value
		_rebuild()
## Collision-box thickness. The visual mesh keeps FloorMeshBuilder's fixed
## 0.1 m height; this only drives the physics box (matching the old builder).
@export var thickness: float = 0.1:
	set(value):
		thickness = value
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
	_mesh.mesh = FloorMeshBuilder.build(cols, rows)
	_apply_materials()
	var box := BoxShape3D.new()
	box.size = Vector3(cols, thickness, rows)
	_shape.shape = box


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
	_mesh.set_surface_override_material(FloorMeshBuilder.SURFACE_TOP,
		MaterialHelper.or_default(top_material, Color(0.8, 0.7, 0.5)))
	_mesh.set_surface_override_material(FloorMeshBuilder.SURFACE_BOTTOM,
		MaterialHelper.or_default(bottom_material, Color(0.6, 0.6, 0.6)))
	_mesh.set_surface_override_material(FloorMeshBuilder.SURFACE_SIDES,
		MaterialHelper.or_default(sides_material, Color(0.5, 0.5, 0.5)))
