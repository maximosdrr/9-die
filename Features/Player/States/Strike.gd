class_name PlayerStrike extends State

var player: Player
@export var animation_player: AnimationPlayer

func _init() -> void:
	self.type = StatesRef.PLAYER_STRIKE

func enter(metadata: Dictionary[Variant, Variant]):
	animation_player.play("strike")

func _process(delta: float) -> void:
	pass
