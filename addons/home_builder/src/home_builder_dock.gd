@tool
class_name HomeBuilderDock
extends Control

signal mode_changed(mode: String)
signal floor_changed(floor_number: int)
signal bake_requested
signal opening_config_changed
signal building_config_changed

const _LEFT := "Background/Margin/MainHBox/LeftColumn"
const _RIGHT := "Background/Margin/MainHBox/RightColumn"
const _STACK := "Background/Margin/MainHBox/RightColumn/ConfigStack"

# Mode buttons
var _floor_button: Button
var _wall_button: Button
var _ceiling_button: Button
var _door_button: Button
var _window_button: Button
var _stairs_button: Button
var _fence_button: Button
var _none_button: Button
var _bake_mode_button: Button

# Floor selector
var _floor_up_button: Button
var _floor_down_button: Button
var _floor_label: Label

# Status
var _status_label: Label

# Config sections (one visible at a time). Stored as Container so the
# section root can be HBox/VBox without changing the variable type.
var _tile_section: Container
var _wall_section: Container
var _stair_section: Container
var _roof_section: Container
var _fence_section: Container
var _bake_section: Container

# Tile material pickers
var _tile_top_picker: EditorResourcePicker
var _tile_bottom_picker: EditorResourcePicker
var _tile_sides_picker: EditorResourcePicker

# Wall material pickers
var _wall_face_a_picker: EditorResourcePicker
var _wall_face_b_picker: EditorResourcePicker
var _wall_edges_picker: EditorResourcePicker

# Stair material pickers
var _stair_top_picker: EditorResourcePicker
var _stair_bottom_picker: EditorResourcePicker
var _stair_sides_picker: EditorResourcePicker

# Roof config + material pickers
var _roof_top_picker: EditorResourcePicker
var _roof_bottom_picker: EditorResourcePicker
var _roof_sides_picker: EditorResourcePicker
var _roof_end_face_a_picker: EditorResourcePicker
var _roof_end_face_b_picker: EditorResourcePicker
var _roof_end_edges_picker: EditorResourcePicker
var _roof_end_materials_row: Container
var _roof_type_option: OptionButton
var _roof_direction_option: OptionButton
var _roof_pitch_spin: SpinBox
var _roof_eave_spin: SpinBox
var _roof_thickness_spin: SpinBox
var _roof_direction_label: Label
var _roof_pitch_label: Label

# Wall config spins
var _wall_height_spin: SpinBox
var _wall_thickness_spin: SpinBox

# Fence config spin
var _fence_length_spin: SpinBox

# Stair config spins
var _stair_count_spin: SpinBox
var _stair_width_spin: SpinBox
var _stair_run_spin: SpinBox

# Floor config spin
var _floor_thickness_spin: SpinBox

# Door config spins
var _door_width_spin: SpinBox
var _door_height_spin: SpinBox

# Window config spins
var _win_width_spin: SpinBox
var _win_height_spin: SpinBox
var _win_sill_spin: SpinBox

# Door / Window config sections
var _door_section: Container
var _window_section: Container

# Door / Window asset pickers
var _door_asset_picker: EditorResourcePicker
var _window_asset_picker: EditorResourcePicker

# Fence asset picker
var _fence_asset_picker: EditorResourcePicker

# Bake section controls
var _bake_path_edit: LineEdit
var _bake_browse_button: Button
var _bake_lod0_end_spin: SpinBox
var _bake_lod1_begin_spin: SpinBox
var _bake_fade_option: OptionButton
var _bake_nav_mesh_check: CheckBox
var _bake_occlusion_check: CheckBox
var _bake_action_button: Button
var _bake_folder_dialog: EditorFileDialog

var _current_floor := 0

# ── Tile materials ───────────────────────────────────────────────────────────
var tile_top_material: Material:
	get: return (_tile_top_picker.edited_resource as Material) if _tile_top_picker != null else null
var tile_bottom_material: Material:
	get: return (_tile_bottom_picker.edited_resource as Material) if _tile_bottom_picker != null else null
var tile_sides_material: Material:
	get: return (_tile_sides_picker.edited_resource as Material) if _tile_sides_picker != null else null

# ── Wall materials ───────────────────────────────────────────────────────────
var wall_face_a_material: Material:
	get: return (_wall_face_a_picker.edited_resource as Material) if _wall_face_a_picker != null else null
