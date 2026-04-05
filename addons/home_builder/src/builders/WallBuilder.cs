using Godot;

public class WallBuilder
{
    private readonly HomeBuilderPlugin _plugin;

    public const float Height    = 3.0f;
    public const float Thickness = 0.3f;

    private CsgBox3D _pointMarker;
    private Vector3? _start;

    public WallBuilder(HomeBuilderPlugin plugin) => _plugin = plugin;

    // -------------------------------------------------------------------------
    // Preview
    // -------------------------------------------------------------------------

    public void CreateMarker(Node3D scene, float floorBaseY)
    {
        _pointMarker = PreviewHelper.CreateMarker(
            scene,
            "__HB_WallPoint__",
            new Vector3(0.2f, 0.2f, 0.2f),
            new Color(0.9f, 0.5f, 0.1f, 0.9f),
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
                if (!_start.Value.IsEqualApprox(corner))
                    PlaceWall(_start.Value, corner, floorBaseY);
                _start = null;
            }

            return 1;
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Placement
    // -------------------------------------------------------------------------

    private void PlaceWall(Vector3 start, Vector3 end, float floorBaseY)
    {
        var wallParent = _plugin.GetOrCreateParentNode($"Walls_{_plugin.ActiveFloor}");
        if (wallParent == null) return;

        float length = new Vector2(end.X - start.X, end.Z - start.Z).Length();
        if (length < 0.01f) return;

        var center = new Vector3(
            (start.X + end.X) * 0.5f,
            floorBaseY + Height * 0.5f,
            (start.Z + end.Z) * 0.5f
        );

        var dirXZ  = (end - start).Normalized();
        var basisX = dirXZ;
        var basisY = Vector3.Up;
        var basisZ = basisY.Cross(basisX).Normalized();

        var wall = new CsgBox3D
        {
            Name         = "Wall",
            Size         = new Vector3(length, Height, Thickness),
            Position     = center,
            Basis        = new Basis(basisX, basisY, basisZ),
            UseCollision = true,
        };

        wallParent.AddChild(wall);
        wall.Owner = wallParent.Owner;

        var undo = _plugin.GetUndoRedo();
        undo.CreateAction("Place Wall");
        undo.AddDoMethod(wallParent,  Node.MethodName.AddChild,    wall);
        undo.AddUndoMethod(wallParent, Node.MethodName.RemoveChild, wall);
        undo.CommitAction(false);
    }
}
