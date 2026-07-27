@tool
class_name RoofMeshBuilder
extends RefCounted

## Generates an ArrayMesh for a roof covering a footprint of (w × d).
##
## Every type is built the same way: an inner shell (the soffit you see from
## below), an outer shell raised above it, and a band around the perimeter
## closing the two into a watertight solid. There is no horizontal base — the
## designer owns the space underneath. The surfaces are:
##   TOP    = outward-facing slope skin
##   BOTTOM = the soffit (the inner shell, inverted)
##   SIDES  = the band around the perimeter — the visible thickness at the eave
##
## SHED and GABLE additionally have vertical end walls — the shed's back wall
## and side triangles, the gable's two ends. Those are walls, so they get the
## same three slots HBWall does, and for the same reason (each side can be
## painted differently):
##   END_FACE_A = the outward face, flush with the facade below
##   END_FACE_B = the inward face, seen from inside the roof
##   END_EDGES  = the rim around the outline, i.e. the wall's thickness
##
## Surface indices are NOT fixed across types, because the end-wall surfaces
## only exist for some of them; callers assigning per-surface materials must
## walk surface_layout() (see HBRoof._apply_materials).
##
## Local origin sits at the minimum corner (0, 0, 0). The footprint spans
## (0..w, 0..d) and the ridge reaches `pitch`; the eave overhang prolongs the
## slope faces outside that box and the thickness raises the outer shell above
## it. Direction is applied as a 90° rotation around +Y about the footprint
## center.

enum RoofType { FLAT, SHED, GABLE, HIP }
enum RoofDirection { NORTH, SOUTH, EAST, WEST }
enum Surface { TOP, BOTTOM, SIDES, END_FACE_A, END_FACE_B, END_EDGES }

## Coincident inner/outer shells would leave the perimeter band with zero
## area, and generate_tangents() fails on a surface with no geometry.
const _MIN_THICKNESS := 0.01


## Which surfaces a type emits, in mesh order. FLAT and HIP have no vertical
## end walls: a flat roof closes on its own band, and every side of a hip is a
## slope already part of TOP.
static func surface_layout(type: RoofType) -> Array:
	if type == RoofType.FLAT or type == RoofType.HIP:
		return [Surface.TOP, Surface.BOTTOM, Surface.SIDES]
	return [Surface.TOP, Surface.BOTTOM, Surface.SIDES,
		Surface.END_FACE_A, Surface.END_FACE_B, Surface.END_EDGES]


static func build(type: RoofType, w: float, d: float, pitch: float,
		dir: RoofDirection, eave: float = 0.0, thickness: float = 0.0,
		end_thickness: float = 0.1) -> ArrayMesh:
	var layout := surface_layout(type)
	var tools := {}
	for role in layout:
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		tools[role] = st

	var rot := _rot_steps(type, dir)
	var swap := (rot % 2) != 0
	var cw := d if swap else w
	var cd := w if swap else d
	var tf := _make_orient(rot, cw, cd, w, d)

	match type:
		RoofType.FLAT:
			_build_flat(tools, cw, cd, eave, thickness, tf)
		RoofType.SHED:
			_build_shed(tools, cw, cd, pitch, eave, thickness, end_thickness, tf)
		RoofType.GABLE:
			_build_gable(tools, cw, cd, pitch, eave, thickness, end_thickness, tf)
		RoofType.HIP:
			_build_hip(tools, cw, cd, pitch, eave, thickness, tf)

	var mesh := ArrayMesh.new()
	for role in layout:
		MeshHelper.add_surface(mesh, tools[role])
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
#
# The degenerate case of the shared scheme: the "slope" is horizontal, so the
# eave just widens the slab in every direction with no drop, and the thickness
# raises the top face without any slope correction. Soffit at y = 0 (the top
# of the walls), skin at y = t — the same convention the sloped types use.

