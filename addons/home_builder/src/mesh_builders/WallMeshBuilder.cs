using Godot;

// Generates an ArrayMesh for a wall segment with three surfaces:
//   0 = Face A  (front face, normal pointing toward -Z local)
//   1 = Face B  (back face,  normal pointing toward +Z local)
//   2 = Edges   (top, bottom, left, right — the thickness faces)
//
// The wall is centred at the origin in local space:
//   X: -length/2 .. +length/2
//   Y: -height/2 .. +height/2
//   Z: -thickness/2 .. +thickness/2

public static class WallMeshBuilder
{
    public const int SurfaceFaceA = 0;
    public const int SurfaceFaceB = 1;
    public const int SurfaceEdges = 2;

    public static ArrayMesh Build(float length, float height, float thickness)
    {
        float hx = length    * 0.5f;
        float hy = height    * 0.5f;
        float hz = thickness * 0.5f;

        var mesh = new ArrayMesh();
        AddSurface(mesh, BuildFaceA(hx, hy, hz));
        AddSurface(mesh, BuildFaceB(hx, hy, hz));
        AddSurface(mesh, BuildEdges(hx, hy, hz));
        return mesh;
    }

    public static ArrayMesh BuildWithOpening(float length, float height, float thickness,
        float openingCenterX, float openingWidth, float openingBottomY, float openingHeight)
    {
        float hx = length    * 0.5f;
        float hy = height    * 0.5f;
        float hz = thickness * 0.5f;

        float ohx = openingWidth  * 0.5f;
        float ohy = openingHeight * 0.5f;

        // Opening bounds in local space
        float oLeft   = openingCenterX - ohx;
        float oRight  = openingCenterX + ohx;
        float oBottom = openingBottomY - hy;
        float oTop    = oBottom + openingHeight;

        var mesh = new ArrayMesh();
        AddSurface(mesh, BuildFaceAWithOpening(hx, hy, hz, oLeft, oRight, oBottom, oTop));
        AddSurface(mesh, BuildFaceBWithOpening(hx, hy, hz, oLeft, oRight, oBottom, oTop));
        AddSurface(mesh, BuildEdgesWithOpening(hx, hy, hz, oLeft, oRight, oBottom, oTop));
        return mesh;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddSurface(ArrayMesh mesh, SurfaceTool st)
    {
        st.GenerateTangents();
        st.Commit(mesh);
    }

    private static void AddQuad(SurfaceTool st,
        Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
        Vector3 normal,
        Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
    {
        // CW winding for Godot right-handed coords
        st.SetNormal(normal); st.SetUV(uv0); st.AddVertex(v0);
        st.SetNormal(normal); st.SetUV(uv2); st.AddVertex(v2);
        st.SetNormal(normal); st.SetUV(uv1); st.AddVertex(v1);

        st.SetNormal(normal); st.SetUV(uv0); st.AddVertex(v0);
        st.SetNormal(normal); st.SetUV(uv3); st.AddVertex(v3);
        st.SetNormal(normal); st.SetUV(uv2); st.AddVertex(v2);
    }

    // ── Face A (normal = -Z, interior side) ──────────────────────────────────

    private static SurfaceTool BuildFaceA(float hx, float hy, float hz)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Viewed from -Z looking toward +Z (interior side)
        // UV: U goes left to right, V goes top to bottom
        AddQuad(st,
            new Vector3(-hx,  hy, -hz),  // top-left
            new Vector3( hx,  hy, -hz),  // top-right
            new Vector3( hx, -hy, -hz),  // bottom-right
            new Vector3(-hx, -hy, -hz),  // bottom-left
            new Vector3(0, 0, -1),
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1)
        );

        return st;
    }

    private static SurfaceTool BuildFaceAWithOpening(float hx, float hy, float hz,
        float oLeft, float oRight, float oBottom, float oTop)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        var normal = new Vector3(0, 0, -1);

        // Top segment (above opening)
        if (oTop < hy)
        {
            AddQuad(st,
                new Vector3(-hx,  hy, -hz),
                new Vector3( hx,  hy, -hz),
                new Vector3( hx,  oTop, -hz),
                new Vector3(-hx,  oTop, -hz),
                normal,
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(1, (hy - oTop) / (2 * hy)), new Vector2(0, (hy - oTop) / (2 * hy))
            );
        }

        // Bottom segment (below opening)
        if (oBottom > -hy)
        {
            AddQuad(st,
                new Vector3(-hx, oBottom, -hz),
                new Vector3( hx, oBottom, -hz),
                new Vector3( hx,     -hy, -hz),
                new Vector3(-hx,     -hy, -hz),
                normal,
                new Vector2(0, (oBottom + hy) / (2 * hy)), new Vector2(1, (oBottom + hy) / (2 * hy)),
                new Vector2(1, 1), new Vector2(0, 1)
            );
        }

