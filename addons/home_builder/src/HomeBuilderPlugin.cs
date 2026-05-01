using Godot;

public enum BuildMode
{
    None, Floor, Walls, Roof, Doors, Windows, Stairs, Fences
}

[Tool]
public partial class HomeBuilderPlugin : EditorPlugin
{
    public int              ActiveFloor => _activeFloor;
    public float            FloorBaseY  => _activeFloor * WallBuilder.Height;
    public HomeBuilderDock  Dock        => _dock as HomeBuilderDock;

    private Control   _dock;
    private BuildMode _activeMode  = BuildMode.None;
    private int       _activeFloor = 0;

    private FloorBuilder   _floorBuilder;
    private WallBuilder    _wallBuilder;
    private OpeningBuilder _openingBuilder;
    private StairsBuilder  _stairsBuilder;
    private RoofBuilder    _roofBuilder;
    private FenceBuilder   _fenceBuilder;

    public override void _EnterTree()
    {
        _floorBuilder   = new FloorBuilder(this);
        _wallBuilder    = new WallBuilder(this);
        _openingBuilder = new OpeningBuilder(this);
        _stairsBuilder  = new StairsBuilder(this);
        _roofBuilder    = new RoofBuilder(this);
        _fenceBuilder   = new FenceBuilder(this);

        var dockScene = GD.Load<PackedScene>("res://addons/home_builder/src/HomeBuilderDock.tscn");
        _dock = dockScene.Instantiate<Control>();
        AddControlToBottomPanel(_dock, "Home Builder");

        _dock.Connect(
            HomeBuilderDock.SignalName.ModeChanged,
            Callable.From((string mode) =>
            {
                ClearAllPreviews();
                _activeMode = mode switch
                {
                    "floor"   => BuildMode.Floor,
                    "walls"   => BuildMode.Walls,
                    "roof"    => BuildMode.Roof,
                    "doors"   => BuildMode.Doors,
                    "windows" => BuildMode.Windows,
                    "stairs"  => BuildMode.Stairs,
                    "fences"  => BuildMode.Fences,
                    "none"    => BuildMode.None,
                    _         => BuildMode.None
                };
                CallDeferred(MethodName.CreateActivePreviews);
            })
        );

        _dock.Connect(
            HomeBuilderDock.SignalName.FloorChanged,
            Callable.From((int floor) =>
            {
                _activeFloor = floor;
                UpdateNodeVisibility();
                ClearAllPreviews();
                CallDeferred(MethodName.CreateActivePreviews);
            })
        );

        SceneChanged += OnSceneChanged;

        // If a scene is already open when the plugin activates, SceneChanged
        // will NOT fire. We must manually select the scene root so that Godot
        // routes _Forward3DGuiInput events to this plugin.
        CallDeferred(MethodName.SelectSceneRoot);
    }

    public override void _ExitTree()
    {
        SceneChanged -= OnSceneChanged;
        ClearAllPreviews();
        if (_dock != null)
        {
            RemoveControlFromBottomPanel(_dock);
            _dock.QueueFree();
            _dock = null;
        }
        _activeMode  = BuildMode.None;
        _activeFloor = 0;
    }

    private void OnSceneChanged(Node sceneRoot)
    {
        ClearAllPreviews();
        CallDeferred(MethodName.CreateActivePreviews);
        // Re-select the new root so input routing is re-established.
        CallDeferred(MethodName.SelectSceneRoot);
    }

    // Selects the scene root node in the editor so that _Forward3DGuiInput
    // is routed to this plugin. Without a selection that _Handles() accepts,
    // Godot 4 does not forward 3D viewport input events to editor plugins.
    private void SelectSceneRoot()
    {
        var root = GetEditorInterface().GetEditedSceneRoot();
        if (root == null) return;
        GetEditorInterface().GetSelection().Clear();
        GetEditorInterface().GetSelection().AddNode(root);
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
            BuildMode.Roof    => _roofBuilder.HandleInput(camera, inputEvent, FloorBaseY),
            BuildMode.Doors   => _openingBuilder.HandleInput(camera, inputEvent, isDoor: true,  wallParent),
            BuildMode.Windows => _openingBuilder.HandleInput(camera, inputEvent, isDoor: false, wallParent),
            BuildMode.Stairs  => _stairsBuilder.HandleInput(camera, inputEvent, FloorBaseY),
            BuildMode.Fences  => _fenceBuilder.HandleInput(camera, inputEvent, FloorBaseY),
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
            case BuildMode.Roof:    _roofBuilder.CreateGhost(scene, FloorBaseY);          break;
            case BuildMode.Doors:   _openingBuilder.CreateMarker(scene, isDoor: true);    break;
            case BuildMode.Windows: _openingBuilder.CreateMarker(scene, isDoor: false);   break;
            case BuildMode.Stairs:  _stairsBuilder.CreateGhost(scene, FloorBaseY);        break;
            case BuildMode.Fences:  _fenceBuilder.CreateMarker(scene, FloorBaseY);        break;
        }
    }

    private void ClearAllPreviews()
    {
        _floorBuilder?.ClearPreview();
        _wallBuilder?.ClearPreview();
        _openingBuilder?.ClearPreview();
        _stairsBuilder?.ClearPreview();
        _roofBuilder?.ClearPreview();
        _fenceBuilder?.ClearPreview();
    }

    // -------------------------------------------------------------------------
    // Node visibility
    // -------------------------------------------------------------------------

    private void UpdateNodeVisibility()
    {
        var scene = GetEditorInterface().GetEditedSceneRoot() as Node3D;
        if (scene == null) return;

        foreach (Node child in scene.GetChildren())
        {
            if (child is not Node3D node3D) continue;

            int? nodeIndex = ParseNodeIndex(child.Name);
            if (nodeIndex == null) continue;

            if (nodeIndex > _activeFloor)
                node3D.Visible = false;
            else
                node3D.Visible = true;
        }
    }

    private static int? ParseNodeIndex(StringName name)
    {
        string s = name.ToString();
        foreach (string prefix in new[] { "Floor_", "Walls_", "Stairs_", "Roof_", "Fences_" })
        {
            if (s.StartsWith(prefix) && int.TryParse(s[prefix.Length..], out int idx))
                return idx;
        }
        return null;
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
