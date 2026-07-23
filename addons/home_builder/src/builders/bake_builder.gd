@tool
class_name BakeBuilder
extends RefCounted

# Untyped: typing it as the plugin class would create a cyclic class_name
# reference between the plugin script and every builder.
var _plugin


func _init(plugin) -> void:
	_plugin = plugin


## [param fade_mode] is a GeometryInstance3D.VisibilityRangeFadeMode value.
func bake(scene_root: Node3D, output_folder: String,
		lod0_end: float, lod1_begin: float, fade_mode: int,
		build_nav_mesh: bool = true, build_occlusion: bool = true) -> void:
	if scene_root == null:
		push_error("[BakeBuilder] No hay escena activa")
		return
	if output_folder.is_empty():
		push_error("[BakeBuilder] Carpeta de salida vacía")
		return
	if not output_folder.begins_with("res://"):
		push_error("[BakeBuilder] La carpeta debe ser una ruta res://")
		return

	var mesh_instances: Array[MeshInstance3D] = []
	var convex_shapes: Array[CollisionShape3D] = []
	_collect_geometry(scene_root, mesh_instances, convex_shapes)

	if mesh_instances.is_empty():
		push_error("[BakeBuilder] No se han encontrado meshes en la escena")
		return

	# Guard: if lod1_begin > lod0_end there would be a dead zone where neither
	# LOD is visible. Clamp lod1_begin so it never exceeds lod0_end.
	if lod1_begin > lod0_end:
		push_error("[BakeBuilder] lod1Begin (%sm) > lod0End (%sm): habría una franja invisible. Se iguala lod1Begin a lod0End." % [lod1_begin, lod0_end])
		lod1_begin = lod0_end

	var root_inverse := scene_root.global_transform.affine_inverse()

	# LOD0: full geometry and materials, one surface per source surface.
	var lod0_mesh := _merge_meshes_flat(mesh_instances, root_inverse, false)

	# LOD1: same shape as LOD0 minus stairs and fences (fewer polys at
	# distance), with simplified materials and surfaces merged by material.
	var lod1_instances := mesh_instances.filter(func(mi): return not _is_lod1_excluded(mi))
	var lod1_mesh := _merge_meshes_by_material(lod1_instances, root_inverse)

	var baked_root := StaticBody3D.new()
	baked_root.name = scene_root.name

	var fade_margin := (
		0.0 if fade_mode == GeometryInstance3D.VISIBILITY_RANGE_FADE_DISABLED
		else maxf(0.0, lod0_end - lod1_begin)
	)

	var lod0_inst := MeshInstance3D.new()
	lod0_inst.name = "LOD0"
	lod0_inst.mesh = lod0_mesh
	lod0_inst.visibility_range_end = lod0_end
	lod0_inst.visibility_range_end_margin = fade_margin
	lod0_inst.visibility_range_fade_mode = fade_mode
	baked_root.add_child(lod0_inst)

	var lod1_inst := MeshInstance3D.new()
	lod1_inst.name = "LOD1"
	lod1_inst.mesh = lod1_mesh
	lod1_inst.visibility_range_begin = lod1_begin
	lod1_inst.visibility_range_begin_margin = fade_margin
	lod1_inst.visibility_range_fade_mode = fade_mode
	baked_root.add_child(lod1_inst)

	if build_occlusion:
		baked_root.add_child(_build_occluder(lod1_mesh))

	# Main collision: visual mesh without stair steps.
	var col_instances := mesh_instances.filter(func(mi): return not _is_stairs_mesh(mi))
	var col_mesh := _merge_meshes_flat(col_instances, root_inverse, false)
	var col_shape := CollisionShape3D.new()
	col_shape.name = "Collision"
	col_shape.shape = _build_concave_shape(col_mesh)
	baked_root.add_child(col_shape)

	# Staircase ramps: kept as ConvexPolygonShape3D (not merged into the
	# ConcavePolygonShape3D) so CharacterBody3D slides up smoothly.
	# Merging them causes an internal edge at the floor/ramp junction that
	# makes the character catch and stop.
	var stair_idx := 0
	for cs in convex_shapes:
		if not (cs.shape is ConvexPolygonShape3D):
			continue
		var convex: ConvexPolygonShape3D = cs.shape
		var local_to_root := root_inverse * cs.global_transform
		var pts := convex.points
		var transformed := PackedVector3Array()
		transformed.resize(pts.size())
		for p in pts.size():
			transformed[p] = local_to_root * pts[p]

		var stair_shape := CollisionShape3D.new()
		stair_shape.name = "Staircase_%d" % stair_idx
		stair_idx += 1
		var stair_convex := ConvexPolygonShape3D.new()
		stair_convex.points = transformed
		stair_shape.shape = stair_convex
		baked_root.add_child(stair_shape)

	if build_nav_mesh:
		baked_root.add_child(_build_nav_region(col_mesh, convex_shapes, root_inverse))

	_set_owner_recursive(baked_root, baked_root)

	var packed_scene := PackedScene.new()
	var pack_result := packed_scene.pack(baked_root)
	if pack_result != OK:
		push_error("[BakeBuilder] Pack falló: %s" % pack_result)
		baked_root.queue_free()
		return

	var file_name := String(scene_root.name) + ".tscn"
	var full_path := output_folder.rstrip("/") + "/" + file_name

	var save_result := ResourceSaver.save(packed_scene, full_path)
	if save_result != OK:
		push_error("[BakeBuilder] Save falló: %s" % save_result)
	else:
		print("[BakeBuilder] Guardado: %s" % full_path)
		ResourceLoader.load(full_path, "", ResourceLoader.CACHE_MODE_REPLACE)
		EditorInterface.get_resource_filesystem().update_file(full_path)

	baked_root.queue_free()


