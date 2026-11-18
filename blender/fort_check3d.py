"""Проверка проходимости петель лучами (капсула: рост 1.8, радиус 0.4, авто-шаг 0.5).
Сегменты: walk / stairs — пол под ногами, просвет над головой, ширина;
ladder / trigger / drop / swim / break — по своим правилам (лестница, переход трубы, прыжок, вплавь, выбить)."""
import bpy, math
from mathutils import Vector

HIDE = ("S3_LIGHTS", "S3_CIRCLE", "S3_MARK3D", "S3_PLAN3D")
B1 = -6.0
K = [0.0, 6.0, 12.0, 18.0, 24.0]


def keep_climb(z):
    """Двухмаршевая лестница донжона с уровня z на z+6."""
    return [("stairs", [(-20.0, 18.6, z), (-20.0, 22.8, z + 3.0)]),
            ("walk", [(-20.0, 23.8, z + 3.0), (-18.1, 23.8, z + 3.0)]),
            ("stairs", [(-18.1, 22.8, z + 3.0), (-18.1, 18.6, z + 6.0)]),
            ("walk", [(-18.1, 17.8, z + 6.0), (-20.0, 17.6, z + 6.0), (-20.0, 18.4, z + 6.0)])]


ROUTES = {
    "A_паёк": [
        ("break", [(-33.0, 9.6, B1), (-33.0, 12.4, B1)]),
        ("walk", [(-33.0, 12.4, B1), (-19.8, 12.4, B1), (-19.8, 20.0, B1), (-15.0, 20.0, B1), (-12.6, 17.2, B1),
                  (-12.0, 18.6, B1)]),
        ("stairs", [(-12.0, 18.6, B1), (-12.0, 22.8, -3.0)]),
        ("walk", [(-12.0, 23.8, -3.0), (-10.1, 23.8, -3.0)]),
        ("stairs", [(-10.1, 22.8, -3.0), (-10.1, 18.6, 0.0)]),
        ("walk", [(-10.1, 17.8, 0.0), (-12.0, 14.4, 0.0), (-16.0, 14.0, 0.0), (-18.6, 14.0, 0.0), (-22.6, 14.4, 0.0), (-24.0, 14.6, 0.0)]),
        ("ladder", [(-24.0, 16.0, 0.0), (-24.0, 16.0, B1)]),
        ("walk", [(-24.0, 14.6, B1), (-24.0, 12.4, B1), (-32.0, 12.4, B1)]),
    ],
    "B_тревога": [("walk", [(-27.0, 13.0, 0.0), (-21.5, 13.4, 0.0), (-20.0, 17.6, 0.0), (-20.0, 18.4, 0.0)])]
                 + keep_climb(0.0) + keep_climb(6.0) + keep_climb(12.0) + keep_climb(18.0) + [
        ("walk", [(-18.1, 17.6, 24.0), (-18.0, 16.2, 24.0), (-22.0, 13.0, 24.0), (-18.4, 15.8, 24.0)]),
        ("ladder", [(-16.6, 16.2, 24.96), (-16.6, 16.2, 29.0)]),
        ("trigger", [(-15.5, 16.2, 29.0), (-14.4, 16.2, 0.0)]),
        ("walk", [(-14.0, 15.0, 0.0), (-14.0, 14.0, 0.0), (-16.0, 14.0, 0.0), (-18.6, 14.0, 0.0), (-27.0, 13.0, 0.0)]),
    ],
    "C_кольцо": [
        ("walk", [(-28.0, 13.0, 0.0), (-26.0, 6.0, 0.0), (-16.0, -4.0, 0.0), (12.0, -18.0, 0.0), (9.6, -23.2, 0.0)]),
        ("stairs", [(9.4, -23.6, 0.0), (24.6, -23.6, 9.0)]),
        ("walk", [(25.0, -26.2, 9.0), (25.0, -27.0, 9.0), (31.0, -27.0, 9.0),
                  (37.0, -27.0, 9.0), (37.0, -21.0, 9.0), (37.0, 21.0, 9.0), (37.0, 27.0, 9.0), (30.0, 27.0, 9.0),
                  (6.0, 27.0, 9.0)]),
        ("drop", [(6.0, 27.0, 9.0), (6.0, 39.0, -2.8)]),
        ("swim", [(6.0, 39.0, -2.8), (4.0, 38.0, -2.8)]),
        ("ladder", [(4.0, 37.2, -2.8), (4.0, 37.2, 0.05)]),
        ("walk", [(4.0, 36.0, 0.05), (4.0, 30.0, 0.05), (4.0, 27.0, 0.0), (4.0, 23.6, 0.0), (0.0, 20.0, 0.0),
                  (-3.0, 20.0, 0.0), (-6.0, 17.0, 0.0), (-8.0, 15.0, 0.0), (-16.0, 14.0, 0.0), (-18.6, 14.0, 0.0), (-28.0, 13.0, 0.0)]),
    ],
    "D_кирка": [
        ("walk", [(-28.0, 13.0, 0.0), (-26.0, 6.0, 0.0), (-22.0, -4.0, 0.0), (-22.0, -6.8, 0.0)]),
        ("stairs", [(-22.0, -7.2, 0.0), (-22.0, -17.2, B1)]),
        ("walk", [(-22.0, -17.8, B1), (-19.8, -16.0, B1), (-20.0, -9.0, B1)]),
        ("crawl", [(-20.0, -7.8, B1), (-20.0, 6.6, B1)]),
        ("walk", [(-20.0, 7.6, B1), (-20.0, 9.0, B1)]),
        ("break", [(-20.0, 9.0, B1), (-20.0, 12.4, B1)]),
        ("walk", [(-20.0, 12.4, B1), (-24.0, 14.8, B1)]),
        ("ladder", [(-24.0, 16.0, B1), (-24.0, 16.0, 0.0)]),
        ("walk", [(-23.0, 14.6, 0.0), (-28.0, 13.0, 0.0), (-26.0, 6.0, 0.0)]),
    ],
    "E_вода": [
        ("walk", [(-13.0, 4.0, 0.0), (-11.6, 3.4, 0.0)]),
        ("ladder", [(-10.0, 2.0, 0.0), (-10.0, 2.0, B1)]),
        ("swim", [(-10.0, 2.0, -5.1), (-10.0, 4.6, -5.1)]),
        ("walk", [(-10.0, 6.0, B1), (-10.0, 11.0, B1), (-10.0, 14.5, B1), (-14.5, 17.5, B1), (-14.0, 22.0, B1), (-14.0, 24.0, B1)]),
        ("ladder", [(-14.0, 24.2, B1), (-14.0, 24.2, -4.4)]),
        ("crawl", [(-14.0, 25.0, -4.4), (-14.0, 35.6, -4.4)]),
        ("swim", [(-14.0, 37.0, -2.8), (4.0, 38.6, -2.8)]),
        ("ladder", [(4.0, 37.2, -2.8), (4.0, 37.2, 0.05)]),
        ("walk", [(4.0, 36.0, 0.05), (4.0, 27.0, 0.0), (4.0, 23.6, 0.0), (0.0, 20.0, 0.0), (-3.0, 20.0, 0.0), (-6.0, 17.0, 0.0)]),
    ],
}


