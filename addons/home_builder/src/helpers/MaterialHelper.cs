using Godot;
using System.Collections.Generic;

public static class MaterialHelper
{
    private static readonly Dictionary<Color, StandardMaterial3D> _defaultCache = new();

    public static StandardMaterial3D MakeDefaultMaterial(Color color)
    {
        if (_defaultCache.TryGetValue(color, out var cached) &&
            GodotObject.IsInstanceValid(cached))
            return cached;

        var mat = new StandardMaterial3D { AlbedoColor = color };
        _defaultCache[color] = mat;
        return mat;
    }
}