var wall_face_b_material: Material:
	get: return (_wall_face_b_picker.edited_resource as Material) if _wall_face_b_picker != null else null
var wall_edges_material: Material:
	get: return (_wall_edges_picker.edited_resource as Material) if _wall_edges_picker != null else null

# ── Stair materials ──────────────────────────────────────────────────────────
var stair_top_material: Material:
	get: return (_stair_top_picker.edited_resource as Material) if _stair_top_picker != null else null
var stair_bottom_material: Material:
	get: return (_stair_bottom_picker.edited_resource as Material) if _stair_bottom_picker != null else null
var stair_sides_material: Material:
	get: return (_stair_sides_picker.edited_resource as Material) if _stair_sides_picker != null else null

# ── Roof config + materials ──────────────────────────────────────────────────
var roof_top_material: Material:
	get: return (_roof_top_picker.edited_resource as Material) if _roof_top_picker != null else null
var roof_bottom_material: Material:
	get: return (_roof_bottom_picker.edited_resource as Material) if _roof_bottom_picker != null else null
var roof_sides_material: Material:
	get: return (_roof_sides_picker.edited_resource as Material) if _roof_sides_picker != null else null
var roof_end_face_a_material: Material:
	get: return (_roof_end_face_a_picker.edited_resource as Material) if _roof_end_face_a_picker != null else null
var roof_end_face_b_material: Material:
	get: return (_roof_end_face_b_picker.edited_resource as Material) if _roof_end_face_b_picker != null else null
var roof_end_edges_material: Material:
	get: return (_roof_end_edges_picker.edited_resource as Material) if _roof_end_edges_picker != null else null
var selected_roof_type: int:  # RoofMeshBuilder.RoofType
	get: return _roof_type_option.selected if _roof_type_option != null else 0
var selected_roof_direction: int:  # RoofMeshBuilder.RoofDirection
	get: return _roof_direction_option.selected if _roof_direction_option != null else 0
var roof_pitch: float:
	get: return _roof_pitch_spin.value if _roof_pitch_spin != null else 1.5
var roof_eave: float:
	get:
		if _roof_eave_spin != null:
			return _roof_eave_spin.value
		var t: RoofMeshBuilder.RoofType = selected_roof_type
		return HBRoof.default_eave(t)
var roof_thickness: float:
	get:
		if _roof_thickness_spin != null:
			return _roof_thickness_spin.value
		var t: RoofMeshBuilder.RoofType = selected_roof_type
		return HBRoof.default_thickness(t)

# ── Wall config ──────────────────────────────────────────────────────────────
var wall_height: float:
	get: return _wall_height_spin.value if _wall_height_spin != null else 3.0
var wall_thickness: float:
	get: return _wall_thickness_spin.value if _wall_thickness_spin != null else 0.1

# ── Fence config ─────────────────────────────────────────────────────────────
var fence_module_length: float:
	get: return _fence_length_spin.value if _fence_length_spin != null else 1.0

# ── Stair config ─────────────────────────────────────────────────────────────
var stair_count: int:
	get: return int(_stair_count_spin.value) if _stair_count_spin != null else 12
var stair_width: float:
	get: return _stair_width_spin.value if _stair_width_spin != null else 1.0
var stair_run: float:
	get: return _stair_run_spin.value if _stair_run_spin != null else 0.25

# ── Floor config ─────────────────────────────────────────────────────────────
var floor_thickness: float:
	get: return _floor_thickness_spin.value if _floor_thickness_spin != null else 0.1

# ── Door config ──────────────────────────────────────────────────────────────
var door_width: float:
	get: return _door_width_spin.value if _door_width_spin != null else 1.0
var door_height: float:
	get: return _door_height_spin.value if _door_height_spin != null else 2.0

# ── Window config ────────────────────────────────────────────────────────────
var win_width: float:
	get: return _win_width_spin.value if _win_width_spin != null else 1.0
var win_height: float:
	get: return _win_height_spin.value if _win_height_spin != null else 1.0
var win_sill: float:
	get: return _win_sill_spin.value if _win_sill_spin != null else 0.9

# ── Door / Window assets ─────────────────────────────────────────────────────
var door_asset_scene: PackedScene:
	get: return (_door_asset_picker.edited_resource as PackedScene) if _door_asset_picker != null else null
var window_asset_scene: PackedScene:
	get: return (_window_asset_picker.edited_resource as PackedScene) if _window_asset_picker != null else null

