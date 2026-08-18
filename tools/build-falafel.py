"""Models "פלאפל הפעמונים" - a corner falafel stand - from scratch, in Blender.

Run it INSIDE a live Blender (the Blender MCP execs it), or headless:

    blender -b -P tools/build-falafel.py

It wipes the scene and rebuilds every time, so iterating is: edit this file, exec it again.
Nothing here is textured - like `police_helicopter.blend`, the whole thing is geometry plus flat
materials, which is what keeps a POI cheap and what makes the sign readable from a car.

Frame: Z up (Blender). The two street facades face -Y (the big sign) and -X (the side sign), so
after the glTF round trip Unity sees them on +Z (forward) and +X (right). Origin sits on the
ground at the centre of the footprint, so placement is `GroundY` and nothing else.
"""

import math
import re

import bpy

# ----------------------------------------------------------------------------- palette

def srgb(hex_rgb: str, alpha: float = 1.0) -> tuple:
    """Blender wants linear; every colour in this file is written the way a designer says it."""
    hex_rgb = hex_rgb.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(hex_rgb[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return (out[0], out[1], out[2], alpha)


PALETTE = {
    # shell
    "Stucco":      dict(color="#E8DFCD", rough=0.92),
    "StuccoWarm":  dict(color="#D9CDB4", rough=0.94),
    "SignWhite":   dict(color="#F6F5F1", rough=0.55),
    "CokeRed":     dict(color="#E11B22", rough=0.5),
    "TextBlue":    dict(color="#123C8E", rough=0.5),
    "TextGreen":   dict(color="#12A150", rough=0.5),
    "TextRed":     dict(color="#D42027", rough=0.5),
    "TextWhite":   dict(color="#FFFFFF", rough=0.5),
    "Tile":        dict(color="#4E9E96", rough=0.35),
    "TileDark":    dict(color="#33726C", rough=0.35),
    "Metal":       dict(color="#C9CDCF", rough=0.35, metal=0.85),
    "MetalDark":   dict(color="#5A6063", rough=0.45, metal=0.7),
    "Steel":       dict(color="#B9BEC1", rough=0.28, metal=0.9),
    "AwningRed":   dict(color="#B7302B", rough=0.8),
    "AwningWhite": dict(color="#EDE9E2", rough=0.8),
    "ChairGreen":  dict(color="#2E5B4E", rough=0.6),
    "TableWood":   dict(color="#A07B4F", rough=0.75),
    "FloorTile":   dict(color="#D6D0C2", rough=0.6),
    "Concrete":    dict(color="#B6B2A8", rough=0.95),
    "Terracotta":  dict(color="#A9573A", rough=0.9),
    "Leaf":        dict(color="#3E7A33", rough=0.85),
    "Trunk":       dict(color="#6B5A44", rough=0.9),
    "Board":       dict(color="#23282B", rough=0.8),
    "Glass":       dict(color="#BFD8DC", rough=0.08, alpha=0.22),
    "Lamp":        dict(color="#FFF6E0", rough=0.4, emit="#FFF6E0", emit_str=2.5),
    # the vitrine
    "Chips":       dict(color="#E0A93A", rough=0.7),
    "Parsley":     dict(color="#2F7D32", rough=0.8),
    "TomatoRed":   dict(color="#C0392B", rough=0.65),
    "Cucumber":    dict(color="#7BA84C", rough=0.7),
    "Onion":       dict(color="#EDE3D6", rough=0.7),
    "PepperRed":   dict(color="#B3241C", rough=0.6),
    "PepperGreen": dict(color="#4E8B2B", rough=0.6),
    "Falafel":     dict(color="#6E4A22", rough=0.85),
    "Hummus":      dict(color="#E2D6AE", rough=0.75),
    "Pickle":      dict(color="#9AA83C", rough=0.6),
    "Cabbage":     dict(color="#C7A0B4", rough=0.7),
    "Shawarma":    dict(color="#8A4B27", rough=0.8),
    "Pita":        dict(color="#DCBF8A", rough=0.85),
}

MATS = {}
ROOT = None


def build_materials() -> None:
    MATS.clear()
    for name, spec in PALETTE.items():
        m = bpy.data.materials.new("FH_" + name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes["Principled BSDF"]
        alpha = spec.get("alpha", 1.0)
        bsdf.inputs["Base Color"].default_value = srgb(spec["color"])
        bsdf.inputs["Roughness"].default_value = spec.get("rough", 0.7)
        bsdf.inputs["Metallic"].default_value = spec.get("metal", 0.0)
        bsdf.inputs["Alpha"].default_value = alpha
        if "emit" in spec:
            bsdf.inputs["Emission Color"].default_value = srgb(spec["emit"])
            bsdf.inputs["Emission Strength"].default_value = spec.get("emit_str", 1.0)
        if alpha < 1.0:
            m.blend_method = "BLEND"
            m.use_backface_culling = True
        MATS[name] = m


# ----------------------------------------------------------------------------- primitives

def _obj(name, mesh, mat_name):
    ob = bpy.data.objects.new(name, mesh)
    ob.data.materials.append(MATS[mat_name])
    bpy.context.collection.objects.link(ob)
    if ROOT is not None:
        ob.parent = ROOT
    return ob


def _mesh(name, verts, faces, mat_name):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.validate()
    me.update()
    ob = _obj(name, me, mat_name)
    for p in ob.data.polygons:
        p.use_smooth = False
    return ob


def box(name, x0, x1, y0, y1, z0, z1, mat_name):
    """Axis-aligned box by corners. Winding is outward - see the memory on inverted faces."""
    x0, x1 = sorted((x0, x1))
    y0, y1 = sorted((y0, y1))
    z0, z1 = sorted((z0, z1))
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
         (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return _mesh(name, v, f, mat_name)


def slab(name, cx, cy, sx, sy, z0, z1, mat_name):
    """Same box, given a centre and a size - reads better for furniture."""
    return box(name, cx - sx / 2, cx + sx / 2, cy - sy / 2, cy + sy / 2, z0, z1, mat_name)


def cyl(name, cx, cy, z0, z1, r, mat_name, seg=12, r_top=None):
    r_top = r if r_top is None else r_top
    v, f = [], []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        v.append((cx + r * math.cos(a), cy + r * math.sin(a), z0))
    for i in range(seg):
        a = 2 * math.pi * i / seg
        v.append((cx + r_top * math.cos(a), cy + r_top * math.sin(a), z1))
    for i in range(seg):
        j = (i + 1) % seg
        f.append((i, j, j + seg, i + seg))
    f.append(tuple(range(seg - 1, -1, -1)))
    f.append(tuple(range(seg, 2 * seg)))
    return _mesh(name, v, f, mat_name)


def ball(name, cx, cy, cz, r, mat_name, seg=8, ring=6, squash=1.0):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=ring, radius=r,
                                         location=(cx, cy, cz))
    ob = bpy.context.active_object
    ob.name = name
    ob.scale.z = squash
    ob.data.materials.append(MATS[mat_name])
    for p in ob.data.polygons:
        p.use_smooth = False
    if ROOT is not None:
        ob.parent = ROOT
        ob.matrix_parent_inverse = ROOT.matrix_world.inverted()
    return ob


# ----------------------------------------------------------------------------- signage text

HEB_FONTS = [
    # what `police_helicopter.blend` used for משטרת ישראל - same font, same reversing rule
    "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
    "/System/Library/Fonts/ArialHB.ttc",
    "/System/Library/Fonts/Supplemental/NewPeninimMT.ttc",
]
# Coca-Cola's wordmark is Spencerian script. Snell Roundhand Black is the closest thing macOS
# ships, and it is the only reason the panel reads as a Coke sign rather than as red paint.
# Blender cannot open a .ttc, so the face is split out once with fontTools into source-assets
# (gitignored - it is Apple's font). Savoye is the fallback and it is far too thin.
SNELL_TTC = "/System/Library/Fonts/Supplemental/SnellRoundhand.ttc"
SNELL_TTF = "/Users/roeesivan/TheBlockUnity/source-assets/fonts/SnellRoundhand-Black.ttf"
SCRIPT_FONTS = [
    SNELL_TTF,
    "/System/Library/Fonts/Supplemental/Savoye LET.ttc",
    "/System/Library/Fonts/Supplemental/Apple Chancery.ttf",
]
_FONTS = {}


def split_snell():
    import os
    import subprocess
    if os.path.exists(SNELL_TTF) or not os.path.exists(SNELL_TTC):
        return
    os.makedirs(os.path.dirname(SNELL_TTF), exist_ok=True)
    subprocess.run(["python3", "-c", (
        "from fontTools.ttLib import TTCollection;"
        f"c=TTCollection({SNELL_TTC!r});"
        "[f.save(%r) for f in c.fonts if 'Black' in f['name'].getDebugName(4)]" % SNELL_TTF
    )], check=False)


def font(kind="hebrew"):
    if kind not in _FONTS:
        _FONTS[kind] = None
        if kind == "script":
            split_snell()
        for path in (HEB_FONTS if kind == "hebrew" else SCRIPT_FONTS):
            try:
                _FONTS[kind] = bpy.data.fonts.load(path)
                break
            except Exception:
                continue
    return _FONTS[kind]


def hebrew_font():
    return font("hebrew")


def rtl(s: str) -> str:
    """Blender lays glyphs out left to right and does no bidi. Reverse the string, then put the
    digit runs back the way round they were - `מאז 1988` must not become `מאז 8891`."""
    return re.sub(r"\d+", lambda m: m.group(0)[::-1], s[::-1])


def sign_text(name, body, size, mat_name, loc, facing="-Y", bold=0.008, extrude=0.012,
              align="CENTER"):
    """A line of signage, converted straight to mesh so the export carries no curve data."""
    cu = bpy.data.curves.new(name, type="FONT")
    cu.body = rtl(body)
    cu.size = size
    cu.align_x = align
    cu.align_y = "BOTTOM"
    cu.offset = bold           # poor man's bold: fattens the outline
    cu.extrude = extrude
    cu.resolution_u = 2        # signage is read at 10 m, not 10 cm
    f = hebrew_font()
    if f:
        cu.font = f
    ob = _obj(name, cu, mat_name)
    ob.location = loc
    ob.rotation_euler = (math.pi / 2, 0, 0) if facing == "-Y" else (math.pi / 2, 0, -math.pi / 2)
    return ob


def latin_text(name, body, size, mat_name, loc, facing="-Y", bold=0.01, extrude=0.012,
               shear=0.0, kind="script"):
    ob = sign_text(name, body[::-1], size, mat_name, loc, facing, bold, extrude)  # un-reverse
    ob.data.body = body
    ob.data.shear = shear
    f = font(kind)
    if f:
        ob.data.font = f
    return ob


def ribbon(name, pts, widths, plane, mat_name, facing="-Y"):
    """A tapered flat stroke on a sign face. The Coke swashes are not in any font - the tails
    under `Coca` and over `ola` are what make the wordmark read as the wordmark."""
    v = []
    for (u, w_), wd in zip(pts, widths):
        if facing == "-Y":
            v += [(u, plane, w_ - wd / 2), (u, plane, w_ + wd / 2)]
        else:
            v += [(plane, u, w_ - wd / 2), (plane, u, w_ + wd / 2)]
    f = []
    for i in range(len(pts) - 1):
        a = 2 * i
        f.append((a, a + 2, a + 3, a + 1) if facing == "-Y" else (a, a + 1, a + 3, a + 2))
    return _mesh(name, v, f, mat_name)


def coke_swash(name, u0, u1, v0, dip, flick, w_max, plane, mat_name, facing="-Y", steps=14):
    """One tail: an arc that sags (or rises) across the word and tapers to a point at both ends."""
    pts, wds = [], []
    for i in range(steps + 1):
        t = i / steps
        u = u0 + (u1 - u0) * t
        v = v0 + dip * math.sin(math.pi * t) + flick * (t ** 3)
        pts.append((u, v))
        wds.append(w_max * max(0.06, math.sin(math.pi * t) ** 0.45))
    return ribbon(name, pts, wds, plane, mat_name, facing)


# ----------------------------------------------------------------------------- the building

# footprint
X0, X1 = -3.60, 3.60          # street corner is at (X0, Y0)
Y0, Y1 = -2.60, 2.60
WALL = 0.15
H = 3.00                       # top of the walls
SILL = 0.95                    # top of the tiled wainscot
HEAD = 2.62                    # top of the glazing
DOOR = (-0.95, 0.65)           # the front opening, in X


def shell():
    box("Floor", X0, X1, Y0, Y1, 0.0, 0.06, "FloorTile")
    box("Wall_Back", X0, X1, Y1 - WALL, Y1, 0.0, H, "Stucco")
    box("Wall_Right", X1 - WALL, X1, Y0, Y1 - WALL, 0.0, H, "Stucco")
    # the corner pier the whole thing hangs off, and the two returns beside the glazing
    box("Pier_Corner", X0, X0 + 0.38, Y0, Y0 + 0.38, 0.0, H, "StuccoWarm")
    box("Pier_FrontRight", X1 - 0.30, X1, Y0, Y0 + WALL, 0.0, H, "StuccoWarm")
    box("Pier_SideBack", X0, X0 + WALL, Y1 - 0.30, Y1, 0.0, H, "StuccoWarm")
    # spandrel over the glazing, both street faces
    box("Spandrel_Front", X0, X1, Y0, Y0 + 0.12, HEAD, H, "Stucco")
    box("Spandrel_Side", X0, X0 + 0.12, Y0, Y1, HEAD, H, "Stucco")
    # roof, parapet, and the two masts that stand over the sign in the reference
    box("Roof", X0 - 0.10, X1 + 0.10, Y0 - 0.10, Y1 + 0.10, H, H + 0.14, "Concrete")
    box("Parapet_Back", X0 - 0.10, X1 + 0.10, Y1 - 0.06, Y1 + 0.10, H + 0.14, H + 0.42, "Stucco")
    box("Parapet_Right", X1 - 0.06, X1 + 0.10, Y0 - 0.10, Y1 + 0.10, H + 0.14, H + 0.42, "Stucco")
    cyl("Mast_A", -1.15, 0.10, H, H + 3.30, 0.055, "MetalDark", seg=8)
    cyl("Mast_B", 0.95, 0.35, H, H + 2.95, 0.050, "MetalDark", seg=8)


def storefront():
    """Tiled wainscot, glass, mullions - on the two street faces, with the door left open."""
    fy0, fy1 = Y0 + 0.02, Y0 + 0.14          # the front glazing plane
    sx0, sx1 = X0 + 0.02, X0 + 0.14          # the side glazing plane

    # --- front (-Y), broken by the entrance
    for i, (a, b) in enumerate([(X0 + 0.38, DOOR[0]), (DOOR[1], X1 - 0.30)]):
        box(f"Wainscot_F{i}", a, b, fy0, fy1, 0.06, SILL, "Tile")
        box(f"WainscotCap_F{i}", a, b, fy0 - 0.015, fy1 + 0.015, SILL, SILL + 0.05, "TileDark")
        box(f"Glass_F{i}", a + 0.04, b - 0.04, fy0 + 0.05, fy0 + 0.09, SILL + 0.05, HEAD, "Glass")
        box(f"Mull_F{i}a", a, a + 0.05, fy0, fy1, SILL, HEAD, "Metal")
        box(f"Mull_F{i}b", b - 0.05, b, fy0, fy1, SILL, HEAD, "Metal")
        box(f"Head_F{i}", a, b, fy0, fy1, HEAD - 0.06, HEAD, "Metal")
        mid = (a + b) / 2
        box(f"Mull_F{i}m", mid - 0.025, mid + 0.025, fy0, fy1, SILL, HEAD, "Metal")
    # door jambs and header
    box("DoorJamb_L", DOOR[0], DOOR[0] + 0.05, fy0, fy1, 0.06, HEAD, "Metal")
    box("DoorJamb_R", DOOR[1] - 0.05, DOOR[1], fy0, fy1, 0.06, HEAD, "Metal")
    box("DoorHead", DOOR[0], DOOR[1], fy0, fy1, HEAD - 0.08, HEAD, "Metal")

    # --- side (-X), unbroken
    a, b = Y0 + 0.38, Y1 - 0.30
    box("Wainscot_S", sx0, sx1, a, b, 0.06, SILL, "Tile")
    box("WainscotCap_S", sx0 - 0.015, sx1 + 0.015, a, b, SILL, SILL + 0.05, "TileDark")
    box("Glass_S", sx0 + 0.05, sx0 + 0.09, a + 0.04, b - 0.04, SILL + 0.05, HEAD, "Glass")
    box("Head_S", sx0, sx1, a, b, HEAD - 0.06, HEAD, "Metal")
    for i in range(1, 3):
        y = a + (b - a) * i / 3
        box(f"Mull_S{i}", sx0, sx1, y - 0.025, y + 0.025, SILL, HEAD, "Metal")


# ----------------------------------------------------------------------------- the sign

# In BOTH reference photographs the sign is not on the building. It rides the outer edge of the
# projecting canopy, out over the pavement, with the glazing set back in shadow behind it - which
# is why the shop reads from a car before any of the glass does.
CANOPY_Z = 2.78                # underside of the canopy slab
CAN_Y = Y0 - 2.15              # its outer edge, front
CAN_X = X0 - 1.95              # its outer edge, side
SZ0, SZ1 = 2.72, 3.60          # the fascia band, bottom and top
FRONT_FACE = CAN_Y - 0.06      # its outward plane
SIDE_FACE = CAN_X - 0.06


def coke_panel(name, x_a, x_b, z_a, z_b, face, axis="front"):
    """A Coke lightbox: white frame, red field, the Spencerian wordmark with both swashes, and
    `טעם החיים` under it - the layout of the user's reference photograph, element for element."""
    facing = "-Y" if axis == "front" else "-X"
    cu = (x_a + x_b) / 2                     # centre along the panel's long axis
    w = x_b - x_a
    h = z_b - z_a
    cv = z_a + h * 0.50                      # the wordmark's baseline; the tagline sits under it

    def plate(mat, inset, depth0, depth1):
        if axis == "front":
            box(f"{name}_{mat}", x_a + inset, x_b - inset, face - depth1, face - depth0,
                z_a + inset, z_b - inset, mat)
        else:
            box(f"{name}_{mat}", face + depth0, face + depth1, x_a + inset, x_b - inset,
                z_a + inset, z_b - inset, mat)

    plate("SignWhite", 0.0, 0.0, 0.06)       # the aluminium box the panel is mounted in
    plate("CokeRed", 0.035, 0.062, 0.075)
    plane = face - 0.085 if axis == "front" else face + 0.085

    size = w * 0.205
    cv = z_a + h * 0.44
    latin_text(f"{name}_Word", "Coca-Cola", size, "TextWhite",
               (cu - w * 0.02, plane, cv) if axis == "front" else (plane, cu - w * 0.02, cv),
               facing=facing, bold=size * 0.02, extrude=0.008)
    # tail one: leaves the foot of the first C, swoops under `oca` and points up past centre.
    # In Snell Black the word is ~4.1 em wide, so the first C's foot is 2.0 em left of centre.
    coke_swash(f"{name}_SwashA", cu - size * 1.85, cu + size * 0.35, cv - size * 0.02,
               -size * 0.20, size * 0.20, size * 0.10, plane, "TextWhite", facing)
    # tail two: leaves the second C's crown, rides over `ola` and hooks up at the end
    coke_swash(f"{name}_SwashB", cu + size * 0.20, cu + size * 1.95, cv + size * 0.52,
               size * 0.06, size * 0.14, size * 0.08, plane, "TextWhite", facing)
    sign_text(f"{name}_Tag", "טעם החיים", w * 0.068, "TextWhite",
              (cu, plane, z_a + h * 0.13) if axis == "front" else (plane, cu, z_a + h * 0.13),
              facing=facing, bold=0.002)
    # the ® mark: a flat ring, which is all it is at this distance
    reg = size * 0.05
    ru, rv = cu + size * 2.12, cv + size * 0.02
    ring = 8
    v, f = [], []
    for i in range(ring):
        a = 2 * math.pi * i / ring
        for r in (reg * 0.62, reg):
            du, dv = r * math.cos(a), r * math.sin(a)
            v.append((ru + du, plane - 0.004, rv + dv) if axis == "front"
                     else (plane + 0.004, ru + du, rv + dv))
    for i in range(ring):
        j = (i + 1) % ring
        f.append((2 * i, 2 * j, 2 * j + 1, 2 * i + 1) if axis == "front"
                 else (2 * i, 2 * i + 1, 2 * j + 1, 2 * j))
    _mesh(f"{name}_Reg", v, f, "TextWhite")


def signage():
    # --- the band itself, wrapping the canopy's corner
    box("Fascia_Front", CAN_X - 0.06, X1 + 0.14, FRONT_FACE, CAN_Y + 0.30, SZ0, SZ1, "SignWhite")
    box("Fascia_Side", SIDE_FACE, CAN_X + 0.30, CAN_Y - 0.06, 1.55, SZ0, SZ1, "SignWhite")

    # --- front face: a small red panel at the corner end, a big one at the far end, the name
    #     across the white between them. The blue line is the biggest thing on the building.
    # both panels stand clear of the corner itself - butted together they read as one red blob
    coke_panel("Coke_FrontL", CAN_X + 0.55, CAN_X + 1.75, SZ0 + 0.05, SZ1 - 0.05, FRONT_FACE)
    coke_panel("Coke_FrontR", 1.28, X1 + 0.10, SZ0 + 0.05, SZ1 - 0.05, FRONT_FACE)
    sign_text("Sign_Name", "פלאפל הפעמונים", 0.60, "TextBlue",
              (-1.20, FRONT_FACE - 0.02, 3.12), facing="-Y", bold=0.018)
    sign_text("Sign_Kind", "פלאפל ושווארמה", 0.29, "TextGreen",
              (0.10, FRONT_FACE - 0.02, 2.79), facing="-Y", bold=0.009)
    sign_text("Sign_Since", "מאז 1988", 0.29, "TextRed",
              (-2.65, FRONT_FACE - 0.02, 2.79), facing="-Y", bold=0.009)

    # --- side face: the name stacked over two lines, as in the older photograph
    coke_panel("Coke_Side", CAN_Y + 0.55, CAN_Y + 1.55, SZ0 + 0.05, SZ1 - 0.05, SIDE_FACE,
               axis="side")
    sign_text("SignS_Name1", "פלאפל", 0.36, "TextBlue",
              (SIDE_FACE - 0.02, -0.83, 3.20), facing="-X", bold=0.013)
    sign_text("SignS_Name2", "הפעמונים", 0.36, "TextBlue",
              (SIDE_FACE - 0.02, -0.83, 2.88), facing="-X", bold=0.013)
    sign_text("SignS_Kind", "פלאפל שווארמה", 0.17, "TextGreen",
              (SIDE_FACE - 0.02, -0.83, 2.74), facing="-X", bold=0.006)


# ----------------------------------------------------------------------------- the sidewalk


def canopy():
    """The slab the sign hangs off, and the thin posts that carry it out over the pavement."""
    box("Canopy_Front", CAN_X, X1 + 0.14, CAN_Y, Y0 + 0.12, CANOPY_Z, CANOPY_Z + 0.10, "SignWhite")
    box("Canopy_Side", CAN_X, X0 + 0.12, CAN_Y, 1.55, CANOPY_Z, CANOPY_Z + 0.10, "SignWhite")
    box("Canopy_Soffit", CAN_X + 0.04, X1 + 0.10, CAN_Y + 0.04, Y0 + 0.12,
        CANOPY_Z - 0.03, CANOPY_Z, "StuccoWarm")
    for i, (x, y) in enumerate([(X1 - 0.25, CAN_Y + 0.20), (1.15, CAN_Y + 0.20),
                                (-1.55, CAN_Y + 0.20), (CAN_X + 0.22, CAN_Y + 0.20),
                                (CAN_X + 0.22, -0.60), (CAN_X + 0.22, 1.25)]):
        box(f"CanopyPost_{i}", x - 0.05, x + 0.05, y - 0.05, y + 0.05, 0.0, CANOPY_Z, "Metal")
        box(f"CanopyFoot_{i}", x - 0.10, x + 0.10, y - 0.10, y + 0.10, 0.0, 0.05, "MetalDark")

    # the striped valance on the building itself, over the door and under the canopy
    vy = Y0 - 0.52
    box("Awning_Box", -2.30, 1.70, vy, Y0 - 0.06, 2.34, 2.48, "MetalDark")
    for i in range(12):
        a = -2.30 + i * (4.00 / 12)
        b = a + (4.00 / 12)
        box(f"Awning_{i}", a, b, vy - 0.04, vy, 2.00, 2.36,
            "AwningRed" if i % 2 == 0 else "AwningWhite")


def table(name, cx, cy, top="TableWood", leg="MetalDark", r=0.36):
    slab(name + "_Top", cx, cy, r * 2, r * 2, 0.72, 0.76, top)
    cyl(name + "_Post", cx, cy, 0.03, 0.72, 0.035, leg, seg=8)
    slab(name + "_Foot", cx, cy, 0.44, 0.44, 0.0, 0.03, leg)


def chair(name, cx, cy, rot, mat="ChairGreen"):
    """Six boxes, built at the origin and rotated into place - bistro chair, no more."""
    parts = [
        (0.0, 0.0, 0.44, 0.06, 0.44, 0.44),        # seat
        (0.0, 0.20, 0.68, 0.42, 0.36, 0.05),       # back rest
        (-0.19, -0.19, 0.22, 0.04, 0.04, 0.44),    # legs
        (0.19, -0.19, 0.22, 0.04, 0.04, 0.44),
        (-0.19, 0.19, 0.22, 0.04, 0.04, 0.44),
        (0.19, 0.19, 0.22, 0.04, 0.04, 0.44),
    ]
    c, s = math.cos(rot), math.sin(rot)
    for i, (px, py, pz, sx, sy, sz) in enumerate(parts):
        if i == 1:   # the back is a vertical plate, so read its size differently
            sx, sy, sz = sx, 0.05, sy
            pz = 0.66
        rx, ry = px * c - py * s, px * s + py * c
        box(f"{name}_{i}", cx + rx - sx / 2, cx + rx + sx / 2,
            cy + ry - sy / 2, cy + ry + sy / 2, pz - sz / 2, pz + sz / 2, mat)


def planter(name, cx, cy, scale=1.0):
    cyl(name + "_Pot", cx, cy, 0.0, 0.34 * scale, 0.30 * scale, "Terracotta",
        seg=10, r_top=0.26 * scale)
    cyl(name + "_Soil", cx, cy, 0.30 * scale, 0.33 * scale, 0.25 * scale, "Trunk", seg=10)
    cyl(name + "_Trunk", cx, cy, 0.30 * scale, 1.05 * scale, 0.045 * scale, "Trunk", seg=6)
    for i, (dx, dy, dz, r) in enumerate([(0, 0, 1.35, 0.42), (0.22, -0.14, 1.12, 0.30),
                                         (-0.20, 0.16, 1.16, 0.28), (0.05, 0.22, 1.48, 0.24)]):
        ball(f"{name}_Leaf{i}", cx + dx * scale, cy + dy * scale, dz * scale,
             r * scale, "Leaf", squash=0.72)


def street_furniture():
    table("TableOut_A", X0 - 0.95, Y0 - 1.25)
    table("TableOut_B", 0.35, Y0 - 1.35)
    table("TableOut_C", 2.55, Y0 - 1.15)
    chair("ChairOut_A1", X0 - 0.95, Y0 - 0.60, 0.0)
    chair("ChairOut_A2", X0 - 0.95, Y0 - 1.90, math.pi)
    chair("ChairOut_B1", -0.35, Y0 - 1.35, math.pi / 2)
    chair("ChairOut_B2", 1.05, Y0 - 1.35, -math.pi / 2)
    chair("ChairOut_C1", 2.55, Y0 - 0.50, 0.25)
    chair("ChairOut_C2", 2.55, Y0 - 1.80, math.pi - 0.2)
    table("TableSide_A", CAN_X + 0.85, 0.45)
    chair("ChairSide_1", CAN_X + 0.85, 1.05, math.pi)
    chair("ChairSide_2", CAN_X + 0.85, -0.15, 0.0)
    planter("Planter_Corner", CAN_X + 0.55, Y0 - 1.60, 1.0)
    planter("Planter_Front", 1.65, Y0 - 1.95, 0.85)
    cyl("Bollard", CAN_X - 0.55, Y0 - 1.90, 0.0, 0.86, 0.15, "Concrete", seg=10, r_top=0.13)
    ball("Bollard_Cap", CAN_X - 0.55, Y0 - 1.90, 0.86, 0.15, "Concrete", squash=0.45)


# ----------------------------------------------------------------------------- inside

CTOP = 0.98                    # counter height
VTOP = 1.58                    # top of the vitrine glass


def tray(name, cx, cy, w, d, fill, kind="mound", count=10, seed=0.0):
    """A gastronorm pan and what is in it. `kind` decides how the food reads at a glance."""
    z = CTOP + 0.01
    top = z + 0.11
    box(name + "_Pan", cx - w / 2, cx + w / 2, cy - d / 2, cy + d / 2, z, top, "Steel")
    box(name + "_Rim", cx - w / 2 - 0.014, cx + w / 2 + 0.014, cy - d / 2 - 0.014,
        cy + d / 2 + 0.014, top - 0.015, top + 0.005, "MetalDark")
    inner_w, inner_d = w - 0.04, d - 0.04
    # every pan is heaped: a coloured slab fills it to the brim, and the pieces sit on that.
    # An almost-empty pan of six cubes read as an empty pan from the door.
    box(name + "_Heap", cx - inner_w / 2, cx + inner_w / 2, cy - inner_d / 2, cy + inner_d / 2,
        z + 0.02, top + 0.005, fill)
    if kind == "mound":
        ball(name + "_Fill", cx, cy, top, min(inner_w, inner_d) * 0.60, fill, squash=0.55)
    elif kind == "cubes":
        n = int(math.sqrt(count)) + 1
        for i in range(count):
            fx = ((i * 7 + seed * 13) % n) / max(n - 1, 1) - 0.5
            fy = ((i * 5 + seed * 11) % n) / max(n - 1, 1) - 0.5
            s = 0.030 + 0.012 * ((i * 3 + int(seed)) % 3)
            box(f"{name}_C{i}", cx + fx * inner_w * 0.85 - s / 2, cx + fx * inner_w * 0.85 + s / 2,
                cy + fy * inner_d * 0.85 - s / 2, cy + fy * inner_d * 0.85 + s / 2,
                top, top + s * (0.6 + 0.4 * ((i + int(seed)) % 2)), fill)
    elif kind == "sticks":     # chips, heaped and every which way
        for i in range(count):
            fx = ((i * 5) % 6) / 5.0 - 0.5
            fy = ((i * 3) % 4) / 3.0 - 0.5
            a = (i * 37) % 180 * math.pi / 180
            L, W = 0.10, 0.018
            dx, dy = math.cos(a) * L / 2, math.sin(a) * L / 2
            lift = 0.018 * (i % 4)
            box(f"{name}_S{i}", cx + fx * inner_w * 0.7 - abs(dx) - W, cx + fx * inner_w * 0.7 + abs(dx) + W,
                cy + fy * inner_d * 0.7 - abs(dy) - W, cy + fy * inner_d * 0.7 + abs(dy) + W,
                top + lift, top + lift + 0.02, fill)
    elif kind == "balls":
        for i in range(count):
            fx = ((i % 4) / 3.0 - 0.5) * 0.85
            fy = ((i // 4) / 2.0 - 0.5) * 0.85
            ball(f"{name}_B{i}", cx + fx * inner_w, cy + fy * inner_d, top + 0.03, 0.034, fill)


def counter_and_vitrine():
    cx0, cx1 = -1.55, X1 - WALL - 0.05
    cy0, cy1 = 1.05, 2.00
    box("Counter_Body", cx0, cx1, cy0, cy1, 0.0, CTOP - 0.04, "SignWhite")
    box("Counter_Kick", cx0, cx1, cy0 - 0.01, cy0 + 0.08, 0.0, 0.10, "MetalDark")
    box("Counter_Top", cx0 - 0.05, cx1, cy0 - 0.06, cy1, CTOP - 0.04, CTOP, "Steel")

    # the vitrine: a steel frame with glass on three sides, food inside
    vx0, vx1 = cx0 + 0.05, cx1 - 0.15
    vy0, vy1 = cy0 + 0.02, cy0 + 0.78
    for name, (a, b, c, d) in {
        "L": (vx0, vx0 + 0.04, vy0, vy1), "R": (vx1 - 0.04, vx1, vy0, vy1),
    }.items():
        box("Vitrine_P" + name, a, b, c, d, CTOP, VTOP, "Steel")
    box("Vitrine_GlassFront", vx0, vx1, vy0, vy0 + 0.03, CTOP + 0.02, VTOP - 0.06, "Glass")
    box("Vitrine_GlassTop", vx0, vx1, vy0, vy1, VTOP - 0.06, VTOP - 0.03, "Glass")
    box("Vitrine_Rail", vx0, vx1, vy0 - 0.02, vy0 + 0.05, VTOP - 0.06, VTOP, "Steel")
    box("Vitrine_Shelf", vx0, vx1, vy0, vy1, CTOP, CTOP + 0.01, "Steel")

    # the food, left to right behind the glass
    span = vx1 - vx0
    cy = (vy0 + vy1) / 2
    w, d = span / 6.4, (vy1 - vy0) * 0.66
    xs = [vx0 + span * f for f in (0.10, 0.26, 0.42, 0.58, 0.74, 0.90)]
    # the pans stand in a back row (deep) and a front row (shallow), the way a falafel bar is
    # laid out so the server can reach both. Front row is what the customer sees first.
    back = vy0 + (vy1 - vy0) * 0.64
    front = vy0 + (vy1 - vy0) * 0.22
    d_back, d_front = (vy1 - vy0) * 0.50, (vy1 - vy0) * 0.30
    tray("Food_Chips", xs[0], back, w, d_back, "Chips", kind="sticks", count=22)
    tray("Food_Salad", xs[1], back, w, d_back, "TomatoRed", kind="cubes", count=18, seed=1)
    tray("Food_Peppers", xs[2], back, w, d_back, "PepperRed", kind="cubes", count=16, seed=5)
    tray("Food_Parsley", xs[3], back, w, d_back, "Parsley", kind="mound")
    tray("Food_Falafel", xs[4], back, w, d_back, "Falafel", kind="balls", count=8)
    tray("Food_Hummus", xs[5], back, w, d_back, "Hummus", kind="mound")
    tray("Food_Cabbage", xs[0], front, w, d_front, "Cabbage", kind="cubes", count=10, seed=4)
    tray("Food_Cucumber", xs[1], front, w, d_front, "Cucumber", kind="cubes", count=12, seed=3)
    tray("Food_PeppersG", xs[2], front, w, d_front, "PepperGreen", kind="cubes", count=10, seed=7)
    tray("Food_Onion", xs[3], front, w, d_front, "Onion", kind="cubes", count=10, seed=6)
    tray("Food_Pickle", xs[4], front, w, d_front, "Pickle", kind="cubes", count=10, seed=2)
    tray("Food_Tahini", xs[5], front, w, d_front, "Onion", kind="mound")

    # pita stack and a till at the open end of the counter
    for i in range(4):
        cyl(f"Pita_{i}", cx1 - 0.45, cy1 - 0.35, CTOP + i * 0.035, CTOP + 0.03 + i * 0.035,
            0.13, "Pita", seg=10)
    box("Till", cx1 - 0.95, cx1 - 0.60, cy1 - 0.50, cy1 - 0.18, CTOP, CTOP + 0.16, "MetalDark")
    box("Till_Screen", cx1 - 0.92, cx1 - 0.63, cy1 - 0.34, cy1 - 0.31, CTOP + 0.16, CTOP + 0.36,
        "Board")

    # the shawarma spit at the open end of the counter, where the customer sees it turn - the
    # sign promises one, and it is the tallest thing behind the glass
    sx, sy = cx0 - 0.55, cy0 + 0.40
    box("Spit_Base", sx - 0.32, sx + 0.32, sy - 0.30, sy + 0.30, 0.0, 0.90, "MetalDark")
    box("Spit_Tray", sx - 0.34, sx + 0.34, sy - 0.32, sy + 0.32, 0.90, 0.96, "Steel")
    box("Spit_Back", sx - 0.30, sx + 0.30, sy + 0.22, sy + 0.28, 0.96, 2.20, "Steel")
    for i in range(3):
        box(f"Spit_Burner{i}", sx - 0.26, sx + 0.26, sy + 0.19, sy + 0.22,
            1.15 + i * 0.32, 1.35 + i * 0.32, "AwningRed")
    cyl("Spit_Rod", sx, sy, 0.96, 2.15, 0.022, "Steel", seg=8)
    cyl("Spit_Meat", sx, sy, 1.08, 1.98, 0.23, "Shawarma", seg=14, r_top=0.10)
    ball("Spit_Onion", sx, sy, 2.02, 0.07, "Onion", squash=0.8)


# The real menu, priced in ₪ - read from 10bis (restaurant 47184) on 2026-08-18. Two columns
# on the board over the counter, the way the shop hangs it. Whatever the falafel POI eventually
# charges the player, THESE are the numbers on the wall.
MENU = [
    ("פלאפל | סביח | סלטים", [
        ("פיתה פלאפל", 35), ("פיתה סביח", 38), ("פיתה סלטים", 35),
        ("פלאפל בלאפה", 45), ("סביח בלאפה", 48), ("חמגשית פלאפל", 45),
    ]),
    ("שווארמה | שניצל", [
        ("פיתה שווארמה", 68), ("לאפה שווארמה", 78), ("חמגשית שווארמה", 78),
        ("פיתה שניצל", 54), ("שניצל בלאפה", 64), ("5 כדורי פלאפל", 15),
    ]),
]


def menu_board():
    bx0, bx1 = -1.55, 1.55
    bz0, bz1 = 1.92, 2.72
    face = Y1 - WALL - 0.05
    box("MenuBoard", bx0, bx1, face, face + 0.03, bz0, bz1, "Board")
    box("MenuBoard_Frame", bx0 - 0.03, bx1 + 0.03, face + 0.005, face + 0.035, bz0 - 0.03,
        bz1 + 0.03, "TableWood")
    # A Hebrew menu runs right to left: names hug the RIGHT edge of each column, prices the
    # left. The reader stands inside facing +Y, so their right is +X, and the text (already
    # reversed by `rtl`) is laid out in +X - so "right edge" is the larger X, `align="RIGHT"`.
    col_w = (bx1 - bx0) / 2
    line_h = 0.088
    for c, (title, items) in enumerate(MENU):
        cx0 = bx1 - (c + 1) * col_w                  # first column on the reader's right
        right = cx0 + col_w - 0.06
        left = cx0 + 0.06
        z = bz1 - 0.16
        sign_text(f"Menu_T{c}", title, 0.075, "TextGreen", (right, face - 0.01, z),
                  facing="-Y", bold=0.002, extrude=0.004, align="RIGHT")
        for i, (item, price) in enumerate(items):
            z = bz1 - 0.30 - i * line_h
            sign_text(f"Menu_{c}_{i}", item, 0.062, "TextWhite", (right, face - 0.01, z),
                      facing="-Y", bold=0.001, extrude=0.004, align="RIGHT")
            sign_text(f"MenuP_{c}_{i}", f"₪{price}", 0.062, "Chips", (left, face - 0.01, z),
                      facing="-Y", bold=0.001, extrude=0.004, align="LEFT")


def interior_fit():
    # wall tiles inside, and the ceiling
    box("InWall_TileBack", X0 + 0.14, X1 - WALL, Y1 - WALL - 0.03, Y1 - WALL, 0.06, 1.45, "Tile")
    box("InWall_TileRight", X1 - WALL - 0.03, X1 - WALL, Y0 + 0.14, Y1 - WALL, 0.06, 1.45, "Tile")
    box("Ceiling", X0 + 0.10, X1 - WALL, Y0 + 0.12, Y1 - WALL, H - 0.09, H - 0.02, "SignWhite")
    for i, x in enumerate((-1.8, 0.9)):
        box(f"Light_{i}", x - 0.60, x + 0.60, -0.60, -0.44, H - 0.14, H - 0.09, "Lamp")
    menu_board()

    # the seating that faces the street
    table("TableIn_A", -2.55, -1.35, top="TableWood", leg="MetalDark", r=0.33)
    table("TableIn_B", -0.85, -1.55, top="TableWood", leg="MetalDark", r=0.33)
    table("TableIn_C", 2.35, -1.30, top="TableWood", leg="MetalDark", r=0.33)
    chair("ChairIn_A1", -2.55, -0.75, math.pi, "MetalDark")
    chair("ChairIn_A2", -2.55, -1.95, 0.0, "MetalDark")
    chair("ChairIn_B1", -0.85, -0.95, math.pi, "MetalDark")
    chair("ChairIn_C1", 2.35, -0.70, math.pi, "MetalDark")
    chair("ChairIn_C2", 2.35, -1.90, 0.0, "MetalDark")
    # a stool run along the side glazing
    for i, y in enumerate((-0.10, 0.60, 1.30)):
        cyl(f"Stool_{i}", X0 + 0.72, y, 0.0, 0.70, 0.05, "MetalDark", seg=8)
        cyl(f"StoolSeat_{i}", X0 + 0.72, y, 0.70, 0.76, 0.19, "ChairGreen", seg=10)
    box("Shelf_Side", X0 + 0.16, X0 + 0.52, -0.45, 1.65, 0.98, 1.04, "TableWood")


# ----------------------------------------------------------------------------- assembly

# --------------------------------------------------------------------------- export prep

# 427 loose objects is fine in Blender and wrong in Unity, so the build is joined into six
# meshes on the way out. The names matter: `WorldBuilder.AddColliders` filters BY NODE NAME, and
# a model with nothing to match is the `downtown-is-one-mesh` trap in reverse.
#
# First match wins, so the specific prefixes come before the general ones.
EXPORT_GROUPS = [
    ("FalafelStand_Glass", ("Glass_", "Vitrine_Glass")),
    ("FalafelStand_Food", ("Food_", "Pita_", "Spit_Meat", "Spit_Onion")),
    ("FalafelStand_Sign", ("Fascia_", "Coke_", "Sign_", "SignS_")),
    ("FalafelStand_Canopy", ("Canopy_", "Awning_")),
    ("FalafelStand_Furniture", ("TableOut", "TableIn", "TableSide", "ChairOut", "ChairIn",
                                "ChairSide", "Stool", "Planter", "Bollard")),
]
SHELL = "FalafelStand_Shell"

# The three points the game needs, held as empty NODES rather than as numbers in a C# file. An
# empty carries its position through glTFast untouched, so nothing has to choose between the
# three handedness rules for it - the idiom `seven-eleven-lot.glb` already proved here.
#
# An empty with no rotation exports so that its forward is the shop's front (Blender −Y →
# glTF +Z → Unity +Z), which is the way a Unity character faces by default: the vendor needs no
# rotation of his own, only the place's yaw.
MARKERS = [
    ("fh_vendor", (0.90, 2.15, 0.0)),    # behind the counter, facing the customer
    ("fh_talk", (-0.15, -3.50, 0.0)),    # in front of the open bay, under the canopy
    ("fh_pin", (0.0, 0.0, 0.0)),         # the map pin - the footprint's centre
]


def group_for(name):
    for group, prefixes in EXPORT_GROUPS:
        if name.startswith(prefixes):
            return group
    return SHELL


def join_for_export():
    """Joins every mesh into its group and returns {group: triangle count}."""
    bpy.ops.object.select_all(action="DESELECT")

    # Unparent first: join bakes world transforms into the active object's space, and a parent
    # with a matrix_parent_inverse is one more thing that has to be right for that to hold.
    for ob in list(bpy.data.objects):
        if ob.parent is not None:
            world = ob.matrix_world.copy()
            ob.parent = None
            ob.matrix_world = world

    buckets = {}
    for ob in bpy.data.objects:
        if ob.type == "MESH":
            buckets.setdefault(group_for(ob.name), []).append(ob)

    counts = {}
    for group, members in buckets.items():
        bpy.ops.object.select_all(action="DESELECT")
        for ob in members:
            ob.select_set(True)
        bpy.context.view_layer.objects.active = members[0]
        if len(members) > 1:
            bpy.ops.object.join()
        joined = bpy.context.view_layer.objects.active
        joined.name = group
        joined.data.name = group
        counts[group] = sum(len(p.vertices) - 2 for p in joined.data.polygons)

    bpy.ops.object.select_all(action="DESELECT")

    # The build's parent empty has no children left and would export as a stray node.
    global ROOT
    if ROOT is not None:
        bpy.data.objects.remove(ROOT, do_unlink=True)
        ROOT = None

    return counts


def add_markers():
    for name, at in MARKERS:
        empty = bpy.data.objects.new(name, None)
        empty.empty_display_type = "ARROWS"
        empty.empty_display_size = 0.4
        empty.location = at
        bpy.context.collection.objects.link(empty)


def clear_scene():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.curves):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def lighting():
    sun = bpy.data.lights.new("FH_Sun", type="SUN")
    sun.energy = 3.2
    sun.angle = math.radians(2.0)
    ob = bpy.data.objects.new("FH_Sun", sun)
    ob.location = (-6, -8, 10)
    ob.rotation_euler = (math.radians(52), 0, math.radians(-35))
    bpy.context.collection.objects.link(ob)
    world = bpy.context.scene.world or bpy.data.worlds.new("World")
    bpy.context.scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = srgb("#9EC4E8")
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.1


def view(loc=(-9.5, -11.0, 2.6), target=(-1.2, -1.6, 2.1)):
    """Point the 3D viewport at the corner, so a screenshot shows what the player would see."""
    import mathutils
    d = mathutils.Vector(target) - mathutils.Vector(loc)
    rot = d.to_track_quat("-Z", "Y")
    for area in bpy.context.screen.areas if bpy.context.screen else []:
        if area.type == "VIEW_3D":
            r3d = area.spaces[0].region_3d
            r3d.view_perspective = "PERSP"
            r3d.view_rotation = rot
            r3d.view_location = mathutils.Vector(target)
            r3d.view_distance = d.length
            area.spaces[0].shading.type = "MATERIAL"
            area.spaces[0].overlay.show_overlays = False


def convert_text():
    bpy.ops.object.select_all(action="DESELECT")
    fonts = [o for o in bpy.data.objects if o.type == "FONT"]
    for o in fonts:
        o.select_set(True)
    if fonts:
        bpy.context.view_layer.objects.active = fonts[0]
        bpy.ops.object.convert(target="MESH")
    bpy.ops.object.select_all(action="DESELECT")


def stats():
    tris = 0
    for o in bpy.data.objects:
        if o.type == "MESH":
            tris += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return len([o for o in bpy.data.objects if o.type == "MESH"]), tris


def main(join=True):
    global ROOT
    clear_scene()
    build_materials()
    ROOT = bpy.data.objects.new("FalafelHapaamonim", None)
    ROOT.empty_display_size = 0.5
    bpy.context.collection.objects.link(ROOT)

    shell()
    storefront()
    signage()
    canopy()
    street_furniture()
    counter_and_vitrine()
    interior_fit()
    convert_text()

    if join:
        counts = join_for_export()
        add_markers()
        for group in sorted(counts):
            print(f"  {group}: {counts[group]} tris")

    lighting()
    view()
    n, tris = stats()
    print(f"falafel: {n} meshes, {tris} triangles")


main()
