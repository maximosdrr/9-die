class_name PoolController extends PlayerGameController

@onready var remote_aim: RemoteTransform3D = $AimPivot/Elevation/RemoteAim
@onready var aim_pivot: AimCameraPivot = $AimPivot
@onready var cue: Cue = $AimPivot/Elevation/Cue

func setup(_parent: Node3D, table_game: TableGame):
	var pool_game = table_game as PoolGame
	
	assert(pool_game != null)
	assert(pool_game is PoolGame)
	
	aim_pivot.set_target_ball(pool_game.cue_ball)
	cue.set_cue_ball(pool_game.cue_ball)

func take_control():
	show()
	set_process_unhandled_input(true)
	set_process(true)
	
	aim_pivot.set_process(true) 
	aim_pivot.set_process_unhandled_input(true)
	
	Global.camera.transition_to(remote_aim)
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)

func give_control():
	hide()
	
	set_process_unhandled_input(false)
	set_process(false)
	
	aim_pivot.set_process(false)
	aim_pivot.set_process_unhandled_input(false)
