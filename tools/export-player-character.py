"""Export the production third- and first-person player rigs for Godot.

Run with Blender, not CPython:
    blender --background 9Die_Cardroom_MotionLab.blend --python tools/export-player-character.py

The export is deliberately allow-listed. Cameras, lights, the poker reference table and both red
card placeholders remain in Blender.  First-person actions are temporarily given the same semantic
names used by the third-person asset; all authoring names are restored before Blender exits.
"""

from contextlib import contextmanager
import os
from pathlib import Path

import bpy


PROJECT_ROOT = Path(r"C:\Users\Hiran Junior\Documents\Godot\9-die")
OUTPUT_DIRECTORY = Path(os.environ.get(
    "PLAYER_EXPORT_OUTPUT",
    str(PROJECT_ROOT / "Assets" / "Characters" / "Player"),
))

THIRD_PERSON_OBJECTS = {
    "Character_Armature",
    "Character_Arm_L",
    "Character_Arm_R",
    "Character_Head",
    "Character_Pants",
    "Character_Torso",
}

# The immersive first-person body is the production body without the head. Keeping its own rig is
# essential: skinning these meshes to Character_Armature was the source of the old twisted model.
FIRST_PERSON_OBJECTS = {
    "FP_Armature",
    "FP_Arm_L",
    "FP_Arm_R",
    "FP_Pants",
    "FP_Torso",
}

THIRD_PERSON_ACTIONS = {
    "Idle": "Idle",
    "Walk": "Walk",
    "Sit": "Sit",
    "IdleSit": "IdleSit",
    "SitHoldingCards": "SitHoldingCards",
    "PickCards": "PickCards",
    "IdleSitHoldingCards": "IdleSitHoldingCards",
    "IdleHoldingCardsDown": "IdleHoldingCardsDown",
}

FIRST_PERSON_ACTIONS = {
    "Idle_FP": "Idle",
    "Walk_FP": "Walk",
    "IdleSit_FP": "IdleSit",
    "PickCards_FP": "PickCards",
    "IdleSitHoldingCards_FP": "IdleSitHoldingCards",
    "IdleHoldingCardsDown_FP": "IdleHoldingCardsDown",
}

CARD_GRIP_MARKER = "CardGripMarker"
CARD_HAND_BONE = "CC_Base_L_Hand"


def select_only(names: set[str], active_armature: str) -> None:
    # A .blend can be saved in Pose Mode. Direct selection remains reliable in background mode.
    for obj in bpy.context.view_layer.objects:
        obj.select_set(False)
    for name in names:
        obj = bpy.data.objects.get(name)
        if obj is None:
            raise RuntimeError(f"Required export object is missing: {name}")
        obj.hide_viewport = False
        obj.hide_render = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = bpy.data.objects[active_armature]


def configure_action_filter(keep_actions: set[str]) -> None:
    # The glTF add-on creates this dynamic scene property only after its filter is enabled.
    if not hasattr(bpy.context.scene, "gltf_action_filter"):
        from io_scene_gltf2 import on_export_action_filter_changed

        class FilterSettings:
            export_action_filter = True

        on_export_action_filter_changed(FilterSettings(), bpy.context)
    if not hasattr(bpy.context.scene, "gltf_action_filter"):
        raise RuntimeError("Blender glTF action filter could not be initialized")

    bpy.ops.scene.gltf2_action_filter_refresh()
    for item in bpy.context.scene.gltf_action_filter:
        item.keep = item.action is not None and item.action.name in keep_actions


@contextmanager
def exported_action_names(action_map: dict[str, str]):
    """Temporarily assign collision-safe runtime names to a set of authored actions."""
    sources = {}
    for source_name in action_map:
        action = bpy.data.actions.get(source_name)
        if action is None:
            raise RuntimeError(f"Required authored action is missing: {source_name}")
        sources[source_name] = action

    renamed_colliders = []
    renamed_sources = []
    try:
        # Reserve every target name first. During FP export these colliders are the 3P actions.
        for target_name in set(action_map.values()):
            collider = bpy.data.actions.get(target_name)
            if collider is None or collider in sources.values():
                continue
            original = collider.name
            collider.name = f"__AUTHORING_HIDDEN__{original}"
            renamed_colliders.append((collider, original))

        for source_name, target_name in action_map.items():
            action = sources[source_name]
            original = action.name
            if original != target_name:
                action.name = target_name
                renamed_sources.append((action, original))

        yield set(action_map.values()), sources
    finally:
        for action, original in reversed(renamed_sources):
            action.name = original
        for action, original in reversed(renamed_colliders):
            action.name = original


