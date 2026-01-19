class_name Player extends CharacterBody3D

var gravity = 12
var speed = 3
var id = 1

@onready var head_pivot: HeadPivot = $FirstPerson/HeadPivot
@onready var remote_fps: RemoteTransform3D = $FirstPerson/HeadPivot/RemoteFPS
@onready var player_toggleable: Toggleable = $Scripts/PlayerToggleable
@onready var game_handler: PlayerGameHandler = $Scripts/PlayerGameHandler
@onready var player_model: Node3D = $FirstPerson/Model3D

enum ControllerStates { Player, Game }

var current_control_state = ControllerStates.Player

func _ready():
	if !is_multiplayer_authority():
		return
	take_control()

func take_control():
	if !is_multiplayer_authority():
		return
	
	player_model.show()
	set_physics_process(true)
	head_pivot.set_process_unhandled_input(true) 
	
	Global.camera.transition_to(remote_fps)
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	current_control_state = ControllerStates.Player

func give_control():
	if !is_multiplayer_authority():
		return

	player_model.hide() 
	set_physics_process(false)
	velocity = Vector3.ZERO
	
	head_pivot.set_process_unhandled_input(false)
	current_control_state = ControllerStates.Game

func _physics_process(delta: float) -> void:
	if !is_multiplayer_authority():
		return

	if not is_on_floor():
		velocity.y -= gravity * delta
	else:
		velocity.y = 0
	
	var input_dir := Input.get_vector("move_left", "move_right", "move_forward", "move_backward")
	var direction = (transform.basis * Vector3(input_dir.x, 0, input_dir.y)).normalized()
	
	if direction != Vector3.ZERO:
		velocity.x = direction.x * speed
		velocity.z = direction.z * speed
	else:
		velocity.x = move_toward(velocity.x, 0, speed)
		velocity.z = move_toward(velocity.z, 0, speed)

	move_and_slide()
