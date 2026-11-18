"""Проверочные рендеры героя: D:/AddData/blender.exe -b hero.blend -P check_hero.py
Кадры в blender/hero/renders/: голый (3/4, спина, лицо), шейп-кеи лица, поза на IK (рука тянется к полу), в одежде."""
import bpy, math, os
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(bpy.data.filepath))
OUT = os.path.join(HERE, "renders") + "/"
os.makedirs(OUT, exist_ok=True)
sc = bpy.context.scene
rig = bpy.data.objects["Hero_Rig"]; body = bpy.data.objects["Hero_Body"]
outfit = [o for o in bpy.data.objects if o.name.startswith("Outfit_")]
try:
    sc.render.engine = 'BLENDER_EEVEE_NEXT'
except TypeError:
    sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = 900; sc.render.resolution_y = 1100
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.30, 0.29, 0.27, 1)
for rot, en in (((55, 0, 200), 3.2), ((70, 0, 20), 1.0), ((20, 0, 110), 0.8)):
    l = bpy.data.lights.new("S", 'SUN'); l.energy = en
    lo = bpy.data.objects.new("S", l); sc.collection.objects.link(lo)
    lo.rotation_euler = [math.radians(a) for a in rot]
# пол на уровне низа культей
lowest = min((body.matrix_world @ v.co).z for v in body.data.vertices)
rig.location.z -= lowest
bpy.ops.mesh.primitive_plane_add(size=10); fl = bpy.context.object
fm = bpy.data.materials.new("F"); fm.use_nodes = True
next(n for n in fm.node_tree.nodes if n.type == "BSDF_PRINCIPLED").inputs[0].default_value = (0.17, 0.16, 0.15, 1)
fl.data.materials.append(fm)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
pb = rig.pose.bones


def shot(name, t, dist, yaw, h, lens=50):
    bpy.context.view_layer.update()
    v = Matrix.Rotation(math.radians(yaw), 3, 'Z') @ Vector((0, -1, 0))
    cam.location = t + v * dist + Vector((0, 0, h))
    lk = pb["CTRL_look"]                   # взгляд — в камеру
    lk.matrix = Matrix.Translation(rig.matrix_world.inverted() @ cam.location) @ lk.matrix.to_3x3().to_4x4()
    bpy.context.view_layer.update()
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = lens
    sc.render.filepath = OUT + name; bpy.ops.render.render(write_still=True)


def rotw(n, ax, deg):
    b = pb[n]; h = b.matrix.to_translation()
    b.matrix = Matrix.Translation(h) @ Matrix.Rotation(math.radians(deg), 4, ax) @ Matrix.Translation(-h) @ b.matrix
    bpy.context.view_layer.update()


def head_pos():
    bpy.context.view_layer.update()
    return rig.matrix_world @ pb["Head"].head + Vector((0, 0, 0.07))


HIDDEN = bpy.data.materials.new("Hidden"); HIDDEN.use_nodes = True
HIDDEN.node_tree.nodes.remove(next(n for n in HIDDEN.node_tree.nodes if n.type == "BSDF_PRINCIPLED"))
HIDDEN.node_tree.links.new(HIDDEN.node_tree.nodes.new("ShaderNodeBsdfTransparent").outputs[0],
                           next(n for n in HIDDEN.node_tree.nodes if n.type == "OUTPUT_MATERIAL").inputs[0])
SKIN = list(body.data.materials)
HIDE_UNDER_OUTFIT = ("M_Hero_Skin_Torso", "M_Hero_Skin_StumpLegs")   # рубаха + штаны


def set_outfit(on):
    for o in outfit:
        o.hide_render = not on
    for i, m in enumerate(SKIN):
        body.data.materials[i] = HIDDEN if (on and m.name in HIDE_UNDER_OUTFIT) else m


set_outfit(False)
HP = head_pos()
keys = body.data.shape_keys.key_blocks
brows = bpy.data.objects["Hero_Brows"]
for kn in ("Blink_R", "Brow_Sad", "Wince", "Mouth_Frown"):
    for k in keys[1:]:
        k.value = 0
    keys[kn].value = 1.0
    if brows.data.shape_keys and kn in brows.data.shape_keys.key_blocks:
        for k in brows.data.shape_keys.key_blocks[1:]:
            k.value = 0
        brows.data.shape_keys.key_blocks[kn].value = 1.0
    shot("06_key_%s.png" % kn, HP, 0.55, 10, 0.02, lens=55)
for k in keys[1:]:
    k.value = 0
if brows.data.shape_keys:
    for k in brows.data.shape_keys.key_blocks[1:]:
        k.value = 0
keys["Brow_Sad"].value = 0.6
if brows.data.shape_keys:
    brows.data.shape_keys.key_blocks["Brow_Sad"].value = 0.6
# --- поза «побитая собака»; рука — через IK-контрол (тянется к полу впереди)
rotw("spine_01", 'X', 16); rotw("spine_02", 'X', 12); rotw("spine_03", 'X', 10)
rotw("clavicle_r", 'Y', 12); rotw("clavicle_r", 'Z', 14)
rotw("clavicle_l", 'Y', -12); rotw("clavicle_l", 'Z', -14)
rotw("neck_01", 'X', 12); rotw("Head", 'X', -24); rotw("Head", 'Z', 8); rotw("Head", 'Y', 6)
ctrl = pb["CTRL_hand_r"]
target_w = Vector((-0.30, -0.42, 0.04))          # ладонь на полу впереди-справа
ctrl.matrix = Matrix.Translation(rig.matrix_world.inverted() @ target_w) @ Matrix.Rotation(math.radians(-80), 4, 'Y') \
    @ ctrl.matrix.to_3x3().to_4x4()
bpy.context.view_layer.update()
hand = rig.matrix_world @ pb["hand_r"].head
print("IK hand reach error", (hand - target_w).length)

set_outfit(False)
C = Vector((0, 0, 0.5))
shot("01_naked_34.png", C, 2.4, -40, 0.35)
shot("02_naked_back.png", C, 2.4, 160, 0.45)
shot("03_naked_side.png", C, 2.4, 90, 0.2)
HP = head_pos()
shot("04_face.png", HP, 0.65, 15, 0.03, lens=55)
shot("05_face_burnside.png", HP, 0.65, 70, 0.03, lens=55)
set_outfit(True)
shot("07_outfit_34.png", C, 2.4, -40, 0.35)
shot("08_outfit_back.png", C, 2.4, 150, 0.45)
# крупно лицо спереди, нейтральная поза головы не нужна — берём текущую
shot("09_face_front_close.png", head_pos() + Vector((0, 0, -0.01)), 0.42, 0, 0.0, lens=60)
print("RENDERED", OUT)
