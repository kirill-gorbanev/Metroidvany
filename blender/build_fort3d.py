"""Крепость-тюрьма v2 — ПОЛНЫЙ 3D, камень, вид от третьего лица.

Ассеты: Quaternius Medieval Village MegaKit (кладка UnevenBrick, полы, лестницы, крыши),
Quaternius Fantasy Props MegaKit (реквизит), + FantasyEnvironments/Pandazole/RK/POLYART.
Сетка кита: стена 2 x 3 м (толщина 0.4), пол 2 x 2, лестница-марш 3 м подъёма.

План (+X восток, +Y север), внутри стен 70 x 50 м:
  СЗ — донжон 18x18 (зал 0 / казарма 6 / штаб 12 / дозорная 18 / крыша 24 с жаровней)
  С  — крыло у северной стены: кухня (очаг + дымоход по стене донжона) и склад (калитка на пристань)
  З  — галерея внутри западной стены (z=6) от казармы до ЮЗ башни лучника
  Ю  — надвратная башня, мост поднят
  В  — ремонтируемый участок стены: леса и журавль у ЮВ башни; СВ башня срезана до уровня стены
  двор — колодец над цистерной, каменоломня у ЮЗ башни, поленницы
  подвал (-6) — тюрьма под залом, лазарет под кухней, цистерна под двором, туннель камера №2 -> каменоломня
  ров — ровный, облицованный, внутри магического круга
"""
import bpy, bmesh, math, sys, importlib, random
from mathutils import Vector
sys.path.insert(0, r"D:/Metroidvany/blender")
import fortkit as fk
import build_fort as bf

H = 3.0          # высота яруса кладки
G = 2.0          # шаг сетки
WALL = "qv_wall_unevenbrick_straight"
DOOR = "qv_wall_unevenbrick_door_round"
WIN = "qv_wall_unevenbrick_window_wide_round"
SLIT = "qv_wall_unevenbrick_window_thin_round"
F_STONE, F_BRICK, F_WOOD = "qv_floor_unevenbrick", "qv_floor_brick", "qv_floor_wooddark"
STAIR, STAIR_EXT = "qv_stair_interior_solid", "qv_stairs_exterior_straight"

# ключевые координаты
CUR_O, CUR_I = (39.0, 29.0), (35.0, 25.0)        # полуразмеры наружной и внутренней линии стены (x, y)
KEEP = (-35.0, -17.0, 7.0, 25.0)                 # донжон: x0, x1, y0, y1
KL = [0.0, 6.0, 12.0, 18.0, 24.0]                # уровни донжона
WING = (-17.0, 11.0, 13.0, 25.0)                 # крыло: кухня (x<-3) и склад
TOWERS = {"SW": (-42, -32, -32, -22), "SE": (32, 42, -32, -22), "NE": (32, 42, 22, 32)}
GATE = (-8.0, 8.0, -33.0, -21.0)
B1 = -6.0
B1W = -6.13                                      # низ кладки подвала (верх уходит под пол)
CHIM = (-15.7, 16.2)                             # очаг и дымоход в кухне у восточной стены донжона
WELL = (-10.0, 2.0)
QUARRY = (-33.0, -19.0, -23.0, -7.0)


def C(name):
    return fk._coll(name, fk._coll("FORT3D"))


def P(key, loc, rz=0.0, s=1.0, coll="S3_STONE", name=None, rx=0.0, ry=0.0):
    return fk.place(key, loc, rz=rz, s=s, coll=coll, name=name, rx=rx, ry=ry)


def _inside(x, y, rects, tol=0.05):
    return any(r[0] + tol < x < r[1] - tol and r[2] + tol < y < r[3] - tol for r in rects)


def wall_line(name, x0, y0, x1, y1, z0, courses, openings=None, skip=(), coll="S3_STONE", key=WALL,
              course_from=0):
    """Ряд кладки вдоль линии (длина кратна 2 м). openings[(слот, ярус)] = ключ | None (проём) | 'arch'."""
    openings = openings or {}
    L = math.hypot(x1 - x0, y1 - y0); n = max(1, round(L / G))
    dx, dy = (x1 - x0) / L, (y1 - y0) / L
    rz = math.degrees(math.atan2(dy, dx))
    for i in range(n):
        cx, cy = x0 + dx * (G * i + 1), y0 + dy * (G * i + 1)
        if _inside(cx, cy, skip):
            continue
        for c in range(course_from, courses):
            k = openings.get((i, c), key)
            if k is None:
                continue
            if k == "arch":
                P("qv_wall_arch", (cx, cy, z0 + c * H), rz=rz, coll=coll, name="%s_arch_%d_%d" % (name, i, c))
                continue
            P(k, (cx, cy, z0 + c * H), rz=rz, coll=coll, name="%s_%d_%d" % (name, i, c))


