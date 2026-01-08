class_name Ball
extends RigidBody3D

@export var data: BallResource
@export var is_cue_ball: bool = false

@onready var label_3d: Label3D = $Label3D
@onready var collision_shape: CollisionShape3D = $CollisionShape3D
@onready var state_machine: StateMachine = $StateMachine
@export var squirt_factor: float = 10
@export_range(0.0, 1.0) var spin_power_factor: float = 0.3

func _update_label():
	if state_machine and state_machine.current:
		var message = "State: %s" % [state_machine.current.name]
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
	
	continuous_cd = false
	can_sleep = true
	
	physics_material_override.bounce = data.bounce
	physics_material_override.friction = data.friction

	add_to_group("Ball") # Ensure string matches your Groups singleton if used

func strike(direction: Vector3, total_force: float, hit_offset_local: Vector3 = Vector3.ZERO) -> void:
	if total_force <= 0.0:
		return

	# 1. Preparar Vetores Básicos
	# Normalizamos a direção (ignorando Y para garantir que o vetor é plano no chão)
	var raw_dir := Vector3(direction.x, 0.0, direction.z).normalized()
	
	# 2. Calcular Deflexão (Squirt)
	# Como sua bola é pequena (0.029m), o offset é pequeno, exigindo um fator alto (15.0)
	var deflection_angle = hit_offset_local.x * squirt_factor
	var final_dir = raw_dir.rotated(Vector3.UP, deflection_angle)
	
	# 3. Calcular a Base de Rotação (Para saber onde é o ponto de impacto no mundo)
	var forward = -final_dir
	var up = Vector3.UP
	var right = forward.cross(up).normalized()
	up = right.cross(forward).normalized() # Recalcula UP
	var aim_basis = Basis(right, up, forward)
	
	# 4. Converter Offset Local para Global
	# hit_offset_world é o vetor "alavanca" do centro da bola até onde batemos
	var hit_offset_world = aim_basis * hit_offset_local
	
	# 5. SEPARAR AS FORÇAS (Aqui está o segredo para corrigir o giro)
	
	# A força linear empurra a bola na direção calculada
	var linear_impulse = final_dir * total_force
	
	# O torque é o produto vetorial (Cross Product) da Alavanca x Força
	# Isso calcula exatamente o eixo e a potência de rotação física
	var raw_torque = hit_offset_world.cross(linear_impulse)
	
	# Aplicamos o multiplicador para "acalmar" a rotação
	var reduced_torque = raw_torque * spin_power_factor
	
	# 6. Aplicação Física
	# Usa apply_central_impulse para mover (não gera rotação extra)
	apply_central_impulse(linear_impulse)
	
	# Usa apply_torque_impulse para girar (com nossa força reduzida)
	apply_torque_impulse(reduced_torque)
