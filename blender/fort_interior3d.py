"""Начинка комнат v2 (по design/gameplay_loops.md, раздел v2.A) + свет + магический круг.
Правило автора: каждый предмет — с глаголом; признаки жилого — состояния вещей; декора ≤ 2 на комнату."""
import bpy, math, random, sys, importlib
sys.path.insert(0, r"D:/Metroidvany/blender")
import fortkit as fk
import build_fort as bf
import build_fort3d as b3

B1 = b3.B1
KZ = b3.KL


def P(key, loc, rz=0.0, s=1.0, name=None, coll="S3_INT", rx=0.0, ry=0.0):
    return fk.place(key, loc, rz=rz, s=s, coll=coll, name=name, rx=rx, ry=ry)


def on(key, base_key, loc, rz=0.0, s=1.0, name=None, base_s=1.0):
    """Поставить предмет на верх другого (стол, полка)."""
    z = loc[2] + fk.size(base_key)[2] * base_s
    return P(key, (loc[0], loc[1], z), rz=rz, s=s, name=name)


def light(name, loc, color, power, radius=0.3):
    return bf.light(name, loc, color, power, radius=radius, c="S3_LIGHTS")


FIRE, WARM, COLD, DAY = (1.0, 0.52, 0.2), (1.0, 0.75, 0.45), (0.55, 0.66, 1.0), (0.9, 0.95, 1.0)


def torch(name, x, y, z, rz):
    P("qp_torch_metal", (x, y, z), rz=rz, name=name)
    light("L_" + name, (x + 0.35 * math.cos(math.radians(rz + 90)), y + 0.35 * math.sin(math.radians(rz + 90)), z + 0.7),
          FIRE, 90, 0.12)


def lantern(name, x, y, z, rz):
    P("qp_lantern_wall", (x, y, z), rz=rz, name=name)
    light("L_" + name, (x + 0.6 * math.cos(math.radians(rz + 90)), y + 0.6 * math.sin(math.radians(rz + 90)), z + 0.4),
          WARM, 120, 0.15)


def brazier(name, x, y, z, s=1.0, power=600):
    P("qp_cauldron", (x, y, z), s=1.5 * s, name=name + "_bowl")
    P("pz_campfire", (x, y, z + 0.55 * s), s=1.1 * s, name=name + "_fire")
    light("L_" + name, (x, y, z + 1.4 * s), FIRE, power, 0.4)


