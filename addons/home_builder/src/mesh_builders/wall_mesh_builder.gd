@tool
class_name WallMeshBuilder
extends RefCounted

## Generates an ArrayMesh for a wall segment with three surfaces:
##   0 = Face A  (front face, normal pointing toward -Z local)
##   1 = Face B  (back face,  normal pointing toward +Z local)
##   2 = Edges   (top, bottom, left, right — the thickness faces)
##
## The wall is centred at the origin in local space:
##   X: -length/2 .. +length/2
##   Y: -height/2 .. +height/2
##   Z: -thickness/2 .. +thickness/2
##
## Multiple openings are supported. Openings must not overlap.
##
## JoinOffsets lets each of the four cap corners slide along local X to
## produce mitered corners at L, T and X junctions (see WallJunctionSolver).
## With zero offsets the mesh is a plain rectangular prism.

const SURFACE_FACE_A := 0
const SURFACE_FACE_B := 1
const SURFACE_EDGES := 2


# ── Public data classes ──────────────────────────────────────────────────────

class Opening:
	var center_x: float  # local X of the opening centre
	var width: float
	var bottom_y: float  # distance from floor (0 = ground level)
	var height: float

	func _init(p_center_x := 0.0, p_width := 0.0, p_bottom_y := 0.0, p_height := 0.0) -> void:
		center_x = p_center_x
		width = p_width
		bottom_y = p_bottom_y
		height = p_height

	func left() -> float:
		return center_x - width * 0.5

	func right() -> float:
		return center_x + width * 0.5

	func local_bottom(hy: float) -> float:  # in mesh local Y
		return bottom_y - hy

	func local_top(hy: float) -> float:  # in mesh local Y
		return bottom_y - hy + height


## Per-endpoint cap offsets used to form clean miter joints when walls
## meet at a corner, T-, or X-junction. Positive values trim the cap
## INWARD from the nominal endpoint; negative values extend it OUTWARD.
## The four fields are applied in the wall's local frame:
##   start_minus_z — start cap corner at local Z = -thickness/2
##   start_plus_z  — start cap corner at local Z = +thickness/2
##   end_minus_z   — end   cap corner at local Z = -thickness/2
##   end_plus_z    — end   cap corner at local Z = +thickness/2
class JoinOffsets:
	var start_minus_z: float
	var start_plus_z: float
	var end_minus_z: float
	var end_plus_z: float

	func _init(p_start_minus_z := 0.0, p_start_plus_z := 0.0,
			p_end_minus_z := 0.0, p_end_plus_z := 0.0) -> void:
		start_minus_z = p_start_minus_z
		start_plus_z = p_start_plus_z
		end_minus_z = p_end_minus_z
		end_plus_z = p_end_plus_z


# ── Public API ───────────────────────────────────────────────────────────────

static func build(length: float, height: float, thickness: float) -> ArrayMesh:
	return build_with_openings_and_joins(length, height, thickness, [], null)


static func build_with_opening(length: float, height: float, thickness: float,
		opening_center_x: float, opening_width: float,
		opening_bottom_y: float, opening_height: float) -> ArrayMesh:
	return build_with_openings_and_joins(length, height, thickness, [
		Opening.new(opening_center_x, opening_width, opening_bottom_y, opening_height)
	], null)


static func build_with_openings(length: float, height: float, thickness: float,
		openings: Array) -> ArrayMesh:
	return build_with_openings_and_joins(length, height, thickness, openings, null)


