"""Главный герой: калека-узник (Quaternius Universal Base Characters → свой персонаж).

Запуск (фоном):  D:/AddData/blender.exe -b --factory-startup -P D:/Metroidvany/blender/hero/build_hero.py
Выход:  blender/hero/hero.blend, blender/hero/textures/*.png,
        MetroidvanyGame/Assets/Characters/Hero/Hero.fbx (+ текстуры рядом).

Что делает:
  1. тело Superhero_Male: истощение (мягкие ткани ужаты к костям), жёстче лицо;
  2. увечья: нет ног (культи ниже таза), нет левой руки (короткая культя плеча),
     нет левого уха (рубец), левый глаз выжжен (закрытая впадина);
  3. текстура кожи: ожоги с левой стороны, клеймо-точки, следы плети на спине,
     порезы, натёртость от кандалов на запястье, рёбра, щетина, тёмные глазницы — запекается в PNG;
  4. риг: 65 костей Quaternius (кости ампутированного — без весов, чтобы Unity собрал Humanoid),
     + eye_r (взгляд), + контролы IK для авторинга (CTRL_*, не экспортируются);
  5. шейп-кеи лица и дыхания: Blink_R, Brow_Sad, Brow_Angry, Wince, Mouth_Frown, Breath_In;
  6. кожа разбита на 4 материала-зоны (голова/торс/рука/культи) — одежда может скрыть зону;
  7. одежда — отдельные меши на том же скелете; fit_outfit() подгоняет любой наряд Quaternius
     (ужимает, режет под культи, выталкивает из тела). Сейчас: Peasant.
"""
import bpy, bmesh, math, os, shutil
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
TEX = os.path.join(HERE, "textures")
R = "D:/Metroidvany/assets_ext/characters/"
BASE = R + "BaseChars/Universal Base Characters[Standard]/"
OUTF = R + "Outfits/Modular Character Outfits - Fantasy[Standard]/Exports/glTF (Godot-Unreal)/Modular Parts/"
UNITY = "D:/Metroidvany/MetroidvanyGame/Assets/Characters/Hero/"
TEX_RES = 2048
os.makedirs(TEX, exist_ok=True)

# ---------------------------------------------------------------- import
bpy.ops.wm.read_factory_settings(use_empty=True)


def imp(f):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=f)
    return list(set(bpy.data.objects) - before)


objs = imp(BASE + "Base Characters/Godot - UE/Superhero_Male_FullBody.gltf")
rig = [o for o in objs if o.type == 'ARMATURE'][0]
for o in list(bpy.data.objects):
    if o.name.startswith("Icosphere") or o.type == 'EMPTY':
        bpy.data.objects.remove(o)
rig.name = rig.data.name = "Hero_Rig"
body = bpy.data.objects["SuperHero_Male"]; body.name = body.data.name = "Hero_Body"
eyes = bpy.data.objects["Eyes"]; eyes.name = eyes.data.name = "Hero_Eyes"
brows = bpy.data.objects["Eyebrows"]; brows.name = brows.data.name = "Hero_Brows"


def bone(n):
    return rig.matrix_world @ rig.data.bones[n].head_local


def bseg(n):
    b = rig.data.bones[n]
    return rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local


SA = bone("upperarm_l")                       # левое плечо (его руки нет)
DA = (bone("lowerarm_l") - SA).normalized()
SIDE = 1 if SA.x > 0 else -1                  # +x — сторона увечий
H = bone("Head")
ev = [eyes.matrix_world @ v.co for v in eyes.data.vertices]
EZ = sum(p.z for p in ev) / len(ev)
EY = min(p.y for p in ev)
EX = sum(abs(p.x) for p in ev) / len(ev)
EYE_R = Vector((-SIDE * EX, sum(p.y for p in ev) / len(ev), EZ))   # центр живого глаза
EYE_L = Vector((SIDE * EX, EYE_R.y, EZ))
MZ = EZ - 0.075                               # линия рта
STUMP_Z = bone("thigh_l").z - 0.22            # культи ног: ~середина бедра
ARM_LEN = 0.07                                # культя плеча
SPINE = sorted([bone(n) for n in ("pelvis", "spine_01", "spine_02", "spine_03", "neck_01")], key=lambda p: p.z)
Z_BELLY = (bone("pelvis").z + 0.02, bone("spine_02").z + 0.04)
print("SIDE", SIDE, "EYE_R", EYE_R, "STUMP_Z", STUMP_Z)


def axis_y(z):
    for a, b in zip(SPINE, SPINE[1:]):
        if a.z <= z <= b.z:
            t = (z - a.z) / max(b.z - a.z, 1e-6)
            return a.y + (b.y - a.y) * t
    return SPINE[0].y if z < SPINE[0].z else SPINE[-1].y


def smooth01(x):
    x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


# ---------------------------------------------------------------- 1. истощение и лицо
LIMBS = {"upperarm_r": 0.68, "lowerarm_r": 0.74, "upperarm_l": 0.68, "thigh_l": 0.80, "thigh_r": 0.80, "neck_01": 0.84}


