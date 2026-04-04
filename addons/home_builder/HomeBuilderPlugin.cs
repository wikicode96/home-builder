using Godot;

[Tool]
public partial class HomeBuilderPlugin : EditorPlugin
{
    private const float WallHeight    = 3.0f;
    private const float WallThickness = 0.3f;

    private Control _dock;
    private string  _activeMode = "";

    // Floor ghost
    private CsgBox3D _ghostTile;

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
                _activeMode = mode;
                if (_activeMode == "floor") CreateGhostTile();
                if (_activeMode == "walls") CreateWallPointMarker();
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
        _activeMode = "";
    }

    public override bool _Handles(GodotObject obj) => true;

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------

    public override int _Forward3DGuiInput(Camera3D camera, InputEvent inputEvent)
    {
        return _activeMode switch
        {
            "floor" => HandleFloorInput(camera, inputEvent),
            "walls" => HandleWallInput(camera, inputEvent),
            _       => (int)AfterGuiInput.Pass,
        };
    }

    // ── Floor ─────────────────────────────────────────────────────────────────

    private int HandleFloorInput(Camera3D camera, InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseMotion motionEvent)
        {
            var pos = RaycastToFloorPlane(camera, motionEvent.Position);
            if (pos.HasValue && _ghostTile != null)
                _ghostTile.Position = SnapToTileCenter(pos.Value);
            return (int)AfterGuiInput.Pass;
        }

        if (inputEvent is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left
            && mb.Pressed)
        {
            var pos = RaycastToFloorPlane(camera, mb.Position);
            if (pos.HasValue)
            {
                PlaceFloorTile(SnapToTileCenter(pos.Value));
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
            if (pos.HasValue && _wallPointMarker != null)
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
        var scene = GetEditorInterface().GetEditedSceneRoot();
        if (scene == null) return;

        // Reuse or create a parent "Walls" node
        Node3D wallParent = scene.GetNodeOrNull<Node3D>("Walls");
        if (wallParent == null)
        {
            wallParent = new Node3D { Name = "Walls" };
            scene.AddChild(wallParent);
            wallParent.Owner = scene;
        }

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

        // Build a basis where local X points from start to end.
        // local Y stays up, local Z is the cross product (thickness axis).
        var dirXZ  = (end - start).Normalized();
        var basisX = dirXZ;
        var basisY = Vector3.Up;
        var basisZ = basisY.Cross(basisX).Normalized();
        wall.Basis = new Basis(basisX, basisY, basisZ);

        wallParent.AddChild(wall);
        wall.Owner = scene;

        var undo = GetUndoRedo();
        undo.CreateAction("Place Wall");
        undo.AddDoMethod(wallParent, Node.MethodName.AddChild, wall);
        undo.AddUndoMethod(wallParent, Node.MethodName.RemoveChild, wall);
        undo.CommitAction(false);
    }

    // -------------------------------------------------------------------------
    // Previews
    // -------------------------------------------------------------------------

    private void CreateGhostTile()
    {
        var scene = GetEditorInterface().GetEditedSceneRoot();
        if (scene == null) return;

        _ghostTile = new CsgBox3D
        {
            Name     = "__HB_GhostFloor__",
            Size     = new Vector3(1f, 0.1f, 1f),
            Position = new Vector3(0f, -0.05f, 0f),
        };
        _ghostTile.MaterialOverride = MakeMaterial(new Color(0.2f, 0.9f, 0.3f, 0.4f));
        scene.AddChild(_ghostTile);
    }

    private void CreateWallPointMarker()
    {
        var scene = GetEditorInterface().GetEditedSceneRoot();
        if (scene == null) return;

        _wallPointMarker = new CsgBox3D
        {
            Name     = "__HB_WallPoint__",
            Size     = new Vector3(0.2f, 0.2f, 0.2f),
            Position = Vector3.Zero,
        };
        _wallPointMarker.MaterialOverride = MakeMaterial(new Color(0.9f, 0.5f, 0.1f, 0.9f));
        scene.AddChild(_wallPointMarker);
    }

    private void ClearAllPreviews()
    {
        if (_ghostTile != null && IsInstanceValid(_ghostTile))        { _ghostTile.QueueFree();        _ghostTile        = null; }
        if (_wallPointMarker != null && IsInstanceValid(_wallPointMarker)) { _wallPointMarker.QueueFree(); _wallPointMarker = null; }
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
    // Floor tile placement
    // -------------------------------------------------------------------------

    private void PlaceFloorTile(Vector3 position)
    {
        var scene = GetEditorInterface().GetEditedSceneRoot();
        if (scene == null) return;

        Node3D floorParent = scene.GetNodeOrNull<Node3D>("Floor");
        if (floorParent == null)
        {
            floorParent = new Node3D { Name = "Floor" };
            scene.AddChild(floorParent);
            floorParent.Owner = scene;
        }

        foreach (Node child in floorParent.GetChildren())
        {
            if (child is Node3D n && n.Position.IsEqualApprox(position))
                return;
        }

        var tile = new CsgBox3D
        {
            Name     = "FloorTile",
            Size     = new Vector3(1f, 0.1f, 1f),
            Position = position,
        };
        floorParent.AddChild(tile);
        tile.Owner = scene;

        var undo = GetUndoRedo();
        undo.CreateAction("Place Floor Tile");
        undo.AddDoMethod(floorParent, Node.MethodName.AddChild, tile);
        undo.AddUndoMethod(floorParent, Node.MethodName.RemoveChild, tile);
        undo.CommitAction(false);
    }
}
