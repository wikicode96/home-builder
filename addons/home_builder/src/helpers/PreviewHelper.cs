using Godot;

public static class PreviewHelper
{
    public static CsgBox3D CreateMarker(Node3D scene, string name, Vector3 size, Color color, Vector3 position)
    {
        if (scene == null) return null;

        var marker = new CsgBox3D
        {
            Name             = name,
            Size             = size,
            Position         = position,
            MaterialOverride = MakeMaterial(color),
        };
        scene.AddChild(marker);
        return marker;
    }

    public static void Free(ref CsgBox3D marker)
    {
        if (marker != null && GodotObject.IsInstanceValid(marker))
        {
            marker.Free();
            marker = null;
        }
    }

    public static StandardMaterial3D MakeMaterial(Color color) => new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor  = color,
        ShadingMode  = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode     = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