static func _build_flat(tools: Dictionary, w: float, d: float, e: float,
		thickness: float, tf: Transform3D) -> void:
	var top: SurfaceTool = tools[Surface.TOP]
	var bot: SurfaceTool = tools[Surface.BOTTOM]
	var sides: SurfaceTool = tools[Surface.SIDES]
	var t := maxf(thickness, _MIN_THICKNESS)
	var x0 := -e
	var x1 := w + e
	var z0 := -e
	var z1 := d + e

	MeshHelper.add_quad(top,
		_v(x0, t, z1, tf), _v(x1, t, z1, tf), _v(x1, t, z0, tf), _v(x0, t, z0, tf),
		_n(Vector3.UP, tf),
		Vector2(x0, z1), Vector2(x1, z1), Vector2(x1, z0), Vector2(x0, z0))

	# Mirrored U, so the soffit texture reads the right way round from below.
	MeshHelper.add_quad(bot,
		_v(x1, 0, z1, tf), _v(x0, 0, z1, tf), _v(x0, 0, z0, tf), _v(x1, 0, z0, tf),
		_n(Vector3.DOWN, tf),
		Vector2(x0, z1), Vector2(x1, z1), Vector2(x1, z0), Vector2(x0, z0))

	var span_x := x1 - x0
	var span_z := z1 - z0
	_add_vertical_quad(sides, _v(x0, 0, z1, tf), _v(x1, 0, z1, tf), t,
		_n(Vector3(0, 0, 1), tf), span_x)
	_add_vertical_quad(sides, _v(x1, 0, z0, tf), _v(x0, 0, z0, tf), t,
		_n(Vector3(0, 0, -1), tf), span_x)
	_add_vertical_quad(sides, _v(x1, 0, z1, tf), _v(x1, 0, z0, tf), t,
		_n(Vector3(1, 0, 0), tf), span_z)
	_add_vertical_quad(sides, _v(x0, 0, z0, tf), _v(x0, 0, z1, tf), t,
		_n(Vector3(-1, 0, 0), tf), span_z)


static func _add_vertical_quad(st: SurfaceTool, a: Vector3, b: Vector3,
		h: float, normal: Vector3, length: float) -> void:
	var top1 := a + Vector3(0, h, 0)
	var top2 := b + Vector3(0, h, 0)
	MeshHelper.add_quad(st, a, b, top2, top1, normal,
		Vector2(0, h), Vector2(length, h),
		Vector2(length, 0), Vector2(0, 0))


# ── Shed — high edge at -Z ───────────────────────────────────────────────────
#
# A single plane, y = p·(d - z)/d, extended `e` beyond the footprint on all
# four sides: at +Z it drops below the eave, at -Z it keeps climbing past the
# back wall, and at ±X it just runs on at constant height. The run is the full
# depth `d`, so the slope factor is √(d² + p²)/d.

static func _build_shed(tools: Dictionary, w: float, d: float, p: float,
		e: float, thickness: float, et: float, tf: Transform3D) -> void:
	var slope := 1.0
	var drop := 0.0
	if d > 0.001:
		slope = sqrt(d * d + p * p) / d
		drop = e * p / d
	var rise := maxf(thickness, _MIN_THICKNESS) * slope

	_emit_shed_shell(tools[Surface.BOTTOM], w, d, p, e, drop, slope, 0.0, true, tf)
	_emit_shed_shell(tools[Surface.TOP], w, d, p, e, drop, slope, rise, false, tf)
	_emit_shed_band(tools[Surface.SIDES], w, d, p, e, drop, rise, tf)
	_emit_shed_ends(tools, w, d, p, et, tf)


static func _emit_shed_shell(st: SurfaceTool, w: float, d: float, p: float,
		e: float, drop: float, slope: float, y_offset: float, flip: bool,
		tf: Transform3D) -> void:
	var y_lo := -drop + y_offset       # at z = d + e
	var y_hi := p + drop + y_offset    # at z = -e

	# V is the distance from the high edge measured along the slope.
	_shell_quad(st, flip, w, tf,
		Vector3(-e, y_lo, d + e), Vector3(w + e, y_lo, d + e),
		Vector3(w + e, y_hi, -e), Vector3(-e, y_hi, -e),
		Vector3(0, d, p).normalized(),
		Vector2(-e, (d + e) * slope), Vector2(w + e, (d + e) * slope),
		Vector2(w + e, -e * slope), Vector2(-e, -e * slope))


