class_name PoolGame extends StaticBody3D

@onready var table_balls_monitor: BallSleepMonitor = $Components/TableBallsMonitor
@onready var cue_ball: Ball = $TableSurfaceCollission/BallsHolder/CueBall
@onready var remote_top: RemoteTransform3D = $Pivot/RemoteTop

@export_category("References")
@export var global_camera: GlobalCamera

func _ready():
	remote_top.remote_path = Global.camera.get_path()
