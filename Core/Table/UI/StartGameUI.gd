class_name PoolStartGameUI extends Node3D

var can_be_show: bool = true

@export var rotation_speed: float = 0.5
@onready var label: Label3D = $Label3D

func _process(delta: float) -> void:
	label.rotate_y(rotation_speed * delta)

func set_text(text):
	label.text = text
