using Godot;
using System.Collections.Generic;
using System.Linq;

// Generates an ArrayMesh for a wall segment with three surfaces:
//   0 = Face A  (front face, normal pointing toward -Z local)
//   1 = Face B  (back face,  normal pointing toward +Z local)
//   2 = Edges   (top, bottom, left, right — the thickness faces)
//
// The wall is centred at the origin in local space:
//   X: -length/2 .. +length/2
//   Y: -height/2 .. +height/2
//   Z: -thickness/2 .. +thickness/2
//
// Multiple openings are supported. Openings must not overlap.
//
// JoinOffsets lets each of the four cap corners slide along local X to
// produce mitered corners at L, T and X junctions (see WallJunctionSolver).
// With zero offsets the mesh is a plain rectangular prism.

public static class WallMeshBuilder
{
    public const int SurfaceFaceA = 0;
    public const int SurfaceFaceB = 1;
    public const int SurfaceEdges = 2;

    // -------------------------------------------------------------------------
    // Public data structs
    // -------------------------------------------------------------------------

    public struct Opening
    {
        public float CenterX;       // local X of the opening centre
        public float Width;
        public float BottomY;       // distance from floor (0 = ground level)
        public float Height;

        public float Left  => CenterX - Width  * 0.5f;
        public float Right => CenterX + Width  * 0.5f;
        public float LocalBottom(float hy) => BottomY - hy;            // in mesh local Y
        public float LocalTop   (float hy) => BottomY - hy + Height;   // in mesh local Y
    }

    // Per-endpoint cap offsets used to form clean miter joints when walls
    // meet at a corner, T-, or X-junction. Positive values trim the cap
    // INWARD from the nominal endpoint; negative values extend it OUTWARD.
    // The four fields are applied in the wall's local frame:
    //   StartMinusZ — start cap corner at local Z = -thickness/2
    //   StartPlusZ  — start cap corner at local Z = +thickness/2
    //   EndMinusZ   — end   cap corner at local Z = -thickness/2
    //   EndPlusZ    — end   cap corner at local Z = +thickness/2
    public struct JoinOffsets
    {
        public float StartMinusZ;
        public float StartPlusZ;
        public float EndMinusZ;
        public float EndPlusZ;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public static ArrayMesh Build(float length, float height, float thickness)
        => BuildWithOpeningsAndJoins(length, height, thickness, null, default);

    public static ArrayMesh BuildWithOpening(float length, float height, float thickness,
        float openingCenterX, float openingWidth, float openingBottomY, float openingHeight)
        => BuildWithOpeningsAndJoins(length, height, thickness, new[]
        {
            new Opening
            {
                CenterX = openingCenterX,
                Width   = openingWidth,
                BottomY = openingBottomY,
                Height  = openingHeight,
            }
        }, default);

    public static ArrayMesh BuildWithOpenings(
        float length, float height, float thickness,
        IEnumerable<Opening> openings)
        => BuildWithOpeningsAndJoins(length, height, thickness, openings, default);

    public static ArrayMesh BuildWithOpeningsAndJoins(
        float length, float height, float thickness,
        IEnumerable<Opening> openings,
        JoinOffsets joins)
    {
        float hx = length    * 0.5f;
        float hy = height    * 0.5f;
        float hz = thickness * 0.5f;

        joins = ClampJoins(joins, length);

        var ops = (openings ?? Enumerable.Empty<Opening>())
            .OrderBy(o => o.CenterX)
            .ToList();

        // Per-face left/right X extents derived from the joins.
        float xStartMZ = -hx + joins.StartMinusZ;
        float xStartPZ = -hx + joins.StartPlusZ;
        float xEndMZ   =  hx - joins.EndMinusZ;
        float xEndPZ   =  hx - joins.EndPlusZ;

        var mesh = new ArrayMesh();
        // Face A at z = -hz uses the -Z-side offsets.
        MeshHelper.AddSurface(mesh, BuildFace(hx, hy, ops, xStartMZ, xEndMZ, faceZ: -hz, normalZ: -1f));
        // Face B at z = +hz uses the +Z-side offsets.
        MeshHelper.AddSurface(mesh, BuildFace(hx, hy, ops, xStartPZ, xEndPZ, faceZ:  hz, normalZ:  1f));
        MeshHelper.AddSurface(mesh, BuildEdges(hx, hy, hz, ops, xStartMZ, xStartPZ, xEndMZ, xEndPZ));
        return mesh;
    }

