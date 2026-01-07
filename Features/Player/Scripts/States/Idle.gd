extends State

class_name PlayerIdleState

@export var toggleable: Toggleable
@export var aim_toggleable: Toggleable
@export var table: Table
@export var camera_manager: CameraManager
	
func _init() -> void:
	self.type = State.Type.IDLE
	
func process(_delta: float) -> void:
	if Input.is_action_just_pressed("aim") and\
	table.table_balls_monitor.balls_are_stopped():
		if aim_toggleable != null:
			toggleable.disable()
			aim_toggleable.enable()
			camera_manager.transition_to(CameraManager.CamState.AIM)
		else:
			push_error("Aim toggleable is null!")
