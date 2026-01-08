class_name TableMatchInfluence extends Node

@export var table: Table

func _on_table_match_influence_body_entered(body: Node3D) -> void:
	if body is Player:
		body.set_current_table(table)


func _on_table_match_influence_body_exited(body: Node3D) -> void:
	if body is Player:
		body.remove_current_table()
