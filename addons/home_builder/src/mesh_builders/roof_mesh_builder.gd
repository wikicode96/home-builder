@tool
class_name RoofMeshBuilder
extends RefCounted

## Generates an ArrayMesh for a roof covering a footprint of (w × d).
## Three surfaces, always in this order:
##   0 = top    (outward-facing slope skin)
##   1 = bottom (underside — the flat soffit on FLAT/SHED/GABLE, the inverted
##               slope shell on HIP)
##   2 = sides  (gable triangles and the shed's back wall; on HIP, the
##               vertical band around the eave that closes the two shells)
##
## Local origin sits at the minimum corner (0, 0, 0). Vertices live in
## the box (0..w, 0..pitch, 0..d) — except for the eave overhang, which
## prolongs the slope faces outside it. Direction is applied as a 90°
## rotation around +Y about the footprint center.

enum RoofType { FLAT, SHED, GABLE, HIP }
enum RoofDirection { NORTH, SOUTH, EAST, WEST }

const SURFACE_TOP := 0
const SURFACE_BOTTOM := 1
const SURFACE_SIDES := 2

const _FLAT_THICKNESS := 0.15

## Coincident inner/outer shells would leave the eave band with zero area,
## and generate_tangents() fails on a surface with no geometry.
const _MIN_THICKNESS := 0.01


static func build(type: RoofType, w: float, d: float, pitch: float,
		dir: RoofDirection, eave: float = 0.0,
		thickness: float = 0.0) -> ArrayMesh:
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
			_build_hip(top, bot, sides, cw, cd, pitch, eave, thickness, tf)

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


static func _p(point: Vector3, tf: Transform3D) -> Vector3:
	return tf * point


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
#
# Built as a solid: an inner shell (the soffit, seen from below), an outer
# shell raised above it, and a vertical band around the eave closing the two
# together. The result is watertight — the ridge and the hip lines are interior
# edges where the faces of each shell already meet, so the eave is the only
# boundary that needs capping.
#
# `e` is the eave overhang. It does not resize the roof: pitch, ridge height
# and ridge position are computed from (w, d, p) exactly as before, and the
# slope planes are then *prolonged* `e` metres outward in plan, dropping to
# y = -e·p/a at the eave edge. Because the ridge is inset by the same `a` on
# both axes, the hip lines run at 45° in plan, so their prolongation passes
# straight through the corners of the outward-expanded rectangle: the four
# faces still meet cleanly and only the base corners move.
#
# `t` is the thickness measured PERPENDICULAR to the slope, but it is applied
# as a vertical translation. That works because all four faces share the same
# pitch (the ridge inset `a` is identical on both axes), so offsetting every
# plane along its own normal by `t` is the same as raising the whole shell by
# t·√(a²+p²)/a — the raised faces still meet at the same ridge and hip lines.
# And since each face is the graph of a function of (x, z), a purely vertical
# translation can never make the two shells intersect.
#
# That same √(a²+p²)/a factor converts plan distance into distance along the
# slope, which is exactly what the V coordinate needs so a square texture
# doesn't come out squashed downhill.

static func _build_hip(outer: SurfaceTool, inner: SurfaceTool, band: SurfaceTool,
		w: float, d: float, p: float, e: float, t: float, tf: Transform3D) -> void:
	var a := (d * 0.5) if w >= d else (w * 0.5)

	# A degenerate footprint (a → 0) makes both the drop and the slope factor
	# diverge; fall back to a horizontal overhang with plan-projected UVs.
	var slope := 1.0
	var drop := 0.0
	if a > 0.001:
		slope = sqrt(a * a + p * p) / a
		drop = e * p / a

	var rise := maxf(t, _MIN_THICKNESS) * slope

	_emit_hip_shell(inner, w, d, p, e, drop, slope, 0.0, true, tf)
	_emit_hip_shell(outer, w, d, p, e, drop, slope, rise, false, tf)
	_emit_eave_band(band, w, d, e, drop, rise, tf)


