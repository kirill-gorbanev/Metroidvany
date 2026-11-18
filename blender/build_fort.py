"""Сборка локации «Крепость-тюрьма» (демо метроидвании, вид сбоку 2.5D).

Оси: X — вдоль локации (игровая плоскость y≈0), Y — глубина (+ дальше от камеры), Z — вверх.
Разрез для вида сбоку — по y = CUT_Y (всё ближе к камере отсекается).

Структура (по схеме автора):
  B1 (-5 м):  ЛЕЧЕНИЕ — коридор — ТЮРЬМА (камеры №1/№2, лаз вверх в зал) — туннель — арка 1
  L0 (0 м):   ворота поставки | СКЛАД — ЕДА (очаг + ТРУБА) | ЦЕНТРАЛЬНЫЙ ЗАЛ (донжон) | ДВОР + КАТОРГА | главные ворота
  Донжон:     СОН (5.25) → НАБЛЮДЕНИЕ (9.0) → СИГНАЛ (15.75, под шатром)
  Кольцо:     частокол R=36.5, берма, ров R 39.5..47.5 (внутри границы), магический круг по ВНЕШНЕЙ кромке рва

Всё узнаваемое — готовые ассеты (fortkit). Блокаут (LD_BLOCKOUT) — только коллизия уровня:
грунт с вырезами, плиты перекрытий, перегородки; текстуры — из пака FantasyEnvironments.
"""
import bpy, bmesh, math, importlib, sys
from mathutils import Vector

sys.path.insert(0, r"D:/Metroidvany/blender")
import fortkit as fk
importlib.reload(fk)

CUT_Y = -2.0
FE_ENVTEX = fk.FE_ENVTEX

# ------------------------------------------------------------------ helpers
def coll(name, parent="FORT"):
    return fk._coll(name, fk._coll(parent) if parent else None)


def box(name, x0, x1, y0, y1, z0, z1, mat=None, c="LD_BLOCKOUT"):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = x0 + (v.co.x + 0.5) * (x1 - x0)
        v.co.y = y0 + (v.co.y + 0.5) * (y1 - y0)
        v.co.z = z0 + (v.co.z + 0.5) * (z1 - z0)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    coll(c).objects.link(o)
    if mat:
        o.data.materials.append(mat)
    return o


def cyl(name, r, z0, z1, verts=96, c="LD_BLOCKOUT", mat=None):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r, depth=z1 - z0)
    for v in bm.verts:
        v.co.z += (z0 + z1) / 2
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    coll(c).objects.link(o)
    if mat:
        o.data.materials.append(mat)
    return o


def cutter(target, cut, name=None, solver="EXACT"):
    cut.display_type = "WIRE"
    cut.hide_render = True
    m = target.modifiers.new(name or ("cut_" + cut.name), "BOOLEAN")
    m.operation = "DIFFERENCE"
    m.object = cut
    m.solver = solver
    return m


def ground_material():
    name = "FK_ground"
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; N = nt.nodes; L = nt.links
    bsdf = next(n for n in N if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = 0.95
    tc = N.new("ShaderNodeTexCoord"); mp = N.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (0.18, 0.18, 0.18)
    L.new(tc.outputs["Object"], mp.inputs["Vector"])

    def tex(fn):
        t = N.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(FE_ENVTEX + "/" + fn, check_existing=True)
        t.projection = "BOX"; t.projection_blend = 0.3
        L.new(mp.outputs["Vector"], t.inputs["Vector"])
        return t
    grass, soil, stones = tex("ground_grass.png"), tex("ground_soil.png"), tex("ground_stones.png")
    geo = N.new("ShaderNodeNewGeometry")
    sep_n = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Normal"], sep_n.inputs[0])
    up = N.new("ShaderNodeMapRange"); up.inputs[1].default_value = 0.55; up.inputs[2].default_value = 0.8
    L.new(sep_n.outputs["Z"], up.inputs[0])
    sep_p = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], sep_p.inputs[0])
    above = N.new("ShaderNodeMapRange"); above.inputs[1].default_value = -0.6; above.inputs[2].default_value = -0.2
    L.new(sep_p.outputs["Z"], above.inputs[0])
    m1 = N.new("ShaderNodeMix"); m1.data_type = "RGBA"
    L.new(above.outputs[0], m1.inputs[0]); L.new(stones.outputs[0], m1.inputs[6]); L.new(grass.outputs[0], m1.inputs[7])
    m2 = N.new("ShaderNodeMix"); m2.data_type = "RGBA"
    L.new(up.outputs[0], m2.inputs[0]); L.new(soil.outputs[0], m2.inputs[6]); L.new(m1.outputs[2], m2.inputs[7])
    L.new(m2.outputs[2], bsdf.inputs["Base Color"])
    return m


