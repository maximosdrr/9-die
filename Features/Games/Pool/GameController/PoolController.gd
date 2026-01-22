class_name PoolController extends PlayerGameController

@onready var remote_aim: RemoteTransform3D = $AimPivot/Elevation/RemoteAim
@onready var aim_pivot: AimCameraPivot = $AimPivot
@onready var cue: Cue = $AimPivot/Cue

var pool_game: PoolGame
var player: Player

func setup(_parent: Player, table_game: TableGame):
	player = _parent
	pool_game = table_game as PoolGame
	
	assert(pool_game != null)
	assert(pool_game is PoolGame)
	
	aim_pivot.setup(pool_game, self)
	cue.setup(pool_game, aim_pivot)
	
	pool_game.turn_changed.connect(_on_turn_change)

func take_control():
	if not is_multiplayer_authority():
		return

	if pool_game.turn_owner == null:
		push_error("Game not started yet! Table.turn_owner is null")
		return
#
	if player.name != pool_game.turn_owner.name:
		push_error("Cannot take control, it's not your turn!")
		return
	
	show()
	set_process_unhandled_input(true)
	set_process(true)
	
	aim_pivot.set_process(true) 
	aim_pivot.set_physics_process(true)
	aim_pivot.set_process_unhandled_input(true)

	player.player_model.hide()
	player.state_machine.change_state(StatesRef.PLAYER_STRIKE, {})
	
	Global.camera.set_global_camera_fov(60)
	Global.camera.transition_to(remote_aim)
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)

func give_control():
	if not is_multiplayer_authority():
		return

	hide()
	Global.camera.set_global_camera_fov(75)
	set_process_unhandled_input(false)
	set_process(false)
	
	aim_pivot.set_process(false)
	aim_pivot.set_physics_process(false)
	aim_pivot.set_process_unhandled_input(false)
	
	player.player_model.show()
	player.state_machine.change_state(StatesRef.PLAYER_IDLE, {})

func apply_control(turn_owner_id: String, context: Dictionary):
	if turn_owner_id == player.name:
		can_take_control = true
		player.give_control()
		if not context.has("ball_replacement"):
			take_control()
		else:
			await pool_game.ball_placement_manager.placement_finished
			await get_tree().create_timer(1).timeout
			take_control()
	else:
		can_take_control = false
		give_control()
		player.take_control()

func _on_turn_change(next_player_name: String, context: Dictionary):
	apply_control(next_player_name, context)
