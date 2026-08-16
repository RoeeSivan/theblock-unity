"""Blender-side half of tools/blend-to-glb.sh. Run headless, never by hand.

Exports the whole scene to one GLB with glTF's own axis convention, which is exactly what
the web build's assets were made with - so a model exported here lands at the same
coordinates `config.ts` already documents, and Convert handles the rest on import.

Usage (via the wrapper):
    blender -b in.blend -P blend-to-glb.py -- out.glb
"""

import sys

import bpy


def out_path() -> str:
    """The path after `--`, which is how Blender hands arguments to a -P script."""
    if "--" not in sys.argv:
        raise SystemExit("blend-to-glb: no output path after `--`")
    return sys.argv[sys.argv.index("--") + 1]


def main() -> None:
    target = out_path()

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    tris = sum(
        sum(len(p.vertices) - 2 for p in o.data.polygons)
        for o in meshes
    )
    print(f"blend-to-glb: {len(meshes)} mesh object(s), {tris} tris -> {target}")
    for o in meshes:
        print(f"  {o.name} dim={tuple(round(v, 3) for v in o.dimensions)}")

    bpy.ops.export_scene.gltf(
        filepath=target,
        export_format="GLB",
        # +Y up: the glTF convention. Blender is Z-up, so this is the axis swap that makes
        # the file mean the same thing the district GLBs already mean.
        export_yup=True,
        # Bake modifiers into the exported mesh. Without it an unapplied Array/Mirror on the
        # stall lines exports as the single pre-modifier tile.
        export_apply=True,
        # Whole scene, not a selection - nothing is selected in a headless run.
        use_selection=False,
        export_materials="EXPORT",
        # A texture that is a .webp inside the .blend exports as one under AUTO, and that writes
        # EXT_texture_webp into extensionsREQUIRED. glTFast cannot read that extension, and because
        # it is required it refuses the whole file - the model imports as a plain DefaultAsset and
        # WorldBuilder then reports it missing, with the real reason only in the Inspector.
        #
        # This writes a PNG beside each webp, which demotes the extension to extensionsUsed and lets
        # glTFast fall back. Forcing JPEG instead would be smaller but drops alpha, and Reichman's
        # flag is an alpha decal - it would import as an opaque rectangle.
        export_image_webp_fallback=True,
        # No rig, no animation, no camera or lamp in a static prop; leaving them on writes
        # empty nodes that then show up in Unity's hierarchy as clutter.
        export_animations=False,
        export_skins=False,
        export_cameras=False,
        export_lights=False,
    )
    print(f"blend-to-glb: wrote {target}")


main()
