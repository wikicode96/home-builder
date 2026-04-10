using Godot;

public class StairsBuilder
{
    private readonly HomeBuilderPlugin _plugin;

    private const int   StairCount    = 17;
    private const float StairRise     = WallBuilder.Height / StairCount;
    private const float StairRun      = 0.28f;
    private const float StairWidth    = 1.0f;
    private const float StairTotalRun = StairCount * StairRun;

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
            new Vector3(StairWidth, StairRise, StairRun),
            new Color(0.9f, 0.8f, 0.1f, 0.4f),
            new Vector3(0f, floorBaseY, 0f)
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
                    _ghost.Size  = new Vector3(StairWidth, StairRise, StairRun);
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

        var center = start
                   + dirXZ    * (StairTotalRun * 0.5f - 0.5f)
                   + Vector3.Up * (floorBaseY + WallBuilder.Height * 0.5f);

        _ghost.Size           = new Vector3(StairWidth, WallBuilder.Height, StairTotalRun);
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

        var undo = _plugin.GetUndoRedo();
        undo.CreateAction("Place Stairs");

        for (int i = 0; i < StairCount; i++)
        {
            float runOffset  = i * StairRun + StairRun * 0.5f - 0.5f;
            float riseOffset = floorBaseY + (i + 0.5f) * StairRise;

            var stepPos = start + dirXZ * runOffset + Vector3.Up * riseOffset;

            var step = new CsgBox3D
            {
                Name         = $"Step_{i + 1}",
                Size         = new Vector3(StairWidth, StairRise, StairRun),
                UseCollision = true,
            };

            stairsParent.AddChild(step);
            step.Owner = scene;
            step.GlobalTransform = new Transform3D(stepBasis, stepPos);

            undo.AddDoMethod(stairsParent,  Node.MethodName.AddChild,    step);
            undo.AddUndoMethod(stairsParent, Node.MethodName.RemoveChild, step);
        }

        undo.CommitAction(false);
    }
}
