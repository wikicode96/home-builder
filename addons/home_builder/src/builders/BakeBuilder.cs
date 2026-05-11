using Godot;
using System.Collections.Generic;

[Tool]
public partial class BakeBuilder
{
    private readonly HomeBuilderPlugin _plugin;

    public BakeBuilder(HomeBuilderPlugin plugin)
    {
        _plugin = plugin;
    }

    public void Bake(
        Node3D sceneRoot,
        string outputFolder,
        float lod0End,
        float lod1Begin,
        GeometryInstance3D.VisibilityRangeFadeModeEnum fadeMode)
    {
        if (sceneRoot == null)
        {
            GD.PrintErr("[BakeBuilder] No hay escena activa");
            return;
        }
        if (string.IsNullOrEmpty(outputFolder))
        {
            GD.PrintErr("[BakeBuilder] Carpeta de salida vacía");
            return;
        }
        if (!outputFolder.StartsWith("res://"))
        {
            GD.PrintErr("[BakeBuilder] La carpeta debe ser una ruta res://");
            return;
        }

        var meshInstances   = new List<MeshInstance3D>();
        var collisionShapes = new List<CollisionShape3D>();
        CollectGeometry(sceneRoot, meshInstances, collisionShapes);

        if (meshInstances.Count == 0)
        {
            GD.PrintErr("[BakeBuilder] No se han encontrado meshes en la escena");
            return;
        }

        var rootInverse = sceneRoot.GlobalTransform.AffineInverse();

        var lod0Mesh = MergeMeshes(meshInstances, rootInverse, simplifyMaterials: false);
        var lod1Mesh = MergeMeshes(meshInstances, rootInverse, simplifyMaterials: true);

        var bakedRoot = new StaticBody3D { Name = sceneRoot.Name };

        float fadeMargin = fadeMode == GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled
            ? 0f
            : Mathf.Max(0f, lod0End - lod1Begin);

        var lod0Inst = new MeshInstance3D
        {
            Name                       = "LOD0",
            Mesh                       = lod0Mesh,
            VisibilityRangeEnd         = lod0End,
            VisibilityRangeEndMargin   = fadeMargin,
            VisibilityRangeFadeMode    = fadeMode,
        };
        bakedRoot.AddChild(lod0Inst);

        var lod1Inst = new MeshInstance3D
        {
            Name                       = "LOD1",
            Mesh                       = lod1Mesh,
            VisibilityRangeBegin       = lod1Begin,
            VisibilityRangeBeginMargin = fadeMargin,
            VisibilityRangeFadeMode    = fadeMode,
        };
        bakedRoot.AddChild(lod1Inst);

        int colIdx = 0;
        foreach (var col in collisionShapes)
        {
            if (col.Shape == null) continue;
            var newCol = new CollisionShape3D
            {
                Name      = $"Collision_{colIdx++}",
                Shape     = col.Shape,
                Transform = rootInverse * col.GlobalTransform,
            };
            bakedRoot.AddChild(newCol);
        }

        SetOwnerRecursive(bakedRoot, bakedRoot);

        var packedScene = new PackedScene();
        var packResult  = packedScene.Pack(bakedRoot);
        if (packResult != Error.Ok)
        {
            GD.PrintErr($"[BakeBuilder] Pack falló: {packResult}");
            bakedRoot.QueueFree();
            return;
        }

        string fileName = sceneRoot.Name + ".tscn";
        string fullPath = outputFolder.TrimEnd('/') + "/" + fileName;

        var saveResult = ResourceSaver.Save(packedScene, fullPath);
        if (saveResult != Error.Ok)
            GD.PrintErr($"[BakeBuilder] Save falló: {saveResult}");
        else
            GD.Print($"[BakeBuilder] Guardado: {fullPath}");

        bakedRoot.QueueFree();
    }

    private static void CollectGeometry(
        Node node,
        List<MeshInstance3D> meshes,
        List<CollisionShape3D> cols)
    {
        if (node is MeshInstance3D mi)   meshes.Add(mi);
        if (node is CollisionShape3D cs) cols.Add(cs);
        foreach (Node child in node.GetChildren())
            CollectGeometry(child, meshes, cols);
    }

