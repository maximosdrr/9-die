class_name PoolBallRespawn extends Node

signal table_ready(cue_ball: Ball, balls: Array[Ball])

const BALL_SCENE = preload("uid://cqwu27wd0ddmr")

@export var head_spot: Vector3 = Vector3(0, 0.17, -0.5)
@export var foot_spot: Vector3 = Vector3(0, 0.17, 0.5)
@export var balls_quantity: int = 9
@export var balls_holder: Node3D

var ball_diameter: float = 0.032
var balls: Array[Ball] = []
var cue_ball: Ball = null

var _ready_emitted := false

func _ready() -> void:
	if not balls_holder:
		return
	balls_holder.child_entered_tree.connect(_on_holder_changed)
	balls_holder.child_exiting_tree.connect(_on_holder_changed)

func start_game() -> void:
	_reset_ready_state()

	if multiplayer.is_server():
		clear_table()
		_spawn_cue_ball()
		_spawn_triangle()

	_refresh_and_maybe_emit()

func clear_table() -> void:
	if not balls_holder:
		return
	for child in balls_holder.get_children():
		child.queue_free()

func _reset_ready_state() -> void:
	_ready_emitted = false
	cue_ball = null
	balls.clear()

func _on_holder_changed(_child: Node = null) -> void:
	_refresh_and_maybe_emit()

func _refresh_and_maybe_emit() -> void:
	if _ready_emitted or not balls_holder:
		return

	var found_cue: Ball = null
	var found_balls: Array[Ball] = []

	for child in balls_holder.get_children():
		if child is not Ball:
			continue

		var b := child as Ball
		if b.texture_id == 0:
			found_cue = b
		else:
			found_balls.append(b)

	if found_cue == null or found_balls.size() < balls_quantity:
		return

	cue_ball = found_cue
	balls = found_balls

	_ready_emitted = true
	table_ready.emit(cue_ball, balls)

func wait_table_ready() -> Array:
	if _ready_emitted and cue_ball != null and balls.size() >= balls_quantity:
		return [cue_ball, balls]

	return await table_ready

func _spawn_cue_ball() -> void:
	var ball: Ball = BALL_SCENE.instantiate()
	ball.position = head_spot
	ball.texture_id = 0
	ball.name = "CueBall"
	balls_holder.add_child(ball, true)

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

	balls_holder.add_child(ball, true)
