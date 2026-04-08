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
    // Preview (CsgBox3D — previews don't need materials)
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

    public void PlaceWall(Vector3 start, Vector3 end, float floorBaseY)
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
        var basis  = new Basis(basisX, basisY, basisZ);

        // StaticBody3D is the root — holds position, basis and collision
        var body = new StaticBody3D
        {
            Name     = "Wall",
            Position = center,
            Basis    = basis,
        };

        // Visual mesh as child
        var wall = new MeshInstance3D
        {
            Mesh = WallMeshBuilder.Build(length, Height, Thickness),
        };
        ApplyMaterials(wall);

        // Collision shape — BoxShape3D matches wall dimensions exactly
        var shape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(length, Height, Thickness) }
        };

        wallParent.AddChild(body);
        body.Owner = wallParent.Owner;

        body.AddChild(wall);
        wall.Owner = wallParent.Owner;

        body.AddChild(shape);
        shape.Owner = wallParent.Owner;

        var undo = _plugin.GetUndoRedo();
        undo.CreateAction("Place Wall");
        undo.AddDoMethod(wallParent,  Node.MethodName.AddChild,    body);
        undo.AddUndoMethod(wallParent, Node.MethodName.RemoveChild, body);
        undo.CommitAction(false);
    }

    // -------------------------------------------------------------------------
    // Materials
    // -------------------------------------------------------------------------

    public void ApplyMaterials(MeshInstance3D wall)
    {
        var dock = _plugin.Dock;
        wall.SetSurfaceOverrideMaterial(WallMeshBuilder.SurfaceFaceA,
            dock?.WallFaceAMaterial ?? MakeDefaultMaterial(new Color(0.9f, 0.9f, 0.85f)));
        wall.SetSurfaceOverrideMaterial(WallMeshBuilder.SurfaceFaceB,
            dock?.WallFaceBMaterial ?? MakeDefaultMaterial(new Color(0.85f, 0.85f, 0.8f)));
        wall.SetSurfaceOverrideMaterial(WallMeshBuilder.SurfaceEdges,
            dock?.WallEdgesMaterial ?? MakeDefaultMaterial(new Color(0.7f, 0.7f, 0.65f)));
    }

    private static StandardMaterial3D MakeDefaultMaterial(Color color) => new()
    {
        AlbedoColor = color,
    };
}
