using Godot;
using System.Collections.Generic;

public class OpeningBuilder
{
    private readonly HomeBuilderPlugin _plugin;

    private const float DoorWidth  = 2.0f;
    private const float DoorHeight = 2.1f;
    private const float WinWidth   = 1.0f;
    private const float WinHeight  = 1.0f;
    private const float WinSill    = 0.9f;

    // Metadata key stored on each StaticBody3D wall node to persist openings
    private const string MetaKey = "hb_openings";

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
                var   wallBody     = hit.Value.Collider;
                float openingWidth = isDoor ? DoorWidth : WinWidth;
                float snapped      = SnapHelper.ToWall(wallBody, hit.Value.Position, openingWidth);

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
                var   wallBody     = hit.Value.Collider;
                float openingWidth = isDoor ? DoorWidth : WinWidth;
                float snapped      = SnapHelper.ToWall(wallBody, hit.Value.Position, openingWidth);

                CutOpening(wallBody, snapped, isDoor);
                return 1;
            }
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // Cut / accumulate opening
    // -------------------------------------------------------------------------

    private void CutOpening(StaticBody3D wallBody, float localCenter, bool isDoor)
    {
        if (EditorInterface.Singleton.GetEditedSceneRoot() is not Node3D)
            return;

        // ── 1. Read wall length ───────────────────────────────────────────────
        float wallLen = GetWallLength(wallBody);
        if (wallLen == 0f) return;

        // ── 2. Build the new Opening struct ──────────────────────────────────
        var newOpening = new WallMeshBuilder.Opening
        {
            CenterX = localCenter,
            Width   = isDoor ? DoorWidth  : WinWidth,
            BottomY = isDoor ? 0f         : WinSill,
            Height  = isDoor ? DoorHeight : WinHeight,
        };

        // ── 3. Load existing openings from node metadata ──────────────────────
        var openings = LoadOpenings(wallBody);

        // ── 4. Guard: reject if this opening overlaps an existing one ─────────
        foreach (var existing in openings)
        {
            if (newOpening.Left < existing.Right && newOpening.Right > existing.Left)
            {
                GD.PrintErr("[HomeBuilder] Opening overlaps an existing one — skipped.");
                return;
            }
        }

        // ── 5. Guard: reject if opening would go outside the wall ─────────────
        float hx = wallLen * 0.5f;
        if (newOpening.Left < -hx || newOpening.Right > hx)
        {
            GD.PrintErr("[HomeBuilder] Opening is outside wall bounds — skipped.");
            return;
        }

        openings.Add(newOpening);

        // ── 6. Save updated list back to metadata ─────────────────────────────
        SaveOpenings(wallBody, openings);

        // ── 7. Rebuild mesh with all openings ─────────────────────────────────
        var newMesh = WallMeshBuilder.BuildWithOpenings(
            wallLen,
            WallBuilder.Height,
            WallBuilder.Thickness,
            openings
        );

        // ── 8. Apply to MeshInstance3D ────────────────────────────────────────
        var wallMesh = GetMeshInstance(wallBody);
        if (wallMesh == null) return;

        wallMesh.Mesh = newMesh;

        // ── 9. Rebuild collision ───────────────────────────────────────────────
        UpdateCollision(wallBody, newMesh);
    }

    // -------------------------------------------------------------------------
    // Metadata helpers — openings stored as a Godot Array of Dictionaries
    // -------------------------------------------------------------------------

    private static List<WallMeshBuilder.Opening> LoadOpenings(StaticBody3D wall)
    {
        var result = new List<WallMeshBuilder.Opening>();

        if (!wall.HasMeta(MetaKey)) return result;

        var arr = wall.GetMeta(MetaKey).AsGodotArray();
        foreach (var item in arr)
        {
            var d = item.AsGodotDictionary();
            result.Add(new WallMeshBuilder.Opening
            {
                CenterX = d["cx"].AsSingle(),
                Width   = d["w"].AsSingle(),
                BottomY = d["by"].AsSingle(),
                Height  = d["h"].AsSingle(),
            });
        }

        return result;
    }

    private static void SaveOpenings(StaticBody3D wall, List<WallMeshBuilder.Opening> openings)
    {
        var arr = new Godot.Collections.Array();
        foreach (var op in openings)
        {
            var d = new Godot.Collections.Dictionary
            {
                ["cx"] = op.CenterX,
                ["w"]  = op.Width,
                ["by"] = op.BottomY,
                ["h"]  = op.Height,
            };
            arr.Add(d);
        }
        wall.SetMeta(MetaKey, arr);
    }

    // -------------------------------------------------------------------------
    // Wall helpers
    // -------------------------------------------------------------------------

    // Metadata key written by WallBuilder when the wall is first created.
    // We read it here because after the first opening the CollisionShape3D
    // becomes a ConcavePolygonShape3D and BoxShape3D is no longer available.
    internal const string MetaWallLength = "hb_wall_length";

    private static float GetWallLength(StaticBody3D wallBody)
    {
        // Preferred: stored metadata (survives collision shape replacement)
        if (wallBody.HasMeta(MetaWallLength))
            return wallBody.GetMeta(MetaWallLength).AsSingle();

        // Fallback: read from BoxShape3D (only valid before the first opening)
        foreach (Node child in wallBody.GetChildren())
        {
            if (child is CollisionShape3D shape && shape.Shape is BoxShape3D box)
                return box.Size.X;
        }
        return 0f;
    }

    private static MeshInstance3D GetMeshInstance(StaticBody3D wallBody)
    {
        foreach (Node child in wallBody.GetChildren())
        {
            if (child is MeshInstance3D mesh) return mesh;
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Collision rebuild
    // -------------------------------------------------------------------------

    private static void UpdateCollision(StaticBody3D wallBody, ArrayMesh newMesh)
    {
        CollisionShape3D collisionShape = null;
        foreach (Node child in wallBody.GetChildren())
        {
            if (child is CollisionShape3D cs) { collisionShape = cs; break; }
        }
        if (collisionShape == null) return;

        var vertexList = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < newMesh.GetSurfaceCount(); i++)
        {
            var meshData = newMesh.SurfaceGetArrays(i);
            if (meshData == null || meshData.Count == 0) continue;

            var verts = (Godot.Collections.Array)meshData[(int)Mesh.ArrayType.Vertex];
            foreach (Vector3 v in verts)
                vertexList.Add(v);
        }

        if (vertexList.Count == 0) return;

        var concaveShape = new ConcavePolygonShape3D { Data = vertexList.ToArray() };
        collisionShape.Shape = concaveShape;
    }
}
