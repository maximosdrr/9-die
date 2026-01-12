class_name GameStarting extends State

@export var start_game_timer: Timer
@export var start_game_ui: PoolStartGameUI

var players_ids = []

func _init() -> void:
	self.type = State.Type.GAME_STARTING

func enter(metadata: Dictionary[Variant, Variant]):
	assert(metadata.has("players_ids"))
	players_ids = metadata.get("players_ids")
	
	if not start_game_timer.timeout.is_connected(_on_time_ends):
		start_game_timer.timeout.connect(_on_time_ends)
	
	start_game_timer.start()

func process(_delta: float) -> void:
	var text = "Game starts in %s" % [ceil(start_game_timer.time_left)]
	start_game_ui.set_text(text)

func _on_time_ends():
	start_game_ui.hide()
	var _metadata = {}
	_metadata.set("players_ids", players_ids)
	state_machine.change_state(State.Type.GAME_STARTED, _metadata)
	start_game_timer.stop()
