"""Камеры и рендеры v2 (3D). Камеры остаются в сцене (CAM3_*)."""
import bpy, math
from mathutils import Vector

OUT = r"D:/Metroidvany/renders/v2_"
MARK = ("S3_MARK3D",)


def cam(name, loc, look, lens=24, ortho=None, clip_start=0.1, res=(2400, 1350)):
    c = bpy.data.objects.get(name) or bpy.data.objects.new(name, bpy.data.cameras.new(name))
    if c.name not in bpy.context.scene.collection.objects:
        bpy.context.scene.collection.objects.link(c)
    c.location = loc
    c.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    c.data.type = "ORTHO" if ortho else "PERSP"
    if ortho:
        c.data.ortho_scale = ortho
    c.data.lens = lens; c.data.clip_start = clip_start; c.data.clip_end = 1200
    c["res"] = res
    return c


def plan_cam(name, cx, cy, width, cut_z, res=(2000, 1500)):
    c = cam(name, (cx, cy, 300), (cx, cy, 0), ortho=width, clip_start=300 - cut_z, res=res)
    c.rotation_euler = (0, 0, 0)
    return c


def make_cameras():
    cam("CAM3_EXT_SE", (78, -96, 62), (-6, 2, 4), lens=30)
    cam("CAM3_EXT_NW", (-92, 70, 48), (0, -2, 4), lens=30)
    cam("CAM3_YARD", (14, -14, 3.2), (-24, 14, 9), lens=20)
    cam("CAM3_HALL", (-21.0, 9.0, 2.4), (-32, 21, 1.6), lens=16)
    cam("CAM3_KITCHEN", (-4.5, 14.2, 2.2), (-16, 22, 1.2), lens=16)
    cam("CAM3_PRISON", (-18.6, 18.5, -4.0), (-30, 9.5, -5.6), lens=16)
    cam("CAM3_BARRACKS", (-19.5, 21.5, 8.3), (-32, 9, 6.6), lens=16)
    plan_cam("CAM3_PLAN_B1", -12, 4, 58, -0.45)
    plan_cam("CAM3_PLAN_L0", 0, -2, 104, 4.6, res=(2200, 1700))
    plan_cam("CAM3_PLAN_WALL", 0, -2, 112, 11.8, res=(2200, 1800))
    plan_cam("CAM3_PLAN_KEEP", -21, 15, 30, 10.6, res=(1600, 1600))


def _vis(names, on):
    for n in names:
        c = bpy.data.collections.get(n)
        if c:
            c.hide_render = not on


def render(cam_name, file, markup=False, pct=60, top_sun=False):
    sc = bpy.context.scene
    _vis(MARK, markup)
    c = bpy.data.objects[cam_name]; sc.camera = c
    sc.render.resolution_x, sc.render.resolution_y = c["res"]; sc.render.resolution_percentage = pct
    sun = bpy.data.objects.get("SUN3D"); old = sun.rotation_euler.copy() if sun else None
    if top_sun and sun:
        sun.rotation_euler = (0.2, 0.12, 0)
    sc.render.filepath = OUT + file
    bpy.ops.render.render(write_still=True)
    if top_sun and sun:
        sun.rotation_euler = old
    return OUT + file


def render_all(pct=60):
    make_cameras()
    out = [render("CAM3_EXT_SE", "01_exterior_se.png", pct=pct),
           render("CAM3_EXT_NW", "02_exterior_nw.png", pct=pct),
           render("CAM3_YARD", "03_yard_eye.png", pct=pct),
           render("CAM3_PLAN_L0", "04_plan_ground_routes.png", markup=True, pct=pct, top_sun=True),
           render("CAM3_PLAN_B1", "05_plan_cellar_routes.png", markup=True, pct=pct, top_sun=True),
           render("CAM3_PLAN_WALL", "06_plan_wallwalk.png", markup=True, pct=pct, top_sun=True),
           render("CAM3_HALL", "07_hall.png", pct=pct),
           render("CAM3_KITCHEN", "08_kitchen.png", pct=pct),
           render("CAM3_PRISON", "09_prison.png", pct=pct),
           render("CAM3_BARRACKS", "10_barracks.png", pct=pct)]
    _vis(MARK, True)
    return out