def emaciate(o, amount=1.0, face=False):
    mw = o.matrix_world; inv = mw.inverted(); n3 = mw.to_3x3()
    gname = {g.index: g.name for g in o.vertex_groups}
    for v in o.data.vertices:
        p = mw @ v.co
        w = {gname[g.group]: g.weight for g in v.groups if g.group in gname}
        newp = p.copy()
        wt = sum(w.get(n, 0) for n in ("pelvis", "spine_01", "spine_02", "spine_03"))
        if wt > 0:
            ay = axis_y(p.z)
            front = p.y < ay                      # тело смотрит в −Y
            sy = (0.70 if Z_BELLY[0] < p.z < Z_BELLY[1] else 0.88) if front else 0.90
            q = Vector((p.x * 0.84, ay + (p.y - ay) * sy, p.z))
            newp += (q - p) * min(wt, 1.0)
        for n, s in LIMBS.items():
            wb = w.get(n, 0)
            if wb <= 0:
                continue
            h, t = bseg(n); ax = t - h
            u = max(0.0, min(1.0, (p - h).dot(ax) / ax.length_squared))
            c = h + ax * u
            k = s + (1 - s) * (1 - math.sin(math.pi * u)) * 0.7      # у суставов ткань не ужимаем
            newp += ((c + (p - c) * k) - p) * wb
        if face and w.get("Head", 0) > 0.5 and p.y < EY + 0.03:
            ax_ = abs(p.x); nrm = (n3 @ v.normal).normalized()
            if EZ - 0.075 < p.z < EZ - 0.02 and 0.028 < ax_ < 0.08:      # впалые щёки
                f = math.sin(math.pi * (p.z - (EZ - 0.075)) / 0.055) * min(1, (ax_ - 0.028) / 0.02)
                newp -= nrm * 0.009 * f
            if EZ < p.z < EZ + 0.045 and ax_ > 0.058:                    # виски
                newp -= nrm * 0.004
            de = min((p - Vector((sx, EY, EZ))).length for sx in (EX, -EX))
            if de < 0.03 and p.z < EZ + 0.012:                            # глубже глазницы
                newp -= nrm * 0.004 * (1 - de / 0.03)
            if EZ + 0.014 < p.z < EZ + 0.03 and 0.01 < ax_ < 0.065:       # тяжелее надбровья
                newp += Vector((0, -0.003, -0.002))
            if MZ + 0.02 < p.z < EZ - 0.005 and ax_ < 0.022 and p.y < EY - 0.005:   # сломанный нос
                k = smooth01((EY - 0.005 - p.y) / 0.025) * math.sin(math.pi * (p.z - (MZ + 0.02)) / (EZ - 0.025 - MZ))
                newp.x -= SIDE * 0.005 * k
            if abs(p.z - MZ) < 0.015 and 0.018 < ax_ < 0.04:              # уголки рта вниз
                newp.z -= 0.003 * (1 - abs(p.z - MZ) / 0.015)
        v.co = inv @ (p + (newp - p) * amount)
    o.data.update()


# ---------------------------------------------------------------- 2. ампутации и купола культей
def boundary_loops(bm):
    """Упорядоченные петли края; порядок — как у петель соседних граней (a→b)."""
    out, used = [], set()
    nxt = {}
    for e in bm.edges:
        if e.is_boundary:
            l = e.link_loops[0]
            nxt[l.vert] = (l.link_loop_next.vert, l)
    for start in list(nxt):
        if start in used:
            continue
        loop, v = [], start
        while v in nxt and v not in used:
            used.add(v); loop.append(v); v = nxt[v][0]
        if len(loop) >= 3:
            out.append(loop)
    return out


def cap(bm, loop, mw, outward, height_k=0.6, rings=4, flat=None):
    """Купол на петле края: кольца сходятся к центру, высота ~ радиус·height_k."""
    inv = mw.inverted()
    pts = [mw @ v.co for v in loop]
    c = sum(pts, Vector()) / len(pts)
    rad = sum((p - c).length for p in pts) / len(pts)
    hgt = flat if flat is not None else rad * height_k
    dl = bm.verts.layers.deform.active
    uvl = bm.loops.layers.uv.active
    uv_of = {}
    for v in loop:
        uv_of[v] = v.link_loops[0][uvl].uv.copy() if v.link_loops else Vector((0, 0))
    prev = loop
    created = []
    for k in range(1, rings + 1):
        t = k / (rings + 1)
        r = math.cos(t * math.pi / 2); h = hgt * math.sin(t * math.pi / 2)
        ring = []
        for v, p in zip(loop, pts):
            nv = bm.verts.new(inv @ (c + (p - c) * r + outward * h))
            if dl is not None:
                for g, w in v[dl].items():
                    nv[dl][g] = w
            uv_of[nv] = uv_of[v]
            ring.append(nv)
        n = len(loop)
        for i in range(n):
            a, b = prev[i], prev[(i + 1) % n]
            a2, b2 = ring[i], ring[(i + 1) % n]
            f = bm.faces.new((b, a, a2, b2)); created.append(f)
        prev = ring
    cv = bm.verts.new(inv @ (c + outward * hgt))
    if dl is not None:
        acc = {}
        for v in loop:
            for g, w in v[dl].items():
                acc[g] = acc.get(g, 0) + w / len(loop)
        for g, w in acc.items():
            cv[dl][g] = w
    uv_of[cv] = uv_of[loop[0]]
    n = len(prev)
    for i in range(n):
        f = bm.faces.new((prev[(i + 1) % n], prev[i], cv)); created.append(f)
    for f in created:
        f.smooth = True
        for l in f.loops:
            l[uvl].uv = uv_of[l.vert]
    return c


