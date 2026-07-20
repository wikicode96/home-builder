@tool
class_name RoofMeshBuilder
extends RefCounted

## Generates an ArrayMesh for a roof covering a footprint of (w × d).
## Three surfaces:
##   0 = top    (slope skin)
##   1 = bottom (underside)
##   2 = sides  (gable triangles + back vertical wall on shed roofs)
##
## Local origin sits at the minimum corner (0, 0, 0). Vertices live in
## the box (0..w, 0..pitch, 0..d). Direction is applied as a 90° rotation
## around +Y about the footprint center.

enum RoofType { FLAT, SHED, GABLE, HIP }
enum RoofDirection { NORTH, SOUTH, EAST, WEST }

const SURFACE_TOP := 0
const SURFACE_BOTTOM := 1
const SURFACE_SIDES := 2

const _FLAT_THICKNESS := 0.15


static func build(type: RoofType, w: float, d: float, pitch: float, dir: RoofDirection) -> ArrayMesh:
	var top := SurfaceTool.new()
	top.begin(Mesh.PRIMITIVE_TRIANGLES)
	var bot := SurfaceTool.new()
	bot.begin(Mesh.PRIMITIVE_TRIANGLES)
	var sides := SurfaceTool.new()
	sides.begin(Mesh.PRIMITIVE_TRIANGLES)

	var rot := _rot_steps(type, dir)
	var swap := (rot % 2) != 0
	var cw := d if swap else w
	var cd := w if swap else d
	var tf := _make_orient(rot, cw, cd, w, d)

	match type:
		RoofType.FLAT:
			_build_flat(top, bot, sides, cw, cd, tf)
		RoofType.SHED:
			_build_shed(top, bot, sides, cw, cd, pitch, tf)
		RoofType.GABLE:
			_build_gable(top, bot, sides, cw, cd, pitch, tf)
		RoofType.HIP:
			_build_hip(top, bot, sides, cw, cd, pitch, tf)

	var mesh := ArrayMesh.new()
	MeshHelper.add_surface(mesh, top)
	MeshHelper.add_surface(mesh, bot)
	MeshHelper.add_surface(mesh, sides)
	return mesh


# ── Orientation ──────────────────────────────────────────────────────────────

static func _rot_steps(type: RoofType, dir: RoofDirection) -> int:
	# Flat and Hip are rotation-invariant (4-way symmetric for our purposes).
	if type == RoofType.FLAT or type == RoofType.HIP:
		return 0

	# Shed: canonical high edge at -Z (North). Rotating 90° CCW around +Y
	# sends -Z → -X (West).
	if type == RoofType.SHED:
		match dir:
			RoofDirection.NORTH:
				return 0
			RoofDirection.WEST:
				return 1
			RoofDirection.SOUTH:
				return 2
			RoofDirection.EAST:
				return 3
			_:
				return 0

	# Gable: canonical ridge along X (gable ends face E/W). N/S means
	# gable ends face E/W (canonical). E/W means ridge along Z.
	match dir:
		RoofDirection.NORTH, RoofDirection.SOUTH:
			return 0
		RoofDirection.EAST, RoofDirection.WEST:
			return 1
		_:
			return 0


static func _make_orient(n: int, cw: float, cd: float, w: float, d: float) -> Transform3D:
	if n == 0:
		return Transform3D.IDENTITY
	var basis := Basis(Vector3.UP, n * PI / 2.0)
	var c_center := Vector3(cw / 2.0, 0, cd / 2.0)
	var act_center := Vector3(w / 2.0, 0, d / 2.0)
	return Transform3D(basis, act_center - basis * c_center)


static func _v(x: float, y: float, z: float, tf: Transform3D) -> Vector3:
	return tf * Vector3(x, y, z)


static func _n(normal: Vector3, tf: Transform3D) -> Vector3:
	return (tf.basis * normal).normalized()


# ── Flat ─────────────────────────────────────────────────────────────────────

static func _build_flat(top: SurfaceTool, bot: SurfaceTool, sides: SurfaceTool,
		w: float, d: float, tf: Transform3D) -> void:
	var t := _FLAT_THICKNESS

	MeshHelper.add_quad(top,
		_v(0, t, d, tf), _v(w, t, d, tf), _v(w, t, 0, tf), _v(0, t, 0, tf),
		_n(Vector3.UP, tf),
		Vector2(0, d), Vector2(w, d), Vector2(w, 0), Vector2(0, 0))

	MeshHelper.add_quad(bot,
		_v(w, 0, d, tf), _v(0, 0, d, tf), _v(0, 0, 0, tf), _v(w, 0, 0, tf),
		_n(Vector3.DOWN, tf),
		Vector2(0, d), Vector2(w, d), Vector2(w, 0), Vector2(0, 0))

	_add_vertical_quad(sides, _v(0, 0, d, tf), _v(w, 0, d, tf), t,
		_n(Vector3(0, 0, 1), tf), w)
	_add_vertical_quad(sides, _v(w, 0, 0, tf), _v(0, 0, 0, tf), t,
		_n(Vector3(0, 0, -1), tf), w)
	_add_vertical_quad(sides, _v(w, 0, d, tf), _v(w, 0, 0, tf), t,
		_n(Vector3(1, 0, 0), tf), d)
	_add_vertical_quad(sides, _v(0, 0, 0, tf), _v(0, 0, d, tf), t,
		_n(Vector3(-1, 0, 0), tf), d)


