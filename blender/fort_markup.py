"""Разметка левелдизайна (LD_MARKUP): подписи комнат, петли проходимости, станции,
односторонние проходы, закрытые выходы, магический круг (граница локации).

Цвета: петли A/B/C/D — тёплые и нейтральные; холодный циан закреплён ТОЛЬКО за кругом
(ключ сценариста: «нечеловеческий» цвет круга больше нигде не встречается)."""
import bpy, math, sys, importlib
sys.path.insert(0, r"D:/Metroidvany/blender")
import fortkit as fk
import build_fort as bf

NL = chr(10)
Y_MARK = bf.CUT_Y - 0.03         # чуть перед срезом, но за плоскостью отсечения камеры
GZ = 0.9                          # высота линии маршрута над полом (центр персонажа)
COL = {
    "A": (1.0, 0.68, 0.12),   # петля A «Паёк» — нижняя (тюрьма-лечение-кухня-зал)
    "B": (0.95, 0.22, 0.22),  # петля B «Тревога» — вертикаль башни + труба
    "C": (0.35, 0.85, 0.3),   # петля C «Кольцо» — двор, стена, ров, склад
    "D": (0.78, 0.45, 1.0),   # петля D «Каторга» — кирка, каменоломня, туннель
    "room": (1.0, 0.97, 0.9),
    "shadow": (0.0, 0.0, 0.0),
    "station": (1.0, 0.86, 0.35),
    "oneway": (1.0, 0.5, 0.1),
    "exit": (0.9, 0.25, 0.25),
    "circle": (0.25, 0.9, 1.0),
}


def mcoll(name="LD_MARKUP"):
    return fk._coll(name, fk._coll("FORT"))


def emit_mat(key, rgb, strength=3.0):
    name = "MK_" + key
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (*rgb, 1); em.inputs[1].default_value = strength
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    nt.links.new(em.outputs[0], out.inputs[0])
    return m


def label(text, x, z, size=0.8, kind="room", y=Y_MARK, c="LD_MARKUP", align="CENTER", name=None):
    cu = bpy.data.curves.new(name or ("T_" + text[:20]), "FONT")
    cu.body = text; cu.size = size; cu.align_x = align; cu.align_y = "CENTER"
    cu.extrude = 0.0
    o = bpy.data.objects.new(name or ("T_" + text[:20]), cu)
    o.location = (x, y, z); o.rotation_euler = (math.radians(90), 0, 0)
    o.data.materials.append(emit_mat(kind, COL.get(kind, COL["room"])))
    mcoll(c).objects.link(o)
    # тень под текстом — читаемость на пёстром фоне
    sh = bpy.data.objects.new(o.name + "_shadow", cu.copy())
    sh.data.materials.clear(); sh.data.materials.append(emit_mat("shadow", (0.0, 0.0, 0.0), 1.0))
    sh.location = (x + size * 0.06, y + 0.012, z - size * 0.06); sh.rotation_euler = o.rotation_euler
    mcoll(c).objects.link(sh)
    return o


def route(name, pts, key, width=0.15, y=Y_MARK + 0.005, closed=False, c="LD_MARKUP", z_is_floor=True):
    cu = bpy.data.curves.new(name, "CURVE"); cu.dimensions = "3D"
    cu.bevel_depth = width; cu.bevel_resolution = 2
    sp = cu.splines.new("POLY"); sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts):
        if len(p) == 2:
            x, z = p; yy = y
        else:
            x, yy, z = p
        sp.points[i].co = (x, yy, z + (GZ if z_is_floor else 0), 1)
    sp.use_cyclic_u = closed
    o = bpy.data.objects.new(name, cu)
    o.data.materials.append(emit_mat("route_" + key, COL[key], 2.5))
    mcoll(c).objects.link(o)
    return o


def magic_circle(R=47.8, z=0.08):
    cu = bpy.data.curves.new("MAGIC_CIRCLE", "CURVE"); cu.dimensions = "3D"
    cu.bevel_depth = 0.16; cu.bevel_resolution = 3
    sp = cu.splines.new("POLY"); n = 192; sp.points.add(n - 1)
    for i in range(n):
        a = 2 * math.pi * i / n
        sp.points[i].co = (R * math.cos(a), R * math.sin(a), z, 1)
    sp.use_cyclic_u = True
    o = bpy.data.objects.new("MAGIC_CIRCLE", cu)
    o.data.materials.append(emit_mat("circle", COL["circle"], 6.0))
    fk._coll("FX_CIRCLE", fk._coll("FORT")).objects.link(o)
    L = bpy.data.lights.get("L_circle_glow") or bpy.data.lights.new("L_circle_glow", "POINT")


