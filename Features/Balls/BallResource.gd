class_name BallResource extends Resource

@export var name: String
@export var mass := 0.17
@export var gravity_scale := 1.0
#linear_damp is a continuous drag force applied every physics step to the body’s linear velocity.
#You can think of it as: air resistance + rolling resistance applied everywhere, all the time
#it reduces speed gradually
@export var linear_damp := 0.4

#angular_damp is the rotational equivalent of linear_damp.
#You can think of it as: how fast spin dies out
#It reduces angular velocity every physics frame.
@export var angular_damp := 0.4

@export var friction := 0.08
@export var bounce := 0.85


@export var model: PackedScene
