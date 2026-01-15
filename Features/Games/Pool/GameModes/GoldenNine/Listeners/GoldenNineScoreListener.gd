class_name GoldenNineScoreListener extends Node

var turn_resolver: TurnResolver

func setup(_turn_resolver: TurnResolver) -> void:
	turn_resolver = _turn_resolver
	
	turn_resolver.pool_game.score_monitor\
		.body_entered.connect(_on_ball_touch_score_ground)

func _on_ball_touch_score_ground(body: Node3D):
	print("Body", body.name)
	if body is Ball:
		print("Body: ", body.name)
		turn_resolver.balls_scored[body.index] = body
