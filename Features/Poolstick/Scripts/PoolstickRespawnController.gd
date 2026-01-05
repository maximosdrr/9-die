class_name PoolstickRespawnController extends Node

@export var poolstick_scene: PackedScene
var current_poolstick: Node3D

func respawn_at_marker(respawn_pos: Marker3D) -> void:
	if respawn_pos == null:
		push_warning("respawn_at_marker: marker is null")
		return
	if poolstick_scene == null:
		push_warning("respawn_at_marker: poolstick_scene is null")
		return

	if is_instance_valid(current_poolstick):
		current_poolstick.queue_free()
		current_poolstick = null

	current_poolstick = poolstick_scene.instantiate() as Node3D

	respawn_pos.add_child(current_poolstick)

	current_poolstick.rotation_degrees = Vector3(-100, 0, 0)
	current_poolstick.position = Vector3(0, 0.25, 1.3)
