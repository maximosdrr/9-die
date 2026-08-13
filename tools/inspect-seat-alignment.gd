extends SceneTree


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	var game := (load("res://Games/Poker/Poker.tscn") as PackedScene).instantiate()
	root.add_child(game)
	await process_frame

	var seat := game.get_node("Seats/Seat0") as Marker3D
	var chair := game.get_node("TableFixture/WoodenChair_006") as MeshInstance3D
	var character := (load("res://World/Player/Components/CharacterVisual.tscn") as PackedScene).instantiate()
	game.add_child(character)
	character.global_transform = seat.global_transform
	var animator := character.find_child("AnimationPlayer", true, false) as AnimationPlayer
	animator.play("IdleSitHoldingCards")
	animator.seek(1.0, true)
	animator.advance(0.0)
	await process_frame

	print("SEAT=", seat.global_transform)
	print("EYE=", seat.get_node("SeatView").global_transform)
	print("CHAIR_ORIGIN=", chair.global_position, " CHAIR_AABB=", _global_aabb(chair))
	print("CHARACTER_AABB=", _merged_aabb(character))
	var skeleton := character.find_child("Skeleton3D", true, false) as Skeleton3D
	for bone_name in ["CC_Base_Hip", "CC_Base_L_Eye", "CC_Base_R_Eye"]:
		var bone := skeleton.find_bone(bone_name)
		print(bone_name, "=", skeleton.global_transform * skeleton.get_bone_global_pose(bone))

	game.queue_free()
	quit()


func _global_aabb(mesh: MeshInstance3D) -> AABB:
	return mesh.global_transform * mesh.get_aabb()


func _merged_aabb(node: Node) -> AABB:
	var found := false
	var result := AABB()
	for child in node.find_children("*", "MeshInstance3D", true, false):
		var mesh := child as MeshInstance3D
		var box: AABB = mesh.global_transform * mesh.get_transformed_aabb()
		if not found:
			result = box
			found = true
		else:
			result = result.merge(box)
	return result
