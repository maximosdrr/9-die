class_name PoolBallRespawn extends Node

const BALL_SCENE = preload("uid://cqwu27wd0ddmr")

@export var head_spot: Vector3 = Vector3(0, 0.17, -0.5)
@export var foot_spot: Vector3 = Vector3(0, 0.17, 0.5)
@export var balls_quantity: int = 9
@export var balls_holder: Node3D

var ball_diameter: float = 0.029 
var balls: Array[Ball] = []
var cue_ball: Ball = null

func start_game() -> void:
	if not multiplayer.is_server():
		return

	_spawn_cue_ball()
	_spawn_triangle()

func clear_table() -> void:
	for child in balls_holder.get_children():
		child.queue_free()

func _spawn_cue_ball() -> void:
	var ball = BALL_SCENE.instantiate()
	ball.name = "CueBall"
	ball.position = head_spot
	
	ball.texture_id = 0 
	
	balls_holder.add_child(ball, true)
	cue_ball = ball

func _spawn_triangle() -> void:
	var index = 0
	var rows = 5
	
	for row in range(rows):
		var z_offset = row * (ball_diameter * 0.866)
		var start_x = -(row * ball_diameter) / 2.0
		
		for col in range(row + 1):
			if index >= balls_quantity: break
			
			var x_pos = start_x + (col * ball_diameter)
			var pos = Vector3(x_pos, foot_spot.y, foot_spot.z + z_offset)
			
			_create_colored_ball(index, pos)
			
			index += 1

func _create_colored_ball(index: int, pos: Vector3) -> void:
	var ball: Ball = BALL_SCENE.instantiate()
	ball.name = "Ball_" + str(index + 1)
	ball.position = pos
	
	ball.texture_id = index + 1 
	ball.index = index + 1
	balls_holder.add_child(ball, true)
	balls.append(ball)
