@tool
class_name HBStairs
extends StaticBody3D

## Self-contained staircase node. Owns every step MeshInstance3D plus a single
## ramp ConvexPolygonShape3D as INTERNAL children, regenerated from the exported
## parameters, so the designer only ever sees one "HBStairs_001" node.
##
## The ramp collision is intentionally a ConvexPolygonShape3D (not a per-step
## shape): a CharacterBody3D slides up it smoothly, and BakeBuilder keeps it as
## a separate convex shape for the same reason.

@export var count: int = 12:
	set(value):
		count = value
		_rebuild()
@export var width: float = 1.0:
	set(value):
		width = value
		_rebuild()
@export var run: float = 0.25:
	set(value):
		run = value
		_rebuild()
## Total climbed height across all steps; per-step rise = height / count.
@export var height: float = 3.0:
	set(value):
		height = value
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

var _shape: CollisionShape3D


func _ready() -> void:
	# "_edit_group_" is the same metadata the Scene dock's Group toggle sets.
	# It makes the 3D viewport escalate a click anywhere on our internal step
	# meshes/collision up to this node, so the staircase — not one of its
	# hidden steps — ends up selected (and shown in the Inspector).
	if Engine.is_editor_hint():
		set_meta("_edit_group_", true)
	_rebuild()


func _rebuild() -> void:
	if not is_inside_tree() or count <= 0:
		return
	_rebuild_steps()
	_rebuild_collision()


# ── Steps ────────────────────────────────────────────────────────────────────

func _rebuild_steps() -> void:
	for step in _existing_steps():
		remove_child(step)
		step.free()

	var rise := height / count
	var step_mesh := StairsMeshBuilder.build(width, rise, run)
	for i in count:
		# Local Z=0 is the back edge of the first step — the staircase's own
		# geometric start, with no grid-alignment offset baked in. Snapping
		# the placement point to a tile edge is StairsBuilder's job, not
		# this node's (see its _place_stairs/_update_ghost).
		var step := MeshInstance3D.new()
		step.name = "Step_%d" % (i + 1)
		step.mesh = step_mesh
		step.position = Vector3(0.0, (i + 0.5) * rise, i * run + run * 0.5)
		_apply_step_materials(step)
		add_child(step, false, Node.INTERNAL_MODE_BACK)
		_own(step)


func _existing_steps() -> Array:
	var result: Array = []
	for c in get_children(true):
		if c is MeshInstance3D:
			result.append(c)
	return result


# ── Collision — one convex ramp wedge ────────────────────────────────────────

func _rebuild_collision() -> void:
	_ensure_collision()
	var half_w := width * 0.5
	var tr := count * run
	var convex := ConvexPolygonShape3D.new()
	convex.points = PackedVector3Array([
		Vector3(-half_w, height, tr - 0.25),
		Vector3(half_w, height, tr - 0.25),
		Vector3(-half_w, height, tr),
		Vector3(half_w, height, tr),
		Vector3(-half_w, 0.0, 0.0),
		Vector3(half_w, 0.0, 0.0),
		Vector3(-half_w, 0.0, -0.25),
		Vector3(half_w, 0.0, -0.25),
	])
	_shape.shape = convex


func _ensure_collision() -> void:
	if _shape != null and is_instance_valid(_shape):
		return
	for c in get_children(true):
		if c is CollisionShape3D:
			_shape = c as CollisionShape3D
			return
	_shape = CollisionShape3D.new()
	_shape.name = "Collision"
	add_child(_shape, false, Node.INTERNAL_MODE_BACK)
	_own(_shape)


# Owner is required for an internal child to register a gizmo at all (unowned
# nodes are invisible to the viewport's click-picking); "_edit_group_" on this
# node (see _ready) then escalates the resulting click up to us.
func _own(node: Node) -> void:
	if Engine.is_editor_hint() and is_inside_tree():
		var root := get_tree().edited_scene_root
		if root:
			node.owner = root


# ── Materials ────────────────────────────────────────────────────────────────

func _apply_materials() -> void:
	for step in _existing_steps():
		_apply_step_materials(step)


const _DEFAULT_FACE_TEXTURE := "res://addons/home_builder/textures/white.png"
const _DEFAULT_EDGE_TEXTURE := "res://addons/home_builder/textures/red.png"


func _apply_step_materials(step: MeshInstance3D) -> void:
	step.set_surface_override_material(StairsMeshBuilder.SURFACE_TOP,
		MaterialHelper.or_default_textured(top_material, _DEFAULT_FACE_TEXTURE))
	step.set_surface_override_material(StairsMeshBuilder.SURFACE_BOTTOM,
		MaterialHelper.or_default_textured(bottom_material, _DEFAULT_FACE_TEXTURE))
	step.set_surface_override_material(StairsMeshBuilder.SURFACE_SIDES,
		MaterialHelper.or_default_textured(sides_material, _DEFAULT_EDGE_TEXTURE))
