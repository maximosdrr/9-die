class_name AimIdleState extends State

@export var toggleable: Toggleable
@export var player_toggleable: Toggleable
@export var table: Table

@export var camera_manager: CameraManager


func _init() -> void:
	self.type = State.Type.IDLE

	
func process(_delta: float) -> void:
	if Input.is_action_just_pressed("aim"):
		if player_toggleable != null:
			toggleable.disable()
			player_toggleable.enable()
			camera_manager.transition_to(CameraManager.CamState.FPS)
		else:
			push_error("Player toggleable is null!")
