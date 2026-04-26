using Godot;

public enum RoofType { Flat, Shed, Gable, Hip }
public enum RoofDirection { North, South, East, West }

// Generates an ArrayMesh for a roof covering a footprint of (w × d).
// Three surfaces:
//   0 = top    (slope skin)
//   1 = bottom (underside)
//   2 = sides  (gable triangles + back vertical wall on shed roofs)
//
// Local origin sits at the minimum corner (0, 0, 0). Vertices live in
// the box (0..w, 0..pitch, 0..d). Direction is applied as a 90° rotation
// around +Y about the footprint center.
public static class RoofMeshBuilder
{
    public const int SurfaceTop    = 0;
    public const int SurfaceBottom = 1;
    public const int SurfaceSides  = 2;

    private const float FlatThickness = 0.15f;

    public static ArrayMesh Build(RoofType type, float w, float d, float pitch, RoofDirection dir)
    {
        var top    = new SurfaceTool(); top.Begin(Mesh.PrimitiveType.Triangles);
        var bot    = new SurfaceTool(); bot.Begin(Mesh.PrimitiveType.Triangles);
        var sides  = new SurfaceTool(); sides.Begin(Mesh.PrimitiveType.Triangles);

        int    rot = RotSteps(type, dir);
        bool   swap = (rot % 2) != 0;
        float  cw   = swap ? d : w;
        float  cd   = swap ? w : d;
        var    tf   = MakeOrient(rot, cw, cd, w, d);

        switch (type)
        {
            case RoofType.Flat:  BuildFlat (top, bot, sides, cw, cd,        tf); break;
            case RoofType.Shed:  BuildShed (top, bot, sides, cw, cd, pitch, tf); break;
            case RoofType.Gable: BuildGable(top, bot, sides, cw, cd, pitch, tf); break;
            case RoofType.Hip:   BuildHip  (top, bot, sides, cw, cd, pitch, tf); break;
        }

        var mesh = new ArrayMesh();
        MeshHelper.AddSurface(mesh, top);
        MeshHelper.AddSurface(mesh, bot);
        MeshHelper.AddSurface(mesh, sides);
        return mesh;
    }

    // ── Orientation ──────────────────────────────────────────────────────────

    private static int RotSteps(RoofType type, RoofDirection dir)
    {
        // Flat and Hip are rotation-invariant (4-way symmetric for our purposes).
        if (type == RoofType.Flat || type == RoofType.Hip) return 0;

        // Shed: canonical high edge at -Z (North). Rotating 90° CCW around +Y
        // sends -Z → -X (West).
        if (type == RoofType.Shed)
            return dir switch
            {
                RoofDirection.North => 0,
                RoofDirection.West  => 1,
                RoofDirection.South => 2,
                RoofDirection.East  => 3,
                _                   => 0,
            };

        // Gable: canonical ridge along X (gable ends face E/W). N/S means
        // gable ends face E/W (canonical). E/W means ridge along Z.
        return dir switch
        {
            RoofDirection.North or RoofDirection.South => 0,
            RoofDirection.East  or RoofDirection.West  => 1,
            _                                          => 0,
        };
    }

    private static Transform3D MakeOrient(int n, float cw, float cd, float w, float d)
    {
        if (n == 0) return Transform3D.Identity;
        var basis     = new Basis(Vector3.Up, n * Mathf.Pi / 2f);
        var cCenter   = new Vector3(cw / 2f, 0, cd / 2f);
        var actCenter = new Vector3(w  / 2f, 0, d  / 2f);
        return new Transform3D(basis, actCenter - basis * cCenter);
    }

    private static Vector3 V(float x, float y, float z, Transform3D tf) =>
        tf * new Vector3(x, y, z);

    private static Vector3 N(Vector3 normal, Transform3D tf) =>
        (tf.Basis * normal).Normalized();

    // ── Flat ─────────────────────────────────────────────────────────────────

