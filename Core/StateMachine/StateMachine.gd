class_name StateMachine extends Node3D

var current: State
var previous: State
var states: Dictionary[State.Type, State]
@export var initial_state: State.Type

func _ready() -> void:
	_setup_states()
	_setup_initial_state()

func change_state(type: State.Type, metadata: Dictionary[Variant, Variant]):
	var new_state = states[type]
	
	if new_state == null:
		var error_message = "State %s not found" % [type]
		push_error(error_message)
		return
	
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

func _setup_states():
	for state in get_children():
		if state is State:
			state.state_machine = self
			states.set(state.type, state)

func _setup_initial_state():
	var state = states.get(initial_state) as State
	
	if state == null:
		push_error("Initial state not found: ", initial_state)
		return
	
	current = state
	state.enter({})
