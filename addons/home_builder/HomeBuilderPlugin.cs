using Godot;

[Tool]
public partial class HomeBuilderPlugin : EditorPlugin
{
    private Control _dock;
    private string  _activeMode = "";

    // Ghost tile shown while hovering in floor mode
    private CsgBox3D _ghostTile;

    public override void _EnterTree()
    {
        var dockScene = GD.Load<PackedScene>("res://addons/home_builder/HomeBuilderDock.tscn");
        _dock = dockScene.Instantiate<Control>();
        AddControlToDock(DockSlot.LeftUl, _dock);

        _dock.Connect(
            HomeBuilderDock.SignalName.ModeChanged,
            Callable.From((string mode) =>
            {
                _activeMode = mode;

                if (_activeMode == "floor")
                    CreateGhostTile();
                else
                    DestroyGhostTile();
            })
        );
    }

    public override void _ExitTree()
    {
        DestroyGhostTile();

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
        if (_activeMode != "floor")
            return (int)AfterGuiInput.Pass;

        // Move ghost on hover
        if (inputEvent is InputEventMouseMotion motionEvent)
        {
            var pos = RaycastToGrid(camera, motionEvent.Position);
            if (pos.HasValue && _ghostTile != null)
                _ghostTile.Position = pos.Value;

            return (int)AfterGuiInput.Pass; // don't consume motion events
        }

        // Place tile on left click
        if (inputEvent is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left
            && mb.Pressed)
        {
            var position = RaycastToGrid(camera, mb.Position);
            if (position.HasValue)
            {
                PlaceFloorTile(position.Value);
                return (int)AfterGuiInput.Stop;
            }
        }

        return (int)AfterGuiInput.Pass;
    }

    // -------------------------------------------------------------------------
    // Ghost tile
    // -------------------------------------------------------------------------

    private void CreateGhostTile()
    {
        var scene = GetEditorInterface().GetEditedSceneRoot();
        if (scene == null) return;

        _ghostTile = new CsgBox3D
        {
            Name     = "__HomeBuilderGhost__",
            Size     = new Vector3(1f, 0.1f, 1f),
            Position = new Vector3(0f, -0.05f, 0f),
        };

        // Semi-transparent green material
        var material = new StandardMaterial3D
        {
            Transparency      = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor       = new Color(0.2f, 0.9f, 0.3f, 0.4f),
            ShadingMode       = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode          = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _ghostTile.MaterialOverride = material;

        scene.AddChild(_ghostTile);
        // No asignamos Owner para que no se guarde en la escena
    }

    private void DestroyGhostTile()
    {
        if (_ghostTile != null && IsInstanceValid(_ghostTile))
        {
            _ghostTile.QueueFree();
            _ghostTile = null;
        }
    }

    // -------------------------------------------------------------------------
    // Raycast → grid snap
    // -------------------------------------------------------------------------

    private Vector3? RaycastToGrid(Camera3D camera, Vector2 screenPos)
    {
        var origin    = camera.ProjectRayOrigin(screenPos);
        var direction = camera.ProjectRayNormal(screenPos);

        if (Mathf.IsZeroApprox(direction.Y))
            return null;

        float t = -origin.Y / direction.Y;
        if (t < 0)
            return null;

        var hit = origin + direction * t;

        float snappedX = Mathf.Floor(hit.X) + 0.5f;
        float snappedZ = Mathf.Floor(hit.Z) + 0.5f;

        return new Vector3(snappedX, -0.05f, snappedZ);
    }

    // -------------------------------------------------------------------------
    // Place real tile
    // -------------------------------------------------------------------------

    private void PlaceFloorTile(Vector3 position)
    {
        var scene = GetEditorInterface().GetEditedSceneRoot();
        if (scene == null)
        {
            GD.PrintErr("[HomeBuilder] No hay una escena abierta.");
            return;
        }

        Node3D floorParent = scene.GetNodeOrNull<Node3D>("Floor");
        if (floorParent == null)
        {
            floorParent = new Node3D { Name = "Floor" };
            scene.AddChild(floorParent);
            floorParent.Owner = scene;
        }

        foreach (Node child in floorParent.GetChildren())
        {
            if (child is Node3D node3D && node3D.Position.IsEqualApprox(position))
                return;
        }

        var tile = new CsgBox3D
        {
            Name     = "FloorTile",
            Size     = new Vector3(1f, 0.1f, 1f),
            Position = position
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