static func _emit_shed_band(st: SurfaceTool, w: float, d: float, p: float,
		e: float, drop: float, rise: float, tf: Transform3D) -> void:
	var y_lo := -drop
	var y_hi := p + drop
	var lo_l := _p(Vector3(-e, y_lo, d + e), tf)
	var lo_r := _p(Vector3(w + e, y_lo, d + e), tf)
	var hi_l := _p(Vector3(-e, y_hi, -e), tf)
	var hi_r := _p(Vector3(w + e, y_hi, -e), tf)
	var span_x := w + 2.0 * e
	var run := d + 2.0 * e
	var slant := sqrt(run * run + (y_hi - y_lo) * (y_hi - y_lo))

	_add_vertical_quad(st, lo_l, lo_r, rise, _n(Vector3(0, 0, 1), tf), span_x)
	_add_vertical_quad(st, hi_r, hi_l, rise, _n(Vector3(0, 0, -1), tf), span_x)
	# The ±X edges follow the slope, so these two are parallelograms.
	_add_vertical_quad(st, hi_l, lo_l, rise, _n(Vector3(-1, 0, 0), tf), slant)
	_add_vertical_quad(st, lo_r, hi_r, rise, _n(Vector3(1, 0, 0), tf), slant)


## Back wall and the two side triangles, at the footprint edge with the roof
## overhanging them — so they line up with the walls below, not with the eave.
##
## These three prisms meet at the two vertical edges (0,·,0) and (w,·,0), so
## the rim quad each would draw there is skipped on both sides of every such
## edge (see _emit_end_prism's [param skip_rim] doc).
static func _emit_shed_ends(tools: Dictionary, w: float, d: float, p: float,
		et: float, tf: Transform3D) -> void:
	_emit_end_prism(tools, tf,
		[Vector3(w, 0, 0), Vector3(0, 0, 0), Vector3(0, p, 0), Vector3(w, p, 0)],
		[Vector2(0, 0), Vector2(w, 0), Vector2(w, p), Vector2(0, p)],
		Vector3(0, 0, -1), et, w, [1, 3])

	_emit_end_prism(tools, tf,
		[Vector3(0, 0, 0), Vector3(0, 0, d), Vector3(0, p, 0)],
		[Vector2(0, 0), Vector2(d, 0), Vector2(0, p)],
		Vector3(-1, 0, 0), et, d, [2])

	_emit_end_prism(tools, tf,
		[Vector3(w, 0, d), Vector3(w, 0, 0), Vector3(w, p, 0)],
		[Vector2(0, 0), Vector2(d, 0), Vector2(d, p)],
		Vector3(1, 0, 0), et, d, [1])


# ── Gable — ridge along X at z = d/2 ─────────────────────────────────────────
#
# Here the eave means two different things at once. At ±Z it is the eave
# proper: the slopes are prolonged outward and drop by e·p/a, exactly as on a
# hip. At ±X it is the rake overhang over the gable ends: the ridge simply
# runs on past x = 0 and x = w at constant height, because the slope does not
# vary along X. One `e` drives both — split it only if that ever looks wrong.
#
# The gable ends themselves stay at the footprint edge with the roof flying
# over them, so they line up with the walls below.

static func _build_gable(tools: Dictionary, w: float, d: float, p: float,
		e: float, thickness: float, et: float, tf: Transform3D) -> void:
	var a := d * 0.5
	var slope := 1.0
	var drop := 0.0
	if a > 0.001:
		slope = sqrt(a * a + p * p) / a
		drop = e * p / a
	var rise := maxf(thickness, _MIN_THICKNESS) * slope

	_emit_gable_shell(tools[Surface.BOTTOM], w, d, p, e, drop, slope, 0.0, true, tf)
	_emit_gable_shell(tools[Surface.TOP], w, d, p, e, drop, slope, rise, false, tf)
	_emit_gable_band(tools[Surface.SIDES], w, d, p, e, drop, rise, tf)
	_emit_gable_ends(tools, w, d, p, et, tf)


