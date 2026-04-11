using Godot;

// Generates an ArrayMesh for a single stair step with three surfaces:
//   0 = top    (walkable surface, normal pointing UP)
//   1 = bottom (underside of step, normal pointing DOWN)
//   2 = sides  (4 lateral faces, normals pointing outward)

public static class StairsMeshBuilder
{
    public const int SurfaceTop    = 0;
    public const int SurfaceBottom = 1;
    public const int SurfaceSides  = 2;

    public static ArrayMesh Build(float width, float rise, float run)
    {
        var mesh = new ArrayMesh();
        MeshHelper.AddSurface(mesh, BuildTop(width, rise, run));
        MeshHelper.AddSurface(mesh, BuildBottom(width, rise, run));
        MeshHelper.AddSurface(mesh, BuildSides(width, rise, run));
        return mesh;
    }

    // ── Top face (normal = Vector3.Up) ───────────────────────────────────────

    private static SurfaceTool BuildTop(float width, float rise, float run)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        float halfX = width * 0.5f;
        float halfY = rise  * 0.5f;
        float halfZ = run   * 0.5f;

        // Viewed from above (normal pointing up = +Y)
        MeshHelper.AddQuad(st,
            new Vector3(-halfX,  halfY,  halfZ),
            new Vector3( halfX,  halfY,  halfZ),
            new Vector3( halfX,  halfY, -halfZ),
            new Vector3(-halfX,  halfY, -halfZ),
            Vector3.Up,
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1)
        );

        return st;
    }

    // ── Bottom face (normal = Vector3.Down) ─────────────────────────────────

    private static SurfaceTool BuildBottom(float width, float rise, float run)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        float halfX = width * 0.5f;
        float halfY = rise  * 0.5f;
        float halfZ = run   * 0.5f;

        // Viewed from below (normal pointing down = -Y)
        MeshHelper.AddQuad(st,
            new Vector3( halfX, -halfY,  halfZ),
            new Vector3(-halfX, -halfY,  halfZ),
            new Vector3(-halfX, -halfY, -halfZ),
            new Vector3( halfX, -halfY, -halfZ),
            Vector3.Down,
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1)
        );

        return st;
    }

    // ── Four side faces ───────────────────────────────────────────────────────

    private static SurfaceTool BuildSides(float width, float rise, float run)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        float halfX = width * 0.5f;
        float halfY = rise  * 0.5f;
        float halfZ = run   * 0.5f;

        // Front face (+Z, normal = +Z)
        MeshHelper.AddQuad(st,
            new Vector3(-halfX,  halfY,  halfZ),
            new Vector3(-halfX, -halfY,  halfZ),
            new Vector3( halfX, -halfY,  halfZ),
            new Vector3( halfX,  halfY,  halfZ),
            new Vector3(0, 0, 1),
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        // Back face (-Z, normal = -Z)
        MeshHelper.AddQuad(st,
            new Vector3( halfX,  halfY, -halfZ),
            new Vector3( halfX, -halfY, -halfZ),
            new Vector3(-halfX, -halfY, -halfZ),
            new Vector3(-halfX,  halfY, -halfZ),
            new Vector3(0, 0, -1),
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        // Right face (+X, normal = +X)
        MeshHelper.AddQuad(st,
            new Vector3( halfX,  halfY,  halfZ),
            new Vector3( halfX, -halfY,  halfZ),
            new Vector3( halfX, -halfY, -halfZ),
            new Vector3( halfX,  halfY, -halfZ),
            new Vector3(1, 0, 0),
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        // Left face (-X, normal = -X)
        MeshHelper.AddQuad(st,
            new Vector3(-halfX,  halfY, -halfZ),
            new Vector3(-halfX, -halfY, -halfZ),
            new Vector3(-halfX, -halfY,  halfZ),
            new Vector3(-halfX,  halfY,  halfZ),
            new Vector3(-1, 0, 0),
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        return st;
    }
}