def amputate(o, caps=True, cap_k=0.6, face_cuts=False):
    """Режет ноги/левую руку (и для тела — ухо, глаз), закрывает срезы куполами."""
    mw = o.matrix_world
    bm = bmesh.new(); bm.from_mesh(o.data)
    if not bm.verts.layers.deform.active:
        bm.verts.layers.deform.verify()
    bm.verts.layers.deform.verify()
    kill = [v for v in bm.verts if (mw @ v.co).z < STUMP_Z or ((mw @ v.co) - SA).dot(DA) > ARM_LEN]
    if face_cuts:
        inv = mw.inverted()
        band = [mw @ v.co for v in bm.verts if H.z + 0.0 < (mw @ v.co).z < H.z + 0.12]
        mx = max(p.x * SIDE for p in band)
        skull = mx - 0.026
        ear = [v for v in bm.verts if (lambda p: p.x * SIDE > skull and H.z - 0.01 < p.z < H.z + 0.13
                                       and abs(p.y - H.y) < 0.07)(mw @ v.co)]
        ce = sum((mw @ v.co for v in ear), Vector()) / max(len(ear), 1)
        for v in ear:
            p = mw @ v.co
            k = min(1.0, (p.x * SIDE - skull) / 0.006)          # у корня уха — плавно
            yz = Vector((0, ce.y + (p.y - ce.y) * 0.4, ce.z + (p.z - ce.z) * 0.4))
            p.y += (yz.y - p.y) * k; p.z += (yz.z - p.z) * k
            p.x = SIDE * (skull + (p.x * SIDE - skull) * 0.15)
            v.co = inv @ p
        print("ear squashed at skull x", skull, "tip", mx)
    bmesh.ops.delete(bm, geom=list(set(kill)), context='VERTS')
    if caps:
        thigh_dir = {s: (bseg("thigh_" + s)[1] - bseg("thigh_" + s)[0]).normalized() for s in "lr"}
        for loop in boundary_loops(bm):
            pts = [mw @ v.co for v in loop]
            c = sum(pts, Vector()) / len(pts)
            if c.z < STUMP_Z + 0.06:
                cap(bm, loop, mw, thigh_dir["l" if c.x * SIDE > 0 else "r"], cap_k)
            elif (c - SA).dot(DA) > ARM_LEN - 0.05 and c.x * SIDE > 0.1:
                cap(bm, loop, mw, DA, cap_k * 0.8)
            elif face_cuts and (c - EYE_L).length < 0.04:
                cap(bm, loop, mw, Vector((0, -1, 0)), flat=0.003, rings=3)       # сросшееся веко
    bm.normal_update()
    bm.to_mesh(o.data); bm.free(); o.data.update()


emaciate(body, 1.0, face=True)
amputate(body, caps=True, face_cuts=True)

# глаза: левого нет, правый — на собственную кость eye_r (взгляд, IK look-at)
bm = bmesh.new(); bm.from_mesh(eyes.data)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if (eyes.matrix_world @ v.co).x * SIDE > 0], context='VERTS')
bm.to_mesh(eyes.data); bm.free()

# брови: над выжженным глазом сгорела; остальные — ниже и сведены (насторожённость, боль)
bm = bmesh.new(); bm.from_mesh(brows.data)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if (lambda p: p.x * SIDE > 0.035 or (p.x * SIDE > 0 and p.z < EZ + 0.012))(brows.matrix_world @ v.co)], context='VERTS')
bm.to_mesh(brows.data); bm.free()
mwb = brows.matrix_world; invb = mwb.inverted()
for v in brows.data.vertices:          # в меше бровей есть и ресницы: брови — ниже, ресницы — за веком
    p = mwb @ v.co
    if p.z > EZ + 0.012:
        p.z -= 0.003 + (0.003 if abs(p.x) < 0.028 else 0.0)
    else:
        p.y += 0.0035                      # глазницы углублены на ~4 мм — ресницы идут за веком
    v.co = invb @ p

# ---------------------------------------------------------------- 3. текстуры
bpy.context.view_layer.update()
rest = body.data.attributes.new("rest", 'FLOAT_VECTOR', 'POINT')
for i, v in enumerate(body.data.vertices):
    rest.data[i].vector = body.matrix_world @ v.co

mat = body.data.materials[0]; nt = mat.node_tree; N = nt.nodes; L = nt.links
bsdf = next(n for n in N if n.type == "BSDF_PRINCIPLED")
base_col = bsdf.inputs["Base Color"].links[0].from_socket
base_rgh = bsdf.inputs["Roughness"].links[0].from_socket if bsdf.inputs["Roughness"].links else None
img_nodes = {n.image.name: n for n in N if n.type == 'TEX_IMAGE' and n.image}
src_normal = next(n.image for n in N if n.type == 'TEX_IMAGE' and n.image and "Normal" in n.image.name)

ra = N.new("ShaderNodeAttribute"); ra.attribute_name = "rest"
P = ra.outputs["Vector"]
sep = N.new("ShaderNodeSeparateXYZ"); L.new(P, sep.inputs[0])
X, Y, Z = sep.outputs[0], sep.outputs[1], sep.outputs[2]


def M(op, a, b=None, c=None):
    m = N.new("ShaderNodeMath"); m.operation = op
    for i, x in enumerate((a, b, c)):
        if x is None:
            continue
        if isinstance(x, (int, float)):
            m.inputs[i].default_value = x
        else:
            L.new(x, m.inputs[i])
    return m.outputs[0]


def clamp(a):
    return M("MINIMUM", M("MAXIMUM", a, 0.0), 1.0)


def mix(fac, a, b, blend='MIX'):
    m = N.new("ShaderNodeMix"); m.data_type = 'RGBA'; m.blend_type = blend
    for sock, val in ((0, fac), (6, a), (7, b)):
        if isinstance(val, (int, float)):
            m.inputs[sock].default_value = val
        elif isinstance(val, tuple):
            m.inputs[sock].default_value = val
        else:
            L.new(val, m.inputs[sock])
    return m.outputs[2]


def noise(scale, detail=4, rough=0.55):
    n = N.new("ShaderNodeTexNoise"); n.inputs["Scale"].default_value = scale
    n.inputs["Detail"].default_value = detail; n.inputs["Roughness"].default_value = rough
    L.new(P, n.inputs["Vector"]); return n.outputs[0]


def dist_to(pt):
    v = N.new("ShaderNodeVectorMath"); v.operation = 'DISTANCE'
    L.new(P, v.inputs[0]); v.inputs[1].default_value = pt; return v.outputs["Value"]


def ramp(fac, stops):
    r = N.new("ShaderNodeValToRGB"); L.new(fac, r.inputs[0])
    el = r.color_ramp.elements
    while len(el) < len(stops):
        el.new(0.5)
    for e, (pos, col) in zip(el, stops):
        e.position = pos; e.color = col
    return r.outputs[0]


