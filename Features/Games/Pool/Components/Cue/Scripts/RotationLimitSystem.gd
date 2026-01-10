class_name CueRotationLimitSystem extends Node

var cue_back_area: Area3D
var aim_pivot: AimCameraPivot

func setup(_cue_back_area: Area3D, _aim_pivot: AimCameraPivot):
	cue_back_area = _cue_back_area
	aim_pivot = _aim_pivot
	
	if not cue_back_area.body_entered.is_connected(_on_table_touch_cue):
		cue_back_area.body_entered.connect(_on_table_touch_cue)
	if not cue_back_area.body_exited.is_connected(_on_table_stop_touching_cue):
		cue_back_area.body_exited.connect(_on_table_stop_touching_cue)

func _on_table_touch_cue(_body: Node3D):
	print(_body)
	#aim_pivot.reduce_down_rotation(-12)

func _on_table_stop_touching_cue(_body: Node3D):
	print(_body)
	#aim_pivot.reset_down_rotation()
	
