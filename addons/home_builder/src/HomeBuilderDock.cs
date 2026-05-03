using Godot;

[Tool]
public partial class HomeBuilderDock : Control
{
    [Signal]
    public delegate void ModeChangedEventHandler(string mode);

    [Signal]
    public delegate void FloorChangedEventHandler(int floor);

    private const string Left   = "Background/Margin/MainHBox/LeftColumn";
    private const string Right  = "Background/Margin/MainHBox/RightColumn";
    private const string Stack  = "Background/Margin/MainHBox/RightColumn/ConfigStack";

    // Mode buttons
    private Button _floorButton;
    private Button _wallButton;
    private Button _ceilingButton;
    private Button _doorButton;
    private Button _windowButton;
    private Button _stairsButton;
    private Button _fenceButton;
    private Button _noneButton;

    // Floor selector
    private Button _floorUpButton;
    private Button _floorDownButton;
    private Label  _floorLabel;

    // Status
    private Label _statusLabel;

    // Config sections (one visible at a time). Stored as Container so the
    // section root can be HBox/VBox without changing the field type.
    private Container _tileSection;
    private Container _wallSection;
    private Container _stairSection;
    private Container _roofSection;
    private Container _fenceSection;

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

    // Roof config + material pickers
    private EditorResourcePicker _roofTopPicker;
    private EditorResourcePicker _roofBottomPicker;
    private EditorResourcePicker _roofSidesPicker;
    private OptionButton         _roofTypeOption;
    private OptionButton         _roofDirectionOption;
    private SpinBox              _roofPitchSpin;
    private Label                _roofDirectionLabel;
    private Label                _roofPitchLabel;

    // Fence asset picker
    private EditorResourcePicker _fenceAssetPicker;

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

    // ── Fence asset ───────────────────────────────────────────────────────────
    public PackedScene FenceAssetScene => _fenceAssetPicker?.EditedResource as PackedScene;

    public override void _Ready()
    {
        // Mode buttons
        _floorButton   = GetNode<Button>($"{Left}/ModeGrid/FloorButton");
        _wallButton    = GetNode<Button>($"{Left}/ModeGrid/WallButton");
        _ceilingButton = GetNode<Button>($"{Left}/ModeGrid/CeilingButton");
        _doorButton    = GetNode<Button>($"{Left}/ModeGrid/DoorButton");
        _windowButton  = GetNode<Button>($"{Left}/ModeGrid/WindowButton");
        _stairsButton  = GetNode<Button>($"{Left}/ModeGrid/StairsButton");
        _fenceButton   = GetNode<Button>($"{Left}/ModeGrid/FenceButton");
        _noneButton    = GetNode<Button>($"{Left}/ModeGrid/NoneButton");

        // Floor selector
        _floorUpButton   = GetNode<Button>($"{Left}/FloorSelector/FloorUpButton");
        _floorDownButton = GetNode<Button>($"{Left}/FloorSelector/FloorDownButton");
        _floorLabel      = GetNode<Label>($"{Left}/FloorSelector/FloorLabel");

        // Status + sections
        _statusLabel  = GetNode<Label>($"{Right}/StatusLabel");
        _tileSection  = GetNode<Container>($"{Stack}/TileMaterials");
        _wallSection  = GetNode<Container>($"{Stack}/WallMaterials");
        _stairSection = GetNode<Container>($"{Stack}/StairMaterials");
        _roofSection  = GetNode<Container>($"{Stack}/RoofMaterials");
        _fenceSection = GetNode<Container>($"{Stack}/FenceAssets");

        // Roof config controls (now nested under ConfigRow)
        _roofTypeOption      = GetNode<OptionButton>($"{Stack}/RoofMaterials/ConfigRow/TypeRow/TypeOption");
        _roofDirectionOption = GetNode<OptionButton>($"{Stack}/RoofMaterials/ConfigRow/DirectionRow/DirectionOption");
        _roofPitchSpin       = GetNode<SpinBox>($"{Stack}/RoofMaterials/ConfigRow/PitchRow/PitchSpin");
        _roofDirectionLabel  = GetNode<Label>($"{Stack}/RoofMaterials/ConfigRow/DirectionRow/DirectionLabel");
        _roofPitchLabel      = GetNode<Label>($"{Stack}/RoofMaterials/ConfigRow/PitchRow/PitchLabel");

        PopulateRoofTypeOptions();
        PopulateRoofDirectionOptions();
        _roofTypeOption.ItemSelected += _ => UpdateRoofShapeControls();
        UpdateRoofShapeControls();

        // Button group (only one mode active at a time)
        var group = new ButtonGroup();
        _floorButton.ButtonGroup   = group;
        _wallButton.ButtonGroup    = group;
        _ceilingButton.ButtonGroup = group;
        _doorButton.ButtonGroup    = group;
        _windowButton.ButtonGroup  = group;
        _stairsButton.ButtonGroup  = group;
        _fenceButton.ButtonGroup   = group;
        _noneButton.ButtonGroup    = group;

        _floorButton.Pressed   += () => OnModeSelected("floor");
        _wallButton.Pressed    += () => OnModeSelected("walls");
        _ceilingButton.Pressed += () => OnModeSelected("roof");
        _doorButton.Pressed    += () => OnModeSelected("doors");
        _windowButton.Pressed  += () => OnModeSelected("windows");
        _stairsButton.Pressed  += () => OnModeSelected("stairs");
        _fenceButton.Pressed   += () => OnModeSelected("fences");
        _noneButton.Pressed    += () => OnModeSelected("none");

        _floorUpButton.Pressed   += OnFloorUp;
        _floorDownButton.Pressed += OnFloorDown;

        // Material pickers — tile
        _tileTopPicker    = CreatePicker($"{Stack}/TileMaterials/TopRow/TopPicker");
        _tileBottomPicker = CreatePicker($"{Stack}/TileMaterials/BottomRow/BottomPicker");
        _tileSidesPicker  = CreatePicker($"{Stack}/TileMaterials/SidesRow/SidesPicker");

        // Material pickers — wall
        _wallFaceAPicker = CreatePicker($"{Stack}/WallMaterials/FaceARow/FaceAPicker");
        _wallFaceBPicker = CreatePicker($"{Stack}/WallMaterials/FaceBRow/FaceBPicker");
        _wallEdgesPicker = CreatePicker($"{Stack}/WallMaterials/EdgesRow/EdgesPicker");

        // Material pickers — stair
        _stairTopPicker    = CreatePicker($"{Stack}/StairMaterials/TopRow/TopPicker");
        _stairBottomPicker = CreatePicker($"{Stack}/StairMaterials/BottomRow/BottomPicker");
        _stairSidesPicker  = CreatePicker($"{Stack}/StairMaterials/SidesRow/SidesPicker");

        // Material pickers — roof (nested under MaterialsRow)
        _roofTopPicker    = CreatePicker($"{Stack}/RoofMaterials/MaterialsRow/TopRow/TopPicker");
        _roofBottomPicker = CreatePicker($"{Stack}/RoofMaterials/MaterialsRow/BottomRow/BottomPicker");
        _roofSidesPicker  = CreatePicker($"{Stack}/RoofMaterials/MaterialsRow/SidesRow/SidesPicker");

        // Fence asset picker (PackedScene)
        _fenceAssetPicker = CreatePicker($"{Stack}/FenceAssets/AssetRow/AssetPicker", "PackedScene");

        UpdateFloorLabel();
        UpdateSectionsVisibility("none");
    }

