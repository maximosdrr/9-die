extends CharacterBody3D


const JUMP_VELOCITY = 4.5
var gravity = 12
var speed = 1

@export var camera: Camera3D
@export var camera_pivot: CameraPivot
@export var cue: RayCast3D
@export var model: Node3D
@export var cue_strength := 1.0

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

func _process(_delta: float):
	model.global_rotation.y = camera_pivot.global_rotation.y + camera_pivot.BASE_YAW
	_strike_ball()

func _get_cue_direction() -> Vector3:
	var origin: Vector3 = cue.global_position
	var target: Vector3 = cue.to_global(cue.target_position)

	var dir: Vector3 = (target - origin).normalized()
	return dir

func _strike_ball():
	if Input.is_action_just_pressed("strike"):
		if cue.is_colliding():
			var collider = cue.get_collider() as Node3D
			if collider.is_in_group(Groups.BALL):
				var ball = collider as Ball
				var cue_direction = _get_cue_direction()
				#PERFECT FRONTAL COLLISION
				#var test = Vector3(-0.001102, -0.301967, 0.953318)
				ball.strike(cue_direction, cue_strength)
