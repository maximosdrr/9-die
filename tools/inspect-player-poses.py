"""Print production rig bounds and key joints at the endpoints of each action."""

import json

import bpy


armature = bpy.data.objects["Character_Armature"]
meshes = [
    bpy.data.objects[name]
    for name in (
        "Character_Arm_L",
        "Character_Arm_R",
        "Character_Head",
        "Character_Pants",
        "Character_Torso",
    )
]
bones = (
    "CC_Base_Hip",
    "CC_Base_Head",
    "CC_Base_L_Hand",
    "CC_Base_R_Hand",
    "CC_Base_L_Foot",
    "CC_Base_R_Foot",
)
report = {}

for action_name in (
    "Idle",
    "Walk",
    "Sit",
    "IdleSit",
    "SitHoldingCards",
    "IdleSitHoldingCards",
):
    action = bpy.data.actions[action_name]
    armature.animation_data.action = action
    report[action_name] = {}

    for frame in sorted({int(action.frame_range[0]), int(action.frame_range[1])}):
        bpy.context.scene.frame_set(frame)
        depsgraph = bpy.context.evaluated_depsgraph_get()
        points = []

        for source in meshes:
            evaluated = source.evaluated_get(depsgraph)
            evaluated_mesh = evaluated.to_mesh()
            points.extend(evaluated.matrix_world @ vertex.co for vertex in evaluated_mesh.vertices)
            evaluated.to_mesh_clear()

        report[action_name][str(frame)] = {
            "min": [min(point[axis] for point in points) for axis in range(3)],
            "max": [max(point[axis] for point in points) for axis in range(3)],
            "bones": {
                bone_name: list(
                    (armature.matrix_world @ armature.pose.bones[bone_name].matrix).translation
                )
                for bone_name in bones
            },
        }

print("POSE_REPORT=" + json.dumps(report))
