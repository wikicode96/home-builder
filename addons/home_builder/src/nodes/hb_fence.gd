@tool
class_name HBFence
extends Node3D

## Self-contained fence segment. Regenerates its module instances (copies of a
## user-supplied asset scene) as INTERNAL children from the stored asset path
## and module count, so the designer only ever sees one "HBFence_001" node.
##
## Modules are laid edge-to-edge along the node's local +X; the node's own
## transform places and orients the whole run. Because the modules are rebuilt
## from `asset_path`, the .tscn only stores the description, not the copies.

## res:// path of the module scene. Empty → nothing is built.
@export_file("*.tscn", "*.scn") var asset_path: String = "":
	set(value):
		asset_path = value
		_rebuild()
@export var module_count: int = 0:
	set(value):
		module_count = value
		_rebuild()
@export var module_length: float = 1.0:
	set(value):
		module_length = value
		_rebuild()


func _ready() -> void:
	# "_edit_group_" is the same metadata the Scene dock's Group toggle sets.
	# It makes the 3D viewport escalate a click anywhere on our internal
	# module instances up to this node, so the fence segment — not one of its
	# hidden modules — ends up selected (and shown in the Inspector).
	if Engine.is_editor_hint():
		set_meta("_edit_group_", true)
	_rebuild()


func _rebuild() -> void:
	if not is_inside_tree():
		return

	for child in get_children(true):
		remove_child(child)
		child.free()

	if asset_path.is_empty() or module_count <= 0:
		return

	var scene := load(asset_path) as PackedScene
	if scene == null:
		push_warning("[HBFence] Could not load asset: %s" % asset_path)
		return

	for i in module_count:
		var module := scene.instantiate() as Node3D
		if module == null:
			continue
		module.position = Vector3((i + 0.5) * module_length, 0.0, 0.0)
		add_child(module, false, Node.INTERNAL_MODE_BACK)

		# Owner is required for the module root to register a gizmo at all
		# (unowned nodes are invisible to the viewport's click-picking);
		# "_edit_group_" on this node (see _ready) then escalates the
		# resulting click up to us.
		if Engine.is_editor_hint() and is_inside_tree():
			var root := get_tree().edited_scene_root
			if root:
				module.owner = root
