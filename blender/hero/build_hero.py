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
TEX_RES = 4096
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


def dome(bm, edges, mw, outward, flat=None, k=0.6):
    """Закрыть дыру (по краевым рёбрам) и выгнуть куполом: holes_fill → два inset-кольца → центр.
    Ориентацию граней держит сам Blender; UV новых вершин — от ближайшей вершины края (кожа культи)."""
    inv3 = mw.inverted().to_3x3()
    uvl = bm.loops.layers.uv.active
    rim = {v for e in edges for v in e.verts}
    rim_uv = {v: (v.link_loops[0][uvl].uv.copy() if v.link_loops else Vector((0, 0))) for v in rim}
    pts = [mw @ v.co for v in rim]
    c = sum(pts, Vector()) / len(pts)
    rad = sum((p - c).length for p in pts) / len(pts)
    h = flat if flat is not None else rad * k
    faces = bmesh.ops.holes_fill(bm, edges=edges, sides=1000)["faces"]
    if not faces:
        faces = bmesh.ops.contextual_create(bm, geom=edges)["faces"]
    if not faces:
        print("dome: nothing filled", len(edges)); return
    print("dome: filled", len(faces), "faces from", len(edges), "edges")
    new = set()
    for t, d in ((rad * 0.30, h * 0.55), (rad * 0.30, h * 0.30)):
        r = bmesh.ops.inset_region(bm, faces=faces, thickness=t, depth=0.0, use_even_offset=True)
        for f in faces:
            for v in f.verts:
                new.add(v)
        mv = {v for f in faces for v in f.verts}
        for v in mv:
            v.co += inv3 @ (outward * d)
    pk = bmesh.ops.poke(bm, faces=faces)
    for v in pk["verts"]:
        v.co += inv3 @ (outward * h * 0.15)
        new.add(v)
    # UV: новые вершины берут UV ближайшей вершины края
    rim_l = list(rim)
    for v in new:
        if v in rim:
            continue
        p = mw @ v.co
        near = min(rim_l, key=lambda q: ((mw @ q.co) - p).length_squared)
        for l in v.link_loops:
            l[uvl].uv = rim_uv[near]
    for f in bm.faces:
        if any(v in new for v in f.verts):
            f.smooth = True


def gather_hems(o, length=0.07, tight=0.22, extend=0.02):
    """Край одежды у культи: стянуть к оси конечности (подвёрнут и присборен), без заглушек.
    Вершины в последних `length` метрах до среза сжимаются к оси до `tight` радиуса и чуть выходят за срез."""
    mw = o.matrix_world; inv = mw.inverted()
    axes = []
    for s_ in "lr":
        h, t = bseg("thigh_" + s_); d = (t - h).normalized()
        end = (Vector((h.x, h.y, STUMP_Z)) - h).dot(d)          # длина бедра до среза
        axes.append((h, d, end))
    axes.append((SA, DA, ARM_LEN))
    n = 0
    for v in o.data.vertices:
        p = mw @ v.co
        for h, d, end in axes:
            u = (p - h).dot(d)
            if u < end - length or u > end + 0.01:
                continue
            c = h + d * u
            r = p - c
            if r.length > 0.16:          # далеко от оси — не эта конечность
                continue
            t = smooth01((u - (end - length)) / length)
            k = 1.0 + (tight - 1.0) * t
            v.co = inv @ (c + r * k + d * (extend * t))
            n += 1
            break
    o.data.update()
    print("hems gathered", o.name, n)


ARM_BONES = {"upperarm_l", "lowerarm_l", "hand_l"} | {f"{f}_0{i}_l" for f in ("index", "middle", "pinky", "ring", "thumb") for i in (1, 2, 3)}     | {f"{f}_04_leaf_l" for f in ("index", "middle", "pinky", "ring", "thumb")}
LEG_BONES = {s_: {f"thigh_{s_}", f"calf_{s_}", f"foot_{s_}", f"ball_{s_}", f"ball_leaf_{s_}"} for s_ in "lr"}


