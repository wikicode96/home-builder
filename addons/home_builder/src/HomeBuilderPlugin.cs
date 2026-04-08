using Godot;

public enum BuildMode
{
    None, Floor, Walls, Ceiling, Doors, Windows, Stairs
}

[Tool]
public partial class HomeBuilderPlugin : EditorPlugin
{
    // Exposed to builders
    public int              ActiveFloor => _activeFloor;
    public float            FloorBaseY  => (_activeFloor - 1) * WallBuilder.Height;
    public HomeBuilderDock  Dock        => _dock as HomeBuilderDock;

    private Control   _dock;
    private BuildMode _activeMode  = BuildMode.None;
    private int       _activeFloor = 1;

    private FloorBuilder   _floorBuilder;
    private WallBuilder    _wallBuilder;
    private OpeningBuilder _openingBuilder;
    private StairsBuilder  _stairsBuilder;

    public override void _EnterTree()
    {
        _floorBuilder   = new FloorBuilder(this);
        _wallBuilder    = new WallBuilder(this);
        _openingBuilder = new OpeningBuilder(this);
        _stairsBuilder  = new StairsBuilder(this);

        var dockScene = GD.Load<PackedScene>("res://addons/home_builder/src/HomeBuilderDock.tscn");
        _dock = dockScene.Instantiate<Control>();
        AddControlToDock(DockSlot.LeftUl, _dock);

        _dock.Connect(
            HomeBuilderDock.SignalName.ModeChanged,
            Callable.From((string mode) =>
            {
                ClearAllPreviews();
                _activeMode = mode switch
                {
                    "floor"   => BuildMode.Floor,
                    "walls"   => BuildMode.Walls,
                    "ceiling" => BuildMode.Ceiling,
                    "doors"   => BuildMode.Doors,
                    "windows" => BuildMode.Windows,
                    "stairs"  => BuildMode.Stairs,
                    "none"    => BuildMode.None,
                    _         => BuildMode.None
                };
                CreateActivePreviews();
            })
        );

        _dock.Connect(
            HomeBuilderDock.SignalName.FloorChanged,
            Callable.From((int floor) =>
            {
                _activeFloor = floor;
                UpdateFloorVisibility();
                ClearAllPreviews();
                CreateActivePreviews();
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
        _activeMode  = BuildMode.None;
        _activeFloor = 1;
    }

    public override bool _Handles(GodotObject obj) => true;

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------

    public override int _Forward3DGuiInput(Camera3D camera, InputEvent inputEvent)
    {
        var wallParent = GetOrCreateParentNode($"Walls_{_activeFloor}");

        return _activeMode switch
        {
            BuildMode.Floor   => _floorBuilder.HandleInput(camera, inputEvent, FloorBaseY),
            BuildMode.Walls   => _wallBuilder.HandleInput(camera, inputEvent, FloorBaseY),
            BuildMode.Doors   => _openingBuilder.HandleInput(camera, inputEvent, isDoor: true,  wallParent),
            BuildMode.Windows => _openingBuilder.HandleInput(camera, inputEvent, isDoor: false, wallParent),
            BuildMode.Stairs  => _stairsBuilder.HandleInput(camera, inputEvent, FloorBaseY),
            _                 => (int)AfterGuiInput.Pass,
        };
    }

    // -------------------------------------------------------------------------
    // Previews
    // -------------------------------------------------------------------------

    private void CreateActivePreviews()
    {
        var scene = GetEditorInterface().GetEditedSceneRoot() as Node3D;
        if (scene == null) return;

        switch (_activeMode)
        {
            case BuildMode.Floor:   _floorBuilder.CreateGhost(scene, FloorBaseY);         break;
            case BuildMode.Walls:   _wallBuilder.CreateMarker(scene, FloorBaseY);         break;
            case BuildMode.Doors:   _openingBuilder.CreateMarker(scene, isDoor: true);    break;
            case BuildMode.Windows: _openingBuilder.CreateMarker(scene, isDoor: false);   break;
            case BuildMode.Stairs:  _stairsBuilder.CreateGhost(scene, FloorBaseY);        break;
        }
    }

    private void ClearAllPreviews()
    {
        _floorBuilder?.ClearPreview();
        _wallBuilder?.ClearPreview();
        _openingBuilder?.ClearPreview();
        _stairsBuilder?.ClearPreview();
    }

    // -------------------------------------------------------------------------
    // Floor visibility
    // -------------------------------------------------------------------------

    private void UpdateFloorVisibility()
    {
        var scene = GetEditorInterface().GetEditedSceneRoot() as Node3D;
        if (scene == null) return;

        foreach (Node child in scene.GetChildren())
        {
            if (child is not Node3D node3D) continue;

            int floorIndex = ParseFloorIndex(child.Name);
            if (floorIndex < 0) continue;

            if (floorIndex == _activeFloor)
                SetNodeTransparency(node3D, 1.0f);
            else if (floorIndex < _activeFloor)
                SetNodeTransparency(node3D, 0.3f);
            else
                node3D.Visible = false;
        }
    }

    private static int ParseFloorIndex(StringName name)
    {
        string s = name.ToString();
        foreach (string prefix in new[] { "Floor_", "Walls_", "Stairs_" })
        {
            if (s.StartsWith(prefix) && int.TryParse(s[prefix.Length..], out int idx))
                return idx;
        }
        return -1;
    }

    private static void SetNodeTransparency(Node3D node, float alpha)
    {
        node.Visible = true;
        foreach (Node child in node.GetChildren())
        {
            if (child is not GeometryInstance3D geo) continue;

            geo.MaterialOverride = alpha < 1.0f
                ? new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor  = new Color(0.8f, 0.8f, 0.8f, alpha),
                    ShadingMode  = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode     = BaseMaterial3D.CullModeEnum.Disabled,
                }
                : null;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers used by builders
    // -------------------------------------------------------------------------

    public Node3D GetOrCreateParentNode(string parentName)
    {
        var scene = GetEditorInterface().GetEditedSceneRoot() as Node3D;
        if (scene == null) return null;

        var parent = scene.GetNodeOrNull<Node3D>(parentName);
        if (parent == null)
        {
            parent = new Node3D { Name = parentName };
            scene.AddChild(parent);
            parent.Owner = scene;
        }
        return parent;
    }
}
