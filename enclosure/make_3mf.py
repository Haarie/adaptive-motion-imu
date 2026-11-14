#!/usr/bin/env python3
"""Build a PrusaSlicer project (.3mf) with the print-ready enclosure parts.

Objects
  base   - base.stl (side mount), filament 1
  lid    - lid.stl + the three inlays as parts of ONE object, so they stay aligned:
           lid -> filament 1 (black), green -> 2, orange -> 3, white -> 4
No printer/print/filament settings are embedded: PrusaSlicer keeps whatever presets are
selected (pick "Original Prusa MK3.5S MMU3" + PETG), so nothing in your profiles gets overwritten.

Usage: python3 make_3mf.py [out.3mf]      (run after the STLs are rendered)
"""
import struct, sys, zipfile, os
from xml.sax.saxutils import quoteattr

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "adaptive_motion_imu_print.3mf")

FILAMENTS = {1: "black PETG (lid + base)", 2: "green #A6FF4D", 3: "orange #FF7A29", 4: "white #F3F5F1"}
OBJECTS = [  # name, bed position (centre, mm) on the 250 x 210 bed, [(volume name, stl, filament)]
    ("AMIMU base (side mount)", (75, 105), [("base", "base.stl", 1)]),
    ("AMIMU lid + Sim-Spares inlay", (170, 105), [("lid", "lid.stl", 1),
                                                   ("inlay green (logo rim)", "lid_inlay_green.stl", 2),
                                                   ("inlay orange (paddles, UP)", "lid_inlay_orange.stl", 3),
                                                   ("inlay white (spokes, text)", "lid_inlay_white.stl", 4)]),
]

def read_stl(path):
    data = open(path, "rb").read()
    if data[:5] == b"solid" and b"facet" in data[:400]:          # ASCII STL (OpenSCAD default)
        tris, cur = [], []
        for line in data.decode().splitlines():
            t = line.split()
            if t and t[0] == "vertex":
                cur.append(tuple(float(v) for v in t[1:4]))
                if len(cur) == 3: tris.append(cur); cur = []
        return tris
    n = struct.unpack_from("<I", data, 80)[0]
    return [[struct.unpack_from("<3f", data, 84 + i * 50 + 12 + k * 12) for k in range(3)] for i in range(n)]

def mesh_xml(tris_all):
    """Shared-vertex mesh for all volumes of an object, plus per-volume triangle ranges."""
    verts, index, tri_out, ranges = [], {}, [], []
    for tris in tris_all:
        first = len(tri_out)
        for t in tris:
            ids = []
            for v in t:
                key = (round(v[0], 5), round(v[1], 5), round(v[2], 5))
                if key not in index: index[key] = len(verts); verts.append(key)
                ids.append(index[key])
            if len(set(ids)) == 3: tri_out.append(ids)
        ranges.append((first, len(tri_out) - 1))
    vx = "".join(f'<vertex x="{x:.5f}" y="{y:.5f}" z="{z:.5f}"/>' for x, y, z in verts)
    tx = "".join(f'<triangle v1="{a}" v2="{b}" v3="{c}"/>' for a, b, c in tri_out)
    return f"<mesh><vertices>{vx}</vertices><triangles>{tx}</triangles></mesh>", ranges

objects_xml, build_xml, cfg = [], [], ['<?xml version="1.0" encoding="UTF-8"?>\n<config>']
for oid, (name, (bx, by), vols) in enumerate(OBJECTS, start=1):
    tris_all = [read_stl(os.path.join(HERE, stl)) for _, stl, _ in vols]
    pts = [v for tris in tris_all for t in tris for v in t]
    cx = (min(p[0] for p in pts) + max(p[0] for p in pts)) / 2
    cy = (min(p[1] for p in pts) + max(p[1] for p in pts)) / 2
    z0 = min(p[2] for p in pts)
    tris_all = [[[(v[0] - cx, v[1] - cy, v[2] - z0) for v in t] for t in tris] for tris in tris_all]  # object origin = footprint centre
    mx, ranges = mesh_xml(tris_all)
    objects_xml.append(f'<object id="{oid}" type="model">{mx}</object>')
    build_xml.append(f'<item objectid="{oid}" transform="1 0 0 0 1 0 0 0 1 {bx} {by} 0" printable="1"/>')
    cfg.append(f' <object id="{oid}" instances_count="1">')
    cfg.append(f'  <metadata type="object" key="name" value={quoteattr(name)}/>')
    cfg.append(f'  <metadata type="object" key="extruder" value="{vols[0][2]}"/>')
    for (vname, _, ext), (a, b) in zip(vols, ranges):
        cfg.append(f'  <volume firstid="{a}" lastid="{b}">')
        cfg.append(f'   <metadata type="volume" key="name" value={quoteattr(vname)}/>')
        cfg.append('   <metadata type="volume" key="volume_type" value="ModelPart"/>')
        cfg.append('   <metadata type="volume" key="matrix" value="1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1"/>')
        cfg.append(f'   <metadata type="volume" key="extruder" value="{ext}"/>')
        cfg.append('  </volume>')
    cfg.append(' </object>')
cfg.append('</config>\n')

model = ('<?xml version="1.0" encoding="UTF-8"?>\n'
         '<model unit="millimeter" xml:lang="en-US" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02" '
         'xmlns:slic3rpe="http://schemas.slic3r.org/3mf/2017/06">\n'
         '<metadata name="slic3rpe:Version3mf">1</metadata>\n'
         '<metadata name="Title">Adaptive Motion IMU enclosure - Sim-Spares</metadata>\n'
         '<metadata name="Designer">Sim-Spares</metadata>\n'
         f'<resources>{"".join(objects_xml)}</resources>\n<build>{"".join(build_xml)}</build>\n</model>\n')
ctypes = ('<?xml version="1.0" encoding="UTF-8"?>\n<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
          '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
          '<Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>'
          '<Default Extension="config" ContentType="text/xml"/></Types>')
rels = ('<?xml version="1.0" encoding="UTF-8"?>\n<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Target="/3D/3dmodel.model" Id="rel-1" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/>'
        '</Relationships>')
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("[Content_Types].xml", ctypes)
    z.writestr("_rels/.rels", rels)
    z.writestr("3D/3dmodel.model", model)
    z.writestr("Metadata/Slic3r_PE_model.config", "\n".join(cfg))
print("wrote", OUT, "| filaments:", "; ".join(f"{k}={v}" for k, v in FILAMENTS.items()))
