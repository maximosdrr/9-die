class_name PoolController extends PlayerGameController

@onready var remote_aim: RemoteTransform3D = $AimPivot/Elevation/RemoteAim
@onready var aim_pivot: AimCameraPivot = $AimPivot
@onready var cue: Cue = $AimPivot/Elevation/Cue

func setup(_parent: Node3D, table_game: TableGame):
	var pool_game = table_game as PoolGame
	
	assert(pool_game != null)
	assert(pool_game is PoolGame)
	
	aim_pivot.setup(pool_game)
	cue.setup(pool_game, aim_pivot)

func take_control():
	show()
	set_process_unhandled_input(true)
	set_process(true)
	
	aim_pivot.set_process(true) 
	aim_pivot.set_process_unhandled_input(true)
	
	Global.camera.set_global_camera_fov(60)
	Global.camera.transition_to(remote_aim)
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)

func give_control():
	hide()
	Global.camera.set_global_camera_fov(75)
	set_process_unhandled_input(false)
	set_process(false)
	
	aim_pivot.set_process(false)
	aim_pivot.set_process_unhandled_input(false)
