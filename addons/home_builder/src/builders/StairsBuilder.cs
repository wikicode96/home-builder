using Godot;

public class StairsBuilder
{
    private readonly HomeBuilderPlugin _plugin;

    private const int   StairCount    = 12;
    private const float StairRise     = WallBuilder.Height / StairCount;  // 0.25 m per step
    private const float StairRun      = 0.25f;                            // 0.25 m → 12 steps = exactly 3 m
    private const float StairWidth    = 1.0f;
    private const float StairTotalRun = StairCount * StairRun;            // exactly 3.0 m = 3 tiles

    private CsgBox3D _ghost;
    private Vector3? _start;

    public StairsBuilder(HomeBuilderPlugin plugin) => _plugin = plugin;

    // -------------------------------------------------------------------------
    // Preview
    // -------------------------------------------------------------------------

    public void CreateGhost(Node3D scene, float floorBaseY)
    {
        _ghost = PreviewHelper.CreateMarker(
            scene,
            "__HB_StairsGhost__",
            new Vector3(1f, 0.1f, 1f),
            new Color(0.9f, 0.8f, 0.1f, 0.4f),
            new Vector3(0f, floorBaseY - 0.05f, 0f)
        );
    }

    public void ClearPreview()
    {
        PreviewHelper.Free(ref _ghost);
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
            if (!pos.HasValue) return 0;

            var snapped = SnapHelper.ToTileCenter(pos.Value, floorBaseY);

            if (_start.HasValue)
                UpdateGhost(_start.Value, snapped, floorBaseY);
            else if (_ghost != null && GodotObject.IsInstanceValid(_ghost))
                _ghost.Position = snapped;

            return 0;
        }

        if (inputEvent is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left
            && mb.Pressed)
        {
            var pos = RaycastHelper.ToFloorPlane(camera, mb.Position, floorBaseY);
            if (!pos.HasValue) return 0;

            var corner = SnapHelper.ToTileCenter(pos.Value, floorBaseY);

            if (_start == null)
            {
                _start = corner;
            }
            else
            {
                if (!_start.Value.IsEqualApprox(corner))
                    PlaceStairs(_start.Value, corner, floorBaseY);
                _start = null;

                if (_ghost != null && GodotObject.IsInstanceValid(_ghost))
                {
                    _ghost.Size  = new Vector3(1f, 0.1f, 1f);
                    _ghost.Basis = Basis.Identity;
                }
            }

            return 1;
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Ghost update
    // -------------------------------------------------------------------------

    private void UpdateGhost(Vector3 start, Vector3 cursor, float floorBaseY)
    {
        if (_ghost == null || !GodotObject.IsInstanceValid(_ghost)) return;

        var dir = cursor - start;
        if (dir.LengthSquared() < 0.001f) return;

        var dirXZ  = new Vector3(dir.X, 0f, dir.Z).Normalized();
        var basisX = Vector3.Up.Cross(dirXZ).Normalized();
        var basisY = Vector3.Up;
        var basisZ = dirXZ;

        var centerXZ = start + dirXZ * (StairTotalRun * 0.5f - 0.5f);
        var center   = new Vector3(centerXZ.X, floorBaseY - 0.05f, centerXZ.Z);

        _ghost.Size           = new Vector3(StairWidth, 0.1f, StairTotalRun);
        _ghost.GlobalPosition = center;
        _ghost.Basis          = new Basis(basisX, basisY, basisZ);
    }

    // -------------------------------------------------------------------------
    // Placement
    // -------------------------------------------------------------------------

    private void PlaceStairs(Vector3 start, Vector3 dirHint, float floorBaseY)
    {
        var stairsParent = _plugin.GetOrCreateParentNode($"Stairs_{_plugin.ActiveFloor}");
        if (stairsParent == null) return;

        var scene = _plugin.GetEditorInterface().GetEditedSceneRoot() as Node3D;
        if (scene == null) return;

        var diff   = dirHint - start;
        var dirXZ  = new Vector3(diff.X, 0f, diff.Z).Normalized();
        var basisZ = dirXZ;
        var basisX = Vector3.Up.Cross(basisZ).Normalized();
        var basisY = Vector3.Up;
        var stepBasis = new Basis(basisX, basisY, basisZ);

        // Use floorBaseY directly so step bottoms sit flush with the floor.
        var origin = new Vector3(start.X, floorBaseY, start.Z);

        // Build the shared mesh once — all steps share the same mesh
        var stepMesh = StairsMeshBuilder.Build(StairWidth, StairRise, StairRun);

        // Single StaticBody3D for the whole staircase
        var body = new StaticBody3D
        {
            Name     = "Staircase",
            Position = origin,
            Basis    = stepBasis,
        };

        // CollisionPolygon3D with the staircase slab profile.
        // Rotated 90° on Y so the polygon aligns with the mesh geometry.
        // Depth extrudes symmetrically ±halfWidth along X_parent.
        var collisionPoly = new CollisionPolygon3D
        {
            Depth   = StairWidth,
            Basis   = Basis.Identity.Rotated(Vector3.Up, Mathf.Pi / 2f),
            Polygon = new[]
            {
                new Vector2(-2.25f,  3.0f),
                new Vector2(-2.5f,   3.0f),
                new Vector2( 0.5f,   0.0f),
                new Vector2( 0.75f,  0.0f),
            }
        };

        var undo = _plugin.GetUndoRedo();
        undo.CreateAction("Place Stairs");

        stairsParent.AddChild(body);
        body.Owner = scene;

        body.AddChild(collisionPoly);
        collisionPoly.Owner = scene;

        // One MeshInstance3D per step as child of the single body, positioned in local space
        var dock = _plugin.Dock;
        for (int i = 0; i < StairCount; i++)
        {
            // runOffset: step center along local Z. The -0.5 aligns the back edge with the tile edge.
            float runOffset  = i * StairRun + StairRun * 0.5f - 0.5f;
            float riseOffset = (i + 0.5f) * StairRise;

            var step = new MeshInstance3D
            {
                Name     = $"Step_{i + 1}",
                Mesh     = stepMesh,
                Position = new Vector3(0f, riseOffset, runOffset),
            };

            step.SetSurfaceOverrideMaterial(StairsMeshBuilder.SurfaceTop,
                dock?.StairTopMaterial    ?? MaterialHelper.MakeDefaultMaterial(new Color(0.8f, 0.7f, 0.5f)));
            step.SetSurfaceOverrideMaterial(StairsMeshBuilder.SurfaceBottom,
                dock?.StairBottomMaterial ?? MaterialHelper.MakeDefaultMaterial(new Color(0.6f, 0.6f, 0.6f)));
            step.SetSurfaceOverrideMaterial(StairsMeshBuilder.SurfaceSides,
                dock?.StairSidesMaterial  ?? MaterialHelper.MakeDefaultMaterial(new Color(0.5f, 0.5f, 0.5f)));

            body.AddChild(step);
            step.Owner = scene;
        }

        undo.AddDoMethod(stairsParent,   Node.MethodName.AddChild,    body);
        undo.AddUndoMethod(stairsParent, Node.MethodName.RemoveChild, body);
        undo.CommitAction(false);
    }
}
