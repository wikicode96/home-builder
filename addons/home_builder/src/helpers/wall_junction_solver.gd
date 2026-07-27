@tool
class_name WallJunctionSolver
extends RefCounted

## Computes per-endpoint miter offsets for wall segments so that corners,
## T-junctions and X-junctions meet cleanly, without gaps or overlaps, for
## ANY pair of angles (not just 90°).
##
## --- Algorithm --------------------------------------------------------------
## At every point in the world XZ plane where two or more walls converge we
## build a "node". Each wall end-point that lies at the node contributes a
## "half-edge" whose inward direction points from the node INTO the wall
## body. When a wall passes straight through the node (T-junction), we add
## two VIRTUAL half-edges that represent the two continuing halves of the
## through-wall; these constrain the miter but themselves receive no offset.
##
## For every pair (a, b) of angularly adjacent half-edges (in CCW order
## around the node) we intersect:
##   • a's CCW-side edge line, offset by perp_ccw(d_a) · thickness/2
##   • b's CW-side edge line,  offset by -perp_ccw(d_b) · thickness/2
## The intersection M is the miter point between a and b. The parametric
## distances s_a and s_b from the node along each half-edge's inward
## direction give the cap offsets applied to wall a (on its CCW side) and
## wall b (on its CW side).
##
## --- Local frame conventions ------------------------------------------------
## Every wall is a StaticBody3D whose basis.x runs from the wall's Start
## endpoint to its End endpoint in world space. Because basis.z = Y × X, at
## the Start endpoint the CCW side of the inward direction maps to local -Z,
## and at the End endpoint it maps to local +Z. See _store_offset below.
##
## --- Edge cases -------------------------------------------------------------
##   • Two walls collinear and opposite (180°): their shared miter is
##     parallel and at infinity — we simply leave the offset at zero on that
##     side, so the walls continue as a straight strip.
##   • Two walls overlapping (0°): treated the same way, skipped.
##   • Acute corners: miter distance = thickness / (2·sin(half-angle)) can
##     become large. The solver clamps each offset to half the wall's length.

## Two world positions within this distance are treated as the same node.
const _POS_TOLERANCE := 0.01
## A point must be at least this far from either endpoint of a wall to
## count as "in the middle" (T-junction detection).
const _MIN_MID_DISTANCE := 0.02
## 2D cross products below this magnitude are treated as parallel.
const _PARALLEL_EPSILON := 1e-5

enum _Kind { START, END, MID_VIRTUAL }


class Offsets:
	var start_minus_z: float  # cap offset at Start on local -Z side
	var start_plus_z: float   # cap offset at Start on local +Z side
	var end_minus_z: float    # cap offset at End   on local -Z side
	var end_plus_z: float     # cap offset at End   on local +Z side


## Convex polygon (world XZ) that fills the gap at a multi-wall junction.
## Points are in CCW order matching the half-edge sort order.
class JunctionFill:
	var points_xz: PackedVector2Array

	func _init(p_points_xz := PackedVector2Array()) -> void:
		points_xz = p_points_xz


class SolveResult:
	var offsets: Dictionary = {}  # StaticBody3D -> Offsets
	var fills: Array = []         # of JunctionFill


class _HalfEdge:
	var wall: StaticBody3D
	var kind: _Kind
	var node_pos: Vector3
	var dir: Vector2  # unit XZ inward direction
	var wall_length: float
	var thickness: float

	func _init(p_wall: StaticBody3D, p_kind: _Kind, p_node_pos: Vector3,
			p_dir: Vector2, p_wall_length: float, p_thickness: float) -> void:
		wall = p_wall
		kind = p_kind
		node_pos = p_node_pos
		dir = p_dir
		wall_length = p_wall_length
		thickness = p_thickness


# ── Public API ───────────────────────────────────────────────────────────────

## Returns every OTHER wall under [param wall_parent] that currently forms a
## junction with one of [param wall]'s two endpoints — either by sharing
## that exact point (L/X corner) or by [param wall]'s endpoint resting on
## the middle of the other wall (a T, with [param wall] as the leg touching
## the other wall's middle). Used by use-case code (e.g. the gizmo) to know,
## BEFORE an edit starts, which neighbours need to be re-solved once
## [param wall] no longer touches them.
static func find_corner_partners(wall: HBWall, wall_parent: Node3D) -> Array[HBWall]:
	var result: Array[HBWall] = []
	if wall_parent == null or wall == null:
		return result

	var w_len := WallHelper.get_wall_length(wall)
	if w_len <= 0.0:
		return result
	var w_tr := wall.global_transform
	var w_axis := w_tr.basis.x
	var endpoints := [
		w_tr.origin - w_axis * (w_len * 0.5),
		w_tr.origin + w_axis * (w_len * 0.5),
	]

	for child in wall_parent.get_children():
		if child == wall or not (child is HBWall):
			continue
		var other: HBWall = child
		var o_len := WallHelper.get_wall_length(other)
		if o_len <= 0.0:
			continue
		var o_tr := other.global_transform
		var o_axis := o_tr.basis.x
		var o_d2 := Vector2(o_axis.x, o_axis.z)
		if o_d2.length_squared() < 1e-8:
			continue
		o_d2 = o_d2.normalized()
		var o_start := o_tr.origin - o_axis * (o_len * 0.5)
		var o_end := o_tr.origin + o_axis * (o_len * 0.5)

		for p in endpoints:
			if _close_xz(p, o_start) or _close_xz(p, o_end):
				result.append(other)
				break
			var rel := Vector2(p.x - o_start.x, p.z - o_start.z)
			var t_along := rel.dot(o_d2)
			var t_perp := rel.dot(Vector2(-o_d2.y, o_d2.x))
			if absf(t_perp) <= _POS_TOLERANCE \
					and t_along > _MIN_MID_DISTANCE and t_along < o_len - _MIN_MID_DISTANCE:
				result.append(other)
				break

	return result


