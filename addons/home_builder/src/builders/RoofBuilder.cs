using Godot;

public class RoofBuilder
{
    private readonly HomeBuilderPlugin _plugin;

    private CsgBox3D _ghost;
    private Vector3? _dragStart;

    public RoofBuilder(HomeBuilderPlugin plugin) => _plugin = plugin;

    // Roof base sits on top of the active floor's walls.
    private static float RoofBaseY(float floorBaseY) => floorBaseY + WallBuilder.Height;

    public void CreateGhost(Node3D scene, float floorBaseY)
    {
        var baseY = RoofBaseY(floorBaseY);
        _ghost = PreviewHelper.CreateMarker(
            scene,
            "__HB_GhostRoof__",
            new Vector3(0.5f, 0.1f, 0.5f),
            new Color(0.2f, 0.5f, 0.9f, 0.4f),
            new Vector3(0f, baseY + 0.05f, 0f)
        );
    }

    public void ClearPreview()
    {
        PreviewHelper.Free(ref _ghost);
        _dragStart = null;
    }

    public int HandleInput(Camera3D camera, InputEvent inputEvent, float floorBaseY)
    {
        var baseY = RoofBaseY(floorBaseY);

        if (inputEvent is InputEventMouseMotion motion)
        {
            var pos = RaycastHelper.ToFloorPlane(camera, motion.Position, baseY);
            if (!pos.HasValue) return 0;

            var cell = SnapHelper.ToHalfTileCenter(pos.Value, baseY);
            cell.Y = baseY + 0.05f;

            if (_dragStart.HasValue)
            {
                UpdateGhostRect(_dragStart.Value, cell, baseY);
            }
            else if (_ghost != null && GodotObject.IsInstanceValid(_ghost))
            {
                _ghost.Size     = new Vector3(0.5f, 0.1f, 0.5f);
                _ghost.Position = cell;
            }
            return 0;
        }

        if (inputEvent is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            var pos = RaycastHelper.ToFloorPlane(camera, mb.Position, baseY);
            if (!pos.HasValue) return 0;

            if (mb.Pressed)
            {
                _dragStart = SnapHelper.ToHalfTileCenter(pos.Value, baseY);
                return 1;
            }

            if (_dragStart.HasValue)
            {
                var endCell = SnapHelper.ToHalfTileCenter(pos.Value, baseY);
                PlaceRoof(_dragStart.Value, endCell, baseY, _plugin.ActiveFloor);
                _dragStart = null;

                if (_ghost != null && GodotObject.IsInstanceValid(_ghost))
                    _ghost.Size = new Vector3(0.5f, 0.1f, 0.5f);
            }
            return 1;
        }

        return 0;
    }

    private void UpdateGhostRect(Vector3 a, Vector3 b, float baseY)
    {
        if (_ghost == null || !GodotObject.IsInstanceValid(_ghost)) return;

        var (minX, maxX, minZ, maxZ) = SnapHelper.HalfGridBounds(a, b);
        int cols = maxX - minX + 1;
        int rows = maxZ - minZ + 1;
        float w = cols * 0.5f;
        float d = rows * 0.5f;

        _ghost.Size     = new Vector3(w, 0.1f, d);
        _ghost.Position = new Vector3(minX * 0.5f + w * 0.5f, baseY + 0.05f, minZ * 0.5f + d * 0.5f);
    }

    private void PlaceRoof(Vector3 a, Vector3 b, float baseY, int activeFloor)
    {
        var (minX, maxX, minZ, maxZ) = SnapHelper.HalfGridBounds(a, b);
        float w = (maxX - minX + 1) * 0.5f;
        float d = (maxZ - minZ + 1) * 0.5f;

        var dock  = _plugin.Dock;
        var type  = dock?.SelectedRoofType      ?? RoofType.Flat;
        var dir   = dock?.SelectedRoofDirection ?? RoofDirection.North;
        var pitch = dock?.RoofPitch              ?? 1.5f;

        var mesh = RoofMeshBuilder.Build(type, w, d, pitch, dir);

        var roofParent = _plugin.GetOrCreateParentNode($"Roof_{activeFloor}");
        if (roofParent == null) return;

        var undo = _plugin.GetUndoRedo();
        undo.CreateAction("Place Roof");

        var body = new StaticBody3D
        {
            Name     = "Roof",
            Position = new Vector3(minX * 0.5f, baseY, minZ * 0.5f),
        };

        var inst = new MeshInstance3D { Mesh = mesh };
        inst.SetSurfaceOverrideMaterial(RoofMeshBuilder.SurfaceTop,
            dock?.RoofTopMaterial    ?? MaterialHelper.MakeDefaultMaterial(new Color(0.65f, 0.25f, 0.2f)));
        inst.SetSurfaceOverrideMaterial(RoofMeshBuilder.SurfaceBottom,
            dock?.RoofBottomMaterial ?? MaterialHelper.MakeDefaultMaterial(new Color(0.5f, 0.5f, 0.5f)));
        inst.SetSurfaceOverrideMaterial(RoofMeshBuilder.SurfaceSides,
            dock?.RoofSidesMaterial  ?? MaterialHelper.MakeDefaultMaterial(new Color(0.85f, 0.82f, 0.75f)));

        var shape = new CollisionShape3D { Shape = mesh.CreateTrimeshShape() };

        roofParent.AddChild(body);
        body.Owner = roofParent.Owner;
        body.AddChild(inst);
        inst.Owner = roofParent.Owner;
        body.AddChild(shape);
        shape.Owner = roofParent.Owner;

        undo.AddDoMethod(roofParent,   Node.MethodName.AddChild,    body);
        undo.AddUndoMethod(roofParent, Node.MethodName.RemoveChild, body);
        undo.CommitAction(false);
    }
}
