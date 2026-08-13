"""Export the authored player rigs from TestCharacter_Rigged.blend for Godot.

Run with Blender, not CPython:
    blender --background TestCharacter_Rigged.blend --python tools/export-player-character.py

The export is deliberately allow-listed. Cameras, lights, source meshes and the temporary red
card fan stay in the authoring file and can never leak into the game assets.
"""

from pathlib import Path

import bpy


PROJECT_ROOT = Path(r"C:\Users\Hiran Junior\Documents\Godot\9-die")
OUTPUT_DIRECTORY = PROJECT_ROOT / "Assets" / "Characters" / "Player"

THIRD_PERSON_OBJECTS = {
    "Character_Armature",
    "Character_Arm_L",
    "Character_Arm_R",
    "Character_Head",
    "Character_Pants",
    "Character_Torso",
}

# First person has its own authoring rig. The meshes are exact arm copies, but they are skinned to
# FP_Armature and use the dedicated arms-only action produced from the approved 3P pose. Exporting
# Character_Armature here was the reason the old first-person asset twisted in Godot.
FIRST_PERSON_OBJECTS = {
    "FP_Armature",
    "FP_Arm_L",
    "FP_Arm_R",
}

FIRST_PERSON_SOURCE_ACTION = "IdleSitHoldingCards_FP"
FIRST_PERSON_EXPORTED_ACTION = "IdleSitHoldingCards"
CARD_GRIP_MARKER = "CardGripMarker"
CARD_PLACEHOLDER = "Cards_Holding_Placeholder"
CARD_HAND_BONE = "CC_Base_L_Hand"
CARD_TRANSITION_ACTION = "SitHoldingCards"
CARD_IDLE_ACTION = "IdleSitHoldingCards"


def select_only(names: set[str], active_armature: str) -> None:
    # A .blend can be saved while its armature is in Pose Mode. Direct selection works in the
    # background exporter regardless of the saved UI mode, while object.select_all does not.
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
    # The glTF add-on owns this property, but only creates it when the filter option is toggled.
    # Initialize the dynamic Scene property through the add-on's own update callback, then mark
    # every non-production action as excluded for this export.
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


def create_card_grip_marker(armature_name: str) -> bpy.types.Object:
    placeholder = bpy.data.objects.get(CARD_PLACEHOLDER)
    armature = bpy.data.objects.get(armature_name)
    if placeholder is None or armature is None:
        raise RuntimeError("The authored card placeholder or target armature is missing")

    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    source_armature = bpy.data.objects.get("Character_Armature")
    source_hand = source_armature.pose.bones.get(CARD_HAND_BONE) if source_armature else None
    target_hand = armature.pose.bones.get(CARD_HAND_BONE)
    if source_hand is None or target_hand is None:
        raise RuntimeError(f"Required card hand bone is missing: {CARD_HAND_BONE}")

    source_hand_world = source_armature.matrix_world @ source_hand.matrix
    grip_from_hand = source_hand_world.inverted() @ placeholder.matrix_world
    target_hand_world = armature.matrix_world @ target_hand.matrix
    authored_world = target_hand_world @ grip_from_hand

    marker = bpy.data.objects.new(CARD_GRIP_MARKER, None)
    bpy.context.scene.collection.objects.link(marker)
    marker.parent = armature
    marker.parent_type = "BONE"
    marker.parent_bone = CARD_HAND_BONE
    marker.matrix_world = authored_world
    marker.empty_display_type = "PLAIN_AXES"
    marker.empty_display_size = 0.04
    return marker


def action_curves(action: bpy.types.Action) -> dict[tuple[str, int], bpy.types.FCurve]:
    """Return the curves from the single armature slot used by the authored actions."""
    if len(action.slots) != 1 or not action.layers:
        raise RuntimeError(f"Action has an unexpected slot/layer layout: {action.name}")
    strip = action.layers[0].strips[0]
    channel_bag = strip.channelbag(action.slots[0])
    if channel_bag is None:
        raise RuntimeError(f"Action has no channel bag: {action.name}")
    return {(curve.data_path, curve.array_index): curve for curve in channel_bag.fcurves}


