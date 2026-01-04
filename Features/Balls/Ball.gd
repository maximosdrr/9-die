class_name Ball extends RigidBody3D

@export var data: BallResource

func _ready() -> void:
	if data == null:
		push_warning("BallPhysics has no Ball resource assigned.")
		return
	
	if data.model != null:
		_load_model()
	
	mass = data.mass
	gravity_scale = data.gravity_scale
	linear_damp = data.linear_damp
	angular_damp = data.angular_damp
	continuous_cd = data.continous_cd
	can_sleep = data.can_sleep
	
	add_to_group(Groups.BALL)

func _load_model():
	add_child(data.model.instantiate())

func _physics_process(delta: float) -> void:
	_apply_roll_drag(delta)
	_clamp_speed()

func _apply_roll_drag(delta: float) -> void:
	var speed := linear_velocity.length()

	if speed < data.stop_speed:
		linear_velocity = Vector3.ZERO
		angular_velocity = Vector3.ZERO
		return

	var drag := data.roll_drag * delta
	linear_velocity -= linear_velocity.normalized() * drag

func _clamp_speed() -> void:
	if linear_velocity.length() > data.max_speed:
		linear_velocity = linear_velocity.normalized() * data.max_speed

func strike(direction: Vector3, power: float, hit_offset: Vector3 = Vector3.ZERO) -> void:
	if direction.length_squared() < 0.0001 or power <= 0.0:
		return

	var impulse := direction.normalized() * power
	apply_impulse(impulse, hit_offset)