# ожог: сторона увечий (лицо, шея, плечо-культя, бок, спина) + край
n1 = noise(6.5, 8, 0.65)
warp = M("MULTIPLY", M("SUBTRACT", noise(2.2, 3), 0.5), 0.07)
tz = clamp(M("MULTIPLY", M("SUBTRACT", Z, H.z - 0.16), 8.0))           # 0 — торс, 1 — голова
bx = M("ADD", M("MULTIPLY", M("SUBTRACT", 1.0, tz), 0.056), M("MULTIPLY", tz, 0.016))
side_mask = M("MULTIPLY", M("SUBTRACT", M("ADD", M("MULTIPLY", X, SIDE), warp), bx), 16.0)
stump_mask = M("SUBTRACT", 1.6, M("MULTIPLY", dist_to(SA), 9.0))
is_head = M("GREATER_THAN", Z, H.z - 0.06)
face_mask = M("ADD", M("MULTIPLY", is_head, M("MULTIPLY_ADD", X, SIDE * 30.0, 0.2)), M("MULTIPLY", M("SUBTRACT", 1.0, is_head), -5.0))
region = M("MAXIMUM", side_mask, stump_mask)
burn = clamp(M("ADD", M("MULTIPLY", M("SUBTRACT", n1, 0.5), 3.0), M("MULTIPLY", region, 1.0)))
burn_col = ramp(noise(14, 6, 0.7), [(0.30, (0.33, 0.09, 0.07, 1)), (0.55, (0.62, 0.26, 0.22, 1)), (0.75, (0.86, 0.55, 0.49, 1))])
burn_edge = clamp(M("SUBTRACT", 1.0, M("MULTIPLY", M("ABSOLUTE", M("SUBTRACT", burn, 0.35)), 5.0)))
# клеймо/прижигания: круглые пятна на груди, руке, бедре
vor = N.new("ShaderNodeTexVoronoi"); vor.inputs["Scale"].default_value = 18
L.new(P, vor.inputs["Vector"])
spots = M("MULTIPLY", M("LESS_THAN", vor.outputs["Distance"], 0.13), M("GREATER_THAN", noise(3.0), 0.58))
spots = M("MULTIPLY", spots, M("LESS_THAN", Z, H.z - 0.08))
# плеть: тонкие косые рубцы на спине, пара порезов спереди
wave = N.new("ShaderNodeTexWave"); wave.bands_direction = 'DIAGONAL'
wave.inputs["Scale"].default_value = 4.0; wave.inputs["Distortion"].default_value = 1.5; wave.inputs["Detail"].default_value = 2
L.new(P, wave.inputs["Vector"])
lines = M("MULTIPLY", M("GREATER_THAN", wave.outputs["Fac"], 0.991), M("GREATER_THAN", noise(5.0), 0.42))
back = M("GREATER_THAN", Y, bone("spine_02").y + 0.01)
torso = M("MULTIPLY", M("LESS_THAN", Z, H.z - 0.12), M("LESS_THAN", M("ABSOLUTE", X), 0.2))
whip = M("MULTIPLY", M("MULTIPLY", lines, torso), M("MAXIMUM", back, M("GREATER_THAN", noise(2.2), 0.68)))
# кандалы: натёртое кольцо на правом запястье
hx = bone("hand_r").x
cuff = M("MULTIPLY", M("LESS_THAN", M("ABSOLUTE", M("SUBTRACT", X, hx + SIDE * 0.035)), 0.022), M("GREATER_THAN", noise(30), 0.35))
# рёбра, глазницы, щетина, бледность, грязь
rib = N.new("ShaderNodeTexWave"); rib.bands_direction = 'Z'
rib.inputs["Scale"].default_value = 9.0; rib.inputs["Distortion"].default_value = 0.6
L.new(P, rib.inputs["Vector"])
ribline = clamp(M("MULTIPLY", M("SUBTRACT", 0.45, rib.outputs["Fac"]), 3.0))
inz = M("MULTIPLY", M("GREATER_THAN", Z, bone("spine_02").z - 0.10), M("LESS_THAN", Z, bone("spine_03").z + 0.04))
flank = M("MULTIPLY", M("GREATER_THAN", M("ABSOLUTE", X), 0.045), M("LESS_THAN", M("ABSOLUTE", X), 0.15))
front_ = M("LESS_THAN", Y, bone("spine_02").y - 0.02)
ribs = M("MULTIPLY", M("MULTIPLY", ribline, M("MULTIPLY", inz, front_)), M("MULTIPLY", flank, 0.3))
sock = clamp(M("SUBTRACT", 1.0, M("MULTIPLY", M("MINIMUM", dist_to(EYE_R + Vector((0, -0.012, 0.004))),
                                                         dist_to(EYE_L + Vector((0, -0.012, 0.004)))), 30.0)))
# щетина: мягкая тень по челюсти с мелким зерном, без резких краёв
jlo = clamp(M("MULTIPLY", M("SUBTRACT", Z, H.z - 0.05), 40.0))
jhi = clamp(M("MULTIPLY", M("SUBTRACT", EZ - 0.045, Z), 40.0))
jfr = clamp(M("MULTIPLY", M("SUBTRACT", H.y + 0.02, Y), 40.0))
jaw = M("MULTIPLY", M("MULTIPLY", jlo, jhi), jfr)
grain = clamp(M("MULTIPLY", M("SUBTRACT", noise(1400, 1, 0.5), 0.35), 2.0))
stub = M("MULTIPLY", jaw, M("ADD", 0.16, M("MULTIPLY", grain, 0.14)))
grime = M("MULTIPLY", clamp(M("MULTIPLY", M("SUBTRACT", noise(4, 6), 0.45), 3.0)), 0.35)

