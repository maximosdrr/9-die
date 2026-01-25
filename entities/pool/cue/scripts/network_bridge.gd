class_name CueNetworkBridge extends Node

var cue: Cue

func setup(_cue: Cue):
	cue = _cue
	cue.strike_executed.connect(_call_strike)

@rpc("any_peer", "call_remote", "reliable")
func request_strike(dir: Vector3, final_force: float, hit_offset: Vector3):
	if not multiplayer.is_server():
		return
	cue.cue_ball.strike(dir, final_force, hit_offset)
	
func _call_strike(dir: Vector3, final_force: float, hit_offset: Vector3):
	rpc_id(1, "request_strike", dir, final_force, hit_offset)
