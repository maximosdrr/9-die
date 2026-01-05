class_name FindReferenceBallUtil

static func execute(source: Node3D) -> Ball:
	var balls := source.get_tree().get_nodes_in_group(Groups.BALL)
	var _reference: Ball = null

	for ball in balls:
		if ball is not Ball:
			continue
		if ball.is_white_ball:
			_reference = ball

	if _reference == null:
		push_error("Best ball not found")
	return _reference