# ── Fence asset ──────────────────────────────────────────────────────────────
var fence_asset_scene: PackedScene:
	get: return (_fence_asset_picker.edited_resource as PackedScene) if _fence_asset_picker != null else null

# ── Bake parameters ──────────────────────────────────────────────────────────
var bake_output_path: String:
	get: return _bake_path_edit.text if _bake_path_edit != null else ""
var bake_lod0_end: float:
	get: return _bake_lod0_end_spin.value if _bake_lod0_end_spin != null else 60.0
var bake_lod1_begin: float:
	get: return _bake_lod1_begin_spin.value if _bake_lod1_begin_spin != null else 50.0
var bake_build_nav_mesh: bool:
	get: return _bake_nav_mesh_check.button_pressed if _bake_nav_mesh_check != null else true
var bake_build_occlusion: bool:
	get: return _bake_occlusion_check.button_pressed if _bake_occlusion_check != null else true
var bake_fade_mode: int:  # GeometryInstance3D.VisibilityRangeFadeMode
	get: return _bake_fade_option.selected if _bake_fade_option != null else 1


func _ready() -> void:
	# Mode buttons
	_floor_button = get_node(_LEFT + "/ModeGrid/FloorButton")
	_wall_button = get_node(_LEFT + "/ModeGrid/WallButton")
	_ceiling_button = get_node(_LEFT + "/ModeGrid/CeilingButton")
	_door_button = get_node(_LEFT + "/ModeGrid/DoorButton")
	_window_button = get_node(_LEFT + "/ModeGrid/WindowButton")
	_stairs_button = get_node(_LEFT + "/ModeGrid/StairsButton")
	_fence_button = get_node(_LEFT + "/ModeGrid/FenceButton")
	_none_button = get_node(_LEFT + "/ModeGrid/NoneButton")
	_bake_mode_button = get_node(_LEFT + "/ModeGrid/BakeModeButton")

	# Floor selector
	_floor_up_button = get_node(_LEFT + "/FloorSelector/FloorUpButton")
	_floor_down_button = get_node(_LEFT + "/FloorSelector/FloorDownButton")
	_floor_label = get_node(_LEFT + "/FloorSelector/FloorLabel")

	# Status + sections
	_status_label = get_node(_RIGHT + "/StatusLabel")
	_tile_section = get_node(_STACK + "/TileMaterials")
	_wall_section = get_node(_STACK + "/WallMaterials")
	_stair_section = get_node(_STACK + "/StairMaterials")
	_roof_section = get_node(_STACK + "/RoofMaterials")
	_fence_section = get_node(_STACK + "/FenceAssets")
	_door_section = get_node(_STACK + "/DoorConfig")
	_window_section = get_node(_STACK + "/WindowConfig")
	_bake_section = get_node(_STACK + "/BakeSection")

	# Roof config controls (nested under ConfigRow)
	_roof_type_option = get_node(_STACK + "/RoofMaterials/ConfigRow/TypeRow/TypeOption")
	_roof_direction_option = get_node(_STACK + "/RoofMaterials/ConfigRow/DirectionRow/DirectionOption")
	_roof_pitch_spin = get_node(_STACK + "/RoofMaterials/ConfigRow/PitchRow/PitchSpin")
	_roof_eave_spin = get_node(_STACK + "/RoofMaterials/ConfigRow/EaveRow/EaveSpin")
	_roof_thickness_spin = get_node(
		_STACK + "/RoofMaterials/ConfigRow/RoofThicknessRow/RoofThicknessSpin")
	_roof_direction_label = get_node(_STACK + "/RoofMaterials/ConfigRow/DirectionRow/DirectionLabel")
	_roof_pitch_label = get_node(_STACK + "/RoofMaterials/ConfigRow/PitchRow/PitchLabel")
	_roof_end_materials_row = get_node(_STACK + "/RoofMaterials/EndMaterialsRow")

	_populate_roof_type_options()
	_populate_roof_direction_options()
	_roof_type_option.item_selected.connect(_on_roof_type_selected)
	# _update_roof_shape_controls() also gates the gable-end material picker,
	# which _create_picker only builds further down — so it runs from there.

	# Button group (only one mode active at a time)
	var group := ButtonGroup.new()
	_floor_button.button_group = group
	_wall_button.button_group = group
	_ceiling_button.button_group = group
	_door_button.button_group = group
	_window_button.button_group = group
	_stairs_button.button_group = group
	_fence_button.button_group = group
	_none_button.button_group = group
	_bake_mode_button.button_group = group

	_floor_button.pressed.connect(_on_mode_selected.bind("floor"))
	_wall_button.pressed.connect(_on_mode_selected.bind("walls"))
	_ceiling_button.pressed.connect(_on_mode_selected.bind("roof"))
	_door_button.pressed.connect(_on_mode_selected.bind("doors"))
	_window_button.pressed.connect(_on_mode_selected.bind("windows"))
	_stairs_button.pressed.connect(_on_mode_selected.bind("stairs"))
	_fence_button.pressed.connect(_on_mode_selected.bind("fences"))
	_none_button.pressed.connect(_on_mode_selected.bind("none"))
	_bake_mode_button.pressed.connect(_on_mode_selected.bind("bake"))

	_floor_up_button.pressed.connect(_on_floor_up)
	_floor_down_button.pressed.connect(_on_floor_down)

	# Floor config spin
	_floor_thickness_spin = get_node(_STACK + "/TileMaterials/ConfigRow/ThicknessVBox/ThicknessSpin")
	_floor_thickness_spin.value_changed.connect(_on_building_config_value_changed)

	# Wall config spins
	_wall_height_spin = get_node(_STACK + "/WallMaterials/ConfigRow/HeightVBox/HeightSpin")
	_wall_thickness_spin = get_node(_STACK + "/WallMaterials/ConfigRow/ThicknessVBox/ThicknessSpin")
	_wall_height_spin.value_changed.connect(_on_building_config_value_changed)
	_wall_thickness_spin.value_changed.connect(_on_building_config_value_changed)

	# Stair config spins
	_stair_count_spin = get_node(_STACK + "/StairMaterials/ConfigRow/CountVBox/CountSpin")
	_stair_width_spin = get_node(_STACK + "/StairMaterials/ConfigRow/WidthVBox/WidthSpin")
	_stair_run_spin = get_node(_STACK + "/StairMaterials/ConfigRow/RunVBox/RunSpin")
	_stair_count_spin.value_changed.connect(_on_building_config_value_changed)
	_stair_width_spin.value_changed.connect(_on_building_config_value_changed)
	_stair_run_spin.value_changed.connect(_on_building_config_value_changed)

	# Material pickers — tile
	_tile_top_picker = _create_picker(_STACK + "/TileMaterials/MaterialsRow/TopRow/TopPicker")
	_tile_bottom_picker = _create_picker(_STACK + "/TileMaterials/MaterialsRow/BottomRow/BottomPicker")
	_tile_sides_picker = _create_picker(_STACK + "/TileMaterials/MaterialsRow/SidesRow/SidesPicker")

	# Material pickers — wall
	_wall_face_a_picker = _create_picker(_STACK + "/WallMaterials/MaterialsRow/FaceARow/FaceAPicker")
	_wall_face_b_picker = _create_picker(_STACK + "/WallMaterials/MaterialsRow/FaceBRow/FaceBPicker")
	_wall_edges_picker = _create_picker(_STACK + "/WallMaterials/MaterialsRow/EdgesRow/EdgesPicker")

	# Material pickers — stair
	_stair_top_picker = _create_picker(_STACK + "/StairMaterials/MaterialsRow/TopRow/TopPicker")
	_stair_bottom_picker = _create_picker(_STACK + "/StairMaterials/MaterialsRow/BottomRow/BottomPicker")
	_stair_sides_picker = _create_picker(_STACK + "/StairMaterials/MaterialsRow/SidesRow/SidesPicker")

	# Material pickers — roof (nested under MaterialsRow)
	_roof_top_picker = _create_picker(_STACK + "/RoofMaterials/MaterialsRow/TopRow/TopPicker")
	_roof_bottom_picker = _create_picker(_STACK + "/RoofMaterials/MaterialsRow/BottomRow/BottomPicker")
	_roof_sides_picker = _create_picker(_STACK + "/RoofMaterials/MaterialsRow/SidesRow/SidesPicker")
	var end_row := _STACK + "/RoofMaterials/EndMaterialsRow/"
	_roof_end_face_a_picker = _create_picker(end_row + "EndFaceARow/EndFaceAPicker")
	_roof_end_face_b_picker = _create_picker(end_row + "EndFaceBRow/EndFaceBPicker")
	_roof_end_edges_picker = _create_picker(end_row + "EndEdgesRow/EndEdgesPicker")
	_update_roof_shape_controls()

	# Door config spins
	_door_width_spin = get_node(_STACK + "/DoorConfig/ConfigRow/WidthVBox/WidthSpin")
	_door_height_spin = get_node(_STACK + "/DoorConfig/ConfigRow/HeightVBox/HeightSpin")
	_door_width_spin.value_changed.connect(_on_opening_config_value_changed)
	_door_height_spin.value_changed.connect(_on_opening_config_value_changed)

	# Window config spins
	_win_width_spin = get_node(_STACK + "/WindowConfig/ConfigRow/WidthVBox/WidthSpin")
	_win_height_spin = get_node(_STACK + "/WindowConfig/ConfigRow/HeightVBox/HeightSpin")
	_win_sill_spin = get_node(_STACK + "/WindowConfig/ConfigRow/SillVBox/SillSpin")
	_win_width_spin.value_changed.connect(_on_opening_config_value_changed)
	_win_height_spin.value_changed.connect(_on_opening_config_value_changed)
	_win_sill_spin.value_changed.connect(_on_opening_config_value_changed)

	# Door / Window asset pickers (PackedScene)
	_door_asset_picker = _create_picker(_STACK + "/DoorConfig/AssetRow/AssetPicker", "PackedScene")
	_window_asset_picker = _create_picker(_STACK + "/WindowConfig/AssetRow/AssetPicker", "PackedScene")

	# Fence asset picker (PackedScene) and module length spin
	_fence_asset_picker = _create_picker(_STACK + "/FenceAssets/AssetRow/AssetPicker", "PackedScene")
	_fence_length_spin = get_node(_STACK + "/FenceAssets/LengthRow/LengthSpin")
	_fence_length_spin.value_changed.connect(_on_building_config_value_changed)

	# Bake section
	_bake_path_edit = get_node(_STACK + "/BakeSection/OutputRow/OutputPathEdit")
	_bake_browse_button = get_node(_STACK + "/BakeSection/OutputRow/BrowseButton")
	_bake_lod0_end_spin = get_node(_STACK + "/BakeSection/ParamsRow/Lod0EndVBox/Lod0EndSpin")
	_bake_lod1_begin_spin = get_node(_STACK + "/BakeSection/ParamsRow/Lod1BeginVBox/Lod1BeginSpin")
	_bake_fade_option = get_node(_STACK + "/BakeSection/ParamsRow/FadeVBox/FadeOption")
	_bake_nav_mesh_check = get_node(_STACK + "/BakeSection/ChecksRow/NavMeshCheck")
	_bake_occlusion_check = get_node(_STACK + "/BakeSection/ChecksRow/OcclusionCheck")
	_bake_action_button = get_node(_STACK + "/BakeSection/BakeActionButton")

	_bake_folder_dialog = EditorFileDialog.new()
	_bake_folder_dialog.access = EditorFileDialog.ACCESS_RESOURCES
	_bake_folder_dialog.file_mode = EditorFileDialog.FILE_MODE_OPEN_DIR
	_bake_folder_dialog.title = "Seleccionar carpeta de salida"
	add_child(_bake_folder_dialog)
	_bake_folder_dialog.dir_selected.connect(_on_bake_dir_selected)

	_bake_browse_button.pressed.connect(_on_bake_browse_pressed)
	_bake_action_button.pressed.connect(_on_bake_action_pressed)

	_update_floor_label()
	_update_sections_visibility("none")