c = mix(0.38, base_col, (0.60, 0.52, 0.43, 1))                 # болезненная бледность
c = mix(ribs, c, (0.45, 0.32, 0.28, 1), 'MULTIPLY')
c = mix(M("MULTIPLY", sock, 0.55), c, (0.42, 0.28, 0.27, 1), 'MULTIPLY')
c = mix(stub, c, (0.16, 0.13, 0.11, 1))
c = mix(grime, c, (0.55, 0.47, 0.40, 1), 'MULTIPLY')
c = mix(cuff, c, (0.45, 0.17, 0.14, 1))
HY0, HY1 = EY + 0.03, H.y + 0.04
ty = clamp(M("MULTIPLY", M("SUBTRACT", Y, HY0), 1.0 / (HY1 - HY0)))
hz = M("ADD", EZ + 0.062, M("MULTIPLY", ty, (H.z + 0.015) - (EZ + 0.062)))
earb = M("MULTIPLY", clamp(M("MULTIPLY", M("SUBTRACT", M("ABSOLUTE", X), 0.058), 40.0)),
         M("SUBTRACT", 1.0, M("ABSOLUTE", M("SUBTRACT", M("MULTIPLY", ty, 2.0), 1.0))))
hz = M("ADD", hz, M("MULTIPLY", earb, 0.075))
hz = M("ADD", hz, M("MULTIPLY", M("SUBTRACT", noise(60, 3), 0.5), 0.012))
scalp = M("MULTIPLY", M("GREATER_THAN", Z, hz), M("GREATER_THAN", Z, H.z - 0.04))
hairm = M("MULTIPLY", scalp, clamp(M("SUBTRACT", 1.0, M("MULTIPLY", burn, 2.5))))
hcol = ramp(noise(420, 2, 0.4), [(0.35, (0.035, 0.027, 0.022, 1)), (0.65, (0.11, 0.085, 0.065, 1))])
c = mix(M("MULTIPLY", hairm, 0.93), c, hcol)
c = mix(burn, c, burn_col)
c = mix(M("MULTIPLY", burn_edge, 0.5), c, (0.25, 0.08, 0.06, 1))
c = mix(spots, c, (0.38, 0.12, 0.09, 1))
c = mix(M("MULTIPLY", whip, 0.7), c, (0.66, 0.40, 0.37, 1))
# шрам-порез: от правой скулы через угол рта к подбородку
sa_, sb_ = Vector((-SIDE * 0.055, EY + 0.012, EZ - 0.03)), Vector((-SIDE * 0.004, EY, MZ - 0.03))
seg = N.new("ShaderNodeVectorMath"); seg.operation = 'SUBTRACT'; L.new(P, seg.inputs[0]); seg.inputs[1].default_value = sa_
dv = sb_ - sa_
sp = N.new("ShaderNodeSeparateXYZ"); L.new(seg.outputs[0], sp.inputs[0])
proj = clamp(M("DIVIDE", M("ADD", M("ADD", M("MULTIPLY", sp.outputs[0], dv.x), M("MULTIPLY", sp.outputs[1], dv.y)),
                           M("MULTIPLY", sp.outputs[2], dv.z)), dv.length_squared))
dx_ = M("SUBTRACT", sp.outputs[0], M("MULTIPLY", proj, dv.x))
dy_ = M("SUBTRACT", sp.outputs[1], M("MULTIPLY", proj, dv.y))
dz_ = M("SUBTRACT", sp.outputs[2], M("MULTIPLY", proj, dv.z))
dd = M("SQRT", M("ADD", M("ADD", M("MULTIPLY", dx_, dx_), M("MULTIPLY", dy_, dy_)), M("MULTIPLY", dz_, dz_)))
scar = M("MULTIPLY", clamp(M("MULTIPLY", M("SUBTRACT", 0.0028, dd), 600.0)), M("LESS_THAN", Y, EY + 0.03))
c = mix(scar, c, (0.62, 0.30, 0.28, 1))
rough_in = base_rgh if base_rgh is not None else 0.7
rgh = M("SUBTRACT", M("ADD", rough_in, 0.0) if not isinstance(rough_in, float) else rough_in,
        M("MULTIPLY", M("MAXIMUM", burn, M("MAXIMUM", spots, whip)), 0.35))


def bake(socket, name, colorspace='sRGB'):
    img = bpy.data.images.new(name, TEX_RES, TEX_RES, alpha=False)
    img.colorspace_settings.name = colorspace
    em = N.new("ShaderNodeEmission")
    if socket.type == 'VALUE':
        cv = N.new("ShaderNodeCombineColor")
        for i in range(3):
            L.new(socket, cv.inputs[i])
        socket = cv.outputs[0]
    L.new(socket, em.inputs[0])
    out = next(n for n in N if n.type == 'OUTPUT_MATERIAL')
    old = out.inputs["Surface"].links[0].from_socket
    L.new(em.outputs[0], out.inputs["Surface"])
    tn = N.new("ShaderNodeTexImage"); tn.image = img
    for n in N:
        n.select = False
    tn.select = True; N.active = tn
    sc = bpy.context.scene
    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True); bpy.context.view_layer.objects.active = body
    bpy.ops.object.bake(type='EMIT', margin=16, use_clear=True)
    L.new(old, out.inputs["Surface"])
    N.remove(tn); N.remove(em)
    img.filepath_raw = os.path.join(TEX, name + ".png"); img.file_format = 'PNG'; img.save()
    return img


sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'; sc.cycles.samples = 1
# временно — тело в покое, без позы
img_col = bake(c, "T_Hero_Body_BaseColor")
img_rgh = bake(rgh, "T_Hero_Body_Roughness", 'Non-Color')

# нормали: мускулатура атлета приглушена (истощённое тело)
src_normal.reload()
a = np.array(src_normal.pixels[:], dtype=np.float32).reshape(-1, 4)
nv = a[:, :3] * 2 - 1
nv = nv * 0.35 + np.array([0, 0, 1], dtype=np.float32) * 0.65
nv /= np.linalg.norm(nv, axis=1, keepdims=True)
a[:, :3] = nv * 0.5 + 0.5
img_nrm = bpy.data.images.new("T_Hero_Body_Normal", src_normal.size[0], src_normal.size[1], alpha=False)
img_nrm.colorspace_settings.name = 'Non-Color'
img_nrm.pixels[:] = a.ravel()
img_nrm.filepath_raw = os.path.join(TEX, "T_Hero_Body_Normal.png"); img_nrm.file_format = 'PNG'; img_nrm.save()

