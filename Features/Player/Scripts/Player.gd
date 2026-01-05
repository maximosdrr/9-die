class_name Player extends CharacterBody3D

var gravity = 12
var speed = 3

@export var camera_pivot: CameraPivot
@export var model: Node3D
@export var player_camera: Camera3D
@export var table: Table
@export var poolstick: Poolstick


@onready var state_machine: StateMachine = $StateMachine

func _physics_process(delta: float) -> void:
	if not is_on_floor():
		velocity.y -= gravity * delta
	else:
		velocity.y = 0

	var input_dir := Input.get_vector( "move_right", "move_left", "move_backward", "move_forward")

	var _basis := camera_pivot.basis.orthonormalized()
	var direction := (_basis * Vector3(-input_dir.x, 0, -input_dir.y)).normalized()
	
	if direction != Vector3.ZERO:
		velocity.x = direction.x * speed
		velocity.z = direction.z * speed
	else:
		velocity.x = move_toward(velocity.x, 0, speed)
		velocity.z = move_toward(velocity.z, 0, speed)

	move_and_slide()
