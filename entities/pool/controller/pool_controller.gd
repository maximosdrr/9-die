class_name PoolController extends TableGameController

@onready var remote_aim: RemoteTransform3D = $AimPivot/Elevation/RemoteAim
@onready var aim_pivot: AimCameraPivot = $AimPivot
@onready var cue: Cue = $AimPivot/Cue

@export var pool_game: PoolGame

var can_take_control = false

func _ready() -> void:
	pass
	aim_pivot.setup(pool_game, self)
	cue.setup(pool_game, aim_pivot)
	
func take_control():
	if not is_multiplayer_authority():
		return

	show()
	set_process_unhandled_input(true)
	set_process(true)
	
	aim_pivot.set_process(true) 
	aim_pivot.set_physics_process(true)
	aim_pivot.set_process_unhandled_input(true)

	Global.camera.set_global_camera_fov(60)
	Global.camera.transition_to(remote_aim)
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)

func drop_control():
	if not is_multiplayer_authority():
		return

	hide()
	Global.camera.set_global_camera_fov(75)
	set_process_unhandled_input(false)
	set_process(false)
	
	aim_pivot.set_process(false)
	aim_pivot.set_physics_process(false)
	aim_pivot.set_process_unhandled_input(false)
