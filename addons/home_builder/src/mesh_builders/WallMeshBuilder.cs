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

public static class WallMeshBuilder
{
    public const int SurfaceFaceA = 0;
    public const int SurfaceFaceB = 1;
    public const int SurfaceEdges = 2;

    // -------------------------------------------------------------------------
    // Public data struct — one opening
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

    // -------------------------------------------------------------------------
    // Build without openings
    // -------------------------------------------------------------------------

    public static ArrayMesh Build(float length, float height, float thickness)
        => BuildWithOpenings(length, height, thickness, null);

    // -------------------------------------------------------------------------
    // Legacy single-opening overload (kept for compatibility)
    // -------------------------------------------------------------------------

    public static ArrayMesh BuildWithOpening(float length, float height, float thickness,
        float openingCenterX, float openingWidth, float openingBottomY, float openingHeight)
        => BuildWithOpenings(length, height, thickness, new[]
        {
            new Opening
            {
                CenterX = openingCenterX,
                Width   = openingWidth,
                BottomY = openingBottomY,
                Height  = openingHeight,
            }
        });

    // -------------------------------------------------------------------------
    // Main builder — N openings
    // -------------------------------------------------------------------------

    public static ArrayMesh BuildWithOpenings(
        float length, float height, float thickness,
        IEnumerable<Opening> openings)
    {
        float hx = length    * 0.5f;
        float hy = height    * 0.5f;
        float hz = thickness * 0.5f;

        // Sort openings left-to-right and convert to local Y coords once
        var ops = (openings ?? Enumerable.Empty<Opening>())
            .OrderBy(o => o.CenterX)
            .ToList();

        var mesh = new ArrayMesh();
        MeshHelper.AddSurface(mesh, BuildFaceWithOpenings(hx, hy, hz, ops, faceZ: -hz, normalZ: -1f));
        MeshHelper.AddSurface(mesh, BuildFaceWithOpenings(hx, hy, hz, ops, faceZ:  hz, normalZ:  1f));
        MeshHelper.AddSurface(mesh, BuildEdgesWithOpenings(hx, hy, hz, ops));
        return mesh;
    }

    // =========================================================================
    // Internal helpers
    // =========================================================================

    // AddQuad: CW winding, Godot right-handed coords.
    //
    //  v0 ── v1
    //  │    ╱ │
    //  v3 ── v2

    // UV helpers — map a local X/Y position to 0..1 UV space
    private static float UvX(float x, float hx) => (x + hx) / (2f * hx);
    private static float UvY(float y, float hy) => (hy - y) / (2f * hy);  // V=0 at top

    // -------------------------------------------------------------------------
    // One flat face (Face A at z=-hz  or  Face B at z=+hz)
    //
    // Strategy: slice the face into a grid of horizontal bands (above top of
    // any opening, between openings vertically, below bottom of any opening)
    // then for each band emit columns: left margin, gaps between openings,
    // right margin.
    // -------------------------------------------------------------------------

    private static SurfaceTool BuildFaceWithOpenings(
        float hx, float hy, float hz,
        List<Opening> ops,
        float faceZ, float normalZ)
    {
        var st     = new SurfaceTool();
        var normal = new Vector3(0f, 0f, normalZ);
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Flip X axis for Face B so it faces outward correctly
        bool flipX = normalZ > 0f;

        if (ops.Count == 0)
        {
            // Full quad
            EmitFaceQuad(st, -hx, hx, -hy, hy, hx, hy, faceZ, normal, flipX);
            return st;
        }

        // Collect the unique Y levels that openings introduce
        // Everything is in mesh-local Y: bottom = -hy, top = +hy
        var yLevels = new SortedSet<float> { -hy, hy };
        foreach (var op in ops)
        {
            float oBot = op.LocalBottom(hy);
            float oTop = op.LocalTop(hy);
            if (oBot > -hy) yLevels.Add(oBot);
            if (oTop <  hy) yLevels.Add(oTop);
        }

        var yList = yLevels.ToList();

        // For each horizontal band [yList[i], yList[i+1]] decide which X spans
        // are occupied by openings and emit the solid segments.
        for (int b = 0; b < yList.Count - 1; b++)
        {
            float yBot = yList[b];
            float yTop = yList[b + 1];
            float yMid = (yBot + yTop) * 0.5f;

            // Collect X ranges blocked by openings in this Y band
            var blocked = new List<(float l, float r)>();
            foreach (var op in ops)
            {
                float oBot = op.LocalBottom(hy);
                float oTop = op.LocalTop(hy);
                if (yMid > oBot && yMid < oTop)
                    blocked.Add((op.Left, op.Right));
            }
            blocked.Sort((a, b2) => a.l.CompareTo(b2.l));

            // Emit solid spans between/around blocked ranges
            float cursor = -hx;
            foreach (var (bLeft, bRight) in blocked)
            {
                if (cursor < bLeft)
                    EmitFaceQuad(st, cursor, bLeft, yBot, yTop, hx, hy, faceZ, normal, flipX);
                cursor = bRight;
            }
            if (cursor < hx)
                EmitFaceQuad(st, cursor, hx, yBot, yTop, hx, hy, faceZ, normal, flipX);
        }

        return st;
    }

