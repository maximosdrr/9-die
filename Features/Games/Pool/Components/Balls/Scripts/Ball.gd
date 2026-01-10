class_name Ball extends RigidBody3D

@export var data: BallResource
@export var continuos_cd: bool = false

@export_range(0.0, 45.0) var max_squirt_angle_deg: float = 10.0 
@export_range(0.0, 1.0) var spin_power_factor: float = 0.02

@onready var collision_shape: CollisionShape3D = $CollisionShape3D
@onready var state_machine: StateMachine = $StateMachine

var radius: float = 0.029 
var _initial_transform: Transform3D

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
	
	continuous_cd = continuos_cd
	can_sleep = true
	
	physics_material_override.bounce = data.bounce
	physics_material_override.friction = data.friction

	add_to_group("Ball")
	
	if collision_shape and collision_shape.shape is SphereShape3D:
		radius = collision_shape.shape.radius
	else:
		push_error("Ball CollisionShape must be a SphereShape3D!")
	
	_initial_transform = global_transform

func strike(direction: Vector3, total_force: float, hit_offset_local: Vector3 = Vector3.ZERO) -> void:
	if total_force <= 0.0:
		return

	var raw_dir := Vector3(direction.x, 0.0, direction.z).normalized()
	
	var offset_ratio = hit_offset_local.x / radius
	
	var max_angle_rad = deg_to_rad(max_squirt_angle_deg)
	var deflection_angle = offset_ratio * max_angle_rad
	
	var final_dir = raw_dir.rotated(Vector3.UP, deflection_angle)
	
	var forward = -final_dir
	var up = Vector3.UP
	var right = forward.cross(up).normalized()
	up = right.cross(forward).normalized()
	var aim_basis = Basis(right, up, forward)
	
	var hit_offset_world = aim_basis * hit_offset_local
	
	var linear_impulse = final_dir * total_force
	var raw_torque = hit_offset_world.cross(linear_impulse)
	var reduced_torque = raw_torque * spin_power_factor
	
	apply_central_impulse(linear_impulse)
	apply_torque_impulse(reduced_torque)

func respawn() -> void:
	linear_velocity = Vector3.ZERO
	angular_velocity = Vector3.ZERO
	
	global_transform = _initial_transform
	
	sleeping = false
	
	visible = true
	process_mode = Node.PROCESS_MODE_INHERIT

func _physics_process(_delta: float) -> void:
	# Lógica para frear a rotação excessiva quando a bola está quase parada
	# Se a bola está muito lenta linearmente (quase parada no lugar)
	if linear_velocity.length() < 0.1:
		# Aumenta drasticamente o freio da rotação (simula o atrito do pano estático)
		angular_damp = 1.0 
	else:
		# Volta para o valor normal configurado no recurso (ex: 1.0) para permitir que ela role bonito
		angular_damp = data.angular_damp
