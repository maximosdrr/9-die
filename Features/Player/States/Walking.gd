class_name PlayerWalking extends State

var player: Player
@export var animation_player: AnimationPlayer

func _init() -> void:
	self.type = StatesRef.PLAYER_WALKING

func setup(parent_node: Node3D):
	player = parent_node as Player

func enter(_m):
	animation_player.play("walk")

func process(_d) -> void:
	if player.velocity.length() <= 0.01:
		state_machine.change_state(StatesRef.PLAYER_IDLE, {})
