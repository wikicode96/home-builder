using Godot;

public static class RaycastHelper
{
    public struct HitInfo
    {
        public Vector3      Position;
        public StaticBody3D Collider;
    }

    // -------------------------------------------------------------------------
    // Floor plane intersection (unchanged)
    // -------------------------------------------------------------------------

    public static Vector3? ToFloorPlane(Camera3D camera, Vector2 screenPos, float floorBaseY)
    {
        var origin    = camera.ProjectRayOrigin(screenPos);
        var direction = camera.ProjectRayNormal(screenPos);

        if (Mathf.IsZeroApprox(direction.Y)) return null;

        float t = -(origin.Y - floorBaseY) / direction.Y;
        if (t < 0) return null;

        return origin + direction * t;
    }

    // -------------------------------------------------------------------------
    // Wall intersection — geometric OBB test, no physics engine required.
    //
    // This works reliably in the Godot editor where DirectSpaceState may not
    // reflect freshly-modified collision shapes (e.g. after replacing
    // BoxShape3D with ConcavePolygonShape3D on the first opening).
    //
    // Each wall StaticBody3D is an OBB of size (length × Height × Thickness).
    // We transform the ray into the body's local space and do a standard
    // ray-vs-AABB slab test there, then transform the hit back to world space.
    // -------------------------------------------------------------------------

    public static HitInfo? ToWalls(Camera3D camera, Vector2 screenPos, Node3D wallParent)
    {
        if (wallParent == null) return null;

        var origin    = camera.ProjectRayOrigin(screenPos);
        var direction = camera.ProjectRayNormal(screenPos);
        const float maxDist = 1000f;

        float   bestT    = float.MaxValue;
        HitInfo bestHit  = default;
        bool    anyHit   = false;

        foreach (Node child in wallParent.GetChildren())
        {
            if (child is not StaticBody3D body) continue;

            // Wall half-extents in local space
            float halfLen = GetWallHalfLength(body);
            if (halfLen <= 0f) continue;

            float halfH = WallBuilder.Height    * 0.5f;
            float halfT = WallBuilder.Thickness * 0.5f;

            // Transform ray to body local space
            var invTransform = body.GlobalTransform.AffineInverse();
            var localOrigin  = invTransform * origin;
            var localDir     = invTransform.Basis * direction;  // direction only, no translation

            // Slab test against [-halfLen..halfLen, -halfH..halfH, -halfT..halfT]
            float tMin, tMax;
            if (!SlabTest(localOrigin, localDir,
                    -halfLen, halfLen,
                    -halfH,   halfH,
                    -halfT,   halfT,
                    out tMin, out tMax))
                continue;

            if (tMin < 0f) tMin = tMax;   // ray starts inside — use exit point
            if (tMin < 0f || tMin > maxDist) continue;

            if (tMin < bestT)
            {
                bestT = tMin;
                var worldHit = origin + direction * tMin;
                bestHit = new HitInfo { Position = worldHit, Collider = body };
                anyHit = true;
            }
        }

        return anyHit ? bestHit : null;
    }

    // -------------------------------------------------------------------------
    // Ray vs AABB slab test in local space.
    // Returns true if the ray intersects the box; tMin/tMax are entry/exit t.
    // -------------------------------------------------------------------------

    private static bool SlabTest(
        Vector3 origin, Vector3 dir,
        float minX, float maxX,
        float minY, float maxY,
        float minZ, float maxZ,
        out float tMin, out float tMax)
    {
        tMin = float.MinValue;
        tMax = float.MaxValue;

        // X slab
        if (!SlabAxis(origin.X, dir.X, minX, maxX, ref tMin, ref tMax)) return false;
        // Y slab
        if (!SlabAxis(origin.Y, dir.Y, minY, maxY, ref tMin, ref tMax)) return false;
        // Z slab
        if (!SlabAxis(origin.Z, dir.Z, minZ, maxZ, ref tMin, ref tMax)) return false;

        return tMax >= tMin;
    }

    private static bool SlabAxis(
        float orig, float dir,
        float slabMin, float slabMax,
        ref float tMin, ref float tMax)
    {
        if (Mathf.IsZeroApprox(dir))
        {
            // Ray is parallel to slab — check if origin is inside
            if (orig < slabMin || orig > slabMax) return false;
        }
        else
        {
            float t1 = (slabMin - orig) / dir;
            float t2 = (slabMax - orig) / dir;
            if (t1 > t2) (t1, t2) = (t2, t1);   // swap
            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMax < tMin) return false;
        }
        return true;
    }

    // -------------------------------------------------------------------------
    // Read wall half-length from metadata (written by WallBuilder) or
    // fall back to BoxShape3D for walls created before this update.
    // -------------------------------------------------------------------------

    private static float GetWallHalfLength(StaticBody3D body)
    {
        if (body.HasMeta(OpeningBuilder.MetaWallLength))
            return body.GetMeta(OpeningBuilder.MetaWallLength).AsSingle() * 0.5f;

        foreach (Node child in body.GetChildren())
        {
            if (child is CollisionShape3D cs && cs.Shape is BoxShape3D box)
                return box.Size.X * 0.5f;
        }
        return 0f;
    }
}
