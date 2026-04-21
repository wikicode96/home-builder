using Godot;
using System.Collections.Generic;

// Computes per-endpoint miter offsets for wall segments so that corners,
// T-junctions and X-junctions meet cleanly, without gaps or overlaps, for
// ANY pair of angles (not just 90°).
//
// --- Algorithm ------------------------------------------------------------
// At every point in the world XZ plane where two or more walls converge we
// build a "node". Each wall end-point that lies at the node contributes a
// "half-edge" whose inward direction points from the node INTO the wall
// body. When a wall passes straight through the node (T-junction), we add
// two VIRTUAL half-edges that represent the two continuing halves of the
// through-wall; these constrain the miter but themselves receive no offset.
//
// For every pair (a, b) of angularly adjacent half-edges (in CCW order
// around the node) we intersect:
//   • a's CCW-side edge line, offset by perpCCW(d_a) · thickness/2
//   • b's CW-side edge line,  offset by -perpCCW(d_b) · thickness/2
// The intersection M is the miter point between a and b. The parametric
// distances s_a and s_b from the node along each half-edge's inward
// direction give the cap offsets applied to wall a (on its CCW side) and
// wall b (on its CW side).
//
// --- Local frame conventions ---------------------------------------------
// Every wall is a StaticBody3D whose Basis.X runs from the wall's Start
// endpoint to its End endpoint in world space. Because Basis.Z = Y × X, at
// the Start endpoint the CCW side of the inward direction maps to local -Z,
// and at the End endpoint it maps to local +Z. See StoreOffset below.
//
// --- Edge cases ----------------------------------------------------------
//   • Two walls collinear and opposite (180°): their shared miter is
//     parallel and at infinity — we simply leave the offset at zero on that
//     side, so the walls continue as a straight strip.
//   • Two walls overlapping (0°): treated the same way, skipped.
//   • Acute corners: miter distance = thickness / (2·sin(half-angle)) can
//     become large. The solver clamps each offset to half the wall's length.

public static class WallJunctionSolver
{
    public struct Offsets
    {
        public float StartMinusZ;  // cap offset at Start on local -Z side
        public float StartPlusZ;   // cap offset at Start on local +Z side
        public float EndMinusZ;    // cap offset at End   on local -Z side
        public float EndPlusZ;     // cap offset at End   on local +Z side
    }

    // Convex polygon (world XZ) that fills the gap at a multi-wall junction.
    // Points are in CCW order matching the half-edge sort order.
    public struct JunctionFill
    {
        public Vector2[] PointsXZ;
    }

    // Two world positions within this distance are treated as the same node.
    private const float PosTolerance = 0.01f;
    // A point must be at least this far from either endpoint of a wall to
    // count as "in the middle" (T-junction detection).
    private const float MinMidDistance = 0.02f;
    // 2D cross products below this magnitude are treated as parallel.
    private const float ParallelEpsilon = 1e-5f;

    private enum Kind { Start, End, MidVirtual }