    private static void BuildFlat(SurfaceTool top, SurfaceTool bot, SurfaceTool sides,
        float w, float d, Transform3D tf)
    {
        const float t = FlatThickness;

        MeshHelper.AddQuad(top,
            V(0, t, d, tf), V(w, t, d, tf), V(w, t, 0, tf), V(0, t, 0, tf),
            N(Vector3.Up, tf),
            new Vector2(0, d), new Vector2(w, d), new Vector2(w, 0), new Vector2(0, 0));

        MeshHelper.AddQuad(bot,
            V(w, 0, d, tf), V(0, 0, d, tf), V(0, 0, 0, tf), V(w, 0, 0, tf),
            N(Vector3.Down, tf),
            new Vector2(0, d), new Vector2(w, d), new Vector2(w, 0), new Vector2(0, 0));

        AddVerticalQuad(sides, V(0, 0, d, tf), V(w, 0, d, tf), t,
            N(new Vector3(0, 0,  1), tf), w);
        AddVerticalQuad(sides, V(w, 0, 0, tf), V(0, 0, 0, tf), t,
            N(new Vector3(0, 0, -1), tf), w);
        AddVerticalQuad(sides, V(w, 0, d, tf), V(w, 0, 0, tf), t,
            N(new Vector3( 1, 0, 0), tf), d);
        AddVerticalQuad(sides, V(0, 0, 0, tf), V(0, 0, d, tf), t,
            N(new Vector3(-1, 0, 0), tf), d);
    }

    private static void AddVerticalQuad(SurfaceTool st, Vector3 a, Vector3 b,
        float h, Vector3 normal, float length)
    {
        var top1 = a + new Vector3(0, h, 0);
        var top2 = b + new Vector3(0, h, 0);
        MeshHelper.AddQuad(st, a, b, top2, top1, normal,
            new Vector2(0, h), new Vector2(length, h),
            new Vector2(length, 0), new Vector2(0, 0));
    }

    // ── Shed (a un agua) — high edge at -Z ───────────────────────────────────

    private static void BuildShed(SurfaceTool top, SurfaceTool bot, SurfaceTool sides,
        float w, float d, float p, Transform3D tf)
    {
        var slopeNormal = new Vector3(0, d, p).Normalized();

        // Top sloped quad: low at +Z (z=d), high at -Z (z=0)
        MeshHelper.AddQuad(top,
            V(0, 0, d, tf), V(w, 0, d, tf), V(w, p, 0, tf), V(0, p, 0, tf),
            N(slopeNormal, tf),
            new Vector2(0, d), new Vector2(w, d),
            new Vector2(w, 0), new Vector2(0, 0));

        // Bottom flat
        MeshHelper.AddQuad(bot,
            V(w, 0, d, tf), V(0, 0, d, tf), V(0, 0, 0, tf), V(w, 0, 0, tf),
            N(Vector3.Down, tf),
            new Vector2(0, d), new Vector2(w, d),
            new Vector2(w, 0), new Vector2(0, 0));

        // Back vertical wall (-Z side, height p)
        MeshHelper.AddQuad(sides,
            V(w, 0, 0, tf), V(0, 0, 0, tf), V(0, p, 0, tf), V(w, p, 0, tf),
            N(new Vector3(0, 0, -1), tf),
            new Vector2(0, p), new Vector2(w, p),
            new Vector2(w, 0), new Vector2(0, 0));

        // Left gable triangle (-X)
        MeshHelper.AddTriangle(sides,
            V(0, 0, 0, tf), V(0, 0, d, tf), V(0, p, 0, tf),
            N(new Vector3(-1, 0, 0), tf),
            new Vector2(0, 0), new Vector2(d, 0), new Vector2(0, p));

        // Right gable triangle (+X)
        MeshHelper.AddTriangle(sides,
            V(w, 0, 0, tf), V(w, p, 0, tf), V(w, 0, d, tf),
            N(new Vector3(1, 0, 0), tf),
            new Vector2(0, 0), new Vector2(0, p), new Vector2(d, 0));
    }

