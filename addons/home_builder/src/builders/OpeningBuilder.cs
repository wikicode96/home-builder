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
            var hit = RaycastHelper.ToWalls(camera, mb.Position, wallParent);
            if (hit.HasValue)
            {
                var wallBody  = hit.Value.Collider;
                float opening = isDoor ? DoorWidth : WinWidth;
                float snapped = SnapHelper.ToWall(wallBody, hit.Value.Position, opening);
                CutOpening(wallBody, snapped, isDoor, wallParent);
                return 1;
            }
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Cut opening
    // -------------------------------------------------------------------------

    private void CutOpening(StaticBody3D wallBody, float localCenter, bool isDoor, Node3D wallParent)
    {
        var scene = _plugin.GetEditorInterface().GetEditedSceneRoot() as Node3D;
        if (scene == null) return;

        var wallGlobalBasis = wallBody.GlobalTransform.Basis;
        var wallOrigin      = wallBody.GlobalPosition;
        var axisX           = wallGlobalBasis.X.Normalized();
        var axisY           = wallGlobalBasis.Y.Normalized();

        // Get wall length from CollisionShape3D
        float wallLen = 0f;
        foreach (Node child in wallBody.GetChildren())
        {
            if (child is CollisionShape3D shape && shape.Shape is BoxShape3D boxShape)
            {
                wallLen = boxShape.Size.X;
                break;
            }
        }
        if (wallLen == 0f) return;

        float opening  = isDoor ? DoorWidth  : WinWidth;
        float oHeight  = isDoor ? DoorHeight : WinHeight;
        float oBottom  = isDoor ? 0f         : WinSill;
        float oTop     = oBottom + oHeight;

        float leftEnd  = -wallLen * 0.5f;
        float rightEnd =  wallLen * 0.5f;
        float gapLeft  = localCenter - opening * 0.5f;
        float gapRight = localCenter + opening * 0.5f;

        Vector3 SegmentWorldPos(float fromX, float toX, float fromY, float toY) =>
            wallOrigin
            + axisX * ((fromX + toX) * 0.5f)
            + axisY * ((fromY + toY) * 0.5f - WallBuilder.Height * 0.5f);

        var undo = _plugin.GetUndoRedo();
        undo.CreateAction(isDoor ? "Cut Door" : "Cut Window");

        void AddSegment(float fromX, float toX, float fromY, float toY)
        {
            float len = toX - fromX;
            float h   = toY - fromY;
            if (len < 0.01f || h < 0.01f) return;

            var seg = new CsgBox3D
            {
                Name         = "Wall",
                Size         = new Vector3(len, h, WallBuilder.Thickness),
                UseCollision = true,
            };

            wallParent.AddChild(seg);
            seg.Owner = scene;
            seg.GlobalTransform = new Transform3D(wallGlobalBasis, SegmentWorldPos(fromX, toX, fromY, toY));

            undo.AddDoMethod(wallParent,  Node.MethodName.AddChild,    seg);
            undo.AddUndoMethod(wallParent, Node.MethodName.RemoveChild, seg);
        }

        AddSegment(leftEnd,  gapLeft,  0f,                WallBuilder.Height);
        AddSegment(gapRight, rightEnd, 0f,                WallBuilder.Height);
        AddSegment(gapLeft,  gapRight, oTop,              WallBuilder.Height);
        if (!isDoor)
            AddSegment(gapLeft, gapRight, 0f, oBottom);

        wallParent.RemoveChild(wallBody);
        undo.AddDoMethod(wallParent,   Node.MethodName.RemoveChild, wallBody);
        undo.AddUndoMethod(wallParent, Node.MethodName.AddChild,    wallBody);

        undo.CommitAction(false);
    }
}