        // Left segment (left of opening)
        if (oLeft > -hx)
        {
            AddQuad(st,
                new Vector3(-hx,  oTop,    -hz),
                new Vector3(oLeft,  oTop,    -hz),
                new Vector3(oLeft,  oBottom, -hz),
                new Vector3(-hx,  oBottom, -hz),
                normal,
                new Vector2(0, (hy - oTop) / (2 * hy)), new Vector2((oLeft + hx) / (2 * hx), (hy - oTop) / (2 * hy)),
                new Vector2((oLeft + hx) / (2 * hx), (oBottom + hy) / (2 * hy)), new Vector2(0, (oBottom + hy) / (2 * hy))
            );
        }

        // Right segment (right of opening)
        if (oRight < hx)
        {
            AddQuad(st,
                new Vector3(oRight,  oTop,    -hz),
                new Vector3( hx,     oTop,    -hz),
                new Vector3( hx,     oBottom, -hz),
                new Vector3(oRight,  oBottom, -hz),
                normal,
                new Vector2((oRight + hx) / (2 * hx), (hy - oTop) / (2 * hy)), new Vector2(1, (hy - oTop) / (2 * hy)),
                new Vector2(1, (oBottom + hy) / (2 * hy)), new Vector2((oRight + hx) / (2 * hx), (oBottom + hy) / (2 * hy))
            );
        }

