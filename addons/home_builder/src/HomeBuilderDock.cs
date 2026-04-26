using Godot;

[Tool]
public partial class HomeBuilderDock : Control
{
    [Signal]
    public delegate void ModeChangedEventHandler(string mode);

    [Signal]
    public delegate void FloorChangedEventHandler(int floor);

    private const string Root = "Scroll/Margin/MainContainer";

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

    // Material section roots (used to show/hide based on active mode)
    private VBoxContainer _tileSection;
    private VBoxContainer _wallSection;
    private VBoxContainer _stairSection;
    private VBoxContainer _roofSection;
    private HSeparator    _tileSeparator;
    private HSeparator    _wallSeparator;
    private HSeparator    _stairSeparator;
    private HSeparator    _roofSeparator;

    // Tile material pickers
    private EditorResourcePicker _tileTopPicker;
    private EditorResourcePicker _tileBottomPicker;
    private EditorResourcePicker _tileSidesPicker;

    // Wall material pickers
    private EditorResourcePicker _wallFaceAPicker;
    private EditorResourcePicker _wallFaceBPicker;
    private EditorResourcePicker _wallEdgesPicker;

    // Stair material pickers
    private EditorResourcePicker _stairTopPicker;
    private EditorResourcePicker _stairBottomPicker;
    private EditorResourcePicker _stairSidesPicker;

    // Roof material pickers + shape config
    private EditorResourcePicker _roofTopPicker;
    private EditorResourcePicker _roofBottomPicker;
    private EditorResourcePicker _roofSidesPicker;
    private OptionButton         _roofTypeOption;
    private OptionButton         _roofDirectionOption;
    private SpinBox              _roofPitchSpin;
    private Label                _roofDirectionLabel;
    private Label                _roofPitchLabel;

    private int _currentFloor = 0;

    // ── Tile materials ────────────────────────────────────────────────────────
    public Material TileTopMaterial    => _tileTopPicker?.EditedResource    as Material;
    public Material TileBottomMaterial => _tileBottomPicker?.EditedResource as Material;
    public Material TileSidesMaterial  => _tileSidesPicker?.EditedResource  as Material;

    // ── Wall materials ────────────────────────────────────────────────────────
    public Material WallFaceAMaterial  => _wallFaceAPicker?.EditedResource  as Material;
    public Material WallFaceBMaterial  => _wallFaceBPicker?.EditedResource  as Material;
    public Material WallEdgesMaterial  => _wallEdgesPicker?.EditedResource  as Material;

    // ── Stair materials ───────────────────────────────────────────────────────
    public Material StairTopMaterial    => _stairTopPicker?.EditedResource    as Material;
    public Material StairBottomMaterial => _stairBottomPicker?.EditedResource as Material;
    public Material StairSidesMaterial  => _stairSidesPicker?.EditedResource  as Material;

    // ── Roof config + materials ───────────────────────────────────────────────
    public Material      RoofTopMaterial       => _roofTopPicker?.EditedResource    as Material;
    public Material      RoofBottomMaterial    => _roofBottomPicker?.EditedResource as Material;
    public Material      RoofSidesMaterial     => _roofSidesPicker?.EditedResource  as Material;
    public RoofType      SelectedRoofType      => (RoofType)(_roofTypeOption?.Selected ?? 0);
    public RoofDirection SelectedRoofDirection => (RoofDirection)(_roofDirectionOption?.Selected ?? 0);
    public float         RoofPitch             => (float)(_roofPitchSpin?.Value ?? 1.5f);