    // Prevents runaway or inverted geometry for very short walls / extreme
    // miter angles. The solver already clamps to half-length; this is the
    // mesh-side safety net.
    private static JoinOffsets ClampJoins(JoinOffsets j, float length)
    {
        float maxEach = length * 0.49f;
        float minEach = -length * 0.49f;
        j.StartMinusZ = Mathf.Clamp(j.StartMinusZ, minEach, maxEach);
        j.StartPlusZ  = Mathf.Clamp(j.StartPlusZ,  minEach, maxEach);
        j.EndMinusZ   = Mathf.Clamp(j.EndMinusZ,   minEach, maxEach);
        j.EndPlusZ    = Mathf.Clamp(j.EndPlusZ,    minEach, maxEach);

        // Keep a tiny residual length on every side so each face quad has area.
        const float minLen = 0.01f;
        if (j.StartMinusZ + j.EndMinusZ > length - minLen)
        {
            float scale = (length - minLen) / (j.StartMinusZ + j.EndMinusZ);
            j.StartMinusZ *= scale;
            j.EndMinusZ   *= scale;
        }
        if (j.StartPlusZ + j.EndPlusZ > length - minLen)
        {
            float scale = (length - minLen) / (j.StartPlusZ + j.EndPlusZ);
            j.StartPlusZ *= scale;
            j.EndPlusZ   *= scale;
        }
        return j;
    }

    // =========================================================================
    // UV helpers — map a local X/Y position to 0..1 UV space using the wall's
    // nominal half-extents, so the texture doesn't shift when caps are mitered.
    // =========================================================================

    private static float UvX(float x, float hx) => (x + hx) / (2f * hx);
    private static float UvY(float y, float hy) => (hy - y) / (2f * hy);   // V=0 at top

    // =========================================================================
    // Face at z = faceZ. leftX / rightX are THIS face's slanted cap extents.
    // =========================================================================

    private static SurfaceTool BuildFace(
        float hx, float hy,
        List<Opening> ops,
        float leftX, float rightX,
        float faceZ, float normalZ)
    {
        var st     = new SurfaceTool();
        var normal = new Vector3(0f, 0f, normalZ);
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Flip X axis for Face B so it faces outward correctly.
        bool flipX = normalZ > 0f;

        if (ops.Count == 0)
        {
            EmitFaceQuad(st, leftX, rightX, -hy, hy, hx, hy, faceZ, normal, flipX);
            return st;
        }

        // Collect unique Y levels introduced by openings.
        var yLevels = new SortedSet<float> { -hy, hy };
        foreach (var op in ops)
        {
            float oBot = op.LocalBottom(hy);
            float oTop = op.LocalTop(hy);
            if (oBot > -hy) yLevels.Add(oBot);
            if (oTop <  hy) yLevels.Add(oTop);
        }
        var yList = yLevels.ToList();

        for (int b = 0; b < yList.Count - 1; b++)
        {
            float yBot = yList[b];
            float yTop = yList[b + 1];
            float yMid = (yBot + yTop) * 0.5f;

            var blocked = new List<(float l, float r)>();
            foreach (var op in ops)
            {
                float oBot = op.LocalBottom(hy);
                float oTop = op.LocalTop(hy);
                if (yMid > oBot && yMid < oTop)
                    blocked.Add((op.Left, op.Right));
            }
            blocked.Sort((a, b2) => a.l.CompareTo(b2.l));

            float cursor = leftX;
            foreach (var (bLeft, bRight) in blocked)
            {
                if (cursor < bLeft)
                    EmitFaceQuad(st, cursor, bLeft, yBot, yTop, hx, hy, faceZ, normal, flipX);
                cursor = bRight;
            }
            if (cursor < rightX)
                EmitFaceQuad(st, cursor, rightX, yBot, yTop, hx, hy, faceZ, normal, flipX);
        }

        return st;
    }