def build_markup():
    fk.clear_coll("LD_MARKUP"); fk.clear_coll("FX_CIRCLE"); fk.clear_coll("LD_RING_ROUTE")
    L1, L2, L3, KX, CX = bf.L1, bf.L2, bf.L3, bf.KEEP_X, bf.CHIM_X
    magic_circle()

    # ---------------- подписи зон (как на схеме автора)
    for t, x, z in [("ТЮРЬМА", -8.0, -1.2), ("ЛЕЧЕНИЕ", -22.4, -1.2), ("СКЛАД", -29.5, 3.9),
                    ("ЕДА", -21.0, 3.9), ("ЦЕНТРАЛЬНЫЙ ЗАЛ", KX, 4.0), ("СОН", KX + 1.6, L1 + 3.0),
                    ("НАБЛЮДЕНИЕ", KX - 1.2, L2 + 4.6), ("СИГНАЛ", KX + 1.0, L3 + 2.4), ("ТРУБА", CX - 2.2, 12.5),
                    ("ДВОР", 3.5, 6.0), ("РАБОТА-КАТОРГА", 13.0, 6.6), ("СТЕНА", 31.0, 7.6),
                    ("РОВ", -43.5, 1.2), ("РОВ", 43.5, 1.2)]:
        label(t, x, z, 1.25 if len(t) < 12 else 1.0)
    label("туннель", 3.0, -4.2, 0.45, name="T_tunnel")
    label("арка 1", 7.6, -1.6, 0.4, name="T_arch1")
    label("арка 2", 20.2, -1.9, 0.4, name="T_arch2")

    # ---------------- станции (по одной на функцию)
    for t, x, z in [("★ СТАРТ · нары = отдых", -12.7, -2.4), ("миска: паёк дня", -11.2, -3.2),
                    ("✚ станция лечения", -19.6, -2.6), ("раненый на койке", -17.9, -3.4),
                    ("кирка (способность)", -27.0, 2.4), ("паёк ×3", -21.9, 2.2),
                    ("отдых №2 (свободная койка)", -7.4, L1 + 2.1), ("стойка: доспехи ×3 · стрелы", -8.0, L1 + 2.9),
                    ("стол-карта", -12.0, L2 + 2.3), ("рог / сигнал", -9.0, L3 + 1.4),
                    ("поленница: укрытие", 1.2, 2.8), ("поленница: ступень", 24.4, 2.8),
                    ("навес: верхний путь", 19.5, 4.9)]:
        label(t, x, z, 0.52, "station")

    # ---------------- односторонние проходы (засов с одной стороны)
    for t, x, z in [("↑ люк: засов СВЕРХУ (из зала)", -5.2, -0.15), ("засов со стороны склада →", -25.6, 3.05),
                    ("↓ колосник: ломается при прыжке", CX - 0.2, -0.45),
                    ("трещина: кирка", -1.1, -1.35)]:
        label(t, x, z, 0.5, "oneway")

    # ---------------- закрытые выходы = точки будущих коммуникаций
    for t, x, z in [("✕ ворота поставки · мост (закрыт)", -36.5, 8.4), ("✕ главные ворота · мост (закрыт)", 36.5, 8.4),
                    ("✕ завал: граница демо", 20.2, -3.6), ("✕ водосток ко рву", -27.2, -2.6)]:
        label(t, x, z, 0.52, "exit")
    label("МАГИЧЕСКИЙ" + NL + "КРУГ", -47.8, 2.6, 0.8, "circle", name="T_circle_L")
    label("МАГИЧЕСКИЙ" + NL + "КРУГ", 47.8, 2.6, 0.8, "circle", name="T_circle_R")

    # ---------------- петли проходимости (в плоскости разреза)
    B = -5.0
    # A «Паёк»: камера -> коридор -> лечение -> лестница -> кухня -> зал -> люк -> тюрьма
    route("ROUTE_A", [(-12.6, B), (-16.0, B), (-26.6, B), (-23.9, 0.0), (-21.0, 0.0), (-16.6, 0.0),
                      (-7.6, 0.0), (-7.6, B + 0.02), (-12.6, B)], "A", closed=True)
    # B «Тревога»: зал -> сон -> наблюдение -> стремянка -> сигнал -> прыжок на трубу -> вниз в очаг -> зал
    route("ROUTE_B", [(-9.0, 0.15), (-14.0, 0.15), (-11.2, L1), (-7.0, L1), (-9.8, L2), (-13.9, L2),
                      (-13.9, L3), (-8.6, L3), (-15.2, L3), (-16.9, L3 + 1.3), (CX, L3 + 0.4),
                      (CX, 0.25), (-16.0, 0.25), (-9.0, 0.15)], "B", closed=True)
    # C «Кольцо»: зал -> двор -> лестница -> стена ... (по кольцу в глубине) ... ров -> ворота склада -> склад -> кухня
    route("ROUTE_C_east", [(-6.3, 0.3), (25.3, 0.3), (29.2, 3.5), (33.6, 3.5), (36.5, 3.5)], "C")
    route("ROUTE_C_west", [(-44.0, -3.4), (-40.0, -3.4), (-39.0, 0.3), (-33.0, 0.3), (-25.6, 0.3), (-16.6, 0.3)], "C")
    route("ROUTE_C_canopy", [(7.7, 0.3), (15.5, 3.35), (23.3, 3.35)], "C", width=0.05)
    label("↻ C: по кольцу стены" + NL + "(в глубине кадра)", 33.0, 5.6, 0.5, "station", name="T_C_east")
    label("C: из рва → берма" + NL + "→ ворота склада", -40.8, -1.0, 0.5, "station", name="T_C_west")
    # D «Каторга»: двор -> спуск в каменоломню -> арка 1 -> туннель -> трещина -> тюрьма -> лаз -> зал -> двор
    route("ROUTE_D", [(7.2, 0.45), (10.0, 0.45), (12.9, B + 0.1), (7.0, B + 0.1), (-0.9, B + 0.1),
                      (-5.9, B + 0.1), (-6.4, B + 0.1), (-6.4, 0.45), (7.2, 0.45)], "D", closed=True)
    for k, x, z in [("A", -26.0, -3.0), ("B", -15.0, 11.5), ("C", 12.0, 1.7), ("D", 9.8, -2.5)]:
        label(k, x, z, 1.8, k if k in COL else "room", name="T_loop_" + k)
        bpy.data.objects["T_loop_" + k].data.materials[0] = emit_mat("route_" + k, COL[k], 2.5)

    # ---------------- петля C по кольцу стены (видна на общем и верхнем виде)
    R = 33.6; pts = []   # чуть внутри кольца: боевой ход под крышей, линию видно сверху
    for i in range(0, 91):
        a = math.radians(-12 + i * (204.0 / 90))
        pts.append((R * math.cos(a), R * math.sin(a), 3.5))
    route("ROUTE_C_ring", pts, "C", width=0.22, c="LD_RING_ROUTE", z_is_floor=True)