    private void PopulateRoofTypeOptions()
    {
        _roofTypeOption.Clear();
        _roofTypeOption.AddItem("Plano",          (int)RoofType.Flat);
        _roofTypeOption.AddItem("A un agua",      (int)RoofType.Shed);
        _roofTypeOption.AddItem("A dos aguas",    (int)RoofType.Gable);
        _roofTypeOption.AddItem("A cuatro aguas", (int)RoofType.Hip);
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
        var t = SelectedRoofType;
        bool needsPitch     = t != RoofType.Flat;
        bool needsDirection = t == RoofType.Shed || t == RoofType.Gable;

        _roofPitchSpin.Editable        = needsPitch;
        _roofPitchLabel.Modulate       = needsPitch     ? Colors.White : new Color(1, 1, 1, 0.4f);
        _roofDirectionOption.Disabled  = !needsDirection;
        _roofDirectionLabel.Modulate   = needsDirection ? Colors.White : new Color(1, 1, 1, 0.4f);
    }

    private EditorResourcePicker CreatePicker(string containerPath, string baseType = "Material")
    {
        var container = GetNode<HBoxContainer>(containerPath);
        if (container == null) return null;

        var picker = new EditorResourcePicker
        {
            BaseType            = baseType,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        container.AddChild(picker);
        return picker;
    }

    private void OnModeSelected(string mode)
    {
        _statusLabel.Text = mode == "none"
            ? "Sin modo activo"
            : $"Modo: {ModeDisplayName(mode)}";
        UpdateSectionsVisibility(mode);
        EmitSignal(SignalName.ModeChanged, mode);
    }

    private void UpdateSectionsVisibility(string mode)
    {
        _tileSection.Visible  = mode is "floor";
        _wallSection.Visible  = mode is "walls";
        _stairSection.Visible = mode is "stairs";
        _roofSection.Visible  = mode is "roof";
        _fenceSection.Visible = mode is "fences";
    }

    private static string ModeDisplayName(string mode) => mode switch
    {
        "floor"   => "Suelos",
        "walls"   => "Paredes",
        "roof"    => "Tejados",
        "doors"   => "Puertas",
        "windows" => "Ventanas",
        "stairs"  => "Escaleras",
        "fences"  => "Vallas",
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

    private void UpdateFloorLabel() => _floorLabel.Text = $"P {_currentFloor}";
}
