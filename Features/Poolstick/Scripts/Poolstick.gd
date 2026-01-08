class_name Poolstick extends Node3D

@export_group("References")
@export var cue_ball: Ball # Optional: Can be set via set_cue_ball

@export_group("Stroke Settings")
@export var stroke_sensitivity: float = 0.01 
@export var max_draw_distance: float = 0.8
@export var ball_radius_offset: float = 0.04
@export var max_speed_reference: float = 6.0
# Add a force multiplier to convert 0-1 power into actual Physics Units (Newtons)
@export var force_multiplier: float = 1.5

@export_group("Spin Settings")
@export var spin_sensitivity: float = 0.005
@export var max_spin_offset: float = 0.025 # Limit how far from center we can hit (Ball radius approx)

var _is_charging: bool = false
var _is_adjusting_spin: bool = false # Flag for spin mode

var _previous_z: float = 0.0
var _stick_velocity: float = 0.0
var _accumulated_mouse_y: float = 0.0

# Stores the X/Y offset on the ball face
var _spin_offset: Vector2 = Vector2.ZERO 

func set_cue_ball(ball: Ball):
	cue_ball = ball

func _ready() -> void:
	position.z = ball_radius_offset
	_previous_z = position.z

func _unhandled_input(event: InputEvent) -> void:
	# --- SPIN INPUT LOGIC ---
	if event.is_action_pressed("spin_modifier"):
		_is_adjusting_spin = true
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	elif event.is_action_released("spin_modifier"):
		_is_adjusting_spin = false
		#Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE) # Or keep captured if FPS style
	
	if _is_adjusting_spin and event is InputEventMouseMotion:
		_handle_spin_input(event.relative)
		get_viewport().set_input_as_handled()
		return # Stop execution so we don't charge power while adjusting spin

	# --- CHARGE INPUT LOGIC ---
	if event.is_action_pressed("stroke_mode"):
		_start_charging()
		get_viewport().set_input_as_handled()
	elif event.is_action_released("stroke_mode"):
		_cancel_charging()
		return

	if _is_charging and event is InputEventMouseMotion:
		_accumulated_mouse_y += event.relative.y
		get_viewport().set_input_as_handled()

func _process(delta: float) -> void:
	# Always apply the visual offset for Spin (X/Y)
	position.x = _spin_offset.x
	position.y = _spin_offset.y

	if not _is_charging:
		return
		
	_apply_movement_logic()
	
	var current_z = position.z
	_stick_velocity = (current_z - _previous_z) / delta
	_previous_z = current_z
	
	if current_z <= (ball_radius_offset + 0.001) and _stick_velocity < -0.1:
		_execute_strike()

func _handle_spin_input(relative: Vector2) -> void:
	# Move the hit point based on mouse movement
	_spin_offset.x += relative.x * spin_sensitivity
	_spin_offset.y -= relative.y * spin_sensitivity # Invert Y for intuitive control
	
	# Clamp the offset to a circle (the ball's face)
	if _spin_offset.length() > max_spin_offset:
		_spin_offset = _spin_offset.normalized() * max_spin_offset

func _start_charging() -> void:
	_is_charging = true
	_accumulated_mouse_y = 0.0
	
	_previous_z = position.z
	_stick_velocity = 0.0

func _cancel_charging() -> void:
	_is_charging = false
	_reset_animation()

func _apply_movement_logic() -> void:
	if _accumulated_mouse_y == 0: return
		
	var target_z = position.z + (_accumulated_mouse_y * stroke_sensitivity)
	target_z = clamp(target_z, ball_radius_offset, max_draw_distance)
	
	position.z = target_z
	_accumulated_mouse_y = 0.0

func _execute_strike() -> void:
	if not cue_ball: return

	var impact_speed = abs(_stick_velocity)
	
	var raw_power = clamp(impact_speed / max_speed_reference, 0.0, 1.0)
	var curved_power = pow(raw_power, 2.0)
	
	# Convert normalized power to actual Force (Newtons)
	var final_force = curved_power * force_multiplier
	
	print("Speed: %.2f | Force: %.2f | Offset: %s" % [impact_speed, final_force, _spin_offset])
	
	var dir = -global_transform.basis.z.normalized()
	dir.y = 0 
	
	# Create Vector3 offset (X, Y, 0)
	var hit_offset_3d = Vector3(_spin_offset.x, _spin_offset.y, 0.0)
	
	# Pass the 3D offset to the ball
	cue_ball.strike(dir, final_force, hit_offset_3d)
	
	_is_charging = false
	_reset_spin() # Reset spin after hit? Optional.
	position.z = ball_radius_offset

func _reset_animation() -> void:
	var tween = create_tween()
	tween.tween_property(self, "position:z", ball_radius_offset, 0.2).set_trans(Tween.TRANS_SINE)

func _reset_spin() -> void:
	var tween = create_tween()
	tween.tween_property(self, "_spin_offset", Vector2.ZERO, 0.5)
