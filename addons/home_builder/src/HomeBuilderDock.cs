#if TOOLS
using Godot;

[Tool]
public partial class HomeBuilderDock : Control
{
    [Signal]
    public delegate void ModeChangedEventHandler(string mode);

    [Signal]
    public delegate void FloorChangedEventHandler(int floor);

    [Signal]
    public delegate void BakeRequestedEventHandler();

    [Signal]
    public delegate void OpeningConfigChangedEventHandler();

    [Signal]
    public delegate void BuildingConfigChangedEventHandler();

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
    private Button _bakeModeButton;

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
    private Container _bakeSection;

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

    // Wall config spin
    private SpinBox _wallHeightSpin;
    private SpinBox _wallThicknessSpin;

    // Fence config spin
    private SpinBox _fenceLengthSpin;

    // Stair config spins
    private SpinBox _stairCountSpin;
    private SpinBox _stairWidthSpin;
    private SpinBox _stairRunSpin;

    // Floor config spin
    private SpinBox _floorThicknessSpin;

    // Door config spins
    private SpinBox _doorWidthSpin;
    private SpinBox _doorHeightSpin;

    // Window config spins
    private SpinBox _winWidthSpin;
    private SpinBox _winHeightSpin;
    private SpinBox _winSillSpin;

    // Door / Window config sections
    private Container _doorSection;
    private Container _windowSection;

    // Fence asset picker
    private EditorResourcePicker _fenceAssetPicker;

    // Bake section controls
    private LineEdit         _bakePathEdit;
    private Button           _bakeBrowseButton;
    private SpinBox          _bakeLod0EndSpin;
    private SpinBox          _bakeLod1BeginSpin;
    private OptionButton     _bakeFadeOption;
    private CheckBox         _bakeNavMeshCheck;
    private CheckBox         _bakeOcclusionCheck;
    private Button           _bakeActionButton;
    private EditorFileDialog _bakeFolderDialog;

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

    // ── Wall config ───────────────────────────────────────────────────────────
    public float WallHeight     => (float)(_wallHeightSpin?.Value     ?? 3.0);
    public float WallThickness  => (float)(_wallThicknessSpin?.Value  ?? 0.1);

    // ── Fence config ──────────────────────────────────────────────────────────
    public float FenceModuleLength => (float)(_fenceLengthSpin?.Value ?? 1.0);

    // ── Stair config ──────────────────────────────────────────────────────────
    public int   StairCount => (int)(_stairCountSpin?.Value ?? 12);
    public float StairWidth => (float)(_stairWidthSpin?.Value ?? 1.0);
    public float StairRun   => (float)(_stairRunSpin?.Value   ?? 0.25);

    // ── Floor config ──────────────────────────────────────────────────────────
    public float FloorThickness => (float)(_floorThicknessSpin?.Value ?? 0.1);

    // ── Door config ───────────────────────────────────────────────────────────
    public float DoorWidth  => (float)(_doorWidthSpin?.Value  ?? 1.0);
    public float DoorHeight => (float)(_doorHeightSpin?.Value ?? 2.0);

    // ── Window config ─────────────────────────────────────────────────────────
    public float WinWidth  => (float)(_winWidthSpin?.Value  ?? 1.0);
    public float WinHeight => (float)(_winHeightSpin?.Value ?? 1.0);
    public float WinSill   => (float)(_winSillSpin?.Value   ?? 0.9);

    // ── Fence asset ───────────────────────────────────────────────────────────
    public PackedScene FenceAssetScene => _fenceAssetPicker?.EditedResource as PackedScene;

    // ── Bake parameters ───────────────────────────────────────────────────────
    public string BakeOutputPath    => _bakePathEdit?.Text ?? "";
    public float  BakeLod0End      => (float)(_bakeLod0EndSpin?.Value   ?? 60.0);
    public float  BakeLod1Begin    => (float)(_bakeLod1BeginSpin?.Value ?? 50.0);
    public bool   BakeBuildNavMesh  => _bakeNavMeshCheck?.ButtonPressed  ?? true;
    public bool   BakeBuildOcclusion => _bakeOcclusionCheck?.ButtonPressed ?? true;
    public GeometryInstance3D.VisibilityRangeFadeModeEnum BakeFadeMode =>
        (GeometryInstance3D.VisibilityRangeFadeModeEnum)(_bakeFadeOption?.Selected ?? 1);

