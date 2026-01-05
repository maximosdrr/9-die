class_name Poolstick extends Node3D

@export var poolstick_camera: Camera3D
@export var camera_pivot: CameraPivot


func add_camera_target(ball: Ball) -> void:
	camera_pivot.set_rotation_target(ball)

func remove_camera_target() -> void:
	camera_pivot.clear_rotation_target()

func set_poolstick_camera_current(value: bool) -> void:
	poolstick_camera.current = value