def water_material():
    m = bpy.data.materials.get("FK_water")
    if m:
        return m
    m = bpy.data.materials.new("FK_water"); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (0.035, 0.085, 0.07, 1)   # стоячая тёмная вода рва
    b.inputs["Roughness"].default_value = 0.12
    return m


def atlas_material(name, img_path, rect, tile=2.0, tint=None):
    """Планарная проекция по нормали + повтор куска атласа пака (rect = (u0,v0,u1,v1))."""
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; N = nt.nodes; L = nt.links
    bsdf = next(n for n in N if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = 0.9
    geo = N.new("ShaderNodeNewGeometry")
    sp = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], sp.inputs[0])
    sn = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Normal"], sn.inputs[0])

    def absstep(sock):
        a = N.new("ShaderNodeMath"); a.operation = "ABSOLUTE"; L.new(sock, a.inputs[0])
        g = N.new("ShaderNodeMath"); g.operation = "GREATER_THAN"; g.inputs[1].default_value = 0.5
        L.new(a.outputs[0], g.inputs[0]); return g.outputs[0]
    sy, sz = absstep(sn.outputs["Y"]), absstep(sn.outputs["Z"])
    mx = N.new("ShaderNodeMath"); mx.operation = "MAXIMUM"; L.new(sy, mx.inputs[0]); L.new(sz, mx.inputs[1])
    u = N.new("ShaderNodeMix"); u.data_type = "FLOAT"
    L.new(mx.outputs[0], u.inputs[0]); L.new(sp.outputs["Y"], u.inputs[2]); L.new(sp.outputs["X"], u.inputs[3])
    v = N.new("ShaderNodeMix"); v.data_type = "FLOAT"
    L.new(sz, v.inputs[0]); L.new(sp.outputs["Z"], v.inputs[2]); L.new(sp.outputs["Y"], v.inputs[3])
    cmb = N.new("ShaderNodeCombineXYZ"); L.new(u.outputs[0], cmb.inputs[0]); L.new(v.outputs[0], cmb.inputs[1])
    sc = N.new("ShaderNodeVectorMath"); sc.operation = "SCALE"; sc.inputs[3].default_value = 1.0 / tile
    L.new(cmb.outputs[0], sc.inputs[0])
    fr = N.new("ShaderNodeVectorMath"); fr.operation = "FRACTION"; L.new(sc.outputs[0], fr.inputs[0])
    mul = N.new("ShaderNodeVectorMath"); mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = (rect[2] - rect[0], rect[3] - rect[1], 0); L.new(fr.outputs[0], mul.inputs[0])
    add = N.new("ShaderNodeVectorMath"); add.operation = "ADD"
    add.inputs[1].default_value = (rect[0], rect[1], 0); L.new(mul.outputs[0], add.inputs[0])
    t = N.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(img_path, check_existing=True)
    L.new(add.outputs[0], t.inputs["Vector"])
    col = t.outputs["Color"]
    if tint:
        mix = N.new("ShaderNodeMix"); mix.data_type = "RGBA"; mix.blend_type = "MULTIPLY"
        mix.inputs[0].default_value = 1.0; mix.inputs[7].default_value = (*tint, 1)
        L.new(col, mix.inputs[6]); col = mix.outputs[2]
    L.new(col, bsdf.inputs["Base Color"])
    return m


def wood_mat():   # вертикальные доски из атласа WoodenWall.png
    return atlas_material("FK_bo_wood", fk.FE_TEX + "/WoodenWall.png", (0.005, 0.02, 0.30, 0.24), tile=2.2)