def build_interiors():
    fk.clear_coll("S3_INT")
    # ---------------- ТЮРЬМА (подвал под залом)
    P("rk_floorbed", (-33.4, 8.7, B1), rz=90, s=0.72, name="CELL1_bunk_REST")              # лежак: отдых/сохранение
    P("rk_bowl_food_wood", (-31.9, 10.4, B1), s=1.7, name="CELL1_bowl_FOOD")               # миска: паёк / бросок
    P("qp_bench", (-21.0, 7.9, B1), s=0.7, name="CELL2_bare_bench")                        # нары без подстилки
    for i, (x, y) in enumerate([(-21.6, 7.6), (-22.3, 8.4), (-18.9, 7.7)]):
        P("pa_rock1_%d" % (i + 1), (x, y, B1), rz=i * 70, s=1.2, name="CELL2_crack_rubble%d" % i)
    for i, x in enumerate((-28.9, -24.9)):                                                 # 2 пустые камеры открыты
        P("qp_bucket_wooden_1", (x - 1.2, 8.5, B1), s=1.4, name="CELL_empty_bucket%d" % i)
    torch("TORCH_prison_a", -29.0, 11.45, -3.4, 0)
    torch("TORCH_prison_b", -21.0, 11.45, -3.4, 0)
    b3.stick_ladder("LADDER_prison_hall", -24.0, 16.0, B1, 6.1, rz=0, coll="S3_INT")
    P("qv_prop_metalfence_simple", (-24.0, 16.0, 0.05), rx=90, s=(1.0, 1.0, 0.7), name="HATCH_grate_BOLT_FROM_HALL")
    P("qp_barrel", (-18.6, 23.6, B1), name="PRISON_water_barrel")
    light("L_prison_cold", (-27.0, 18.0, -2.0), COLD, 260, 2.0)
    # ---------------- ЛАЗАРЕТ
    P("qp_table_large", (-7.2, 15.2, B1), name="HEAL_station_table")
    on("qp_cauldron", "qp_table_large", (-7.9, 15.2, B1), s=0.8, name="HEAL_cauldron")
    for i, k in enumerate(("qp_potion_1", "qp_potion_2", "qp_potion_4")):
        on(k, "qp_table_large", (-6.6 + i * 0.4, 15.4, B1), s=1.2, name="HEAL_potion%d" % i)
    P("qp_shelf_small_bottles", (-3.35, 16.5, B1 + 1.2), rz=90, name="HEAL_shelf")
    P("qp_bed_twin1", (-14.8, 15.5, B1), rz=0, name="HEAL_bed_wounded")
    P("fe_wall_props", (-14.0, 24.6, -4.4), rz=180, s=(0.9, 0.9, 0.9), name="DRAIN_grate_to_moat")
    P("qp_bucket_wooden_1", (-10.0, 13.6, B1), s=1.5, name="HEAL_bucket_from_cistern")
    P("qp_candlestick_stand", (-9.0, 14.1, B1), name="HEAL_candle")
    light("L_infirmary", (-8.0, 17.0, -3.0), WARM, 280, 1.0)
    # ---------------- ЦИСТЕРНА
    P("pz_woodstick_01", (-10.0, 2.0, -5.0), ry=-90, s=(5.6 / 0.83, 0.5, 0.5), name="WELL_rope_down")
    P("qp_bucket_wooden_1", (-10.0, 2.0, -5.1), s=1.5, name="WELL_bucket_bottom")
    light("L_cistern", (-10.0, 6.0, -3.0), COLD, 180, 1.5)
    # ---------------- КУХНЯ
    P("qp_cabinet", (-14.6, 24.55, 0.0), rz=180, name="KITCHEN_cabinet")
    on("qp_shelf_arch", "qp_cabinet", (-14.6, 24.6, 0.0), rz=180, name="FOOD_niche_shelf")
    for i, k in enumerate(("fe_food_bread_1", "fe_food_pie", "fe_food_chicken")):
        P(k, (-15.0 + i * 0.4, 24.45, 1.0 + 1.06), name="FOOD_ration%d" % i)                 # 3 пайка в нише
    P("qp_table_large", (-14.6, 21.6, 0.0), name="KITCHEN_table_step")                      # ступень к нише
    P("fe_food_bowler", (-15.0, 16.2, 0.0), s=1.8, name="KITCHEN_pot_at_hearth")
    P("qp_barrel", (-5.0, 14.0, 0.0), name="KITCHEN_water_barrel")
    P("qp_bucket_wooden_1", (-5.9, 14.2, 0.0), s=1.5, name="KITCHEN_bucket")
    P("qp_pot_1", (-14.0, 21.6, 0.82), name="KITCHEN_pot")
    light("L_hearth", (-14.9, 16.2, 1.0), FIRE, 1200, 0.6)
    lantern("LANTERN_kitchen", -9.0, 13.35, 2.4, 0)
    # ---------------- СКЛАД
    P("qp_peg_rack", (10.7, 20.0, 1.6), rz=90, name="STORE_pegs_more_than_picks")
    P("qp_pickaxe_bronze", (10.62, 19.6, 0.75), rz=90, name="PICKAXE_ability")
    for i, (k, x, y) in enumerate([("fe_storage_bag", 1.0, 23.8), ("fe_storage_bag_potato", 1.9, 24.0),
                                   ("qp_barrel", 2.9, 24.1), ("qp_barrel", 3.0, 23.2), ("qv_prop_crate", 6.6, 23.9),
                                   ("qv_prop_crate", 6.6, 23.9)]):
        P(k, (x, y, 1.06 if i == 5 else 0.0), rz=i * 23, name="STORE_stack%d" % i)       # штабель: ступень/укрытие
    light("L_store_dim", (4.0, 19.0, 3.5), WARM, 120, 1.5)
    # ---------------- ЗАЛ (донжон, 0)
    P("qp_table_large", (-31.6, 10.6, 0.0), name="HALL_guard_post_table")      # сбоку от ворот, лицом к ним
    P("qp_stool", (-31.6, 11.7, 0.0), name="HALL_guard_stool")
    torch("TORCH_hall_a", -31.0, 7.45, 2.4, 0)
    torch("TORCH_hall_b", -22.0, 7.45, 2.4, 0)
    P("qp_chandelier", (-27.0, 16.0, 4.3), s=1.4, name="HALL_chandelier")
    light("L_hall_chandelier", (-27.0, 16.0, 4.6), WARM, 320, 1.0)
    P("qp_banner_1", (-27.0, 24.55, 2.2), rz=180, s=1.3, name="HALL_banner_decor")
    P("qp_bench", (-34.2, 16.0, 0.0), rz=90, name="HALL_bench_cover")
    # ---------------- КАЗАРМА (6)
    z = KZ[1]
    for i, (x, y, rz) in enumerate([(-33.6, 9.2, 90), (-33.6, 12.0, 90), (-29.6, 8.5, 0)]):
        P("qp_bed_twin1", (x, y, z), rz=rz, name="SON_bed%d%s" % (i, "_FREE_REST" if i == 2 else "_sleeper"))
    for i, x in enumerate((-24.5, -23.0, -21.5)):
        P("qp_dummy", (x, 7.9, z), rz=180, name="SON_armor_stand%d" % i)
    P("qp_crate_wooden", (-31.5, 22.5, z), name="SON_arrow_box")
    for i in range(4):
        P("pz_arrow_01", (-31.6 + i * 0.08, 22.5, z + 1.05), ry=-80 + i * 5, s=1.4, name="SON_arrow%d" % i)
    P("qp_table_large", (-28.0, 18.0, z), name="SON_table")
    for i in range(2):
        on("rk_bowl_food_wood", "qp_table_large", (-28.6 + i * 1.1, 18.0, z), s=1.6, name="SON_bowl%d_throw" % i)
    lantern("LANTERN_son", -27.0, 7.35, z + 2.4, 0)
    # ---------------- ШТАБ (12)
    z = KZ[2]
    P("qp_table_large", (-27.0, 14.0, z), name="HQ_map_table")
    on("pz_map_01", "qp_table_large", (-27.0, 14.0, z), s=1.8, name="HQ_MAP")
    on("qp_scroll_1", "qp_table_large", (-25.9, 14.2, z), name="HQ_scroll")
    P("qp_pot_1", (-32.5, 22.5, z), name="HQ_pot_throw1")
    P("qp_vase_2", (-31.6, 23.0, z), name="HQ_pot_throw2")
    P("qp_bookcase_2", (-34.5, 14.0, z), rz=-90, name="HQ_bookcase_decor")
    lantern("LANTERN_hq", -27.0, 7.35, z + 2.4, 0)
    # ---------------- ДОЗОРНАЯ (18): окна на все стороны, рог
    z = KZ[3]
    P("qp_stool", (-27.0, 8.6, z), name="WATCH_stool_to_yard")
    P("qp_peg_rack", (-30.0, 7.45, z + 1.7), name="WATCH_HORN_hook")                       # рог: ассета нет — крюк
    P("qp_vase_4", (-34.4, 16.0, z + 0.0), name="WATCH_plant_decor")
    lantern("LANTERN_watch", -24.0, 24.65, z + 2.4, 180)
    # ---------------- КРЫША (24): сигнальная жаровня (главный ориентир), дождевая бочка
    z = KZ[4]
    brazier("SIGNAL_brazier", -28.0, 12.0, z, s=1.6, power=4000)
    P("qp_barrel", (-33.5, 23.2, z), name="ROOF_rain_barrel")
    P("qp_bucket_wooden_1", (-32.7, 23.0, z), s=1.5, name="ROOF_bucket")
    # ---------------- ворота и двор: огни — глаза стражи (пятна света, между ними тень)
    torch("TORCH_gate_w", -1.7, -27.0, 2.2, -90)
    torch("TORCH_gate_e", 1.7, -27.0, 2.2, 90)
    for i, (x, y, rz) in enumerate([(-31.0, 6.6, 180), (-21.0, 6.6, 180), (-12.0, 12.6, 180), (6.0, 12.6, 180),
                                    (-5.0, -20.6, 0), (5.0, -20.6, 0), (33.0, -7.6, 0)]):
        lantern("LANTERN_yard%d" % i, x, y, 3.0, rz)
    brazier("BRAZIER_quarry", -24.5, -4.5, 0.0, 1.0, 700)
    brazier("BRAZIER_stairs", 16.0, -19.0, 0.0, 1.0, 600)
    P("rk_wood_stump", (-6.0, 10.5, 0.0), name="YARD_chopping_block_decor")


