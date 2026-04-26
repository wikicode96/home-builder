using Godot;

public static class MeshHelper
{
    public static void AddSurface(ArrayMesh mesh, SurfaceTool st)
    {
        st.GenerateTangents();
        st.Commit(mesh);
    }

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
    public static void AddQuad(SurfaceTool st,
        Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
        Vector3 normal,
        Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
    {
        // CW winding — Godot's default front face is CW in right-handed coords
        st.SetNormal(normal); st.SetUV(uv0); st.AddVertex(v0);
        st.SetNormal(normal); st.SetUV(uv2); st.AddVertex(v2);
        st.SetNormal(normal); st.SetUV(uv1); st.AddVertex(v1);

        st.SetNormal(normal); st.SetUV(uv0); st.AddVertex(v0);
        st.SetNormal(normal); st.SetUV(uv3); st.AddVertex(v3);
        st.SetNormal(normal); st.SetUV(uv2); st.AddVertex(v2);
    }

    // Adds a single triangle to the surface tool. Vertices in CCW order
    // when viewed from the direction the normal points.
    public static void AddTriangle(SurfaceTool st,
        Vector3 v0, Vector3 v1, Vector3 v2,
        Vector3 normal,
        Vector2 uv0, Vector2 uv1, Vector2 uv2)
    {
        st.SetNormal(normal); st.SetUV(uv0); st.AddVertex(v0);
        st.SetNormal(normal); st.SetUV(uv2); st.AddVertex(v2);
        st.SetNormal(normal); st.SetUV(uv1); st.AddVertex(v1);
    }
}
