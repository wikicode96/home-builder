using Godot;

public enum BuildMode
{
    None,
    Floor,
    Walls,
    Ceiling,
    Doors,
    Windows,
    Stairs
}

[Tool]
public partial class HomeBuilderPlugin : EditorPlugin
{
    private const float WallHeight    = 3.0f;
    private const float WallThickness = 0.3f;

    private Control _dock;
    private BuildMode _activeMode = BuildMode.None;

    // Floor ghost
    private CsgBox3D _ghostTile;
    private Vector3? _floorDragStart;

    // Wall state
    private CsgBox3D _wallPointMarker;
    private Vector3? _wallStart;

    public override void _EnterTree()
    {
        var dockScene = GD.Load<PackedScene>("res://addons/home_builder/HomeBuilderDock.tscn");
        _dock = dockScene.Instantiate<Control>();
        AddControlToDock(DockSlot.LeftUl, _dock);

        _dock.Connect(
            HomeBuilderDock.SignalName.ModeChanged,
            Callable.From((string mode) =>
            {
                ClearAllPreviews();
                _activeMode = mode switch
                {
                    "floor" => BuildMode.Floor,
                    "walls" => BuildMode.Walls,
                    "ceiling" => BuildMode.Ceiling,
                    "doors" => BuildMode.Doors,
                    "windows" => BuildMode.Windows,
                    "stairs" => BuildMode.Stairs,
                    _ => BuildMode.None
                };
                if (_activeMode == BuildMode.Floor) CreateGhostTile();
                if (_activeMode == BuildMode.Walls) CreateWallPointMarker();
            })
        );
    }

    public override void _ExitTree()
    {
        ClearAllPreviews();
        if (_dock != null)
        {
            RemoveControlFromDocks(_dock);
            _dock.QueueFree();
            _dock = null;
        }
        _activeMode = BuildMode.None;
    }

    public override bool _Handles(GodotObject obj) => true;

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------

    public override int _Forward3DGuiInput(Camera3D camera, InputEvent inputEvent)
    {
        return _activeMode switch
        {
            BuildMode.Floor => HandleFloorInput(camera, inputEvent),
            BuildMode.Walls => HandleWallInput(camera, inputEvent),
            _               => (int)AfterGuiInput.Pass,
        };
    }

    // ── Floor ─────────────────────────────────────────────────────────────────

    private int HandleFloorInput(Camera3D camera, InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseMotion motionEvent)
        {
            var pos = RaycastToFloorPlane(camera, motionEvent.Position);
            if (!pos.HasValue) return (int)AfterGuiInput.Pass;

            var cell = SnapToTileCenter(pos.Value);

            if (_floorDragStart.HasValue)
            {
                // Dragging: resize ghost to cover the whole rectangle
                UpdateGhostRect(_floorDragStart.Value, cell);
            }
            else
            {
                // Hovering: single tile ghost
                if (_ghostTile != null && IsInstanceValid(_ghostTile))
                {
                    _ghostTile.Size     = new Vector3(1f, 0.1f, 1f);
                    _ghostTile.Position = cell;
                }
            }
            return (int)AfterGuiInput.Pass;
        }

        if (inputEvent is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            var pos = RaycastToFloorPlane(camera, mb.Position);
            if (!pos.HasValue) return (int)AfterGuiInput.Pass;

            if (mb.Pressed)
            {
                // Mouse down: start drag
                _floorDragStart = SnapToTileCenter(pos.Value);
                return (int)AfterGuiInput.Stop;
            }
            else
            {
                // Mouse up: fill rectangle
                if (_floorDragStart.HasValue)
                {
                    var endCell = SnapToTileCenter(pos.Value);
                    FillFloorRect(_floorDragStart.Value, endCell);
                    _floorDragStart = null;

                    // Reset ghost to single tile
                    if (_ghostTile != null && IsInstanceValid(_ghostTile))
                        _ghostTile.Size = new Vector3(1f, 0.1f, 1f);
                }
                return (int)AfterGuiInput.Stop;
            }
        }