def _ray(dg, o, d, dist):
    hit, loc, nor, idx, obj, mat = bpy.context.scene.ray_cast(dg, Vector(o), Vector(d).normalized(), distance=dist)
    return (loc, obj) if hit else (None, None)


def check(step=0.35, verbose=6):
    vis = {}
    for n in HIDE:
        c = bpy.data.collections.get(n)
        if c:
            vis[n] = c.hide_viewport; c.hide_viewport = True
    dg = bpy.context.evaluated_depsgraph_get()
    report = {}
    try:
        for rname, segs in ROUTES.items():
            issues = []; samples = 0
            for kind, pts in segs:
                for a, b in zip(pts[:-1], pts[1:]):
                    a, b = Vector(a), Vector(b)
                    if kind in ("walk", "stairs", "crawl"):
                        L = (b - a).length; n = max(1, int(L / step))
                        dirv = (b - a).normalized() if L > 0 else Vector((1, 0, 0))
                        side = Vector((-dirv.y, dirv.x, 0)).normalized() if dirv.xy.length > 1e-6 else Vector((1, 0, 0))
                        head = 1.25 if kind == "crawl" else 1.75
                        prev = None
                        for i in range(n + 1):
                            p = a.lerp(b, i / n); samples += 1
                            loc, obj = _ray(dg, (p.x, p.y, p.z + 1.2), (0, 0, -1), 2.4)
                            if loc is None:
                                issues.append(("нет пола", kind, tuple(round(v, 1) for v in p))); continue
                            fz = loc.z
                            if abs(fz - p.z) > 0.75:
                                issues.append(("пол не на той высоте %.2f" % fz, kind, tuple(round(v, 1) for v in p), obj.name if obj else ""))
                            if prev is not None and kind == "walk" and fz - prev > 0.55:
                                issues.append(("ступень %.2f" % (fz - prev), kind, tuple(round(v, 1) for v in p), obj.name if obj else ""))
                            prev = fz
                            up, uo = _ray(dg, (p.x, p.y, fz + 0.15), (0, 0, 1), head)
                            if up is not None:
                                issues.append(("низко %.2f" % (up.z - fz), kind, tuple(round(v, 1) for v in p), uo.name if uo else ""))
                            if kind == "walk":
                                hz = fz + 1.0
                                lh, lo = _ray(dg, (p.x, p.y, hz), side, 0.42)
                                rh, ro = _ray(dg, (p.x, p.y, hz), -side, 0.42)
                                fh, fo = _ray(dg, (p.x, p.y, hz), dirv, 0.3) if i < n else (None, None)
                                if fh is not None:
                                    issues.append(("стена/предмет на пути", kind, tuple(round(v, 1) for v in p), fo.name if fo else ""))
                                elif lh is not None and rh is not None:
                                    issues.append(("узко", kind, tuple(round(v, 1) for v in p), (lo.name if lo else "") + "|" + (ro.name if ro else "")))
                    elif kind in ("drop", "trigger", "ladder"):
                        loc, obj = _ray(dg, (b.x, b.y, b.z + 1.0), (0, 0, -1), 2.2)
                        if loc is None and kind != "drop":
                            issues.append(("нет площадки в конце " + kind, kind, tuple(round(v, 1) for v in b)))
                        if kind == "ladder":
                            top = a if a.z > b.z else b
                            up, uo = _ray(dg, (top.x, top.y, top.z + 0.15), (0, 0, 1), 1.75)
                            if up is not None:
                                issues.append(("лестница: низко наверху", kind, tuple(round(v, 1) for v in top), uo.name if uo else ""))
                    elif kind == "swim":
                        loc, obj = _ray(dg, (b.x, b.y, b.z + 1.0), (0, 0, -1), 1.6)
                        if obj is None or "WATER" not in obj.name:
                            issues.append(("нет воды", kind, tuple(round(v, 1) for v in b), obj.name if obj else ""))
            # схлопнуть повторы: по типу проблемы и объекту
            uniq = {}
            for it in issues:
                key = (it[0].split(" ")[0], it[-1] if len(it) > 3 else "")
                uniq.setdefault(key, []).append(it)
            report[rname] = {"samples": samples, "issues": len(issues),
                             "groups": [(k, len(v), v[0][2]) for k, v in sorted(uniq.items(), key=lambda kv: -len(kv[1]))][:verbose * 3]}
    finally:
        for n, h in vis.items():
            bpy.data.collections[n].hide_viewport = h
    return report
