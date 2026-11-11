# Photo render of the mounted IMU box (Blender 4.x, Cycles).
#   blender -b -P render_blender.py -- build ../../docs/enclosure_render.png [samples] [view]
# build/ holds the STLs written by render.sh (one per material). Units: STL mm -> scene metres.
import bpy, sys, math, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
BUILD = os.path.abspath(argv[0] if len(argv) > 0 else "build")
OUT = os.path.abspath(argv[1] if len(argv) > 1 else "render.png")
SAMPLES = int(argv[2]) if len(argv) > 2 else 256
VIEW = argv[3] if len(argv) > 3 else "front"

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

def hexcol(h, a=1.0):
    h = h.lstrip("#"); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c) + (a,)  # sRGB -> linear

def material(name, col, metal=0.0, rough=0.5, layer_lines=False, coat=0.0, aniso=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = hexcol(col)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if coat: b.inputs["Coat Weight"].default_value = coat
    if aniso: b.inputs["Anisotropic"].default_value = aniso
    if layer_lines:  # 0.2 mm FDM layer lines along world Y (both prints stack along the box's height)
        tc = nt.nodes.new("ShaderNodeTexCoord"); sep = nt.nodes.new("ShaderNodeSeparateXYZ")
        mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"; mul.inputs[1].default_value = 2 * math.pi / 0.0002
        sn = nt.nodes.new("ShaderNodeMath"); sn.operation = "SINE"
        bump = nt.nodes.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.25; bump.inputs["Distance"].default_value = 0.00003
        nt.links.new(tc.outputs["Object"], sep.inputs[0]); nt.links.new(sep.outputs["Y"], mul.inputs[0])
        nt.links.new(mul.outputs[0], sn.inputs[0]); nt.links.new(sn.outputs[0], bump.inputs["Height"])
        nt.links.new(bump.outputs["Normal"], b.inputs["Normal"])
    return m

MATS = {
    "profile":      material("Aluminium (black anodised)", "#1C1D20", metal=0.85, rough=0.36),
    "base":         material("PETG carbon grey", "#2A2E2C", rough=0.42, layer_lines=True),
    "lid":          material("PETG black", "#141615", rough=0.38, layer_lines=True),
    "inlay_green":  material("PETG green", "#A6FF4D", rough=0.38),
    "inlay_orange": material("PETG orange", "#FF7A29", rough=0.38),
    "inlay_white":  material("PETG white", "#F3F5F1", rough=0.38),
    "bolts":        material("Steel 12.9 black oxide", "#2B2B2D", metal=1.0, rough=0.33),
    "washers":      material("Zinc plated", "#B9BDC2", metal=1.0, rough=0.28),
    "tnuts":        material("Zinc plated nut", "#B9BDC2", metal=1.0, rough=0.3),
    "lidscrews":    material("Stainless A2", "#D3D5D8", metal=1.0, rough=0.2),
    "plug":         material("USB-C overmould", "#1A1A1C", rough=0.55),
    "cable":        material("Cable jacket", "#151517", rough=0.7),
}

for name, mat in MATS.items():
    path = os.path.join(BUILD, name + ".stl")
    if not os.path.exists(path): continue
    bpy.ops.wm.stl_import(filepath=path, global_scale=0.001)
    ob = bpy.context.selected_objects[0]; ob.name = name
    ob.data.materials.append(mat)
    bpy.context.view_layer.objects.active = ob
    if bpy.app.version >= (4, 1, 0):
        bpy.ops.object.shade_auto_smooth(angle=math.radians(35))
    else:
        bpy.ops.object.shade_smooth(); ob.data.use_auto_smooth = True; ob.data.auto_smooth_angle = math.radians(35)

# ---- studio: seamless sweep backdrop, key/fill/rim area lights ----------------------------------
bpy.ops.mesh.primitive_plane_add(size=3.0, location=(0, 0.0, -0.075))
floor = bpy.context.object; floor.name = "floor"
bpy.ops.mesh.primitive_plane_add(size=2.0, location=(0, 0.16, 0.9), rotation=(math.radians(90), 0, 0))
wall = bpy.context.object; wall.name = "wall"
bg = material("Backdrop", "#8E9390", rough=0.9)
floor.data.materials.append(bg); wall.data.materials.append(bg)

def area(name, loc, target, power, size, col=(1, 1, 1)):
    bpy.ops.object.light_add(type="AREA", location=loc)
    L = bpy.context.object; L.name = name
    L.data.energy = power; L.data.size = size; L.data.color = col
    d = Vector(target) - Vector(loc); L.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    return L

T = (0.0, -0.008, 0.016)
area("key",  (-0.30, -0.35, 0.40), T, 28, 0.45, (1.0, 0.97, 0.93))
area("fill", ( 0.40, -0.30, 0.12), T, 9, 0.6, (0.92, 0.96, 1.0))
area("rim",  ( 0.10,  0.05, 0.45), T, 22, 0.3)
area("top",  ( 0.00, -0.10, 0.60), T, 8, 1.2)

w = bpy.data.worlds.new("World"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.57, 0.6, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.04

# ---- camera ---------------------------------------------------------------------------------------
cams = {   # view: (camera location, lens mm, look-at target)
    "front": ((0.135, -0.255, 0.105), 56, T),
    "close": ((0.105, -0.165, -0.030), 60, (0.0, -0.010, 0.006)),
}
loc, lens, T = cams.get(VIEW, cams["front"])
bpy.ops.object.camera_add(location=loc)
cam = bpy.context.object; sc.camera = cam
cam.data.lens = lens
cam.rotation_euler = (Vector(T) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
cam.data.dof.use_dof = True; cam.data.dof.focus_distance = (Vector(T) - Vector(loc)).length; cam.data.dof.aperture_fstop = 11 if VIEW == "close" else 8

# ---- render settings --------------------------------------------------------------------------------
sc.render.engine = "CYCLES"
sc.cycles.device = "CPU"
sc.cycles.samples = SAMPLES
sc.cycles.use_denoising = os.environ.get("DENOISE", "0") == "1"   # distro Blender builds often lack OIDN
sc.cycles.use_adaptive_sampling = True; sc.cycles.adaptive_threshold = 0.008
sc.cycles.max_bounces = 8; sc.cycles.caustics_reflective = False; sc.cycles.caustics_refractive = False
sc.cycles.blur_glossy = 1.0
sc.render.resolution_x, sc.render.resolution_y = 1600, 1000
sc.render.film_transparent = False
sc.view_settings.view_transform = os.environ.get("VT", "Standard") if os.environ.get("VT", "Standard") in [i.identifier for i in sc.view_settings.bl_rna.properties["view_transform"].enum_items] else "AgX" if "AgX" in [i.identifier for i in sc.view_settings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
sc.view_settings.look = "None"
for lk in ("Medium High Contrast", "AgX - Punchy"):
    try: sc.view_settings.look = lk; break
    except TypeError: pass
sc.view_settings.exposure = float(os.environ.get("EXPOSURE", "-0.6"))
sc.render.image_settings.file_format = "PNG"
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("wrote", OUT)