def parapet(name, x0, y0, x1, y1, z, outer=True, skip=(), gaps=(), coll="S3_STONE"):
    """Зубцы: через слот высокий зубец (1.9 м) и низкий парапет (1.1 м). Внутренний край — сплошной низкий."""
    L = math.hypot(x1 - x0, y1 - y0); n = round(L / G)
    dx, dy = (x1 - x0) / L, (y1 - y0) / L
    rz = math.degrees(math.atan2(dy, dx))
    for i in range(n):
        cx, cy = x0 + dx * (G * i + 1), y0 + dy * (G * i + 1)
        if _inside(cx, cy, skip) or i in gaps:
            continue
        sz = (0.63 if (outer and i % 2 == 0) else 0.36)
        P(WALL, (cx, cy, z), rz=rz, s=(1.0, 1.0, sz), coll=coll, name="%s_p%d" % (name, i))


def floor_rect(name, x0, y0, x1, y1, z, key=F_STONE, skip=(), coll="S3_FLOORS"):
    nx, ny = round((x1 - x0) / G), round((y1 - y0) / G)
    for i in range(nx):
        for j in range(ny):
            cx, cy = x0 + G * i + 1, y0 + G * j + 1
            if _inside(cx, cy, skip):
                continue
            P(key, (cx, cy, z), coll=coll, name="%s_%d_%d" % (name, i, j))


def stair(name, x, y_low, z, toward=+1, coll="S3_STONE", key=STAIR):
    """Марш внутренней лестницы (подъём 3 м, длина 4.71). toward=+1 — подъём к +Y, -1 — к -Y."""
    yc = y_low + toward * 4.71 / 2
    P(key, (x, yc, z), rz=0 if toward > 0 else 180, coll=coll, name=name)


def switchback(name, x_a, x_b, y0, z, levels=2, coll="S3_STONE", land_key=F_STONE):
    """Двухмаршевая лестница на 6 м: марш A вверх к +Y от y0, площадка, марш B обратно к -Y."""
    stair(name + "_A", x_a, y0, z, +1, coll)
    stair(name + "_B", x_b, y0 + 4.71, z + 3.0, -1, coll)
    lx0 = min(x_a, x_b) - 1.0
    floor_rect(name + "_land", lx0, y0 + 4.31, lx0 + 4.0, y0 + 6.31, z + 3.0, land_key, coll=coll)   # заходит под ступень: без шва


def ext_stairs(name, x0, y0, z0, n, rz, coll="S3_STONE"):
    """Наружная каменная лестница из модулей (2 м шаг, +1.2 м подъёма). rz=-90 — подъём к +X."""
    d = Vector((math.cos(math.radians(rz + 90)), math.sin(math.radians(rz + 90)), 0))
    for k in range(n):
        c = Vector((x0, y0, 0)) + d * (2.078 * k + 1.04)
        P(STAIR_EXT, (c.x, c.y, z0 + 1.2 * k), rz=rz, coll=coll, name="%s_%d" % (name, k))


def log_post(name, x, y, z0, h, coll="S3_YARD", s=3.2):
    return P("pz_woodlog_01", (x, y, z0), ry=-90, s=(h / 0.511, s, s), coll=coll, name=name)


def stick_ladder(name, x, y, z, h, rz=0.0, coll="S3_INT"):
    """Стремянка из жердей Pandazole."""
    ox, oy = math.cos(math.radians(rz)) * 0.34, math.sin(math.radians(rz)) * 0.34
    for sgn in (-1, 1):
        P("pz_woodstick_01", (x + ox * sgn, y + oy * sgn, z), ry=-90, s=(h / 0.83, 1.8, 1.8), coll=coll,
          name=name + "_rail")
    for i in range(1, int(h / 0.38) + 1):
        P("pz_woodstick_01", (x, y, z + i * 0.38), rz=rz, s=(0.9, 1.4, 1.4), coll=coll, name="%s_r%02d" % (name, i))


