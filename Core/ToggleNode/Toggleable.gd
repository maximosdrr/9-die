class_name Toggleable
extends Node

enum InitialState { ENABLED, DISABLED }

@export_group("Setup")
@export var initial_state: InitialState = InitialState.ENABLED
@export var targets: Array[NodePath] = []

@export_group("What to toggle")
@export var toggle_process := true
@export var toggle_physics := true
@export var toggle_visible := false
@export var disable_children := true
@export var disable_cameras := false

func _ready() -> void:
	if initial_state == InitialState.DISABLED:
		disable()
	else:
		enable()

func disable() -> void:
	_apply(false)

func enable() -> void:
	_apply(true)

func _apply(on: bool) -> void:
	for path in targets:
		var root := get_node_or_null(path)
		if root == null:
			continue

		_apply_to_node(root, on)

		if disable_children:
			for child in _get_all_descendants(root):
				_apply_to_node(child, on)

func _apply_to_node(n: Node, on: bool) -> void:
	if toggle_process:
		n.set_process(on)

	if toggle_physics:
		n.set_physics_process(on)

	if toggle_visible:
		if n is CanvasItem:
			(n as CanvasItem).visible = on
		elif n is Node3D:
			(n as Node3D).visible = on

	if disable_cameras and n is Camera3D:
		(n as Camera3D).current = on

func _get_all_descendants(root: Node) -> Array[Node]:
	var out: Array[Node] = []
	var stack: Array[Node] = [root]

	while stack.size() > 0:
		var node = stack.pop_back()
		for c in node.get_children():
			if c is Node:
				out.append(c)
				stack.append(c)
	return out
