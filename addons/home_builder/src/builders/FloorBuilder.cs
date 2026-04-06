using Godot;

public class FloorBuilder
{
    private readonly HomeBuilderPlugin _plugin;

    private CsgBox3D _ghost;
    private Vector3? _dragStart;

    public FloorBuilder(HomeBuilderPlugin plugin) => _plugin = plugin;

    // -------------------------------------------------------------------------
    // Preview (still CsgBox3D — previews don't need materials)
    // -------------------------------------------------------------------------

    public void CreateGhost(Node3D scene, float floorBaseY)
    {
        _ghost = PreviewHelper.CreateMarker(
            scene,
            "__HB_GhostFloor__",
            new Vector3(1f, 0.1f, 1f),
            new Color(0.2f, 0.9f, 0.3f, 0.4f),
            new Vector3(0f, floorBaseY - 0.05f, 0f)
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
                _ghost.Size     = new Vector3(1f, 0.1f, 1f);
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
                        _ghost.Size = new Vector3(1f, 0.1f, 1f);
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

        _ghost.Size     = new Vector3(cols, 0.1f, rows);
        _ghost.Position = new Vector3(minX + cols * 0.5f, floorBaseY - 0.05f, minZ + rows * 0.5f);
    }

    // -------------------------------------------------------------------------
    // Placement — MeshInstance3D with 3 surfaces
    // -------------------------------------------------------------------------

    private void FillFloorRect(Vector3 a, Vector3 b, float floorBaseY, int activeFloor)
    {
        var (minX, maxX, minZ, maxZ) = SnapHelper.GridBounds(a, b);

        var floorParent = _plugin.GetOrCreateParentNode($"Floor_{activeFloor}");
        if (floorParent == null) return;

        // Build the shared mesh once — all tiles in this rect share the same mesh
        var tileMesh = FloorMeshBuilder.Build();

        var undo = _plugin.GetUndoRedo();
        undo.CreateAction("Fill Floor Rect");

        // Collect occupied cells to avoid duplicates
        var occupied = new System.Collections.Generic.HashSet<(int, int)>();
        foreach (Node child in floorParent.GetChildren())
        {
            if (child is Node3D n)
                occupied.Add((Mathf.RoundToInt(n.Position.X - 0.5f),
                              Mathf.RoundToInt(n.Position.Z - 0.5f)));
        }

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                if (occupied.Contains((x, z))) continue;

                var tile = new MeshInstance3D
                {
                    Name     = "FloorTile",
                    Mesh     = tileMesh,
                    Position = new Vector3(x + 0.5f, floorBaseY - 0.05f, z + 0.5f),
                };

                // Assign a default material per surface so the user can override later
                tile.SetSurfaceOverrideMaterial(FloorMeshBuilder.SurfaceTop,    MakeDefaultMaterial(new Color(0.8f, 0.7f, 0.5f)));
                tile.SetSurfaceOverrideMaterial(FloorMeshBuilder.SurfaceBottom, MakeDefaultMaterial(new Color(0.6f, 0.6f, 0.6f)));
                tile.SetSurfaceOverrideMaterial(FloorMeshBuilder.SurfaceSides,  MakeDefaultMaterial(new Color(0.5f, 0.5f, 0.5f)));

                floorParent.AddChild(tile);
                tile.Owner = floorParent.Owner;

                undo.AddDoMethod(floorParent,   Node.MethodName.AddChild,    tile);
                undo.AddUndoMethod(floorParent, Node.MethodName.RemoveChild, tile);
            }
        }

        // Save tile data to metadata
        SaveTiles(minX, maxX, minZ, maxZ, activeFloor, occupied);

        undo.CommitAction(false);
    }

    // -------------------------------------------------------------------------
    // Metadata
    // -------------------------------------------------------------------------

    private void SaveTiles(int minX, int maxX, int minZ, int maxZ,
                           int activeFloor,
                           System.Collections.Generic.HashSet<(int, int)> occupied)
    {
        var scenePath = _plugin.GetEditorInterface().GetEditedSceneRoot()?.SceneFilePath;
        if (string.IsNullOrEmpty(scenePath)) return;

        var data  = HBMetadataIO.Load(scenePath);
        var floor = data.GetOrCreateFloor(activeFloor);

        for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
            {
                if (occupied.Contains((x, z))) continue;
                floor.Tiles.Add(new HBTileData { X = x, Z = z });
            }

        HBMetadataIO.Save(data, scenePath);
    }

    // -------------------------------------------------------------------------
    // Material helper
    // -------------------------------------------------------------------------

    private static StandardMaterial3D MakeDefaultMaterial(Color color) => new()
    {
        AlbedoColor = color,
    };
}
