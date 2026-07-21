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
		# run_offset: step centre along local Z; the -0.5 aligns the back edge
		# with the tile edge. rise_offset centres each step vertically.
		var step := MeshInstance3D.new()
		step.name = "Step_%d" % (i + 1)
		step.mesh = step_mesh
		step.position = Vector3(0.0, (i + 0.5) * rise, i * run + run * 0.5 - 0.5)
		_apply_step_materials(step)
		add_child(step, false, Node.INTERNAL_MODE_BACK)


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
		Vector3(-half_w, height, tr - 0.75),
		Vector3(half_w, height, tr - 0.75),
		Vector3(-half_w, height, tr - 0.5),
		Vector3(half_w, height, tr - 0.5),
		Vector3(-half_w, 0.0, -0.5),
		Vector3(half_w, 0.0, -0.5),
		Vector3(-half_w, 0.0, -0.75),
		Vector3(half_w, 0.0, -0.75),
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


# ── Materials ────────────────────────────────────────────────────────────────

func _apply_materials() -> void:
	for step in _existing_steps():
		_apply_step_materials(step)


func _apply_step_materials(step: MeshInstance3D) -> void:
	step.set_surface_override_material(StairsMeshBuilder.SURFACE_TOP,
		MaterialHelper.or_default(top_material, Color(0.8, 0.7, 0.5)))
	step.set_surface_override_material(StairsMeshBuilder.SURFACE_BOTTOM,
		MaterialHelper.or_default(bottom_material, Color(0.6, 0.6, 0.6)))
	step.set_surface_override_material(StairsMeshBuilder.SURFACE_SIDES,
		MaterialHelper.or_default(sides_material, Color(0.5, 0.5, 0.5)))
