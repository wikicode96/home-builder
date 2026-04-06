using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

public static class HBMetadataIO
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // -------------------------------------------------------------------------
    // Path helpers
    // -------------------------------------------------------------------------

    // Returns the .hb.json path for a given .tscn path
    // e.g. "res://scenes/house.tscn" → "res://scenes/house.hb.json"
    public static string MetadataPathFor(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath)) return null;
        var withoutExt = scenePath.GetBaseName(); // strips last extension
        return withoutExt + ".hb.json";
    }

    // -------------------------------------------------------------------------
    // Load
    // -------------------------------------------------------------------------

    public static HBSceneData Load(string scenePath)
    {
        var metaPath = MetadataPathFor(scenePath);
        if (metaPath == null) return new HBSceneData();

        var absPath = ProjectSettings.GlobalizePath(metaPath);
        if (!System.IO.File.Exists(absPath)) return new HBSceneData();

        try
        {
            var json = System.IO.File.ReadAllText(absPath);
            var data = JsonSerializer.Deserialize<HBSceneData>(json, _options)
                       ?? new HBSceneData();

            return HBMetadataMigrator.Migrate(data);
        }
        catch (System.Exception e)
        {
            GD.PushError($"[HomeBuilder] Failed to load metadata from {metaPath}: {e.Message}");
            return new HBSceneData();
        }
    }

    // -------------------------------------------------------------------------
    // Save
    // -------------------------------------------------------------------------

    public static void Save(HBSceneData data, string scenePath)
    {
        var metaPath = MetadataPathFor(scenePath);
        if (metaPath == null) return;

        var absPath = ProjectSettings.GlobalizePath(metaPath);

        try
        {
            data.Version = HBMetadataVersion.Current;
            var json = JsonSerializer.Serialize(data, _options);
            System.IO.File.WriteAllText(absPath, json);
        }
        catch (System.Exception e)
        {
            GD.PushError($"[HomeBuilder] Failed to save metadata to {metaPath}: {e.Message}");
        }
    }
}
