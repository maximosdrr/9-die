class_name CueNetworkSystem extends Node

var cue: Cue

func setup(_cue: Cue):
	cue = _cue

@rpc("any_peer", "call_local", "reliable")
func request_strike(impact_speed: float):
	if not multiplayer.is_server():
		return
	
	cue.execute_strike(impact_speed)
	