def stone_mat():  # кладка из атласа Wall1.png
    return atlas_material("FK_bo_stone", fk.FE_TEX + "/Wall1.png", (0.005, 0.465, 0.27, 0.67), tile=2.6,
                          tint=(0.8, 0.82, 0.88))


# ------------------------------------------------------------------ свет (ключ сценариста)
def light(name, loc, color, power, kind="POINT", radius=0.4, c="LIGHTS"):
    L = bpy.data.lights.get(name) or bpy.data.lights.new(name, kind)
    L.color = color; L.energy = power
    try:
        L.shadow_soft_size = radius
    except Exception:
        pass
    o = bpy.data.objects.get(name) or bpy.data.objects.new(name, L)
    if o.name not in coll(c).objects:
        coll(c).objects.link(o)
    o.location = loc
    return o


def build_lights():
    fk.clear_coll("LIGHTS")
    sc = bpy.context.scene
    w = sc.world or bpy.data.worlds.new("World"); sc.world = w; w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.52, 0.66, 0.86, 1); bg.inputs[1].default_value = 0.85
    for o in [o for o in bpy.data.objects if o.type == "LIGHT" and o.name in ("SUN", "Light")]:
        bpy.data.objects.remove(o)
    sun = light("SUN", (0, -40, 60), (1.0, 0.94, 0.84), 3.2, "SUN")
    sun.rotation_euler = Vector((0.35, 0.75, -0.62)).to_track_quat("-Z", "Y").to_euler()
    sun.data.angle = math.radians(3)
    cold, warm, fire = (0.55, 0.68, 1.0), (1.0, 0.72, 0.42), (1.0, 0.5, 0.18)
    # B1 — холодно; огонь только у стражи в коридоре
    light("L_prison_cold_a", (-12.5, 0.5, -1.4), cold, 120)
    light("L_prison_cold_b", (-3.6, 0.5, -1.4), cold, 120)
    light("L_corridor_torch", (-15.9, 1.4, -3.0), fire, 90, radius=0.15)
    light("L_guard_torch", (-8.1, 1.0, -2.6), fire, 70, radius=0.15)
    light("L_infirmary_warm", (-22.0, 1.0, -1.6), warm, 260)   # тёплый потолок: над ним очаг
    light("L_tunnel", (4.0, 0.5, -3.2), cold, 40)
    # L0 — очаг кухни, склад в полумраке, зал с дневным столбом из двери
    light("L_hearth", (CHIM_X, 0.3, 1.0), fire, 900, radius=0.6)
    light("L_store_dim", (-30.0, 1.0, 3.0), warm, 120)
    light("L_hall", (KEEP_X, 0.5, 3.2), warm, 220)
    # башня — дневной свет со всех сторон
    light("L_son", (KEEP_X - 2, 0.5, L1 + 2.4), warm, 180)
    light("L_watch_day", (KEEP_X, 0.0, L2 + 3.5), (0.9, 0.95, 1.0), 420, radius=2.0)
    light("L_signal", (KEEP_X, 0.0, L3 + 2.0), (0.9, 0.95, 1.0), 200, radius=2.0)
    ee = sc.eevee
    for attr, val in (("use_shadows", True), ("use_raytracing", True), ("use_gtao", True)):
        try:
            setattr(ee, attr, val)
        except Exception:
            pass
    try:
        sc.view_settings.view_transform = "AgX"; sc.view_settings.look = "AgX - Punchy"
    except Exception:
        try:
            sc.view_settings.look = "None"
        except Exception:
            pass


