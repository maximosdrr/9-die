class_name CueLockedState extends State

var cue: Cue

func _init():
	type = StatesRef.CUE_LOCKED

func setup(parent_node: Node3D):
	cue = parent_node as Cue

func enter(_m):
	cue.position.z = cue.ball_radius_offset
	_apply_spin_to_pose()

func process(_delta):
	cue.position.z = cue.ball_radius_offset
	_apply_spin_to_pose()


func _apply_spin_to_pose():
	cue.position.x = cue.spin_offset.x
	cue.position.y = cue.spin_offset.y
