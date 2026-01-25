class_name PlayerStrike extends State

var player: Player

@export var animation_player: AnimationPlayer

func _init() -> void:
	self.type = StatesRef.PLAYER_STRIKE

func setup(parent_node: Node3D):
	player = parent_node as Player

func enter(_m):
	animation_player.play("strike")
