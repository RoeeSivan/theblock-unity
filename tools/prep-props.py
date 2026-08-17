"""Blender-side half of tools/prep-props.sh. Run headless, never by hand.

Turns a raw Sketchfab download into a prop this project can place 95 times:

  * the Sketchfab root matrices (an Rx+90 / Rx-90 pair that does NOT cancel through glTFast -
    the prop lies on its side and sinks) are baked into the mesh by applying every transform;
  * the origin moves to the base centre, so `GroundY` is the whole placement and a Rigidbody's
    pose IS the foot;
  * an optional uniform scale (the cone is 0.29 m tall in the file);
  * an optional decimate to a triangle budget (the bin is 8,940 tris for a 0.8 m object);
  * every image is downsized to at most 1024 square. The bin ships three 4096² textures - as
    BC7 that is 64 MB for one bin, and U15's rule is 1024² for anything that is not a district.

Usage (via the wrapper):
    blender -b -P prep-props.py -- <in.glb> <out.glb> <scale> <max_tris> <max_texture>
"""

import sys

import bpy
import bmesh


def args() -> list:
    if "--" not in sys.argv:
        raise SystemExit("prep-props: no arguments after `--`")
    return sys.argv[sys.argv.index("--") + 1:]


def clear_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def tri_count(obj) -> int:
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def main() -> None:
    src, dst, scale, max_tris, max_texture = args()
    scale = float(scale)
    max_tris = int(max_tris)
    max_texture = int(max_texture)

    clear_scene()
    bpy.ops.import_scene.gltf(filepath=src)

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit(f"prep-props: {src} imported no mesh")

    # Bake the whole node chain into world space. Every empty above the meshes goes; what is
    # left is one or more mesh objects with identity transforms whose vertices are where the
    # file's own root matrices put them.
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    for o in list(bpy.data.objects):
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)

    # One object, so the export has one node and one origin.
    if len(meshes) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for o in meshes:
            o.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = bpy.path.display_name_from_filepath(dst)
    obj.data.name = obj.name

    # Optional scale, applied so the mesh data is in final metres.
    if abs(scale - 1.0) > 1e-6:
        obj.scale = (scale, scale, scale)
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Origin to the base centre: X/Y at the bounds centre, Z at the lowest vertex. Blender is
    # Z-up here; the exporter swaps to glTF's Y-up on the way out.
    verts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    min_x = min(v.x for v in verts); max_x = max(v.x for v in verts)
    min_y = min(v.y for v in verts); max_y = max(v.y for v in verts)
    min_z = min(v.z for v in verts)
    base = ((min_x + max_x) / 2, (min_y + max_y) / 2, min_z)
    for v in obj.data.vertices:
        v.co.x -= base[0]
        v.co.y -= base[1]
        v.co.z -= base[2]
    obj.location = (0, 0, 0)

    # Decimate to the budget, if over it. Collapse keeps UVs; the ratio is the budget over the
    # count, with a small margin because collapse overshoots.
    before = tri_count(obj)
    if max_tris > 0 and before > max_tris:
        mod = obj.modifiers.new("Decimate", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = (max_tris / before) * 0.98
        mod.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)

    # Every image down to the cap. `scale` resamples in place; the exporter then writes the
    # resampled pixels, so the .glb carries the small texture, not the 4096² one plus a hint.
    for img in bpy.data.images:
        w, h = img.size
        if w == 0 or h == 0:
            continue
        longest = max(w, h)
        if longest > max_texture:
            f = max_texture / longest
            img.scale(max(1, round(w * f)), max(1, round(h * f)))

    dims = tuple(round(v, 3) for v in obj.dimensions)
    print(f"prep-props: {src}")
    print(f"  tris {before} -> {tri_count(obj)}, dims (x,y,z Blender) {dims}, "
          f"materials {[m.name for m in obj.data.materials]}, "
          f"images {[(i.name, tuple(i.size)) for i in bpy.data.images]}")

    bpy.ops.export_scene.gltf(
        filepath=dst,
        export_format="GLB",
        export_yup=True,
        export_apply=True,
        use_selection=False,
        export_materials="EXPORT",
        export_image_format="AUTO",
        export_image_webp_fallback=True,
        export_animations=False,
        export_skins=False,
        export_cameras=False,
        export_lights=False,
    )
    print(f"prep-props: wrote {dst}")


main()