# ------------------------------------------------------------------ рельеф, ров, подвал
def rrect_prism(name, x0, x1, y0, y1, r, z0, z1, seg=10, coll="S3_GROUND"):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    pts = []
    for cx, cy, a0 in ((x1 - r, y1 - r, 0), (x0 + r, y1 - r, 90), (x0 + r, y0 + r, 180), (x1 - r, y0 + r, 270)):
        for k in range(seg + 1):
            a = math.radians(a0 + 90 * k / seg)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    vb = [bm.verts.new((x, y, z0)) for x, y in pts]
    vt = [bm.verts.new((x, y, z1)) for x, y in pts]
    bm.faces.new(vb[::-1]); bm.faces.new(vt)
    n = len(pts)
    for i in range(n):
        bm.faces.new((vb[i], vb[(i + 1) % n], vt[(i + 1) % n], vt[i]))
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); C(coll).objects.link(o)
    return o


MOAT_IN = (-45.0, 45.0, -39.0, 35.0, 10.0)
MOAT_W = 12.0


def build_ground():
    for c in ("S3_GROUND",):
        fk.clear_coll(c)
    g = bf.box("GROUND3D", -170, 170, -170, 170, -18, 0, bf.ground_material(), "S3_GROUND")
    x0, x1, y0, y1, r = MOAT_IN
    outer = rrect_prism("CUT3_moat_out", x0 - MOAT_W, x1 + MOAT_W, y0 - MOAT_W, y1 + MOAT_W, r + MOAT_W, -4.6, 2)
    inner = rrect_prism("CUT3_moat_in", x0, x1, y0, y1, r, -7, 3)
    bf.cutter(outer, inner); inner.hide_viewport = True
    bf.cutter(g, outer, "cut_moat")
    k = KEEP; w = WING
    cuts = [
        ("prison", k[0], k[1], k[2], k[3], B1, 0.0),
        ("infirmary", w[0], -3.0, w[2], w[3], B1, 0.0),
        ("cistern", -15.0, -5.0, -3.0, 9.0, B1, -0.6),               # над цистерной 0.6 м грунта
        ("cist_pass", -11.0, -9.0, 8.8, 13.2, B1, -3.0),
        ("well", WELL[0] - 1, WELL[0] + 1, WELL[1] - 1, WELL[1] + 1, B1, 0.6),
        ("kstairs", -13.0, -9.0, 17.0, 25.0, B1, 0.6),              # лестница лазарет -> кухня
        ("hall_hatch", -25.0, -23.0, 15.0, 17.0, B1, 0.6),          # люк зал -> тюрьма
        ("tunnel", -21.0, -19.0, -7.2, 7.2, B1, -4.2),              # камера №2 -> арка 1
        ("quarry", QUARRY[0], QUARRY[1], QUARRY[2], QUARRY[3], B1, 1.0),
        ("arch2", -34.8, -32.8, -16.0, -14.0, B1, -3.4),             # арка 2: завал (граница демо)
        ("drain", -14.7, -13.3, 24.6, 36.5, -4.4, -3.0),            # сток лазарета в ров
        ("vent", -33.6, -32.4, 5.2, 6.9, -3.0, 0.6),                # продух камеры №1 во двор
        ("postern", 3.0, 5.0, 25.0, 29.0, -0.2, 0.6),
    ]
    for n, a, b, c_, d, z0, z1 in cuts:
        bf.cutter(g, bf.box("CUT3_" + n, a, b, c_, d, z0, z1, None, "S3_GROUND"), "cut_" + n)
    w_out = rrect_prism("WATER3D", x0 - MOAT_W - 0.2, x1 + MOAT_W + 0.2, y0 - MOAT_W - 0.2, y1 + MOAT_W + 0.2,
                        r + MOAT_W, -3.3, -2.8)
    w_in = rrect_prism("CUT3_water_in", x0 + 0.2, x1 - 0.2, y0 + 0.2, y1 - 0.2, r, -5, -1)
    bf.cutter(w_out, w_in); w_in.hide_viewport = True
    w_out.data.materials.append(bf.water_material())
    cist = bf.box("WATER3D_cistern", -14.8, -5.2, -2.8, 5.0, -5.4, -5.1, bf.water_material(), "S3_GROUND")
    for o in C("S3_GROUND").objects:
        if o.name.startswith("CUT3_"):
            o.hide_render = True; o.hide_viewport = True; o.display_type = "WIRE"
    return g


# ------------------------------------------------------------------ стена, башни, ворота
def skip_rects():
    return list(TOWERS.values()) + [GATE]