# ── Geometry collection ──────────────────────────────────────────────────────

static func _collect_geometry(node: Node,
		meshes: Array[MeshInstance3D], convex_shapes: Array[CollisionShape3D]) -> void:
	if node is MeshInstance3D:
		meshes.append(node)
	# Only ConvexPolygonShape3D matters here — that is exclusively the
	# staircase ramp. Box/ConcavePolygon shapes are replaced by the visual mesh.
	if node is CollisionShape3D and node.shape is ConvexPolygonShape3D:
		convex_shapes.append(node)
	# include_internal = true: HBWall (and the other typed nodes) keep their
	# mesh and collision as internal children, which get_children() hides by
	# default. Without this the bake would collect no wall geometry.
	for child in node.get_children(true):
		_collect_geometry(child, meshes, convex_shapes)


# ── LOD0 merge — one ArrayMesh surface per source surface, original materials ─

static func _merge_meshes_flat(instances: Array, root_inverse: Transform3D,
		simplify_materials: bool) -> ArrayMesh:
	var result := ArrayMesh.new()

	for mi in instances:
		if mi.mesh == null:
			continue
		var local_to_root: Transform3D = root_inverse * mi.global_transform
		var flip_winding := local_to_root.basis.determinant() < 0.0

		var surf_count: int = mi.mesh.get_surface_count()
		for s in surf_count:
			var arrays: Array = mi.mesh.surface_get_arrays(s)
			if arrays.is_empty():
				continue
			if not _transform_arrays_in_place(arrays, local_to_root):
				continue
			if flip_winding:
				_flip_triangle_winding(arrays)

			var new_surf_idx := result.get_surface_count()
			result.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)

			var mat: Material = mi.get_active_material(s)
			if simplify_materials:
				mat = _simplify_material(mat)
			if mat != null:
				result.surface_set_material(new_surf_idx, mat)

	return result


# ── LOD1 merge — surfaces grouped by material → one draw call per material ───
#
# Same geometry as LOD0 (openings and frames preserved) but with simplified
# materials (no normal/AO/roughmap textures) and surfaces concatenated by
# material to minimise draw calls when viewing the building from a distance.