def collapse_limbs(o, dome_arm=0.05, dome_leg=0.07):
    """Ампутация без разрезов: всё дальше среза плавно стягивается в купол культи.
    Сетка остаётся замкнутой (ни дыр, ни заглушек); вес отнятых костей уходит в кость культи.
    Одинаково работает для тела и для одежды (рукав/штанина — зашитый конец)."""
    mw = o.matrix_world; inv = mw.inverted()
    limbs = []
    h, t = bseg("upperarm_l")
    limbs.append((SA, DA, ARM_LEN, dome_arm, ARM_BONES, "upperarm_l"))
    for s_ in "lr":
        h, t = bseg("thigh_" + s_); d = (t - h).normalized()
        end = (Vector((h.x, h.y, STUMP_Z)) - h).dot(d)
        limbs.append((h, d, end, dome_leg, LEG_BONES[s_], "thigh_" + s_))
    gname = {g.index: g.name for g in o.vertex_groups}
    gidx = {g.name: g.index for g in o.vertex_groups}
    moved = 0
    for v in o.data.vertices:
        w = {gname[g.group]: g.weight for g in v.groups if g.group in gname}
        tot = sum(w.values()) or 1.0
        for h, d, end, dome, names, keep in limbs:
            if sum(w.get(n, 0) for n in names) / tot < 0.5:
                continue
            p = mw @ v.co
            u = (p - h).dot(d)
            s0 = end - dome
            if u <= s0:
                break
            c = h + d * u
            x = (u - s0) / dome
            tt = 1.0 - math.exp(-1.3 * x)                       # сжатие длины в купол
            rk = math.sqrt(max(0.0, 1.0 - tt * tt))             # полусфера
            v.co = inv @ (h + d * (s0 + dome * tt) + (p - c) * rk)
            # вес отнятых костей → кость культи
            lost = sum(wt for n, wt in w.items() if n in names and n != keep)
            if lost > 0 and keep in gidx:
                for n in names:
                    if n != keep and n in gidx:
                        o.vertex_groups[gidx[n]].remove([v.index])
                o.vertex_groups[gidx[keep]].add([v.index], w.get(keep, 0) + lost, 'REPLACE')
            moved += 1
            break
    o.data.update()
    print("limbs collapsed", o.name, moved)


def amputate(o, caps=True, cap_k=0.6, face_cuts=False):
    """Культи — сжатием конечностей (collapse_limbs); для тела ещё ухо (смято) и выжженный глаз (сросшееся веко)."""
    collapse_limbs(o)
    mw = o.matrix_world
    bm = bmesh.new(); bm.from_mesh(o.data)
    bm.verts.layers.deform.verify()
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
        for loop in boundary_loops(bm):
            c = sum((mw @ v.co for v in loop), Vector()) / len(loop)
            if (c - EYE_L).length < 0.04:
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
lines = M("MULTIPLY", M("GREATER_THAN", wave.outputs["Fac"], 0.993), M("GREATER_THAN", noise(5.0), 0.45))
back = M("GREATER_THAN", Y, bone("spine_02").y + 0.01)
torso = M("MULTIPLY", M("MULTIPLY", M("LESS_THAN", Z, H.z - 0.12), M("GREATER_THAN", Z, bone("pelvis").z + 0.08)), M("LESS_THAN", M("ABSOLUTE", X), 0.2))
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
rough_in = base_rgh if base_rgh is not None else 0.7
SCULPT_RES = 2048          # маски, AO, кривизна
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'; sc.cycles.samples = 1
OUT_NODE = next(n for n in N if n.type == 'OUTPUT_MATERIAL')
BSDF_OUT = OUT_NODE.inputs["Surface"].links[0].from_socket


