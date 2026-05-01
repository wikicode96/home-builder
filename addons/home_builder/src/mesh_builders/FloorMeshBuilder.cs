using Godot;

// Generates an ArrayMesh for a floor slab of arbitrary size (cols x rows tiles
// of 1m, height 0.1m), centered at local origin. Three surfaces:
//   0 = top    (walkable surface, normal pointing UP)
//   1 = bottom (ceiling of floor below, normal pointing DOWN)
//   2 = sides  (4 lateral faces, normals pointing outward)
//
// UVs on top/bottom tile per 1m so a single texture repeats once per metre.

public static class FloorMeshBuilder
{
    public const int SurfaceTop    = 0;
    public const int SurfaceBottom = 1;
    public const int SurfaceSides  = 2;

    private const float HalfY = 0.05f; // half of 0.1m height

    public static ArrayMesh Build() => Build(1, 1);

    public static ArrayMesh Build(int cols, int rows)
    {
        float halfX = cols * 0.5f;
        float halfZ = rows * 0.5f;

        var mesh = new ArrayMesh();
        MeshHelper.AddSurface(mesh, BuildTop(halfX, halfZ, cols, rows));
        MeshHelper.AddSurface(mesh, BuildBottom(halfX, halfZ, cols, rows));
        MeshHelper.AddSurface(mesh, BuildSides(halfX, halfZ, cols, rows));
        return mesh;
    }

    // ── Top face (Y = +HalfY, normal = Vector3.Up) ───────────────────────────

    private static SurfaceTool BuildTop(float halfX, float halfZ, int cols, int rows)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        MeshHelper.AddQuad(st,
            new Vector3(-halfX,  HalfY,  halfZ),
            new Vector3( halfX,  HalfY,  halfZ),
            new Vector3( halfX,  HalfY, -halfZ),
            new Vector3(-halfX,  HalfY, -halfZ),
            Vector3.Up,
            new Vector2(0,    0),    new Vector2(cols, 0),
            new Vector2(cols, rows), new Vector2(0,    rows)
        );

        return st;
    }

    // ── Bottom face (Y = -HalfY, normal = Vector3.Down) ─────────────────────

    private static SurfaceTool BuildBottom(float halfX, float halfZ, int cols, int rows)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        MeshHelper.AddQuad(st,
            new Vector3( halfX, -HalfY,  halfZ),
            new Vector3(-halfX, -HalfY,  halfZ),
            new Vector3(-halfX, -HalfY, -halfZ),
            new Vector3( halfX, -HalfY, -halfZ),
            Vector3.Down,
            new Vector2(0,    0),    new Vector2(cols, 0),
            new Vector2(cols, rows), new Vector2(0,    rows)
        );

        return st;
    }

    // ── Four side faces ───────────────────────────────────────────────────────

    private static SurfaceTool BuildSides(float halfX, float halfZ, int cols, int rows)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Front face (+Z)
        MeshHelper.AddQuad(st,
            new Vector3(-halfX,  HalfY,  halfZ),
            new Vector3(-halfX, -HalfY,  halfZ),
            new Vector3( halfX, -HalfY,  halfZ),
            new Vector3( halfX,  HalfY,  halfZ),
            new Vector3(0, 0, 1),
            new Vector2(0, 0),    new Vector2(0, 1),
            new Vector2(cols, 1), new Vector2(cols, 0)
        );

        // Back face (-Z)
        MeshHelper.AddQuad(st,
            new Vector3( halfX,  HalfY, -halfZ),
            new Vector3( halfX, -HalfY, -halfZ),
            new Vector3(-halfX, -HalfY, -halfZ),
            new Vector3(-halfX,  HalfY, -halfZ),
            new Vector3(0, 0, -1),
            new Vector2(0, 0),    new Vector2(0, 1),
            new Vector2(cols, 1), new Vector2(cols, 0)
        );

        // Right face (+X)
        MeshHelper.AddQuad(st,
            new Vector3( halfX,  HalfY,  halfZ),
            new Vector3( halfX, -HalfY,  halfZ),
            new Vector3( halfX, -HalfY, -halfZ),
            new Vector3( halfX,  HalfY, -halfZ),
            new Vector3(1, 0, 0),
            new Vector2(0, 0),    new Vector2(0, 1),
            new Vector2(rows, 1), new Vector2(rows, 0)
        );

        // Left face (-X)
        MeshHelper.AddQuad(st,
            new Vector3(-halfX,  HalfY, -halfZ),
            new Vector3(-halfX, -HalfY, -halfZ),
            new Vector3(-halfX, -HalfY,  halfZ),
            new Vector3(-halfX,  HalfY,  halfZ),
            new Vector3(-1, 0, 0),
            new Vector2(0, 0),    new Vector2(0, 1),
            new Vector2(rows, 1), new Vector2(rows, 0)
        );

        return st;
    }
}