static func _merge_meshes_by_material(instances: Array, root_inverse: Transform3D) -> ArrayMesh:
	# Keyed by the original Material object (null for surfaces without one).
	# Dictionaries preserve insertion order, so surfaces come out in the
	# order their materials were first seen.
	var group_arrays := {}     # Material or null -> Array of surface arrays
	var group_materials := {}  # Material or null -> simplified Material

	for mi in instances:
		if mi.mesh == null:
			continue

		var local_to_root: Transform3D = root_inverse * mi.global_transform
		var flip_winding := local_to_root.basis.determinant() < 0.0

		# For wall bodies, replace the original mesh (which has opening holes
		# and complex geometry) with a solid rectangular mesh — just FaceA and
		# FaceB, no holes, no edge surfaces. Materials are still taken from the
		# original MeshInstance3D so the user's assignments are preserved.
		var simplified_wall := _get_simplified_wall_mesh(mi)
		var source_mesh: Mesh = simplified_wall if simplified_wall != null else mi.mesh
		var surf_count: int = (
			WallMeshBuilder.SURFACE_FACE_B + 1 if simplified_wall != null  # only FaceA (0) and FaceB (1)
			else source_mesh.get_surface_count()
		)

		for s in surf_count:
			var arrays: Array = source_mesh.surface_get_arrays(s)
			if arrays.is_empty():
				continue
			if not _transform_arrays_in_place(arrays, local_to_root):
				continue
			if flip_winding:
				_flip_triangle_winding(arrays)

			# Material always comes from the original scene node so we pick
			# up the user's configured surface overrides, not the raw mesh.
			var orig_mat: Material = mi.get_active_material(s)

			if not group_arrays.has(orig_mat):
				group_arrays[orig_mat] = []
				group_materials[orig_mat] = _simplify_material(orig_mat)
			group_arrays[orig_mat].append(arrays)

	var result := ArrayMesh.new()
	for key in group_arrays:
		var merged = _concatenate_arrays(group_arrays[key])
		if merged == null:
			continue

		var surf_idx := result.get_surface_count()
		result.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, merged)

		var mat: Material = group_materials[key]
		if mat != null:
			result.surface_set_material(surf_idx, mat)
	return result


# ── Material simplification ──────────────────────────────────────────────────
#
# duplicate() copies ALL properties — uv1_scale, uv1_triplanar, cull_mode,
# texture_filter, etc. — so UV tiling and material look are preserved.
# Only the expensive per-pixel textures (normal, AO, heightmap) are stripped
# because they contribute nothing at LOD1 viewing distances.

static func _simplify_material(source: Material) -> Material:
	if source == null:
		return null
	if not (source is StandardMaterial3D):
		return source

	var std: StandardMaterial3D = source
	var s: StandardMaterial3D = std.duplicate()
	s.normal_enabled = false
	s.normal_texture = null
	s.ao_enabled = false
	s.ao_texture = null
	s.heightmap_enabled = false
	s.heightmap_texture = null
	if std.roughness_texture != null:
		s.roughness = 0.7
		s.roughness_texture = null
	if std.metallic_texture != null:
		s.metallic = 0.0
		s.metallic_texture = null
	return s


# ── Array helpers ────────────────────────────────────────────────────────────

## Transforms vertex positions and normals in-place using local_to_root.
## Returns false when the surface has no vertices (caller should skip it).
static func _transform_arrays_in_place(arrays: Array, t: Transform3D) -> bool:
	if typeof(arrays[Mesh.ARRAY_VERTEX]) != TYPE_PACKED_VECTOR3_ARRAY:
		return false
	var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	if verts.is_empty():
		return false

	for v in verts.size():
		verts[v] = t * verts[v]
	arrays[Mesh.ARRAY_VERTEX] = verts

	if typeof(arrays[Mesh.ARRAY_NORMAL]) == TYPE_PACKED_VECTOR3_ARRAY:
		var norms: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
		for v in norms.size():
			norms[v] = (t.basis * norms[v]).normalized()
		arrays[Mesh.ARRAY_NORMAL] = norms
	return true


