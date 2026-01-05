class_name Ball
extends RigidBody3D

@onready var poolstick_respawn_marker: Marker3D = $PoolstickRespawnPosition

@export var data: BallResource
@export var is_white_ball: bool = false
@onready var label_3d: Label3D = $Label3D

func _update_label():
	var message = "Ball bounce: %s\nBall Friction %s" % [physics_material_override.bounce, physics_material_override.friction]
	label_3d.text = message

func _process(_delta: float) -> void:
	_update_label()

func _ready() -> void:
	if data == null:
		push_warning("Ball has no BallResource assigned.")
		return

	if data.model != null:
		add_child(data.model.instantiate())

	mass = data.mass
	gravity_scale = data.gravity_scale

	linear_damp = data.linear_damp
	angular_damp = data.angular_damp
	
	#Should be false. If you turn it true, frontal collisions will start seem heavy
	continuous_cd = false
	#Should be true. If you turn it  false ball will start doing micromovements instead of stop when his energy is ending
	can_sleep = true
	
	physics_material_override.bounce = data.bounce
	physics_material_override.friction = data.friction

	add_to_group(Groups.BALL)

func strike(direction: Vector3, power: float, hit_offset_local: Vector3 = Vector3.ZERO) -> void:
	var dir := Vector3(direction.x, 0.0, direction.z)
	if dir.length_squared() < 0.0001 or power <= 0.0:
		return

	var impulse := dir.normalized() * power
	var hit_offset_world := global_transform.basis * hit_offset_local
	apply_impulse(impulse, hit_offset_world)