static func _add_vertical_quad(st: SurfaceTool, a: Vector3, b: Vector3,
		h: float, normal: Vector3, length: float) -> void:
	var top1 := a + Vector3(0, h, 0)
	var top2 := b + Vector3(0, h, 0)
	MeshHelper.add_quad(st, a, b, top2, top1, normal,
		Vector2(0, h), Vector2(length, h),
		Vector2(length, 0), Vector2(0, 0))


# ── Shed — high edge at -Z ───────────────────────────────────────────────────

static func _build_shed(top: SurfaceTool, bot: SurfaceTool, sides: SurfaceTool,
		w: float, d: float, p: float, tf: Transform3D) -> void:
	var slope_normal := Vector3(0, d, p).normalized()

	# Top sloped quad: low at +Z (z=d), high at -Z (z=0)
	MeshHelper.add_quad(top,
		_v(0, 0, d, tf), _v(w, 0, d, tf), _v(w, p, 0, tf), _v(0, p, 0, tf),
		_n(slope_normal, tf),
		Vector2(0, d), Vector2(w, d),
		Vector2(w, 0), Vector2(0, 0))

	# Bottom flat
	MeshHelper.add_quad(bot,
		_v(w, 0, d, tf), _v(0, 0, d, tf), _v(0, 0, 0, tf), _v(w, 0, 0, tf),
		_n(Vector3.DOWN, tf),
		Vector2(0, d), Vector2(w, d),
		Vector2(w, 0), Vector2(0, 0))

	# Back vertical wall (-Z side, height p)
	MeshHelper.add_quad(sides,
		_v(w, 0, 0, tf), _v(0, 0, 0, tf), _v(0, p, 0, tf), _v(w, p, 0, tf),
		_n(Vector3(0, 0, -1), tf),
		Vector2(0, p), Vector2(w, p),
		Vector2(w, 0), Vector2(0, 0))

	# Left gable triangle (-X)
	MeshHelper.add_triangle(sides,
		_v(0, 0, 0, tf), _v(0, 0, d, tf), _v(0, p, 0, tf),
		_n(Vector3(-1, 0, 0), tf),
		Vector2(0, 0), Vector2(d, 0), Vector2(0, p))

	# Right gable triangle (+X)
	MeshHelper.add_triangle(sides,
		_v(w, 0, 0, tf), _v(w, p, 0, tf), _v(w, 0, d, tf),
		_n(Vector3(1, 0, 0), tf),
		Vector2(0, 0), Vector2(0, p), Vector2(d, 0))


# ── Gable — ridge along X at z = d/2 ─────────────────────────────────────────

static func _build_gable(top: SurfaceTool, bot: SurfaceTool, sides: SurfaceTool,
		w: float, d: float, p: float, tf: Transform3D) -> void:
	var half_d := d * 0.5
	var n_front := Vector3(0, half_d, p).normalized()
	var n_back := Vector3(0, half_d, -p).normalized()

	# Front slope: from low edge at z=d up to ridge at z=d/2
	MeshHelper.add_quad(top,
		_v(0, 0, d, tf), _v(w, 0, d, tf),
		_v(w, p, half_d, tf), _v(0, p, half_d, tf),
		_n(n_front, tf),
		Vector2(0, half_d), Vector2(w, half_d),
		Vector2(w, 0), Vector2(0, 0))

	# Back slope: from ridge down to low edge at z=0
	MeshHelper.add_quad(top,
		_v(0, p, half_d, tf), _v(w, p, half_d, tf),
		_v(w, 0, 0, tf), _v(0, 0, 0, tf),
		_n(n_back, tf),
		Vector2(0, 0), Vector2(w, 0),
		Vector2(w, half_d), Vector2(0, half_d))

	# Bottom flat
	MeshHelper.add_quad(bot,
		_v(w, 0, d, tf), _v(0, 0, d, tf), _v(0, 0, 0, tf), _v(w, 0, 0, tf),
		_n(Vector3.DOWN, tf),
		Vector2(0, d), Vector2(w, d),
		Vector2(w, 0), Vector2(0, 0))

	# Left gable triangle (-X)
	MeshHelper.add_triangle(sides,
		_v(0, 0, 0, tf), _v(0, 0, d, tf), _v(0, p, half_d, tf),
		_n(Vector3(-1, 0, 0), tf),
		Vector2(0, 0), Vector2(d, 0), Vector2(half_d, p))

	# Right gable triangle (+X)
	MeshHelper.add_triangle(sides,
		_v(w, 0, d, tf), _v(w, 0, 0, tf), _v(w, p, half_d, tf),
		_n(Vector3(1, 0, 0), tf),
		Vector2(0, 0), Vector2(d, 0), Vector2(half_d, p))


