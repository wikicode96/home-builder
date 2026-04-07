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
}