def build_world():
    fk.clear_coll("S3_LIGHTS")
    sc = bpy.context.scene
    w = sc.world; w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.32, 0.4, 0.62, 1); bg.inputs[1].default_value = 0.55   # сумерки
    for o in [o for o in bpy.data.objects if o.type == "LIGHT" and o.name in ("SUN",)]:
        bpy.data.objects.remove(o)
    sun = light("SUN3D", (0, 0, 80), (1.0, 0.72, 0.48), 2.6)
    sun.data.type = "SUN"
    from mathutils import Vector
    sun.rotation_euler = Vector((0.7, 0.55, -0.42)).to_track_quat("-Z", "Y").to_euler()
    sun.data.angle = math.radians(4)
    try:
        sc.view_settings.view_transform = "AgX"; sc.view_settings.look = "AgX - Punchy"
    except Exception:
        pass
    for attr, val in (("use_shadows", True), ("use_raytracing", True)):
        try:
            setattr(sc.eevee, attr, val)
        except Exception:
            pass


def build_circle(R=74.0, cx=0.0, cy=-2.0):
    fk.clear_coll("S3_CIRCLE")
    cu = bpy.data.curves.new("MAGIC_CIRCLE3D", "CURVE"); cu.dimensions = "3D"
    cu.bevel_depth = 0.25; cu.bevel_resolution = 3
    sp = cu.splines.new("POLY"); n = 256; sp.points.add(n - 1)
    for i in range(n):
        a = 2 * math.pi * i / n
        sp.points[i].co = (cx + R * math.cos(a), cy + R * math.sin(a), 0.1, 1)
    sp.use_cyclic_u = True
    o = bpy.data.objects.new("MAGIC_CIRCLE3D", cu)
    m = bpy.data.materials.get("MK_circle3d") or bpy.data.materials.new("MK_circle3d")
    m.use_nodes = True; nt = m.node_tree
    for nd in list(nt.nodes):
        if nd.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(nd)
    em = nt.nodes.new("ShaderNodeEmission"); em.inputs[0].default_value = (0.25, 0.9, 1.0, 1); em.inputs[1].default_value = 8
    nt.links.new(em.outputs[0], next(nd for nd in nt.nodes if nd.type == "OUTPUT_MATERIAL").inputs[0])
    o.data.materials.append(m)
    b3.C("S3_CIRCLE").objects.link(o)


