@tool
class_name HBFloorSlab
extends StaticBody3D

## Self-contained floor-slab node. Like HBWall, it owns its mesh and collision
## as INTERNAL children and regenerates them from the exported parameters, so
## the designer only ever sees a single "HBFloorSlab_001" node.

## Footprint size in metres. Normally kept to whole numbers by the gizmo
## (FloorMeshBuilder tiles the top/bottom UVs per metre), but stored as float
## so the gizmo's free-drag mode (hold Ctrl) can size it continuously too.
@export var cols: float = 1.0:
	set(value):
		cols = value
		_rebuild()
@export var rows: float = 1.0:
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
	# "_edit_group_" is the same metadata the Scene dock's Group toggle sets.
	# It makes the 3D viewport escalate a click anywhere on our internal
	# Mesh/Collision children up to this node, so the slab — not its hidden
	# internals — ends up selected (and shown in the Inspector).
	if Engine.is_editor_hint():
		set_meta("_edit_group_", true)
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

	# Owner is required for the internal children to register a gizmo at all
	# (unowned nodes are invisible to the viewport's click-picking); "_edit_group_"
	# on this node (see _ready) then escalates the resulting click up to us.
	if Engine.is_editor_hint() and is_inside_tree():
		var root := get_tree().edited_scene_root
		if root:
			_mesh.owner = root
			_shape.owner = root


const _DEFAULT_FACE_TEXTURE := "res://addons/home_builder/textures/white.png"
const _DEFAULT_EDGE_TEXTURE := "res://addons/home_builder/textures/red.png"


func _apply_materials() -> void:
	if _mesh == null or not is_instance_valid(_mesh):
		return
	_mesh.set_surface_override_material(FloorMeshBuilder.SURFACE_TOP,
		MaterialHelper.or_default_textured(top_material, _DEFAULT_FACE_TEXTURE))
	_mesh.set_surface_override_material(FloorMeshBuilder.SURFACE_BOTTOM,
		MaterialHelper.or_default_textured(bottom_material, _DEFAULT_FACE_TEXTURE))
	_mesh.set_surface_override_material(FloorMeshBuilder.SURFACE_SIDES,
		MaterialHelper.or_default_textured(sides_material, _DEFAULT_EDGE_TEXTURE))
