class_name PoolController extends PlayerGameController

@export var remote_aim: RemoteTransform3D
@export var aim_pivot: AimCameraPivot
@export var cue: Cue
@export var player: Player

var pool_game: PoolGame

func setup(_table_game: TableGame):
	pool_game = _table_game
	
	aim_pivot.setup(pool_game, self)
	cue.setup(pool_game)
	
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
	player.state_machine.change_state(StatesRef.PLAYER_READY_TO_STRIKE, {})
	set_process_unhandled_input(true)
	set_process(true)
	
	aim_pivot.set_process(true) 
	aim_pivot.set_process_unhandled_input(true)
	
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
	aim_pivot.set_process_unhandled_input(false)
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