## Concatenates a list of surface arrays into one, expanding indexed meshes
## to non-indexed before concatenation so all sources can be merged uniformly.
## Returns the merged surface arrays, or null when there is no geometry.
static func _concatenate_arrays(list: Array) -> Variant:
	var verts := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var tangents := PackedFloat32Array()
	var has_normals := false
	var has_uvs := false
	var has_tangents := false

	for arrays in list:
		var src_verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]

		var indices := PackedInt32Array()
		var has_indices := typeof(arrays[Mesh.ARRAY_INDEX]) == TYPE_PACKED_INT32_ARRAY
		if has_indices:
			indices = arrays[Mesh.ARRAY_INDEX]

		if has_indices:
			for i in indices:
				verts.append(src_verts[i])
		else:
			verts.append_array(src_verts)

		if typeof(arrays[Mesh.ARRAY_NORMAL]) == TYPE_PACKED_VECTOR3_ARRAY:
			has_normals = true
			var src_n: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
			if has_indices:
				for i in indices:
					normals.append(src_n[i])
			else:
				normals.append_array(src_n)

		if typeof(arrays[Mesh.ARRAY_TEX_UV]) == TYPE_PACKED_VECTOR2_ARRAY:
			has_uvs = true
			var src_uv: PackedVector2Array = arrays[Mesh.ARRAY_TEX_UV]
			if has_indices:
				for i in indices:
					uvs.append(src_uv[i])
			else:
				uvs.append_array(src_uv)

		if typeof(arrays[Mesh.ARRAY_TANGENT]) == TYPE_PACKED_FLOAT32_ARRAY:
			has_tangents = true
			var src_t: PackedFloat32Array = arrays[Mesh.ARRAY_TANGENT]
			if has_indices:
				for i in indices:
					for k in 4:
						tangents.append(src_t[i * 4 + k])
			else:
				tangents.append_array(src_t)

	if verts.is_empty():
		return null

	var result := []
	result.resize(Mesh.ARRAY_MAX)
	result[Mesh.ARRAY_VERTEX] = verts
	if has_normals and normals.size() == verts.size():
		result[Mesh.ARRAY_NORMAL] = normals
	if has_uvs and uvs.size() == verts.size():
		result[Mesh.ARRAY_TEX_UV] = uvs
	if has_tangents and tangents.size() == verts.size() * 4:
		result[Mesh.ARRAY_TANGENT] = tangents
	return _reindex_surface(result)


# ── Re-indexing — deduplicates vertices shared between merged surfaces ───────
#
# _concatenate_arrays expands all surfaces to non-indexed, so seam vertices
# between adjacent surfaces are duplicated. This pass rebuilds an index
# buffer and removes those duplicates, reducing vertex count and improving
# GPU vertex cache performance.

static func _reindex_surface(arrays: Array) -> Array:
	var src_verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]

	var has_normals := typeof(arrays[Mesh.ARRAY_NORMAL]) == TYPE_PACKED_VECTOR3_ARRAY
	var has_uvs := typeof(arrays[Mesh.ARRAY_TEX_UV]) == TYPE_PACKED_VECTOR2_ARRAY
	var has_tangents := typeof(arrays[Mesh.ARRAY_TANGENT]) == TYPE_PACKED_FLOAT32_ARRAY

	var src_normals := PackedVector3Array()
	var src_uvs := PackedVector2Array()
	var src_tangents := PackedFloat32Array()
	if has_normals:
		src_normals = arrays[Mesh.ARRAY_NORMAL]
	if has_uvs:
		src_uvs = arrays[Mesh.ARRAY_TEX_UV]
	if has_tangents:
		src_tangents = arrays[Mesh.ARRAY_TANGENT]

	var unique_verts := PackedVector3Array()
	var unique_normals := PackedVector3Array()
	var unique_uvs := PackedVector2Array()
	var unique_tangents := PackedFloat32Array()
	var indices := PackedInt32Array()
	indices.resize(src_verts.size())

	# Key: [position, normal, uv] — exact float equality is intentional here
	# because all duplicates originate from the same source data and share
	# the exact same bit pattern, not from independent calculations.
	var key_map := {}

	for i in src_verts.size():
		var key := [
			src_verts[i],
			src_normals[i] if has_normals else Vector3.ZERO,
			src_uvs[i] if has_uvs else Vector2.ZERO,
		]

		var idx: int
		if key_map.has(key):
			idx = key_map[key]
		else:
			idx = unique_verts.size()
			key_map[key] = idx
			unique_verts.append(src_verts[i])
			if has_normals:
				unique_normals.append(src_normals[i])
			if has_uvs:
				unique_uvs.append(src_uvs[i])
			if has_tangents:
				for k in 4:
					unique_tangents.append(src_tangents[i * 4 + k])
		indices[i] = idx

	print("[BakeBuilder] ReindexSurface: %d → %d vértices (%d duplicados eliminados)" % [
		src_verts.size(), unique_verts.size(), src_verts.size() - unique_verts.size()])

	var result := []
	result.resize(Mesh.ARRAY_MAX)
	result[Mesh.ARRAY_VERTEX] = unique_verts
	if has_normals:
		result[Mesh.ARRAY_NORMAL] = unique_normals
	if has_uvs:
		result[Mesh.ARRAY_TEX_UV] = unique_uvs
	if has_tangents:
		result[Mesh.ARRAY_TANGENT] = unique_tangents
	result[Mesh.ARRAY_INDEX] = indices
	return result


