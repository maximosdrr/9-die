class_name PoolBallRespawn extends Node

const BALL_SCENE = preload("uid://cqwu27wd0ddmr")
const WHITE_BALL_MESH = preload("uid://bhjlnqgqhyfnc")

const COLORED_BALL_MESHES = [
	preload("uid://bu6okh4akm3ec"), preload("uid://dfmov82m3kcqh"), 
	preload("uid://i25llnp0d762"), preload("uid://bai0gasp48bda"), 
	preload("uid://bi80fjwrtn0pb"), preload("uid://d0bej0qthal2d"), 
	preload("uid://d1gohyrlrh0km"), preload("uid://dqhiy43ibqtnu"), 
	preload("uid://dpnwjgewr20pt"), preload("uid://eb8mq2lhl83w"), 
	preload("uid://nsf6cg0twjqg"), preload("uid://dnmrfdttylve1"), 
	preload("uid://b3skwpys2hnwk"), preload("uid://dk2el0bm0kq7v"), 
	preload("uid://fc8ua6ttootl")
]

@export var head_spot: Vector3 = Vector3(0, 0, -0.5)
@export var foot_spot: Vector3 = Vector3(0, 0, 0.5)
@export var balls_quantity: int = 9
@export var balls_holder: Node3D

var ball_diameter: float = 0.029 
var cue_ball: Ball = null 
var balls: Array[Ball] = []

func _ready() -> void:
	spawn_cue_ball()
	spawn_triangle_rack()

func spawn_cue_ball() -> void:
	cue_ball = BALL_SCENE.instantiate()
	
	var visual = WHITE_BALL_MESH.instantiate()
	cue_ball.add_child(visual)
	
	balls_holder.add_child(cue_ball)
	cue_ball.position = head_spot
	cue_ball.name = "CueBall"

func spawn_triangle_rack() -> void:
	var index = 0
	var rows = 5
	var COLORED_BALL_ARRAY = COLORED_BALL_MESHES.duplicate().slice(0, balls_quantity)
	
	for row in range(rows):
		var z_offset = row * (ball_diameter * 0.866)
		var start_x = -(row * ball_diameter) / 2.0
		
		for col in range(row + 1):
			if index >= COLORED_BALL_ARRAY.size():
				break
				
			var mesh_scene = COLORED_BALL_ARRAY[index]
			
			var x_pos = start_x + (col * ball_diameter)
			var pos_final = Vector3(x_pos, foot_spot.y, foot_spot.z + z_offset)
			
			create_colored_ball(mesh_scene, pos_final, index + 1)
			
			index += 1

func create_colored_ball(visual_resource: PackedScene, pos: Vector3, number: int) -> void:
	var ball_instance = BALL_SCENE.instantiate()
	
	var visual_node = visual_resource.instantiate()
	ball_instance.add_child(visual_node)
	
	ball_instance.position = pos
	ball_instance.name = "Ball_" + str(number)
	ball_instance.index = number
	balls_holder.add_child(ball_instance)
	
	balls.append(ball_instance)
