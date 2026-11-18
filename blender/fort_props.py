"""Реквизит крепости. Количества — по слою геймдизайна (design/gameplay_loops.md):
каждый предмет на плоскости игры — то, чем игрок пользуется; дубли из прототипа вырезаны."""
import bpy, math, random, importlib, sys
sys.path.insert(0, r"D:/Metroidvany/blender")
import fortkit as fk
import build_fort as bf

B1 = -5.0


def top_of(key, s=1.0):
    return fk.size(key)[2] * s


def P(*a, **k):
    return fk.place(*a, coll="ART_PROPS", **k)


def woodpile(name, x, y, rows=(5, 4, 3), s=4.0):
    d = 0.157 * s
    for r, n in enumerate(rows):
        for i in range(n):
            P("pz_woodlog_01", (x + (i - (n - 1) / 2) * d * 1.02, y, r * d * 0.88), rz=90, s=s,
              name="%s_log%d%d" % (name, r, i))


def build_props():
    fk.clear_coll("ART_PROPS")
    bf.wood_asset_mat("rk_hanging_rack_wood")
    L1, L2, L3, CHIM_X = bf.L1, bf.L2, bf.L3, bf.CHIM_X

    # --- ТЮРЬМА: камера №1 (старт игрока) и камера №2
    P("rk_floorbed", (-13.4, 2.6, B1), rz=90, s=0.75, name="CELL1_bunk_SAVE")     # нары: отдых/сохранение
    P("rk_bowl_food_wood", (-11.4, 1.2, B1), s=1.6, name="CELL1_bowl_FOOD")         # миска: паёк нового дня
    P("pz_bucket", (-14.2, 3.6, B1), name="CELL1_bucket")
    P("rk_floorbed", (-4.3, 2.6, B1), rz=90, s=0.75, name="CELL2_bunk")
    for i, (x, y, z, sc) in enumerate([(-0.6, -0.4, B1, 1.4), (-0.2, 0.8, B1, 1.1), (0.4, 0.1, B1, 1.0)]):
        P("pa_rock1_%d" % (i + 1), (x, y, z), rz=i * 70, s=sc, name="CRACK_rubble_%d" % i)
    P("cp_walltorch", (-8.6, 3.95, -3.2), s=5, name="TORCH_guard")
    P("cp_walltorch", (-15.9, 1.75, -3.0), s=5, name="TORCH_corridor")

    # --- ЛЕЧЕНИЕ: 1 станция (стол с эликсирами) + 1 кровать (раненый) + водосток ко рву
    P("fe_furniture_table", (-19.6, 2.3, B1), name="HEAL_station_table")
    th = top_of("fe_furniture_table")
    for i, k in enumerate(["fe_furniture_elixir1", "fe_furniture_elixir2", "fe_furniture_elixir3"]):
        P(k, (-20.2 + i * 0.55, 2.3, B1 + th), name="HEAL_elixir%d" % i)
    P("fe_furniture_bed", (-17.9, 1.6, B1), rz=90, name="HEAL_bed_wounded")
    P("fe_wall_props", (-27.2, 4.05, B1), s=1.4, name="DRAIN_to_moat")

    # --- СКЛАД: подвода, мешки, бочки; 1 кирка
    P("fe_cart1", (-30.6, 2.3, 0), rz=90, name="STORE_cart")
    for i, (k, x, y) in enumerate([("fe_storage_bag", -28.4, 3.4), ("fe_storage_bag_potato", -27.6, 3.7),
                                   ("fe_storage_bag_carrot", -28.0, 2.6), ("fe_storage_barrel", -26.7, 3.5),
                                   ("fe_storage_barrel", -26.6, 2.5), ("fe_storage_basket", -32.2, 0.8)]):
        P(k, (x, y, 0), rz=i * 37, name="STORE_%d" % i)
    P("pz_pickaxe", (-27.0, 1.4, 0.05), rx=90, rz=0, s=1.2, name="PICKAXE_ability")
    P("fe_woodenhouse_door", (-25.6, 2.0, 0), rz=20, name="DOOR_store_kitchen_BOLT")

    # --- КУХНЯ: очаг у трубы, стол с 3 пайками
    P("fe_furniture_table", (-21.9, 0.6, 0), name="KITCHEN_table")
    th = top_of("fe_furniture_table")
    for i, k in enumerate(["fe_food_bread_1", "fe_food_pie", "fe_food_chicken"]):
        P(k, (-22.5 + i * 0.6, 0.6, th), name="FOOD_ration%d" % i)
    P("fe_food_bowler", (CHIM_X, 0.3, 0.0), s=1.8, name="KITCHEN_pot")
    P("fe_storage_barrel_drink", (-16.9, 3.4, 0), name="KITCHEN_barrel")
    P("fe_furniture_cupboard2", (-20.0, 4.0, 0), name="KITCHEN_cupboard")

    # --- ЗАЛ: пост стражи
    P("fe_furniture_table_small", (-11.6, 0.4, 0), name="HALL_guard_table")
    P("fe_furniture_chair1", (-12.6, 0.4, 0), rz=90, name="HALL_guard_chair")
    P("fe_furniture_candlestick1", (-11.6, 0.4, top_of("fe_furniture_table_small")), name="HALL_candle")
    P("fe_tavern_door_room", (-6.4, 1.4, 0), rz=65, name="DOOR_hall_yard")

    # --- СОН: двухъярусная (2 спящих) + свободная койка (сохранение №2), стойка с доспехами, стрелы
    P("fe_furniture_bed", (-10.0, 0.9, L1), name="SON_bunk_low_sleeper")
    P("fe_furniture_bed", (-10.0, 0.9, L1 + 1.45), name="SON_bunk_high_sleeper")
    P("fe_furniture_bed", (-7.6, 0.9, L1), name="SON_bed_free_SAVE")
    P("rk_hanging_rack_wood", (-8.4, 3.6, L1), s=0.75, name="SON_armor_rack")
    for i in range(3):
        P("pz_leather", (-9.0 + i * 0.6, 3.55, L1 + 0.7), rx=90, s=1.3, name="SON_armor%d" % i)
    P("fe_storage_basket", (-6.9, 2.2, L1), name="SON_arrows_box")
    for i in range(4):
        P("pz_arrow_01", (-6.9 + (i - 1.5) * 0.08, 2.2, L1 + 0.35), ry=-80 + i * 6, s=1.3, name="SON_arrow%d" % i)

    # --- НАБЛЮДЕНИЕ: стол-карта, бинокль, табурет к окну во двор
    P("fe_furniture_table", (-12.0, 0.8, L2), name="WATCH_map_table")
    th = top_of("fe_furniture_table")
    P("pz_map_01", (-12.0, 0.8, L2 + th), s=1.6, name="WATCH_map_MAP")
    P("pz_binoculars", (-11.2, 0.9, L2 + th), name="WATCH_binoculars")
    P("fe_furniture_chair2", (-10.4, 1.6, L2), rz=-90, name="WATCH_stool_to_yard")

    # --- СИГНАЛ: сигнальная чаша (рога в паках нет — место отмечено разметкой)
    P("pz_campfire", (-9.0, 0.8, L3), s=1.4, name="SIGNAL_brazier")

    # --- ДВОР: поленницы, навес над каменоломней, каменоломня, арки
    woodpile("WOODPILE_cover", 1.2, 1.2)
    woodpile("WOODPILE_step", 24.4, 1.2)
    P("fe_woodenwall_stairs", (15.5, 2.0, 0.0), s=(2.0, 2.8, 1.0), name="CANOPY_quarry")
    for i, (k, x, y, sc) in enumerate([("fe_rock1", 9.6, 4.5, 0.35), ("fe_rock2", 13.6, 5.5, 0.3),
                                       ("fe_rock3", 18.0, 4.0, 0.35), ("fe_stone1", 12.0, 1.4, 0.6),
                                       ("fe_stone2", 16.8, 1.0, 0.5)]):
        P(k, (x, y, B1), rz=i * 50, s=sc, name="QUARRY_rock%d" % i)
    P("fe_cart2", (14.6, 3.0, B1), rz=90, name="QUARRY_cart")
    P("rk_hanging_rack_wood", (6.7, 0.0, B1), rz=90, s=(1.0, 1.0, 1.5), name="ARCH1_timber")
    P("fe_wall_props", (20.2, 3.4, B1), s=(2.0, 2.0, 2.4), name="ARCH2_grate_END")
    for i, (x, sc) in enumerate([(19.4, 1.6), (20.4, 1.2), (21.0, 1.9)]):
        P("pa_rock1_%d" % (i + 4), (x, 1.6, B1), rz=i * 40, s=sc, name="ARCH2_rubble%d" % i)


def build_nature():
    fk.clear_coll("ART_NATURE")
    rnd = random.Random(7)
    for i in range(70):
        a = rnd.uniform(0, 2 * math.pi); r = rnd.uniform(53, 100)
        k = rnd.choice(["fe_birch_tree1", "fe_birch_tree2", "fe_birch_tree3", "fe_birch_tree1", "fe_pine_tree1", "fe_oak_tree1"])
        fk.place(k, (r * math.cos(a), r * math.sin(a), 0), rz=rnd.uniform(0, 360), s=rnd.uniform(0.8, 1.2),
                 coll="ART_NATURE", name="tree_%02d" % i)
    for i in range(30):
        a = rnd.uniform(0, 2 * math.pi); r = rnd.uniform(49, 75)
        fk.place(rnd.choice(["fe_rock1", "fe_rock2", "fe_bush1", "fe_grass1", "fe_grass2"]),
                 (r * math.cos(a), r * math.sin(a), 0), rz=rnd.uniform(0, 360), coll="ART_NATURE", name="nat_%02d" % i)