# ── Winding flip — needed when local_to_root has a negative determinant ──────
# (walls use a reflected basis: basis_z = up × dir).

static func _flip_triangle_winding(arrays: Array) -> void:
	if typeof(arrays[Mesh.ARRAY_INDEX]) == TYPE_PACKED_INT32_ARRAY:
		var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
		var i := 0
		while i + 2 < indices.size():
			var tmp := indices[i + 1]
			indices[i + 1] = indices[i + 2]
			indices[i + 2] = tmp
			i += 3
		arrays[Mesh.ARRAY_INDEX] = indices
		return

	_swap_vector3_every_3(arrays, Mesh.ARRAY_VERTEX)
	_swap_vector3_every_3(arrays, Mesh.ARRAY_NORMAL)
	_swap_vector2_every_3(arrays, Mesh.ARRAY_TEX_UV)
	_swap_vector2_every_3(arrays, Mesh.ARRAY_TEX_UV2)
	_swap_tangents_every_3(arrays)


static func _swap_vector3_every_3(arrays: Array, type: int) -> void:
	if typeof(arrays[type]) != TYPE_PACKED_VECTOR3_ARRAY:
		return
	var arr: PackedVector3Array = arrays[type]
	var i := 0
	while i + 2 < arr.size():
		var tmp := arr[i + 1]
		arr[i + 1] = arr[i + 2]
		arr[i + 2] = tmp
		i += 3
	arrays[type] = arr


static func _swap_vector2_every_3(arrays: Array, type: int) -> void:
	if typeof(arrays[type]) != TYPE_PACKED_VECTOR2_ARRAY:
		return
	var arr: PackedVector2Array = arrays[type]
	var i := 0
	while i + 2 < arr.size():
		var tmp := arr[i + 1]
		arr[i + 1] = arr[i + 2]
		arr[i + 2] = tmp
		i += 3
	arrays[type] = arr


static func _swap_tangents_every_3(arrays: Array) -> void:
	if typeof(arrays[Mesh.ARRAY_TANGENT]) != TYPE_PACKED_FLOAT32_ARRAY:
		return
	var arr: PackedFloat32Array = arrays[Mesh.ARRAY_TANGENT]
	if arr.size() % 4 != 0:
		return
	@warning_ignore("integer_division")
	var vert_count := arr.size() / 4
	var i := 0
	while i + 2 < vert_count:
		var p1 := (i + 1) * 4
		var p2 := (i + 2) * 4
		for k in 4:
			var tmp := arr[p1 + k]
			arr[p1 + k] = arr[p2 + k]
			arr[p2 + k] = tmp
		i += 3
	arrays[Mesh.ARRAY_TANGENT] = arr


# ── Utilities ────────────────────────────────────────────────────────────────

static func _set_owner_recursive(node: Node, owner: Node) -> void:
	for child in node.get_children():
		if child != owner:
			child.owner = owner
		_set_owner_recursive(child, owner)


## Returns true when the node is inside an HBStairs, HBFence, or
## HBFloorSlab, so it gets skipped in LOD1 to reduce polygon count at
## distance. Checked by node TYPE rather than by container name — walls,
## floor slabs, roof, stairs and fences are all siblings under the same
## per-floor "Floor_%d" node, so the container name alone can no longer
## tell them apart.
static func _is_lod1_excluded(node: Node) -> bool:
	var current := node.get_parent()
	while current != null and is_instance_valid(current):
		if current is HBStairs or current is HBFence or current is HBFloorSlab:
			return true
		current = current.get_parent()
	return false


