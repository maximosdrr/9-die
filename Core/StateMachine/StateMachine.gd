class_name StateMachine extends Node3D

signal state_changed(type: String, metadata: Dictionary[Variant, Variant])

var current: State
var previous: State
var states: Dictionary[String, State]
var current_metadata = {}

@export var enabled: bool = true
@export var initial_state: String
@export var public_state_syncronizer: PublicStateSyncronizer = null
@export var check_for_multiplayer_authority_on_state_handle_input: bool = false

func _ready() -> void:
	_setup_states()
	_setup_initial_state()
	
	if public_state_syncronizer != null:
		public_state_syncronizer.setup(self)

func change_state(type: String, metadata: Dictionary[Variant, Variant]):
	if current.type == type:
		return
	
	var new_state = states[type]
	
	if new_state == null:
		var error_message = "State %s not found" % [type]
		push_error(error_message)
		return
	
	current_metadata = metadata
	state_changed.emit(type, metadata)
	current.exit(metadata)
	
	previous = current
	current = new_state
	
	current.enter(metadata)

func _process(delta: float) -> void:
	if current != null:
		current.process(delta)

func _physics_process(delta: float) -> void:
	if current != null:
		current.physics_process(delta)

func _unhandled_input(event: InputEvent) -> void:
	if check_for_multiplayer_authority_on_state_handle_input and\
	not is_multiplayer_authority(): return
	
	if current != null:
		current.handle_input(event)

func _setup_states():
	var parent = get_parent()
	
	for state in get_children():
		if state is State:
			state.state_machine = self
			state.parent = parent
			state.setup(parent)
			print(state.type)
			states.set(state.type, state)

func _setup_initial_state():
	var state = states.get(initial_state) as State
	
	if state == null:
		push_error("Initial state not found: ", initial_state)
		return
	
	current = state
	state.enter({})
