@tool
class_name HBRoof
extends StaticBody3D

## Self-contained roof node. Owns its mesh and collision as INTERNAL children
## and regenerates them from the exported parameters, so the designer only ever
## sees a single "HBRoof_001" node.

## Typical residential eave overhang is 0.3–0.6 m; 0.4 reads well at the
## addon's 0.5 m grid without swallowing a whole tile.
const DEFAULT_EAVE := 0.4

## Roof build-up (rafters, boarding, tiles) is roughly 0.2–0.35 m in reality.
## 0.2 gives the eave a visible fascia without looking like a slab.
const DEFAULT_THICKNESS := 0.2

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
## Eave overhang, in metres: how far the slope faces are prolonged past the
## footprint. Independent of width/depth/pitch — resizing the roof with the
## gizmo re-generates the same overhang off the new edge. 0 = flush with the
## footprint (the pre-eave behaviour). Only HIP honours it for now.
@export var eave: float = DEFAULT_EAVE:
	set(value):
		eave = value
		_rebuild()
## Roof thickness, in metres, measured perpendicular to the slope. The slope
## faces become the underside and a second shell is raised above them, so the
## ridge rises by slightly more than this. Only HIP honours it for now.
@export var thickness: float = DEFAULT_THICKNESS:
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
	# Mesh/Collision children up to this node, so the roof — not its hidden
	# internals — ends up selected (and shown in the Inspector).
	if Engine.is_editor_hint():
		set_meta("_edit_group_", true)
	_rebuild()


func _rebuild() -> void:
	if not is_inside_tree():
		return
	_ensure_children()
	var mesh := RoofMeshBuilder.build(
		roof_type, width, depth, pitch, direction, eave, thickness)
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
	_mesh.set_surface_override_material(RoofMeshBuilder.SURFACE_TOP,
		MaterialHelper.or_default_textured(top_material, _DEFAULT_FACE_TEXTURE))
	_mesh.set_surface_override_material(RoofMeshBuilder.SURFACE_BOTTOM,
		MaterialHelper.or_default_textured(bottom_material, _DEFAULT_FACE_TEXTURE))
	_mesh.set_surface_override_material(RoofMeshBuilder.SURFACE_SIDES,
		MaterialHelper.or_default_textured(sides_material, _DEFAULT_EDGE_TEXTURE))