# ── Hip — ridge along longer axis ────────────────────────────────────────────

static func _build_hip(top: SurfaceTool, bot: SurfaceTool, _sides: SurfaceTool,
		w: float, d: float, p: float, tf: Transform3D) -> void:
	var ridge_along_x := w >= d
	var a := d * 0.5 if ridge_along_x else w * 0.5

	var ridge_lo: Vector3
	var ridge_hi: Vector3
	if ridge_along_x:
		ridge_lo = Vector3(a, p, a)
		ridge_hi = Vector3(w - a, p, a)
	else:
		ridge_lo = Vector3(a, p, a)
		ridge_hi = Vector3(a, p, d - a)

	var c00 := Vector3(0, 0, 0)
	var c10 := Vector3(w, 0, 0)
	var c11 := Vector3(w, 0, d)
	var c01 := Vector3(0, 0, d)

	if ridge_along_x:
		# Front trapezoid (+Z)
		MeshHelper.add_quad(top,
			_v(c01.x, c01.y, c01.z, tf), _v(c11.x, c11.y, c11.z, tf),
			_v(ridge_hi.x, ridge_hi.y, ridge_hi.z, tf),
			_v(ridge_lo.x, ridge_lo.y, ridge_lo.z, tf),
			_n(Vector3(0, a, p).normalized(), tf),
			Vector2(0, a), Vector2(w, a),
			Vector2(w - a, 0), Vector2(a, 0))

		# Back trapezoid (-Z)
		MeshHelper.add_quad(top,
			_v(c10.x, c10.y, c10.z, tf), _v(c00.x, c00.y, c00.z, tf),
			_v(ridge_lo.x, ridge_lo.y, ridge_lo.z, tf),
			_v(ridge_hi.x, ridge_hi.y, ridge_hi.z, tf),
			_n(Vector3(0, a, -p).normalized(), tf),
			Vector2(0, a), Vector2(w, a),
			Vector2(w - a, 0), Vector2(a, 0))

		# Left triangle (-X)
		MeshHelper.add_triangle(top,
			_v(c00.x, c00.y, c00.z, tf), _v(c01.x, c01.y, c01.z, tf),
			_v(ridge_lo.x, ridge_lo.y, ridge_lo.z, tf),
			_n(Vector3(-p, a, 0).normalized(), tf),
			Vector2(0, 0), Vector2(d, 0), Vector2(a, p))

		# Right triangle (+X)
		MeshHelper.add_triangle(top,
			_v(c11.x, c11.y, c11.z, tf), _v(c10.x, c10.y, c10.z, tf),
			_v(ridge_hi.x, ridge_hi.y, ridge_hi.z, tf),
			_n(Vector3(p, a, 0).normalized(), tf),
			Vector2(0, 0), Vector2(d, 0), Vector2(a, p))
	else:
		# Left trapezoid (-X)
		MeshHelper.add_quad(top,
			_v(c00.x, c00.y, c00.z, tf), _v(c01.x, c01.y, c01.z, tf),
			_v(ridge_hi.x, ridge_hi.y, ridge_hi.z, tf),
			_v(ridge_lo.x, ridge_lo.y, ridge_lo.z, tf),
			_n(Vector3(-p, a, 0).normalized(), tf),
			Vector2(0, a), Vector2(d, a),
			Vector2(d - a, 0), Vector2(a, 0))

		# Right trapezoid (+X)
		MeshHelper.add_quad(top,
			_v(c11.x, c11.y, c11.z, tf), _v(c10.x, c10.y, c10.z, tf),
			_v(ridge_lo.x, ridge_lo.y, ridge_lo.z, tf),
			_v(ridge_hi.x, ridge_hi.y, ridge_hi.z, tf),
			_n(Vector3(p, a, 0).normalized(), tf),
			Vector2(0, a), Vector2(d, a),
			Vector2(d - a, 0), Vector2(a, 0))

		# Front triangle (+Z)
		MeshHelper.add_triangle(top,
			_v(c01.x, c01.y, c01.z, tf), _v(c11.x, c11.y, c11.z, tf),
			_v(ridge_hi.x, ridge_hi.y, ridge_hi.z, tf),
			_n(Vector3(0, a, p).normalized(), tf),
			Vector2(0, 0), Vector2(w, 0), Vector2(a, p))

		# Back triangle (-Z)
		MeshHelper.add_triangle(top,
			_v(c10.x, c10.y, c10.z, tf), _v(c00.x, c00.y, c00.z, tf),
			_v(ridge_lo.x, ridge_lo.y, ridge_lo.z, tf),
			_n(Vector3(0, a, -p).normalized(), tf),
			Vector2(0, 0), Vector2(w, 0), Vector2(a, p))

	# Bottom flat
	MeshHelper.add_quad(bot,
		_v(w, 0, d, tf), _v(0, 0, d, tf), _v(0, 0, 0, tf), _v(w, 0, 0, tf),
		_n(Vector3.DOWN, tf),
		Vector2(0, d), Vector2(w, d),
		Vector2(w, 0), Vector2(0, 0))
