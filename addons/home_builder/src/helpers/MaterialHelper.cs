using Godot;

public static class MaterialHelper
{
    public static StandardMaterial3D MakeDefaultMaterial(Color color) => new()
    {
        AlbedoColor = color,
    };
}