static func _emit_gable_shell(st: SurfaceTool, w: float, d: float, p: float,
		e: float, drop: float, slope: float, y_offset: float, flip: bool,
		tf: Transform3D) -> void:
	var a := d * 0.5
	var y_eave := -drop + y_offset
	var y_ridge := p + y_offset
	var v_eave := (a + e) * slope

	# Front slope (+Z)
	_shell_quad(st, flip, w, tf,
		Vector3(-e, y_eave, d + e), Vector3(w + e, y_eave, d + e),
		Vector3(w + e, y_ridge, a), Vector3(-e, y_ridge, a),
		Vector3(0, a, p).normalized(),
		Vector2(-e, v_eave), Vector2(w + e, v_eave),
		Vector2(w + e, 0), Vector2(-e, 0))

	# Back slope (-Z)
	_shell_quad(st, flip, w, tf,
		Vector3(-e, y_ridge, a), Vector3(w + e, y_ridge, a),
		Vector3(w + e, y_eave, -e), Vector3(-e, y_eave, -e),
		Vector3(0, a, -p).normalized(),
		Vector2(-e, 0), Vector2(w + e, 0),
		Vector2(w + e, v_eave), Vector2(-e, v_eave))


static func _emit_gable_band(st: SurfaceTool, w: float, d: float, p: float,
		e: float, drop: float, rise: float, tf: Transform3D) -> void:
	var a := d * 0.5
	var y_eave := -drop
	var f_l := _p(Vector3(-e, y_eave, d + e), tf)
	var f_r := _p(Vector3(w + e, y_eave, d + e), tf)
	var b_l := _p(Vector3(-e, y_eave, -e), tf)
	var b_r := _p(Vector3(w + e, y_eave, -e), tf)
	var r_l := _p(Vector3(-e, p, a), tf)
	var r_r := _p(Vector3(w + e, p, a), tf)
	var span_x := w + 2.0 * e
	var slant := sqrt((a + e) * (a + e) + (p + drop) * (p + drop))

	_add_vertical_quad(st, f_l, f_r, rise, _n(Vector3(0, 0, 1), tf), span_x)
	_add_vertical_quad(st, b_r, b_l, rise, _n(Vector3(0, 0, -1), tf), span_x)
	# Each rake is a chevron, so it takes one parallelogram per slope.
	_add_vertical_quad(st, b_l, r_l, rise, _n(Vector3(-1, 0, 0), tf), slant)
	_add_vertical_quad(st, r_l, f_l, rise, _n(Vector3(-1, 0, 0), tf), slant)
	_add_vertical_quad(st, f_r, r_r, rise, _n(Vector3(1, 0, 0), tf), slant)
	_add_vertical_quad(st, r_r, b_r, rise, _n(Vector3(1, 0, 0), tf), slant)


static func _emit_gable_ends(tools: Dictionary, w: float, d: float, p: float,
		et: float, tf: Transform3D) -> void:
	var a := d * 0.5

	_emit_end_prism(tools, tf,
		[Vector3(0, 0, 0), Vector3(0, 0, d), Vector3(0, p, a)],
		[Vector2(0, 0), Vector2(d, 0), Vector2(a, p)],
		Vector3(-1, 0, 0), et, d)

	_emit_end_prism(tools, tf,
		[Vector3(w, 0, d), Vector3(w, 0, 0), Vector3(w, p, a)],
		[Vector2(0, 0), Vector2(d, 0), Vector2(a, p)],
		Vector3(1, 0, 0), et, d)


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

static func _build_hip(tools: Dictionary, w: float, d: float, p: float,
		e: float, t: float, tf: Transform3D) -> void:
	var a := (d * 0.5) if w >= d else (w * 0.5)

	# A degenerate footprint (a → 0) makes both the drop and the slope factor
	# diverge; fall back to a horizontal overhang with plan-projected UVs.
	var slope := 1.0
	var drop := 0.0
	if a > 0.001:
		slope = sqrt(a * a + p * p) / a
		drop = e * p / a

	var rise := maxf(t, _MIN_THICKNESS) * slope

	_emit_hip_shell(tools[Surface.BOTTOM], w, d, p, e, drop, slope, 0.0, true, tf)
	_emit_hip_shell(tools[Surface.TOP], w, d, p, e, drop, slope, rise, false, tf)
	_emit_eave_band(tools[Surface.SIDES], w, d, e, drop, rise, tf)


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