def build_curtain():
    fk.clear_coll("S3_STONE"); fk.clear_coll("S3_FLOORS")
    ox, oy = CUR_O; ix, iy = CUR_I; sk = skip_rects()
    # наружная линия (3 яруса = 9 м), бойницы через 6 слотов на 2-м ярусе
    slits = {(i, 1): SLIT for i in range(3, 40, 6)}
    wall_line("CUR_N_out", ox, oy, -ox, oy, 0, 3, {**slits, (17, 0): DOOR}, sk)          # калитка склада x=4
    wall_line("CUR_S_out", -ox, -oy, ox, -oy, 0, 3, slits, sk)
    wall_line("CUR_W_out", -ox, oy, -ox, -oy, 0, 3, {**slits, **{(i, 2): SLIT for i in range(12, 28, 3)}}, sk)
    wall_line("CUR_E_out", ox, -oy, ox, oy, 0, 3, slits, sk)
    # внутренняя линия
    wall_line("CUR_N_in", -ix, iy, ix, iy, 0, 3, {(19, 0): DOOR}, sk)
    wall_line("CUR_S_in", ix, -iy, -ix, -iy, 0, 3, {}, sk)
    gal_open = {(i, 2): SLIT for i in range(1, 16, 3)}                 # галерея: бойницы во двор
    gal_open[(20, 2)] = DOOR                                            # дверь казармы в галерею (y=16)
    wall_line("CUR_W_in", -ix, -iy, -ix, iy, 0, 3, gal_open, sk)
    wall_line("CUR_E_in", ix, iy, ix, -iy, 0, 3, {}, sk)
    # боевой ход (z=9) и парапеты; восточный ремонтируемый участок без зубцов
    repair = (31.0, 41.0, -20.0, -6.0)
    for nm, a in (("N", (-ox, iy, ox, oy)), ("S", (-ox, -oy, ox, -iy)), ("W", (-ox, -iy, -ix, iy)), ("E", (ix, -iy, ox, iy))):
        floor_rect("WALK_" + nm, *a, 9.0, F_STONE, sk)
    parapet("PAR_N_out", -ox, oy, ox, oy, 9.0, True, sk)
    parapet("PAR_S_out", ox, -oy, -ox, -oy, 9.0, True, sk)
    parapet("PAR_W_out", -ox, -oy, -ox, oy, 9.0, True, sk)
    parapet("PAR_E_out", ox, oy, ox, -oy, 9.0, True, sk + [repair])
    parapet("PAR_N_in", -ix, iy, ix, iy, 9.0, False, sk + [KEEP_RECT()])
    parapet("PAR_S_in", ix, -iy, -ix, -iy, 9.0, False, sk, gaps=(4, 5))            # проход с наружной лестницы
    parapet("PAR_W_in", -ix, -iy, -ix, iy, 9.0, False, sk + [KEEP_RECT()])
    parapet("PAR_E_in", ix, iy, ix, -iy, 9.0, False, sk + [repair])
    # галерея внутри западной стены: пол z=6 от ЮЗ башни до донжона
    floor_rect("GALLERY_W", -ox, -22.0, -ix, iy, 6.0, F_STONE)
    # проход калитки склада
    floor_rect("POSTERN", 3.0, 25.0, 5.0, 29.0, 0.02, F_STONE)


def KEEP_RECT():
    k = KEEP
    return (k[0] - 0.5, k[1] + 0.5, k[2] - 0.5, k[3] + 0.5)


def tower(name, rect, courses, floors, openings=None, roof=None, coll="S3_STONE", fskip=None):
    x0, x1, y0, y1 = rect
    o = openings or {}
    wall_line(name + "_S", x0, y0, x1, y0, 0, courses, o.get("S"))
    wall_line(name + "_E", x1, y0, x1, y1, 0, courses, o.get("E"))
    wall_line(name + "_N", x1, y1, x0, y1, 0, courses, o.get("N"))
    wall_line(name + "_W", x0, y1, x0, y0, 0, courses, o.get("W"))
    fskip = fskip or {}
    top = courses * H
    for z in floors + ([top] if roof in ("merlons", "spire") else []):
        floor_rect(name + "_F%d" % int(z), x0, y0, x1, y1, z, F_STONE, fskip.get(z, ()))
    if roof == "merlons":
        for side, a in (("S", (x0, y0, x1, y0)), ("E", (x1, y0, x1, y1)), ("N", (x1, y1, x0, y1)), ("W", (x0, y1, x0, y0))):
            parapet(name + "_par" + side, *a, top, True)
    elif roof == "spire":
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        P("qv_roof_tower_roundtiles", (cx, cy, top - 0.1), s=(x1 - x0 + 1.2) / 5.65, coll=coll, name=name + "_spire")


