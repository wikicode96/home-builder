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

        // LOD0: geometría y materiales completos, una surface por surface original.
        var lod0Mesh = MergeMeshesFlat(meshInstances, rootInverse, simplifyMaterials: false);

        // LOD1: walls sin aperturas + materiales simplificados + surfaces fusionadas
        // por material para minimizar draw calls al ver el edificio desde lejos.
        var lod1Mesh = MergeMeshesByMaterial(meshInstances, rootInverse);

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

    // -------------------------------------------------------------------------
    // Geometry collection
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // LOD0 merge — one ArrayMesh surface per source surface, original materials
    // -------------------------------------------------------------------------

    private static ArrayMesh MergeMeshesFlat(
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
                if (!TransformArraysInPlace(arrays, localToRoot)) continue;
                if (flipWinding) FlipTriangleWinding(arrays);

                int newSurfIdx = result.GetSurfaceCount();
                result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

                var mat = mi.GetActiveMaterial(s);
                if (simplifyMaterials) mat = SimplifyMaterial(mat);
                if (mat != null) result.SurfaceSetMaterial(newSurfIdx, mat);
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // LOD1 merge — surfaces grouped by material → one draw call per material
    //
    // Wall MeshInstances whose StaticBody3D parent carries the wall-length
    // metadata are processed using a freshly built solid mesh (no openings),
    // which removes the door/window frame triangles invisible at LOD1 distance.
    // All surfaces sharing the same original material are concatenated into one
    // surface in the output mesh to minimise draw calls over open terrain.
    // -------------------------------------------------------------------------

    private static ArrayMesh MergeMeshesByMaterial(
        List<MeshInstance3D> instances,
        Transform3D rootInverse)
    {
        // Key: original material InstanceId (ulong.MaxValue for null material).
        var groupArrays    = new Dictionary<ulong, List<Godot.Collections.Array>>();
        var groupMaterials = new Dictionary<ulong, Material>();
        var groupOrder     = new List<ulong>();

        const ulong nullKey = ulong.MaxValue;

        foreach (var mi in instances)
        {
            if (mi.Mesh == null) continue;

            // For wall MeshInstances, substitute a solid mesh (no openings) so
            // that the baked LOD1 has fewer triangles. The original node stays
            // in the scene tree; we only swap the Mesh resource used for baking.
            Mesh bakeSource = mi.Mesh;
            if (mi.GetParent() is StaticBody3D body && body.HasMeta(WallHelper.MetaWallLength))
            {
                float len = body.GetMeta(WallHelper.MetaWallLength).AsSingle();
                bakeSource = WallMeshBuilder.Build(len, WallBuilder.Height, WallBuilder.Thickness);
            }

            var localToRoot = rootInverse * mi.GlobalTransform;
            bool flipWinding = localToRoot.Basis.Determinant() < 0f;

            int surfCount = bakeSource.GetSurfaceCount();
            for (int s = 0; s < surfCount; s++)
            {
                var arrays = bakeSource.SurfaceGetArrays(s);
                if (arrays.Count == 0) continue;
                if (!TransformArraysInPlace(arrays, localToRoot)) continue;
                if (flipWinding) FlipTriangleWinding(arrays);

                // Material always comes from the original scene node so we pick
                // up the user's configured surface overrides, not the raw mesh.
                var origMat = mi.GetActiveMaterial(s);
                ulong key   = origMat != null ? origMat.GetInstanceId() : nullKey;

                if (!groupArrays.ContainsKey(key))
                {
                    groupArrays[key]    = new List<Godot.Collections.Array>();
                    groupMaterials[key] = SimplifyMaterial(origMat);
                    groupOrder.Add(key);
                }
                groupArrays[key].Add(arrays);
            }
        }

        var result = new ArrayMesh();
        foreach (var key in groupOrder)
        {
            var merged = ConcatenateArrays(groupArrays[key]);
            if (merged == null) continue;

            int surfIdx = result.GetSurfaceCount();
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, merged);

            var mat = groupMaterials[key];
            if (mat != null) result.SurfaceSetMaterial(surfIdx, mat);
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Material simplification
    //
    // Duplicate() copies ALL properties — Uv1Scale, Uv1Triplanar, CullMode,
    // TextureFilter, etc. — so UV tiling and material look are preserved.
    // Only the expensive per-pixel textures (normal, AO, heightmap) are stripped
    // because they contribute nothing at LOD1 viewing distances.
    // -------------------------------------------------------------------------

    private static Material SimplifyMaterial(Material source)
    {
        if (source == null) return null;
        if (source is not StandardMaterial3D std) return source;

        var s = (StandardMaterial3D)std.Duplicate();
        s.NormalEnabled    = false;
        s.NormalTexture    = null;
        s.AOEnabled        = false;
        s.AOTexture        = null;
        s.HeightmapEnabled = false;
        s.HeightmapTexture = null;
        if (std.RoughnessTexture != null) { s.Roughness = 0.7f; s.RoughnessTexture = null; }
        if (std.MetallicTexture  != null) { s.Metallic  = 0.0f; s.MetallicTexture  = null; }
        return s;
    }

    // -------------------------------------------------------------------------
    // Array helpers
    // -------------------------------------------------------------------------

    // Transforms vertex positions and normals in-place using localToRoot.
    // Returns false when the surface has no vertices (caller should skip it).
    private static bool TransformArraysInPlace(Godot.Collections.Array arrays, Transform3D t)
    {
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (verts == null || verts.Length == 0) return false;

        for (int v = 0; v < verts.Length; v++)
            verts[v] = t * verts[v];
        arrays[(int)Mesh.ArrayType.Vertex] = verts;

        var nVar = arrays[(int)Mesh.ArrayType.Normal];
        if (nVar.VariantType == Variant.Type.PackedVector3Array)
        {
            var norms = nVar.AsVector3Array();
            for (int v = 0; v < norms.Length; v++)
                norms[v] = (t.Basis * norms[v]).Normalized();
            arrays[(int)Mesh.ArrayType.Normal] = norms;
        }
        return true;
    }

    // Concatenates a list of surface arrays into one, expanding indexed meshes
    // to non-indexed before concatenation so all sources can be merged uniformly.
    private static Godot.Collections.Array ConcatenateArrays(List<Godot.Collections.Array> list)
    {
        var verts    = new List<Vector3>();
        var normals  = new List<Vector3>();
        var uvs      = new List<Vector2>();
        var tangents = new List<float>();
        bool hasNormals = false, hasUVs = false, hasTangents = false;

        foreach (var arrays in list)
        {
            var srcVerts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();

            int[] indices = null;
            var idxVar = arrays[(int)Mesh.ArrayType.Index];
            if (idxVar.VariantType == Variant.Type.PackedInt32Array)
                indices = idxVar.AsInt32Array();

            if (indices != null)
                foreach (int i in indices) verts.Add(srcVerts[i]);
            else
                verts.AddRange(srcVerts);

            var normVar = arrays[(int)Mesh.ArrayType.Normal];
            if (normVar.VariantType == Variant.Type.PackedVector3Array)
            {
                hasNormals = true;
                var srcN = normVar.AsVector3Array();
                if (indices != null)
                    foreach (int i in indices) normals.Add(srcN[i]);
                else
                    normals.AddRange(srcN);
            }

            var uvVar = arrays[(int)Mesh.ArrayType.TexUV];
            if (uvVar.VariantType == Variant.Type.PackedVector2Array)
            {
                hasUVs = true;
                var srcUV = uvVar.AsVector2Array();
                if (indices != null)
                    foreach (int i in indices) uvs.Add(srcUV[i]);
                else
                    uvs.AddRange(srcUV);
            }

            var tanVar = arrays[(int)Mesh.ArrayType.Tangent];
            if (tanVar.VariantType == Variant.Type.PackedFloat32Array)
            {
                hasTangents = true;
                var srcT = tanVar.AsFloat32Array();
                if (indices != null)
                    foreach (int i in indices)
                        for (int k = 0; k < 4; k++)
                            tangents.Add(srcT[i * 4 + k]);
                else
                    tangents.AddRange(srcT);
            }
        }

        if (verts.Count == 0) return null;

        var result = new Godot.Collections.Array();
        result.Resize((int)Mesh.ArrayType.Max);
        result[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        if (hasNormals  && normals.Count  == verts.Count)     result[(int)Mesh.ArrayType.Normal]  = normals.ToArray();
        if (hasUVs      && uvs.Count      == verts.Count)     result[(int)Mesh.ArrayType.TexUV]   = uvs.ToArray();
        if (hasTangents && tangents.Count == verts.Count * 4) result[(int)Mesh.ArrayType.Tangent] = tangents.ToArray();
        return result;
    }

    // -------------------------------------------------------------------------
    // Winding flip — needed when localToRoot has a negative determinant
    // (walls use a reflected basis: basisZ = up × dir).
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Utilities
    // -------------------------------------------------------------------------

    private static void SetOwnerRecursive(Node node, Node owner)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child != owner) child.Owner = owner;
            SetOwnerRecursive(child, owner);
        }
    }
}
