class_name Ball extends RigidBody3D

const WHITE_BALL_MESH = preload("uid://duqktbfsd1n6u")
const COLORED_BALL_MESHES = [
	preload("uid://dfbechio6ptbs"), preload("uid://xb3gwg7b6vym"),
	preload("uid://bgm0x1tow64ow"), preload("uid://bohcijga8l31y"),
	preload("uid://blo2j7vueioa8"), preload("uid://csl0h6mpj5wrf"),
	preload("uid://b3u1v561vo0k6"), preload("uid://dkmf4qijjxk0y"),
	preload("uid://cl3vhtcj52lj2"), preload("uid://c2pjjww311x5"),
	preload("uid://dsoxx0stji020"), preload("uid://bvnrqbdvnvih1"),
	preload("uid://bdr1hxm60mcdi"), preload("uid://3pq44lqvihfa"),
	preload("uid://buoe2fsbwjbk1")
]

@export var jump_efficiency: float = 0.6
@export var data: BallResource
@export var index = 0
@export var texture_id: int = 0:
	set(value):
		texture_id = value
		if is_inside_tree():
			call_deferred("_update_visual")

@onready var collision_shape: CollisionShape3D = $CollisionShape3D
@onready var state_machine: StateMachine = $StateMachine
@onready var multiplayer_synchronizer: MultiplayerSynchronizer = $MultiplayerSynchronizer

signal stopped_moving(position: Vector3)
signal striked
signal ball_contacted(ball: Ball)

var radius: float = 0.029 
var _initial_transform: Transform3D

func _ready() -> void:
	_update_visual()

	if data:
		mass = data.mass
		gravity_scale = data.gravity_scale
		linear_damp = data.linear_damp
		angular_damp = data.angular_damp
		
		continuous_cd = data.continuos_cd 
		can_sleep = data.can_sleep
		
		var new_mat = PhysicsMaterial.new()
		new_mat.bounce = data.bounce
		new_mat.friction = data.friction
		new_mat.absorbent = data.absorbent 
		
		physics_material_override = new_mat
		
	add_to_group("Ball")
	
	if collision_shape and collision_shape.shape:
		radius = collision_shape.shape.radius
		
	_initial_transform = global_transform

func _update_visual() -> void:
	for child in get_children():
		if child is MeshInstance3D or (child is Node3D and child.name != "CollisionShape3D" and not child is MultiplayerSynchronizer and not child is StateMachine):
			if child.get_class() == "MeshInstance3D" or child.has_method("get_aabb"): 
				child.queue_free()
	
	var new_visual = null
	
	if texture_id == 0:
		new_visual = WHITE_BALL_MESH.instantiate()
	elif texture_id > 0 and (texture_id - 1) < COLORED_BALL_MESHES.size():
		new_visual = COLORED_BALL_MESHES[texture_id - 1].instantiate()
	
	if new_visual:
		add_child(new_visual)

func strike(direction: Vector3, total_force: float, hit_offset_local: Vector3 = Vector3.ZERO) -> void:
	striked.emit()

	if total_force <= 0.0:
		return

	var is_jump_shot := direction.y < -0.05
	
	var raw_dir: Vector3
	if is_jump_shot:
		raw_dir = direction.normalized()
	else:
		raw_dir = Vector3(direction.x, 0.0, direction.z).normalized()

	var offset_ratio = hit_offset_local.x / radius
	
	var max_angle_deg = data.max_squirt_angle_deg if data else 0.0
	var spin_power = data.spin_power_factor if data else 1.0
	
	var max_angle_rad = deg_to_rad(max_angle_deg)
	var deflection_angle = offset_ratio * max_angle_rad
	
	var final_dir = raw_dir.rotated(Vector3.UP, deflection_angle)
	var linear_impulse = final_dir * total_force
	
	if is_jump_shot:
		linear_impulse.y = abs(linear_impulse.y) * jump_efficiency

	var forward = -Vector3(final_dir.x, 0.0, final_dir.z).normalized() 
	var up = Vector3.UP
	var right = forward.cross(up).normalized()
	up = right.cross(forward).normalized()
	var aim_basis = Basis(right, up, forward)
	
	var hit_offset_world = aim_basis * hit_offset_local
	
	var raw_torque = hit_offset_world.cross(linear_impulse)
	raw_torque.y = -raw_torque.y
	
	var reduced_torque = raw_torque * spin_power
	
	apply_central_impulse(linear_impulse)
	apply_torque_impulse(reduced_torque)

func respawn() -> void:
	linear_velocity = Vector3.ZERO
	angular_velocity = Vector3.ZERO
	global_transform = _initial_transform
	sleeping = false
	visible = true
	process_mode = Node.PROCESS_MODE_INHERIT

func _physics_process(delta: float) -> void:
	var speed := linear_velocity.length()
	var slow_threshold := 0.3
	var stop_threshold := 0.05
	
	var min_damp := data.angular_damp if data else 1.0
	var max_damp := 1.5
	
	var t := inverse_lerp(slow_threshold, stop_threshold, speed)
	t = clamp(t, 0.0, 1.0)
	angular_damp = lerp(min_damp, max_damp, t)

func _on_body_entered(body: Node) -> void:
	if body is Ball:
		ball_contacted.emit(body)