def build_towers():
    # ЮЗ башня лучника: галерея входит на z=6 (с севера), боевой ход на z=9 (с севера и с востока)
    sw = TOWERS["SW"]
    tower("TW_SW", sw, 5, [6.0, 9.0], {
        "N": {(2, 2): DOOR, (2, 3): "arch"},
        "E": {(2, 3): "arch"},
        "W": {(2, 1): SLIT, (2, 4): SLIT}, "S": {(2, 1): SLIT, (2, 4): SLIT}}, roof="merlons",
        fskip={9.0: [(-36.0, -34.0, -31.9, -24.1)], 15.0: [(-42.0, -36.0, -29.9, -22.0)]})
    stair("TW_SW_st69", -35.0, -30.5, 6.0, +1)
    switchback("TW_SW_st915", -40.0, -38.0, -30.0, 9.0)
    # ЮВ башня: узел лесов и журавля, шатёр; ход проходит насквозь на z=9
    tower("TW_SE", TOWERS["SE"], 5, [9.0], {"N": {(2, 3): "arch"}, "W": {(2, 3): "arch"},
                                             "E": {(2, 2): SLIT}, "S": {(2, 2): SLIT}}, roof="spire")
    # СВ башня срезана до уровня стены (камень ушёл на латки): 3 яруса, сверху щебень
    tower("TW_NE", TOWERS["NE"], 3, [9.0], {}, roof=None)
    for i, (x, y) in enumerate([(40.6, 30.4), (39.8, 31.2), (41.2, 29.2), (40.9, 23.4)]):
        P("pa_rock1_%d" % (i % 7 + 1), (x, y, 9.0), rz=i * 60, s=1.6 + 0.3 * i, coll="S3_STONE", name="NE_rubble%d" % i)
    # надвратная башня: проход 4 м (x -2..2), мост поднят
    g = GATE
    arch = {(3, 0): "arch", (4, 0): "arch"}
    tower("GATEHOUSE", g, 5, [6.0, 9.0], {"N": arch, "S": arch,
                                         "W": {(2, 3): "arch", (3, 3): "arch"}, "E": {(2, 3): "arch", (3, 3): "arch"}},
          roof="merlons", fskip={6.0: [(-7.0, -3.0, -30.0, -21.0)], 9.0: [(-7.0, -5.0, -29.0, -24.0)]})
    wall_line("GATE_pass_W", -2.0, g[2], -2.0, g[3], 0, 2)
    wall_line("GATE_pass_E", 2.0, g[3], 2.0, g[2], 0, 2)
    switchback("GATE_st", -6.0, -4.0, -30.0, 0.0)                     # из караулки на этаж ворот
    stair("GATE_st69", -6.0, -29.0, 6.0, +1)
    # поднятый мост: настил из досок стоит у ворот
    for i in range(2):
        for j in range(4):
            P(F_WOOD, (-1 + 2 * i, g[2] - 0.6, 1 + 2 * j), rx=90, coll="S3_STONE", name="DRAWBRIDGE_%d%d" % (i, j))
    for sx in (-2.3, 2.3):
        P("qp_chain_coil", (sx, g[2] - 0.7, 7.6), rx=90, s=1.5, coll="S3_STONE", name="DRAWBRIDGE_chain")