    private class HalfEdge
    {
        public StaticBody3D Wall;
        public Kind         Kind;
        public Vector3      NodePos;
        public Vector2      Dir;          // unit XZ inward direction
        public float        WallLength;
        public float        Thickness;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    // Solves all junctions under `wallParent`. Returns per-wall cap offsets and
    // a list of fill polygons (one per junction gap that needs covering).
    public static (Dictionary<StaticBody3D, Offsets> Offsets, List<JunctionFill> Fills)
        Solve(Node3D wallParent)
    {
        var result = new Dictionary<StaticBody3D, Offsets>();
        var fills  = new List<JunctionFill>();
        if (wallParent == null) return (result, fills);

        // ── Collect walls and per-wall info
        var walls     = new List<StaticBody3D>();
        var startPos  = new Dictionary<StaticBody3D, Vector3>();
        var endPos    = new Dictionary<StaticBody3D, Vector3>();
        var wallDir   = new Dictionary<StaticBody3D, Vector2>();
        var wallLen   = new Dictionary<StaticBody3D, float>();

        foreach (Node child in wallParent.GetChildren())
        {
            if (child is not StaticBody3D b) continue;
            float len = WallHelper.GetWallLength(b);
            if (len <= 0f) continue;

            var tr   = b.GlobalTransform;
            var axis = tr.Basis.X;
            var d2   = new Vector2(axis.X, axis.Z);
            if (d2.LengthSquared() < 1e-8f) continue;
            d2 = d2.Normalized();

            walls.Add(b);
            startPos[b] = tr.Origin - axis * (len * 0.5f);
            endPos[b]   = tr.Origin + axis * (len * 0.5f);
            wallDir[b]  = d2;
            wallLen[b]  = len;
            result[b]   = default;
        }

        if (walls.Count == 0) return (result, fills);

        // ── Build half-edges at both endpoints of every wall
        var halfEdges = new List<HalfEdge>();
        foreach (var w in walls)
        {
            halfEdges.Add(new HalfEdge
            {
                Wall = w, Kind = Kind.Start,
                NodePos = startPos[w],
                Dir = wallDir[w],                    // Start inward = +axis
                WallLength = wallLen[w],
                Thickness = WallBuilder.Thickness,
            });
            halfEdges.Add(new HalfEdge
            {
                Wall = w, Kind = Kind.End,
                NodePos = endPos[w],
                Dir = -wallDir[w],                   // End inward = -axis
                WallLength = wallLen[w],
                Thickness = WallBuilder.Thickness,
            });
        }

        // ── Group half-edges into nodes by world XZ position (with tolerance)
        var nodes = new List<List<HalfEdge>>();
        foreach (var he in halfEdges)
        {
            bool placed = false;
            foreach (var node in nodes)
            {
                if (CloseXZ(node[0].NodePos, he.NodePos))
                {
                    node.Add(he);
                    placed = true; break;
                }
            }
            if (!placed) nodes.Add(new List<HalfEdge> { he });
        }

        // ── Detect T-junctions: an endpoint sitting on another wall's
        //    centerline (away from that other wall's endpoints). We insert
        //    two virtual half-edges so the miter code handles the T like any
        //    other N-way junction — but the through-wall receives no offsets.
        foreach (var node in nodes)
        {
            var P = node[0].NodePos;
            foreach (var w in walls)
            {
                // Skip if this wall is already terminating at this node.
                bool alreadyHere = false;
                foreach (var he in node)
                    if (he.Wall == w) { alreadyHere = true; break; }
                if (alreadyHere) continue;

                var dir = wallDir[w];
                var s   = startPos[w];
                var rel = new Vector2(P.X - s.X, P.Z - s.Z);
                float tAlong = rel.Dot(dir);
                float tPerp  = rel.Dot(new Vector2(-dir.Y, dir.X));
                if (Mathf.Abs(tPerp) > PosTolerance) continue;

                float L = wallLen[w];
                if (tAlong < MinMidDistance || tAlong > L - MinMidDistance) continue;

                // Virtual half-edges in both directions along the through-wall.
                node.Add(new HalfEdge
                {
                    Wall = w, Kind = Kind.MidVirtual,
                    NodePos = P, Dir = dir,
                    WallLength = L - tAlong,
                    Thickness = WallBuilder.Thickness,
                });
                node.Add(new HalfEdge
                {
                    Wall = w, Kind = Kind.MidVirtual,
                    NodePos = P, Dir = -dir,
                    WallLength = tAlong,
                    Thickness = WallBuilder.Thickness,
                });
            }
        }

        // ── Solve miters per node
        foreach (var node in nodes)
        {
            if (node.Count < 2) continue;
            node.Sort((a, b) => Mathf.Atan2(a.Dir.Y, a.Dir.X)
                                   .CompareTo(Mathf.Atan2(b.Dir.Y, b.Dir.X)));

            int n = node.Count;
            var miterPts = new List<Vector2>();

            for (int i = 0; i < n; i++)
            {
                var a = node[i];
                var b = node[(i + 1) % n];
                if (!TryMiter(a, b, out float sa, out float sb))
                {
                    // Antiparallel real walls (e.g. two collinear walls both
                    // terminating at this node, plus a third branch): TryMiter
                    // finds no unique intersection because the miter lines are
                    // coincident. The gap vertex is the node offset by
                    // perpCCW(da) * thickness/2 — the exterior face of the
                    // straight joint. We only do this for real (non-virtual)
                    // half-edges; a virtual antiparallel pair means a single
                    // through-wall whose own top surface already covers the node.
                    if (a.Kind != Kind.MidVirtual && b.Kind != Kind.MidVirtual)
                    {
                        float dot = a.Dir.X * b.Dir.X + a.Dir.Y * b.Dir.Y;
                        if (dot < -1f + ParallelEpsilon * 10f)
                        {
                            var pXZ2 = new Vector2(a.NodePos.X, a.NodePos.Z);
                            var nLa2 = new Vector2(-a.Dir.Y, a.Dir.X);
                            miterPts.Add(pXZ2 + nLa2 * (a.Thickness * 0.5f));
                        }
                    }
                    continue;
                }

                // Clamp so we never trim more than half the wall's length.
                sa = Mathf.Clamp(sa, -a.WallLength * 0.5f, a.WallLength * 0.5f);
                sb = Mathf.Clamp(sb, -b.WallLength * 0.5f, b.WallLength * 0.5f);

                StoreOffset(result, a, sideCCW: true,  value: sa);
                StoreOffset(result, b, sideCCW: false, value: sb);

                // World-XZ position of this miter intersection.
                var nLa = new Vector2(-a.Dir.Y, a.Dir.X);
                var pXZ = new Vector2(a.NodePos.X, a.NodePos.Z);
                miterPts.Add(pXZ + nLa * (a.Thickness * 0.5f) + sa * a.Dir);
            }

            // Three or more miter points form a gap polygon that needs filling.
            if (miterPts.Count >= 3)
                fills.Add(new JunctionFill { PointsXZ = miterPts.ToArray() });
        }

        return (result, fills);
    }