def create_export_card_transition() -> tuple[bpy.types.Action, bpy.types.Action, str]:
    """Build an export-only transition ending exactly at the newly authored card idle.

    The approved IdleSitHoldingCards start pose changed substantially after SitHoldingCards had
    already been authored. Keeping the source action untouched lets it remain editable in the open
    Blender file, while every game export receives a seamless hand-off into the new idle.
    """
    source = bpy.data.actions.get(CARD_TRANSITION_ACTION)
    idle = bpy.data.actions.get(CARD_IDLE_ACTION)
    if source is None or idle is None:
        raise RuntimeError("The card transition or card idle action is missing")

    source_name = source.name
    source.name = f"_AUTHORING_{source_name}"
    transition = source.copy()
    transition.name = source_name

    transition_curves = action_curves(transition)
    idle_curves = action_curves(idle)
    transition_end = transition.frame_range[1]
    idle_start = idle.frame_range[0]

    missing = sorted(set(transition_curves) - set(idle_curves))
    if missing:
        bpy.data.actions.remove(transition)
        source.name = source_name
        raise RuntimeError(f"Card idle is missing {len(missing)} transition curves")

    for curve_key, curve in transition_curves.items():
        value = idle_curves[curve_key].evaluate(idle_start)
        endpoint = next(
            (point for point in curve.keyframe_points
             if abs(point.co[0] - transition_end) < 0.0001),
            None,
        )
        if endpoint is None:
            endpoint = curve.keyframe_points.insert(transition_end, value)
        endpoint.co[1] = value

    return transition, source, source_name


def export_glb(filename: str, names: set[str], active_armature: str,
               keep_actions: set[str]) -> None:
    marker = create_card_grip_marker(active_armature)
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
            export_all_influences=False,
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
character_action = character_rig.animation_data.action
character_slot = character_rig.animation_data.action_slot
export_transition = None
source_transition = None
source_transition_name = None
try:
    export_transition, source_transition, source_transition_name = create_export_card_transition()
    export_glb(
        "PlayerCharacter.glb", THIRD_PERSON_OBJECTS, "Character_Armature",
        {"Idle", "Walk", "Sit", "IdleSit", "SitHoldingCards", "IdleSitHoldingCards"},
    )
finally:
    if export_transition is not None:
        bpy.data.actions.remove(export_transition)
    if source_transition is not None:
        source_transition.name = source_transition_name

# Godot gameplay intentionally uses the same semantic clip name in both views. Rename only for the
# export transaction and restore the Blender authoring name even if glTF export fails.
first_person_action = bpy.data.actions.get(FIRST_PERSON_SOURCE_ACTION)
if first_person_action is None:
    raise RuntimeError(f"Required first-person action is missing: {FIRST_PERSON_SOURCE_ACTION}")
if bpy.data.actions.get(FIRST_PERSON_EXPORTED_ACTION) is None:
    raise RuntimeError(f"Required third-person action is missing: {FIRST_PERSON_EXPORTED_ACTION}")

third_person_action = bpy.data.actions[FIRST_PERSON_EXPORTED_ACTION]
third_person_original_name = third_person_action.name
first_person_original_name = first_person_action.name
try:
    third_person_action.name = "_THIRD_PERSON_IDLE_SIT_HOLDING_CARDS"
    first_person_action.name = FIRST_PERSON_EXPORTED_ACTION
    first_person_rig = bpy.data.objects["FP_Armature"]
    first_person_rig.animation_data_create()
    first_person_rig.animation_data.action = first_person_action
    first_person_rig.animation_data.action_slot = first_person_action.slots[0]
    export_glb(
        "PlayerFirstPerson.glb", FIRST_PERSON_OBJECTS, "FP_Armature",
        {FIRST_PERSON_EXPORTED_ACTION},
    )
finally:
    first_person_action.name = first_person_original_name
    third_person_action.name = third_person_original_name
    character_rig.animation_data.action = character_action
    character_rig.animation_data.action_slot = character_slot
