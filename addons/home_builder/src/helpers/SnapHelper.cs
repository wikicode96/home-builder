using Godot;

public static class SnapHelper
{
    public static Vector3 ToTileCenter(Vector3 hit, float floorBaseY) =>
        new(Mathf.Floor(hit.X) + 0.5f, floorBaseY - 0.05f, Mathf.Floor(hit.Z) + 0.5f);

    public static Vector3 ToGridCorner(Vector3 hit, float floorBaseY) =>
        new(Mathf.Round(hit.X), floorBaseY, Mathf.Round(hit.Z));

    public static float ToWall(StaticBody3D wallBody, Vector3 worldHit, float openingWidth)
    {
        var localHit = wallBody.GlobalTransform.AffineInverse() * worldHit;
        
        // Get wall length from CollisionShape3D
        float wallLen = 0f;
        foreach (Node child in wallBody.GetChildren())
        {
            if (child is CollisionShape3D shape && shape.Shape is BoxShape3D boxShape)
            {
                wallLen = boxShape.Size.X;
                break;
            }
        }
        
        if (wallLen == 0f) return localHit.X;
        
        float halfLen = wallLen * 0.5f;
        float snapped = Mathf.Round(localHit.X);
        return Mathf.Clamp(snapped, -halfLen + openingWidth * 0.5f, halfLen - openingWidth * 0.5f);
    }

    public static (int minX, int maxX, int minZ, int maxZ) GridBounds(Vector3 a, Vector3 b)
    {
        int x0 = Mathf.RoundToInt(a.X - 0.5f);
        int z0 = Mathf.RoundToInt(a.Z - 0.5f);
        int x1 = Mathf.RoundToInt(b.X - 0.5f);
        int z1 = Mathf.RoundToInt(b.Z - 0.5f);

        return (
            Mathf.Min(x0, x1),
            Mathf.Max(x0, x1),
            Mathf.Min(z0, z1),
            Mathf.Max(z0, z1)
        );
    }
}
