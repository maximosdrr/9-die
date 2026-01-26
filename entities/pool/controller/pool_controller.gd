class_name PoolController extends TableGameController

@onready var remote_aim: RemoteTransform3D = $AimPivot/Elevation/RemoteAim
@onready var aim_pivot: AimCameraPivot = $AimPivot
@onready var cue: Cue = $AimPivot/Cue
@export var pool_game: PoolGame

func _ready() -> void:
	process_mode = Node.PROCESS_MODE_DISABLED

func setup() -> void:
	aim_pivot.setup(pool_game, self)
	cue.setup(pool_game, aim_pivot)
	
func take_control():
	if not is_multiplayer_authority():
		return
	
	process_mode = Node.PROCESS_MODE_INHERIT
	show()

	Global.camera.set_global_camera_fov(60)
	Global.camera.transition_to(remote_aim)
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)

func drop_control():
	if not is_multiplayer_authority():
		return
	
	process_mode = Node.PROCESS_MODE_DISABLED
	hide()
	
	Global.camera.set_global_camera_fov(75)