        return st;
    }

    // ── Face B (normal = +Z, exterior side) ──────────────────────────────────

    private static SurfaceTool BuildFaceB(float hx, float hy, float hz)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Viewed from +Z looking toward -Z (exterior side)
        // X is reversed so the face points outward correctly
        AddQuad(st,
            new Vector3( hx,  hy,  hz),  // top-left  (when viewed from outside)
            new Vector3(-hx,  hy,  hz),  // top-right
            new Vector3(-hx, -hy,  hz),  // bottom-right
            new Vector3( hx, -hy,  hz),  // bottom-left
            new Vector3(0, 0, 1),
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1)
        );

        return st;
    }

    private static SurfaceTool BuildFaceBWithOpening(float hx, float hy, float hz,
        float oLeft, float oRight, float oBottom, float oTop)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        var normal = new Vector3(0, 0, 1);

        // Top segment (above opening) - X reversed for exterior
        if (oTop < hy)
        {
            AddQuad(st,
                new Vector3( hx,  hy,  hz),
                new Vector3(-hx,  hy,  hz),
                new Vector3(-hx,  oTop, hz),
                new Vector3( hx,  oTop, hz),
                normal,
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(1, (hy - oTop) / (2 * hy)), new Vector2(0, (hy - oTop) / (2 * hy))
            );
        }

        // Bottom segment (below opening) - X reversed for exterior
        if (oBottom > -hy)
        {
            AddQuad(st,
                new Vector3( hx, oBottom, hz),
                new Vector3(-hx, oBottom, hz),
                new Vector3(-hx,     -hy, hz),
                new Vector3( hx,     -hy, hz),
                normal,
                new Vector2(0, (oBottom + hy) / (2 * hy)), new Vector2(1, (oBottom + hy) / (2 * hy)),
                new Vector2(1, 1), new Vector2(0, 1)
            );
        }

        // Left segment (left of opening) - same X coordinates as Face A
        if (oLeft > -hx)
        {
            AddQuad(st,
                new Vector3(oLeft,  oTop,    hz),
                new Vector3(-hx,    oTop,    hz),
                new Vector3(-hx,    oBottom, hz),
                new Vector3(oLeft,  oBottom, hz),
                normal,
                new Vector2((oLeft + hx) / (2 * hx), (hy - oTop) / (2 * hy)), new Vector2(0, (hy - oTop) / (2 * hy)),
                new Vector2(0, (oBottom + hy) / (2 * hy)), new Vector2((oLeft + hx) / (2 * hx), (oBottom + hy) / (2 * hy))
            );
        }

        // Right segment (right of opening) - same X coordinates as Face A
        if (oRight < hx)
        {
            AddQuad(st,
                new Vector3( hx,     oTop,    hz),
                new Vector3(oRight,  oTop,    hz),
                new Vector3(oRight,  oBottom, hz),
                new Vector3( hx,     oBottom, hz),
                normal,
                new Vector2(1, (hy - oTop) / (2 * hy)), new Vector2((oRight + hx) / (2 * hx), (hy - oTop) / (2 * hy)),
                new Vector2((oRight + hx) / (2 * hx), (oBottom + hy) / (2 * hy)), new Vector2(1, (oBottom + hy) / (2 * hy))
            );
        }

        return st;
    }

    // ── Edges: top, bottom, left, right (the thickness) ──────────────────────

    private static SurfaceTool BuildEdges(float hx, float hy, float hz)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Top edge (normal = +Y)
        AddQuad(st,
            new Vector3(-hx,  hy,  hz),
            new Vector3( hx,  hy,  hz),
            new Vector3( hx,  hy, -hz),
            new Vector3(-hx,  hy, -hz),
            Vector3.Up,
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1)
        );

        // Bottom edge (normal = -Y)
        AddQuad(st,
            new Vector3( hx, -hy,  hz),
            new Vector3(-hx, -hy,  hz),
            new Vector3(-hx, -hy, -hz),
            new Vector3( hx, -hy, -hz),
            Vector3.Down,
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1)
        );

        // Left edge (normal = -X) — v1<->v3 swapped to compensate global winding flip
        AddQuad(st,
            new Vector3(-hx,  hy, -hz),
            new Vector3(-hx, -hy, -hz),
            new Vector3(-hx, -hy,  hz),
            new Vector3(-hx,  hy,  hz),
            Vector3.Left,
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        // Right edge (normal = +X) — v1<->v3 swapped to compensate global winding flip
        AddQuad(st,
            new Vector3( hx,  hy,  hz),
            new Vector3( hx, -hy,  hz),
            new Vector3( hx, -hy, -hz),
            new Vector3( hx,  hy, -hz),
            Vector3.Right,
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        return st;
    }

    private static SurfaceTool BuildEdgesWithOpening(float hx, float hy, float hz,
        float oLeft, float oRight, float oBottom, float oTop)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Top edge (above opening, normal = +Y)
        if (oTop < hy)
        {
            AddQuad(st,
                new Vector3(-hx,  hy,  hz),
                new Vector3( hx,  hy,  hz),
                new Vector3( hx,  hy, -hz),
                new Vector3(-hx,  hy, -hz),
                Vector3.Up,
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(1, 1), new Vector2(0, 1)
            );
        }

        // Bottom edge (below opening, normal = -Y)
        if (oBottom > -hy)
        {
            AddQuad(st,
                new Vector3( hx, -hy,  hz),
                new Vector3(-hx, -hy,  hz),
                new Vector3(-hx, -hy, -hz),
                new Vector3( hx, -hy, -hz),
                Vector3.Down,
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(1, 1), new Vector2(0, 1)
            );
        }

        // Left edge (left of opening, normal = -X)
        if (oLeft > -hx)
        {
            AddQuad(st,
                new Vector3(-hx,  hy, -hz),
                new Vector3(-hx, -hy, -hz),
                new Vector3(-hx, -hy,  hz),
                new Vector3(-hx,  hy,  hz),
                Vector3.Left,
                new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(1, 1), new Vector2(1, 0)
            );
        }

        // Right edge (right of opening, normal = +X)
        if (oRight < hx)
        {
            AddQuad(st,
                new Vector3( hx,  hy,  hz),
                new Vector3( hx, -hy,  hz),
                new Vector3( hx, -hy, -hz),
                new Vector3( hx,  hy, -hz),
                Vector3.Right,
                new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(1, 1), new Vector2(1, 0)
            );
        }

        // Opening frame edges (interior faces of the opening)
        // Top frame (normal = -Y, facing down into the opening)
        AddQuad(st,
            new Vector3(oLeft,  oTop,  hz),
            new Vector3(oRight, oTop,  hz),
            new Vector3(oRight, oTop, -hz),
            new Vector3(oLeft,  oTop, -hz),
            Vector3.Down,
            new Vector2((oLeft + hx) / (2 * hx), 0), new Vector2((oRight + hx) / (2 * hx), 0),
            new Vector2((oRight + hx) / (2 * hx), 1), new Vector2((oLeft + hx) / (2 * hx), 1)
        );

        // Bottom frame (normal = +Y, facing up into the opening)
        AddQuad(st,
            new Vector3(oRight, oBottom,  hz),
            new Vector3(oLeft,  oBottom,  hz),
            new Vector3(oLeft,  oBottom, -hz),
            new Vector3(oRight, oBottom, -hz),
            Vector3.Up,
            new Vector2((oRight + hx) / (2 * hx), 0), new Vector2((oLeft + hx) / (2 * hx), 0),
            new Vector2((oLeft + hx) / (2 * hx), 1), new Vector2((oRight + hx) / (2 * hx), 1)
        );

        // Left frame (normal = +X, facing right into the opening)
        AddQuad(st,
            new Vector3(oLeft, oTop,    hz),
            new Vector3(oLeft, oBottom, hz),
            new Vector3(oLeft, oBottom, -hz),
            new Vector3(oLeft, oTop,    -hz),
            Vector3.Right,
            new Vector2(0, (hy - oTop) / (2 * hy)), new Vector2(0, (oBottom + hy) / (2 * hy)),
            new Vector2(1, (oBottom + hy) / (2 * hy)), new Vector2(1, (hy - oTop) / (2 * hy))
        );

        // Right frame (normal = -X, facing left into the opening)
        AddQuad(st,
            new Vector3(oRight, oBottom, hz),
            new Vector3(oRight, oTop,    hz),
            new Vector3(oRight, oTop,    -hz),
            new Vector3(oRight, oBottom, -hz),
            Vector3.Left,
            new Vector2(0, (oBottom + hy) / (2 * hy)), new Vector2(0, (hy - oTop) / (2 * hy)),
            new Vector2(1, (hy - oTop) / (2 * hy)), new Vector2(1, (oBottom + hy) / (2 * hy))
        );

        return st;
    }
}
