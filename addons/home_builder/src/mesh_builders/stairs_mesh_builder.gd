@tool
class_name StairsMeshBuilder
extends RefCounted

## Generates an ArrayMesh for a single stair step with three surfaces:
##   0 = top    (walkable surface, normal pointing UP)
##   1 = bottom (underside of step, normal pointing DOWN)
##   2 = sides  (4 lateral faces, normals pointing outward)

const SURFACE_TOP := 0
const SURFACE_BOTTOM := 1
const SURFACE_SIDES := 2


static func build(width: float, rise: float, run: float) -> ArrayMesh:
	var mesh := ArrayMesh.new()
	MeshHelper.add_surface(mesh, _build_top(width, rise, run))
	MeshHelper.add_surface(mesh, _build_bottom(width, rise, run))
	MeshHelper.add_surface(mesh, _build_sides(width, rise, run))
	return mesh


# ── Top face (normal = Vector3.UP) ───────────────────────────────────────────

static func _build_top(width: float, rise: float, run: float) -> SurfaceTool:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	var half_x := width * 0.5
	var half_y := rise * 0.5
	var half_z := run * 0.5

	# Viewed from above (normal pointing up = +Y)
	MeshHelper.add_quad(st,
		Vector3(-half_x, half_y, half_z),
		Vector3(half_x, half_y, half_z),
		Vector3(half_x, half_y, -half_z),
		Vector3(-half_x, half_y, -half_z),
		Vector3.UP,
		Vector2(0, 0), Vector2(1, 0),
		Vector2(1, 1), Vector2(0, 1)
	)

	return st


# ── Bottom face (normal = Vector3.DOWN) ──────────────────────────────────────

static func _build_bottom(width: float, rise: float, run: float) -> SurfaceTool:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	var half_x := width * 0.5
	var half_y := rise * 0.5
	var half_z := run * 0.5

	# Viewed from below (normal pointing down = -Y)
	MeshHelper.add_quad(st,
		Vector3(half_x, -half_y, half_z),
		Vector3(-half_x, -half_y, half_z),
		Vector3(-half_x, -half_y, -half_z),
		Vector3(half_x, -half_y, -half_z),
		Vector3.DOWN,
		Vector2(0, 0), Vector2(1, 0),
		Vector2(1, 1), Vector2(0, 1)
	)

	return st


# ── Four side faces ──────────────────────────────────────────────────────────

static func _build_sides(width: float, rise: float, run: float) -> SurfaceTool:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	var half_x := width * 0.5
	var half_y := rise * 0.5
	var half_z := run * 0.5

	# Front face (+Z, normal = +Z)
	MeshHelper.add_quad(st,
		Vector3(-half_x, half_y, half_z),
		Vector3(-half_x, -half_y, half_z),
		Vector3(half_x, -half_y, half_z),
		Vector3(half_x, half_y, half_z),
		Vector3(0, 0, 1),
		Vector2(0, 0), Vector2(0, 1),
		Vector2(1, 1), Vector2(1, 0)
	)

	# Back face (-Z, normal = -Z)
	MeshHelper.add_quad(st,
		Vector3(half_x, half_y, -half_z),
		Vector3(half_x, -half_y, -half_z),
		Vector3(-half_x, -half_y, -half_z),
		Vector3(-half_x, half_y, -half_z),
		Vector3(0, 0, -1),
		Vector2(0, 0), Vector2(0, 1),
		Vector2(1, 1), Vector2(1, 0)
	)

	# Right face (+X, normal = +X)
	MeshHelper.add_quad(st,
		Vector3(half_x, half_y, half_z),
		Vector3(half_x, -half_y, half_z),
		Vector3(half_x, -half_y, -half_z),
		Vector3(half_x, half_y, -half_z),
		Vector3(1, 0, 0),
		Vector2(0, 0), Vector2(0, 1),
		Vector2(1, 1), Vector2(1, 0)
	)

	# Left face (-X, normal = -X)
	MeshHelper.add_quad(st,
		Vector3(-half_x, half_y, -half_z),
		Vector3(-half_x, -half_y, -half_z),
		Vector3(-half_x, -half_y, half_z),
		Vector3(-half_x, half_y, half_z),
		Vector3(-1, 0, 0),
		Vector2(0, 0), Vector2(0, 1),
		Vector2(1, 1), Vector2(1, 0)
	)

	return st
