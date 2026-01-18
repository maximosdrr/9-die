class_name BallResource extends Resource

@export var name: String
@export var mass := 0.17
@export var gravity_scale := 1.0
#linear_damp is a continuous drag force applied every physics step to the body’s linear velocity.
#You can think of it as: air resistance + rolling resistance applied everywhere, all the time
#it reduces speed gradually
@export var linear_damp := 0.2

#angular_damp is the rotational equivalent of linear_damp.
#You can think of it as: how fast spin dies out
#It reduces angular velocity every physics frame.
@export var angular_damp := 0.1

@export var friction := 0.1
@export var bounce := 0.85
@export var absorbent := false
@export var can_sleep := true
@export var continuos_cd: bool = true
@export_range(0.0, 45.0) var max_squirt_angle_deg: float = 2.0
@export_range(0.0, 1.0) var spin_power_factor: float = 0.4

@export var model: PackedScene
