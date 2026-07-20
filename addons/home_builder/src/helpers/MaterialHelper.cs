#if TOOLS
using Godot;

public static class MaterialHelper
{
    // Sin caché estática: los campos static que retienen objetos Godot impiden
    // descargar el assembly al recompilar C# (godotengine/godot#78513). Crear
    // el material en cada colocación es barato para una herramienta de editor.
    public static StandardMaterial3D MakeDefaultMaterial(Color color)
    {
        return new StandardMaterial3D { AlbedoColor = color };
    }
}
#endif