## Solves all junctions under [param wall_parent]. Returns per-wall cap
## offsets and a list of fill polygons (one per junction gap needing cover).
## [param wall_parent] may also contain non-wall siblings (floor slabs,
## roof, stairs, fences all live under the same per-floor node) — only
## HBWall children take part in the solve.
static func solve(wall_parent: Node3D) -> SolveResult:
	var result := SolveResult.new()
	if wall_parent == null:
		return result

	# ── Collect walls and per-wall info
	var walls: Array[StaticBody3D] = []
	var start_pos := {}  # StaticBody3D -> Vector3
	var end_pos := {}    # StaticBody3D -> Vector3
	var wall_dir := {}   # StaticBody3D -> Vector2
	var wall_len := {}   # StaticBody3D -> float
	var wall_thk := {}   # StaticBody3D -> float

	for child in wall_parent.get_children():
		if not (child is HBWall):
			continue
		var b: StaticBody3D = child
		var body_len := WallHelper.get_wall_length(b)
		if body_len <= 0.0:
			continue

		var tr := b.global_transform
		var axis := tr.basis.x
		var d2 := Vector2(axis.x, axis.z)
		if d2.length_squared() < 1e-8:
			continue
		d2 = d2.normalized()

		walls.append(b)
		start_pos[b] = tr.origin - axis * (body_len * 0.5)
		end_pos[b] = tr.origin + axis * (body_len * 0.5)
		wall_dir[b] = d2
		wall_len[b] = body_len
		wall_thk[b] = WallHelper.get_wall_thickness(b)
		result.offsets[b] = Offsets.new()

	if walls.is_empty():
		return result

	# ── Build half-edges at both endpoints of every wall
	var half_edges: Array[_HalfEdge] = []
	for w in walls:
		half_edges.append(_HalfEdge.new(
			w, _Kind.START, start_pos[w],
			wall_dir[w],                    # Start inward = +axis
			wall_len[w], wall_thk[w]))
		half_edges.append(_HalfEdge.new(
			w, _Kind.END, end_pos[w],
			-wall_dir[w],                   # End inward = -axis
			wall_len[w], wall_thk[w]))

	# ── Group half-edges into nodes by world XZ position (with tolerance)
	var nodes: Array = []  # of Array[_HalfEdge]
	for he in half_edges:
		var placed := false
		for node in nodes:
			if _close_xz(node[0].node_pos, he.node_pos):
				node.append(he)
				placed = true
				break
		if not placed:
			nodes.append([he])

	# ── Detect T-junctions: an endpoint sitting on another wall's
	#    centerline (away from that other wall's endpoints). We insert
	#    two virtual half-edges so the miter code handles the T like any
	#    other N-way junction — but the through-wall receives no offsets.
	for node in nodes:
		var p: Vector3 = node[0].node_pos
		for w in walls:
			# Skip if this wall is already terminating at this node.
			var already_here := false
			for he in node:
				if he.wall == w:
					already_here = true
					break
			if already_here:
				continue

			var dir: Vector2 = wall_dir[w]
			var s: Vector3 = start_pos[w]
			var rel := Vector2(p.x - s.x, p.z - s.z)
			var t_along := rel.dot(dir)
			var t_perp := rel.dot(Vector2(-dir.y, dir.x))
			if absf(t_perp) > _POS_TOLERANCE:
				continue

			var length: float = wall_len[w]
			if t_along < _MIN_MID_DISTANCE or t_along > length - _MIN_MID_DISTANCE:
				continue

			# Virtual half-edges in both directions along the through-wall.
			node.append(_HalfEdge.new(
				w, _Kind.MID_VIRTUAL, p, dir, length - t_along, wall_thk[w]))
			node.append(_HalfEdge.new(
				w, _Kind.MID_VIRTUAL, p, -dir, t_along, wall_thk[w]))

	# ── Solve miters per node
	for node in nodes:
		if node.size() < 2:
			continue
		node.sort_custom(func(a, b):
			return atan2(a.dir.y, a.dir.x) < atan2(b.dir.y, b.dir.x))

		var n: int = node.size()
		var miter_pts := PackedVector2Array()

		for i in n:
			var a: _HalfEdge = node[i]
			var b: _HalfEdge = node[(i + 1) % n]
			var miter = _try_miter(a, b)
			if miter == null:
				# Antiparallel real walls (e.g. two collinear walls both
				# terminating at this node, plus a third branch): _try_miter
				# finds no unique intersection because the miter lines are
				# coincident. The gap vertex is the node offset by
				# perp_ccw(da) * thickness/2 — the exterior face of the
				# straight joint. We only do this for real (non-virtual)
				# half-edges; a virtual antiparallel pair means a single
				# through-wall whose own top surface already covers the node.
				if a.kind != _Kind.MID_VIRTUAL and b.kind != _Kind.MID_VIRTUAL:
					var dot := a.dir.x * b.dir.x + a.dir.y * b.dir.y
					if dot < -1.0 + _PARALLEL_EPSILON * 10.0:
						var p_xz2 := Vector2(a.node_pos.x, a.node_pos.z)
						var n_la2 := Vector2(-a.dir.y, a.dir.x)
						miter_pts.append(p_xz2 + n_la2 * (a.thickness * 0.5))
				continue

			# Clamp so we never trim more than half the wall's length.
			var sa := clampf(miter.x, -a.wall_length * 0.5, a.wall_length * 0.5)
			var sb := clampf(miter.y, -b.wall_length * 0.5, b.wall_length * 0.5)

			_store_offset(result.offsets, a, true, sa)
			_store_offset(result.offsets, b, false, sb)

			# World-XZ position of this miter intersection.
			var n_la := Vector2(-a.dir.y, a.dir.x)
			var p_xz := Vector2(a.node_pos.x, a.node_pos.z)
			miter_pts.append(p_xz + n_la * (a.thickness * 0.5) + sa * a.dir)

		# Three or more miter points form a gap polygon that needs filling.
		if miter_pts.size() >= 3:
			result.fills.append(JunctionFill.new(miter_pts))

	return result


