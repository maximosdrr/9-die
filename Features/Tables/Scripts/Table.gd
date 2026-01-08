class_name Table extends StaticBody3D

@onready var table_balls_monitor: BallSleepMonitor = $Components/TableBallsMonitor
@onready var cue_ball: Ball = $TableSurfaceCollission/BallsHolder/CueBall
@onready var remote_top: RemoteTransform3D = $Pivot/RemoteTop

@export_group("External References")
@export var global_camera: GlobalCamera

func _ready():
	remote_top.remote_path = global_camera.get_path()
