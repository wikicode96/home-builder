@tool
class_name FloorMeshBuilder
extends RefCounted

## Generates an ArrayMesh for a floor slab of arbitrary size (cols x rows tiles
## of 1m, height 0.1m), centered at local origin. Three surfaces:
##   0 = top    (walkable surface, normal pointing UP)
##   1 = bottom (ceiling of floor below, normal pointing DOWN)
##   2 = sides  (4 lateral faces, normals pointing outward)
##
## UVs on top/bottom tile per 1m so a single texture repeats once per metre.

const SURFACE_TOP := 0
const SURFACE_BOTTOM := 1
const SURFACE_SIDES := 2

const _HALF_Y := 0.05  # half of 0.1m height


static func build(cols: int = 1, rows: int = 1) -> ArrayMesh:
	var half_x := cols * 0.5
	var half_z := rows * 0.5

	var mesh := ArrayMesh.new()
	MeshHelper.add_surface(mesh, _build_top(half_x, half_z, cols, rows))
	MeshHelper.add_surface(mesh, _build_bottom(half_x, half_z, cols, rows))
	MeshHelper.add_surface(mesh, _build_sides(half_x, half_z, cols, rows))
	return mesh


# ── Top face (Y = +_HALF_Y, normal = Vector3.UP) ─────────────────────────────

static func _build_top(half_x: float, half_z: float, cols: int, rows: int) -> SurfaceTool:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	MeshHelper.add_quad(st,
		Vector3(-half_x, _HALF_Y, half_z),
		Vector3(half_x, _HALF_Y, half_z),
		Vector3(half_x, _HALF_Y, -half_z),
		Vector3(-half_x, _HALF_Y, -half_z),
		Vector3.UP,
		Vector2(0, 0), Vector2(cols, 0),
		Vector2(cols, rows), Vector2(0, rows)
	)

	return st


# ── Bottom face (Y = -_HALF_Y, normal = Vector3.DOWN) ────────────────────────

static func _build_bottom(half_x: float, half_z: float, cols: int, rows: int) -> SurfaceTool:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	MeshHelper.add_quad(st,
		Vector3(half_x, -_HALF_Y, half_z),
		Vector3(-half_x, -_HALF_Y, half_z),
		Vector3(-half_x, -_HALF_Y, -half_z),
		Vector3(half_x, -_HALF_Y, -half_z),
		Vector3.DOWN,
		Vector2(0, 0), Vector2(cols, 0),
		Vector2(cols, rows), Vector2(0, rows)
	)

	return st


# ── Four side faces ──────────────────────────────────────────────────────────

static func _build_sides(half_x: float, half_z: float, cols: int, rows: int) -> SurfaceTool:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	# Front face (+Z)
	MeshHelper.add_quad(st,
		Vector3(-half_x, _HALF_Y, half_z),
		Vector3(-half_x, -_HALF_Y, half_z),
		Vector3(half_x, -_HALF_Y, half_z),
		Vector3(half_x, _HALF_Y, half_z),
		Vector3(0, 0, 1),
		Vector2(0, 0), Vector2(0, 1),
		Vector2(cols, 1), Vector2(cols, 0)
	)

	# Back face (-Z)
	MeshHelper.add_quad(st,
		Vector3(half_x, _HALF_Y, -half_z),
		Vector3(half_x, -_HALF_Y, -half_z),
		Vector3(-half_x, -_HALF_Y, -half_z),
		Vector3(-half_x, _HALF_Y, -half_z),
		Vector3(0, 0, -1),
		Vector2(0, 0), Vector2(0, 1),
		Vector2(cols, 1), Vector2(cols, 0)
	)

	# Right face (+X)
	MeshHelper.add_quad(st,
		Vector3(half_x, _HALF_Y, half_z),
		Vector3(half_x, -_HALF_Y, half_z),
		Vector3(half_x, -_HALF_Y, -half_z),
		Vector3(half_x, _HALF_Y, -half_z),
		Vector3(1, 0, 0),
		Vector2(0, 0), Vector2(0, 1),
		Vector2(rows, 1), Vector2(rows, 0)
	)

	# Left face (-X)
	MeshHelper.add_quad(st,
		Vector3(-half_x, _HALF_Y, -half_z),
		Vector3(-half_x, -_HALF_Y, -half_z),
		Vector3(-half_x, -_HALF_Y, half_z),
		Vector3(-half_x, _HALF_Y, half_z),
		Vector3(-1, 0, 0),
		Vector2(0, 0), Vector2(0, 1),
		Vector2(rows, 1), Vector2(rows, 0)
	)

	return st