def bake_img(name, res, colorspace='sRGB', socket=None, btype='EMIT', src=None, samples=1, save=True):
    """Запекание в картинку по UV тела. src — высокополигональная копия (selected → active)."""
    img = bpy.data.images.new(name, res, res, alpha=False)
    img.colorspace_settings.name = colorspace
    em = None
    if socket is not None:
        em = N.new("ShaderNodeEmission")
        if socket.type == 'VALUE':
            cv = N.new("ShaderNodeCombineColor")
            for i in range(3):
                L.new(socket, cv.inputs[i])
            socket = cv.outputs[0]
        L.new(socket, em.inputs[0]); L.new(em.outputs[0], OUT_NODE.inputs["Surface"])
    tn = N.new("ShaderNodeTexImage"); tn.image = img
    for n in N:
        n.select = False
    tn.select = True; N.active = tn
    bpy.ops.object.select_all(action='DESELECT')
    if src is not None:
        src.hide_set(False); src.hide_render = False; src.select_set(True)
    body.select_set(True); bpy.context.view_layer.objects.active = body
    sc.cycles.samples = samples
    bpy.ops.object.bake(type=btype, margin=16, use_clear=True, use_selected_to_active=src is not None,
                        cage_extrusion=0.012, max_ray_distance=0.03)
    if src is not None:
        src.select_set(False); src.hide_render = True
    L.new(BSDF_OUT, OUT_NODE.inputs["Surface"])
    N.remove(tn)
    if em:
        N.remove(em)
    if save:
        img.filepath_raw = os.path.join(TEX, name + ".png"); img.file_format = 'PNG'; img.save()
    return img


def img_array(img):
    return np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)


# ---- 3a. маски увечий: R ожог, G плеть, B клеймо-прижигания
mk = N.new("ShaderNodeCombineColor")
L.new(burn, mk.inputs[0]); L.new(whip, mk.inputs[1]); L.new(spots, mk.inputs[2])
img_mask = bake_img("T_Hero_Masks", SCULPT_RES, 'Non-Color', mk.outputs[0])
MASK = img_array(img_mask)