# ------------------------------------------------------------------ донжон и крыло
def build_keep():
    x0, x1, y0, y1 = KEEP
    win = {}
    # юг: ворота зала (2 арки), окна этажей
    S = {(4, 0): "arch", (5, 0): "arch", (1, 3): WIN, (7, 3): WIN, (2, 4): WIN, (6, 4): WIN,
         (1, 6): WIN, (4, 6): WIN, (7, 6): WIN}
    E = {(3, 0): DOOR, (1, 3): WIN, (2, 5): WIN, (1, 6): WIN, (4, 6): WIN}      # дверь в кухню y=14 (мимо лестницы)
    wall_line("KEEP_S", x0, y0, x1, y0, 0, 8, S)
    wall_line("KEEP_E", x1, y0, x1, y1, 0, 8, E)
    # север и запад до 9 м — это стена крепости; донжон надстраивает ярусы 3..7
    wall_line("KEEP_N", x1, y1, x0, y1, 0, 8, {(2, 6): WIN, (6, 6): WIN}, course_from=3)
    wall_line("KEEP_W", x0, y1, x0, y0, 0, 8, {(4, 6): WIN, (2, 4): WIN}, course_from=3)
    # этажи: проём лестницы x -21..-17, y 19..25 на каждом верхнем этаже; люк в тюрьму в зале
    for z in KL:
        sk = [(-21.0, -17.0, 19.0, 25.0)] if z > 0 else [(-25.0, -23.0, 15.0, 17.0)]
        floor_rect("KEEP_F%d" % int(z), x0, y0, x1, y1, z, F_STONE if z in (0.0, 24.0) else F_WOOD, sk)
    for i, z in enumerate(KL[:-1]):
        switchback("KEEP_st%d" % i, -20.0, -18.1, 18.3, z, coll="S3_STONE",
                   land_key=F_WOOD if z > 0 else F_STONE)
    # крыша: зубцы + башенка над лестницей
    for side, a in (("S", (x0, y0, x1, y0)), ("E", (x1, y0, x1, y1)), ("N", (x1, y1, x0, y1)), ("W", (x0, y1, x0, y0))):
        parapet("KEEP_par" + side, *a, 24.96, True, skip=[(-21.5, -16.5, 14.5, 25.5)])   # проём к трубе y 14.5..17
    # будка над выходом лестницы на крышу (y 17..25), двускатная черепица
    for side, a, o in (("S", (-21, 17, -17, 17), {(1, 0): DOOR}), ("W", (-21, 25, -21, 17), {}),
                       ("E", (-17, 17, -17, 25), {}), ("N", (-17, 25, -21, 25), {})):
        wall_line("KEEP_turret" + side, *a, 24.96, 1, o)
    P("qv_roof_roundtiles_6x8", (-19.0, 21.0, 27.9), s=(0.62, 0.92, 0.6), coll="S3_STONE", name="KEEP_turret_roof")


def build_wing():
    x0, x1, y0, y1 = WING
    S = {(5, 0): DOOR, (2, 0): WIN, (10, 0): WIN, (12, 0): WIN}
    wall_line("WING_S", x0, y0, x1, y0, 0, 2, S)
    wall_line("WING_E", x1, y0, x1, y1, 0, 2, {(2, 0): WIN})
    wall_line("WING_part", -3.0, y0, -3.0, y1, 0, 2, {(3, 0): DOOR})            # кухня | склад, засов со стороны склада
    floor_rect("KITCHEN_F", x0, y0, -3.0, y1, 0.02, F_BRICK, [(-13.0, -9.0, 19.0, 25.0)])
    floor_rect("STORE_F", -3.0, y0, x1, y1, 0.02, F_WOOD)
    for i, cx in enumerate((-10.0, 4.0)):
        P("qv_roof_roundtiles_8x14", (cx, 19.0, 6.15), rz=90, s=(1.27, 0.9, 0.85), coll="S3_STONE", name="WING_roof%d" % i)
    # очаг (камин FantasyEnvironments) + дымоход по стене донжона выше крыши (вторая вершина силуэта)
    P("fe_woodenhouse_fireplace", (CHIM[0], CHIM[1], 0.0), rz=90, s=(1.25, 1.25, 1.0), coll="S3_STONE", name="HEARTH")
    for k in range(7):
        P("qv_prop_chimney2", (CHIM[0] + 0.15, CHIM[1], 8.0 + 3.0 * k), s=(1.7, 1.6, 1.0), coll="S3_STONE",
          name="CHIMNEY_%d" % k)
    # скобы трубочиста снаружи: с крыши донжона (через проём в зубцах) на верх трубы (z=29)
    stick_ladder("CHIMNEY_rungs", CHIM[0] - 0.95, CHIM[1], 24.96, 4.1, rz=90, coll="S3_STONE")


