@tool
class_name JunctionFillMeshBuilder
extends RefCounted

## Generates an ArrayMesh that fills the gap polygons at wall junctions
## (X-, Y-, and other multi-wall nodes where individual wall tops would leave
## an uncovered region). Each fill is a convex polygon in the XZ plane,
## extruded to a flat top face (y = +hy) and bottom face (y = -hy).
##
## Side faces of the gap are already covered by each wall's end/start cap, so
## only the horizontal faces are needed here.


## [param fills] is an Array of WallJunctionSolver.JunctionFill.
## Returns null when there is nothing to build.
static func build(fills: Array, wall_height: float) -> ArrayMesh:
	if fills == null or fills.is_empty():
		return null

	var hy := wall_height * 0.5
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	for fill in fills:
		var pts: PackedVector2Array = fill.points_xz
		var n := pts.size()
		if n < 3:
			continue

		var c := Vector2.ZERO
		for p in pts:
			c += p
		c /= n

		for i in n:
			var p0 := pts[i]
			var p1 := pts[(i + 1) % n]

			# Top face — normal up, CCW winding from above.
			_add_tri(st,
				Vector3(c.x, hy, c.y),
				Vector3(p0.x, hy, p0.y),
				Vector3(p1.x, hy, p1.y),
				Vector3.UP)

			# Bottom face — normal down, CCW winding from below (reversed).
			_add_tri(st,
				Vector3(c.x, -hy, c.y),
				Vector3(p1.x, -hy, p1.y),
				Vector3(p0.x, -hy, p0.y),
				Vector3.DOWN)

	var mesh := ArrayMesh.new()
	MeshHelper.add_surface(mesh, st)
	return mesh


static func _add_tri(st: SurfaceTool,
		v0: Vector3, v1: Vector3, v2: Vector3, normal: Vector3) -> void:
	st.set_normal(normal)
	st.set_uv(Vector2(0.5, 0.5))
	st.add_vertex(v0)
	st.set_normal(normal)
	st.set_uv(Vector2(0.0, 1.0))
	st.add_vertex(v1)
	st.set_normal(normal)
	st.set_uv(Vector2(1.0, 0.0))
	st.add_vertex(v2)
