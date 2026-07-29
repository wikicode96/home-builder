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

## Tags each door instance with the `openings` index it belongs to (saved with
## the scene) so _sync_doors can find/replace the right child instead of
## matching by name or position.
const _META_OPENING_INDEX := "hb_opening_index"

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

## Door scene placed in each opening, parallel to `openings` by index (null
## for windows, or a door opening with no asset assigned yet). Reassigning an
## entry from the Inspector — e.g. to re-skin every door in the project by
## dropping in a new scene — swaps that opening's instance in place without
## touching the cut-out geometry.
@export var door_scenes: Array[PackedScene] = []:
	set(value):
		door_scenes = value
		_sync_doors()

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
	# "_edit_group_" is the same metadata the Scene dock's Group toggle sets.
	# It makes the 3D viewport escalate a click anywhere on our internal
	# Mesh/Collision children up to this node, so the wall — not its hidden
	# internals — ends up selected (and shown in the Inspector).
	if Engine.is_editor_hint():
		set_meta("_edit_group_", true)
	_rebuild()
	_sync_doors()


# ── Public API (used by the builders) ────────────────────────────────────────

## Replaces the mitre offsets and rebuilds. Called by WallBuilder after the
## junction solver runs for this wall's parent.
func set_join_offsets(joins: WallMeshBuilder.JoinOffsets) -> void:
	set_meta(_META_JOINS, PackedFloat32Array([
		joins.start_minus_z, joins.start_plus_z,
		joins.end_minus_z, joins.end_plus_z,
		1.0 if joins.has_start_wedge else 0.0,
		joins.start_wedge.x, joins.start_wedge.y,
		1.0 if joins.has_end_wedge else 0.0,
		joins.end_wedge.x, joins.end_wedge.y,
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


## Assigns (or clears, with null) the door scene for the opening at [param
## index], growing door_scenes as needed, and syncs the instance immediately.
## Called by OpeningBuilder right after cutting a new door opening; also safe
## to call from a batch/re-skin script to swap every door in a wall at once.
func set_door_scene(index: int, scene: PackedScene) -> void:
	while door_scenes.size() <= index:
		door_scenes.append(null)
	door_scenes[index] = scene
	_sync_doors()


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

	# Internal children default to owner = null, which stops the editor's 3D
	# viewport click-picking from bubbling up to this node (it only walks the
	# parent chain for owned nodes) — clicking the wall's mesh would then do
	# nothing, even though selecting HBWall_001 from the Scene dock works fine.
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
	_mesh.set_surface_override_material(WallMeshBuilder.SURFACE_FACE_A,
		MaterialHelper.or_default_textured(face_a_material, _DEFAULT_FACE_TEXTURE))
	_mesh.set_surface_override_material(WallMeshBuilder.SURFACE_FACE_B,
		MaterialHelper.or_default_textured(face_b_material, _DEFAULT_FACE_TEXTURE))
	_mesh.set_surface_override_material(WallMeshBuilder.SURFACE_EDGES,
		MaterialHelper.or_default_textured(edges_material, _DEFAULT_EDGE_TEXTURE))


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


# ── Door instances ───────────────────────────────────────────────────────────

## Keeps the actual child instances in sync with door_scenes: creates the
## ones that are missing, replaces the ones whose assigned scene changed, and
## removes the ones reassigned to null. Instances are regular (non-internal,
## owned) children — unlike Mesh/Collision — so each one can still be
## selected and tweaked in the Scene dock (e.g. Door.cs's hinge/angle).
func _sync_doors() -> void:
	if not is_inside_tree():
		return

	for i in openings.size():
		var desired: PackedScene = door_scenes[i] if i < door_scenes.size() else null
		var existing := _find_door_instance(i)

		if existing != null and (desired == null or existing.scene_file_path != desired.resource_path):
			existing.queue_free()
			existing = null

		if desired != null and existing == null:
			_instantiate_door(i, desired)


func _find_door_instance(index: int) -> Node3D:
	for child in get_children():
		if child.has_meta(_META_OPENING_INDEX) and child.get_meta(_META_OPENING_INDEX) == index:
			return child as Node3D
	return null


func _instantiate_door(index: int, scene: PackedScene) -> void:
	var instance := scene.instantiate() as Node3D
	if instance == null:
		push_warning("[HomeBuilder] Door asset scene root is not a Node3D — skipped.")
		return

	var op := WallMeshBuilder.Opening.new(
		openings[index]["cx"], openings[index]["w"], openings[index]["by"], openings[index]["h"])
	var local_y := op.bottom_y - height * 0.5

	instance.name = "Door_%d" % index
	instance.set_meta(_META_OPENING_INDEX, index)

	add_child(instance, false)
	if Engine.is_editor_hint():
		var root := get_tree().edited_scene_root
		if root:
			instance.owner = root

	instance.position = Vector3(op.center_x, local_y, 0.0)
	instance.basis = Basis.IDENTITY


func _joins_from_meta() -> WallMeshBuilder.JoinOffsets:
	if not has_meta(_META_JOINS):
		return WallMeshBuilder.JoinOffsets.new()
	var j: PackedFloat32Array = get_meta(_META_JOINS)
	if j.size() < 4:
		return WallMeshBuilder.JoinOffsets.new()
	# Scenes saved before junction wedges existed only have the first 4
	# scalars — read them with no wedge rather than treat the whole array
	# as invalid; the next rebuild_junctions() pass fills the rest in.
	if j.size() < 10:
		return WallMeshBuilder.JoinOffsets.new(j[0], j[1], j[2], j[3])
	return WallMeshBuilder.JoinOffsets.new(j[0], j[1], j[2], j[3],
		j[4] != 0.0, Vector2(j[5], j[6]),
		j[7] != 0.0, Vector2(j[8], j[9]))