    public override void _Ready()
    {
        _floorButton   = GetNode<Button>($"{Root}/ModeGrid/FloorButton");
        _wallButton    = GetNode<Button>($"{Root}/ModeGrid/WallButton");
        _ceilingButton = GetNode<Button>($"{Root}/ModeGrid/CeilingButton");
        _doorButton    = GetNode<Button>($"{Root}/ModeGrid/DoorButton");
        _windowButton  = GetNode<Button>($"{Root}/ModeGrid/WindowButton");
        _stairsButton  = GetNode<Button>($"{Root}/ModeGrid/StairsButton");
        _noneButton    = GetNode<Button>($"{Root}/NoneButton");
        _statusLabel   = GetNode<Label>($"{Root}/StatusLabel");

        _floorUpButton   = GetNode<Button>($"{Root}/FloorSelector/FloorUpButton");
        _floorDownButton = GetNode<Button>($"{Root}/FloorSelector/FloorDownButton");
        _floorLabel      = GetNode<Label>($"{Root}/FloorSelector/FloorLabel");

        _tileSection    = GetNode<VBoxContainer>($"{Root}/TileMaterials");
        _wallSection    = GetNode<VBoxContainer>($"{Root}/WallMaterials");
        _stairSection   = GetNode<VBoxContainer>($"{Root}/StairMaterials");
        _roofSection    = GetNode<VBoxContainer>($"{Root}/RoofMaterials");
        _tileSeparator  = GetNode<HSeparator>($"{Root}/HSeparator4");
        _wallSeparator  = GetNode<HSeparator>($"{Root}/HSeparator5");
        _stairSeparator = GetNode<HSeparator>($"{Root}/HSeparator6");
        _roofSeparator  = GetNode<HSeparator>($"{Root}/HSeparator7");

        // Roof config controls
        _roofTypeOption      = GetNode<OptionButton>($"{Root}/RoofMaterials/TypeRow/TypeOption");
        _roofDirectionOption = GetNode<OptionButton>($"{Root}/RoofMaterials/DirectionRow/DirectionOption");
        _roofPitchSpin       = GetNode<SpinBox>($"{Root}/RoofMaterials/PitchRow/PitchSpin");
        _roofDirectionLabel  = GetNode<Label>($"{Root}/RoofMaterials/DirectionRow/DirectionLabel");
        _roofPitchLabel      = GetNode<Label>($"{Root}/RoofMaterials/PitchRow/PitchLabel");

        PopulateRoofTypeOptions();
        PopulateRoofDirectionOptions();
        _roofTypeOption.ItemSelected += _ => UpdateRoofShapeControls();
        UpdateRoofShapeControls();

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
        _ceilingButton.Pressed += () => OnModeSelected("roof");
        _doorButton.Pressed    += () => OnModeSelected("doors");
        _windowButton.Pressed  += () => OnModeSelected("windows");
        _stairsButton.Pressed  += () => OnModeSelected("stairs");
        _noneButton.Pressed    += () => OnModeSelected("none");

        _floorUpButton.Pressed   += OnFloorUp;
        _floorDownButton.Pressed += OnFloorDown;

        // Tile pickers
        _tileTopPicker    = CreatePicker($"{Root}/TileMaterials/TopRow/TopPicker");
        _tileBottomPicker = CreatePicker($"{Root}/TileMaterials/BottomRow/BottomPicker");
        _tileSidesPicker  = CreatePicker($"{Root}/TileMaterials/SidesRow/SidesPicker");

        // Wall pickers
        _wallFaceAPicker = CreatePicker($"{Root}/WallMaterials/FaceARow/FaceAPicker");
        _wallFaceBPicker = CreatePicker($"{Root}/WallMaterials/FaceBRow/FaceBPicker");
        _wallEdgesPicker = CreatePicker($"{Root}/WallMaterials/EdgesRow/EdgesPicker");

        // Stair pickers
        _stairTopPicker    = CreatePicker($"{Root}/StairMaterials/TopRow/TopPicker");
        _stairBottomPicker = CreatePicker($"{Root}/StairMaterials/BottomRow/BottomPicker");
        _stairSidesPicker  = CreatePicker($"{Root}/StairMaterials/SidesRow/SidesPicker");

        // Roof pickers
        _roofTopPicker    = CreatePicker($"{Root}/RoofMaterials/TopRow/TopPicker");
        _roofBottomPicker = CreatePicker($"{Root}/RoofMaterials/BottomRow/BottomPicker");
        _roofSidesPicker  = CreatePicker($"{Root}/RoofMaterials/SidesRow/SidesPicker");

        UpdateFloorLabel();
        UpdateMaterialSectionsVisibility("none");
    }

    private void PopulateRoofTypeOptions()
    {
        _roofTypeOption.Clear();
        _roofTypeOption.AddItem("Plano",         (int)RoofType.Flat);
        _roofTypeOption.AddItem("A un agua",     (int)RoofType.Shed);
        _roofTypeOption.AddItem("A dos aguas",   (int)RoofType.Gable);
        _roofTypeOption.AddItem("A cuatro aguas",(int)RoofType.Hip);
    }

    private void PopulateRoofDirectionOptions()
    {
        _roofDirectionOption.Clear();
        _roofDirectionOption.AddItem("Norte", (int)RoofDirection.North);
        _roofDirectionOption.AddItem("Sur",   (int)RoofDirection.South);
        _roofDirectionOption.AddItem("Este",  (int)RoofDirection.East);
        _roofDirectionOption.AddItem("Oeste", (int)RoofDirection.West);
    }

    private void UpdateRoofShapeControls()
    {
        // Direction and pitch are only meaningful for non-flat roofs;
        // hip ignores direction (rotational symmetry of a rectangular hip).
        var t = SelectedRoofType;
        bool needsPitch     = t != RoofType.Flat;
        bool needsDirection = t == RoofType.Shed || t == RoofType.Gable;

        _roofPitchSpin.Editable      = needsPitch;
        _roofPitchLabel.Modulate     = needsPitch ? Colors.White : new Color(1, 1, 1, 0.4f);
        _roofDirectionOption.Disabled = !needsDirection;
        _roofDirectionLabel.Modulate  = needsDirection ? Colors.White : new Color(1, 1, 1, 0.4f);
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
        _statusLabel.Text = mode == "none"
            ? "Sin modo activo"
            : $"Modo activo: {ModeDisplayName(mode)}";
        UpdateMaterialSectionsVisibility(mode);
        EmitSignal(SignalName.ModeChanged, mode);
    }

    private void UpdateMaterialSectionsVisibility(string mode)
    {
        bool showTile  = mode is "floor";
        bool showWall  = mode is "walls" or "doors" or "windows";
        bool showStair = mode is "stairs";
        bool showRoof  = mode is "roof";

        _tileSection.Visible    = showTile;
        _tileSeparator.Visible  = showTile;
        _wallSection.Visible    = showWall;
        _wallSeparator.Visible  = showWall;
        _stairSection.Visible   = showStair;
        _stairSeparator.Visible = showStair;
        _roofSection.Visible    = showRoof;
        _roofSeparator.Visible  = showRoof;
    }

    private static string ModeDisplayName(string mode) => mode switch
    {
        "floor"   => "Suelos",
        "walls"   => "Paredes",
        "roof"    => "Tejados",
        "doors"   => "Puertas",
        "windows" => "Ventanas",
        "stairs"  => "Escaleras",
        _         => mode,
    };

    private void OnFloorUp()
    {
        _currentFloor++;
        UpdateFloorLabel();
        EmitSignal(SignalName.FloorChanged, _currentFloor);
    }

    private void OnFloorDown()
    {
        _currentFloor--;
        UpdateFloorLabel();
        EmitSignal(SignalName.FloorChanged, _currentFloor);
    }

    private void UpdateFloorLabel()
    {
        _floorLabel.Text = $"Piso {_currentFloor}";
    }
}
