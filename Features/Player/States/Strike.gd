class_name PlayerStrike extends State

var player: Player
var pool_controller: PoolController
var cue: Cue
var spine_idx = null

@export var animation_player: AnimationPlayer

func _init() -> void:
	self.type = StatesRef.PLAYER_STRIKE

func setup(parent_node: Node3D):
	player = parent_node as Player
	

func exit(_m):
	player.skeleton.reset_bone_pose(spine_idx)

func enter(_m):
	animation_player.play("strike")
	assert(player.game_handler.current_controller is PoolController)
	
	if pool_controller != null: return
	
	pool_controller = player.game_handler.current_controller
	cue = pool_controller.cue
	spine_idx = player.skeleton.find_bone("DEF-spine.002")

func process(_delta: float) -> void:
	if is_multiplayer_authority():
		_bend(cue.min_safe_angle)

func _bend(value: float) -> void:
	_apply_bend_local(value)
	_send_bend_rpc.rpc(value)

func _apply_bend_local(value: float) -> void:
	animation_player.stop()
		
	var angle = deg_to_rad(value)
	var angle_left = deg_to_rad(14.4)

	var rot_right = Quaternion(Vector3.RIGHT, angle)
	var rot_forward = Quaternion(Vector3.FORWARD, angle_left)

	var final_rot = rot_right * rot_forward
	
	player.skeleton.set_bone_pose_rotation(
		spine_idx, 
		final_rot, 
	)

@rpc("any_peer", "call_remote", "unreliable")
func _send_bend_rpc(value: float) -> void:
	if is_multiplayer_authority():
		return
	
	_apply_bend_local(value)
