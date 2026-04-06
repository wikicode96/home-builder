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
    private Label  _statusLabel;

    private Button _floorUpButton;
    private Button _floorDownButton;
    private Label  _floorLabel;

    // Material pickers for floor tiles
    private EditorResourcePicker _tileTopPicker;
    private EditorResourcePicker _tileBottomPicker;
    private EditorResourcePicker _tileSidesPicker;

    private int _currentFloor = 1;

    // Current materials — read by FloorBuilder when placing tiles
    public Material TileTopMaterial    => _tileTopPicker?.EditedResource    as Material;
    public Material TileBottomMaterial => _tileBottomPicker?.EditedResource as Material;
    public Material TileSidesMaterial  => _tileSidesPicker?.EditedResource  as Material;

    public override void _Ready()
    {
        _floorButton   = GetNode<Button>("MainContainer/FloorButton");
        _wallButton    = GetNode<Button>("MainContainer/WallButton");
        _ceilingButton = GetNode<Button>("MainContainer/CeilingButton");
        _doorButton    = GetNode<Button>("MainContainer/DoorButton");
        _windowButton  = GetNode<Button>("MainContainer/WindowButton");
        _stairsButton  = GetNode<Button>("MainContainer/StairsButton");
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

        _floorButton.Pressed   += () => OnModeSelected("floor");
        _wallButton.Pressed    += () => OnModeSelected("walls");
        _ceilingButton.Pressed += () => OnModeSelected("ceiling");
        _doorButton.Pressed    += () => OnModeSelected("doors");
        _windowButton.Pressed  += () => OnModeSelected("windows");
        _stairsButton.Pressed  += () => OnModeSelected("stairs");

        _floorUpButton.Pressed   += OnFloorUp;
        _floorDownButton.Pressed += OnFloorDown;

        // Build material pickers at runtime — EditorResourcePicker
        // must be created in code because it's an editor-only class
        _tileTopPicker    = CreatePicker("MainContainer/TileMaterials/TopRow/TopPicker");
        _tileBottomPicker = CreatePicker("MainContainer/TileMaterials/BottomRow/BottomPicker");
        _tileSidesPicker  = CreatePicker("MainContainer/TileMaterials/SidesRow/SidesPicker");

        UpdateFloorLabel();
    }

    private EditorResourcePicker CreatePicker(string containerPath)
    {
        var container = GetNode<HBoxContainer>(containerPath);
        if (container == null) return null;

        var picker = new EditorResourcePicker
        {
            BaseType         = "Material",
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