func _populate_roof_type_options() -> void:
	_roof_type_option.clear()
	_roof_type_option.add_item("Plano", RoofMeshBuilder.RoofType.FLAT)
	_roof_type_option.add_item("A un agua", RoofMeshBuilder.RoofType.SHED)
	_roof_type_option.add_item("A dos aguas", RoofMeshBuilder.RoofType.GABLE)
	_roof_type_option.add_item("A cuatro aguas", RoofMeshBuilder.RoofType.HIP)


func _populate_roof_direction_options() -> void:
	_roof_direction_option.clear()
	_roof_direction_option.add_item("Norte", RoofMeshBuilder.RoofDirection.NORTH)
	_roof_direction_option.add_item("Sur", RoofMeshBuilder.RoofDirection.SOUTH)
	_roof_direction_option.add_item("Este", RoofMeshBuilder.RoofDirection.EAST)
	_roof_direction_option.add_item("Oeste", RoofMeshBuilder.RoofDirection.WEST)


func _update_roof_shape_controls() -> void:
	var t: RoofMeshBuilder.RoofType = selected_roof_type
	var needs_pitch := t != RoofMeshBuilder.RoofType.FLAT
	# SHED and GABLE are the two types with an orientation, and also the two
	# with vertical end walls. Kept as separate names because those are two
	# different reasons that happen to coincide today.
	var needs_direction := (
		t == RoofMeshBuilder.RoofType.SHED or t == RoofMeshBuilder.RoofType.GABLE
	)
	var has_ends := (
		t == RoofMeshBuilder.RoofType.SHED or t == RoofMeshBuilder.RoofType.GABLE
	)
	_roof_pitch_spin.editable = needs_pitch
	_roof_pitch_label.modulate = Color.WHITE if needs_pitch else Color(1, 1, 1, 0.4)
	_roof_direction_option.disabled = not needs_direction
	_roof_direction_label.modulate = Color.WHITE if needs_direction else Color(1, 1, 1, 0.4)
	# The gable-end materials only exist for SHED and GABLE, so that whole row
	# folds away for the others rather than sitting there greyed out.
	_roof_end_materials_row.visible = has_ends

	# Eave and thickness apply to every type, but what a *sensible* value is
	# doesn't survive the jump between a slab and a pitched roof — see
	# HBRoof.default_eave. So picking a type re-seeds both spins rather than
	# carrying the previous shape's numbers over. This deliberately discards a
	# value the user typed by hand; it happens in front of them, in two spins
	# they can immediately type over, which beats silently building a flat roof
	# with a hip's 0.4 m overhang. Neither spin has a value_changed connection
	# (they are pulled on demand by roof_eave / roof_thickness), so writing
	# them here fires nothing.
	_roof_eave_spin.value = HBRoof.default_eave(t)
	_roof_thickness_spin.value = HBRoof.default_thickness(t)


