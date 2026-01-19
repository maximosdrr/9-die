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

@export_group("Jump Physics")
@export var jump_efficiency: float = 1.2
@export var min_jump_angle: float = 25.0
@export var cue_max_angle: float = 65.0
@export var floor_tolerance: float = 0.02 

signal stopped_moving(position: Vector3)
signal striked
signal ball_contacted(ball: Ball)
signal jump_started
signal jump_landed

var radius: float = 0.029 
var _initial_transform: Transform3D
var _is_in_air: bool = false

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

	var raw_normal = direction.normalized()
	
	var attack_angle_deg = rad_to_deg(asin(abs(raw_normal.y)))
	var is_valid_jump = raw_normal.y < 0 and attack_angle_deg >= min_jump_angle
	
	var raw_dir: Vector3
	var jump_factor: float = 0.0

	if is_valid_jump:
		raw_dir = raw_normal
		var angle_range = cue_max_angle - min_jump_angle
		var angle_progress = clamp(attack_angle_deg - min_jump_angle, 0.0, angle_range)
		
		jump_factor = angle_progress / angle_range
	else:
		raw_dir = Vector3(direction.x, 0.0, direction.z).normalized()

	var offset_ratio = hit_offset_local.x / radius
	var max_angle_deg = data.max_squirt_angle_deg if data else 0.0
	var spin_power = data.spin_power_factor if data else 1.0
	var max_angle_rad = deg_to_rad(max_angle_deg)
	var deflection_angle = offset_ratio * max_angle_rad
	
	var final_dir = raw_dir.rotated(Vector3.UP, deflection_angle)
	var linear_impulse = final_dir * total_force
	
	if is_valid_jump:
		var vertical_force = abs(linear_impulse.y) * jump_efficiency * jump_factor
		linear_impulse.y = vertical_force
	else:
		linear_impulse.y = 0.0

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
	_is_in_air = false
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
	
	_check_ground_state()

func _check_ground_state() -> void:
	var space_state = get_world_3d().direct_space_state
	
	var from = global_position 
	var to = from + Vector3.DOWN * (radius + floor_tolerance)
	
	var query = PhysicsRayQueryParameters3D.create(from, to)
	
	query.exclude = [self.get_rid()]
	
	var result = space_state.intersect_ray(query)
	var is_on_floor = not result.is_empty()
	
	if not is_on_floor and not _is_in_air:
		_is_in_air = true
		jump_started.emit()
		
	elif is_on_floor and _is_in_air:
		_is_in_air = false
		jump_landed.emit()

func _on_body_entered(body: Node) -> void:
	if body is Ball:
		ball_contacted.emit(body)