    private static ArrayMesh MergeMeshes(
        List<MeshInstance3D> instances,
        Transform3D rootInverse,
        bool simplifyMaterials)
    {
        var result = new ArrayMesh();

        foreach (var mi in instances)
        {
            if (mi.Mesh == null) continue;
            var localToRoot = rootInverse * mi.GlobalTransform;
            bool flipWinding = localToRoot.Basis.Determinant() < 0f;

            int surfCount = mi.Mesh.GetSurfaceCount();
            for (int s = 0; s < surfCount; s++)
            {
                var arrays = mi.Mesh.SurfaceGetArrays(s);
                if (arrays.Count == 0) continue;

                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (vertices == null || vertices.Length == 0) continue;

                for (int v = 0; v < vertices.Length; v++)
                    vertices[v] = localToRoot * vertices[v];
                arrays[(int)Mesh.ArrayType.Vertex] = vertices;

                var normalsVar = arrays[(int)Mesh.ArrayType.Normal];
                if (normalsVar.VariantType == Variant.Type.PackedVector3Array)
                {
                    var normals = normalsVar.AsVector3Array();
                    for (int v = 0; v < normals.Length; v++)
                        normals[v] = (localToRoot.Basis * normals[v]).Normalized();
                    arrays[(int)Mesh.ArrayType.Normal] = normals;
                }

                // When localToRoot has a negative determinant (e.g. walls use a
                // reflected basis: basisZ = up×dir), applying it to vertex positions
                // reverses CCW→CW winding. Godot auto-corrects this for live
                // MeshInstances, but baked geometry loses that information.
                if (flipWinding)
                    FlipTriangleWinding(arrays);

                int newSurfIdx = result.GetSurfaceCount();
                result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

                var mat = mi.GetActiveMaterial(s);
                if (simplifyMaterials) mat = SimplifyMaterial(mat);
                if (mat != null)
                    result.SurfaceSetMaterial(newSurfIdx, mat);
            }
        }

        return result;
    }

    private static Material SimplifyMaterial(Material source)
    {
        if (source is not StandardMaterial3D std)
            return source;

        var s = new StandardMaterial3D();
        s.AlbedoTexture = std.AlbedoTexture;   // textura visible
        s.AlbedoColor   = std.AlbedoColor;
        s.Uv1Scale      = std.Uv1Scale;
        s.Uv1Offset     = std.Uv1Offset;
        s.Transparency  = std.Transparency;
        // Roughness/metallic como valores escalares, sin texturas extra
        s.Roughness     = std.RoughnessTexture != null ? 0.7f : std.Roughness;
        s.Metallic      = std.MetallicTexture  != null ? 0.0f : std.Metallic;
        // Normal map, AO, emisión y heightmap se descartan en LOD1
        return s;
    }

    private static void SetOwnerRecursive(Node node, Node owner)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child != owner) child.Owner = owner;
            SetOwnerRecursive(child, owner);
        }
    }

    // Reverses the winding of every triangle in the surface arrays so that faces
    // remain front-facing after a transform with negative determinant is applied.
    // Handles both indexed meshes (swap index pairs) and non-indexed (swap per-vertex data).
    private static void FlipTriangleWinding(Godot.Collections.Array arrays)
    {
        var idxVariant = arrays[(int)Mesh.ArrayType.Index];
        if (idxVariant.VariantType == Variant.Type.PackedInt32Array)
        {
            var indices = idxVariant.AsInt32Array();
            for (int i = 0; i + 2 < indices.Length; i += 3)
                (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
            arrays[(int)Mesh.ArrayType.Index] = indices;
            return;
        }

        // Non-indexed: swap per-vertex data at positions 3k+1 ↔ 3k+2
        SwapVector3Every3(arrays, Mesh.ArrayType.Vertex);
        SwapVector3Every3(arrays, Mesh.ArrayType.Normal);
        SwapVector2Every3(arrays, Mesh.ArrayType.TexUV);
        SwapVector2Every3(arrays, Mesh.ArrayType.TexUV2);
        SwapTangentsEvery3(arrays);
    }

    private static void SwapVector3Every3(Godot.Collections.Array arrays, Mesh.ArrayType type)
    {
        var v = arrays[(int)type];
        if (v.VariantType != Variant.Type.PackedVector3Array) return;
        var arr = v.AsVector3Array();
        for (int i = 0; i + 2 < arr.Length; i += 3)
            (arr[i + 1], arr[i + 2]) = (arr[i + 2], arr[i + 1]);
        arrays[(int)type] = arr;
    }

    private static void SwapVector2Every3(Godot.Collections.Array arrays, Mesh.ArrayType type)
    {
        var v = arrays[(int)type];
        if (v.VariantType != Variant.Type.PackedVector2Array) return;
        var arr = v.AsVector2Array();
        for (int i = 0; i + 2 < arr.Length; i += 3)
            (arr[i + 1], arr[i + 2]) = (arr[i + 2], arr[i + 1]);
        arrays[(int)type] = arr;
    }

    private static void SwapTangentsEvery3(Godot.Collections.Array arrays)
    {
        var v = arrays[(int)Mesh.ArrayType.Tangent];
        if (v.VariantType != Variant.Type.PackedFloat32Array) return;
        var arr = v.AsFloat32Array();
        if (arr.Length % 4 != 0) return;
        int vertCount = arr.Length / 4;
        for (int i = 0; i + 2 < vertCount; i += 3)
        {
            int p1 = (i + 1) * 4, p2 = (i + 2) * 4;
            for (int k = 0; k < 4; k++)
                (arr[p1 + k], arr[p2 + k]) = (arr[p2 + k], arr[p1 + k]);
        }
        arrays[(int)Mesh.ArrayType.Tangent] = arr;
    }
}