    // ── Gable (a dos aguas) — ridge along X at z = d/2 ───────────────────────

    private static void BuildGable(SurfaceTool top, SurfaceTool bot, SurfaceTool sides,
        float w, float d, float p, Transform3D tf)
    {
        float halfD  = d * 0.5f;
        var   nFront = new Vector3(0, halfD,  p).Normalized();
        var   nBack  = new Vector3(0, halfD, -p).Normalized();

        // Front slope: from low edge at z=d up to ridge at z=d/2
        MeshHelper.AddQuad(top,
            V(0, 0, d, tf), V(w, 0, d, tf),
            V(w, p, halfD, tf), V(0, p, halfD, tf),
            N(nFront, tf),
            new Vector2(0, halfD), new Vector2(w, halfD),
            new Vector2(w, 0),     new Vector2(0, 0));

        // Back slope: from ridge down to low edge at z=0
        MeshHelper.AddQuad(top,
            V(0, p, halfD, tf), V(w, p, halfD, tf),
            V(w, 0, 0, tf),     V(0, 0, 0, tf),
            N(nBack, tf),
            new Vector2(0, 0),     new Vector2(w, 0),
            new Vector2(w, halfD), new Vector2(0, halfD));

        // Bottom flat
        MeshHelper.AddQuad(bot,
            V(w, 0, d, tf), V(0, 0, d, tf), V(0, 0, 0, tf), V(w, 0, 0, tf),
            N(Vector3.Down, tf),
            new Vector2(0, d), new Vector2(w, d),
            new Vector2(w, 0), new Vector2(0, 0));

        // Left gable triangle (-X)
        MeshHelper.AddTriangle(sides,
            V(0, 0, 0, tf), V(0, 0, d, tf), V(0, p, halfD, tf),
            N(new Vector3(-1, 0, 0), tf),
            new Vector2(0, 0), new Vector2(d, 0), new Vector2(halfD, p));

        // Right gable triangle (+X)
        MeshHelper.AddTriangle(sides,
            V(w, 0, d, tf), V(w, 0, 0, tf), V(w, p, halfD, tf),
            N(new Vector3(1, 0, 0), tf),
            new Vector2(0, 0), new Vector2(d, 0), new Vector2(halfD, p));
    }

    // ── Hip (a cuatro aguas) — ridge along longer axis ───────────────────────