## If mi belongs to a wall body (parent StaticBody3D has hb_wall_length meta),
## returns a solid rectangular wall mesh with no openings and no edge surfaces,
## so LOD1 walls are clean flat faces. Returns null for non-wall meshes.
static func _get_simplified_wall_mesh(mi: MeshInstance3D) -> ArrayMesh:
	var parent := mi.get_parent()
	if parent is HBWall:
		var wall: HBWall = parent
		return WallMeshBuilder.build(wall.length, wall.height, wall.thickness)
	# Legacy walls stored their dimensions as metadata on a plain StaticBody3D.
	if parent is StaticBody3D and parent.has_meta(WallHelper.META_WALL_LENGTH):
		var body: StaticBody3D = parent
		var length := float(body.get_meta(WallHelper.META_WALL_LENGTH))
		return WallMeshBuilder.build(
			length, WallHelper.get_wall_height(body), WallHelper.get_wall_thickness(body))
	return null


## Returns true when the node is a step mesh inside an HBStairs. Used to
## exclude stair visuals from the collision mesh — the ramp
## ConvexPolygonShape3D handles walkability instead.
static func _is_stairs_mesh(node: Node) -> bool:
	var current := node.get_parent()
	while current != null and is_instance_valid(current):
		if current is HBStairs:
			return true
		current = current.get_parent()
	return false


# ── Collision — single ConcavePolygonShape3D ─────────────────────────────────
#
# Uses the visual mesh (walls, floors, roof — no stair steps) so
# door/window openings are real holes and the geometry is exact.
# Staircases are kept as separate ConvexPolygonShape3D nodes.

static func _build_concave_shape(mesh: ArrayMesh) -> ConcavePolygonShape3D:
	var faces := PackedVector3Array()

	for s in mesh.get_surface_count():
		var arrays := mesh.surface_get_arrays(s)
		if arrays.is_empty():
			continue

		var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]

		if typeof(arrays[Mesh.ARRAY_INDEX]) == TYPE_PACKED_INT32_ARRAY:
			var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
			var i := 0
			while i + 2 < indices.size():
				faces.append(verts[indices[i]])
				faces.append(verts[indices[i + 1]])
				faces.append(verts[indices[i + 2]])
				i += 3
		else:
			var i := 0
			while i + 2 < verts.size():
				faces.append(verts[i])
				faces.append(verts[i + 1])
				faces.append(verts[i + 2])
				i += 3

	@warning_ignore("integer_division")
	print("[BakeBuilder] BuildConcaveShape: %d triángulos" % (faces.size() / 3))
	var shape := ConcavePolygonShape3D.new()
	shape.set_faces(faces)
	return shape


# ── Occluder — built from LOD1 geometry (already simplified: flat walls, ─────
# no stairs/fences) so the polygon count is low and the shape is accurate.
# The OccluderInstance3D tells Godot's occlusion system which fragments can
# be skipped when this building is blocking the view.

static func _build_occluder(source_mesh: ArrayMesh) -> OccluderInstance3D:
	var all_verts := PackedVector3Array()
	var all_indices := PackedInt32Array()

	for s in source_mesh.get_surface_count():
		var arrays := source_mesh.surface_get_arrays(s)
		if arrays.is_empty():
			continue

		var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var base_idx := all_verts.size()
		all_verts.append_array(verts)

		if typeof(arrays[Mesh.ARRAY_INDEX]) == TYPE_PACKED_INT32_ARRAY:
			var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
			for i in indices:
				all_indices.append(base_idx + i)
		else:
			for i in verts.size():
				all_indices.append(base_idx + i)

	var occluder := ArrayOccluder3D.new()
	occluder.set_arrays(all_verts, all_indices)

	var occluder_inst := OccluderInstance3D.new()
	occluder_inst.name = "Occluder"
	occluder_inst.occluder = occluder
	return occluder_inst


# ── Navigation mesh — baked at export time so the .tscn carries a ready- ─────
# to-use navmesh. Parameters match what was validated interactively:
#   agent_radius 0.3  → door opening 1.0m − 2×0.3 = 0.4m free (passes)
#   agent_max_slope 50 → stair ramps at 45° pass the walkability check
# The source geometry is:
#   • col_mesh — floors + walls with door holes (no stair steps)
#   • ramp triangles — extracted from each staircase ConvexPolygonShape3D
# Both are already in root-local space, so the .tscn is position-independent.

