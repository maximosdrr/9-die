class_name Aim extends Node3D

@onready var camera_pivot: CameraPivot = $CameraPivot
@onready var camera: Camera3D = $CameraPivot/Camera3D
@onready var poolstick: Poolstick = $CameraPivot/Camera3D/Poolstick

#func _process(_delta):
	#poolstick.position.x = camera_pivot.rotation.y