## One slope shell: four faces meeting at the ridge and along the hip lines,
## its eave edge at y = y_offset - drop. With [param flip] the winding, the
## normals and the U axis are all reversed, so the same definition serves as
## the inverted soffit — the same trick WallMeshBuilder._build_face uses to
## turn Face A into Face B.
static func _emit_hip_shell(st: SurfaceTool, w: float, d: float, p: float,
		e: float, drop: float, slope: float, y_offset: float, flip: bool,
		tf: Transform3D) -> void:
	var ridge_along_x := w >= d
	var a := (d * 0.5) if ridge_along_x else (w * 0.5)
	var ridge_y := p + y_offset

	var ridge_lo: Vector3
	var ridge_hi: Vector3
	if ridge_along_x:
		ridge_lo = Vector3(a, ridge_y, a)
		ridge_hi = Vector3(w - a, ridge_y, a)
	else:
		ridge_lo = Vector3(a, ridge_y, a)
		ridge_hi = Vector3(a, ridge_y, d - a)

	var eave_y := y_offset - drop
	var c00 := Vector3(-e, eave_y, -e)
	var c10 := Vector3(w + e, eave_y, -e)
	var c11 := Vector3(w + e, eave_y, d + e)
	var c01 := Vector3(-e, eave_y, d + e)

	# V is the distance from the ridge measured ALONG the slope, on all four
	# faces alike, so tile courses line up across the hip lines. U is the plan
	# coordinate running along the eave; it necessarily breaks at the hips — a
	# hip roof has no seamless unwrap, and real tiles are cut there too.
	var v_eave := (a + e) * slope

	if ridge_along_x:
		# Front trapezoid (+Z)
		_shell_quad(st, flip, w, tf,
			c01, c11, ridge_hi, ridge_lo,
			Vector3(0, a, p).normalized(),
			Vector2(-e, v_eave), Vector2(w + e, v_eave),
			Vector2(w - a, 0), Vector2(a, 0))

		# Back trapezoid (-Z)
		_shell_quad(st, flip, w, tf,
			c10, c00, ridge_lo, ridge_hi,
			Vector3(0, a, -p).normalized(),
			Vector2(-e, v_eave), Vector2(w + e, v_eave),
			Vector2(w - a, 0), Vector2(a, 0))

		# Left triangle (-X)
		_shell_triangle(st, flip, d, tf,
			c00, c01, ridge_lo,
			Vector3(-p, a, 0).normalized(),
			Vector2(-e, v_eave), Vector2(d + e, v_eave), Vector2(a, 0))

		# Right triangle (+X)
		_shell_triangle(st, flip, d, tf,
			c11, c10, ridge_hi,
			Vector3(p, a, 0).normalized(),
			Vector2(-e, v_eave), Vector2(d + e, v_eave), Vector2(a, 0))
	else:
		# Left trapezoid (-X)
		_shell_quad(st, flip, d, tf,
			c00, c01, ridge_hi, ridge_lo,
			Vector3(-p, a, 0).normalized(),
			Vector2(-e, v_eave), Vector2(d + e, v_eave),
			Vector2(d - a, 0), Vector2(a, 0))

		# Right trapezoid (+X)
		_shell_quad(st, flip, d, tf,
			c11, c10, ridge_lo, ridge_hi,
			Vector3(p, a, 0).normalized(),
			Vector2(-e, v_eave), Vector2(d + e, v_eave),
			Vector2(d - a, 0), Vector2(a, 0))

		# Front triangle (+Z)
		_shell_triangle(st, flip, w, tf,
			c01, c11, ridge_hi,
			Vector3(0, a, p).normalized(),
			Vector2(-e, v_eave), Vector2(w + e, v_eave), Vector2(a, 0))

		# Back triangle (-Z)
		_shell_triangle(st, flip, w, tf,
			c10, c00, ridge_lo,
			Vector3(0, a, -p).normalized(),
			Vector2(-e, v_eave), Vector2(w + e, v_eave), Vector2(a, 0))


## The vertical strip around the eave that closes the two shells into a solid.
## Corner order per face follows the same convention _build_flat uses: the two
## bottom corners left-to-right as seen from outside.
static func _emit_eave_band(st: SurfaceTool, w: float, d: float, e: float,
		drop: float, rise: float, tf: Transform3D) -> void:
	var y := -drop
	var c00 := _p(Vector3(-e, y, -e), tf)
	var c10 := _p(Vector3(w + e, y, -e), tf)
	var c11 := _p(Vector3(w + e, y, d + e), tf)
	var c01 := _p(Vector3(-e, y, d + e), tf)
	var span_x := w + 2.0 * e
	var span_z := d + 2.0 * e

	_add_vertical_quad(st, c01, c11, rise, _n(Vector3(0, 0, 1), tf), span_x)
	_add_vertical_quad(st, c10, c00, rise, _n(Vector3(0, 0, -1), tf), span_x)
	_add_vertical_quad(st, c11, c10, rise, _n(Vector3(1, 0, 0), tf), span_z)
	_add_vertical_quad(st, c00, c01, rise, _n(Vector3(-1, 0, 0), tf), span_z)


# ── Shell face emitters ──────────────────────────────────────────────────────
#
# Reversing a face means three things at once: swapping v1 with v3 (which
# flips the winding of both triangles MeshHelper.add_quad emits), negating the
# normal, and mirroring U about `u_span` so the texture still reads the right
# way round when seen from the other side.

static func _shell_quad(st: SurfaceTool, flip: bool, u_span: float, tf: Transform3D,
		v0: Vector3, v1: Vector3, v2: Vector3, v3: Vector3, normal: Vector3,
		uv0: Vector2, uv1: Vector2, uv2: Vector2, uv3: Vector2) -> void:
	if flip:
		MeshHelper.add_quad(st,
			_p(v0, tf), _p(v3, tf), _p(v2, tf), _p(v1, tf), _n(-normal, tf),
			_mirror_u(uv0, u_span), _mirror_u(uv3, u_span),
			_mirror_u(uv2, u_span), _mirror_u(uv1, u_span))
	else:
		MeshHelper.add_quad(st,
			_p(v0, tf), _p(v1, tf), _p(v2, tf), _p(v3, tf), _n(normal, tf),
			uv0, uv1, uv2, uv3)


static func _shell_triangle(st: SurfaceTool, flip: bool, u_span: float, tf: Transform3D,
		v0: Vector3, v1: Vector3, v2: Vector3, normal: Vector3,
		uv0: Vector2, uv1: Vector2, uv2: Vector2) -> void:
	if flip:
		MeshHelper.add_triangle(st,
			_p(v0, tf), _p(v2, tf), _p(v1, tf), _n(-normal, tf),
			_mirror_u(uv0, u_span), _mirror_u(uv2, u_span), _mirror_u(uv1, u_span))
	else:
		MeshHelper.add_triangle(st,
			_p(v0, tf), _p(v1, tf), _p(v2, tf), _n(normal, tf),
			uv0, uv1, uv2)


static func _mirror_u(uv: Vector2, span: float) -> Vector2:
	return Vector2(span - uv.x, uv.y)