# ------------------------------------------------------------------ подвал
def build_b1():
    fk.clear_coll("S3_B1")
    k = KEEP; w = WING
    c = "S3_B1"
    # тюрьма под залом
    wall_line("B1_PR_W", k[0], k[2], k[0], k[3], B1W, 2, coll=c)
    wall_line("B1_PR_N", k[0], k[3], k[1], k[3], B1W, 2, coll=c)
    back = {(1, 0): None, (7, 1): SLIT}                  # слот x=-20: трещина -> туннель; x=-32: продух
    wall_line("B1_PR_S", k[1], k[2], k[0], k[2], B1W, 2, back, coll=c)
    wall_line("B1_PR_E", k[1], k[3], k[1], k[2], B1W, 2, {(2, 0): DOOR}, coll=c)   # дверь в лазарет (y=20)
    floor_rect("B1_PR_F", k[0], k[2], k[1], k[3], B1, F_STONE, coll=c)
    # клетки 4 x 4 вдоль южной стены, решётки по y=11, над решётками кладка
    for x in (-31.0, -27.0, -23.0, -19.0):
        wall_line("B1_cellwall_%d" % int(x), x, k[2], x, 11.0, B1W, 2, coll=c)
    for i, x in enumerate(range(-34, -18, 2)):
        P("qv_prop_metalfence_simple", (x, 11.0, B1), coll=c, name="B1_bars_%d" % i)
    wall_line("B1_cells_lintel", k[0], 11.0, -19.0, 11.0, B1W, 2, course_from=1, coll=c)
    # лазарет под кухней
    wall_line("B1_INF_N", w[0], w[3], -3.0, w[3], B1W, 2, {(1, 0): None}, coll=c)    # слот x=-14: сток в ров
    wall_line("B1_INF_E", -3.0, w[3], -3.0, w[2], B1W, 2, coll=c)
    wall_line("B1_INF_S", -3.0, w[2], w[0], w[2], B1W, 2, {(3, 0): "arch"}, coll=c)  # проход в цистерну (x=-10)
    floor_rect("B1_INF_F", w[0], w[2], -3.0, w[3], B1, F_BRICK, coll=c)
    switchback("B1_kst", -12.0, -10.1, 18.3, B1, coll=c)
    # цистерна под двором
    wall_line("B1_CI_W", -15.0, -3.0, -15.0, 9.0, B1W, 2, coll=c)
    wall_line("B1_CI_S", -5.0, -3.0, -15.0, -3.0, B1W, 2, coll=c)
    wall_line("B1_CI_E", -5.0, 9.0, -5.0, -3.0, B1W, 2, coll=c)
    wall_line("B1_CI_N", -15.0, 9.0, -5.0, 9.0, B1W, 2, {(2, 0): "arch"}, coll=c)
    floor_rect("B1_CI_F", -15.0, -3.0, -5.0, 9.0, B1, F_STONE, coll=c)
    floor_rect("B1_CI_pass", -11.0, 9.0, -9.0, 13.0, B1, F_STONE, coll=c)