# ------------------------------------------------------------------ 1. terrain
def build_terrain():
    fk.clear_coll("LD_BLOCKOUT")
    g = box("GROUND", -300, 300, -300, 300, -14, 0, ground_material())
    # ров — кольцо R 40..48, дно -4.5
    moat = cyl("CUT_moat_out", 47.5, -4.5, 2)
    moat_in = cyl("CUT_moat_in", 39.5, -6, 3)
    cutter(moat, moat_in, solver="EXACT"); moat_in.hide_viewport = True
    cutter(g, moat, "cut_moat")
    # подвал B1: пол -5, потолок -0.5
    rooms = [
        ("CUT_infirmary", -28.0, -16.8, CUT_Y - 1, 4.4, -5, -0.5),  # ЛЕЧЕНИЕ (под кухней)
        ("CUT_corridor", -17.0, -14.8, CUT_Y - 1, 1.8, -5, -2.2),   # коридор лечение-тюрьма
        ("CUT_prison", -15.0, -0.9, CUT_Y - 1, 4.4, -5, -0.5),      # ТЮРЬМА (под донжоном)
        ("CUT_tunnel", -1.4, 7.2, CUT_Y - 1, 1.8, -5, -2.4),        # туннель: торец тюрьмы (трещина) -> арка 1
        ("CUT_hatch_hall", -7.9, -6.1, 0.8, 3.4, -5, 0.5),          # лаз тюрьма -> зал (засов сверху)
        ("CUT_stair_kitchen", -25.2, -20.8, 0.8, 4.4, -5, 0.5),     # лестница лечение -> кухня
        ("CUT_quarry", 7.0, 19.0, CUT_Y - 1, 7.0, -5, 1.0),         # КАТОРГА: каменоломня
        ("CUT_quarry_ledge", 19.0, 21.2, CUT_Y - 1, 7.0, -2.6, 1.0),# уступ у правого края
        ("CUT_arch2", 18.8, 21.6, 0.0, 3.6, -5, -3.0),              # арка 2 (завал = конец демо)
    ]
    for n, x0, x1, y0, y1, z0, z1 in rooms:
        cutter(g, box(n, x0, x1, y0, y1, z0, z1), "cut_" + n[4:])
    sec = box("CUT_SECTION", -320, 320, -330, CUT_Y, -30, 80)
    ms = cutter(g, sec, "cut_SECTION")
    ms.show_viewport = False; ms.show_render = False
    sec.hide_viewport = True
    # вода во рву
    w_out = cyl("WATER", 47.7, -3.1, -2.6, mat=water_material())
    w_in = cyl("CUT_water_in", 39.3, -4, -1)
    cutter(w_out, w_in); w_in.hide_viewport = True
    msw = cutter(w_out, sec, "cut_SECTION"); msw.show_viewport = False; msw.show_render = False
    for o in bpy.data.collections["LD_BLOCKOUT"].objects:
        if o.name.startswith("CUT_"):
            o.hide_render = True
    return g


def section(on=True):
    """Включает/выключает разрез по y=CUT_Y (грунт + вода + камера)."""
    for o in bpy.data.collections["LD_BLOCKOUT"].all_objects:
        for m in o.modifiers:
            if m.name == "cut_SECTION":
                m.show_viewport = on; m.show_render = on
    # отсечённые камерой передние половины оболочек не должны затенять интерьер
    for key in ("fe_woodenwall_3", "fe_woodenhouse_3", "fe_woodenwall_1", "fe_woodenwall_entrace"):
        c = bpy.data.collections.get("A_" + key)
        if c:
            for o in c.all_objects:
                try:
                    o.visible_shadow = not on
                except Exception:
                    pass
    for o in bpy.data.collections["LIGHTS"].objects:
        if o.type == "LIGHT" and o.data.type != "SUN":
            base = o.get("base_energy", o.data.energy)
            o["base_energy"] = base
            o.data.energy = base * (2.2 if on else 1.0)


