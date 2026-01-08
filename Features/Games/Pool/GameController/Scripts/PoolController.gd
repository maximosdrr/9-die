class_name PoolController extends GameController

@onready var remote_aim: RemoteTransform3D = $AimPivot/Elevation/RemoteAim
@onready var aim_pivot: AimCameraPivot = $AimPivot
@onready var cue: Cue = $AimPivot/Elevation/Cue
@onready var pool_controller: PoolController = $"."

func setup(_parent: Node3D, ...args):
	var parameters = args[0]
	var pool_game = parameters[0] as PoolGame
	
	assert(pool_game != null)
	
	aim_pivot.set_target_ball(pool_game.cue_ball)
	cue.set_cue_ball(pool_game.cue_ball)

func take_control():
	pool_controller.show()
	set_process_unhandled_input(true)
	set_process(true)
	
	aim_pivot.set_process(true) 
	aim_pivot.set_process_unhandled_input(true)
	
	cue.visible = true
	
	Global.camera.transition_to(remote_aim)
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)

func give_control():
	pool_controller.hide()
	
	set_process_unhandled_input(false)
	set_process(false)
	
	aim_pivot.set_process(false)
	aim_pivot.set_process_unhandled_input(false)
