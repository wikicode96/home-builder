using Godot;

public class FloorBuilder
{
    private readonly HomeBuilderPlugin _plugin;

    public static float SlabThickness { get; set; } = 0.1f;

    private CsgBox3D _ghost;
    private Vector3? _dragStart;

    public FloorBuilder(HomeBuilderPlugin plugin) => _plugin = plugin;

    // -------------------------------------------------------------------------
    // Preview (still CsgBox3D — previews don't need materials)
    // -------------------------------------------------------------------------

    public void CreateGhost(Node3D scene, float floorBaseY)
    {
        float ht = SlabThickness;
        _ghost = PreviewHelper.CreateMarker(
            scene,
            "__HB_GhostFloor__",
            new Vector3(1f, ht, 1f),
            new Color(0.2f, 0.9f, 0.3f, 0.4f),
            new Vector3(0f, floorBaseY - ht * 0.5f, 0f)
        );
    }

    public void ClearPreview()
    {
        PreviewHelper.Free(ref _ghost);
        _dragStart = null;
    }

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------

    public int HandleInput(Camera3D camera, InputEvent inputEvent, float floorBaseY)
    {
        if (inputEvent is InputEventMouseMotion motionEvent)
        {
            var pos = RaycastHelper.ToFloorPlane(camera, motionEvent.Position, floorBaseY);
            if (!pos.HasValue) return 0;

            var cell = SnapHelper.ToTileCenter(pos.Value, floorBaseY);

            if (_dragStart.HasValue)
            {
                UpdateGhostRect(_dragStart.Value, cell, floorBaseY);
            }
            else if (_ghost != null && GodotObject.IsInstanceValid(_ghost))
            {
                _ghost.Size     = new Vector3(1f, SlabThickness, 1f);
                _ghost.Position = cell;
            }

            return 0;
        }

        if (inputEvent is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            var pos = RaycastHelper.ToFloorPlane(camera, mb.Position, floorBaseY);
            if (!pos.HasValue) return 0;

            if (mb.Pressed)
            {
                _dragStart = SnapHelper.ToTileCenter(pos.Value, floorBaseY);
                return 1;
            }
            else
            {
                if (_dragStart.HasValue)
                {
                    var endCell = SnapHelper.ToTileCenter(pos.Value, floorBaseY);
                    FillFloorRect(_dragStart.Value, endCell, floorBaseY, _plugin.ActiveFloor);
                    _dragStart = null;

                    if (_ghost != null && GodotObject.IsInstanceValid(_ghost))
                        _ghost.Size = new Vector3(1f, SlabThickness, 1f);
                }
                return 1;
            }
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Ghost rect resize
    // -------------------------------------------------------------------------

    private void UpdateGhostRect(Vector3 a, Vector3 b, float floorBaseY)
    {
        if (_ghost == null || !GodotObject.IsInstanceValid(_ghost)) return;

        var (minX, maxX, minZ, maxZ) = SnapHelper.GridBounds(a, b);
        int cols = maxX - minX + 1;
        int rows = maxZ - minZ + 1;

        float ht = SlabThickness;
        _ghost.Size     = new Vector3(cols, ht, rows);
        _ghost.Position = new Vector3(minX + cols * 0.5f, floorBaseY - ht * 0.5f, minZ + rows * 0.5f);
    }

    // -------------------------------------------------------------------------
    // Placement — MeshInstance3D with 3 surfaces
    // -------------------------------------------------------------------------

    private void FillFloorRect(Vector3 a, Vector3 b, float floorBaseY, int activeFloor)
    {
        var (minX, maxX, minZ, maxZ) = SnapHelper.GridBounds(a, b);
        int cols = maxX - minX + 1;
        int rows = maxZ - minZ + 1;

        var floorParent = _plugin.GetOrCreateParentNode($"Floor_{activeFloor}");
        if (floorParent == null) return;

        // One mesh for the entire rectangle — a 10x10 room is now a single
        // instance instead of 100 tiles.
        var slabMesh = FloorMeshBuilder.Build(cols, rows);

        float ht       = SlabThickness;
        float zFighting = 0.001f;

        var body = new StaticBody3D
        {
            Name     = "FloorSlab",
            Position = new Vector3(minX + cols * 0.5f, floorBaseY - ht * 0.5f + zFighting, minZ + rows * 0.5f),
        };
        body.SetMeta("HB_FloorRect", new Vector4(minX, minZ, cols, rows));

        var tile = new MeshInstance3D { Mesh = slabMesh };
        var dock = _plugin.Dock;
        tile.SetSurfaceOverrideMaterial(FloorMeshBuilder.SurfaceTop,
            dock?.TileTopMaterial    ?? MaterialHelper.MakeDefaultMaterial(new Color(0.8f, 0.7f, 0.5f)));
        tile.SetSurfaceOverrideMaterial(FloorMeshBuilder.SurfaceBottom,
            dock?.TileBottomMaterial ?? MaterialHelper.MakeDefaultMaterial(new Color(0.6f, 0.6f, 0.6f)));
        tile.SetSurfaceOverrideMaterial(FloorMeshBuilder.SurfaceSides,
            dock?.TileSidesMaterial  ?? MaterialHelper.MakeDefaultMaterial(new Color(0.5f, 0.5f, 0.5f)));

        var shape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(cols, ht, rows) }
        };

        floorParent.AddChild(body);
        body.Owner = floorParent.Owner;

        body.AddChild(tile);
        tile.Owner = floorParent.Owner;

        body.AddChild(shape);
        shape.Owner = floorParent.Owner;

        var undo = _plugin.GetUndoRedo();
        undo.CreateAction("Fill Floor Rect");
        undo.AddDoMethod(floorParent,   Node.MethodName.AddChild,    body);
        undo.AddUndoMethod(floorParent, Node.MethodName.RemoveChild, body);
        undo.CommitAction(false);
    }

}