def build_nature():
    fk.clear_coll("S3_NATURE")
    rnd = random.Random(11)
    for i in range(90):
        a = rnd.uniform(0, 2 * math.pi); r = rnd.uniform(80, 130)
        k = rnd.choice(["fe_birch_tree1", "fe_birch_tree2", "fe_birch_tree3", "fe_pine_tree1", "fe_pine_tree1", "fe_oak_tree1"])
        fk.place(k, (r * math.cos(a), -2 + r * math.sin(a), 0), rz=rnd.uniform(0, 360), s=rnd.uniform(0.9, 1.3),
                 coll="S3_NATURE", name="tree3_%02d" % i)
    # западная берма — сторона рубежа, заросла до воды
    for i in range(40):
        x = rnd.uniform(-44.5, -40.0); y = rnd.uniform(-36, 32)
        fk.place(rnd.choice(["fe_grass1", "fe_grass2", "fe_bush1", "fe_grass1"]), (x, y, 0), rz=rnd.uniform(0, 360),
                 s=rnd.uniform(1.0, 1.8), coll="S3_NATURE", name="overgrowth_%02d" % i)


def build_life():
    """Доп. начинка (правка автора «пусто внутри»): у каждой вещи есть применение — укрытие, ступень, свет, ресурс."""
    fk.clear_coll("S3_LIFE")
    L = lambda *a, **k: fk.place(*a, coll="S3_LIFE", **k)
    # подвал: пост тюремщика, стойка с мисками (маршрут пайков), бочки и ящики старого погреба — укрытия
    L("qp_table_large", (-30.0, 20.0, B1), name="JAILER_post_table")
    L("qp_stool", (-30.0, 21.1, B1), name="JAILER_stool")
    L("qp_candlestick_stand", (-31.6, 20.0, B1), name="JAILER_candle")
    L("qp_shelf_simple", (-18.6, 21.0, B1 + 1.3), rz=90, name="MEAL_rack_bowls")
    for i in range(3):
        L("rk_bowl_food_wood", (-18.55, 20.6 + i * 0.4, B1 + 1.62), s=1.5, name="MEAL_bowl%d" % i)
    for i, (k, x, y) in enumerate([("qp_barrel", -34.0, 24.0), ("qp_barrel", -33.2, 24.1), ("qp_barrel", -34.1, 23.2),
                                   ("qv_prop_crate", -26.0, 24.2), ("qv_prop_crate", -24.9, 24.2), ("qp_crate_wooden", -22.0, 23.6)]):
        L(k, (x, y, B1), rz=i * 31, name="CELLAR_cover%d" % i)
    L("qp_cage_small", (-28.5, 24.3, B1), name="CELLAR_cage_decor")
    light("L_jailer_candle", (-31.6, 20.0, -4.4), WARM, 70, 0.1)
    # склад: стеллажи вдоль южной стены и штабели — подъём к высокому окну; подвода у калитки
    for i, x in enumerate((-1.5, 0.5, 2.5)):
        L("qp_shelf_arch", (x, 13.35, 0.0), name="STORE_shelf%d" % i)
        L("qp_pot_1", (x - 0.25, 13.35, 0.62), name="STORE_pot%d" % i)
        L("fe_storage_bag", (x + 0.25, 13.35, 1.18), s=0.6, name="STORE_bag%d" % i)
    for i, (x, y, z) in enumerate([(8.6, 14.0, 0.0), (8.6, 14.0, 1.06), (9.7, 14.0, 0.0), (9.7, 15.1, 0.0)]):
        L("qv_prop_crate", (x, y, z), rz=i * 15, name="STORE_climb_crate%d" % i)
    L("qv_prop_wagon", (4.0, 21.0, 0.0), rz=0, name="STORE_supply_wagon")
    # зал: 4 колонны (укрытия, место сбора по тревоге), бочки у ворот
    for i, (x, y) in enumerate([(-30.0, 12.0), (-22.0, 12.0), (-30.0, 20.0), (-22.0, 20.0)]):
        for k in range(2):
            L("qv_corner_exterior_brick", (x, y, 3.0 * k), s=(2.6, 2.6, 1.0), name="HALL_pillar%d_%d" % (i, k))
    for i, (x, y) in enumerate([(-32.6, 8.0), (-33.4, 8.4), (-20.6, 8.0)]):
        L("qp_barrel", (x, y, 0.0), name="HALL_gate_barrel%d" % i)
    # двор: штабели добытого камня — система «камень»: выработка -> тачка -> журавль -> леса
    for i, (x, y) in enumerate([(-17.5, -9.0), (-16.6, -9.6), (-17.0, -10.6), (29.0, -11.0), (29.6, -12.2), (28.6, -13.0)]):
        L("fe_stone%d" % (i % 3 + 1), (x, y, 0.0), rz=i * 47, s=0.55, name="STONE_pile%d" % i)
    L("qv_prop_wagon", (27.0, -9.0, 0.0), rz=80, name="STONE_cart_to_crane")


