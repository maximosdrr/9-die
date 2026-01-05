class_name BallResource extends Resource

@export var name: String
@export var mass := 0.17
@export var gravity_scale := 1.0
@export var linear_damp := 0.3
@export var angular_damp := 0.3
@export var can_sleep := true
@export var friction := 0.4
@export var bounce := 0.85

@export var roll_drag: float = 0.6
@export var stop_speed: float = 0.2
@export var max_speed: float = 10.0

@export var model: PackedScene