    public override void _Ready()
    {
        // Mode buttons
        _floorButton   = GetNode<Button>($"{Left}/ModeGrid/FloorButton");
        _wallButton    = GetNode<Button>($"{Left}/ModeGrid/WallButton");
        _ceilingButton = GetNode<Button>($"{Left}/ModeGrid/CeilingButton");
        _doorButton    = GetNode<Button>($"{Left}/ModeGrid/DoorButton");
        _windowButton  = GetNode<Button>($"{Left}/ModeGrid/WindowButton");
        _stairsButton  = GetNode<Button>($"{Left}/ModeGrid/StairsButton");
        _fenceButton    = GetNode<Button>($"{Left}/ModeGrid/FenceButton");
        _noneButton     = GetNode<Button>($"{Left}/ModeGrid/NoneButton");
        _bakeModeButton = GetNode<Button>($"{Left}/ModeGrid/BakeModeButton");

        // Floor selector
        _floorUpButton   = GetNode<Button>($"{Left}/FloorSelector/FloorUpButton");
        _floorDownButton = GetNode<Button>($"{Left}/FloorSelector/FloorDownButton");
        _floorLabel      = GetNode<Label>($"{Left}/FloorSelector/FloorLabel");

        // Status + sections
        _statusLabel   = GetNode<Label>($"{Right}/StatusLabel");
        _tileSection   = GetNode<Container>($"{Stack}/TileMaterials");
        _wallSection   = GetNode<Container>($"{Stack}/WallMaterials");
        _stairSection  = GetNode<Container>($"{Stack}/StairMaterials");
        _roofSection   = GetNode<Container>($"{Stack}/RoofMaterials");
        _fenceSection  = GetNode<Container>($"{Stack}/FenceAssets");
        _doorSection   = GetNode<Container>($"{Stack}/DoorConfig");
        _windowSection = GetNode<Container>($"{Stack}/WindowConfig");
        _bakeSection   = GetNode<Container>($"{Stack}/BakeSection");

        // Roof config controls (now nested under ConfigRow)
        _roofTypeOption      = GetNode<OptionButton>($"{Stack}/RoofMaterials/ConfigRow/TypeRow/TypeOption");
        _roofDirectionOption = GetNode<OptionButton>($"{Stack}/RoofMaterials/ConfigRow/DirectionRow/DirectionOption");
        _roofPitchSpin       = GetNode<SpinBox>($"{Stack}/RoofMaterials/ConfigRow/PitchRow/PitchSpin");
        _roofDirectionLabel  = GetNode<Label>($"{Stack}/RoofMaterials/ConfigRow/DirectionRow/DirectionLabel");
        _roofPitchLabel      = GetNode<Label>($"{Stack}/RoofMaterials/ConfigRow/PitchRow/PitchLabel");

        PopulateRoofTypeOptions();
        PopulateRoofDirectionOptions();
        ConnectNative(_roofTypeOption, OptionButton.SignalName.ItemSelected, MethodName.OnRoofTypeSelected);
        UpdateRoofShapeControls();

        // Button group (only one mode active at a time)
        var group = new ButtonGroup();
        _floorButton.ButtonGroup    = group;
        _wallButton.ButtonGroup     = group;
        _ceilingButton.ButtonGroup  = group;
        _doorButton.ButtonGroup     = group;
        _windowButton.ButtonGroup   = group;
        _stairsButton.ButtonGroup   = group;
        _fenceButton.ButtonGroup    = group;
        _noneButton.ButtonGroup     = group;
        _bakeModeButton.ButtonGroup = group;

        ConnectNative(_floorButton,    BaseButton.SignalName.Pressed, MethodName.OnFloorModePressed);
        ConnectNative(_wallButton,     BaseButton.SignalName.Pressed, MethodName.OnWallModePressed);
        ConnectNative(_ceilingButton,  BaseButton.SignalName.Pressed, MethodName.OnRoofModePressed);
        ConnectNative(_doorButton,     BaseButton.SignalName.Pressed, MethodName.OnDoorModePressed);
        ConnectNative(_windowButton,   BaseButton.SignalName.Pressed, MethodName.OnWindowModePressed);
        ConnectNative(_stairsButton,   BaseButton.SignalName.Pressed, MethodName.OnStairsModePressed);
        ConnectNative(_fenceButton,    BaseButton.SignalName.Pressed, MethodName.OnFenceModePressed);
        ConnectNative(_noneButton,     BaseButton.SignalName.Pressed, MethodName.OnNoneModePressed);
        ConnectNative(_bakeModeButton, BaseButton.SignalName.Pressed, MethodName.OnBakeModePressed);

        ConnectNative(_floorUpButton,   BaseButton.SignalName.Pressed, MethodName.OnFloorUp);
        ConnectNative(_floorDownButton, BaseButton.SignalName.Pressed, MethodName.OnFloorDown);

        // Floor config spin
        _floorThicknessSpin = GetNode<SpinBox>($"{Stack}/TileMaterials/ConfigRow/ThicknessVBox/ThicknessSpin");
        ConnectNative(_floorThicknessSpin, Range.SignalName.ValueChanged, MethodName.OnBuildingConfigValueChanged);

        // Wall config spins
        _wallHeightSpin    = GetNode<SpinBox>($"{Stack}/WallMaterials/ConfigRow/HeightVBox/HeightSpin");
        _wallThicknessSpin = GetNode<SpinBox>($"{Stack}/WallMaterials/ConfigRow/ThicknessVBox/ThicknessSpin");
        ConnectNative(_wallHeightSpin,    Range.SignalName.ValueChanged, MethodName.OnBuildingConfigValueChanged);
        ConnectNative(_wallThicknessSpin, Range.SignalName.ValueChanged, MethodName.OnBuildingConfigValueChanged);

        // Stair config spins
        _stairCountSpin = GetNode<SpinBox>($"{Stack}/StairMaterials/ConfigRow/CountVBox/CountSpin");
        _stairWidthSpin = GetNode<SpinBox>($"{Stack}/StairMaterials/ConfigRow/WidthVBox/WidthSpin");
        _stairRunSpin   = GetNode<SpinBox>($"{Stack}/StairMaterials/ConfigRow/RunVBox/RunSpin");
        ConnectNative(_stairCountSpin, Range.SignalName.ValueChanged, MethodName.OnBuildingConfigValueChanged);
        ConnectNative(_stairWidthSpin, Range.SignalName.ValueChanged, MethodName.OnBuildingConfigValueChanged);
        ConnectNative(_stairRunSpin,   Range.SignalName.ValueChanged, MethodName.OnBuildingConfigValueChanged);

        // Material pickers — tile
        _tileTopPicker    = CreatePicker($"{Stack}/TileMaterials/MaterialsRow/TopRow/TopPicker");
        _tileBottomPicker = CreatePicker($"{Stack}/TileMaterials/MaterialsRow/BottomRow/BottomPicker");
        _tileSidesPicker  = CreatePicker($"{Stack}/TileMaterials/MaterialsRow/SidesRow/SidesPicker");

        // Material pickers — wall
        _wallFaceAPicker = CreatePicker($"{Stack}/WallMaterials/MaterialsRow/FaceARow/FaceAPicker");
        _wallFaceBPicker = CreatePicker($"{Stack}/WallMaterials/MaterialsRow/FaceBRow/FaceBPicker");
        _wallEdgesPicker = CreatePicker($"{Stack}/WallMaterials/MaterialsRow/EdgesRow/EdgesPicker");

        // Material pickers — stair
        _stairTopPicker    = CreatePicker($"{Stack}/StairMaterials/MaterialsRow/TopRow/TopPicker");
        _stairBottomPicker = CreatePicker($"{Stack}/StairMaterials/MaterialsRow/BottomRow/BottomPicker");
        _stairSidesPicker  = CreatePicker($"{Stack}/StairMaterials/MaterialsRow/SidesRow/SidesPicker");

        // Material pickers — roof (nested under MaterialsRow)
        _roofTopPicker    = CreatePicker($"{Stack}/RoofMaterials/MaterialsRow/TopRow/TopPicker");
        _roofBottomPicker = CreatePicker($"{Stack}/RoofMaterials/MaterialsRow/BottomRow/BottomPicker");
        _roofSidesPicker  = CreatePicker($"{Stack}/RoofMaterials/MaterialsRow/SidesRow/SidesPicker");

        // Door config spins
        _doorWidthSpin  = GetNode<SpinBox>($"{Stack}/DoorConfig/ConfigRow/WidthVBox/WidthSpin");
        _doorHeightSpin = GetNode<SpinBox>($"{Stack}/DoorConfig/ConfigRow/HeightVBox/HeightSpin");
        ConnectNative(_doorWidthSpin,  Range.SignalName.ValueChanged, MethodName.OnOpeningConfigValueChanged);
        ConnectNative(_doorHeightSpin, Range.SignalName.ValueChanged, MethodName.OnOpeningConfigValueChanged);

        // Window config spins
        _winWidthSpin  = GetNode<SpinBox>($"{Stack}/WindowConfig/ConfigRow/WidthVBox/WidthSpin");
        _winHeightSpin = GetNode<SpinBox>($"{Stack}/WindowConfig/ConfigRow/HeightVBox/HeightSpin");
        _winSillSpin   = GetNode<SpinBox>($"{Stack}/WindowConfig/ConfigRow/SillVBox/SillSpin");
        ConnectNative(_winWidthSpin,  Range.SignalName.ValueChanged, MethodName.OnOpeningConfigValueChanged);
        ConnectNative(_winHeightSpin, Range.SignalName.ValueChanged, MethodName.OnOpeningConfigValueChanged);
        ConnectNative(_winSillSpin,   Range.SignalName.ValueChanged, MethodName.OnOpeningConfigValueChanged);

        // Fence asset picker (PackedScene) and module length spin
        _fenceAssetPicker = CreatePicker($"{Stack}/FenceAssets/AssetRow/AssetPicker", "PackedScene");
        _fenceLengthSpin  = GetNode<SpinBox>($"{Stack}/FenceAssets/LengthRow/LengthSpin");
        ConnectNative(_fenceLengthSpin, Range.SignalName.ValueChanged, MethodName.OnBuildingConfigValueChanged);

        // Bake section
        _bakePathEdit       = GetNode<LineEdit>($"{Stack}/BakeSection/OutputRow/OutputPathEdit");
        _bakeBrowseButton   = GetNode<Button>($"{Stack}/BakeSection/OutputRow/BrowseButton");
        _bakeLod0EndSpin    = GetNode<SpinBox>($"{Stack}/BakeSection/ParamsRow/Lod0EndVBox/Lod0EndSpin");
        _bakeLod1BeginSpin  = GetNode<SpinBox>($"{Stack}/BakeSection/ParamsRow/Lod1BeginVBox/Lod1BeginSpin");
        _bakeFadeOption     = GetNode<OptionButton>($"{Stack}/BakeSection/ParamsRow/FadeVBox/FadeOption");
        _bakeNavMeshCheck   = GetNode<CheckBox>($"{Stack}/BakeSection/ChecksRow/NavMeshCheck");
        _bakeOcclusionCheck = GetNode<CheckBox>($"{Stack}/BakeSection/ChecksRow/OcclusionCheck");
        _bakeActionButton   = GetNode<Button>($"{Stack}/BakeSection/BakeActionButton");

        _bakeFolderDialog = new EditorFileDialog
        {
            Access   = EditorFileDialog.AccessEnum.Resources,
            FileMode = EditorFileDialog.FileModeEnum.OpenDir,
            Title    = "Seleccionar carpeta de salida",
        };
        AddChild(_bakeFolderDialog);
        ConnectNative(_bakeFolderDialog, EditorFileDialog.SignalName.DirSelected, MethodName.OnBakeDirSelected);

        ConnectNative(_bakeBrowseButton, BaseButton.SignalName.Pressed, MethodName.OnBakeBrowsePressed);
        ConnectNative(_bakeActionButton, BaseButton.SignalName.Pressed, MethodName.OnBakeActionPressed);

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

    // Conecta con un callable nativo (objeto + nombre de método) en vez de un
    // delegado C# (`+=` o Callable.From): cada delegado conectado a una señal
    // crea un ManagedCallable cuyo GCHandle fuerte bloquea la descarga del
    // assembly .NET durante la recarga (godotengine/godot#78513). El callable
    // nativo se resuelve por nombre en el motor y no toca el GC.
    private void ConnectNative(GodotObject source, StringName signal, StringName method)
        => source.Connect(signal, new Callable(this, method));

    private void OnFloorModePressed()  => OnModeSelected("floor");
    private void OnWallModePressed()   => OnModeSelected("walls");
    private void OnRoofModePressed()   => OnModeSelected("roof");
    private void OnDoorModePressed()   => OnModeSelected("doors");
    private void OnWindowModePressed() => OnModeSelected("windows");
    private void OnStairsModePressed() => OnModeSelected("stairs");
    private void OnFenceModePressed()  => OnModeSelected("fences");
    private void OnNoneModePressed()   => OnModeSelected("none");
    private void OnBakeModePressed()   => OnModeSelected("bake");

    private void OnRoofTypeSelected(long index) => UpdateRoofShapeControls();

    private void OnBuildingConfigValueChanged(double value) => EmitSignal(SignalName.BuildingConfigChanged);
    private void OnOpeningConfigValueChanged(double value)  => EmitSignal(SignalName.OpeningConfigChanged);

    private void OnBakeDirSelected(string dir) => _bakePathEdit.Text = dir;
    private void OnBakeBrowsePressed() => _bakeFolderDialog.PopupCentered(new Vector2I(800, 600));
    private void OnBakeActionPressed() => EmitSignal(SignalName.BakeRequested);

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
        _tileSection.Visible   = mode is "floor";
        _wallSection.Visible   = mode is "walls";
        _stairSection.Visible  = mode is "stairs";
        _roofSection.Visible   = mode is "roof";
        _fenceSection.Visible  = mode is "fences";
        _doorSection.Visible   = mode is "doors";
        _windowSection.Visible = mode is "windows";
        _bakeSection.Visible   = mode is "bake";
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
        "bake"    => "Bakear",
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
#endif