func _create_picker(container_path: String, base_type: String = "Material") -> EditorResourcePicker:
	var container := get_node(container_path) as HBoxContainer
	if container == null:
		return null

	var picker := EditorResourcePicker.new()
	picker.base_type = base_type
	picker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	container.add_child(picker)
	return picker


func _on_roof_type_selected(_index: int) -> void:
	_update_roof_shape_controls()


func _on_building_config_value_changed(_value: float) -> void:
	building_config_changed.emit()


func _on_opening_config_value_changed(_value: float) -> void:
	opening_config_changed.emit()


func _on_bake_dir_selected(dir: String) -> void:
	_bake_path_edit.text = dir


func _on_bake_browse_pressed() -> void:
	_bake_folder_dialog.popup_centered(Vector2i(800, 600))


func _on_bake_action_pressed() -> void:
	bake_requested.emit()


func _on_mode_selected(mode: String) -> void:
	_status_label.text = (
		"Sin modo activo" if mode == "none"
		else "Modo: %s" % _mode_display_name(mode)
	)
	_update_sections_visibility(mode)
	mode_changed.emit(mode)


func _update_sections_visibility(mode: String) -> void:
	_tile_section.visible = mode == "floor"
	_wall_section.visible = mode == "walls"
	_stair_section.visible = mode == "stairs"
	_roof_section.visible = mode == "roof"
	_fence_section.visible = mode == "fences"
	_door_section.visible = mode == "doors"
	_window_section.visible = mode == "windows"
	_bake_section.visible = mode == "bake"


static func _mode_display_name(mode: String) -> String:
	match mode:
		"floor":
			return "Suelos"
		"walls":
			return "Paredes"
		"roof":
			return "Tejados"
		"doors":
			return "Puertas"
		"windows":
			return "Ventanas"
		"stairs":
			return "Escaleras"
		"fences":
			return "Vallas"
		"bake":
			return "Bakear"
		_:
			return mode


func _on_floor_up() -> void:
	_current_floor += 1
	_update_floor_label()
	floor_changed.emit(_current_floor)


func _on_floor_down() -> void:
	_current_floor -= 1
	_update_floor_label()
	floor_changed.emit(_current_floor)


func _update_floor_label() -> void:
	_floor_label.text = "P %d" % _current_floor
