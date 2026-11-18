"""Разметка v2 (3D): петли A–E строятся из маршрутов проверки (что нарисовано, то и проверено),
подписи комнат и станций лежат плашмя — читаются на поэтажных планах и на общем виде."""
import bpy, math, sys
sys.path.insert(0, r"D:/Metroidvany/blender")
import fortkit as fk
import build_fort3d as b3
import fort_check3d as fc
import fort_markup as fm      # emit_mat

COL = {"A": (1.0, 0.68, 0.12), "B": (0.95, 0.22, 0.22), "C": (0.35, 0.85, 0.3), "D": (0.78, 0.45, 1.0),
       "E": (0.2, 0.55, 1.0), "room": (1.0, 0.97, 0.9), "station": (1.0, 0.86, 0.35), "exit": (0.95, 0.3, 0.3)}
NL = chr(10)


def _curve(name, pts, rgb, width=0.22, coll="S3_MARK3D"):
    cu = bpy.data.curves.new(name, "CURVE"); cu.dimensions = "3D"; cu.bevel_depth = width; cu.bevel_resolution = 2
    sp = cu.splines.new("POLY"); sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts):
        sp.points[i].co = (p[0], p[1], p[2], 1)
    o = bpy.data.objects.new(name, cu)
    o.data.materials.append(fm.emit_mat("r3_" + name[:8], rgb, 3.0))
    b3.C(coll).objects.link(o)
    return o


def label(text, x, y, z, size=1.2, kind="room", coll="S3_MARK3D", rz=0.0):
    cu = bpy.data.curves.new("T3_" + text[:14], "FONT")
    cu.body = text; cu.size = size; cu.align_x = "CENTER"; cu.align_y = "CENTER"
    o = bpy.data.objects.new("T3_" + text[:14], cu)
    o.location = (x, y, z); o.rotation_euler = (0, 0, math.radians(rz))
    o.data.materials.append(fm.emit_mat("t3_" + kind, COL[kind], 2.5))
    b3.C(coll).objects.link(o)
    sh = bpy.data.objects.new(o.name + "_sh", cu.copy())
    sh.data.materials.clear(); sh.data.materials.append(fm.emit_mat("shadow", (0, 0, 0), 1.0))
    sh.location = (x + size * 0.07, y - size * 0.07, z - 0.04); sh.rotation_euler = o.rotation_euler
    b3.C(coll).objects.link(sh)


def build_markup():
    fk.clear_coll("S3_MARK3D")
    for rname, segs in fc.ROUTES.items():
        key = rname[0]
        pts = []
        for kind, ps in segs:
            for p in ps:
                pts.append((p[0], p[1], p[2] + 0.35))
        _curve("ROUTE3_" + key, pts, COL[key])
    R = [  # (текст, x, y, z)
        ("ТЮРЬМА", -27, 18, -5.0), ("камеры", -27, 9.0, -5.0), ("ЛАЗАРЕТ", -10, 20, -5.0), ("ЦИСТЕРНА", -10, 7.4, -5.0),
        ("ЗАЛ", -26, 18, 1.2), ("КУХНЯ", -9, 15.5, 1.2), ("СКЛАД", 5, 17, 1.2), ("ДВОР", 5, -4, 0.6),
        ("КАТОРГА", -26, -15, -4.6), ("колодец", -10, -0.5, 0.6), ("ворота (мост поднят)", 0, -36, 0.6),
        ("КАЗАРМА", -26, 15, 7.2), ("ШТАБ", -26, 18, 13.2), ("ДОЗОРНАЯ", -26, 16, 19.2), ("СИГНАЛ", -26, 18, 25.4),
        ("галерея", -37, -4, 7.2), ("башня лучника", -37, -27, 15.6), ("леса · журавль", 33, -14, 10.2),
        ("срезанная башня", 37, 27, 10.0), ("пристань", 4, 40, 0.6), ("РОВ", -51, 0, -2.2), ("РОВ", 51, 0, -2.2),
    ]
    for t, x, y, z in R:
        label(t, x, y, z, 1.6 if t.isupper() else 1.0)
    S = [("★ старт", -33, 8.0, -4.6), ("люк: засов сверху", -24, 16.0, 1.0), ("трещина: кирка", -20, 6.2, -4.6),
         ("лаз в ров", -14, 27.5, -3.6), ("паёк ×3", -14.6, 23.0, 2.4), ("кирка", 9.4, 19.6, 1.6),
         ("засов со стороны склада", -3, 21.5, 1.4), ("отдых №2", -29.6, 8.6, 7.4), ("стол-карта", -27, 12.6, 13.4),
         ("рог", -30, 9.0, 19.6), ("жаровня", -28, 10.0, 26.4), ("скобы на трубу", -16.6, 14.6, 25.6),
         ("колосник: в очаг", -15.7, 14.0, 1.4)]
    for t, x, y, z in S:
        label(t, x, y, z, 0.7, "station")
    for t, x, y, z in [("✕ мост к городу", 0, -40, 0.6), ("✕ завал: граница демо", -33, -18, -4.6)]:
        label(t, x, y, z, 0.8, "exit")
