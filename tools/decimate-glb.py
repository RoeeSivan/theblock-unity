"""Blender-side half of tools/decimate-glb.sh. Run headless, never by hand.

Reduces a .glb's triangle count while keeping everything Unity addresses it by.

WHAT MUST SURVIVE, and why each one matters here:

  * OBJECT NAMES. Unity's prefabs and the scene reference meshes inside the .glb by name. The
    jetski's renderers are `plasticGlossy_vents_plasticGlossy_0`, `whiteGlossy_body_carpaint_0`
    and so on; rename any of them and Jetski.prefab loses that renderer, and the compressed-texture
    rebind that was pointed at it in U30b round 2 silently comes undone.
  * MATERIAL SLOTS, in order. Car .glbs in this project group geometry BY MATERIAL rather than by
    part, so a slot is not cosmetic - it is the grouping.
  * THE HIERARCHY. The transforms are what put the parts in the right place.

Decimate's COLLAPSE mode preserves all three: it is a modifier on the mesh data, applied in place,
and touches neither the object nor its material assignment.

⚠ THE INPUT IS IMPORTED HERE, NOT OPENED BY BLENDER.

`blender -b file.blend` opens that file; `blender -b file.glb` does NOT - Blender only opens its
own format that way, and it starts on the default cube instead. The first version of this script
relied on `-b in.glb` the way its sibling blend-to-glb.sh legitimately does, found no meshes worth
touching, and the WRAPPER STILL REPORTED SUCCESS because an output file existed - it was simply the
untouched original. A tool that silently does nothing and says it worked is worse than one that
crashes, so the import is explicit and a failed import is fatal.

Usage (via the wrapper):
    blender -b -P decimate-glb.py -- <in.glb> <out.glb> <target-triangles>
"""

import sys

import bpy


def args() -> tuple:
    """Everything after `--`, which is how Blender hands arguments to a -P script."""
    if "--" not in sys.argv:
        raise SystemExit("decimate-glb: no arguments after `--`")
    rest = sys.argv[sys.argv.index("--") + 1:]
    if len(rest) < 3:
        raise SystemExit("decimate-glb: need <in.glb> <out.glb> <target-triangles> [mode]")
    mode = rest[3].upper() if len(rest) > 3 else "PLANAR"
    if mode not in ("PLANAR", "COLLAPSE"):
        raise SystemExit(f"decimate-glb: mode must be PLANAR or COLLAPSE, not {mode}")
    return rest[0], rest[1], int(rest[2]), mode


def load(in_path: str) -> None:
    """Empty the default scene, then import the glb into it."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=in_path)
    if not [o for o in bpy.data.objects if o.type == "MESH"]:
        raise SystemExit(f"decimate-glb: imported {in_path} and found no meshes")


def triangles(obj) -> int:
    """Triangles in a mesh, counting an n-gon as the n-2 triangles it becomes."""
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# The angle above which an edge stays hard once the baked-in normals are gone. 30° keeps a car's
# panel gaps, window frames and wheel arches crisp while letting a curved roof shade as one surface.
SHARP_ANGLE_DEGREES = 30.0


def select_only(obj) -> None:
    """Blender's mesh operators act on the selection, not on an argument."""
    for other in bpy.data.objects:
        other.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def drop_custom_normals(obj) -> bool:
    """
    Removes a glTF's baked per-corner normals so Decimate can shade what it produces.

    ⚠ <b>THIS IS WHY THE FIRST LOD1 LOOKED LIKE STATIC.</b> Every mesh in these car .glbs imports
    with `custom_normals=True` - glTF stores a normal per corner, and Blender keeps them as a custom
    split-normal layer. COLLAPSE rewrites the topology underneath that layer but the layer itself is
    interpolated onto the new corners, so a 10,430-triangle Tesla ended up wearing the shading of a
    52,096-triangle one: neighbouring faces got normals from parts of the surface that no longer
    existed, and the car rendered as speckled noise with a black windscreen at 51 m. It was not a
    triangle-budget problem at all - the silhouette was fine.

    Clearing the layer first makes Decimate's output shade from its own geometry. The hard edges
    survive separately, in the `sharp_edge` attribute, which is what `smooth_by_angle` then honours.

    Returns whether anything was cleared, so the caller only re-shades meshes that needed it.
    """
    mesh = obj.data
    if not getattr(mesh, "has_custom_normals", False):
        return False
    select_only(obj)
    bpy.ops.mesh.customdata_custom_splitnormals_clear()
    return True


def smooth_by_angle(obj) -> None:
    """Shade the decimated mesh smooth, keeping edges sharper than SHARP_ANGLE_DEGREES hard."""
    select_only(obj)
    bpy.ops.object.shade_smooth_by_angle(angle=SHARP_ANGLE_DEGREES * 3.14159265 / 180.0)


