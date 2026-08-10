"""Build the runtime poker chip pack from the high-detail Blender source.

Run with Blender, not regular Python:
    blender -b <source.blend> --python tools/export_poker_chips.py -- <output.glb>

The source file is never saved.  Each exported chip is centred, resized to a
real 40 x 3.5 mm chip and simplified conservatively so the engraved values stay
legible while redundant bevel topology is reduced aggressively.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy


DIAMETER = 0.040
THICKNESS = 0.0035
SOURCE_VALUES = (1, 5, 10, 25, 50, 100, 500, 1000, 5000)
OUTPUT_VALUES = SOURCE_VALUES + (10000,)


def output_path() -> Path:
    args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    if len(args) != 1:
        raise SystemExit("Expected one output .glb path after --")
    return Path(args[0]).resolve()


def remove_everything_except_sources() -> dict[int, bpy.types.Object]:
    sources: dict[int, bpy.types.Object] = {}
    for value in SOURCE_VALUES:
        source = bpy.data.objects.get(str(value))
        if source is None or source.type != "MESH":
            raise RuntimeError(f"Source chip {value} is missing")
        sources[value] = source

    for obj in list(bpy.data.objects):
        if obj not in sources.values():
            bpy.data.objects.remove(obj, do_unlink=True)
    return sources


def opaque(material: bpy.types.Material | None) -> None:
    if material is None:
        return
    # glTF exports this as opaque because alpha stays at one and no alpha input
    # is linked.  Do not preserve the source pack's unnecessary blend setup.
    material.diffuse_color[3] = 1.0
    if material.node_tree is not None:
        for node in material.node_tree.nodes:
            if node.type != "BSDF_PRINCIPLED":
                continue
            node.inputs["Roughness"].default_value = max(
                0.48, node.inputs["Roughness"].default_value
            )


def rename_materials(chip: bpy.types.Object, value: int) -> None:
    """Give the four source surfaces stable names for Godot's tint adapter."""
    if len(chip.material_slots) != 4:
        raise RuntimeError(f"Chip {value} has {len(chip.material_slots)} materials, expected 4")

    # Find the body by its distinctive polygon count.  The remaining central
    # stripe is the smallest non-label surface.  Label top/bottom can stay
    # separate because they have opposing normals.
    counts = [sum(p.material_index == index for p in chip.data.polygons)
              for index in range(4)]
    body_index = min(range(4), key=lambda index: abs(counts[index] - 4354))
    lines_index = min(
        (index for index in range(4) if index != body_index),
        key=lambda index: abs(counts[index] - 1216),
    )
    label_indices = [index for index in range(4)
                     if index not in (body_index, lines_index)]

    roles = {
        body_index: "ChipBody",
        lines_index: "ChipLines",
        label_indices[0]: "ChipValueTop",
        label_indices[1]: "ChipValueBottom",
    }
    for index, name in roles.items():
        material = chip.data.materials[index].copy()
        material.name = f"{name}_{value}"
        opaque(material)
        chip.data.materials[index] = material