# ── Miter between two half-edges ─────────────────────────────────────────────
#
#   line A:  P + perp_ccw(d_a)·(t_a/2) + s_a · d_a
#   line B:  P - perp_ccw(d_b)·(t_b/2) + s_b · d_b
#
# Solve:  s_a · d_a - s_b · d_b = -perp_ccw(d_b)·(t_b/2) - perp_ccw(d_a)·(t_a/2)

## Returns Vector2(s_a, s_b), or null when the directions are parallel
## (0° or 180°) and there is no unique intersection.
static func _try_miter(a: _HalfEdge, b: _HalfEdge) -> Variant:
	var da := a.dir
	var db := b.dir

	# 2D cross product; near-zero means parallel directions (0° or 180°).
	var cross := da.x * db.y - da.y * db.x
	if absf(cross) < _PARALLEL_EPSILON:
		return null

	var n_la := Vector2(-da.y, da.x)   # perp_ccw(d_a)
	var n_rb := Vector2(db.y, -db.x)   # -perp_ccw(d_b)

	var rhs := n_rb * (b.thickness * 0.5) - n_la * (a.thickness * 0.5)

	# [ da.x  -db.x ] [ sa ]   [ rhs.x ]
	# [ da.y  -db.y ] [ sb ] = [ rhs.y ]
	var det := -(da.x * db.y - da.y * db.x)   # = -cross, guaranteed non-zero
	var sa := (rhs.x * -db.y - (-db.x) * rhs.y) / det
	var sb := (da.x * rhs.y - da.y * rhs.x) / det
	return Vector2(sa, sb)


static func _store_offset(offsets: Dictionary, he: _HalfEdge,
		side_ccw: bool, value: float) -> void:
	if he.kind == _Kind.MID_VIRTUAL or he.wall == null:
		return
	if not offsets.has(he.wall):
		return

	var off: Offsets = offsets[he.wall]
	if he.kind == _Kind.START:
		# At Start the inward dir is +basis_x, so CCW-of-inward = local -Z.
		if side_ccw:
			off.start_minus_z = value
		else:
			off.start_plus_z = value
	else:
		# At End the inward dir is -basis_x, so CCW-of-inward = local +Z.
		if side_ccw:
			off.end_plus_z = value
		else:
			off.end_minus_z = value


static func _close_xz(a: Vector3, b: Vector3) -> bool:
	var dx := a.x - b.x
	var dz := a.z - b.z
	return dx * dx + dz * dz < _POS_TOLERANCE * _POS_TOLERANCE


# ── Convenience: translate solver offsets to the mesh-builder class ──────────

static func to_mesh_joins(o: Offsets) -> WallMeshBuilder.JoinOffsets:
	return WallMeshBuilder.JoinOffsets.new(
		o.start_minus_z,
		o.start_plus_z,
		o.end_minus_z,
		o.end_plus_z
	)
