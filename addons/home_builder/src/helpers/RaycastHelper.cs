using Godot;

public static class RaycastHelper
{
    public struct HitInfo
    {
        public Vector3  Position;
        public CsgBox3D Collider;
    }

    public static Vector3? ToFloorPlane(Camera3D camera, Vector2 screenPos, float floorBaseY)
    {
        var origin    = camera.ProjectRayOrigin(screenPos);
        var direction = camera.ProjectRayNormal(screenPos);

        if (Mathf.IsZeroApprox(direction.Y)) return null;

        float t = -(origin.Y - floorBaseY) / direction.Y;
        if (t < 0) return null;

        return origin + direction * t;
    }

    // OBB slab test against every CsgBox3D child of wallParent
    public static HitInfo? ToWalls(Camera3D camera, Vector2 screenPos, Node3D wallParent)
    {
        if (wallParent == null) return null;

        var origin    = camera.ProjectRayOrigin(screenPos);
        var direction = camera.ProjectRayNormal(screenPos);

        CsgBox3D bestBox   = null;
        Vector3  bestPoint = Vector3.Zero;
        float    bestDist  = float.MaxValue;

        foreach (Node child in wallParent.GetChildren())
        {
            if (child is not CsgBox3D box) continue;

            var invTransform = box.GlobalTransform.AffineInverse();
            var localOrigin  = invTransform * origin;
            var localDir     = invTransform.Basis * direction;
            var half         = box.Size * 0.5f;

            float tMin = float.NegativeInfinity;
            float tMax = float.PositiveInfinity;

            for (int axis = 0; axis < 3; axis++)
            {
                float o = localOrigin[axis];
                float d = localDir[axis];
                float h = half[axis];

                if (Mathf.IsZeroApprox(d))
                {
                    if (o < -h || o > h) { tMin = float.PositiveInfinity; break; }
                }
                else
                {
                    float t1 = (-h - o) / d;
                    float t2 = ( h - o) / d;
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    tMin = Mathf.Max(tMin, t1);
                    tMax = Mathf.Min(tMax, t2);
                    if (tMin > tMax) { tMin = float.PositiveInfinity; break; }
                }
            }

            if (tMin < 0 || tMin == float.PositiveInfinity || tMin >= bestDist) continue;

            bestDist  = tMin;
            bestBox   = box;
            bestPoint = origin + direction * tMin;
        }

        if (bestBox == null) return null;
        return new HitInfo { Position = bestPoint, Collider = bestBox };
    }
}