def simplify(chip: bpy.types.Object, value: int) -> bpy.types.Object:
    """Collapse redundant bevel loops while retaining the printed value."""
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = chip
    chip.select_set(True)

    # Bake the vendor's auto-smooth modifier first.  The source is almost all
    # bevel loops, so a quarter of its original topology still leaves enough
    # geometry for the small embossed digits while cutting the runtime cost by
    # roughly three quarters.
    for source_modifier in list(chip.modifiers):
        bpy.ops.object.modifier_apply(modifier=source_modifier.name)

    modifier = chip.modifiers.new("Runtime simplification", "DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.ratio = 0.25
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    chip.name = f"Chip_{value}"

    chip.select_set(False)
    return chip


def resize_and_centre(chip: bpy.types.Object) -> None:
    # Apply the source transforms first; all subsequent dimensions are metres.
    bpy.context.view_layer.objects.active = chip
    chip.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    dimensions = chip.dimensions.copy()
    chip.scale = (
        DIAMETER / dimensions.x,
        DIAMETER / dimensions.y,
        THICKNESS / dimensions.z,
    )
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    local_centre = sum(
        (mathutils.Vector(corner) for corner in chip.bound_box),
        mathutils.Vector(),
    ) / 8.0
    chip.data.transform(mathutils.Matrix.Translation(-local_centre))
    chip.location = (0.0, 0.0, 0.0)
    chip.select_set(False)


def create_ten_thousand(template: bpy.types.Object) -> bpy.types.Object:
    """Create the missing denomination without changing runtime dimensions.

    The vendor pack stops at 5000.  We reuse its optimized physical chip and
    replace only the top/bottom number with lightweight text geometry.
    """
    chip = template.copy()
    chip.data = template.data.copy()
    chip.name = "Chip_10000"
    bpy.context.collection.objects.link(chip)

    # Replace the inherited 5000 label surfaces. The generated text below is
    # joined into this denomination node and therefore moves as one chip.
    value_indices = [
        index for index, material in enumerate(chip.data.materials)
        if material and material.name.startswith("ChipValue")
    ]
    if len(value_indices) != 2:
        raise RuntimeError(
            f"Expected two value surfaces on the 5000 template, got {value_indices}"
        )

    label_materials = []
    for ordinal, index in enumerate(value_indices):
        visible_label = chip.data.materials[index].copy()
        visible_label.name = (
            f"ChipValue{'Top' if ordinal == 0 else 'Bottom'}_10000"
        )
        opaque(visible_label)
        label_materials.append(visible_label)

    # Remove the inherited 5000 relief rather than hiding it with alpha.  This
    # avoids invisible triangles, shadow artefacts and two transparent draw
    # calls on every 10000 chip.
    import bmesh

    editable = bmesh.new()
    editable.from_mesh(chip.data)
    inherited_value_faces = [
        face for face in editable.faces if face.material_index in value_indices
    ]
    bmesh.ops.delete(editable, geom=inherited_value_faces, context="FACES")
    editable.to_mesh(chip.data)
    editable.free()

    labels = []
    for top in (True, False):
        curve = bpy.data.curves.new(
            f"ChipValue10000_{'Top' if top else 'Bottom'}", "FONT"
        )
        curve.body = "10000"
        curve.align_x = "CENTER"
        curve.align_y = "CENTER"
        curve.size = 0.0080
        curve.extrude = 0.00008
        curve.resolution_u = 2
        curve.bevel_depth = 0.000025
        curve.bevel_resolution = 0

        label = bpy.data.objects.new(curve.name, curve)
        bpy.context.collection.objects.link(label)
        label.parent = chip
        label.location = (0.0, 0.0, THICKNESS * (0.505 if top else -0.505))
        label.rotation_euler = (0.0 if top else math.pi, 0.0, 0.0)
        label.data.materials.append(label_materials[0 if top else 1])
        labels.append(label)

    # Keep every denomination as one MeshInstance3D.  Poker can display many
    # chips at once, so creating extra child nodes for only this value would be
    # a needless per-chip cost and would complicate runtime pooling.
    for label in labels:
        bpy.ops.object.select_all(action="DESELECT")
        label.select_set(True)
        bpy.context.view_layer.objects.active = label
        bpy.ops.object.convert(target="MESH")

    bpy.ops.object.select_all(action="DESELECT")
    chip.select_set(True)
    for label in labels:
        label.select_set(True)
    bpy.context.view_layer.objects.active = chip
    bpy.ops.object.join()

    return chip


def main() -> None:
    # Imported lazily so this script remains easy to lint outside Blender.
    global mathutils
    import mathutils

    destination = output_path()
    destination.parent.mkdir(parents=True, exist_ok=True)
    sources = remove_everything_except_sources()

    optimized: dict[int, bpy.types.Object] = {}
    for value, chip in sources.items():
        chip.name = f"Chip_{value}"
        rename_materials(chip, value)
        chip = simplify(chip, value)
        resize_and_centre(chip)
        optimized[value] = chip

    optimized[10000] = create_ten_thousand(optimized[5000])

    bpy.ops.object.select_all(action="DESELECT")
    for value in OUTPUT_VALUES:
        optimized[value].select_set(True)
        for child in optimized[value].children:
            child.select_set(True)

    bpy.context.view_layer.objects.active = optimized[1]
    bpy.ops.export_scene.gltf(
        filepath=str(destination),
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_materials="EXPORT",
        export_cameras=False,
        export_lights=False,
        export_yup=True,
    )

    triangles = {}
    for value, chip in optimized.items():
        mesh = chip.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
        mesh.calc_loop_triangles()
        triangles[value] = len(mesh.loop_triangles)
    print(f"POKER_CHIP_EXPORT={destination}")
    print(f"POKER_CHIP_TRIANGLES={triangles}")


if __name__ == "__main__":
    main()
