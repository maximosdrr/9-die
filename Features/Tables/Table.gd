class_name Table extends Resource

@export var table_name: String = "Table"
@export var model: PackedScene

@export var friction := 0.5
@export var bounce := 0.03

#P, R, S
@export var position: Vector3 = Vector3(0.0, 0.0, 0.0)
@export var rotation: Vector3 = Vector3(0.0, 0.0, 0.0)
@export var scale: Vector3 = Vector3.ONE