def build_paths_and_accents():
    """Мощёные тропы = направленность (вытоптанные маршруты жизни крепости) + знамёна и плющ (силуэт, сторона рубежа)."""
    fk.clear_coll("S3_PATHS")
    T = lambda *a, **k: fk.place(*a, coll="S3_PATHS", **k)
    def path(name, pts, key="qv_floor_unevenbrick"):
        n = 0
        for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
            L = math.hypot(x1 - x0, y1 - y0); steps = max(1, int(L / 2.0))
            for i in range(steps + 1):
                t = i / steps
                T(key, (x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, 0.03), rz=math.degrees(math.atan2(y1 - y0, x1 - x0)),
                  name="%s_%03d" % (name, n)); n += 1
    path("PATH_hall_gate", [(-26.0, 5.6), (-26.0, -3.0), (-12.0, -12.0), (0.0, -19.4)])      # зал -> ворота
    path("PATH_well", [(-17.0, -0.6), (-11.6, 2.0)])
    path("PATH_kitchen", [(-12.0, -6.0), (-6.0, 4.0), (-6.0, 12.2)])                         # дрова и вода на кухню
    path("PATH_quarry", [(-26.0, -3.0), (-22.0, -5.6)])                                      # тропа каторжников
    path("PATH_stairs_S", [(0.0, -19.4), (9.0, -21.4)])
    path("PATH_scaffold", [(9.0, -21.4), (22.0, -12.0), (29.0, -10.0)])                      # камень к журавлю
    # знамёна: южная грань донжона, ворота изнутри и снаружи
    for i, (x, y, z, rz) in enumerate([(-30.0, 6.75, 15.5, 180), (-22.0, 6.75, 15.5, 180),
                                       (-4.0, -20.75, 9.6, 180), (4.0, -33.25, 9.6, 0)]):
        T("qp_banner_2_cloth", (x, y, z), rz=rz, s=1.6, name="BANNER_%d" % i)
    # плющ по западной (рубежной) стене и срезанной башне
    rnd = random.Random(5)
    for i in range(14):
        y = rnd.uniform(-26, 26)
        T(rnd.choice(["qv_prop_vine1", "qv_prop_vine2", "qv_prop_vine4", "qv_prop_vine5", "qv_prop_vine6"]),
          (-39.25, y, rnd.uniform(0.0, 4.0)), rz=90, s=rnd.uniform(1.4, 2.2), name="VINE_W%02d" % i)
    for i in range(5):
        T(("qv_prop_vine1", "qv_prop_vine2", "qv_prop_vine4")[i % 3], (42.25, 22.0 + i * 2.0, 1.0), rz=-90, s=1.8, name="VINE_NE%d" % i)