# ── Gable ends ───────────────────────────────────────────────────────────────

## Extrudes a flat vertical outline inward into a solid slab, so a gable end
## reads as a wall from both sides instead of as a single-sided sheet. It is
## pushed inward from the footprint edge by `et` for the same reason the ends
## sit there at all: to continue the wall below, whose outer face is flush
## with the footprint.
##
## [param poly] is 3 or 4 points, wound CCW as seen from [param normal], and
## [param uvs] matches it one-for-one. The outward face, the inward face and
## the rim go to three separate surfaces, mirroring how HBWall splits a wall
## into Face A, Face B and Edges, so each side can be painted on its own.
##
## [param skip_rim] lists edge indices (into [param poly]) whose rim quad to
## omit. SHED's back wall and its two side triangles are three independent
## prisms meeting at right angles at the footprint corners — without this,
## each one's rim there is coplanar with, and entirely covered by, the
## neighbouring prism's own face_a, exactly the way two HBWalls without a
## WallJunctionSolver miter would overlap through their shared thickness. The
## fix is the mirror image of a miter: instead of computing a diagonal cut,
## just don't draw the redundant duplicate face. This leaves a sliver at the
## very top of the corner uncovered — the neighbour's face_a only reaches the
## ridgeline, while the skipped rim quad was flat — but it's bounded by
## et·pitch/depth, a few millimetres for the addon's usual proportions, right
## at the corner where the roof skin sits above it; not worth a true mitered
## cut for that. GABLE's two ends sit at opposite corners of the building and
## never share an edge, so they never need this.
static func _emit_end_prism(tools: Dictionary, tf: Transform3D,
		poly: Array, uvs: Array, normal: Vector3, et: float,
		u_span: float, skip_rim: Array = []) -> void:
	var face_a: SurfaceTool = tools[Surface.END_FACE_A]
	var face_b: SurfaceTool = tools[Surface.END_FACE_B]
	var edges: SurfaceTool = tools[Surface.END_EDGES]
	var back := -normal * et
	var n := poly.size()

	if n == 3:
		MeshHelper.add_triangle(face_a,
			_p(poly[0], tf), _p(poly[1], tf), _p(poly[2], tf),
			_n(normal, tf), uvs[0], uvs[1], uvs[2])
		MeshHelper.add_triangle(face_b,
			_p(poly[0] + back, tf), _p(poly[2] + back, tf), _p(poly[1] + back, tf),
			_n(-normal, tf),
			_mirror_u(uvs[0], u_span), _mirror_u(uvs[2], u_span),
			_mirror_u(uvs[1], u_span))
	else:
		MeshHelper.add_quad(face_a,
			_p(poly[0], tf), _p(poly[1], tf), _p(poly[2], tf), _p(poly[3], tf),
			_n(normal, tf), uvs[0], uvs[1], uvs[2], uvs[3])
		MeshHelper.add_quad(face_b,
			_p(poly[0] + back, tf), _p(poly[3] + back, tf),
			_p(poly[2] + back, tf), _p(poly[1] + back, tf),
			_n(-normal, tf),
			_mirror_u(uvs[0], u_span), _mirror_u(uvs[3], u_span),
			_mirror_u(uvs[2], u_span), _mirror_u(uvs[1], u_span))

	# Rim: one quad per edge of the outline. `edge × normal` points away from
	# the outline for a CCW winding, which is the outward direction we need.
	for i in n:
		if skip_rim.has(i):
			continue
		var a: Vector3 = poly[i]
		var b: Vector3 = poly[(i + 1) % n]
		var edge := b - a
		if edge.length_squared() < 1e-8:
			continue
		MeshHelper.add_quad(edges,
			_p(a, tf), _p(a + back, tf), _p(b + back, tf), _p(b, tf),
			_n(edge.cross(normal).normalized(), tf),
			Vector2(0, 0), Vector2(et, 0),
			Vector2(et, edge.length()), Vector2(0, edge.length()))
