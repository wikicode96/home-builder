using System.Collections.Generic;
using Godot;

// Version of the metadata format — bump when the schema changes
// and add a migration in HBMetadataMigrator
public static class HBMetadataVersion
{
    public const int Current = 1;
}

// ── Tile ─────────────────────────────────────────────────────────────────────

public class HBTileData
{
    public int X { get; set; }
    public int Z { get; set; }

    // Reserved for future per-tile material overrides
    // public string MaterialTop    { get; set; }
    // public string MaterialBottom { get; set; }
    // public string MaterialSide   { get; set; }
}

// ── Floor ─────────────────────────────────────────────────────────────────────

public class HBFloorData
{
    public List<HBTileData> Tiles { get; set; } = new();
}

// ── Scene root ────────────────────────────────────────────────────────────────

public class HBSceneData
{
    public int Version { get; set; } = HBMetadataVersion.Current;

    // Keyed by floor number (1, 2, 3...)
    public Dictionary<int, HBFloorData> Floors { get; set; } = new();

    // Walls, stairs, etc. will be added here in future steps
    // public Dictionary<int, List<HBWallData>>  Walls  { get; set; } = new();
    // public Dictionary<int, List<HBStairData>> Stairs { get; set; } = new();

    public HBFloorData GetOrCreateFloor(int floorIndex)
    {
        if (!Floors.TryGetValue(floorIndex, out var floor))
        {
            floor = new HBFloorData();
            Floors[floorIndex] = floor;
        }
        return floor;
    }
}
