class_name GoldenNineBallFellOffListener extends Node

var turn_resolver: GoldenNineTurnResolver

func setup(_turn_resolver: GoldenNineTurnResolver):
	turn_resolver = _turn_resolver
	turn_resolver.pool_game.off_table_monitor\
		.ball_fell_off.connect(_on_ball_fell_off)

func _on_ball_fell_off(ball: Ball):
	if not turn_resolver.balls_off_table_list.has(ball):
		turn_resolver.balls_off_table_list.append(ball)