def build_garden():
    """Огород в СВ четверти двора: замкнутая пищевая система (грядки -> склад -> кухня -> миски)
    и укрытие — высокая рожь для присяда. Подтверждено автором (оставить)."""
    fk.clear_coll("S3_GARDEN")
    G = lambda *a, **k: fk.place(*a, coll="S3_GARDEN", **k)
    rnd = random.Random(3)
    for r in range(6):
        y = 4.0 + r * 2.6
        for i in range(12):
            x = 15.0 + i * 1.3 + rnd.uniform(-0.2, 0.2)
            G("fe_rye", (x, y + rnd.uniform(-0.3, 0.3), 0.0), rz=rnd.uniform(0, 360), s=rnd.uniform(1.1, 1.5), name="GARDEN_rye_%d_%02d" % (r, i))
    for i, (k, x, y) in enumerate([("qp_farmcrate_carrot", 31.0, 6.0), ("qp_farmcrate_apple", 31.0, 7.3),
                                   ("qp_farmcrate_empty", 31.9, 6.6), ("qp_bucket_wooden_1", 30.6, 8.4)]):
        G(k, (x, y, 0.0), rz=i * 20, name="GARDEN_harvest%d" % i)
    G("qp_dummy", (22.0, 10.5, 0.0), rz=200, name="GARDEN_scarecrow")
    for i in range(6):
        G("rk_fence_long", (14.0 + i * 4.2, 2.2, 0.0), name="GARDEN_fence%d" % i)
