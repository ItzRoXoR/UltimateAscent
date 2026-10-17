"""Builds src/UltimateAscent/Resources/SixSlotBackpack.bin from the six-slot backpack's OBJ files.

Usage: python tools/SixSlotBackpack/build_mesh.py <Backpack_Worn_6Slot.obj> <Backpack_Ground_6Slot.obj>

The OBJs are the game's backpack meshes exported with UnityPy (X negated, every face turned around)
with the four side handles taken out and four side pouches added (group SixSlotSidePouches). The game's
meshes are not readable at runtime, so the whole mesh ships: this converts each OBJ back into Unity's
space (X negated again, corners turned back), keeps only the vertices its faces use, and finds the four
pouches' centres, where the items of slots 3 to 6 hang.

The file: "U6BP", version 1, the number of meshes; per mesh its name in the game, its vertex count,
positions, normals and UVs (floats), its triangle count and the triangles' indices (uint16); then the
four pouch centres (slots 3 to 6, floats, in the meshes' own space).
"""
import os, struct, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "src", "UltimateAscent", "Resources", "SixSlotBackpack.bin")
POUCHES = "SixSlotSidePouches"


def read_obj(path):
    v, vt, vn, faces = [], [], [], []
    group = None
    for line in open(path, encoding="utf8"):
        p = line.split()
        if not p:
            continue
        if p[0] == "v":
            v.append(tuple(float(x) for x in p[1:4]))
        elif p[0] == "vt":
            vt.append(tuple(float(x) for x in p[1:3]))
        elif p[0] == "vn":
            vn.append(tuple(float(x) for x in p[1:4]))
        elif p[0] == "g":
            group = p[1]
        elif p[0] == "f":
            corners = []
            for c in p[1:]:
                i = (c.split("/") + ["", ""])[:3]
                corners.append((int(i[0]) - 1, int(i[1]) - 1 if i[1] else -1, int(i[2]) - 1 if i[2] else -1))
            faces.append((group, corners))
    return v, vt, vn, faces


def to_unity(path, name):
    v, vt, vn, faces = read_obj(path)
    index = {}
    positions, normals, uvs, triangles = [], [], [], []
    pouch_points = []

    def vertex(corner):
        if corner not in index:
            pi, ti, ni = corner
            x, y, z = v[pi]
            positions.append((-x, y, z))
            nx, ny, nz = vn[ni] if ni >= 0 else (0.0, 1.0, 0.0)
            normals.append((-nx, ny, nz))
            uvs.append(vt[ti] if ti >= 0 else (0.0, 0.0))
            index[corner] = len(positions) - 1
        return index[corner]

    for group, corners in faces:
        ids = [vertex(c) for c in corners]
        for k in range(1, len(ids) - 1):
            # OBJ corners a, b, c are Unity's c, b, a.
            triangles.append((ids[k + 1], ids[k], ids[0]))
        if group == POUCHES:
            pouch_points.extend(positions[i] for i in ids)
    if len(positions) >= 65536:
        raise SystemExit("%s: %d vertices do not fit 16-bit indices" % (path, len(positions)))
    print("%s: %d vertices, %d triangles (%d OBJ vertices)" % (name, len(positions), len(triangles), len(v)))
    return name, positions, normals, uvs, triangles, pouch_points


# Where the items of slots 3-6 sit in their pouch (Y: -1 is the front of the bag, where its flap is).
# The upper pouches' items stand at the rim, so they rise above the pouch instead of hanging over the
# lower pouch; the lower pouches' items stand at their rim too, further out and nearer the front, where
# the upper pouch above does not cover them.
UPPER = {"out": 0.60, "front": 0.50, "height": 1.00}
LOWER = {"out": 0.75, "front": 0.35, "height": 1.00}


def pouch_centres(points):
    """Slots 3 to 6: +X upper, -X upper, +X lower, -X lower (Unity space; Z is up in the mesh). Each pouch
    is a crescent around the bag, its inner rim against the body. Per pouch: "out" is how far across the
    rim from the body, "front" how far from the rim's front edge to its back one, "height" how far up from
    the pouch's bottom to its rim."""
    zs = sorted(p[2] for p in points)
    middle = (zs[0] + zs[-1]) / 2
    centres = []
    for side, upper in ((1, True), (-1, True), (1, False), (-1, False)):
        place = UPPER if upper else LOWER
        group = [p for p in points if (p[0] > 0) == (side > 0) and (p[2] >= middle) == upper]
        top = max(p[2] for p in group)
        bottom = min(p[2] for p in group)
        rim = [p for p in group if p[2] > top - 0.03]
        inner = min(abs(p[0]) for p in rim)
        outer = max(abs(p[0]) for p in rim)
        front = min(p[1] for p in rim)
        back = max(p[1] for p in rim)
        x = side * (inner + place["out"] * (outer - inner))
        y = front + place["front"] * (back - front)
        centres.append((x, y, bottom + place["height"] * (top - bottom)))
    return centres


worn = to_unity(sys.argv[1], "Cube")
ground = to_unity(sys.argv[2], "Backpack_Ground")
centres = pouch_centres(worn[5])
print("pouch centres (slots 3-6):", ["(%.3f, %.3f, %.3f)" % c for c in centres])
with open(OUT, "wb") as f:
    f.write(b"U6BP")
    f.write(struct.pack("<ii", 1, 2))
    for name, positions, normals, uvs, triangles, _ in (worn, ground):
        raw = name.encode("utf8")
        f.write(struct.pack("<i", len(raw)) + raw)
        f.write(struct.pack("<i", len(positions)))
        for p in positions:
            f.write(struct.pack("<3f", *p))
        for n in normals:
            f.write(struct.pack("<3f", *n))
        for uv in uvs:
            f.write(struct.pack("<2f", *uv))
        f.write(struct.pack("<i", len(triangles)))
        for t in triangles:
            f.write(struct.pack("<3H", *t))
    for c in centres:
        f.write(struct.pack("<3f", *c))
print("wrote", OUT, os.path.getsize(OUT), "bytes")
