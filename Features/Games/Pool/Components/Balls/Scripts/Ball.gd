class_name Ball
extends RigidBody3D

@export var data: BallResource
@export var is_cue_ball: bool = false

@export_range(0.0, 45.0) var max_squirt_angle_deg: float = 15.0 
@export_range(0.0, 1.0) var spin_power_factor: float = 0.3

@onready var label_3d: Label3D = $Label3D
@onready var collision_shape: CollisionShape3D = $CollisionShape3D
@onready var state_machine: StateMachine = $StateMachine

var radius: float = 0.029 

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
	
	continuous_cd = false
	can_sleep = true
	
	physics_material_override.bounce = data.bounce
	physics_material_override.friction = data.friction

	add_to_group("Ball")
	
	if collision_shape and collision_shape.shape is SphereShape3D:
		radius = collision_shape.shape.radius
	else:
		push_error("Ball CollisionShape must be a SphereShape3D!")

func _process(_delta: float) -> void:
	_update_label()

func _update_label():
	if state_machine and state_machine.current:
		label_3d.text = "State: %s" % [state_machine.current.name]

func strike(direction: Vector3, total_force: float, hit_offset_local: Vector3 = Vector3.ZERO) -> void:
	if total_force <= 0.0:
		return

	var raw_dir := Vector3(direction.x, 0.0, direction.z).normalized()
	
	# --- CÁLCULO DE DEFLEXÃO (GRAUS -> RADIANOS) ---
	var offset_ratio = hit_offset_local.x / radius
	
	# AQUI ESTÁ A MUDANÇA:
	# 1. Pegamos o valor em graus (ex: 15.0)
	# 2. Convertemos para radianos com deg_to_rad()
	# 3. Multiplicamos pela proporção do offset
	var max_angle_rad = deg_to_rad(max_squirt_angle_deg)
	var deflection_angle = offset_ratio * max_angle_rad
	
	var final_dir = raw_dir.rotated(Vector3.UP, deflection_angle)
	
	# --- RESTANTE DA FÍSICA (IGUAL) ---
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
