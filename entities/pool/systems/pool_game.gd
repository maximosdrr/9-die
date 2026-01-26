class_name PoolGame extends Table

@onready var off_table_monitor: OffTableMonitor = $Scripts/OffTableMonitor
@onready var balls_movement_monitor: BallsMovementMonitor = $Scripts/BallsMovementMonitor
@onready var ball_placement_manager: BallPlacementManager = $Scripts/BallPlacementManager
@onready var balls_holder: BallHolder = $BallsHolder

@onready var _match_manager: MatchManager = $MatchManager
@onready var _game_controller: PoolController = $PoolController

@export var pool_table: PoolGameTable

var cue_ball: Ball = null
var balls: Array[Ball] = []
var score_monitor: Area3D

func _ready() -> void:
	add_to_group(Groups.TABLE)
	match_manager = _match_manager
	game_controller = _game_controller
	
	score_monitor = pool_table.score_monitor
	off_table_monitor.setup(pool_table.ball_off_monitor)
	
	#Connect signals
	match_manager.match_started.connect(_on_match_starts)
	match_manager.turn_changed.connect(_on_turn_changes)
	balls_movement_monitor.balls_stopped.connect(_on_turn_resolves)
	
	name = 'Pool_%s' % [get_instance_id()]

func start_match(_players: Array[String]):
	match_manager.start_match(_players, {})
	balls_holder.spawn_balls()

func _on_match_starts():
	await balls_holder.table_ready
	cue_ball = balls_holder.get_cue_ball()
	balls = balls_holder.get_normal_balls()
	game_controller.setup()
	balls_movement_monitor.setup(self)
	
func _on_turn_changes(new_owner_id: String):
	game_controller.set_multiplayer_authority(int(new_owner_id))

func _on_turn_resolves():
	if multiplayer.is_server():
		match_manager.call_next_turn({})
