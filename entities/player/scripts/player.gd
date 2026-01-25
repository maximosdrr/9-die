class_name Player extends CharacterBody3D

var gravity = 12
var speed = 3
var id = 1

@onready var head_pivot: HeadPivot = $FirstPerson/HeadPivot
@onready var remote_fps: RemoteTransform3D = $FirstPerson/HeadPivot/RemoteFPS
@onready var player_model: Node3D = $FirstPerson/Model3D
@onready var state_machine: StateMachine = $StateMachine
@onready var debug_label: Label3D = $DebugLabel
@onready var table_detector: Area3D = $TableDetector

var table: Table

func _ready() -> void:
	if is_multiplayer_authority():
		Global.camera.transition_to(remote_fps)

	add_to_group(Groups.PLAYER)

func _physics_process(delta: float) -> void:
	if not is_multiplayer_authority():
		return
	
	_update_debug_label()
	
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

func _update_debug_label():
	var table_name = 'Null' if table == null else str(table.name)
	var text = "Current table: %s"  % [table_name]
	debug_label.text = text
