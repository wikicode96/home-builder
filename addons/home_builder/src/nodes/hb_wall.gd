@tool
class_name HBWall
extends StaticBody3D

## Self-contained wall node.
##
## A wall is a single StaticBody3D that owns its visual mesh and collision
## shape as INTERNAL children. Internal children never appear in the Scene
## dock and are never written to the .tscn, so the level designer only ever
## sees and edits one clean `HBWall_001` node. The geometry is regenerated
## from the exported parameters below whenever the wall enters the tree
## (scene load, undo/redo, duplication) or one of its properties changes.
##
## Because the mesh is rebuilt from parameters, the .tscn only stores the
## wall's description (length, height, thickness, openings, materials) — not
## a baked ArrayMesh — which keeps scenes small and diffs readable.

## Miter offsets are persisted as node metadata (saved with the scene, hidden
## from the Inspector) so a wall rebuilds with correct corners on load without
## needing to re-solve against its neighbours first.
const _META_JOINS := "hb_joins"

@export var length: float = 3.0:
	set(value):
		length = value
		_rebuild()
@export var height: float = 3.0:
	set(value):
		height = value
		_rebuild()
@export var thickness: float = 0.1:
	set(value):
		thickness = value
		_rebuild()

## Door/window cut-outs. Each entry is {cx, w, by, h} in the wall's local
## frame (see WallMeshBuilder.Opening). Stored as plain dictionaries so the
## list serialises without a custom Resource.
@export var openings: Array[Dictionary] = []:
	set(value):
		openings = value
		_rebuild()

@export_group("Materials")
@export var face_a_material: Material:
	set(value):
		face_a_material = value
		_apply_materials()
@export var face_b_material: Material:
	set(value):
		face_b_material = value
		_apply_materials()
@export var edges_material: Material:
	set(value):
		edges_material = value
		_apply_materials()

# Internal children — created lazily, never saved. Kept as references so we
# can refresh them in place instead of rebuilding the subtree on every edit.
var _mesh: MeshInstance3D
var _shape: CollisionShape3D


func _ready() -> void:
	_rebuild()


# ── Public API (used by the builders) ────────────────────────────────────────

## Replaces the mitre offsets and rebuilds. Called by WallBuilder after the
## junction solver runs for this wall's parent.
func set_join_offsets(joins: WallMeshBuilder.JoinOffsets) -> void:
	set_meta(_META_JOINS, PackedFloat32Array([
		joins.start_minus_z, joins.start_plus_z,
		joins.end_minus_z, joins.end_plus_z,
	]))
	_rebuild()


## Returns the current openings as WallMeshBuilder.Opening objects.
func get_openings() -> Array:
	var result: Array = []
	for d in openings:
		result.append(WallMeshBuilder.Opening.new(d["cx"], d["w"], d["by"], d["h"]))
	return result


## Appends one opening and rebuilds. The caller is responsible for overlap
## and bounds validation (see OpeningBuilder).
func add_opening(op: WallMeshBuilder.Opening) -> void:
	openings.append({"cx": op.center_x, "w": op.width, "by": op.bottom_y, "h": op.height})
	_rebuild()


## Edge material, or null — used by WallBuilder to tint junction-fill meshes
## with the same material as the walls that meet there.
func get_edge_material() -> Material:
	return edges_material


# ── Rebuild ──────────────────────────────────────────────────────────────────

func _rebuild() -> void:
	# Property setters fire while the node is being configured before it enters
	# the tree; defer the actual build to _ready() in that case.
	if not is_inside_tree():
		return

	_ensure_children()

	var ops := get_openings()
	var joins := _joins_from_meta()
	var mesh := WallMeshBuilder.build_with_openings_and_joins(
		length, height, thickness, ops, joins)

	_mesh.mesh = mesh
	_apply_materials()
	_rebuild_collision(mesh)


func _ensure_children() -> void:
	if _mesh != null and is_instance_valid(_mesh) \
			and _shape != null and is_instance_valid(_shape):
		return

	# Re-adopt existing internal children (e.g. after an editor duplicate copies
	# them) instead of creating a second pair.
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
	_mesh.set_surface_override_material(WallMeshBuilder.SURFACE_FACE_A,
		MaterialHelper.or_default(face_a_material, Color(0.9, 0.9, 0.85)))
	_mesh.set_surface_override_material(WallMeshBuilder.SURFACE_FACE_B,
		MaterialHelper.or_default(face_b_material, Color(0.85, 0.85, 0.8)))
	_mesh.set_surface_override_material(WallMeshBuilder.SURFACE_EDGES,
		MaterialHelper.or_default(edges_material, Color(0.7, 0.7, 0.65)))


## No openings → an exact BoxShape3D (cheap). With openings → a
## ConcavePolygonShape3D built from the mesh so the holes are real.
func _rebuild_collision(mesh: ArrayMesh) -> void:
	if _shape == null or not is_instance_valid(_shape):
		return

	if openings.is_empty():
		var box := BoxShape3D.new()
		box.size = Vector3(length, height, thickness)
		_shape.shape = box
		return

	var vertices := PackedVector3Array()
	for i in mesh.get_surface_count():
		var arrays := mesh.surface_get_arrays(i)
		if arrays.is_empty():
			continue
		vertices.append_array(arrays[Mesh.ARRAY_VERTEX])
	if vertices.is_empty():
		return

	var concave := ConcavePolygonShape3D.new()
	concave.set_faces(vertices)
	_shape.shape = concave


func _joins_from_meta() -> WallMeshBuilder.JoinOffsets:
	if not has_meta(_META_JOINS):
		return WallMeshBuilder.JoinOffsets.new()
	var j: PackedFloat32Array = get_meta(_META_JOINS)
	if j.size() < 4:
		return WallMeshBuilder.JoinOffsets.new()
	return WallMeshBuilder.JoinOffsets.new(j[0], j[1], j[2], j[3])