def build_plan_markup():
    """Подписи для вида сверху (коллекция LD_PLAN): линия разреза = плоскость игры, ворота, круг."""
    fk.clear_coll("LD_PLAN")
    z = 22.0

    def flat(text, x, y, size, kind, rot=0.0, name=None):
        cu = bpy.data.curves.new(name or ("P_" + text[:16]), "FONT")
        cu.body = text; cu.size = size; cu.align_x = "CENTER"; cu.align_y = "CENTER"
        o = bpy.data.objects.new(name or ("P_" + text[:16]), cu)
        o.location = (x, y, z); o.rotation_euler = (0, 0, math.radians(rot))
        o.data.materials.append(emit_mat(kind, COL[kind]))
        mcoll("LD_PLAN").objects.link(o)
        sh = bpy.data.objects.new(o.name + "_sh", cu.copy())
        sh.data.materials.clear(); sh.data.materials.append(emit_mat("shadow", (0, 0, 0), 1.0))
        sh.location = (x + size * 0.07, y - size * 0.07, z - 0.05); sh.rotation_euler = o.rotation_euler
        mcoll("LD_PLAN").objects.link(sh)

    route("PLAN_cut_line", [(-50, bf.CUT_Y, z - 0.1), (50, bf.CUT_Y, z - 0.1)], "room", width=0.25,
          c="LD_PLAN", z_is_floor=False)
    flat("линия разреза = плоскость игры (вид сбоку)", 0, bf.CUT_Y - 3.2, 1.9, "room")
    flat("ворота поставки (закрыты)", -36.5, 15.5, 1.6, "exit", rot=90)
    flat("главные ворота (закрыты)", 36.5, 15.5, 1.6, "exit", rot=-90)
    flat("РОВ", 0, 43.5, 2.4, "room")
    flat("МАГИЧЕСКИЙ КРУГ — граница локации", 0, -50.8, 2.0, "circle")
    flat("C: обход по кольцу стены", 0, 30.0, 1.7, "C")
    flat("склад·кухня", -24.8, 9.0, 1.5, "station")
    flat("донжон", -10.5, 7.5, 1.5, "station")
    flat("каторга", 13.0, 8.5, 1.5, "station")