## [param openings] is an Array of Opening; [param joins] may be null (no offsets).
static func build_with_openings_and_joins(length: float, height: float, thickness: float,
		openings: Array, joins: JoinOffsets) -> ArrayMesh:
	var hx := length * 0.5
	var hy := height * 0.5
	var hz := thickness * 0.5

	var clamped := _clamp_joins(joins if joins != null else JoinOffsets.new(), length)

	# Openings are only validated against the wall's size at the moment
	# they're cut (see OpeningBuilder._cut_opening) — nothing re-validates
	# them if the wall is shrunk afterwards (gizmo length drag, endpoint
	# drag, or a manual Inspector edit). Without clamping here, a door/
	# window whose stored footprint no longer fits can block a face's
	# entire width or height, leaving that surface with zero geometry —
	# generate_tangents() then fails ("UVs are required to generate
	# tangents") and the surface never commits, shifting every later
	# surface index down and breaking the fixed SURFACE_FACE_A/B/EDGES
	# indices HBWall relies on for materials.
	var ops: Array = _sanitize_openings(openings if openings != null else [], hx, hy)
	ops.sort_custom(func(a, b): return a.center_x < b.center_x)

	# Per-face left/right X extents derived from the joins.
	var x_start_mz := -hx + clamped.start_minus_z
	var x_start_pz := -hx + clamped.start_plus_z
	var x_end_mz := hx - clamped.end_minus_z
	var x_end_pz := hx - clamped.end_plus_z

	var mesh := ArrayMesh.new()
	# Face A at z = -hz uses the -Z-side offsets.
	MeshHelper.add_surface(mesh, _build_face(hx, hy, ops, x_start_mz, x_end_mz, -hz, -1.0))
	# Face B at z = +hz uses the +Z-side offsets.
	MeshHelper.add_surface(mesh, _build_face(hx, hy, ops, x_start_pz, x_end_pz, hz, 1.0))
	MeshHelper.add_surface(mesh, _build_edges(hx, hy, hz, ops, x_start_mz, x_start_pz, x_end_mz, x_end_pz))
	return mesh


## Clamps every opening's footprint to fit inside the current [-hx, hx] ×
## [-hy, hy] face bounds, keeping at least _OPENING_MARGIN of solid face on
## every side so a face quad always has area. Openings that no longer fit
## at all (clamped span below the minimum) are dropped from the mesh for
## this rebuild — they reappear once the wall grows back to fit them again,
## since the stored Opening data on the HBWall node itself is untouched.
const _OPENING_MARGIN := 0.01

static func _sanitize_openings(openings: Array, hx: float, hy: float) -> Array:
	var result: Array = []
	for op in openings:
		var left: float = clampf(op.left(), -hx + _OPENING_MARGIN, hx - _OPENING_MARGIN)
		var right: float = clampf(op.right(), -hx + _OPENING_MARGIN, hx - _OPENING_MARGIN)
		if right - left < _OPENING_MARGIN:
			continue

		var local_bottom: float = clampf(op.local_bottom(hy), -hy + _OPENING_MARGIN, hy - _OPENING_MARGIN)
		var local_top: float = clampf(op.local_top(hy), -hy + _OPENING_MARGIN, hy - _OPENING_MARGIN)
		if local_top - local_bottom < _OPENING_MARGIN:
			continue

		result.append(Opening.new(
			(left + right) * 0.5, right - left,
			local_bottom + hy, local_top - local_bottom))
	return result


## Prevents runaway or inverted geometry for very short walls / extreme
## miter angles. The solver already clamps to half-length; this is the
## mesh-side safety net. Returns a new instance; the input is not modified.
static func _clamp_joins(j: JoinOffsets, length: float) -> JoinOffsets:
	var max_each := length * 0.49
	var min_each := -length * 0.49
	var out := JoinOffsets.new(
		clampf(j.start_minus_z, min_each, max_each),
		clampf(j.start_plus_z, min_each, max_each),
		clampf(j.end_minus_z, min_each, max_each),
		clampf(j.end_plus_z, min_each, max_each)
	)

	# Keep a tiny residual length on every side so each face quad has area.
	var min_len := 0.01
	if out.start_minus_z + out.end_minus_z > length - min_len:
		var scale := (length - min_len) / (out.start_minus_z + out.end_minus_z)
		out.start_minus_z *= scale
		out.end_minus_z *= scale
	if out.start_plus_z + out.end_plus_z > length - min_len:
		var scale := (length - min_len) / (out.start_plus_z + out.end_plus_z)
		out.start_plus_z *= scale
		out.end_plus_z *= scale
	return out


