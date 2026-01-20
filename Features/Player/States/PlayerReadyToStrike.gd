class_name PlayerReadyToStrike extends State

@export var animation_player: AnimationPlayer

func _init() -> void:
	self.type = StatesRef.PLAYER_READY_TO_STRIKE

func enter(_m):
	animation_player.play("ready_to_strike")