# волосы и брови: тёмные (не седые)
hair_mat = brows.data.materials[0]
himg = next(n.image for n in hair_mat.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and "Base" in n.image.name)
himg.reload()
a = np.array(himg.pixels[:], dtype=np.float32).reshape(-1, 4)
lum = a[:, :3].mean(axis=1, keepdims=True)
a[:, :3] = lum * np.array([0.16, 0.12, 0.09], dtype=np.float32)
img_hair = bpy.data.images.new("T_Hero_Hair_BaseColor", himg.size[0], himg.size[1], alpha=True)
img_hair.pixels[:] = a.ravel()
img_hair.filepath_raw = os.path.join(TEX, "T_Hero_Hair_BaseColor.png"); img_hair.file_format = 'PNG'; img_hair.save()


def simple_mat(name, col_img, rgh_img=None, nrm_img=None, rough=0.7, alpha=False):
    m = bpy.data.materials.new(name); m.use_nodes = True
    t = m.node_tree; b = next(n for n in t.nodes if n.type == "BSDF_PRINCIPLED")
    ti = t.nodes.new("ShaderNodeTexImage"); ti.image = col_img
    t.links.new(ti.outputs[0], b.inputs["Base Color"])
    if alpha:
        t.links.new(ti.outputs[1], b.inputs["Alpha"])
    if rgh_img:
        tr = t.nodes.new("ShaderNodeTexImage"); tr.image = rgh_img
        t.links.new(tr.outputs[0], b.inputs["Roughness"])
    else:
        b.inputs["Roughness"].default_value = rough
    if nrm_img:
        tn = t.nodes.new("ShaderNodeTexImage"); tn.image = nrm_img
        nm = t.nodes.new("ShaderNodeNormalMap"); nm.inputs["Strength"].default_value = 1.0
        t.links.new(tn.outputs[0], nm.inputs[1]); t.links.new(nm.outputs[0], b.inputs["Normal"])
    return m


hm = simple_mat("M_Hero_Hair", img_hair, rough=0.8, alpha=True)
for o in (brows,):
    o.data.materials.clear(); o.data.materials.append(hm)

# ---------------------------------------------------------------- 6. кожа: 4 зоны-материала (одежда прячет зону)
ZONES = ["M_Hero_Skin_Head", "M_Hero_Skin_Torso", "M_Hero_Skin_ArmR", "M_Hero_Skin_StumpArm", "M_Hero_Skin_StumpLegs"]
body.data.materials.clear()
for zn in ZONES:
    body.data.materials.append(simple_mat(zn, img_col, img_rgh, img_nrm))
gname = {g.index: g.name for g in body.vertex_groups}


def zone_of(vgw):
    best = max(vgw, key=vgw.get) if vgw else "spine_03"
    if best in ("Head", "neck_01"):
        return 0
    if best.endswith("_r") and not best.startswith(("thigh", "calf", "foot", "ball", "clavicle")):
        return 2
    if best == "upperarm_l":
        return 3
    if best in ("thigh_l", "thigh_r", "pelvis"):
        return 4
    return 1


vz = []
for v in body.data.vertices:
    vz.append(zone_of({gname[g.group]: g.weight for g in v.groups if g.group in gname}))
for poly in body.data.polygons:
    zs = [vz[i] for i in poly.vertices]
    poly.material_index = max(set(zs), key=zs.count)

# ---------------------------------------------------------------- 5. шейп-кеи
def add_keys(o, fns):
    if not o.data.shape_keys:
        o.shape_key_add(name="Basis", from_mix=False)
    mw = o.matrix_world; inv = mw.inverted()
    for name, fn in fns.items():
        k = o.shape_key_add(name=name, from_mix=False)
        for i, v in enumerate(o.data.vertices):
            p = mw @ v.co
            d = fn(p, v)
            if d is not None:
                k.data[i].co = inv @ (p + d)


def blink_r(p, v):
    dx = math.hypot(p.x - EYE_R.x, (p.z - EYE_R.z) * 0.9)
    if p.y > EYE_R.y + 0.01 or dx > 0.03:
        return None
    w = smooth01(1 - dx / 0.03)
    if p.z > EYE_R.z - 0.002:                                    # верхнее веко опускается на глаз
        return Vector((0, -0.004 * w, -(p.z - (EYE_R.z - 0.004)) * w))
    return Vector((0, 0, 0.002 * w))                             # нижнее чуть поднимается


def brow_zone(p):
    return EZ + 0.01 < p.z < EZ + 0.05 and p.y < EY + 0.03 and abs(p.x) < 0.07


def brow_sad(p, v):
    if not brow_zone(p):
        return None
    inner = smooth01(1 - abs(p.x) / 0.04)
    return Vector((0, 0, 0.005 * inner - 0.001 * (1 - inner)))


def brow_angry(p, v):
    if not brow_zone(p):
        return None
    inner = smooth01(1 - abs(p.x) / 0.04)
    return Vector((0, -0.001 * inner, -0.004 * inner))


def wince(p, v):
    d = Vector()
    if brow_zone(p):
        d += Vector((0, -0.001, -0.003))
    if abs(p.z - (EZ - 0.03)) < 0.025 and 0.02 < abs(p.x) < 0.07 and p.y < EY + 0.02:   # щёки вверх
        d += Vector((0, -0.001, 0.003 * smooth01(1 - abs(p.z - (EZ - 0.03)) / 0.025)))
    b = blink_r(p, v)
    if b is not None:
        d += b * 0.55
    if abs(p.z - MZ) < 0.015 and 0.012 < abs(p.x) < 0.04:                                # губы поджаты
        d += Vector((-math.copysign(0.002, p.x), 0, 0.001))
    return d if d.length > 0 else None


