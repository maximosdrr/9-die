class_name PoolScoreManager extends Node

var cue_ball: Ball

func _ready() -> void:
	pass

func setup(_cue_ball):
	cue_ball = _cue_ball


func _on_score_monitor_body_entered(body: Node3D) -> void:
	if body is Ball:
		print("Body hits the score ground: ", body.name)
