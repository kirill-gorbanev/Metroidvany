"""Камеры и рендеры локации. Камеры остаются в сцене: CAM_SECTION, CAM_EXTERIOR, CAM_TOP."""
import bpy, math, sys
from mathutils import Vector
sys.path.insert(0, r"D:/Metroidvany/blender")
import build_fort as bf

OUT = r"D:/Metroidvany/renders/"


def cam(name, loc, look_at, ortho=None, lens=32, clip_start=0.1, res=(3000, 1080)):
    c = bpy.data.objects.get(name)
    if not c:
        c = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        bpy.context.scene.collection.objects.link(c)
    c.location = loc
    d = (Vector(look_at) - Vector(loc)).normalized()
    c.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    if ortho:
        c.data.type = "ORTHO"; c.data.ortho_scale = ortho
    else:
        c.data.type = "PERSP"; c.data.lens = lens
    c.data.clip_start = clip_start; c.data.clip_end = 1000
    c["res"] = res
    return c


def make_cameras():
    # разрез: орто сбоку, плоскость отсечения = срез по y = CUT_Y (как схема автора)
    cam("CAM_SECTION", (-0.5, -150, 9.5), (-0.5, 0, 9.5), ortho=104, clip_start=150 + bf.CUT_Y - 0.06, res=(3000, 1080))
    cam("CAM_EXTERIOR", (-62, -88, 58), (-4, 4, 0), lens=30, res=(2400, 1350))
    c = cam("CAM_TOP", (0, 0, 160), (0, 0, 0), ortho=112, res=(2000, 2000))
    c.rotation_euler = (0, 0, 0)


def _vis(name, on):
    c = bpy.data.collections.get(name)
    if c:
        c.hide_render = not on


def render(cam_name, file, section=False, markup=True, ring_route=False, pct=100, plan=False):
    sc = bpy.context.scene
    bf.section(section)
    _vis("LD_MARKUP", markup); _vis("LD_RING_ROUTE", ring_route); _vis("LD_PLAN", plan)
    for n in ("CATALOG", "CATALOG2", "TEST"):
        _vis(n, False)
    c = bpy.data.objects[cam_name]
    sc.camera = c
    sc.render.resolution_x, sc.render.resolution_y = c["res"]
    sc.render.resolution_percentage = pct
    sc.render.filepath = OUT + file
    bpy.ops.render.render(write_still=True)
    return OUT + file


def render_all(pct=100):
    make_cameras()
    out = [render("CAM_SECTION", "01_section_markup.png", section=True, markup=True, pct=pct),
           render("CAM_SECTION", "02_section_clean.png", section=True, markup=False, pct=pct),
           render("CAM_EXTERIOR", "03_exterior.png", section=False, markup=False, ring_route=False, pct=pct),
           render("CAM_TOP", "04_plan_top.png", section=False, markup=False, ring_route=True, pct=pct, plan=True)]
    bf.section(True)
    _vis("LD_MARKUP", True); _vis("LD_RING_ROUTE", True); _vis("LD_PLAN", True)
    bpy.context.scene.camera = bpy.data.objects["CAM_SECTION"]
    return out