# ═════════════════════════════════════════════════════════════════════════════
# UV helpers — map a local X/Y position to 0..1 UV space using the wall's
# nominal half-extents, so the texture doesn't shift when caps are mitered.
# ═════════════════════════════════════════════════════════════════════════════

static func _uv_x(x: float, hx: float) -> float:
	return (x + hx) / (2.0 * hx)


static func _uv_y(y: float, hy: float) -> float:  # V=0 at top
	return (hy - y) / (2.0 * hy)


# ═════════════════════════════════════════════════════════════════════════════
# Face at z = face_z. left_x / right_x are THIS face's slanted cap extents.
# ═════════════════════════════════════════════════════════════════════════════

static func _build_face(hx: float, hy: float, ops: Array,
		left_x: float, right_x: float,
		face_z: float, normal_z: float) -> SurfaceTool:
	var st := SurfaceTool.new()
	var normal := Vector3(0.0, 0.0, normal_z)
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	# Flip X axis for Face B so it faces outward correctly.
	var flip_x := normal_z > 0.0

	if ops.is_empty():
		_emit_face_quad(st, left_x, right_x, -hy, hy, hx, hy, face_z, normal, flip_x)
		return st

	# Collect unique Y levels introduced by openings (exact-value dedup).
	var y_levels: Array[float] = [-hy, hy]
	for op in ops:
		var o_bot: float = op.local_bottom(hy)
		var o_top: float = op.local_top(hy)
		if o_bot > -hy and not y_levels.has(o_bot):
			y_levels.append(o_bot)
		if o_top < hy and not y_levels.has(o_top):
			y_levels.append(o_top)
	y_levels.sort()

	for b in y_levels.size() - 1:
		var y_bot := y_levels[b]
		var y_top := y_levels[b + 1]
		var y_mid := (y_bot + y_top) * 0.5

		# Blocked X spans at this band, stored as Vector2(left, right).
		var blocked: Array[Vector2] = []
		for op in ops:
			var o_bot: float = op.local_bottom(hy)
			var o_top: float = op.local_top(hy)
			if y_mid > o_bot and y_mid < o_top:
				blocked.append(Vector2(op.left(), op.right()))
		blocked.sort_custom(func(p, q): return p.x < q.x)

		var cursor := left_x
		for span in blocked:
			if cursor < span.x:
				_emit_face_quad(st, cursor, span.x, y_bot, y_top, hx, hy, face_z, normal, flip_x)
			cursor = span.y
		if cursor < right_x:
			_emit_face_quad(st, cursor, right_x, y_bot, y_top, hx, hy, face_z, normal, flip_x)

	return st


static func _emit_face_quad(st: SurfaceTool,
		x0: float, x1: float, y0: float, y1: float,
		hx: float, hy: float, face_z: float,
		normal: Vector3, flip_x: bool) -> void:
	if x1 - x0 <= 0.0001:  # skip zero-width spans
		return

	var lx := x1 if flip_x else x0
	var rx := x0 if flip_x else x1

	MeshHelper.add_quad(st,
		Vector3(lx, y1, face_z),
		Vector3(rx, y1, face_z),
		Vector3(rx, y0, face_z),
		Vector3(lx, y0, face_z),
		normal,
		Vector2(_uv_x(lx, hx), _uv_y(y1, hy)),
		Vector2(_uv_x(rx, hx), _uv_y(y1, hy)),
		Vector2(_uv_x(rx, hx), _uv_y(y0, hy)),
		Vector2(_uv_x(lx, hx), _uv_y(y0, hy))
	)


