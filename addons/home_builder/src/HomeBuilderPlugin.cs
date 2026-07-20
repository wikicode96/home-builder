#if TOOLS
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
    private BakeBuilder    _bakeBuilder;

    public override void _EnterTree()
    {
        _floorBuilder   = new FloorBuilder(this);
        _wallBuilder    = new WallBuilder(this);
        _openingBuilder = new OpeningBuilder(this);
        _stairsBuilder  = new StairsBuilder(this);
        _roofBuilder    = new RoofBuilder(this);
        _fenceBuilder   = new FenceBuilder(this);
        _bakeBuilder    = new BakeBuilder(this);

        var dockScene = GD.Load<PackedScene>("res://addons/home_builder/src/HomeBuilderDock.tscn");
        _dock = dockScene.Instantiate<Control>();
        AddControlToBottomPanel(_dock, "Home Builder");

        // Callables nativos (objeto + nombre de método), nunca delegados C#
        // (`+=` o Callable.From): cada delegado conectado a una señal crea un
        // ManagedCallable cuyo GCHandle fuerte bloquea la descarga del assembly
        // .NET durante la recarga (godotengine/godot#78513). El callable nativo
        // se resuelve por nombre en el motor y no toca el GC.
        _dock.Connect(HomeBuilderDock.SignalName.ModeChanged, new Callable(this, MethodName.OnModeChanged));
        _dock.Connect(HomeBuilderDock.SignalName.FloorChanged, new Callable(this, MethodName.OnFloorChanged));
        _dock.Connect(HomeBuilderDock.SignalName.BakeRequested, new Callable(this, MethodName.OnBakeRequested));
        _dock.Connect(HomeBuilderDock.SignalName.OpeningConfigChanged, new Callable(this, MethodName.OnOpeningConfigChanged));
        _dock.Connect(HomeBuilderDock.SignalName.BuildingConfigChanged, new Callable(this, MethodName.OnBuildingConfigChanged));

        Connect(SignalName.SceneChanged, new Callable(this, MethodName.OnSceneChanged));

        // If a scene is already open when the plugin activates, SceneChanged
        // will NOT fire. We must manually select the scene root so that Godot
        // routes _Forward3DGuiInput events to this plugin.
        CallDeferred(MethodName.SelectSceneRoot);
    }

    public override void _ExitTree()
    {
        DisconnectIfConnected(this, SignalName.SceneChanged, MethodName.OnSceneChanged);

        ClearAllPreviews();
        if (_dock != null)
        {
            DisconnectIfConnected(_dock, HomeBuilderDock.SignalName.ModeChanged, MethodName.OnModeChanged);
            DisconnectIfConnected(_dock, HomeBuilderDock.SignalName.FloorChanged, MethodName.OnFloorChanged);
            DisconnectIfConnected(_dock, HomeBuilderDock.SignalName.BakeRequested, MethodName.OnBakeRequested);
            DisconnectIfConnected(_dock, HomeBuilderDock.SignalName.OpeningConfigChanged, MethodName.OnOpeningConfigChanged);
            DisconnectIfConnected(_dock, HomeBuilderDock.SignalName.BuildingConfigChanged, MethodName.OnBuildingConfigChanged);

            RemoveControlFromBottomPanel(_dock);
            _dock.QueueFree();
            _dock = null;
        }
        _activeMode  = BuildMode.None;
        _activeFloor = 0;
    }

    // Godot puede haber destruido ya la conexión cuando corre _ExitTree (p. ej.
    // al cerrar el editor); desconectar a ciegas loguea "nonexistent connection".
    private void DisconnectIfConnected(GodotObject source, StringName signal, StringName method)
    {
        var callable = new Callable(this, method);
        if (source.IsConnected(signal, callable))
            source.Disconnect(signal, callable);
    }

    private void OnModeChanged(string mode)
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
            "bake"    => BuildMode.None,
            _         => BuildMode.None
        };
        CallDeferred(MethodName.CreateActivePreviews);
    }

    private void OnFloorChanged(int floor)
    {
        _activeFloor = floor;
        UpdateNodeVisibility();
        ClearAllPreviews();
        CallDeferred(MethodName.CreateActivePreviews);
    }

    private void OnOpeningConfigChanged()
    {
        if (_activeMode is BuildMode.Doors or BuildMode.Windows)
        {
            ClearAllPreviews();
            CallDeferred(MethodName.CreateActivePreviews);
        }
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
        return _activeMode switch
        {
            BuildMode.Floor   => _floorBuilder.HandleInput(camera, inputEvent, FloorBaseY),
            BuildMode.Walls   => _wallBuilder.HandleInput(camera, inputEvent, FloorBaseY),
            BuildMode.Roof    => _roofBuilder.HandleInput(camera, inputEvent, FloorBaseY),
            BuildMode.Doors   => _openingBuilder.HandleInput(camera, inputEvent, isDoor: true,  GetOrCreateParentNode($"Walls_{_activeFloor}")),
            BuildMode.Windows => _openingBuilder.HandleInput(camera, inputEvent, isDoor: false, GetOrCreateParentNode($"Walls_{_activeFloor}")),
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

            node3D.Visible = nodeIndex <= _activeFloor;
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
    // Bake
    // -------------------------------------------------------------------------

    private void OnBuildingConfigChanged()
    {
        var dock = Dock;
        if (dock == null) return;

        WallBuilder.Height           = dock.WallHeight;
        WallBuilder.Thickness        = dock.WallThickness;
        StairsBuilder.StairCount     = dock.StairCount;
        StairsBuilder.StairWidth     = dock.StairWidth;
        StairsBuilder.StairRun       = dock.StairRun;
        FloorBuilder.SlabThickness   = dock.FloorThickness;
        FenceBuilder.ModuleLength    = dock.FenceModuleLength;

        if (_activeMode != BuildMode.None)
        {
            ClearAllPreviews();
            CallDeferred(MethodName.CreateActivePreviews);
        }
    }

    private void OnBakeRequested()
    {
        var scene = GetEditorInterface().GetEditedSceneRoot() as Node3D;
        if (scene == null) return;

        ClearAllPreviews();

        var dock = Dock;
        if (dock == null)
        {
            GD.PrintErr("[HomeBuilder] Dock no disponible al bakear");
            return;
        }

        _bakeBuilder.Bake(
            scene,
            dock.BakeOutputPath,
            dock.BakeLod0End,
            dock.BakeLod1Begin,
            dock.BakeFadeMode,
            dock.BakeBuildNavMesh,
            dock.BakeBuildOcclusion
        );

        CallDeferred(MethodName.CreateActivePreviews);
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
#endif