# ---- 3b. скульпт: высокополигональная копия + рельеф (рубцы, морщины, складки)
from mathutils import noise as mn
hp = body.copy(); hp.data = body.data.copy(); hp.name = hp.data.name = "Hero_Body_Sculpt"
bpy.context.scene.collection.objects.link(hp)
hp.parent = None; hp.matrix_world = body.matrix_world.copy()
hp.modifiers.clear(); hp.vertex_groups.clear()
sub = hp.modifiers.new("Sub", 'SUBSURF'); sub.levels = sub.render_levels = 3
bpy.ops.object.select_all(action='DESELECT'); hp.select_set(True); bpy.context.view_layer.objects.active = hp
bpy.ops.object.modifier_apply(modifier="Sub")
me = hp.data; nV = len(me.vertices)
co = np.empty(nV * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
nr = np.empty(nV * 3, np.float32); me.vertices.foreach_get("normal", nr); nr = nr.reshape(-1, 3)
mwn = np.array(hp.matrix_world, dtype=np.float32)
W = co @ mwn[:3, :3].T + mwn[:3, 3]
li = np.empty(len(me.loops), np.int64); me.loops.foreach_get("vertex_index", li)
uvs = np.empty(len(me.loops) * 2, np.float32); me.uv_layers.active.data.foreach_get("uv", uvs)
vuv = np.zeros((nV, 2), np.float32); vuv[li] = uvs.reshape(-1, 2)
px = np.clip((vuv * SCULPT_RES).astype(np.int64), 0, SCULPT_RES - 1)
mB = MASK[px[:, 1], px[:, 0], 0]; mW = MASK[px[:, 1], px[:, 0], 1]; mS = MASK[px[:, 1], px[:, 0], 2]
print("sculpt verts", nV)

nz1 = np.array([mn.noise(Vector(p) * 55.0) for p in W], np.float32)
nzp = np.array([mn.noise(Vector(p) * 18.0 + Vector((3, 9, 2))) for p in W], np.float32)
nz2 = np.array([mn.noise(Vector(p) * 30.0 + Vector((7, 3, 1))) for p in W], np.float32)
nz3 = np.array([mn.noise(Vector(p) * 600.0) for p in W], np.float32)
x, y, z = W[:, 0], W[:, 1], W[:, 2]
h = np.zeros(nV, np.float32)
# ожог: стянутая бугристая рубцовая ткань, валик по краю
ridged = 1.0 - np.abs(nz1)
edge = np.clip(mB * (1 - mB) * 4, 0, 1)
h += mB * (0.0007 * ridged + 0.0012 * nzp + 0.0004 * nz2 - 0.0004) + 0.0013 * edge ** 2
# плеть — вздутые рубцы; клеймо — кратеры
h += 0.0010 * mW
h -= 0.0008 * mS
# кожа: поры
h += 0.00012 * nz3 * (1 - mB)
# лицо (жёстче): носогубные складки, лоб, межбровье, мешок и носослёзная борозда, «гусиные лапки»
front = (y < EY + 0.03) & (z > H.z - 0.02)


def seg_dist2d(ax, az, bx, bz):
    dx, dz = bx - ax, bz - az
    t = np.clip(((x - ax) * dx + (z - az) * dz) / (dx * dx + dz * dz), 0, 1)
    return np.hypot(x - (ax + t * dx), z - (az + t * dz))


for sx in (-1, 1):
    d = seg_dist2d(sx * 0.020, MZ + 0.032, sx * 0.037, MZ - 0.014)
    h -= front * 0.0016 * np.exp(-(d / 0.0035) ** 2)
fz = (z - (EZ + 0.028)) / 0.013
fh = np.clip(1 - np.abs(x) / 0.05, 0, 1) * ((z > EZ + 0.026) & (z < EZ + 0.078))
h -= front * fh * 0.00045 * np.clip(np.cos(fz * 2 * np.pi), 0, 1) ** 6
gl = (z > EZ + 0.006) & (z < EZ + 0.032)
h -= front * gl * 0.0007 * np.exp(-((np.abs(x) - 0.005) / 0.0018) ** 2)
ex, ez = EYE_R.x, EZ
d = seg_dist2d(ex + SIDE * 0.012, ez - 0.019, ex - SIDE * 0.016, ez - 0.013)
h -= front * 0.0008 * np.exp(-(d / 0.003) ** 2)
d = seg_dist2d(ex + SIDE * 0.010, ez - 0.012, ex - SIDE * 0.013, ez - 0.008)
h += front * 0.0005 * np.exp(-(d / 0.003) ** 2)
oc = ex - SIDE * 0.019
for k in (-1, 0, 1):
    d = seg_dist2d(oc, ez + k * 0.002, oc - SIDE * 0.012, ez + k * 0.006)
    h -= front * 0.0003 * np.exp(-(d / 0.0012) ** 2)
# шрам-порез через правую щёку — валик
pa, pb_ = np.array(sa_, np.float32), np.array(sb_, np.float32)
dv_ = pb_ - pa
t = np.clip(((W - pa) @ dv_) / (dv_ @ dv_), 0, 1)
d = np.linalg.norm(W - (pa + t[:, None] * dv_), axis=1)
h += (y < EY + 0.03) * 0.0007 * np.exp(-(d / 0.0016) ** 2)
# рёбра (передние бока) — дуги рёбер и межрёберные впадины
rz = z + 0.25 * np.abs(x)
ribz = (z > bone("spine_02").z - 0.10) & (z < bone("spine_03").z + 0.04)
flk = np.clip((np.abs(x) - 0.04) / 0.03, 0, 1) * np.clip((0.16 - np.abs(x)) / 0.03, 0, 1)
fr = np.array([axis_y(zz) for zz in z], np.float32)
h += ribz * flk * (y < fr) * 0.0016 * (np.cos(rz * 2 * np.pi / 0.032))
# культи — сморщенный шов
tip = np.clip((STUMP_Z + 0.03 - z) / 0.03, 0, 1) + np.clip(((W - np.array(SA)) @ np.array(DA) - (ARM_LEN - 0.01)) / 0.03, 0, 1)
h -= np.clip(tip, 0, 1) * 0.0009 * np.abs(nz1)
co += nr * h[:, None]
me.vertices.foreach_set("co", co.ravel()); me.update()
print("sculpt height range", float(h.min()), float(h.max()))
# материал копии: кривизна (pointiness) для запекания
cm = bpy.data.materials.new("Sculpt_Curvature"); cm.use_nodes = True
ct = cm.node_tree
ct.nodes.remove(next(n for n in ct.nodes if n.type == "BSDF_PRINCIPLED"))
geo = ct.nodes.new("ShaderNodeNewGeometry")
cr_ = ct.nodes.new("ShaderNodeValToRGB"); cr_.color_ramp.elements[0].position = 0.42; cr_.color_ramp.elements[1].position = 0.58
ce = ct.nodes.new("ShaderNodeEmission")
ct.links.new(geo.outputs["Pointiness"], cr_.inputs[0]); ct.links.new(cr_.outputs[0], ce.inputs[0])
ct.links.new(ce.outputs[0], next(n for n in ct.nodes if n.type == "OUTPUT_MATERIAL").inputs[0])
hp.data.materials.clear(); hp.data.materials.append(cm)
scol = bpy.data.collections.new("Sculpt (bake source)"); bpy.context.scene.collection.children.link(scol)
bpy.context.scene.collection.objects.unlink(hp); scol.objects.link(hp)

# ---- 3c. запекание скульпта на игровую модель
img_hpn = bake_img("T_Hero_SculptNormal", TEX_RES, 'Non-Color', btype='NORMAL', src=hp, save=False)
img_ao = bake_img("T_Hero_AO", SCULPT_RES, 'Non-Color', btype='AO', src=hp, samples=24)
img_cv = bake_img("T_Hero_Curvature", SCULPT_RES, 'Non-Color', btype='EMIT', src=hp)


def tex(img):
    t = N.new("ShaderNodeTexImage"); t.image = img; t.interpolation = 'Linear'
    return t.outputs[0]


AO = M("ADD", tex(img_ao), 0.0)
CV = M("ADD", tex(img_cv), 0.0)
concave = clamp(M("MULTIPLY", M("SUBTRACT", 0.5, CV), 4.0))
convex = clamp(M("MULTIPLY", M("SUBTRACT", CV, 0.5), 4.0))

# ---- 3d. цвет: болезненная кожа, грязь в складках, синяки, многослойный ожог
c = mix(0.38, base_col, (0.60, 0.52, 0.43, 1))                 # бледность
c = mix(ribs, c, (0.55, 0.40, 0.36, 1), 'MULTIPLY')
c = mix(M("MULTIPLY", sock, 0.55), c, (0.42, 0.28, 0.27, 1), 'MULTIPLY')
c = mix(stub, c, (0.16, 0.13, 0.11, 1))
c = mix(grime, c, (0.55, 0.47, 0.40, 1), 'MULTIPLY')
c = mix(cuff, c, (0.45, 0.17, 0.14, 1))
# синяки: правые рёбра, правое плечо, у культи — фиолетовый центр, жёлто-зелёный край
for ctr, rad in ((Vector((-SIDE * 0.10, bone("spine_02").y - 0.06, bone("spine_02").z)), 0.06),
                 (bone("upperarm_r") + Vector((-SIDE * 0.10, 0, -0.02)), 0.05),
                 (SA + Vector((-SIDE * 0.05, -0.05, -0.06)), 0.05)):
    bd = clamp(M("SUBTRACT", 1.0, M("DIVIDE", M("ADD", dist_to(ctr), M("MULTIPLY", M("SUBTRACT", noise(25, 3), 0.5), 0.03)), rad)))
    bcol = ramp(bd, [(0.0, (0.62, 0.58, 0.36, 1)), (0.45, (0.50, 0.40, 0.42, 1)), (0.85, (0.33, 0.18, 0.28, 1))])
    c = mix(M("MULTIPLY", clamp(M("MULTIPLY", bd, 3.0)), 0.45), c, bcol)
c = mix(M("MULTIPLY", hairm, 0.93), c, hcol)
# ожог: тёмное вино в складках → красный → розовый блестящий на гребнях; белёсые зажившие пятна; тёмный край
bn = noise(9, 6, 0.6)
burn_base = ramp(bn, [(0.30, (0.36, 0.10, 0.09, 1)), (0.50, (0.60, 0.22, 0.19, 1)), (0.70, (0.80, 0.45, 0.40, 1))])
bc = mix(M("MULTIPLY", concave, 0.8), burn_base, (0.24, 0.06, 0.07, 1))
bc = mix(M("MULTIPLY", convex, 0.7), bc, (0.93, 0.66, 0.60, 1))
pale = M("MULTIPLY", clamp(M("MULTIPLY", M("SUBTRACT", noise(4.5, 4), 0.58), 8.0)), 0.75)
bc = mix(pale, bc, (0.86, 0.72, 0.66, 1))
c = mix(burn, c, bc)
c = mix(M("MULTIPLY", burn_edge, 0.55), c, (0.30, 0.09, 0.07, 1))
c = mix(spots, c, (0.30, 0.10, 0.08, 1))
c = mix(M("MULTIPLY", whip, 0.6), c, (0.60, 0.36, 0.34, 1))
c = mix(scar, c, (0.62, 0.30, 0.28, 1))
# объём со скульпта: AO, грязь в складках, светлые гребни
c = mix(0.6, c, M("ADD", M("MULTIPLY", AO, 0.5), 0.5), 'MULTIPLY')
c = mix(M("MULTIPLY", concave, 0.45), c, (0.20, 0.15, 0.12, 1), 'MULTIPLY')
c = mix(M("MULTIPLY", convex, 0.18), c, (1.0, 0.92, 0.85, 1), 'SCREEN')
rgh = clamp(M("ADD", M("SUBTRACT", rough_in, M("MULTIPLY", burn, M("ADD", 0.22, M("MULTIPLY", convex, 0.25)))),
              M("MULTIPLY", concave, 0.15)))

img_col = bake_img("T_Hero_Body_BaseColor", TEX_RES, 'sRGB', c)
img_rgh = bake_img("T_Hero_Body_Roughness", TEX_RES, 'Non-Color', rgh)

# ---- 3e. нормали: скульпт + приглушённая деталь исходника (whiteout blend)
src_normal.reload()
sn = src_normal.copy(); sn.scale(TEX_RES, TEX_RES)
a = img_array(sn)[:, :, :3] * 2 - 1
a = a * 0.35 + np.array([0, 0, 1], np.float32) * 0.65
b = img_array(img_hpn)[:, :, :3] * 2 - 1
n_ = np.dstack([a[:, :, 0] + b[:, :, 0], a[:, :, 1] + b[:, :, 1], a[:, :, 2] * b[:, :, 2]])
n_ /= np.linalg.norm(n_, axis=2, keepdims=True)
out = np.ones((TEX_RES, TEX_RES, 4), np.float32); out[:, :, :3] = n_ * 0.5 + 0.5
img_nrm = bpy.data.images.new("T_Hero_Body_Normal", TEX_RES, TEX_RES, alpha=False)
img_nrm.colorspace_settings.name = 'Non-Color'
img_nrm.pixels[:] = out.ravel()
img_nrm.filepath_raw = os.path.join(TEX, "T_Hero_Body_Normal.png"); img_nrm.file_format = 'PNG'; img_nrm.save()
bpy.data.images.remove(sn)
hp.hide_set(True); hp.hide_render = True

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
            amputate(o, caps=False)
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
    if o.type in ('MESH', 'ARMATURE') and not o.name.startswith("Hero_Body_Sculpt"):
        o.select_set(True)
bpy.ops.export_scene.fbx(filepath=UNITY + "Hero.fbx", use_selection=True, object_types={'ARMATURE', 'MESH'},
                         use_armature_deform_only=True, add_leaf_bones=False, bake_anim=False,
                         use_mesh_modifiers=False, mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False,
                         axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL')
print("EXPORTED", UNITY + "Hero.fbx")
