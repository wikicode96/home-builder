using Godot;
using System.Collections.Generic;

// Generates an ArrayMesh that fills the gap polygons at wall junctions
// (X-, Y-, and other multi-wall nodes where individual wall tops would leave
// an uncovered region). Each fill is a convex polygon in the XZ plane,
// extruded to a flat top face (y = +hy) and bottom face (y = -hy).
//
// Side faces of the gap are already covered by each wall's end/start cap, so
// only the horizontal faces are needed here.

public static class JunctionFillMeshBuilder
{
    public static ArrayMesh Build(List<WallJunctionSolver.JunctionFill> fills, float wallHeight)
    {
        if (fills == null || fills.Count == 0) return null;

        float hy = wallHeight * 0.5f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        foreach (var fill in fills)
        {
            var pts = fill.PointsXZ;
            int n = pts.Length;
            if (n < 3) continue;

            var c = Vector2.Zero;
            foreach (var p in pts) c += p;
            c /= n;

            for (int i = 0; i < n; i++)
            {
                var p0 = pts[i];
                var p1 = pts[(i + 1) % n];

                // Top face — normal up, CCW winding from above.
                AddTri(st,
                    new Vector3(c.X,  hy, c.Y),
                    new Vector3(p0.X, hy, p0.Y),
                    new Vector3(p1.X, hy, p1.Y),
                    Vector3.Up);

                // Bottom face — normal down, CCW winding from below (reversed).
                AddTri(st,
                    new Vector3(c.X,  -hy, c.Y),
                    new Vector3(p1.X, -hy, p1.Y),
                    new Vector3(p0.X, -hy, p0.Y),
                    Vector3.Down);
            }
        }

        var mesh = new ArrayMesh();
        MeshHelper.AddSurface(mesh, st);
        return mesh;
    }

    private static void AddTri(SurfaceTool st,
        Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal)
    {
        st.SetNormal(normal); st.SetUV(new Vector2(0.5f, 0.5f)); st.AddVertex(v0);
        st.SetNormal(normal); st.SetUV(new Vector2(0f,   1f));   st.AddVertex(v1);
        st.SetNormal(normal); st.SetUV(new Vector2(1f,   0f));   st.AddVertex(v2);
    }
}