    private static void EmitFaceQuad(SurfaceTool st,
        float x0, float x1, float y0, float y1,
        float hx, float hy, float faceZ,
        Vector3 normal, bool flipX)
    {
        if (x1 - x0 <= 0.0001f) return;   // skip zero-width spans

        float lx = flipX ? x1 : x0;
        float rx = flipX ? x0 : x1;

        MeshHelper.AddQuad(st,
            new Vector3(lx, y1, faceZ),
            new Vector3(rx, y1, faceZ),
            new Vector3(rx, y0, faceZ),
            new Vector3(lx, y0, faceZ),
            normal,
            new Vector2(UvX(lx, hx), UvY(y1, hy)),
            new Vector2(UvX(rx, hx), UvY(y1, hy)),
            new Vector2(UvX(rx, hx), UvY(y0, hy)),
            new Vector2(UvX(lx, hx), UvY(y0, hy))
        );
    }

    // =========================================================================
    // Edge surfaces: top, bottom, slanted caps, and opening frames.
    // =========================================================================

    private static SurfaceTool BuildEdges(
        float hx, float hy, float hz,
        List<Opening> ops,
        float xStartMZ, float xStartPZ, float xEndMZ, float xEndPZ)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // ── Top (+Y): single (possibly trapezoidal) quad ──────────────────────
        MeshHelper.AddQuad(st,
            new Vector3(xStartPZ,  hy,  hz),
            new Vector3(xEndPZ,    hy,  hz),
            new Vector3(xEndMZ,    hy, -hz),
            new Vector3(xStartMZ,  hy, -hz),
            Vector3.Up,
            new Vector2(UvX(xStartPZ, hx), 0),
            new Vector2(UvX(xEndPZ,   hx), 0),
            new Vector2(UvX(xEndMZ,   hx), 1),
            new Vector2(UvX(xStartMZ, hx), 1)
        );

        // ── Bottom (-Y): split around floor-reaching openings ────────────────
        {
            var blocked = new List<(float l, float r)>();
            foreach (var op in ops)
                if (op.LocalBottom(hy) <= -hy)
                    blocked.Add((op.Left, op.Right));
            blocked.Sort((a, b) => a.l.CompareTo(b.l));

            // Walk along X, emitting trapezoidal pieces. The left/right edges
            // of each piece can be slanted (at a cap) or flat (at an opening).
            float leftMZ = xStartMZ, leftPZ = xStartPZ;
            foreach (var (bLeft, bRight) in blocked)
            {
                EmitBottomQuad(st, leftMZ, leftPZ, bLeft, bLeft, hx, hy, hz);
                leftMZ = bRight;
                leftPZ = bRight;
            }
            EmitBottomQuad(st, leftMZ, leftPZ, xEndMZ, xEndPZ, hx, hy, hz);
        }

        // ── Start cap (outer -X end, possibly tilted) ─────────────────────────
        EmitStartCap(st, xStartMZ, xStartPZ, hy, hz);

        // ── End cap (outer +X end, possibly tilted) ───────────────────────────
        EmitEndCap(st, xEndMZ, xEndPZ, hy, hz);