    // Emits one axis-aligned quad on a flat face, with UV derived from wall extent.
    private static void EmitFaceQuad(SurfaceTool st,
        float x0, float x1, float y0, float y1,
        float hx, float hy, float faceZ,
        Vector3 normal, bool flipX)
    {
        // v0=top-left, v1=top-right, v2=bottom-right, v3=bottom-left
        // (in Face A frame — flip v0/v1 and v2/v3 for Face B)
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

    // -------------------------------------------------------------------------
    // Edge surfaces (top, bottom, left, right sides + opening frames)
    // -------------------------------------------------------------------------

    private static SurfaceTool BuildEdgesWithOpenings(
        float hx, float hy, float hz,
        List<Opening> ops)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // ── Top edge (normal = +Y) ────────────────────────────────────────────
        MeshHelper.AddQuad(st,
            new Vector3(-hx,  hy,  hz),
            new Vector3( hx,  hy,  hz),
            new Vector3( hx,  hy, -hz),
            new Vector3(-hx,  hy, -hz),
            Vector3.Up,
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1)
        );

        // ── Bottom edge (normal = -Y) — split around any ground-level openings ──
        {
            // Collect X spans blocked at floor level
            var blocked = new List<(float l, float r)>();
            foreach (var op in ops)
            {
                if (op.LocalBottom(hy) <= -hy)   // opening reaches the floor
                    blocked.Add((op.Left, op.Right));
            }
            blocked.Sort((a, b) => a.l.CompareTo(b.l));

            float cursor = -hx;
            foreach (var (bLeft, bRight) in blocked)
            {
                if (cursor < bLeft)
                    MeshHelper.AddQuad(st,
                        new Vector3( bLeft, -hy,  hz),
                        new Vector3(cursor, -hy,  hz),
                        new Vector3(cursor, -hy, -hz),
                        new Vector3( bLeft, -hy, -hz),
                        Vector3.Down,
                        new Vector2(UvX(bLeft,  hx), 0), new Vector2(UvX(cursor, hx), 0),
                        new Vector2(UvX(cursor, hx), 1), new Vector2(UvX(bLeft,  hx), 1)
                    );
                cursor = bRight;
            }
            if (cursor < hx)
                MeshHelper.AddQuad(st,
                    new Vector3(  hx,  -hy,  hz),
                    new Vector3(cursor, -hy,  hz),
                    new Vector3(cursor, -hy, -hz),
                    new Vector3(  hx,  -hy, -hz),
                    Vector3.Down,
                    new Vector2(UvX(hx,     hx), 0), new Vector2(UvX(cursor, hx), 0),
                    new Vector2(UvX(cursor, hx), 1), new Vector2(UvX(hx,     hx), 1)
                );
        }

        // ── Left outer edge (normal = -X) ─────────────────────────────────────
        MeshHelper.AddQuad(st,
            new Vector3(-hx,  hy, -hz),
            new Vector3(-hx, -hy, -hz),
            new Vector3(-hx, -hy,  hz),
            new Vector3(-hx,  hy,  hz),
            Vector3.Left,
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        // ── Right outer edge (normal = +X) ────────────────────────────────────
        MeshHelper.AddQuad(st,
            new Vector3( hx,  hy,  hz),
            new Vector3( hx, -hy,  hz),
            new Vector3( hx, -hy, -hz),
            new Vector3( hx,  hy, -hz),
            Vector3.Right,
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        // ── Opening frames ────────────────────────────────────────────────────
        foreach (var op in ops)
        {
            float oLeft   = op.Left;
            float oRight  = op.Right;
            float oBottom = op.LocalBottom(hy);
            float oTop    = op.LocalTop(hy);

            // Top lintel (normal = -Y, ceiling of the opening — visible from below)
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

            // Sill (normal = +Y — only for windows, not floor-level doors)
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

            // Left jamb (normal = +X, faces right into the opening)
            MeshHelper.AddQuad(st,
                new Vector3(oLeft, oTop,    hz),
                new Vector3(oLeft, oBottom, hz),
                new Vector3(oLeft, oBottom, -hz),
                new Vector3(oLeft, oTop,    -hz),
                Vector3.Right,
                new Vector2(0, UvY(oTop,    hy)),
                new Vector2(0, UvY(oBottom, hy)),
                new Vector2(1, UvY(oBottom, hy)),
                new Vector2(1, UvY(oTop,    hy))
            );

            // Right jamb (normal = -X, faces left into the opening)
            MeshHelper.AddQuad(st,
                new Vector3(oRight, oBottom, hz),
                new Vector3(oRight, oTop,    hz),
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
}
