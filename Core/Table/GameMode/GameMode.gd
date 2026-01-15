class_name GameMode extends Node

var turn_ruler: TurnRuler
var turn_resolver: TurnResolver

func _ready() -> void:
	for node in get_children():
		if node is TurnRuler:
			turn_ruler = node
		elif node is TurnResolver:
			turn_resolver = node
		else:
			push_error("Game Mode should only have children of type TurnRuler or TurnResolver")	
