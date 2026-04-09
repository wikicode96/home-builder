using Godot;

public class OpeningBuilder
{
    private readonly HomeBuilderPlugin _plugin;

    private const float DoorWidth  = 2.0f;
    private const float DoorHeight = 2.1f;
    private const float WinWidth   = 1.0f;
    private const float WinHeight  = 1.0f;
    private const float WinSill    = 0.9f;

    private CsgBox3D _marker;

    public OpeningBuilder(HomeBuilderPlugin plugin) => _plugin = plugin;

    // -------------------------------------------------------------------------
    // Preview
    // -------------------------------------------------------------------------

    public void CreateMarker(Node3D scene, bool isDoor)
    {
        float w = isDoor ? DoorWidth  : WinWidth;
        float h = isDoor ? DoorHeight : WinHeight;

        _marker = PreviewHelper.CreateMarker(
            scene,
            "__HB_OpeningMarker__",
            new Vector3(w, h, WallBuilder.Thickness + 0.05f),
            new Color(0.2f, 0.6f, 1.0f, 0.5f),
            Vector3.Zero
        );
    }

    public void ClearPreview() => PreviewHelper.Free(ref _marker);

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------

    public int HandleInput(Camera3D camera, InputEvent inputEvent, bool isDoor, Node3D wallParent)
    {
        if (inputEvent is InputEventMouseMotion motionEvent)
        {
            var hit = RaycastHelper.ToWalls(camera, motionEvent.Position, wallParent);
            if (hit.HasValue)
            {
                var wallBody  = hit.Value.Collider;
                float opening = isDoor ? DoorWidth : WinWidth;
                float snapped = SnapHelper.ToWall(wallBody, hit.Value.Position, opening);

                if (_marker != null && GodotObject.IsInstanceValid(_marker))
                {
                    float markerLocalY = isDoor
                        ? DoorHeight * 0.5f - WallBuilder.Height * 0.5f
                        : WinSill + WinHeight * 0.5f - WallBuilder.Height * 0.5f;

                    var axisX = wallBody.GlobalTransform.Basis.X.Normalized();
                    var axisY = wallBody.GlobalTransform.Basis.Y.Normalized();
                    _marker.GlobalPosition = wallBody.GlobalPosition + axisX * snapped + axisY * markerLocalY;
                    _marker.Basis          = wallBody.GlobalTransform.Basis;
                }
            }
            return 0;
        }

        if (inputEvent is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left
            && mb.Pressed)
        {
            GD.Print($"Left click detected - isDoor: {isDoor}");
            var hit = RaycastHelper.ToWalls(camera, mb.Position, wallParent);
            if (hit.HasValue)
            {
                GD.Print($"Raycast hit successful");
                var wallBody  = hit.Value.Collider;
                float opening = isDoor ? DoorWidth : WinWidth;
                float snapped = SnapHelper.ToWall(wallBody, hit.Value.Position, opening);
                GD.Print($"Snapped position: {snapped}");
                CutOpening(wallBody, snapped, isDoor, wallParent);
                return 1;
            }
            else
            {
                GD.Print("Raycast hit failed");
            }
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Cut opening
    // -------------------------------------------------------------------------

    private void CutOpening(StaticBody3D wallBody, float localCenter, bool isDoor, Node3D wallParent)
    {
        GD.Print($"CutOpening called - isDoor: {isDoor}, localCenter: {localCenter}");

        if (EditorInterface.Singleton.GetEditedSceneRoot() is not Node3D scene)
        {
            GD.Print("Scene is null");
            return;
        }

        // Get wall length from CollisionShape3D
        float wallLen = 0f;
        foreach (Node child in wallBody.GetChildren())
        {
            if (child is CollisionShape3D shape && shape.Shape is BoxShape3D boxShape)
            {
                wallLen = boxShape.Size.X;
                GD.Print($"Wall length: {wallLen}");
                break;
            }
        }
        if (wallLen == 0f)
        {
            GD.Print("Wall length is 0");
            return;
        }

        // Find the MeshInstance3D
        MeshInstance3D wallMesh = null;
        foreach (Node child in wallBody.GetChildren())
        {
            if (child is MeshInstance3D mesh)
            {
                wallMesh = mesh;
                GD.Print($"Found MeshInstance3D: {mesh.Name}");
                break;
            }
        }
        if (wallMesh == null)
        {
            GD.Print("MeshInstance3D not found");
            return;
        }

        float opening  = isDoor ? DoorWidth  : WinWidth;
        float oHeight  = isDoor ? DoorHeight : WinHeight;
        float oBottom  = isDoor ? 0f         : WinSill;

        GD.Print($"Opening params - width: {opening}, height: {oHeight}, bottom: {oBottom}");

        // Create new ArrayMesh with opening
        var newMesh = WallMeshBuilder.BuildWithOpening(
            wallLen,
            WallBuilder.Height,
            WallBuilder.Thickness,
            localCenter,
            opening,
            oBottom,
            oHeight
        );

        GD.Print($"New mesh created: {newMesh != null}");

        // Apply mesh directly first (without undo/redo for testing)
        wallMesh.Mesh = newMesh;

        // Update collision to match the new mesh with opening
        UpdateCollision(wallBody, newMesh);

        GD.Print("CutOpening completed");
    }

    private void UpdateCollision(StaticBody3D wallBody, ArrayMesh newMesh)
    {
        // Find the CollisionShape3D
        CollisionShape3D collisionShape = null;
        foreach (Node child in wallBody.GetChildren())
        {
            if (child is CollisionShape3D shape)
            {
                collisionShape = shape;
                GD.Print($"Found CollisionShape3D: {shape.Name}");
                break;
            }
        }
        if (collisionShape == null)
        {
            GD.Print("CollisionShape3D not found");
            return;
        }

        // Extract vertices from all surfaces using native C# arrays
        var vertexList = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < newMesh.GetSurfaceCount(); i++)
        {
            var meshData = newMesh.SurfaceGetArrays(i);
            if (meshData == null || meshData.Count == 0) continue;

            var meshVertices = (Godot.Collections.Array)meshData[(int)Mesh.ArrayType.Vertex];
            foreach (Vector3 v in meshVertices)
            {
                vertexList.Add(v);
            }
        }

        if (vertexList.Count == 0)
        {
            GD.Print("No vertices found in mesh");
            return;
        }

        GD.Print($"Extracted {vertexList.Count} vertices from mesh");

        // Create ConcavePolygonShape3D and set the data using the Data property
        var concaveShape = new ConcavePolygonShape3D();
        
        // Convert List<Vector3> to native Vector3[] array for the Data property
        concaveShape.Data = vertexList.ToArray();

        // Replace the collision shape
        collisionShape.Shape = concaveShape;
        GD.Print("Collision updated to ConcavePolygonShape3D");
    }
}