def frown(p, v):
    if abs(p.z - MZ) < 0.02 and 0.012 < abs(p.x) < 0.045 and p.y < EY:
        return Vector((0, 0, -0.004 * smooth01(1 - abs(p.z - MZ) / 0.02) * smooth01((abs(p.x) - 0.012) / 0.012)))
    return None


CH = (bone("spine_02").z - 0.05, bone("neck_01").z)


def breath(p, v):
    if not (CH[0] < p.z < CH[1] + 0.05) or abs(p.x) > 0.25:
        return None
    w = smooth01(1 - abs(p.z - (CH[0] + CH[1]) / 2) / ((CH[1] - CH[0]) / 2 + 0.05))
    ay = axis_y(p.z)
    return Vector((p.x * 0.03, (p.y - ay) * 0.06, 0.004)) * w


add_keys(body, {"Blink_R": blink_r, "Brow_Sad": brow_sad, "Brow_Angry": brow_angry,
                "Wince": wince, "Mouth_Frown": frown, "Breath_In": breath})
add_keys(brows, {"Brow_Sad": brow_sad, "Brow_Angry": brow_angry, "Wince": wince})

# ---------------------------------------------------------------- 4. риг
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = rig.data.edit_bones
inv = rig.matrix_world.inverted()
e = eb.new("eye_r"); e.head = inv @ EYE_R; e.tail = inv @ (EYE_R + Vector((0, -0.03, 0))); e.parent = eb["Head"]
hand = eb["hand_r"]; elbow = eb["lowerarm_r"]


def ctrl(name, head, tail, parent=None):
    b = eb.new(name); b.head = head; b.tail = tail; b.use_deform = False
    if parent:
        b.parent = eb[parent]
    return b


CTRLP = "root"                    # контролы — под root (не экспортируются, deform-only)
ctrl("CTRL_hand_r", hand.head.copy(), hand.tail.copy(), CTRLP)
pole_pos = elbow.head + Vector((0, 0.35, 0))
ctrl("CTRL_elbow_r", pole_pos, pole_pos + Vector((0, 0.08, 0)), CTRLP)
look = inv @ (EYE_R + Vector((SIDE * EX, -0.8, 0)))
ctrl("CTRL_look", look, look + Vector((0, 0, 0.08)), CTRLP)
for n in ("calf_l", "calf_r", "foot_l", "foot_r", "ball_l", "ball_r", "ball_leaf_l", "ball_leaf_r",
          "lowerarm_l", "hand_l"):
    pass
bpy.ops.object.mode_set(mode='POSE')
pb = rig.pose.bones
ik = pb["lowerarm_r"].constraints.new('IK')
ik.target = rig; ik.subtarget = "CTRL_hand_r"; ik.pole_target = rig; ik.pole_subtarget = "CTRL_elbow_r"; ik.chain_count = 2
cr = pb["hand_r"].constraints.new('COPY_ROTATION'); cr.target = rig; cr.subtarget = "CTRL_hand_r"
for bn, infl in (("neck_01", 0.3), ("Head", 0.6)):
    dt = pb[bn].constraints.new('DAMPED_TRACK'); dt.target = rig; dt.subtarget = "CTRL_look"; dt.track_axis = 'TRACK_Y'
    dt.influence = infl
dt = pb["eye_r"].constraints.new('DAMPED_TRACK'); dt.target = rig; dt.subtarget = "CTRL_look"; dt.track_axis = 'TRACK_Y'
bpy.context.view_layer.update()


def arm_err():
    bpy.context.view_layer.update()
    m = pb["lowerarm_r"].matrix; r = rig.data.bones["lowerarm_r"].matrix_local
    return (m.to_3x3() - r.to_3x3()).to_quaternion().angle if False else \
        sum((m.col[i].to_3d() - r.col[i].to_3d()).length for i in range(3))


best = min(range(-180, 181, 2), key=lambda a: (setattr(ik, "pole_angle", math.radians(a)), arm_err())[1])
ik.pole_angle = math.radians(best)
best2 = min([best + d * 0.25 for d in range(-8, 9)], key=lambda a: (setattr(ik, "pole_angle", math.radians(a)), arm_err())[1])
ik.pole_angle = math.radians(best2)
print("IK pole angle", best2, "err", arm_err())
# head look-at не должен сдвигать голову в покое — Damped Track оси Y кости смотрит на CTRL_look;
# при авторинге его влияние регулируется, в Unity это делает свой Look-At.
for bn in ("neck_01", "Head"):
    for c_ in pb[bn].constraints:
        c_.enabled = False          # включать по необходимости в Blender
bpy.ops.object.mode_set(mode='OBJECT')

# коллекции костей
arm_d = rig.data
for nm in ("Deform", "Controls", "Amputated (no weights)"):
    if nm not in arm_d.collections:
        arm_d.collections.new(nm)
AMP = {"calf_l", "calf_r", "foot_l", "foot_r", "ball_l", "ball_r", "ball_leaf_l", "ball_leaf_r", "lowerarm_l", "hand_l"}
AMP |= {b.name for b in arm_d.bones if b.name.endswith("_l") and b.name.split("_")[0] in
        ("index", "middle", "pinky", "ring", "thumb")}
for b in arm_d.bones:
    nm = "Controls" if b.name.startswith("CTRL_") else ("Amputated (no weights)" if b.name in AMP else "Deform")
    arm_d.collections[nm].assign(b)

# глаз → кость eye_r; веса ампутированного у кожи — в ноль (могли остаться крохи)
g = eyes.vertex_groups.get("eye_r") or eyes.vertex_groups.new(name="eye_r")
for vg in list(eyes.vertex_groups):
    if vg.name != "eye_r":
        eyes.vertex_groups.remove(vg)
g.add(range(len(eyes.data.vertices)), 1.0, 'REPLACE')
for o in (body,):
    for nm in AMP:
        vg = o.vertex_groups.get(nm)
        if vg:
            o.vertex_groups.remove(vg)


