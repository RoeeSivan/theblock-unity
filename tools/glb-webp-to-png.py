#!/usr/bin/env python3
"""Rewrite a .glb so its embedded WebP textures become PNG/JPEG.

Why this exists
---------------
The web build's optimized assets store textures as WebP, which the exporter writes into
`extensionsRequired: ["EXT_texture_webp"]`. glTFast cannot read that extension, and because it is
*required* it rejects the whole file: Unity imports the .glb as a DefaultAsset and WorldBuilder can
only report the place as "missing" - the real reason hides in the Inspector.

Blender exports get `export_image_webp_fallback=True` for the same reason (U8 decision). These lot
car models come from `public/models/optimized/lod/`, have no source asset anywhere, and cannot be
re-exported - so the transcode happens here instead, once, and the result is committed.

The geometry is untouched: Draco stays compressed, accessors and bufferViews other than the image
ones are copied byte for byte. Only the image payloads change, plus the texture indirection the
extension added.

    python3 tools/glb-webp-to-png.py in.glb out.glb
"""

import json
import struct
import sys
from io import BytesIO

from PIL import Image

GLB_MAGIC = b"glTF"
JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
EXT = "EXT_texture_webp"


def read_glb(path):
    data = open(path, "rb").read()
    if data[:4] != GLB_MAGIC:
        raise SystemExit(f"{path} is not a binary glTF")
    gltf, blob, off = None, b"", 12
    while off < len(data):
        length, kind = struct.unpack_from("<II", data, off)
        chunk = data[off + 8 : off + 8 + length]
        if kind == JSON_CHUNK:
            gltf = json.loads(chunk)
        elif kind == BIN_CHUNK:
            blob = chunk
        off += 8 + length
    return gltf, blob


def write_glb(path, gltf, blob):
    def pad(chunk, filler):
        return chunk + filler * (-len(chunk) % 4)

    js = pad(json.dumps(gltf, separators=(",", ":")).encode("utf-8"), b" ")
    bn = pad(blob, b"\0")
    total = 12 + 8 + len(js) + (8 + len(bn) if bn else 0)
    out = bytearray(struct.pack("<4sII", GLB_MAGIC, 2, total))
    out += struct.pack("<II", len(js), JSON_CHUNK) + js
    if bn:
        out += struct.pack("<II", len(bn), BIN_CHUNK) + bn
    open(path, "wb").write(bytes(out))


def transcode(src, dst):
    gltf, blob = read_glb(src)

    # Flatten the extension's indirection first: a texture that names its source only under
    # EXT_texture_webp has no plain `source`, so dropping the extension without this would leave the
    # texture pointing at nothing.
    for texture in gltf.get("textures", []):
        ext = texture.get("extensions", {}).pop(EXT, None)
        if ext is not None:
            texture["source"] = ext["source"]
        if not texture.get("extensions"):
            texture.pop("extensions", None)

    # Rebuild the binary blob, re-encoding image bufferViews and copying everything else.
    views = gltf.get("bufferViews", [])
    webp_views = {}
    for image in gltf.get("images", []):
        if image.get("mimeType") != "image/webp" or "bufferView" not in image:
            continue
        view = views[image["bufferView"]]
        start = view.get("byteOffset", 0)
        pixels = Image.open(BytesIO(blob[start : start + view["byteLength"]]))
        buffer = BytesIO()
        # JPEG where there is nothing to lose by it; PNG only when the alpha channel is real,
        # since a decal's cutout is exactly what a JPEG would destroy.
        has_alpha = pixels.mode in ("RGBA", "LA") or (
            pixels.mode == "P" and "transparency" in pixels.info
        )
        if has_alpha:
            pixels.convert("RGBA").save(buffer, format="PNG", optimize=True)
            image["mimeType"] = "image/png"
        else:
            pixels.convert("RGB").save(buffer, format="JPEG", quality=92, optimize=True)
            image["mimeType"] = "image/jpeg"
        webp_views[image["bufferView"]] = buffer.getvalue()

    if not webp_views:
        print(f"{src}: no embedded WebP - nothing to do")

    rebuilt = bytearray()
    for index, view in enumerate(views):
        payload = webp_views.get(index)
        if payload is None:
            start = view.get("byteOffset", 0)
            payload = blob[start : start + view["byteLength"]]
        # 4-byte alignment is required for accessor-backed views and harmless for image ones.
        rebuilt += b"\0" * (-len(rebuilt) % 4)
        view["byteOffset"] = len(rebuilt)
        view["byteLength"] = len(payload)
        rebuilt += payload

    if gltf.get("buffers"):
        gltf["buffers"][0]["byteLength"] = len(rebuilt)
        gltf["buffers"][0].pop("uri", None)

    for key in ("extensionsUsed", "extensionsRequired"):
        if EXT in gltf.get(key, []):
            gltf[key] = [e for e in gltf[key] if e != EXT]
            if not gltf[key]:
                del gltf[key]

    write_glb(dst, gltf, bytes(rebuilt))
    print(f"{src} → {dst}: {len(webp_views)} image(s) transcoded, {len(rebuilt)} bytes of buffer")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    transcode(sys.argv[1], sys.argv[2])
