@tool
class_name FenceBuilder
extends RefCounted

## Width of the user-supplied asset along its local +X. The dock exposes
## this so any asset width works; we use it to space modules edge-to-edge.
static var module_length: float = 1.0

## Per-segment metadata — lets future passes (posts, corners, asset swap)
## reconstruct the segment from the original placement data.
const META_START := "HB_FenceStart"
const META_END := "HB_FenceEnd"
const META_AXIS := "HB_FenceAxis"

## Only segments parallel to X or Z are supported (see _project_to_axis).
enum Axis { NONE, X, Z }

# Untyped: typing it as the plugin class would create a cyclic class_name
# reference between the plugin script and every builder.
var _plugin

var _point_marker: CSGBox3D
var _start = null  # Vector3 while a first point is set, null otherwise


func _init(plugin) -> void:
	_plugin = plugin


# ── Preview ──────────────────────────────────────────────────────────────────

func create_marker(scene: Node3D, floor_base_y: float) -> void:
	_point_marker = PreviewHelper.create_marker(
		scene,
		"__HB_FencePoint__",
		Vector3(0.2, 0.2, 0.2),
		Color(0.2, 0.7, 0.9, 0.9),
		Vector3(0.0, floor_base_y, 0.0)
	)


func clear_preview() -> void:
	PreviewHelper.free_marker(_point_marker)
	_point_marker = null
	_start = null


# ── Input ────────────────────────────────────────────────────────────────────

func handle_input(camera: Camera3D, event: InputEvent, floor_base_y: float) -> int:
	if event is InputEventMouseMotion:
		var pos = RaycastHelper.to_floor_plane(camera, event.position, floor_base_y)
		if pos != null and _point_marker != null and is_instance_valid(_point_marker):
			_point_marker.position = SnapHelper.to_grid_corner(pos, floor_base_y)
		return EditorPlugin.AFTER_GUI_INPUT_PASS

	if event is InputEventMouseButton \
			and event.button_index == MOUSE_BUTTON_LEFT \
			and event.pressed:
		var pos = RaycastHelper.to_floor_plane(camera, event.position, floor_base_y)
		if pos == null:
			return EditorPlugin.AFTER_GUI_INPUT_PASS

		var corner := SnapHelper.to_grid_corner(pos, floor_base_y)

		if _start == null:
			_start = corner
		else:
			var projection := _project_to_axis(_start, corner)
			if projection.axis != Axis.NONE:
				_place_fence(_start, projection.end, projection.axis, floor_base_y)
			_start = null

		return EditorPlugin.AFTER_GUI_INPUT_STOP

	return EditorPlugin.AFTER_GUI_INPUT_PASS


# ── Dominant axis ────────────────────────────────────────────────────────────
#
# Only segments parallel to X or Z are supported. Rather than rejecting
# diagonal clicks, we project the second click onto whichever axis has the
# larger delta. If both deltas are near zero we return Axis.NONE and the
# placement is cancelled silently.

class AxisProjection:
	var end: Vector3
	var axis: Axis

	func _init(p_end: Vector3, p_axis: Axis) -> void:
		end = p_end
		axis = p_axis


static func _project_to_axis(start: Vector3, end: Vector3) -> AxisProjection:
	var dx := end.x - start.x
	var dz := end.z - start.z
	var adx := absf(dx)
	var adz := absf(dz)

	if adx < 0.5 and adz < 0.5:
		return AxisProjection.new(start, Axis.NONE)

	if adx >= adz:
		return AxisProjection.new(Vector3(end.x, start.y, start.z), Axis.X)
	else:
		return AxisProjection.new(Vector3(start.x, start.y, end.z), Axis.Z)


# ── Placement ────────────────────────────────────────────────────────────────

func _place_fence(start: Vector3, end: Vector3, axis: Axis, floor_base_y: float) -> void:
	var dock = _plugin.dock
	var asset_scene: PackedScene = dock.fence_asset_scene if dock != null else null
	if asset_scene == null:
		push_warning("FenceBuilder: no asset selected in the dock.")
		return

	var length := (end - start).length()
	var n_modules := roundi(length / module_length)
	if n_modules <= 0:
		return

	var fence_parent: Node3D = _plugin.get_or_create_parent_node("Fences_%d" % _plugin.active_floor)
	if fence_parent == null:
		return

	# The segment is placed at `start` and rotated so its local +X points
	# toward `end`. HBFence positions each module at local x = (i + 0.5) and
	# owns them as internal children, so the designer sees one clean node.
	var dir := (end - start).normalized()
	var basis_x := dir
	var basis_y := Vector3.UP
	var basis_z := basis_y.cross(basis_x).normalized()

	var segment := HBFence.new()
	segment.name = "HBFence_001"
	segment.position = Vector3(start.x, floor_base_y, start.z)
	segment.basis = Basis(basis_x, basis_y, basis_z)
	segment.set_meta(META_START, start)
	segment.set_meta(META_END, end)
	segment.set_meta(META_AXIS, "X" if axis == Axis.X else "Z")
	segment.module_length = module_length
	segment.module_count = n_modules
	segment.asset_path = asset_scene.resource_path

	# force_readable_name = true so name collisions increment the trailing
	# number (HBFence_002, …) instead of "@Node3D@NN".
	fence_parent.add_child(segment, true)
	segment.owner = fence_parent.owner

	var undo: EditorUndoRedoManager = _plugin.get_undo_redo()
	undo.create_action("Place Fence")
	undo.add_do_method(fence_parent, &"add_child", segment)
	undo.add_undo_method(fence_parent, &"remove_child", segment)
	undo.commit_action(false)
