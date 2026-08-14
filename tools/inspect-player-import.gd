extends SceneTree


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	for path in [
		"res://Assets/Characters/Player/PlayerCharacter.glb",
		"res://Assets/Characters/Player/PlayerFirstPerson.glb",
	]:
		print("SCENE=", path)
		var packed := load(path) as PackedScene
		if packed == null:
			push_error("Could not load " + path)
			continue
		var instance := packed.instantiate()
		root.add_child(instance)
		var animator := instance.find_child("AnimationPlayer", true, false) as AnimationPlayer
		var skeleton := instance.find_child("Skeleton3D", true, false) as Skeleton3D
		if animator != null and skeleton != null:
			for clip in ["Idle", "Walk", "IdleSit", "PickCards",
				"IdleSitHoldingCards", "IdleHoldingCardsDown"]:
				if not animator.has_animation(clip):
					print("MISSING_OPTIONAL_CLIP=", clip)
					continue
				animator.play(clip)
				animator.seek(0.5, true)
				animator.advance(0.0)
				var hand := skeleton.find_bone("CC_Base_L_Hand")
				var right_hand := skeleton.find_bone("CC_Base_R_Hand")
				var neck := skeleton.find_bone("CC_Base_NeckTwist02")
				var left_eye := skeleton.find_bone("CC_Base_L_Eye")
				var right_eye := skeleton.find_bone("CC_Base_R_Eye")
				print("POSE=", clip,
					" ROOT=", skeleton.global_transform,
					" LEFT=", skeleton.global_transform * skeleton.get_bone_global_pose(hand),
					" RIGHT=", skeleton.global_transform * skeleton.get_bone_global_pose(right_hand),
					" NECK=", skeleton.global_transform * skeleton.get_bone_global_pose(neck),
					" LEFT_EYE=", skeleton.global_transform * skeleton.get_bone_global_pose(left_eye),
					" RIGHT_EYE=", skeleton.global_transform * skeleton.get_bone_global_pose(right_eye))
		_dump(instance, "")
		var grip := instance.find_child("CardGripMarker", true, false) as Node3D
		if grip != null:
			print("CARD_GRIP_TRANSFORM=", grip.transform,
				" ATTACHMENT_GLOBAL=", (grip.get_parent() as Node3D).global_transform,
				" GLOBAL_WITHIN_SCENE=", grip.global_transform)
		root.remove_child(instance)
		instance.free()
	quit()


func _dump(node: Node, indent: String) -> void:
	print(indent, node.name, " [", node.get_class(), "]")
	if node is Skeleton3D:
		var skeleton := node as Skeleton3D
		var bones: Array[String] = []
		for bone in skeleton.get_bone_count():
			bones.append(skeleton.get_bone_name(bone))
		print(indent, "  BONES=", ",".join(bones))
	elif node is MeshInstance3D:
		var mesh_instance := node as MeshInstance3D
		print(indent, "  AABB=", mesh_instance.get_aabb())
	elif node is AnimationPlayer:
		var player := node as AnimationPlayer
		for library_name in player.get_animation_library_list():
			var library := player.get_animation_library(library_name)
			for animation_name in library.get_animation_list():
				var animation := library.get_animation(animation_name)
				print(indent, "  ANIMATION=", animation_name,
					" LENGTH=", animation.length, " LOOP=", animation.loop_mode)

	for child in node.get_children():
		_dump(child, indent + "  ")