def main() -> None:
    in_path, out_path, target, mode = args()
    load(in_path)

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    before = sum(triangles(o) for o in meshes)
    if before == 0:
        raise SystemExit("decimate-glb: no mesh geometry in this file")

    print(f"decimate-glb: {len(meshes)} mesh objects, {before:,} triangles in, mode {mode}")

    # PLANAR IS THE DEFAULT, AND THE REASON IS A FAILED ATTEMPT.
    #
    # COLLAPSE at a 0.042 ratio took the jetski from 1,190,600 to 50,221 triangles and DESTROYED it:
    # rendered at 3 m the hull was shattered into shards, the panel lines were broken and the seat
    # was torn. It failed its pixel diff and was reverted. Collapse works on organic shapes; this is
    # a product-visualisation model, all smooth curves and flat panels, and collapse has no notion
    # of the hard edges that ARE the design.
    #
    # ⚠ HOW MUCH OF THAT WAS SHADING RATHER THAN SHAPE IS NOW AN OPEN QUESTION. The lot cars'
    # collapse produced exactly the same verdict - "shattered" - and it turned out to be
    # `drop_custom_normals`: the geometry was fine and the interpolated split normals were not. The
    # jetski was never re-tested with the normals cleared, so this note records a suspicion, not a
    # finding. Re-run it before believing collapse is unusable on a CAD hull.
    #
    # PLANAR dissolves only faces whose normals agree within `angle`, so a flat panel made of 4,000
    # coplanar triangles becomes a handful and a curved hull keeps enough loops to stay curved. On a
    # CAD-style asset that is where the waste actually is.
    #
    # `target` is honoured differently per mode: COLLAPSE takes a ratio, PLANAR takes an angle, so
    # here it drives a search for the loosest angle that still lands under the target.
    if before <= target:
        print(f"decimate-glb: already under {target:,} triangles - nothing to do")
    elif mode == "COLLAPSE":
        ratio = min(1.0, target / before)
        print(f"decimate-glb: collapse ratio {ratio:.4f}")
        for obj in meshes:
            # Below this a part stops being recognisable rather than getting simpler: a bolt with
            # 40 triangles decimated to 1 is a spike. Small parts are cheap; leave them alone.
            if triangles(obj) < 200:
                continue
            reshade = drop_custom_normals(obj)
            modifier = obj.modifiers.new(name="PerfDecimate", type="DECIMATE")
            modifier.decimate_type = "COLLAPSE"
            modifier.ratio = ratio
            modifier.use_collapse_triangulate = True
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.modifier_apply(modifier=modifier.name)
            if reshade:
                smooth_by_angle(obj)
    else:
        # One angle for the whole model, chosen by trying the gentle ones first and stopping at the
        # first that reaches the target. Gentle is always preferable: a 1° dissolve is invisible by
        # construction, and there is no reason to spend 15° of shape if 5° already fits.
        for angle_degrees in (1.0, 2.0, 3.0, 5.0, 7.5, 10.0, 15.0, 20.0):
            bpy.ops.wm.read_factory_settings(use_empty=True)
            bpy.ops.import_scene.gltf(filepath=in_path)
            meshes = [o for o in bpy.data.objects if o.type == "MESH"]

            for obj in meshes:
                if triangles(obj) < 200:
                    continue
                modifier = obj.modifiers.new(name="PerfDecimate", type="DECIMATE")
                modifier.decimate_type = "DISSOLVE"          # Blender's name for planar
                modifier.angle_limit = angle_degrees * 3.14159265 / 180.0
                modifier.delimit = {"NORMAL"}                 # never dissolve across a hard edge
                bpy.context.view_layer.objects.active = obj
                bpy.ops.object.modifier_apply(modifier=modifier.name)

            got = sum(triangles(o) for o in bpy.data.objects if o.type == "MESH")
            print(f"decimate-glb:   planar {angle_degrees:>4.1f}° -> {got:,} triangles")
            if got <= target:
                break

    after = sum(triangles(o) for o in bpy.data.objects if o.type == "MESH")
    print(f"decimate-glb: {after:,} triangles out ({before / max(after, 1):.1f}× lighter)")

    # Same export settings as blend-to-glb.py: glTF's own axis convention, so the result lands
    # exactly where the original did and Convert handles the rest on import.
    bpy.ops.export_scene.gltf(
        filepath=out_path,
        export_format="GLB",
        export_yup=True,
        export_apply=False,
        export_materials="EXPORT",
        export_texcoords=True,
        export_normals=True,
        use_selection=False,
    )
    print(f"decimate-glb: wrote {out_path}")


if __name__ == "__main__":
    main()