# ------------------------------------------------------------------ 2. ring wall (частокол)
def build_ring(R=36.5):
    fk.clear_coll("ART_RING")
    seg = 7.59
    step = 2 * math.asin((seg / 2) / R)
    gates = [0.0, math.pi]                      # ворота на пересечении с полосой игры (x=±R)
    gate_half = math.asin(10.6 / R)
    towers = [math.radians(a) for a in (55, 90, 125, 235, 270, 305)]
    a = 0.0
    angs = []
    # обходим круг, пропуская сектора ворот
    for start, end in [(gate_half, math.pi - gate_half), (math.pi + gate_half, 2 * math.pi - gate_half)]:
        n = max(1, round((end - start) / step))
        st = (end - start) / n
        for i in range(n):
            angs.append(start + st * (i + 0.5))
    for i, a in enumerate(angs):
        x, y = R * math.cos(a), R * math.sin(a)
        if any(abs(a - t) < step * 0.55 for t in towers):
            fk.place("fe_woodenwall_3", (x, y, 0), rz=math.degrees(a) + 90, coll="ART_RING", name="ring_tower_%02d" % i)
        else:
            fk.place("fe_woodenwall_1", (x, y, 0), rz=math.degrees(a) + 90, coll="ART_RING", name="ring_wall_%02d" % i)
    for g, nm in zip(gates, ["GATE_main_E", "GATE_supply_W"]):
        gx, gy = R * math.cos(g), R * math.sin(g)
        fk.place("fe_woodenwall_entrace", (gx, gy, 0), rz=math.degrees(g) + 90, coll="ART_RING", name=nm)
        # створки закрыты: выходы из локации закрыты в демо
        tx, ty = -math.sin(g), math.cos(g)
        for side, key in ((-1, "fe_woodenwall_gatel"), (1, "fe_woodenwall_gater")):
            fk.place(key, (gx + tx * side * 0.74, gy + ty * side * 0.74, 0), rz=math.degrees(g) + 90,
                     coll="ART_RING", name=nm + "_leaf_" + key[-1])


def run_all():
    build_terrain()
    build_ring()


# ------------------------------------------------------------------ 3. buildings + interiors
KEEP_X, KEEP_S = -10.5, 1.5          # донжон: башня WoodenWall_3 ×1.5
L1, L2, L3 = 5.25, 9.0, 15.75        # этажи донжона: сон / наблюдение / сигнал
ANNEX_X = -24.85                     # пристройка WoodenHouse_3: склад + кухня
CHIM_X = -18.2                       # очаг и труба


def iron_mat():
    return fk.flat_material("iron", (0.07, 0.065, 0.06), rough=0.6)


def wood_asset_mat(key):
    """Перекрасить ассет без текстуры в дерево пака FE (стиль один)."""
    c = fk.load(key)
    for o in c.all_objects:
        if o.type == "MESH":
            o.data.materials.clear(); o.data.materials.append(wood_mat())


def iron_asset_mat(key):
    c = fk.load(key)
    for o in c.all_objects:
        if o.type == "MESH":
            o.data.materials.clear(); o.data.materials.append(iron_mat())


def build_buildings():
    for c in ("ART_BUILD", "ART_PROPS", "LD_FLOORS"):
        fk.clear_coll(c)
    P = lambda *a, **k: fk.place(*a, coll="ART_BUILD", **k)
    # донжон
    P("fe_woodenwall_3", (KEEP_X, 0, 0), s=KEEP_S, name="KEEP_tower")
    # пристройка: склад + кухня (пол дома на 1.0 -> ставим на -1.0)
    P("fe_woodenhouse_3", (ANNEX_X, 3.6, -1.0), name="ANNEX_house")
    # очаг + труба (камин пака, вытянут по высоте: котёл на гарнизон и дымоход выше всех крыш)
    P("fe_woodenhouse_fireplace", (CHIM_X, 1.4, 0), s=(1.25, 1.25, 1.95), name="CHIMNEY_hearth")

    W, S = wood_mat(), stone_mat()
    F = "LD_FLOORS"
    # перекрытия донжона (проёмы под лестницы чередуются)
    box("FLOOR_L1_son", KEEP_X - 0.7, KEEP_X + 4.3, CUT_Y, 4.4, L1 - 0.35, L1, W, F)        # проём слева: лестница из зала
    box("FLOOR_L2_watch", KEEP_X - 4.3, KEEP_X + 0.7, CUT_Y, 4.4, L2 - 0.35, L2, W, F)       # проём справа: лестница из казармы
    box("FLOOR_L3_signal", KEEP_X - 2.4, KEEP_X + 3.6, CUT_Y, 3.6, L3 - 0.35, L3, W, F)      # проём слева: стремянка
    # перегородка склад | кухня (притолока над дверным проёмом)
    box("WALL_store_kitchen", -25.8, -25.4, CUT_Y, 6.5, 2.5, 5.6, W, F)
    # стены камер (каменные простенки) и задник подвала
    for i, x in enumerate((-15.0, -10.3, -5.9, -1.6)):
        box("WALL_cell_%d" % i, x - 0.25, x + 0.25, 0.4, 4.4, -5, -0.5, S, F)
    box("WALL_prison_back", -15.0, -0.9, 4.2, 4.6, -5, -0.5, S, F)
    box("WALL_prison_end_cracked", -1.35, -0.85, CUT_Y, 4.4, -2.4, -0.5, S, F)   # над туннелем: треснувший торец
    # задники интерьеров (чтобы комнаты читались комнатами, а не изнанкой фасада)
    box("WALL_annex_back", -33.0, -16.7, 4.4, 4.8, 0, 4.6, W, F)
    box("WALL_keep_hall_back", KEEP_X - 3.6, KEEP_X + 3.6, 3.9, 4.3, 0, L1 - 0.35, W, F)
    box("WALL_keep_son_back", KEEP_X - 3.4, KEEP_X + 3.4, 3.9, 4.3, L1, L2 - 0.35, W, F)
    box("WALL_infirm_back", -28.0, -16.8, 4.2, 4.6, -5, -0.5, S, F)


