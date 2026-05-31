using Godot;
using System.Collections.Generic;
using System.Linq;

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
        GeometryInstance3D.VisibilityRangeFadeModeEnum fadeMode,
        bool buildNavMesh   = true,
        bool buildOcclusion = true)
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

        var meshInstances = new List<MeshInstance3D>();
        var convexShapes  = new List<CollisionShape3D>();
        CollectGeometry(sceneRoot, meshInstances, convexShapes);

        if (meshInstances.Count == 0)
        {
            GD.PrintErr("[BakeBuilder] No se han encontrado meshes en la escena");
            return;
        }

        // Guard: if lod1Begin > lod0End there would be a dead zone where neither
        // LOD is visible. Clamp lod1Begin so it never exceeds lod0End.
        if (lod1Begin > lod0End)
        {
            GD.PrintErr($"[BakeBuilder] lod1Begin ({lod1Begin}m) > lod0End ({lod0End}m): " +
                        "habría una franja invisible. Se iguala lod1Begin a lod0End.");
            lod1Begin = lod0End;
        }

        var rootInverse = sceneRoot.GlobalTransform.AffineInverse();

        // LOD0: full geometry and materials, one surface per source surface.
        var lod0Mesh = MergeMeshesFlat(meshInstances, rootInverse, simplifyMaterials: false);

        // LOD1: same shape as LOD0 minus stairs and fences (fewer polys at
        // distance), with simplified materials and surfaces merged by material.
        var lod1Instances = meshInstances.FindAll(mi => !IsLod1Excluded(mi));
        var lod1Mesh = MergeMeshesByMaterial(lod1Instances, rootInverse);

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

        if (buildOcclusion)
            bakedRoot.AddChild(BuildOccluder(lod1Mesh));

        // Main collision: visual mesh without stair steps.
        var colInstances = meshInstances.FindAll(mi => !IsStairsMesh(mi));
        var colMesh      = MergeMeshesFlat(colInstances, rootInverse, simplifyMaterials: false);
        bakedRoot.AddChild(new CollisionShape3D
        {
            Name  = "Collision",
            Shape = BuildConcaveShape(colMesh),
        });

        // Staircase ramps: kept as ConvexPolygonShape3D (not merged into the
        // ConcavePolygonShape3D) so CharacterBody3D slides up smoothly.
        // Merging them causes an internal edge at the floor/ramp junction that
        // makes the character catch and stop.
        int stairIdx = 0;
        foreach (var cs in convexShapes)
        {
            if (cs.Shape is not ConvexPolygonShape3D convex) continue;
            var localToRoot = rootInverse * cs.GlobalTransform;
            var pts         = convex.Points;
            var transformed = new Vector3[pts.Length];
            for (int p = 0; p < pts.Length; p++)
                transformed[p] = localToRoot * pts[p];

            bakedRoot.AddChild(new CollisionShape3D
            {
                Name  = $"Staircase_{stairIdx++}",
                Shape = new ConvexPolygonShape3D { Points = transformed },
            });
        }

        if (buildNavMesh)
            bakedRoot.AddChild(BuildNavRegion(colMesh, convexShapes, rootInverse));

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
        {
            GD.Print($"[BakeBuilder] Guardado: {fullPath}");
            ResourceLoader.Load(fullPath, "", ResourceLoader.CacheMode.Replace);
            #if TOOLS
            EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(fullPath);
            #endif
        }

        bakedRoot.QueueFree();
    }

    // -------------------------------------------------------------------------
    // Geometry collection
    // -------------------------------------------------------------------------

    private static void CollectGeometry(
        Node node,
        List<MeshInstance3D> meshes,
        List<CollisionShape3D> convexShapes)
    {
        if (node is MeshInstance3D mi) meshes.Add(mi);
        // Only ConvexPolygonShape3D matters here — that is exclusively the
        // staircase ramp. Box/ConcavePolygon shapes are replaced by the visual mesh.
        if (node is CollisionShape3D cs && cs.Shape is ConvexPolygonShape3D)
            convexShapes.Add(cs);
        foreach (Node child in node.GetChildren())
            CollectGeometry(child, meshes, convexShapes);
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
    // Same geometry as LOD0 (openings and frames preserved) but with simplified
    // materials (no normal/AO/roughmap textures) and surfaces concatenated by
    // material to minimise draw calls when viewing the building from a distance.
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

            var localToRoot = rootInverse * mi.GlobalTransform;
            bool flipWinding = localToRoot.Basis.Determinant() < 0f;

            // For wall bodies, replace the original mesh (which has opening holes
            // and complex geometry) with a solid rectangular mesh — just FaceA and
            // FaceB, no holes, no edge surfaces. Materials are still taken from the
            // original MeshInstance3D so the user's assignments are preserved.
            var simplifiedWall = GetSimplifiedWallMesh(mi);
            var sourceMesh     = simplifiedWall ?? mi.Mesh;
            int surfCount      = simplifiedWall != null
                ? WallMeshBuilder.SurfaceFaceB + 1   // only FaceA (0) and FaceB (1)
                : sourceMesh.GetSurfaceCount();

            for (int s = 0; s < surfCount; s++)
            {
                var arrays = sourceMesh.SurfaceGetArrays(s);
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
        return ReindexSurface(result);
    }

    // -------------------------------------------------------------------------
    // Re-indexing — duplicates vertices shared between merged surfaces.
    // ConcatenateArrays expands all surfaces to non-indexed, so seam vertices
    // between adjacent surfaces are duplicated. This pass rebuilds an index
    // buffer and removes those duplicates, reducing vertex count and improving
    // GPU vertex cache performance.
    // -------------------------------------------------------------------------

    private static Godot.Collections.Array ReindexSurface(Godot.Collections.Array arrays)
    {
        var srcVerts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();

        Vector3[] srcNormals  = null;
        Vector2[] srcUVs      = null;
        float[]   srcTangents = null;

        var normVar = arrays[(int)Mesh.ArrayType.Normal];
        if (normVar.VariantType == Variant.Type.PackedVector3Array)
            srcNormals = normVar.AsVector3Array();

        var uvVar = arrays[(int)Mesh.ArrayType.TexUV];
        if (uvVar.VariantType == Variant.Type.PackedVector2Array)
            srcUVs = uvVar.AsVector2Array();

        var tanVar = arrays[(int)Mesh.ArrayType.Tangent];
        if (tanVar.VariantType == Variant.Type.PackedFloat32Array)
            srcTangents = tanVar.AsFloat32Array();

        var uniqueVerts    = new List<Vector3>(srcVerts.Length);
        var uniqueNormals  = srcNormals  != null ? new List<Vector3>(srcVerts.Length) : null;
        var uniqueUVs      = srcUVs      != null ? new List<Vector2>(srcVerts.Length) : null;
        var uniqueTangents = srcTangents != null ? new List<float>(srcVerts.Length * 4) : null;
        var indices        = new int[srcVerts.Length];

        // Key: (position, normal, uv) — exact float equality is intentional here
        // because all duplicates originate from the same source data and share
        // the exact same bit pattern, not from independent calculations.
        var keyMap = new Dictionary<(Vector3, Vector3, Vector2), int>(srcVerts.Length);

        for (int i = 0; i < srcVerts.Length; i++)
        {
            var key = (
                srcVerts[i],
                srcNormals != null ? srcNormals[i] : Vector3.Zero,
                srcUVs     != null ? srcUVs[i]     : Vector2.Zero
            );

            if (!keyMap.TryGetValue(key, out int idx))
            {
                idx = uniqueVerts.Count;
                keyMap[key] = idx;
                uniqueVerts.Add(srcVerts[i]);
                uniqueNormals?.Add(srcNormals[i]);
                uniqueUVs?.Add(srcUVs[i]);
                if (srcTangents != null)
                    for (int k = 0; k < 4; k++)
                        uniqueTangents.Add(srcTangents[i * 4 + k]);
            }
            indices[i] = idx;
        }

        GD.Print($"[BakeBuilder] ReindexSurface: {srcVerts.Length} → {uniqueVerts.Count} vértices " +
                 $"({srcVerts.Length - uniqueVerts.Count} duplicados eliminados)");

        var result = new Godot.Collections.Array();
        result.Resize((int)Mesh.ArrayType.Max);
        result[(int)Mesh.ArrayType.Vertex]  = uniqueVerts.ToArray();
        if (uniqueNormals  != null) result[(int)Mesh.ArrayType.Normal]  = uniqueNormals.ToArray();
        if (uniqueUVs      != null) result[(int)Mesh.ArrayType.TexUV]   = uniqueUVs.ToArray();
        if (uniqueTangents != null) result[(int)Mesh.ArrayType.Tangent] = uniqueTangents.ToArray();
        result[(int)Mesh.ArrayType.Index]   = indices;
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

    // Returns true when the node is inside a Stairs_*, Fences_*, or Floor_*
    // subtree, so it gets skipped in LOD1 to reduce polygon count at distance.
    private static bool IsLod1Excluded(Node node)
    {
        var current = node.GetParent();
        while (current != null && GodotObject.IsInstanceValid(current))
        {
            string name = (string)current.Name;
            if (name.StartsWith("Stairs_") || name.StartsWith("Fences_") || name.StartsWith("Floor_"))
                return true;
            current = current.GetParent();
        }
        return false;
    }

    // If mi belongs to a wall body (parent StaticBody3D has hb_wall_length meta),
    // returns a solid rectangular wall mesh with no openings and no edge surfaces,
    // so LOD1 walls are clean flat faces. Returns null for non-wall meshes.
    private static ArrayMesh GetSimplifiedWallMesh(MeshInstance3D mi)
    {
        if (mi.GetParent() is StaticBody3D body && body.HasMeta("hb_wall_length"))
        {
            float length = (float)body.GetMeta("hb_wall_length");
            return WallMeshBuilder.Build(length, WallHelper.GetWallHeight(body), WallHelper.GetWallThickness(body));
        }
        return null;
    }

    // Returns true when the node is a step mesh inside a Stairs_* subtree.
    // Used to exclude stair visuals from the collision mesh — the ramp
    // ConvexPolygonShape3D handles walkability instead.
    private static bool IsStairsMesh(Node node)
    {
        var current = node.GetParent();
        while (current != null && GodotObject.IsInstanceValid(current))
        {
            if (((string)current.Name).StartsWith("Stairs_"))
                return true;
            current = current.GetParent();
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // Collision — single ConcavePolygonShape3D.
    //
    // Uses the visual mesh (walls, floors, roof — no stair steps) so
    // door/window openings are real holes and the geometry is exact.
    // Staircases are kept as separate ConvexPolygonShape3D nodes.
    // -------------------------------------------------------------------------

    private static ConcavePolygonShape3D BuildConcaveShape(ArrayMesh mesh)
    {
        var faces = new List<Vector3>();

        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            if (arrays.Count == 0) continue;

            var verts  = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var idxVar = arrays[(int)Mesh.ArrayType.Index];

            if (idxVar.VariantType == Variant.Type.PackedInt32Array)
            {
                var indices = idxVar.AsInt32Array();
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    faces.Add(verts[indices[i]]);
                    faces.Add(verts[indices[i + 1]]);
                    faces.Add(verts[indices[i + 2]]);
                }
            }
            else
            {
                for (int i = 0; i + 2 < verts.Length; i += 3)
                {
                    faces.Add(verts[i]);
                    faces.Add(verts[i + 1]);
                    faces.Add(verts[i + 2]);
                }
            }
        }

        GD.Print($"[BakeBuilder] BuildConcaveShape: {faces.Count / 3} triángulos");
        return new ConcavePolygonShape3D { Data = faces.ToArray() };
    }

    // -------------------------------------------------------------------------
    // Occluder — built from LOD1 geometry (already simplified: flat walls,
    // no stairs/fences) so the polygon count is low and the shape is accurate.
    // The OccluderInstance3D tells Godot's occlusion system which fragments can
    // be skipped when this building is blocking the view.
    // -------------------------------------------------------------------------

    private static OccluderInstance3D BuildOccluder(ArrayMesh sourceMesh)
    {
        var allVerts   = new List<Vector3>();
        var allIndices = new List<int>();

        for (int s = 0; s < sourceMesh.GetSurfaceCount(); s++)
        {
            var arrays = sourceMesh.SurfaceGetArrays(s);
            if (arrays.Count == 0) continue;

            var verts   = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            int baseIdx = allVerts.Count;
            allVerts.AddRange(verts);

            var idxVar = arrays[(int)Mesh.ArrayType.Index];
            if (idxVar.VariantType == Variant.Type.PackedInt32Array)
            {
                foreach (int i in idxVar.AsInt32Array())
                    allIndices.Add(baseIdx + i);
            }
            else
            {
                for (int i = 0; i < verts.Length; i++)
                    allIndices.Add(baseIdx + i);
            }
        }

        var occluder = new ArrayOccluder3D();
        occluder.SetArrays(allVerts.ToArray(), allIndices.ToArray());

        return new OccluderInstance3D { Name = "Occluder", Occluder = occluder };
    }

    // -------------------------------------------------------------------------
    // Navigation mesh — baked at export time so the .tscn carries a ready-to-use
    // navmesh. Parameters match what was validated interactively:
    //   agent_radius 0.3  → door opening 1.0m − 2×0.3 = 0.4m free (passes)
    //   agent_max_slope 50 → stair ramps at 45° pass the walkability check
    // The source geometry is:
    //   • colMesh  — floors + walls with door holes (no stair steps)
    //   • ramp triangles — extracted from each staircase ConvexPolygonShape3D
    // Both are already in root-local space, so the .tscn is position-independent.
    // -------------------------------------------------------------------------

    private static NavigationRegion3D BuildNavRegion(
        ArrayMesh colMesh,
        List<CollisionShape3D> convexShapes,
        Transform3D rootInverse)
    {
        var navMesh = new NavigationMesh
        {
            AgentRadius   = 0.3f,
            AgentHeight   = 1.8f,
            AgentMaxClimb = 0.25f,
            AgentMaxSlope = 50.0f,
            CellSize      = 0.10f,
            CellHeight    = 0.10f,
            RegionMinSize = 0.0f,
        };

        var sourceGeom = new NavigationMeshSourceGeometryData3D();

        // Floors + walls with door holes (no stair steps)
        sourceGeom.AddMesh(colMesh, Transform3D.Identity);

        // One ramp quad per staircase
        foreach (var cs in convexShapes)
        {
            if (cs.Shape is not ConvexPolygonShape3D convex) continue;

            var localToRoot = rootInverse * cs.GlobalTransform;
            var transformed = new Vector3[convex.Points.Length];
            for (int p = 0; p < transformed.Length; p++)
                transformed[p] = localToRoot * convex.Points[p];

            var ramp = ExtractRampFace(transformed);
            if (ramp != null)
                sourceGeom.AddFaces(ramp, Transform3D.Identity);
        }

        NavigationMeshGenerator.BakeFromSourceGeometryData(navMesh, sourceGeom);

        GD.Print("[BakeBuilder] NavigationMesh horneado.");
        return new NavigationRegion3D { Name = "Navigation", NavigationMesh = navMesh };
    }

    // Extracts the two triangles that form the walkable inclined face of the
    // staircase ConvexPolygonShape3D (an 8-point wedge). Works for any rotation:
    // splits points into top/bottom halves by Y, then selects the two "front"
    // points from each half by projecting onto the ramp direction vector.
    private static Vector3[] ExtractRampFace(Vector3[] pts)
    {
        if (pts.Length < 4) return null;

        var byY    = pts.OrderBy(p => p.Y).ToArray();
        int half   = byY.Length / 2;
        var botPts = byY.Take(half).ToArray();
        var topPts = byY.Skip(byY.Length - half).ToArray();

        var botCentroid = Vector3.Zero;
        foreach (var p in botPts) botCentroid += p;
        botCentroid /= botPts.Length;

        var topCentroid = Vector3.Zero;
        foreach (var p in topPts) topCentroid += p;
        topCentroid /= topPts.Length;

        // Direction from floor-level centroid to top-level centroid
        var rampDir = (topCentroid - botCentroid).Normalized();

        // Width axis: perpendicular to rampDir and world-Up, gives consistent
        // left-right ordering for any staircase orientation (not just north-facing).
        var widthDir = rampDir.Cross(Vector3.Up).Normalized();

        // "Front" edge = points most advanced along rampDir from their group centroid
        var topNear = topPts.OrderByDescending(p => (p - topCentroid).Dot(rampDir))
                            .Take(2).OrderBy(p => (p - topCentroid).Dot(widthDir)).ToArray();
        var botNear = botPts.OrderByDescending(p => (p - botCentroid).Dot(rampDir))
                            .Take(2).OrderBy(p => (p - botCentroid).Dot(widthDir)).ToArray();

        if (topNear.Length < 2 || botNear.Length < 2) return null;

        // Quad as two CCW triangles (normal faces upward)
        return new[]
        {
            topNear[0], topNear[1], botNear[1],
            topNear[0], botNear[1], botNear[0],
        };
    }
}
