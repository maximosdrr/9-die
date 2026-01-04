class_name Ball
extends RigidBody3D

@export var data: BallResource

func _ready() -> void:
	if data == null:
		push_warning("BallPhysics has no Ball resource assigned.")
		return

	if data.model != null:
		add_child(data.model.instantiate())

	mass = data.mass
	gravity_scale = data.gravity_scale
	linear_damp = data.linear_damp
	angular_damp = data.angular_damp
	continuous_cd = data.continous_cd
	can_sleep = data.can_sleep

	add_to_group(Groups.BALL)

func _integrate_forces(state: PhysicsDirectBodyState3D) -> void:
	_apply_table_friction(state)

func _apply_table_friction(state: PhysicsDirectBodyState3D) -> void:
	if state.get_contact_count() == 0:
		return

	var v := state.linear_velocity
	var v_xz := Vector3(v.x, 0.0, v.z)
	var speed := v_xz.length()

	if speed < data.stop_speed:
		state.linear_velocity = Vector3(0.0, v.y, 0.0)
		return

	var decel := data.roll_drag * state.step
	var new_speed = max(0.0, speed - decel)
	var new_v_xz = v_xz.normalized() * new_speed

	state.linear_velocity = Vector3(new_v_xz.x, v.y, new_v_xz.z)

func strike(direction: Vector3, power: float, hit_offset: Vector3 = Vector3.ZERO) -> void:
	var dir := Vector3(direction.x, 0.0, direction.z)
	if dir.length_squared() < 0.0001 or power <= 0.0:
		return

	var impulse := dir.normalized() * power
	apply_impulse(impulse, hit_offset)