def ladder(name, x, y, z, h, c="ART_BUILD"):
    """Стремянка, собранная из жердей Pandazole (2 тетивы + перекладины)."""
    for dx in (-0.32, 0.32):
        fk.place("pz_woodstick_01", (x + dx, y, z), ry=-90, s=(h / 0.83, 1.6, 1.6), coll=c, name="%s_rail" % name)
    n = int(h / 0.38)
    for i in range(1, n + 1):
        fk.place("pz_woodstick_01", (x, y, z + i * 0.38), s=(0.85, 1.3, 1.3), coll=c, name="%s_rung%02d" % (name, i))


def stairs(name, x, y, z, rise, run, toward=+1, c="ART_BUILD"):
    """Лестница с площадкой (WoodenWall_Stairs). x — центр; toward=+1 подъём к +X, -1 — к -X."""
    fk.place("fe_woodenwall_stairs", (x, y, z), rz=0 if toward > 0 else 180,
             s=(run / 7.796, 1.0, rise / 3.342), coll=c, name=name)


def build_ladders_and_bars():
    P = lambda *a, **k: fk.place(*a, coll="ART_BUILD", **k)
    # лаз тюрьма -> зал и стремянка наблюдение -> сигнал (на схеме — лестница с перекладинами)
    ladder("LADDER_prison_hall", -7.0, 2.4, -5.0, 5.3)
    ladder("LADDER_watch_signal", KEEP_X - 3.4, 2.2, L2, L3 - L2 + 0.3)
    # лестницы (волнистые линии на схеме)
    stairs("STAIRS_infirm_kitchen", -24.0, 2.7, -5.0, 5.0, 5.85, +1)
    stairs("STAIRS_hall_son", KEEP_X - 0.7, 2.7, 0.0, L1, 5.6, +1)
    stairs("STAIRS_son_watch", KEEP_X + 0.7, 2.7, L1, L2 - L1, 5.6, -1)
    stairs("STAIRS_yard_wall", 29.2, 3.0, 0.0, 3.5, 7.8, +1)
    stairs("STAIRS_quarry", 10.0, 5.0, -5.0, 5.0, 5.85, -1)
    # решётки камер (герса CastlePack, тёмное железо) и решётка люка в полу зала
    for nm, x0, x1 in [("BARS_cell1_player", -14.75, -10.55), ("BARS_cell2", -5.65, -1.85)]:
        P("cp_harrow", ((x0 + x1) / 2, 0.4, -5.0), s=((x1 - x0) / 0.706, 6.0, 2.7 / 0.468), name=nm)
    P("cp_harrow", (-7.0, 2.1, 0.02), rx=90, s=(2.4 / 0.706, 6.0, 2.4 / 0.468), name="GRATE_hall_hatch")
