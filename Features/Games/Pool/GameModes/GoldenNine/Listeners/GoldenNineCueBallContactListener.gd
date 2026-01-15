class_name GoldenNineCueBallContactListener extends Node

var turn_resolver: GoldenNineTurnResolver

func setup(_turn_resolver: GoldenNineTurnResolver):
	turn_resolver = _turn_resolver

func _on_cue_ball_contact(ball: Ball):
	if turn_resolver.first_ball_hit == null:
		turn_resolver.first_ball_hit = ball

func start_listening_collisions():
	if not turn_resolver.cue_ball.ball_contacted.is_connected(_on_cue_ball_contact):
		turn_resolver.cue_ball.ball_contacted.connect(_on_cue_ball_contact)

func stop_listening_collisions():
	if turn_resolver.cue_ball.ball_contacted.is_connected(_on_cue_ball_contact):
		turn_resolver.cue_ball.ball_contacted.disconnect(_on_cue_ball_contact)
