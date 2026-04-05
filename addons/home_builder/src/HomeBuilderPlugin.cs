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

    // Door / window state
    private const float DoorWidth  = 2.0f;
    private const float DoorHeight = 2.1f;
    private const float WinWidth   = 1.0f;
    private const float WinHeight  = 1.0f;
    private const float WinSill    = 0.9f;

    private CsgBox3D _openingMarker;

    public override void _EnterTree()
    {
        var dockScene = GD.Load<PackedScene>("res://addons/home_builder/src/HomeBuilderDock.tscn");
        _dock = dockScene.Instantiate<Control>();
        AddControlToDock(DockSlot.LeftUl, _dock);

        _dock.Connect(
            HomeBuilderDock.SignalName.ModeChanged,
            Callable.From((string mode) =>
            {
                ClearAllPreviews();
                _activeMode = mode switch
                {
                    "floor"   => BuildMode.Floor,
                    "walls"   => BuildMode.Walls,
                    "ceiling" => BuildMode.Ceiling,
                    "doors"   => BuildMode.Doors,
                    "windows" => BuildMode.Windows,
                    "stairs"  => BuildMode.Stairs,
                    _         => BuildMode.None
                };
                if (_activeMode == BuildMode.Floor)   CreateGhostTile();
                if (_activeMode == BuildMode.Walls)   CreateWallPointMarker();
                if (_activeMode == BuildMode.Doors)   CreateOpeningMarker(isDoor: true);
                if (_activeMode == BuildMode.Windows) CreateOpeningMarker(isDoor: false);
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
            BuildMode.Floor   => HandleFloorInput(camera, inputEvent),
            BuildMode.Walls   => HandleWallInput(camera, inputEvent),
            BuildMode.Doors   => HandleOpeningInput(camera, inputEvent, isDoor: true),
            BuildMode.Windows => HandleOpeningInput(camera, inputEvent, isDoor: false),
            _                 => (int)AfterGuiInput.Pass,
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

        var dirXZ  = (end - start).Normalized();
        var basisX = dirXZ;
        var basisY = Vector3.Up;
        var basisZ = basisY.Cross(basisX).Normalized();

        var wall = new CsgBox3D
        {
            Name         = "Wall",
            Size         = new Vector3(length, WallHeight, WallThickness),
            Position     = center,
            Basis        = new Basis(basisX, basisY, basisZ),
            UseCollision = true,
        };

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
            Name             = name,
            Size             = size,
            Position         = position,
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

    private void CreateOpeningMarker(bool isDoor)
    {
        float w = isDoor ? DoorWidth  : WinWidth;
        float h = isDoor ? DoorHeight : WinHeight;

        _openingMarker = CreatePreviewMarker(
            "__HB_OpeningMarker__",
            new Vector3(w, h, WallThickness + 0.05f),
            new Color(0.2f, 0.6f, 1.0f, 0.5f),
            Vector3.Zero
        );
    }

    private void ClearAllPreviews()
    {
        if (_ghostTile       != null && IsInstanceValid(_ghostTile))      { _ghostTile.Free();      _ghostTile      = null; }
        if (_wallPointMarker != null && IsInstanceValid(_wallPointMarker)){ _wallPointMarker.Free(); _wallPointMarker = null; }
        if (_openingMarker   != null && IsInstanceValid(_openingMarker))  { _openingMarker.Free();   _openingMarker   = null; }
        _floorDragStart = null;
        _wallStart      = null;
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
                    Name         = "FloorTile",
                    Size         = new Vector3(1f, 0.1f, 1f),
                    Position     = position,
                    UseCollision = true,
                };

                floorParent.AddChild(tile);
                tile.Owner = floorParent.Owner;

                undo.AddDoMethod(floorParent, Node.MethodName.AddChild, tile);
                undo.AddUndoMethod(floorParent, Node.MethodName.RemoveChild, tile);
            }
        }

        undo.CommitAction(false);
    }

    // ── Doors / Windows ───────────────────────────────────────────────────────

    private int HandleOpeningInput(Camera3D camera, InputEvent inputEvent, bool isDoor)
    {
        if (inputEvent is InputEventMouseMotion motionEvent)
        {
            var hit = RaycastToWalls(camera, motionEvent.Position);
            if (hit.HasValue)
            {
                var box       = hit.Value.Collider;
                float opening = isDoor ? DoorWidth : WinWidth;
                float snapped = SnapToWall(box, hit.Value.Position, opening);

                if (_openingMarker != null && IsInstanceValid(_openingMarker))
                {
                    float markerLocalY = isDoor
                        ? DoorHeight * 0.5f - WallHeight * 0.5f
                        : WinSill + WinHeight * 0.5f - WallHeight * 0.5f;

                    var axisX = box.GlobalTransform.Basis.X.Normalized();
                    var axisY = box.GlobalTransform.Basis.Y.Normalized();
                    _openingMarker.GlobalPosition = box.GlobalPosition + axisX * snapped + axisY * markerLocalY;
                    _openingMarker.Basis          = box.GlobalTransform.Basis;
                }
            }
            return (int)AfterGuiInput.Pass;
        }

        if (inputEvent is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left
            && mb.Pressed)
        {
            var hit = RaycastToWalls(camera, mb.Position);
            if (hit.HasValue)
            {
                var box       = hit.Value.Collider;
                float opening = isDoor ? DoorWidth : WinWidth;
                float snapped = SnapToWall(box, hit.Value.Position, opening);
                CutOpening(box, snapped, isDoor);
                return (int)AfterGuiInput.Stop;
            }
        }

        return (int)AfterGuiInput.Pass;
    }

    private static float SnapToWall(CsgBox3D box, Vector3 worldHit, float openingWidth)
    {
        var localHit  = box.GlobalTransform.AffineInverse() * worldHit;
        float halfLen = box.Size.X * 0.5f;
        float snapped = Mathf.Round(localHit.X);
        return Mathf.Clamp(snapped, -halfLen + openingWidth * 0.5f, halfLen - openingWidth * 0.5f);
    }

    private RaycastHitInfo? RaycastToWalls(Camera3D camera, Vector2 screenPos)
    {
        var wallParent = GetOrCreateParentNode("Walls");
        if (wallParent == null) return null;

        var origin    = camera.ProjectRayOrigin(screenPos);
        var direction = camera.ProjectRayNormal(screenPos);

        CsgBox3D bestBox   = null;
        Vector3  bestPoint = Vector3.Zero;
        float    bestDist  = float.MaxValue;

        foreach (Node child in wallParent.GetChildren())
        {
            if (child is not CsgBox3D box) continue;

            var invTransform = box.GlobalTransform.AffineInverse();
            var localOrigin  = invTransform * origin;
            var localDir     = invTransform.Basis * direction;
            var half         = box.Size * 0.5f;

            float tMin = float.NegativeInfinity;
            float tMax = float.PositiveInfinity;

            for (int axis = 0; axis < 3; axis++)
            {
                float o = localOrigin[axis];
                float d = localDir[axis];
                float h = half[axis];

                if (Mathf.IsZeroApprox(d))
                {
                    if (o < -h || o > h) { tMin = float.PositiveInfinity; break; }
                }
                else
                {
                    float t1 = (-h - o) / d;
                    float t2 = ( h - o) / d;
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    tMin = Mathf.Max(tMin, t1);
                    tMax = Mathf.Min(tMax, t2);
                    if (tMin > tMax) { tMin = float.PositiveInfinity; break; }
                }
            }

            if (tMin < 0 || tMin == float.PositiveInfinity || tMin >= bestDist) continue;

            bestDist  = tMin;
            bestBox   = box;
            bestPoint = origin + direction * tMin;
        }

        if (bestBox == null) return null;
        return new RaycastHitInfo { Position = bestPoint, Collider = bestBox };
    }

    private struct RaycastHitInfo
    {
        public Vector3   Position;
        public CsgBox3D  Collider;
    }

    // Split the wall into segments leaving a gap for the door/window
    private void CutOpening(CsgBox3D wall, float localCenter, bool isDoor)
    {
        var scene = GetEditorInterface().GetEditedSceneRoot();
        if (scene == null) return;

        var wallParent = GetOrCreateParentNode("Walls");

        // Snapshot transform before modifying the scene tree
        var wallGlobalBasis = wall.GlobalTransform.Basis;
        var wallOrigin      = wall.GlobalPosition;
        var axisX           = wallGlobalBasis.X.Normalized();
        var axisY           = wallGlobalBasis.Y.Normalized();

        float wallLen  = wall.Size.X;
        float opening  = isDoor ? DoorWidth  : WinWidth;
        float oHeight  = isDoor ? DoorHeight : WinHeight;
        float oBottom  = isDoor ? 0f         : WinSill;
        float oTop     = oBottom + oHeight;

        float leftEnd  = -wallLen * 0.5f;
        float rightEnd =  wallLen * 0.5f;
        float gapLeft  = localCenter - opening * 0.5f;
        float gapRight = localCenter + opening * 0.5f;

        // wallOrigin is at WallHeight/2 up, so offset Y relative to that centre
        Vector3 SegmentWorldPos(float fromX, float toX, float fromY, float toY) =>
            wallOrigin
            + axisX * ((fromX + toX) * 0.5f)
            + axisY * ((fromY + toY) * 0.5f - WallHeight * 0.5f);

        var undo = GetUndoRedo();
        undo.CreateAction(isDoor ? "Cut Door" : "Cut Window");

        void AddSegment(float fromX, float toX, float fromY, float toY)
        {
            float len = toX - fromX;
            float h   = toY - fromY;
            if (len < 0.01f || h < 0.01f) return;

            var seg = new CsgBox3D
            {
                Name         = "Wall",
                Size         = new Vector3(len, h, WallThickness),
                UseCollision = true,
            };

            // Add to tree first so GlobalTransform is writable
            wallParent.AddChild(seg);
            seg.Owner = scene;

            // Assign basis and position AFTER being in the tree
            seg.GlobalTransform = new Transform3D(wallGlobalBasis, SegmentWorldPos(fromX, toX, fromY, toY));

            undo.AddDoMethod(wallParent,  Node.MethodName.AddChild,    seg);
            undo.AddUndoMethod(wallParent, Node.MethodName.RemoveChild, seg);
        }

        AddSegment(leftEnd,  gapLeft,  0f,   WallHeight);  // left
        AddSegment(gapRight, rightEnd, 0f,   WallHeight);  // right
        AddSegment(gapLeft,  gapRight, oTop, WallHeight);  // lintel
        if (!isDoor)
            AddSegment(gapLeft, gapRight, 0f, oBottom);    // sill (windows only)

        wallParent.RemoveChild(wall);
        undo.AddDoMethod(wallParent,   Node.MethodName.RemoveChild, wall);
        undo.AddUndoMethod(wallParent, Node.MethodName.AddChild,    wall);

        undo.CommitAction(false);
    }
}
