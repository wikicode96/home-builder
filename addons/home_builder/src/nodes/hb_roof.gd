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

## Gable ends continue the wall below them, so this defaults to the same
## thickness WallBuilder gives a wall.
const DEFAULT_END_THICKNESS := 0.1

@export var roof_type: RoofMeshBuilder.RoofType = RoofMeshBuilder.RoofType.FLAT:
	set(value):
		roof_type = value
		# Which properties are meaningful depends on the type — see
		# _validate_property — so the Inspector has to redraw its list.
		notify_property_list_changed()
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
## ridge rises by slightly more than this.
@export var thickness: float = DEFAULT_THICKNESS:
	set(value):
		thickness = value
		_rebuild()
## Thickness of the vertical end walls — SHED's back wall and side triangles,
## GABLE's two gable ends. Ignored by FLAT and HIP, which have none. Kept out
## of the dock because it should just match the walls below rather than be
## tuned per roof.
@export var end_thickness: float = DEFAULT_END_THICKNESS:
	set(value):
		end_thickness = value
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
@export var end_face_a_material: Material:
	set(value):
		end_face_a_material = value
		_apply_materials()
@export var end_face_b_material: Material:
	set(value):
		end_face_b_material = value
		_apply_materials()
@export var end_edges_material: Material:
	set(value):
		end_edges_material = value
		_apply_materials()

var _mesh: MeshInstance3D
var _shape: CollisionShape3D


## Hides the properties a given type ignores, so the Inspector doesn't offer
## knobs that do nothing — a gable-end material on a hip roof, a direction on
## a shape that is rotationally symmetric, a pitch on a flat roof. Uses
## PROPERTY_USAGE_NO_EDITOR rather than dropping the property outright, so a
## value set for one type survives switching away and back.
func _validate_property(property: Dictionary) -> void:
	var has_ends := (
		roof_type == RoofMeshBuilder.RoofType.SHED
		or roof_type == RoofMeshBuilder.RoofType.GABLE
	)
	var hide := false
	match property.name:
		"end_thickness", "end_face_a_material", "end_face_b_material", \
		"end_edges_material":
			hide = not has_ends
		"pitch":
			hide = roof_type == RoofMeshBuilder.RoofType.FLAT
		"direction":
			# Only SHED and GABLE have an orientation; FLAT is symmetric and
			# HIP orients itself along the longest side.
			hide = not has_ends
	if hide:
		property.usage = PROPERTY_USAGE_NO_EDITOR


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
		roof_type, width, depth, pitch, direction, eave, thickness, end_thickness)
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
	var mesh := _mesh.mesh
	if mesh == null:
		return
	# Which surfaces exist depends on the roof type — FLAT and HIP have no
	# ENDS — so walk the builder's layout instead of assuming fixed indices.
	var layout := RoofMeshBuilder.surface_layout(roof_type)
	for i in mini(layout.size(), mesh.get_surface_count()):
		var material: Material
		var fallback := _DEFAULT_FACE_TEXTURE
		match layout[i]:
			RoofMeshBuilder.Surface.TOP:
				material = top_material
			RoofMeshBuilder.Surface.BOTTOM:
				material = bottom_material
			RoofMeshBuilder.Surface.END_FACE_A:
				material = end_face_a_material
			RoofMeshBuilder.Surface.END_FACE_B:
				material = end_face_b_material
			RoofMeshBuilder.Surface.END_EDGES:
				material = end_edges_material
				fallback = _DEFAULT_EDGE_TEXTURE
			_:
				material = sides_material
				fallback = _DEFAULT_EDGE_TEXTURE
		# Non-triplanar: a roof is the one element whose faces are sloped, and
		# triplanar would blend two projections over each slope and show the
		# texture twice. RoofMeshBuilder emits metre-scaled UVs (V measured
		# along the slope) precisely so the mapping can come from them instead.
		_mesh.set_surface_override_material(i,
			MaterialHelper.or_default_textured(material, fallback, false))
