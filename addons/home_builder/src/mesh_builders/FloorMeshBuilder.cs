using Godot;

// Generates an ArrayMesh for a single 1x1x0.1 floor tile with three surfaces:
//   0 = top    (walkable surface, normal pointing UP)
//   1 = bottom (ceiling of floor below, normal pointing DOWN)
//   2 = sides  (4 lateral faces, normals pointing outward)

public static class FloorMeshBuilder
{
    public const int SurfaceTop    = 0;
    public const int SurfaceBottom = 1;
    public const int SurfaceSides  = 2;

    private const float HalfX =  0.5f;
    private const float HalfZ =  0.5f;
    private const float HalfY =  0.05f; // half of 0.1m height

    public static ArrayMesh Build()
    {
        var mesh = new ArrayMesh();
        MeshHelper.AddSurface(mesh, BuildTop());
        MeshHelper.AddSurface(mesh, BuildBottom());
        MeshHelper.AddSurface(mesh, BuildSides());
        return mesh;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Adds a quad (2 triangles) to the surface tool.
    // Vertices must be given in counter-clockwise order when viewed
    // from the direction the normal points (Godot uses CCW front faces).
    //
    //  v0 ── v1
    //  │    ╱ │
    //  │  ╱   │
    //  v3 ── v2
    //
    // Triangle 1: v0, v1, v2
    // Triangle 2: v0, v2, v3

    // ── Top face (Y = +HalfY, normal = Vector3.Up) ───────────────────────────

    private static SurfaceTool BuildTop()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Viewed from above (normal pointing up = +Y)
        // CCW when looking down from above:
        //  v0(-X,+Z) ── v1(+X,+Z)
        //  │                    │
        //  v3(-X,-Z) ── v2(+X,-Z)
        MeshHelper.AddQuad(st,
            new Vector3(-HalfX,  HalfY,  HalfZ),  // v0
            new Vector3( HalfX,  HalfY,  HalfZ),  // v1
            new Vector3( HalfX,  HalfY, -HalfZ),  // v2
            new Vector3(-HalfX,  HalfY, -HalfZ),  // v3
            Vector3.Up,
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1)
        );

        return st;
    }

    // ── Bottom face (Y = -HalfY, normal = Vector3.Down) ─────────────────────

    private static SurfaceTool BuildBottom()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Viewed from below (normal pointing down = -Y)
        // CCW when looking up from below — reverse X order:
        //  v0(+X,-Y,+Z) ── v1(-X,-Y,+Z)
        //  │                           │
        //  v3(+X,-Y,-Z) ── v2(-X,-Y,-Z)
        MeshHelper.AddQuad(st,
            new Vector3( HalfX, -HalfY,  HalfZ),  // v0
            new Vector3(-HalfX, -HalfY,  HalfZ),  // v1
            new Vector3(-HalfX, -HalfY, -HalfZ),  // v2
            new Vector3( HalfX, -HalfY, -HalfZ),  // v3
            Vector3.Down,
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(1, 1), new Vector2(0, 1)
        );

        return st;
    }

    // ── Four side faces ───────────────────────────────────────────────────────

    private static SurfaceTool BuildSides()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Front face (+Z, normal = +Z) — v1 and v3 swapped vs top/bottom
        MeshHelper.AddQuad(st,
            new Vector3(-HalfX,  HalfY,  HalfZ),
            new Vector3(-HalfX, -HalfY,  HalfZ),
            new Vector3( HalfX, -HalfY,  HalfZ),
            new Vector3( HalfX,  HalfY,  HalfZ),
            new Vector3(0, 0, 1),
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        // Back face (-Z, normal = -Z)
        MeshHelper.AddQuad(st,
            new Vector3( HalfX,  HalfY, -HalfZ),
            new Vector3( HalfX, -HalfY, -HalfZ),
            new Vector3(-HalfX, -HalfY, -HalfZ),
            new Vector3(-HalfX,  HalfY, -HalfZ),
            new Vector3(0, 0, -1),
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        // Right face (+X, normal = +X)
        MeshHelper.AddQuad(st,
            new Vector3( HalfX,  HalfY,  HalfZ),
            new Vector3( HalfX, -HalfY,  HalfZ),
            new Vector3( HalfX, -HalfY, -HalfZ),
            new Vector3( HalfX,  HalfY, -HalfZ),
            new Vector3(1, 0, 0),
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        // Left face (-X, normal = -X)
        MeshHelper.AddQuad(st,
            new Vector3(-HalfX,  HalfY, -HalfZ),
            new Vector3(-HalfX, -HalfY, -HalfZ),
            new Vector3(-HalfX, -HalfY,  HalfZ),
            new Vector3(-HalfX,  HalfY,  HalfZ),
            new Vector3(-1, 0, 0),
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        );

        return st;
    }
}