        // ── Opening frames ────────────────────────────────────────────────────
        foreach (var op in ops)
        {
            float oLeft   = op.Left;
            float oRight  = op.Right;
            float oBottom = op.LocalBottom(hy);
            float oTop    = op.LocalTop(hy);

            // Top lintel
            MeshHelper.AddQuad(st,
                new Vector3(oRight, oTop,  hz),
                new Vector3(oLeft,  oTop,  hz),
                new Vector3(oLeft,  oTop, -hz),
                new Vector3(oRight, oTop, -hz),
                Vector3.Down,
                new Vector2(UvX(oRight, hx), 0),
                new Vector2(UvX(oLeft,  hx), 0),
                new Vector2(UvX(oLeft,  hx), 1),
                new Vector2(UvX(oRight, hx), 1)
            );

            // Sill (windows only)
            if (oBottom > -hy)
            {
                MeshHelper.AddQuad(st,
                    new Vector3(oLeft,  oBottom,  hz),
                    new Vector3(oRight, oBottom,  hz),
                    new Vector3(oRight, oBottom, -hz),
                    new Vector3(oLeft,  oBottom, -hz),
                    Vector3.Up,
                    new Vector2(UvX(oLeft,  hx), 0),
                    new Vector2(UvX(oRight, hx), 0),
                    new Vector2(UvX(oRight, hx), 1),
                    new Vector2(UvX(oLeft,  hx), 1)
                );
            }

            // Left jamb
            MeshHelper.AddQuad(st,
                new Vector3(oLeft, oTop,     hz),
                new Vector3(oLeft, oBottom,  hz),
                new Vector3(oLeft, oBottom, -hz),
                new Vector3(oLeft, oTop,    -hz),
                Vector3.Right,
                new Vector2(0, UvY(oTop,    hy)),
                new Vector2(0, UvY(oBottom, hy)),
                new Vector2(1, UvY(oBottom, hy)),
                new Vector2(1, UvY(oTop,    hy))
            );

            // Right jamb
            MeshHelper.AddQuad(st,
                new Vector3(oRight, oBottom,  hz),
                new Vector3(oRight, oTop,     hz),
                new Vector3(oRight, oTop,    -hz),
                new Vector3(oRight, oBottom, -hz),
                Vector3.Left,
                new Vector2(0, UvY(oBottom, hy)),
                new Vector2(0, UvY(oTop,    hy)),
                new Vector2(1, UvY(oTop,    hy)),
                new Vector2(1, UvY(oBottom, hy))
            );
        }

        return st;
    }

    private static void EmitBottomQuad(SurfaceTool st,
        float leftMZ, float leftPZ, float rightMZ, float rightPZ,
        float hx, float hy, float hz)
    {
        // Skip pieces with no area on either side.
        if (rightMZ - leftMZ <= 0.0001f && rightPZ - leftPZ <= 0.0001f) return;

        // Matches the winding of the original axis-aligned bottom quads.
        MeshHelper.AddQuad(st,
            new Vector3(rightPZ, -hy,  hz),
            new Vector3(leftPZ,  -hy,  hz),
            new Vector3(leftMZ,  -hy, -hz),
            new Vector3(rightMZ, -hy, -hz),
            Vector3.Down,
            new Vector2(UvX(rightPZ, hx), 0),
            new Vector2(UvX(leftPZ,  hx), 0),
            new Vector2(UvX(leftMZ,  hx), 1),
            new Vector2(UvX(rightMZ, hx), 1)
        );
    }

    // Start cap: outward normal points roughly -X, tilted by the offset
    // difference between the two sides.
    private static void EmitStartCap(SurfaceTool st,
        float xStartMZ, float xStartPZ, float hy, float hz)
    {
        var tan = new Vector3(xStartPZ - xStartMZ, 0f, 2f * hz);
        if (tan.LengthSquared() < 1e-8f) return;
        var normal = tan.Cross(Vector3.Up).Normalized();   // outward = away from +X wall body

        MeshHelper.AddQuad(st,
            new Vector3(xStartMZ,  hy, -hz),
            new Vector3(xStartMZ, -hy, -hz),
            new Vector3(xStartPZ, -hy,  hz),
            new Vector3(xStartPZ,  hy,  hz),
            normal,
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );
    }

    // End cap: outward normal points roughly +X, tilted by the offset
    // difference between the two sides.
    private static void EmitEndCap(SurfaceTool st,
        float xEndMZ, float xEndPZ, float hy, float hz)
    {
        var tan = new Vector3(xEndMZ - xEndPZ, 0f, -2f * hz);
        if (tan.LengthSquared() < 1e-8f) return;
        var normal = tan.Cross(Vector3.Up).Normalized();   // outward = away from -X wall body

        MeshHelper.AddQuad(st,
            new Vector3(xEndPZ,  hy,  hz),
            new Vector3(xEndPZ, -hy,  hz),
            new Vector3(xEndMZ, -hy, -hz),
            new Vector3(xEndMZ,  hy, -hz),
            normal,
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );
    }
}