        return (int)AfterGuiInput.Pass;
    }

    // ── Walls ─────────────────────────────────────────────────────────────────

    private int HandleWallInput(Camera3D camera, InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseMotion motionEvent)
        {
            var pos = RaycastToFloorPlane(camera, motionEvent.Position);
            if (pos.HasValue && _wallPointMarker != null && IsInstanceValid(_wallPointMarker))
                _wallPointMarker.Position = SnapToGridCorner(pos.Value);
            return (int)AfterGuiInput.Pass;
        }

        if (inputEvent is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left
            && mb.Pressed)
        {
            var pos = RaycastToFloorPlane(camera, mb.Position);
            if (!pos.HasValue) return (int)AfterGuiInput.Pass;

            var corner = SnapToGridCorner(pos.Value);

            if (_wallStart == null)
            {
                _wallStart = corner;
            }
            else
            {
                if (!_wallStart.Value.IsEqualApprox(corner))
                    PlaceWall(_wallStart.Value, corner);
                _wallStart = null;
            }

            return (int)AfterGuiInput.Stop;
        }

        return (int)AfterGuiInput.Pass;
    }

    // -------------------------------------------------------------------------
    // Wall placement
    // -------------------------------------------------------------------------

    private void PlaceWall(Vector3 start, Vector3 end)
    {
        var wallParent = GetOrCreateParentNode("Walls");
        if (wallParent == null) return;

        // Length = horizontal distance between the two corners
        float length = new Vector2(end.X - start.X, end.Z - start.Z).Length();
        if (length < 0.01f) return;

        // Centre of the wall sits halfway between start and end, vertically at half height
        var center = new Vector3(
            (start.X + end.X) * 0.5f,
            WallHeight * 0.5f,
            (start.Z + end.Z) * 0.5f
        );

        var wall = new CsgBox3D
        {
            Name     = "Wall",
            Size     = new Vector3(length, WallHeight, WallThickness),
            Position = center,
        };

        // Add collision to the wall
        wall.UseCollision = true;

        // Build a basis where local X points from start to end.
        // local Y stays up, local Z is the cross product (thickness axis).
        var dirXZ  = (end - start).Normalized();
        var basisX = dirXZ;
        var basisY = Vector3.Up;
        var basisZ = basisY.Cross(basisX).Normalized();
        wall.Basis = new Basis(basisX, basisY, basisZ);

        wallParent.AddChild(wall);
        wall.Owner = wallParent.Owner;

        var undo = GetUndoRedo();
        undo.CreateAction("Place Wall");
        undo.AddDoMethod(wallParent, Node.MethodName.AddChild, wall);
        undo.AddUndoMethod(wallParent, Node.MethodName.RemoveChild, wall);
        undo.CommitAction(false);
    }

    // -------------------------------------------------------------------------
    // Previews
    // -------------------------------------------------------------------------

    private CsgBox3D CreatePreviewMarker(string name, Vector3 size, Color color, Vector3 position)
    {
        var scene = GetEditorInterface().GetEditedSceneRoot();
        if (scene == null) return null;

        var marker = new CsgBox3D
        {
            Name     = name,
            Size     = size,
            Position = position,
            MaterialOverride = MakeMaterial(color)
        };
        scene.AddChild(marker);
        return marker;
    }

    private void CreateGhostTile()
    {
        _ghostTile = CreatePreviewMarker(
            "__HB_GhostFloor__",
            new Vector3(1f, 0.1f, 1f),
            new Color(0.2f, 0.9f, 0.3f, 0.4f),
            new Vector3(0f, -0.05f, 0f)
        );
    }

    private void CreateWallPointMarker()
    {
        _wallPointMarker = CreatePreviewMarker(
            "__HB_WallPoint__",
            new Vector3(0.2f, 0.2f, 0.2f),
            new Color(0.9f, 0.5f, 0.1f, 0.9f),
            Vector3.Zero
        );
    }

    private void ClearAllPreviews()
    {
        if (_ghostTile != null && IsInstanceValid(_ghostTile))            { _ghostTile.Free();        _ghostTile        = null; }
        if (_wallPointMarker != null && IsInstanceValid(_wallPointMarker)) { _wallPointMarker.Free(); _wallPointMarker = null; }
        _floorDragStart = null;
        _wallStart = null;
    }

    // -------------------------------------------------------------------------
    // Raycast + snap helpers
    // -------------------------------------------------------------------------

    private Vector3? RaycastToFloorPlane(Camera3D camera, Vector2 screenPos)
    {
        var origin    = camera.ProjectRayOrigin(screenPos);
        var direction = camera.ProjectRayNormal(screenPos);

        if (Mathf.IsZeroApprox(direction.Y)) return null;

        float t = -origin.Y / direction.Y;
        if (t < 0) return null;

        return origin + direction * t;
    }

    private static Vector3 SnapToTileCenter(Vector3 hit) =>
        new(Mathf.Floor(hit.X) + 0.5f, -0.05f, Mathf.Floor(hit.Z) + 0.5f);

    private static Vector3 SnapToGridCorner(Vector3 hit) =>
        new(Mathf.Round(hit.X), 0f, Mathf.Round(hit.Z));

    // -------------------------------------------------------------------------
    // Material helper
    // -------------------------------------------------------------------------

    private static StandardMaterial3D MakeMaterial(Color color) => new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor  = color,
        ShadingMode  = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode     = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // -------------------------------------------------------------------------
    // Grid calculation helper
    // -------------------------------------------------------------------------

    private static (int minX, int maxX, int minZ, int maxZ) CalculateGridBounds(Vector3 a, Vector3 b)
    {
        int x0 = Mathf.RoundToInt(a.X - 0.5f);
        int z0 = Mathf.RoundToInt(a.Z - 0.5f);
        int x1 = Mathf.RoundToInt(b.X - 0.5f);
        int z1 = Mathf.RoundToInt(b.Z - 0.5f);

        return (
            Mathf.Min(x0, x1),
            Mathf.Max(x0, x1),
            Mathf.Min(z0, z1),
            Mathf.Max(z0, z1)
        );
    }

    // -------------------------------------------------------------------------
    // Parent node helper
    // -------------------------------------------------------------------------

    private Node3D GetOrCreateParentNode(string parentName)
    {
        var scene = GetEditorInterface().GetEditedSceneRoot();
        if (scene == null) return null;

        var parent = scene.GetNodeOrNull<Node3D>(parentName);
        if (parent == null)
        {
            parent = new Node3D { Name = parentName };
            scene.AddChild(parent);
            parent.Owner = scene;
        }
        return parent;
    }

    // -------------------------------------------------------------------------
    // Floor tile placement
    // -------------------------------------------------------------------------

    // Resize and reposition the ghost to cover the rectangle from a to b
    private void UpdateGhostRect(Vector3 a, Vector3 b)
    {
        if (_ghostTile == null || !IsInstanceValid(_ghostTile)) return;

        var (minX, maxX, minZ, maxZ) = CalculateGridBounds(a, b);

        int cols = maxX - minX + 1;
        int rows = maxZ - minZ + 1;

        _ghostTile.Size     = new Vector3(cols, 0.1f, rows);
        _ghostTile.Position = new Vector3(minX + cols * 0.5f, -0.05f, minZ + rows * 0.5f);
    }

    // Fill every cell in the rectangle between a and b
    private void FillFloorRect(Vector3 a, Vector3 b)
    {
        var (minX, maxX, minZ, maxZ) = CalculateGridBounds(a, b);

        var floorParent = GetOrCreateParentNode("Floor");
        if (floorParent == null) return;

        var undo = GetUndoRedo();
        undo.CreateAction("Fill Floor Rect");

        // Collect existing positions to avoid duplicates
        var occupied = new System.Collections.Generic.HashSet<(int, int)>();
        foreach (Node child in floorParent.GetChildren())
        {
            if (child is Node3D n)
                occupied.Add((Mathf.RoundToInt(n.Position.X - 0.5f), Mathf.RoundToInt(n.Position.Z - 0.5f)));
        }

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                if (occupied.Contains((x, z))) continue;

                var position = new Vector3(x + 0.5f, -0.05f, z + 0.5f);
                var tile = new CsgBox3D
                {
                    Name     = "FloorTile",
                    Size     = new Vector3(1f, 0.1f, 1f),
                    Position = position,
                };

                // Add collision to the tile
                tile.UseCollision = true;

                floorParent.AddChild(tile);
                tile.Owner = floorParent.Owner;

                undo.AddDoMethod(floorParent, Node.MethodName.AddChild, tile);
                undo.AddUndoMethod(floorParent, Node.MethodName.RemoveChild, tile);
            }
        }

        undo.CommitAction(false);
    }
}
