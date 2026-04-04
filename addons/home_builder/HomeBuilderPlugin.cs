using Godot;

[Tool]
public partial class HomeBuilderPlugin : EditorPlugin
{
    private Control _dock;
    private string _activeMode = "";

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
                GD.Print($"[HomeBuilder] _activeMode cambiado a: '{_activeMode}'");
            })
        );

        GD.Print("[HomeBuilder] Plugin iniciado (_EnterTree)");
    }

    public override void _ExitTree()
    {
        if (_dock != null)
        {
            RemoveControlFromDocks(_dock);
            _dock.QueueFree();
            _dock = null;
        }
        _activeMode = "";
    }

    // Sin esto Godot no llama a _Forward3DGuiInput
    public override bool _Handles(GodotObject obj) => true;

    public override int _Forward3DGuiInput(Camera3D camera, InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
        {
            GD.Print($"[HomeBuilder] Clic recibido. _activeMode='{_activeMode}', Botón={mouseEvent.ButtonIndex}");
        }

        if (_activeMode != "floor")
            return (int)AfterGuiInput.Pass;

        if (inputEvent is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left
            && mb.Pressed)
        {
            GD.Print($"[HomeBuilder] Procesando clic en modo floor. ScreenPos={mb.Position}");

            var position = RaycastToGrid(camera, mb.Position);

            if (position.HasValue)
            {
                GD.Print($"[HomeBuilder] Posición en grid: {position.Value}");
                PlaceFloorTile(position.Value);
                return (int)AfterGuiInput.Stop;
            }
            else
            {
                GD.Print("[HomeBuilder] RaycastToGrid devolvió null");
            }
        }

        return (int)AfterGuiInput.Pass;
    }

    private Vector3? RaycastToGrid(Camera3D camera, Vector2 screenPos)
    {
        var origin    = camera.ProjectRayOrigin(screenPos);
        var direction = camera.ProjectRayNormal(screenPos);

        GD.Print($"[HomeBuilder] Ray origin={origin}, direction={direction}");

        if (Mathf.IsZeroApprox(direction.Y))
            return null;

        float t = -origin.Y / direction.Y;

        if (t < 0)
            return null;

        var hit = origin + direction * t;

        float snappedX = Mathf.Floor(hit.X) + 0.5f;
        float snappedZ = Mathf.Floor(hit.Z) + 0.5f;

        return new Vector3(snappedX, 0f, snappedZ);
    }

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
            {
                GD.Print($"[HomeBuilder] Ya existe una baldosa en {position}, ignorando.");
                return;
            }
        }

        var tile = new CsgBox3D
        {
            Name     = "FloorTile",
            Size     = new Vector3(1f, 0.1f, 1f),
            Position = position
        };

        floorParent.AddChild(tile);
        tile.Owner = scene;

        GD.Print($"[HomeBuilder] Baldosa colocada en {position}");

        var undo = GetUndoRedo();
        undo.CreateAction("Place Floor Tile");
        undo.AddDoMethod(floorParent, Node.MethodName.AddChild, tile);
        undo.AddUndoMethod(floorParent, Node.MethodName.RemoveChild, tile);
        undo.CommitAction(false);
    }
}
