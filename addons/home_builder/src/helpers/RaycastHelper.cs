using Godot;

public static class RaycastHelper
{
    public struct HitInfo
    {
        public Vector3      Position;
        public StaticBody3D Collider;
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

    // Raycast against StaticBody3D children of wallParent (ArrayMesh walls)
    public static HitInfo? ToWalls(Camera3D camera, Vector2 screenPos, Node3D wallParent)
    {
        if (wallParent == null) return null;

        var origin    = camera.ProjectRayOrigin(screenPos);
        var direction = camera.ProjectRayNormal(screenPos);

        var space = camera.GetWorld3D().DirectSpaceState;
        if (space == null) return null;

        var query = new PhysicsRayQueryParameters3D
        {
            From             = origin,
            To               = origin + direction * 1000f,
            CollisionMask    = 1, // Layer 1 for walls
            Exclude          = new Godot.Collections.Array<Rid>(),
            HitFromInside    = true,
        };

        var result = space.IntersectRay(query);
        if (result.Count == 0) return null;

        var collider = result["collider"].As<Node3D>();
        if (collider == null) return null;

        // Find the StaticBody3D parent (the wall root)
        StaticBody3D wallBody = collider as StaticBody3D;
        if (wallBody == null)
        {
            // If collider is a child (CollisionShape3D), get the parent StaticBody3D
            wallBody = collider.GetParent() as StaticBody3D;
        }

        if (wallBody == null) return null;

        // Verify it's a child of wallParent
        if (wallBody.GetParent() != wallParent) return null;

        var position = result["position"].AsVector3();
        return new HitInfo { Position = position, Collider = wallBody };
    }
}
