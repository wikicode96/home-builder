using Godot;

public static class WallHelper
{
    // Metadata key written by WallBuilder when the wall is first created.
    // We read it here because after the first opening the CollisionShape3D
    // becomes a ConcavePolygonShape3D and BoxShape3D is no longer available.
    public const string MetaWallLength = "hb_wall_length";

    public static float GetWallLength(StaticBody3D wallBody)
    {
        // Preferred: stored metadata (survives collision shape replacement)
        if (wallBody.HasMeta(MetaWallLength))
            return wallBody.GetMeta(MetaWallLength).AsSingle();

        // Fallback: read from BoxShape3D (only valid before the first opening)
        foreach (Node child in wallBody.GetChildren())
        {
            if (child is CollisionShape3D shape && shape.Shape is BoxShape3D box)
                return box.Size.X;
        }
        return 0f;
    }

    public static float GetWallHalfLength(StaticBody3D body)
    {
        return GetWallLength(body) * 0.5f;
    }
}
