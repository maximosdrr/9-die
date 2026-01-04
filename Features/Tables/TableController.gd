@tool

class_name TableController extends Node

@export var table_resource: Table:
	get: return _table_resource
	set(value):
		_table_resource = value
		if Engine.is_editor_hint():
			call_deferred("spawn_table")
@export var model_parent: Node3D

var current_model: Node3D
var _table_resource: Table

func spawn_table():
	if table_resource:
		spawn_table()
	
	if table_resource.model:
		current_model = table_resource.model.instantiate()
		model_parent.add_child(current_model)
		
		current_model.position = table_resource.position
		current_model.rotation_degrees = table_resource.rotation
		current_model.scale = table_resource.scale
