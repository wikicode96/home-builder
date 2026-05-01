using Godot;
using System.Collections.Generic;

public class FenceBuilder
{
    private readonly HomeBuilderPlugin _plugin;

    // Convención del asset: 1 m de ancho, pivote en el centro de la base,
    // mirando hacia +X. Cualquier asset que cumpla esto es plug-and-play.
    public const float ModuleLength = 1.0f;

    // Metadata por segmento — permite refactorizar el segmento en el futuro
    // (postes, esquinas, cambio de asset) leyendo la información original.
    public const string MetaStart     = "HB_FenceStart";
    public const string MetaEnd       = "HB_FenceEnd";
    public const string MetaAxis      = "HB_FenceAxis";
    public const string MetaAssetPath = "HB_FenceAssetPath";

    private CsgBox3D _pointMarker;
    private Vector3? _start;

    public FenceBuilder(HomeBuilderPlugin plugin) => _plugin = plugin;

    // -------------------------------------------------------------------------
    // Preview
    // -------------------------------------------------------------------------

    public void CreateMarker(Node3D scene, float floorBaseY)
    {
        _pointMarker = PreviewHelper.CreateMarker(
            scene,
            "__HB_FencePoint__",
            new Vector3(0.2f, 0.2f, 0.2f),
            new Color(0.2f, 0.7f, 0.9f, 0.9f),
            new Vector3(0f, floorBaseY, 0f)
        );
    }

    public void ClearPreview()
    {
        PreviewHelper.Free(ref _pointMarker);
        _start = null;
    }

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------

    public int HandleInput(Camera3D camera, InputEvent inputEvent, float floorBaseY)
    {
        if (inputEvent is InputEventMouseMotion motionEvent)
        {
            var pos = RaycastHelper.ToFloorPlane(camera, motionEvent.Position, floorBaseY);
            if (pos.HasValue && _pointMarker != null && GodotObject.IsInstanceValid(_pointMarker))
                _pointMarker.Position = SnapHelper.ToGridCorner(pos.Value, floorBaseY);
            return 0;
        }

        if (inputEvent is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left
            && mb.Pressed)
        {
            var pos = RaycastHelper.ToFloorPlane(camera, mb.Position, floorBaseY);
            if (!pos.HasValue) return 0;

            var corner = SnapHelper.ToGridCorner(pos.Value, floorBaseY);

            if (_start == null)
            {
                _start = corner;
            }
            else
            {
                var (projectedEnd, axis) = ProjectToAxis(_start.Value, corner);
                if (axis != Axis.None)
                    PlaceFence(_start.Value, projectedEnd, axis, floorBaseY);
                _start = null;
            }

            return 1;
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Eje dominante
    //
    // El happy-path 0.4.0 solo admite segmentos paralelos a X o Z. En vez de
    // rechazar clicks "diagonales", proyectamos el segundo click sobre el eje
    // dominante (el que tenga mayor delta). Si los dos deltas son cero
    // devolvemos Axis.None y el placement se cancela silenciosamente.
    //
    // Cuando en el futuro queramos diagonales, este método cambia a devolver
    // un vector libre y EnumerateModulePlacements se adapta.
    // -------------------------------------------------------------------------

    private enum Axis { None, X, Z }

    private static (Vector3 end, Axis axis) ProjectToAxis(Vector3 start, Vector3 end)
    {
        float dx = end.X - start.X;
        float dz = end.Z - start.Z;
        float adx = Mathf.Abs(dx);
        float adz = Mathf.Abs(dz);

        if (adx < 0.5f && adz < 0.5f) return (start, Axis.None);

        if (adx >= adz)
            return (new Vector3(end.X, start.Y, start.Z), Axis.X);
        else
            return (new Vector3(start.X, start.Y, end.Z), Axis.Z);
    }

    // -------------------------------------------------------------------------
    // Placement
    // -------------------------------------------------------------------------

    private void PlaceFence(Vector3 start, Vector3 end, Axis axis, float floorBaseY)
    {
        var assetScene = _plugin.Dock?.FenceAssetScene;
        if (assetScene == null)
        {
            GD.PushWarning("FenceBuilder: no hay asset seleccionado en el dock.");
            return;
        }

        float length = (end - start).Length();
        int   nModules = Mathf.RoundToInt(length / ModuleLength);
        if (nModules <= 0) return;

        var fenceParent = _plugin.GetOrCreateParentNode($"Fences_{_plugin.ActiveFloor}");
        if (fenceParent == null) return;

        // El segmento se posiciona en `start` y se rota para que su +X local
        // apunte a `end`. Así cada módulo se coloca en local x = (i + 0.5)
        // sin pensar en el eje global.
        var dir   = (end - start).Normalized();
        var basisX = dir;
        var basisY = Vector3.Up;
        var basisZ = basisY.Cross(basisX).Normalized();
        var basis  = new Basis(basisX, basisY, basisZ);

        var segment = new Node3D
        {
            Name     = "Fence",
            Position = new Vector3(start.X, floorBaseY, start.Z),
            Basis    = basis,
        };
        segment.SetMeta(MetaStart,     start);
        segment.SetMeta(MetaEnd,       end);
        segment.SetMeta(MetaAxis,      axis == Axis.X ? "X" : "Z");
        segment.SetMeta(MetaAssetPath, assetScene.ResourcePath);

        fenceParent.AddChild(segment);
        segment.Owner = fenceParent.Owner;

        foreach (var local in EnumerateModulePlacements(nModules))
        {
            var module = assetScene.Instantiate<Node3D>();
            module.Transform = local;
            segment.AddChild(module);
            SetOwnerRecursive(module, fenceParent.Owner);
        }

        var undo = _plugin.GetUndoRedo();
        undo.CreateAction("Place Fence");
        undo.AddDoMethod(fenceParent,   Node.MethodName.AddChild,    segment);
        undo.AddUndoMethod(fenceParent, Node.MethodName.RemoveChild, segment);
        undo.CommitAction(false);
    }

    // -------------------------------------------------------------------------
    // Enumeración de módulos
    //
    // Punto único de extensión: hoy devuelve N transforms locales alineados en
    // X. Mañana, aquí entran postes intercalados, módulos de longitud variable,
    // esquinas, etc. El resto del builder es agnóstico.
    // -------------------------------------------------------------------------

    private static IEnumerable<Transform3D> EnumerateModulePlacements(int nModules)
    {
        for (int i = 0; i < nModules; i++)
        {
            float x = (i + 0.5f) * ModuleLength;
            yield return new Transform3D(Basis.Identity, new Vector3(x, 0f, 0f));
        }
    }

    private static void SetOwnerRecursive(Node node, Node owner)
    {
        node.Owner = owner;
        foreach (Node child in node.GetChildren())
            SetOwnerRecursive(child, owner);
    }
}
