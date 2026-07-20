@tool
class_name MeshHelper
extends RefCounted


static func add_surface(mesh: ArrayMesh, st: SurfaceTool) -> void:
	st.generate_tangents()
	st.commit(mesh)


## Adds a quad (2 triangles) to the surface tool.
## Vertices must be given in CCW order when viewed from the direction the
## normal points; this method emits them in Godot's CW front-face order.
##
##  v0 ── v1
##  │    ╱ │
##  │  ╱   │
##  v3 ── v2
static func add_quad(st: SurfaceTool,
		v0: Vector3, v1: Vector3, v2: Vector3, v3: Vector3,
		normal: Vector3,
		uv0: Vector2, uv1: Vector2, uv2: Vector2, uv3: Vector2) -> void:
	st.set_normal(normal)
	st.set_uv(uv0)
	st.add_vertex(v0)
	st.set_normal(normal)
	st.set_uv(uv2)
	st.add_vertex(v2)
	st.set_normal(normal)
	st.set_uv(uv1)
	st.add_vertex(v1)

	st.set_normal(normal)
	st.set_uv(uv0)
	st.add_vertex(v0)
	st.set_normal(normal)
	st.set_uv(uv3)
	st.add_vertex(v3)
	st.set_normal(normal)
	st.set_uv(uv2)
	st.add_vertex(v2)


## Adds a single triangle. Vertices must be given in CCW order when viewed
## from the direction the normal points.
static func add_triangle(st: SurfaceTool,
		v0: Vector3, v1: Vector3, v2: Vector3,
		normal: Vector3,
		uv0: Vector2, uv1: Vector2, uv2: Vector2) -> void:
	st.set_normal(normal)
	st.set_uv(uv0)
	st.add_vertex(v0)
	st.set_normal(normal)
	st.set_uv(uv2)
	st.add_vertex(v2)
	st.set_normal(normal)
	st.set_uv(uv1)
	st.add_vertex(v1)