def create_card_grip_marker(
    armature_name: str,
    placeholder_name: str,
) -> bpy.types.Object:
    """Create an export-only marker at the placeholder authored for this exact rig."""
    placeholder = bpy.data.objects.get(placeholder_name)
    armature = bpy.data.objects.get(armature_name)
    if placeholder is None or armature is None:
        raise RuntimeError(
            f"Missing authored card placeholder or armature: {placeholder_name}, {armature_name}")

    hand = armature.pose.bones.get(CARD_HAND_BONE)
    if hand is None:
        raise RuntimeError(f"Required card hand bone is missing: {CARD_HAND_BONE}")

    # Placeholder and marker share the same bone parent. Preserve the exact artist-authored world
    # transform after assigning the parent so Blender computes the proper bone-relative matrix.
    marker = bpy.data.objects.new(CARD_GRIP_MARKER, None)
    bpy.context.scene.collection.objects.link(marker)
    authored_world = placeholder.matrix_world.copy()
    marker.parent = armature
    marker.parent_type = "BONE"
    marker.parent_bone = CARD_HAND_BONE
    marker.matrix_world = authored_world
    marker.empty_display_type = "PLAIN_AXES"
    marker.empty_display_size = 0.04
    return marker


def export_glb(
    filename: str,
    names: set[str],
    active_armature: str,
    placeholder_name: str,
    keep_actions: set[str],
) -> None:
    marker = create_card_grip_marker(active_armature, placeholder_name)
    export_names = set(names)
    export_names.add(marker.name)
    select_only(export_names, active_armature)
    configure_action_filter(keep_actions)
    destination = OUTPUT_DIRECTORY / filename
    try:
        result = bpy.ops.export_scene.gltf(
            filepath=str(destination),
            export_format="GLB",
            use_selection=True,
            export_cameras=False,
            export_lights=False,
            export_extras=False,
            export_animations=True,
            export_animation_mode="ACTIONS",
            export_action_filter=True,
            export_nla_strips=False,
            export_frame_range=False,
            export_force_sampling=True,
            export_optimize_animation_size=True,
            export_skins=True,
            # The character has deliberately authored fifth/sixth influences. Godot imports the
            # second JOINTS/WEIGHTS set, so discarding it would deform shoulders and sleeves.
            export_all_influences=True,
            export_morph=False,
            export_apply=False,
            export_yup=True,
        )
    finally:
        bpy.data.objects.remove(marker, do_unlink=True)

    if "FINISHED" not in result:
        raise RuntimeError(f"Blender failed to export {destination}: {result}")
    print(f"EXPORTED={destination}")


OUTPUT_DIRECTORY.mkdir(parents=True, exist_ok=True)
character_rig = bpy.data.objects["Character_Armature"]
first_person_rig = bpy.data.objects["FP_Armature"]
character_action = character_rig.animation_data.action if character_rig.animation_data else None
character_slot = character_rig.animation_data.action_slot if character_rig.animation_data else None
first_person_rig.animation_data_create()
first_person_action = first_person_rig.animation_data.action
first_person_slot = first_person_rig.animation_data.action_slot

try:
    with exported_action_names(THIRD_PERSON_ACTIONS) as (keep_actions, actions):
        character_rig.animation_data.action = actions["Idle"]
        character_rig.animation_data.action_slot = actions["Idle"].slots[0]
        export_glb(
            "PlayerCharacter.glb",
            THIRD_PERSON_OBJECTS,
            "Character_Armature",
            "Cards_Holding_Placeholder",
            keep_actions,
        )

    with exported_action_names(FIRST_PERSON_ACTIONS) as (keep_actions, actions):
        first_person_rig.animation_data.action = actions["Idle_FP"]
        first_person_rig.animation_data.action_slot = actions["Idle_FP"].slots[0]
        export_glb(
            "PlayerFirstPerson.glb",
            FIRST_PERSON_OBJECTS,
            "FP_Armature",
            "Cards_Holding_Placeholder_FP",
            keep_actions,
        )
finally:
    character_rig.animation_data.action = character_action
    character_rig.animation_data.action_slot = character_slot
    first_person_rig.animation_data.action = first_person_action
    first_person_rig.animation_data.action_slot = first_person_slot
