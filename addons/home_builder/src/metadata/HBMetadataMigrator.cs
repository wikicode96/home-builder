using Godot;

public static class HBMetadataMigrator
{
    public static HBSceneData Migrate(HBSceneData data)
    {
        // Nothing to migrate yet — version 1 is the baseline.
        // Future example:
        // if (data.Version < 2) MigrateV1ToV2(data);
        // if (data.Version < 3) MigrateV2ToV3(data);

        if (data.Version > HBMetadataVersion.Current)
            GD.PushWarning($"[HomeBuilder] Metadata version {data.Version} is newer than plugin version {HBMetadataVersion.Current}. Some data may be ignored.");

        return data;
    }
}
