using Godot;

[Tool]
public partial class HomeBuilderDock : Control
{
    [Signal]
    public delegate void ModeChangedEventHandler(string mode);

    [Signal]
    public delegate void FloorChangedEventHandler(int floor);

    private Button _floorButton;
    private Button _wallButton;
    private Button _ceilingButton;
    private Button _doorButton;
    private Button _windowButton;
    private Button _stairsButton;
    private Button _noneButton;
    private Label  _statusLabel;

    private Button _floorUpButton;
    private Button _floorDownButton;
    private Label  _floorLabel;

    // Tile material pickers
    private EditorResourcePicker _tileTopPicker;
    private EditorResourcePicker _tileBottomPicker;
    private EditorResourcePicker _tileSidesPicker;

    // Wall material pickers
    private EditorResourcePicker _wallFaceAPicker;
    private EditorResourcePicker _wallFaceBPicker;
    private EditorResourcePicker _wallEdgesPicker;

    private int _currentFloor = 1;

    // ── Tile materials ────────────────────────────────────────────────────────
    public Material TileTopMaterial    => _tileTopPicker?.EditedResource    as Material;
    public Material TileBottomMaterial => _tileBottomPicker?.EditedResource as Material;
    public Material TileSidesMaterial  => _tileSidesPicker?.EditedResource  as Material;

    // ── Wall materials ────────────────────────────────────────────────────────
    public Material WallFaceAMaterial  => _wallFaceAPicker?.EditedResource  as Material;
    public Material WallFaceBMaterial  => _wallFaceBPicker?.EditedResource  as Material;
    public Material WallEdgesMaterial  => _wallEdgesPicker?.EditedResource  as Material;

    public override void _Ready()
    {
        _floorButton   = GetNode<Button>("MainContainer/FloorButton");
        _wallButton    = GetNode<Button>("MainContainer/WallButton");
        _ceilingButton = GetNode<Button>("MainContainer/CeilingButton");
        _doorButton    = GetNode<Button>("MainContainer/DoorButton");
        _windowButton  = GetNode<Button>("MainContainer/WindowButton");
        _stairsButton  = GetNode<Button>("MainContainer/StairsButton");
        _noneButton    = GetNode<Button>("MainContainer/NoneButton");
        _statusLabel   = GetNode<Label>("MainContainer/StatusLabel");

        _floorUpButton   = GetNode<Button>("MainContainer/FloorSelector/FloorUpButton");
        _floorDownButton = GetNode<Button>("MainContainer/FloorSelector/FloorDownButton");
        _floorLabel      = GetNode<Label>("MainContainer/FloorSelector/FloorLabel");

        var group = new ButtonGroup();
        _floorButton.ButtonGroup   = group;
        _wallButton.ButtonGroup    = group;
        _ceilingButton.ButtonGroup = group;
        _doorButton.ButtonGroup    = group;
        _windowButton.ButtonGroup  = group;
        _stairsButton.ButtonGroup  = group;
        _noneButton.ButtonGroup    = group;

        _floorButton.Pressed   += () => OnModeSelected("floor");
        _wallButton.Pressed    += () => OnModeSelected("walls");
        _ceilingButton.Pressed += () => OnModeSelected("ceiling");
        _doorButton.Pressed    += () => OnModeSelected("doors");
        _windowButton.Pressed  += () => OnModeSelected("windows");
        _stairsButton.Pressed  += () => OnModeSelected("stairs");
        _noneButton.Pressed    += () => OnModeSelected("none");

        _floorUpButton.Pressed   += OnFloorUp;
        _floorDownButton.Pressed += OnFloorDown;

        // Tile pickers
        _tileTopPicker    = CreatePicker("MainContainer/TileMaterials/TopRow/TopPicker");
        _tileBottomPicker = CreatePicker("MainContainer/TileMaterials/BottomRow/BottomPicker");
        _tileSidesPicker  = CreatePicker("MainContainer/TileMaterials/SidesRow/SidesPicker");

        // Wall pickers
        _wallFaceAPicker = CreatePicker("MainContainer/WallMaterials/FaceARow/FaceAPicker");
        _wallFaceBPicker = CreatePicker("MainContainer/WallMaterials/FaceBRow/FaceBPicker");
        _wallEdgesPicker = CreatePicker("MainContainer/WallMaterials/EdgesRow/EdgesPicker");

        UpdateFloorLabel();
    }

    private EditorResourcePicker CreatePicker(string containerPath)
    {
        var container = GetNode<HBoxContainer>(containerPath);
        if (container == null) return null;

        var picker = new EditorResourcePicker
        {
            BaseType            = "Material",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        container.AddChild(picker);
        return picker;
    }

    private void OnModeSelected(string mode)
    {
        _statusLabel.Text = $"Modo activo: {mode}";
        EmitSignal(SignalName.ModeChanged, mode);
    }

    private void OnFloorUp()
    {
        _currentFloor++;
        UpdateFloorLabel();
        EmitSignal(SignalName.FloorChanged, _currentFloor);
    }

    private void OnFloorDown()
    {
        if (_currentFloor <= 1) return;
        _currentFloor--;
        UpdateFloorLabel();
        EmitSignal(SignalName.FloorChanged, _currentFloor);
    }

    private void UpdateFloorLabel()
    {
        _floorLabel.Text          = $"Piso {_currentFloor}";
        _floorDownButton.Disabled = _currentFloor <= 1;
    }
}