static func _build_nav_region(col_mesh: ArrayMesh,
		convex_shapes: Array[CollisionShape3D], root_inverse: Transform3D) -> NavigationRegion3D:
	var nav_mesh := NavigationMesh.new()
	nav_mesh.agent_radius = 0.3
	nav_mesh.agent_height = 1.8
	nav_mesh.agent_max_climb = 0.25
	nav_mesh.agent_max_slope = 50.0
	nav_mesh.cell_size = 0.10
	nav_mesh.cell_height = 0.10
	nav_mesh.region_min_size = 0.0

	var source_geom := NavigationMeshSourceGeometryData3D.new()

	# Floors + walls with door holes (no stair steps)
	source_geom.add_mesh(col_mesh, Transform3D.IDENTITY)

	# One ramp quad per staircase
	for cs in convex_shapes:
		if not (cs.shape is ConvexPolygonShape3D):
			continue
		var convex: ConvexPolygonShape3D = cs.shape

		var local_to_root := root_inverse * cs.global_transform
		var transformed := PackedVector3Array()
		transformed.resize(convex.points.size())
		for p in transformed.size():
			transformed[p] = local_to_root * convex.points[p]

		var ramp := _extract_ramp_face(transformed)
		if not ramp.is_empty():
			source_geom.add_faces(ramp, Transform3D.IDENTITY)

	NavigationServer3D.bake_from_source_geometry_data(nav_mesh, source_geom)

	print("[BakeBuilder] NavigationMesh horneado.")
	var region := NavigationRegion3D.new()
	region.name = "Navigation"
	region.navigation_mesh = nav_mesh
	return region


## Extracts the two triangles that form the walkable inclined face of the
## staircase ConvexPolygonShape3D (an 8-point wedge). Works for any rotation:
## splits points into top/bottom halves by Y, then selects the two "front"
## points from each half by projecting onto the ramp direction vector.
## Returns an empty array when the shape is degenerate.
static func _extract_ramp_face(pts: PackedVector3Array) -> PackedVector3Array:
	if pts.size() < 4:
		return PackedVector3Array()

	var by_y: Array = []
	for p in pts:
		by_y.append(p)
	by_y.sort_custom(func(a, b): return a.y < b.y)

	@warning_ignore("integer_division")
	var half: int = by_y.size() / 2
	var bot_pts := by_y.slice(0, half)
	var top_pts := by_y.slice(by_y.size() - half)

	var bot_centroid := Vector3.ZERO
	for p in bot_pts:
		bot_centroid += p
	bot_centroid /= bot_pts.size()

	var top_centroid := Vector3.ZERO
	for p in top_pts:
		top_centroid += p
	top_centroid /= top_pts.size()

	# Direction from floor-level centroid to top-level centroid
	var ramp_dir := (top_centroid - bot_centroid).normalized()

	# Width axis: perpendicular to ramp_dir and world-up, gives consistent
	# left-right ordering for any staircase orientation (not just north-facing).
	var width_dir := ramp_dir.cross(Vector3.UP).normalized()

	# "Front" edge = points most advanced along ramp_dir from their group centroid
	var top_near := top_pts.duplicate()
	top_near.sort_custom(func(a, b):
		return (a - top_centroid).dot(ramp_dir) > (b - top_centroid).dot(ramp_dir))
	top_near = top_near.slice(0, 2)
	top_near.sort_custom(func(a, b):
		return (a - top_centroid).dot(width_dir) < (b - top_centroid).dot(width_dir))

	var bot_near := bot_pts.duplicate()
	bot_near.sort_custom(func(a, b):
		return (a - bot_centroid).dot(ramp_dir) > (b - bot_centroid).dot(ramp_dir))
	bot_near = bot_near.slice(0, 2)
	bot_near.sort_custom(func(a, b):
		return (a - bot_centroid).dot(width_dir) < (b - bot_centroid).dot(width_dir))

	if top_near.size() < 2 or bot_near.size() < 2:
		return PackedVector3Array()

	# Quad as two CCW triangles (normal faces upward)
	return PackedVector3Array([
		top_near[0], top_near[1], bot_near[1],
		top_near[0], bot_near[1], bot_near[0],
	])
