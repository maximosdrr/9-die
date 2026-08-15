extends SceneTree


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	var failures: Array[String] = []
	_validate_asset(
		"res://Assets/Characters/Player/PlayerCharacter.glb",
		"Character_Armature", 5,
		["Idle", "Walk", "Sit", "IdleSit", "SitHoldingCards", "PickCards",
			"IdleSitHoldingCards", "IdleHoldingCardsDown", "PokerPass", "PokerBet",
			"Showdown"],
		failures)
	_validate_asset(
		"res://Assets/Characters/Player/PlayerFirstPerson.glb",
		"FP_Armature", 4,
		["Idle", "Walk", "IdleSit", "PickCards", "IdleSitHoldingCards",
			"IdleHoldingCardsDown", "PokerPass", "PokerBet", "Showdown"], failures)

	var hands_scene := load(
		"res://Games/Poker/Components/Hands/PlayerFirstPersonHands.tscn") as PackedScene
	var hands := hands_scene.instantiate() if hands_scene != null else null
	if hands == null:
		failures.append("PlayerFirstPersonHands.tscn não instancia")
	else:
		root.add_child(hands)
		var skeleton := hands.get_node_or_null("PlayerFirstPerson/FP_Armature/Skeleton3D") as Skeleton3D
		var marker := hands.get_node_or_null(
			"PlayerFirstPerson/FP_Armature/Skeleton3D/CC_Base_L_Hand/CardGripMarker") as Node3D
		var animator := hands.get_node_or_null("PlayerFirstPerson/AnimationPlayer") as AnimationPlayer
		if skeleton == null:
			failures.append("cena 1P não aponta para FP_Armature")
		if marker == null:
			failures.append("cena 1P não encontra CardGripMarker")
		if animator == null or not animator.has_animation("IdleHoldingCardsDown"):
			failures.append("cena 1P não contém IdleHoldingCardsDown")
		elif skeleton != null and marker != null:
			animator.play("IdleHoldingCardsDown")
			for time in [0.0, 0.8, 1.6, 2.5]:
				animator.seek(time, true)
				animator.advance(0.0)
				var hand := skeleton.find_bone("CC_Base_L_Hand")
				var grip := skeleton.global_transform * skeleton.get_bone_global_pose(hand) * marker.transform
				if not grip.origin.is_finite():
					failures.append("CardGrip inválido em %.1fs" % time)
				print("GRIP_TIME=", time, " ORIGIN=", grip.origin,
					" SCALE=", grip.basis.get_scale())
		hands.queue_free()

	if failures.is_empty():
		print("PLAYER_ASSET_VALIDATION=PASS")
		quit(0)
	else:
		for failure in failures:
			push_error(failure)
		print("PLAYER_ASSET_VALIDATION=FAIL COUNT=", failures.size())
		quit(1)


func _validate_asset(
	path: String,
	armature_name: String,
	mesh_count: int,
	expected_animations: Array[String],
	failures: Array[String]) -> void:
	var scene := load(path) as PackedScene
	if scene == null:
		failures.append(path + " não carrega")
		return
	var instance := scene.instantiate()
	root.add_child(instance)
	var armature := instance.find_child(armature_name, true, false) as Node3D
	var skeleton := instance.find_child("Skeleton3D", true, false) as Skeleton3D
	var animator := instance.find_child("AnimationPlayer", true, false) as AnimationPlayer
	var marker := instance.find_child("CardGripMarker", true, false) as Node3D
	var meshes := instance.find_children("*", "MeshInstance3D", true, false)
	if armature == null:
		failures.append(path + " não contém " + armature_name)
	if skeleton == null or skeleton.get_bone_count() != 101:
		failures.append(path + " não contém o esqueleto esperado")
	if meshes.size() != mesh_count:
		failures.append(path + " contém %d meshes, esperado %d" % [meshes.size(), mesh_count])
	if marker == null:
		failures.append(path + " não contém CardGripMarker")
	if animator == null:
		failures.append(path + " não contém AnimationPlayer")
	else:
		var actual: Array[String] = []
		for library_name in animator.get_animation_library_list():
			for animation_name in animator.get_animation_library(library_name).get_animation_list():
				actual.append(animation_name)
		actual.sort()
		var expected := expected_animations.duplicate()
		expected.sort()
		if actual != expected:
			failures.append(path + " animações=%s esperado=%s" % [actual, expected])
	print("ASSET=", path, " ARMATURE=", armature_name,
		" MESHES=", meshes.size(), " ANIMATIONS=", expected_animations)
	instance.queue_free()