# ═════════════════════════════════════════════════════════════════════════════
# Edge surfaces: top, bottom, slanted caps, and opening frames.
# ═════════════════════════════════════════════════════════════════════════════

static func _build_edges(hx: float, hy: float, hz: float, ops: Array,
		x_start_mz: float, x_start_pz: float,
		x_end_mz: float, x_end_pz: float) -> SurfaceTool:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	# ── Top (+Y): single (possibly trapezoidal) quad ─────────────────────────
	MeshHelper.add_quad(st,
		Vector3(x_start_pz, hy, hz),
		Vector3(x_end_pz, hy, hz),
		Vector3(x_end_mz, hy, -hz),
		Vector3(x_start_mz, hy, -hz),
		Vector3.UP,
		Vector2(_uv_x(x_start_pz, hx), 0),
		Vector2(_uv_x(x_end_pz, hx), 0),
		Vector2(_uv_x(x_end_mz, hx), 1),
		Vector2(_uv_x(x_start_mz, hx), 1)
	)

	# ── Bottom (-Y): split around floor-reaching openings ────────────────────
	var blocked: Array[Vector2] = []
	for op in ops:
		if op.local_bottom(hy) <= -hy:
			blocked.append(Vector2(op.left(), op.right()))
	blocked.sort_custom(func(p, q): return p.x < q.x)

	# Walk along X, emitting trapezoidal pieces. The left/right edges
	# of each piece can be slanted (at a cap) or flat (at an opening).
	var left_mz := x_start_mz
	var left_pz := x_start_pz
	for span in blocked:
		_emit_bottom_quad(st, left_mz, left_pz, span.x, span.x, hx, hy, hz)
		left_mz = span.y
		left_pz = span.y
	_emit_bottom_quad(st, left_mz, left_pz, x_end_mz, x_end_pz, hx, hy, hz)

	# ── Start cap (outer -X end, possibly tilted) ────────────────────────────
	_emit_start_cap(st, x_start_mz, x_start_pz, hy, hz)

	# ── End cap (outer +X end, possibly tilted) ──────────────────────────────
	_emit_end_cap(st, x_end_mz, x_end_pz, hy, hz)

	# ── Opening frames ───────────────────────────────────────────────────────
	var adjacent_epsilon := 0.001
	for i in ops.size():
		var op = ops[i]
		var o_left: float = op.left()
		var o_right: float = op.right()
		var o_bottom: float = op.local_bottom(hy)
		var o_top: float = op.local_top(hy)

		var left_adjacent_to_prev: bool = (
			i > 0 and absf(o_left - ops[i - 1].right()) < adjacent_epsilon
		)
		var right_adjacent_to_next: bool = (
			i < ops.size() - 1 and absf(o_right - ops[i + 1].left()) < adjacent_epsilon
		)

		# Top lintel
		MeshHelper.add_quad(st,
			Vector3(o_right, o_top, hz),
			Vector3(o_left, o_top, hz),
			Vector3(o_left, o_top, -hz),
			Vector3(o_right, o_top, -hz),
			Vector3.DOWN,
			Vector2(_uv_x(o_right, hx), 0),
			Vector2(_uv_x(o_left, hx), 0),
			Vector2(_uv_x(o_left, hx), 1),
			Vector2(_uv_x(o_right, hx), 1)
		)

		# Sill (windows only)
		if o_bottom > -hy:
			MeshHelper.add_quad(st,
				Vector3(o_left, o_bottom, hz),
				Vector3(o_right, o_bottom, hz),
				Vector3(o_right, o_bottom, -hz),
				Vector3(o_left, o_bottom, -hz),
				Vector3.UP,
				Vector2(_uv_x(o_left, hx), 0),
				Vector2(_uv_x(o_right, hx), 0),
				Vector2(_uv_x(o_right, hx), 1),
				Vector2(_uv_x(o_left, hx), 1)
			)

		# Left jamb — omitted when the previous opening is flush against this one
		if not left_adjacent_to_prev:
			MeshHelper.add_quad(st,
				Vector3(o_left, o_top, hz),
				Vector3(o_left, o_bottom, hz),
				Vector3(o_left, o_bottom, -hz),
				Vector3(o_left, o_top, -hz),
				Vector3.RIGHT,
				Vector2(0, _uv_y(o_top, hy)),
				Vector2(0, _uv_y(o_bottom, hy)),
				Vector2(1, _uv_y(o_bottom, hy)),
				Vector2(1, _uv_y(o_top, hy))
			)

		# Right jamb — omitted when the next opening is flush against this one
		if not right_adjacent_to_next:
			MeshHelper.add_quad(st,
				Vector3(o_right, o_bottom, hz),
				Vector3(o_right, o_top, hz),
				Vector3(o_right, o_top, -hz),
				Vector3(o_right, o_bottom, -hz),
				Vector3.LEFT,
				Vector2(0, _uv_y(o_bottom, hy)),
				Vector2(0, _uv_y(o_top, hy)),
				Vector2(1, _uv_y(o_top, hy)),
				Vector2(1, _uv_y(o_bottom, hy))
			)

	return st