    // -------------------------------------------------------------------------
    // Miter between two half-edges
    //
    //   line A:  P + perpCCW(d_a)·(t_a/2) + s_a · d_a
    //   line B:  P - perpCCW(d_b)·(t_b/2) + s_b · d_b
    //
    // Solve:  s_a · d_a - s_b · d_b = -perpCCW(d_b)·(t_b/2) - perpCCW(d_a)·(t_a/2)
    // -------------------------------------------------------------------------

    private static bool TryMiter(HalfEdge a, HalfEdge b, out float sa, out float sb)
    {
        sa = 0f; sb = 0f;

        Vector2 da = a.Dir;
        Vector2 db = b.Dir;

        // 2D cross product; near-zero means parallel directions (0° or 180°).
        float cross = da.X * db.Y - da.Y * db.X;
        if (Mathf.Abs(cross) < ParallelEpsilon) return false;

        Vector2 nLa = new Vector2(-da.Y, da.X);              // perpCCW(d_a)
        Vector2 nRb = new Vector2( db.Y, -db.X);             // -perpCCW(d_b)

        Vector2 rhs = nRb * (b.Thickness * 0.5f) - nLa * (a.Thickness * 0.5f);

        // [ da.x  -db.x ] [ sa ]   [ rhs.x ]
        // [ da.y  -db.y ] [ sb ] = [ rhs.y ]
        float det = -(da.X * db.Y - da.Y * db.X);   // = -cross, guaranteed non-zero
        sa = (rhs.X * -db.Y - (-db.X) * rhs.Y) / det;
        sb = (da.X  * rhs.Y -  da.Y   * rhs.X) / det;
        return true;
    }

    private static void StoreOffset(
        Dictionary<StaticBody3D, Offsets> result,
        HalfEdge he, bool sideCCW, float value)
    {
        if (he.Kind == Kind.MidVirtual || he.Wall == null) return;
        if (!result.TryGetValue(he.Wall, out var off)) return;

        if (he.Kind == Kind.Start)
        {
            // At Start the inward dir is +basisX, so CCW-of-inward = local -Z.
            if (sideCCW) off.StartMinusZ = value;
            else         off.StartPlusZ  = value;
        }
        else
        {
            // At End the inward dir is -basisX, so CCW-of-inward = local +Z.
            if (sideCCW) off.EndPlusZ  = value;
            else         off.EndMinusZ = value;
        }
        result[he.Wall] = off;
    }

    private static bool CloseXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return dx * dx + dz * dz < PosTolerance * PosTolerance;
    }

    // -------------------------------------------------------------------------
    // Convenience: translate Solver offsets to the mesh-builder struct.
    // -------------------------------------------------------------------------

    public static WallMeshBuilder.JoinOffsets ToMeshJoins(Offsets o)
        => new WallMeshBuilder.JoinOffsets
        {
            StartMinusZ = o.StartMinusZ,
            StartPlusZ  = o.StartPlusZ,
            EndMinusZ   = o.EndMinusZ,
            EndPlusZ    = o.EndPlusZ,
        };
}