    private static void BuildHip(SurfaceTool top, SurfaceTool bot, SurfaceTool sides,
        float w, float d, float p, Transform3D tf)
    {
        bool ridgeAlongX = w >= d;
        float a = ridgeAlongX ? d * 0.5f : w * 0.5f;

        Vector3 ridgeLo, ridgeHi;
        if (ridgeAlongX)
        {
            ridgeLo = new Vector3(a,     p, a);
            ridgeHi = new Vector3(w - a, p, a);
        }
        else
        {
            ridgeLo = new Vector3(a, p, a);
            ridgeHi = new Vector3(a, p, d - a);
        }

        Vector3 c00 = new(0, 0, 0);
        Vector3 c10 = new(w, 0, 0);
        Vector3 c11 = new(w, 0, d);
        Vector3 c01 = new(0, 0, d);

        if (ridgeAlongX)
        {
            // Front trapezoid (+Z)
            MeshHelper.AddQuad(top,
                V(c01.X, c01.Y, c01.Z, tf), V(c11.X, c11.Y, c11.Z, tf),
                V(ridgeHi.X, ridgeHi.Y, ridgeHi.Z, tf),
                V(ridgeLo.X, ridgeLo.Y, ridgeLo.Z, tf),
                N(new Vector3(0, a,  p).Normalized(), tf),
                new Vector2(0, a), new Vector2(w, a),
                new Vector2(w - a, 0), new Vector2(a, 0));

            // Back trapezoid (-Z)
            MeshHelper.AddQuad(top,
                V(c10.X, c10.Y, c10.Z, tf), V(c00.X, c00.Y, c00.Z, tf),
                V(ridgeLo.X, ridgeLo.Y, ridgeLo.Z, tf),
                V(ridgeHi.X, ridgeHi.Y, ridgeHi.Z, tf),
                N(new Vector3(0, a, -p).Normalized(), tf),
                new Vector2(0, a), new Vector2(w, a),
                new Vector2(w - a, 0), new Vector2(a, 0));

            // Left triangle (-X)
            MeshHelper.AddTriangle(top,
                V(c00.X, c00.Y, c00.Z, tf), V(c01.X, c01.Y, c01.Z, tf),
                V(ridgeLo.X, ridgeLo.Y, ridgeLo.Z, tf),
                N(new Vector3(-p, a, 0).Normalized(), tf),
                new Vector2(0, 0), new Vector2(d, 0), new Vector2(a, p));

            // Right triangle (+X)
            MeshHelper.AddTriangle(top,
                V(c11.X, c11.Y, c11.Z, tf), V(c10.X, c10.Y, c10.Z, tf),
                V(ridgeHi.X, ridgeHi.Y, ridgeHi.Z, tf),
                N(new Vector3(p, a, 0).Normalized(), tf),
                new Vector2(0, 0), new Vector2(d, 0), new Vector2(a, p));
        }
        else
        {
            // Left trapezoid (-X)
            MeshHelper.AddQuad(top,
                V(c00.X, c00.Y, c00.Z, tf), V(c01.X, c01.Y, c01.Z, tf),
                V(ridgeHi.X, ridgeHi.Y, ridgeHi.Z, tf),
                V(ridgeLo.X, ridgeLo.Y, ridgeLo.Z, tf),
                N(new Vector3(-p, a, 0).Normalized(), tf),
                new Vector2(0, a), new Vector2(d, a),
                new Vector2(d - a, 0), new Vector2(a, 0));

            // Right trapezoid (+X)
            MeshHelper.AddQuad(top,
                V(c11.X, c11.Y, c11.Z, tf), V(c10.X, c10.Y, c10.Z, tf),
                V(ridgeLo.X, ridgeLo.Y, ridgeLo.Z, tf),
                V(ridgeHi.X, ridgeHi.Y, ridgeHi.Z, tf),
                N(new Vector3(p, a, 0).Normalized(), tf),
                new Vector2(0, a), new Vector2(d, a),
                new Vector2(d - a, 0), new Vector2(a, 0));

            // Front triangle (+Z)
            MeshHelper.AddTriangle(top,
                V(c01.X, c01.Y, c01.Z, tf), V(c11.X, c11.Y, c11.Z, tf),
                V(ridgeHi.X, ridgeHi.Y, ridgeHi.Z, tf),
                N(new Vector3(0, a, p).Normalized(), tf),
                new Vector2(0, 0), new Vector2(w, 0), new Vector2(a, p));

            // Back triangle (-Z)
            MeshHelper.AddTriangle(top,
                V(c10.X, c10.Y, c10.Z, tf), V(c00.X, c00.Y, c00.Z, tf),
                V(ridgeLo.X, ridgeLo.Y, ridgeLo.Z, tf),
                N(new Vector3(0, a, -p).Normalized(), tf),
                new Vector2(0, 0), new Vector2(w, 0), new Vector2(a, p));
        }

        // Bottom flat
        MeshHelper.AddQuad(bot,
            V(w, 0, d, tf), V(0, 0, d, tf), V(0, 0, 0, tf), V(w, 0, 0, tf),
            N(Vector3.Down, tf),
            new Vector2(0, d), new Vector2(w, d),
            new Vector2(w, 0), new Vector2(0, 0));
    }
}
