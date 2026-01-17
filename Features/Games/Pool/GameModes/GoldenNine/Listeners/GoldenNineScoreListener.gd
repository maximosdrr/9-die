class_name GoldenNineScoreListener extends Node

var turn_resolver: TurnResolver

func setup(_turn_resolver: TurnResolver) -> void:
	turn_resolver = _turn_resolver
	
	if not turn_resolver.pool_game.score_monitor.body_entered\
		.is_connected(_on_ball_touch_score_ground):
		turn_resolver.pool_game.score_monitor\
			.body_entered.connect(_on_ball_touch_score_ground)

func _on_ball_touch_score_ground(body: Node3D):
	if body is Ball:
		turn_resolver.balls_scored[body.index] = body