# ---------------------------------------------------------------- 7. одежда
def fit_outfit(tag, files, thin=0.45, gap=0.012):
    parts = []
    for f in files:
        for o in imp(OUTF + f + ".gltf"):
            if o.type == 'ARMATURE':
                bpy.data.objects.remove(o); continue
            if o.type != 'MESH' or o.name.startswith("Icosphere"):
                bpy.data.objects.remove(o)
                continue
            mwo = o.matrix_world.copy(); o.parent = rig; o.matrix_world = mwo
            for md in o.modifiers:
                if md.type == 'ARMATURE':
                    md.object = rig
            # кожа из нарядов не нужна — своё тело под одеждой
            skin = [i for i, m in enumerate(o.data.materials) if m and ("Male" in m.name or "Superhero" in m.name)]
            if skin and len(skin) < len(o.data.materials):
                bm = bmesh.new(); bm.from_mesh(o.data)
                bmesh.ops.delete(bm, geom=[fc for fc in bm.faces if fc.material_index in skin], context='FACES')
                bm.to_mesh(o.data); bm.free()
            emaciate(o, thin)
            amputate(o, caps=True, cap_k=0.5)
            o.name = o.data.name = "Outfit_%s_%s" % (tag, f.split("_")[-1])
            parts.append(o)
    # вытолкнуть ткань из тела. Сдвиг берём максимальный по соседству (все слои наряда вместе),
    # чтобы слои ткани (рубаха, подкладка, наплечник) сдвигались одинаково и не слипались
    from mathutils.kdtree import KDTree
    dg = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(body, dg, deform=False)
    imw = body.matrix_world.inverted(); bm3 = body.matrix_world.to_3x3()
    allv = []                               # (obj, index, world pos, need, normal)
    for o in parts:
        mw = o.matrix_world
        for v in o.data.vertices:
            p = mw @ v.co
            hit = bvh.find_nearest(imw @ p, 0.08)
            need, n = 0.0, Vector((0, 0, 1))
            if hit[0] is not None:
                q = body.matrix_world @ hit[0]; n = (bm3 @ hit[1]).normalized()
                need = max(0.0, gap - (p - q).dot(n))
            allv.append((o, v.index, p, need, n))
    kd = KDTree(len(allv))
    for i, it in enumerate(allv):
        kd.insert(it[2], i)
    kd.balance()
    moved = 0
    for o, vi, p, need, n in allv:
        m = max([allv[j][3] for (_, j, _) in kd.find_range(p, 0.025)] + [need])
        if m > 0:
            o.data.vertices[vi].co = o.matrix_world.inverted() @ (p + n * m); moved += 1
    print("outfit", tag, "pushed", moved)
    col = bpy.data.collections.new("Outfit_" + tag); bpy.context.scene.collection.children.link(col)
    for o in parts:
        for cl in o.users_collection:
            cl.objects.unlink(o)
        col.objects.link(o)
    return parts


peasant = fit_outfit("Peasant", ["Male_Peasant_Body", "Male_Peasant_Arms", "Male_Peasant_Legs"])
for o in list(bpy.data.objects):
    if o.type == 'EMPTY' or o.name.startswith("Icosphere"):
        bpy.data.objects.remove(o)

# простые материалы для Unity (URP распознаёт базовый цвет/нормаль по описанию FBX)
def find_img(m, key):
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image and key in n.image.name:
            return n.image
    return None


done = {}
for o in peasant + [eyes]:
    used = {p.material_index for p in o.data.polygons}
    mats = [m for i, m in enumerate(o.data.materials) if i in used]
    remap = {i: mats.index(m) for i, m in enumerate(o.data.materials) if i in used}
    newm = []
    for m in mats:
        key = "eye" if o is eyes else m.name.split(".")[0]
        if key in done:
            done[m.name] = done[key]
        if m.name not in done:
            col = find_img(m, "BaseColor") or find_img(m, "Eye_Brown") or find_img(m, "Base")
            nrm = find_img(m, "Normal")
            if nrm:
                nrm.colorspace_settings.name = 'Non-Color'
            nm = "M_Hero_Eye" if o is eyes else "M_Outfit_" + m.name.split(".")[0].replace("MI_", "")
            done[m.name] = done[key] = simple_mat(nm, col, None, nrm, rough=0.85 if o is not eyes else 0.2) if col else m
        newm.append(done[m.name])
    idx = [remap[p.material_index] for p in o.data.polygons]
    o.data.materials.clear()
    for m in newm:
        o.data.materials.append(m)
    for p, i in zip(o.data.polygons, idx):
        p.material_index = i

# ---------------------------------------------------------------- сохранить + экспорт
for o in bpy.data.objects:
    if o.type == 'MESH':
        for md in o.modifiers:
            if md.type == 'ARMATURE':
                md.object = rig
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "hero.blend"))

os.makedirs(UNITY + "Textures", exist_ok=True)
if os.path.isdir(UNITY + "Hero.fbm"):
    shutil.rmtree(UNITY + "Hero.fbm")
imgs = set()
for o in bpy.data.objects:
    if o.type == 'MESH':
        for m in o.data.materials:
            if m and m.use_nodes:
                imgs |= {n.image for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image}
for im in imgs:
    src = bpy.path.abspath(im.filepath_raw or im.filepath)
    dst = UNITY + "Textures/" + im.name.split(".png")[0] + ".png"
    if os.path.isfile(src):
        shutil.copyfile(src, dst)
    elif im.size[0] > 0:
        im.filepath_raw = dst; im.file_format = 'PNG'; im.save()
    else:
        print("texture missing, skipped:", im.name); continue
    print("texture", im.name, "->", dst)
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type in ('MESH', 'ARMATURE'):
        o.select_set(True)
bpy.ops.export_scene.fbx(filepath=UNITY + "Hero.fbx", use_selection=True, object_types={'ARMATURE', 'MESH'},
                         use_armature_deform_only=True, add_leaf_bones=False, bake_anim=False,
                         use_mesh_modifiers=False, mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False,
                         axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL')
print("EXPORTED", UNITY + "Hero.fbx")