# ------------------------------------------------------------------ двор: колодец, каменоломня, леса, журавль
def build_yard():
    fk.clear_coll("S3_YARD")
    c = "S3_YARD"
    wx, wy = WELL
    for i, (dx, dy, rz) in enumerate([(0, 1.2, 0), (0, -1.2, 0), (1.2, 0, 90), (-1.2, 0, 90)]):
        P("qv_prop_exteriorborder_straight1", (wx + dx, wy + dy, 0), rz=rz, s=(1.25, 1.0, 4.0), coll=c, name="WELL_ring%d" % i)
    for sx in (-1.3, 1.3):
        log_post("WELL_post", wx + sx, wy, 0, 2.6, c, s=1.6)
    P("qv_roof_wooden_2x1", (wx, wy, 2.6), s=(1.3, 1.6, 1.0), coll=c, name="WELL_roof")
    P("pz_woodlog_01", (wx, wy, 2.2), s=(5.0, 1.6, 1.6), coll=c, name="WELL_windlass")
    P("qp_bucket_wooden_1", (wx + 1.6, wy + 0.9, 0.0), s=1.6, coll=c, name="WELL_bucket")
    P("qp_rope_1", (wx - 0.6, wy + 0.5, 0.75), s=1.4, coll=c, name="WELL_rope")
    # каменоломня: спуск-лестница, навес на бревенчатых столбах, глыбы, арки
    q = QUARRY
    ext_stairs("QUARRY_stairs", -22.0, -17.4, B1, 5, 0, c)
    for x in (-32.0, -24.0):
        for y in (-21.5, -13.0):
            log_post("CANOPY_post", x, y, B1, 10.2, c)
    P("qv_roof_roundtiles_6x8", (-28.0, -17.2, 4.0), rz=90, s=(1.15, 1.0, 0.8), coll=c, name="CANOPY_roof")
    for i, (k, x, y, sc) in enumerate([("fe_rock1", -30.5, -10.0, 0.35), ("fe_rock2", -26.0, -20.0, 0.32),
                                       ("fe_rock3", -31.0, -18.0, 0.4), ("fe_stone1", -24.5, -9.5, 0.7)]):
        P(k, (x, y, B1), rz=i * 70, s=sc, coll=c, name="CRACKED_boulder%d" % i)
    P("qv_prop_wagon", (-27.0, -13.0, B1), rz=20, coll=c, name="QUARRY_cart")
    P("rk_hanging_rack_wood", (-20.0, -7.4, B1), s=(1.0, 1.0, 1.4), coll=c, name="ARCH1_timber")
    P("fe_wall_props", (-33.4, -15.0, B1), rz=90, s=(1.5, 1.5, 1.8), coll=c, name="ARCH2_grate_END")
    for i in range(3):
        P("pa_rock1_%d" % (i + 4), (-32.6, -15.8 + i * 0.8, B1), rz=i * 40, s=1.5, coll=c, name="ARCH2_rubble%d" % i)
    # поленницы: у стены кухни (топливо) и у лесов (ступень)
    for nm, (x, y) in (("WOODPILE_kitchen", (-8.0, 11.6)), ("WOODPILE_step", (30.0, -6.0))):
        for r, n in enumerate((5, 4, 3)):
            for i in range(n):
                P("pz_woodlog_01", (x + (i - (n - 1) / 2) * 0.66, y, r * 0.56), rz=90, s=4.2, coll=c,
                  name="%s_%d%d" % (nm, r, i))
    # наружная каменная лестница на южную стену у ворот (подъём к +X до z=9)
    ext_stairs("STAIRS_S", 8.4, -23.6, -0.6, 8, -90, c)            # к востоку от ворот, верх у x=25
    # леса на ремонтируемом участке восточной стены (x 31..35, y -20..-8): помосты 3 / 6 / 9, марши между
    for y in (-20.0, -16.0, -12.0, -8.0):
        for x in (31.2, 34.6):
            log_post("SCAF_post", x, y, 0.0, 9.2, c, s=2.4)
    floor_rect("SCAF_deck3", 31.0, -20.0, 35.0, -8.0, 3.0, F_WOOD, [(31.0, 33.0, -20.0, -15.0)], coll=c)
    floor_rect("SCAF_deck6", 31.0, -20.0, 35.0, -8.0, 6.0, F_WOOD, [(31.0, 33.0, -20.0, -15.0), (33.0, 35.0, -18.0, -12.0)], coll=c)
    floor_rect("SCAF_deck9", 31.0, -16.0, 35.0, -8.0, 9.0, F_WOOD, coll=c)
    stair("SCAF_st03", 32.1, -19.6, 0.0, +1, c, "qv_stair_interior_rails")
    stair("SCAF_st36", 33.9, -13.0, 3.0, -1, c, "qv_stair_interior_rails")
    stair("SCAF_st69", 32.1, -19.6, 6.0, +1, c, "qv_stair_interior_rails")
    for i, (x, y) in enumerate([(38.5, -18.0), (38.6, -12.5), (38.4, -9.0)]):
        P("fe_stone%d" % (i % 3 + 1), (x, y, 9.0), rz=i * 50, s=0.6, coll=c, name="REPAIR_stones%d" % i)
    # журавль у ЮВ башни: мачта, наклонная стрела, канат с ведром
    log_post("CRANE_mast", 37.0, -19.0, 9.0, 7.0, c, s=3.0)
    P("qv_roof_log", (33.5, -19.0, 15.2), rz=90, rx=0, ry=-18, s=(1.0, 0.95, 1.0), coll=c, name="CRANE_boom")
    P("pz_woodstick_01", (29.0, -19.0, 4.0), ry=-90, s=(12.0 / 0.83, 0.6, 0.6), coll=c, name="CRANE_rope")
    P("qp_bucket_metal", (29.0, -19.0, 3.6), s=2.0, coll=c, name="CRANE_bucket")
    # пристань у калитки склада (север): настил на сваях до воды, стремянка из воды
    floor_rect("PIER", 2.0, 29.0, 6.0, 37.0, 0.05, F_WOOD, coll=c)
    for x in (2.3, 5.7):
        for y in (33.0, 36.7):
            log_post("PIER_pile", x, y, -4.0, 4.1, c, s=2.2)
    stick_ladder("PIER_ladder", 4.0, 37.2, -3.0, 3.1, rz=0, coll=c)
    # продух камеры №1 у фундамента донжона
    P("fe_wall_props", (-33.0, 6.6, 0.0), s=(0.9, 0.9, 0.7), coll=c, name="VENT_cell1")
