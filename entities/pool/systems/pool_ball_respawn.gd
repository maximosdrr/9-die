class_name BallHolder extends Node3D

const BALL_SCENE = preload("uid://cqwu27wd0ddmr")

@export var head_spot: Vector3 = Vector3(0, 0.17, -0.5)
@export var foot_spot: Vector3 = Vector3(0, 0.17, 0.5)
@export var balls_quantity: int = 9
@export var ball_diameter: float = 0.032

var cue_ball: Ball
var balls: Array[Ball] = []

signal table_ready

func spawn_balls():
	_request_server_start_game.rpc_id(MultiplayerPeer.TARGET_PEER_SERVER)

func get_cue_ball():
	if cue_ball != null: return cue_ball

	for node in get_children():
		if not node is Ball: continue
		if node.index != 0: continue
		cue_ball = node
	
	return cue_ball

func get_normal_balls():
	if balls.size() > 0: return balls
	
	for node in get_children():
		if not node is Ball: continue
		if node.index == 0: continue
		balls.append(node)
	
	return balls

@rpc('any_peer', 'call_local', 'reliable')
func _request_server_start_game() -> void:
	if multiplayer.is_server():
		clear_table()
		_spawn_cue_ball()
		_spawn_triangle()
		notify_table_ready.rpc()

@rpc('authority', 'call_local', 'reliable')
func notify_table_ready() -> void:
	table_ready.emit()

func clear_table() -> void:
	for child in get_children():
		child.queue_free()
	
	cue_ball = null
	balls.clear()

func _spawn_cue_ball() -> void:
	var ball: Ball = BALL_SCENE.instantiate()
	ball.position = head_spot
	ball.texture_id = 0
	ball.name = "CueBall"
	add_child(ball, true)

func _spawn_triangle() -> void:
	var index := 0
	var rows := 5

	for row in range(rows):
		var z_offset := row * (ball_diameter * 0.866)
		var start_x := -(row * ball_diameter) / 2.0

		for col in range(row + 1):
			if index >= balls_quantity:
				return

			var x_pos := start_x + (col * ball_diameter)
			var pos := Vector3(x_pos, foot_spot.y, foot_spot.z + z_offset)

			_create_colored_ball(index, pos)
			index += 1

func _create_colored_ball(index: int, pos: Vector3) -> void:
	var ball: Ball = BALL_SCENE.instantiate()
	ball.position = pos

	ball.texture_id = index + 1
	ball.index = index + 1
	ball.name = "Ball_%s" % str(index + 1)

	add_child(ball, true)