static func _emit_bottom_quad(st: SurfaceTool,
		left_mz: float, left_pz: float, right_mz: float, right_pz: float,
		hx: float, hy: float, hz: float) -> void:
	# Skip pieces with no area on either side.
	if right_mz - left_mz <= 0.0001 and right_pz - left_pz <= 0.0001:
		return

	# Matches the winding of the original axis-aligned bottom quads.
	MeshHelper.add_quad(st,
		Vector3(right_pz, -hy, hz),
		Vector3(left_pz, -hy, hz),
		Vector3(left_mz, -hy, -hz),
		Vector3(right_mz, -hy, -hz),
		Vector3.DOWN,
		Vector2(_uv_x(right_pz, hx), 0),
		Vector2(_uv_x(left_pz, hx), 0),
		Vector2(_uv_x(left_mz, hx), 1),
		Vector2(_uv_x(right_mz, hx), 1)
	)


## Start cap: outward normal points roughly -X, tilted by the offset
## difference between the two sides.
static func _emit_start_cap(st: SurfaceTool,
		x_start_mz: float, x_start_pz: float, hy: float, hz: float) -> void:
	var cap_tangent := Vector3(x_start_pz - x_start_mz, 0.0, 2.0 * hz)
	if cap_tangent.length_squared() < 1e-8:
		return
	var normal := cap_tangent.cross(Vector3.UP).normalized()  # outward = away from +X wall body

	MeshHelper.add_quad(st,
		Vector3(x_start_mz, hy, -hz),
		Vector3(x_start_mz, -hy, -hz),
		Vector3(x_start_pz, -hy, hz),
		Vector3(x_start_pz, hy, hz),
		normal,
		Vector2(0, 0), Vector2(0, 1),
		Vector2(1, 1), Vector2(1, 0)
	)


## End cap: outward normal points roughly +X, tilted by the offset
## difference between the two sides.
static func _emit_end_cap(st: SurfaceTool,
		x_end_mz: float, x_end_pz: float, hy: float, hz: float) -> void:
	var cap_tangent := Vector3(x_end_mz - x_end_pz, 0.0, -2.0 * hz)
	if cap_tangent.length_squared() < 1e-8:
		return
	var normal := cap_tangent.cross(Vector3.UP).normalized()  # outward = away from -X wall body

	MeshHelper.add_quad(st,
		Vector3(x_end_pz, hy, hz),
		Vector3(x_end_pz, -hy, hz),
		Vector3(x_end_mz, -hy, -hz),
		Vector3(x_end_mz, hy, -hz),
		normal,
		Vector2(0, 0), Vector2(0, 1),
		Vector2(1, 1), Vector2(1, 0)
	)
