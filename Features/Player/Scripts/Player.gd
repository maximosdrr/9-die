class_name Player extends CharacterBody3D

var gravity = 12
var speed = 3

@onready var head_pivot: HeadPivot = $FirstPerson/HeadPivot

@onready var remote_fps: RemoteTransform3D = $FirstPerson/HeadPivot/RemoteFps
@onready var remote_aim: RemoteTransform3D = $Aim/AimPivot/Elevation/RemoteAim

@onready var aim_toggleable: Toggleable = $Components/AimToggleable
@onready var player_toggleable: Toggleable = $Components/PlayerToggleable

@onready var poolstick: Poolstick = $Aim/AimPivot/Elevation/Poolstick
@onready var aim_pivot: AimCameraPivot = $Aim/AimPivot

@export_group("External References")
@export var global_camera: GlobalCamera

var current_table: Table = null:
	get():
		if current_table == null:
			push_error("Current table is null!")
		return current_table

func _ready():
	remote_fps.remote_path = global_camera.get_path()
	remote_aim.remote_path = global_camera.get_path()

func _physics_process(delta: float) -> void:
	if not is_on_floor():
		velocity.y -= gravity * delta
	else:
		velocity.y = 0

	var input_dir := Input.get_vector("move_right", "move_left",  "move_backward", "move_forward")

	var _basis := head_pivot.basis.orthonormalized()
	var direction := (_basis * Vector3(-input_dir.x, 0, -input_dir.y)).normalized()
	
	if direction != Vector3.ZERO:
		velocity.x = direction.x * speed
		velocity.z = direction.z * speed
	else:
		velocity.x = move_toward(velocity.x, 0, speed)
		velocity.z = move_toward(velocity.z, 0, speed)

	move_and_slide()

func set_current_table(table: Table):
	current_table = table
	poolstick.set_cue_ball(table.cue_ball)
	aim_pivot.set_target_ball(table.cue_ball)

func remove_current_table():
	current_table = null
	poolstick.set_cue_ball(null)
	aim_pivot.set_target_ball(null)
