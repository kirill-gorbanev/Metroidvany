"""fortkit — сборка локации «Крепость-тюрьма» из готовых ассетов.

Ассеты НЕ моделируются: всё берётся из паков в Unity-проектах автора.
Каждый ассет импортируется один раз в скрытую коллекцию _LIB, а в сцене
ставится инстансом (collection instance) — легко двигать и экспортировать.

Запуск в Blender:
    import importlib, sys; sys.path.insert(0, r"D:/Metroidvany/blender")
    import fortkit; importlib.reload(fortkit)
    fortkit.place("fe_bed", (0, 0, 0), rz=90, coll="R_Barracks")
"""
import bpy, os, re, math
from mathutils import Vector

VIS = r"D:/UnityProjects/Survive Lighthouse/Assets/Visual"
FE = VIS + "/FantasyEnvironments/Environments"
FE_T = FE + "/Town/FBXs"
FE_TEX = FE + "/Town/Textures"
FE_ENV = FE + "/FBX"
FE_ENVTEX = FE + "/Textures"
FE_TREES = FE + "/Ambient-Occlusion-Trees"
CP = VIS + "/CastlePack/FBX"
PZ = VIS + "/Pandazole_Ultimate_Pack/Pandazole Survival Crafting Pack"
RK = r"D:/UnityProjects/Acvadoor/Assets/RPG Creation Kit/Models"
RKE = RK + "/RPG Creation Kit Environment"
RKA = RK + "/Additional Models"

# pack-tag: fe (FantasyEnvironments, Y-up -> rotate), cp (CastlePack), pz (Pandazole), rk (RPG Creation Kit)
ASSETS = {}

def _reg(prefix, folder, names, pack):
    for n in names:
        key = prefix + re.sub(r"[^a-z0-9]+", "_", n.lower()).strip("_")
        ASSETS[key] = (folder + "/" + n + ".fbx", pack)

_reg("fe_", FE_T, [
    "WoodenWall_1", "WoodenWall_2", "WoodenWall_3", "WoodenWall_Corner", "WoodenWall_Entrace",
    "WoodenWall_GateL", "WoodenWall_GateR", "WoodenWall_Stairs", "WoodenWall_TowerCorner", "WoodenWall_Towers",
    "Wall_corner", "Wall_entrance", "Wall_gateL", "Wall_gateR", "Wall_part1", "Wall_part2", "Wall_props",
    "Wall_tower1", "Wall_tower2", "House1", "House2", "House3", "WoodenHouse_1", "WoodenHouse_2", "WoodenHouse_3",
    "WoodenHouse_door", "WoodenHouse_fireplace", "WoodenHouse_shutter_R_L", "Tavern", "Tavern_doorL", "Tavern_doorR",
    "Tavern_door_room", "Tavern_interior_chandelier", "Tavern_interior_curtain", "Tavern_interior_lamp",
    "Windmill", "cart1", "cart2", "cart3", "cart4",
    "Furniture_bed", "Furniture_book1", "Furniture_book2", "Furniture_book3", "Furniture_candlestick1",
    "Furniture_candlestick3", "Furniture_chair1", "Furniture_chair2", "Furniture_chair3", "Furniture_chair4",
    "Furniture_cupboard1", "Furniture_cupboard2", "Furniture_cupboard3", "Furniture_elixir1", "Furniture_elixir2",
    "Furniture_elixir3", "Furniture_scroll", "Furniture_scrolls", "Furniture_table", "Furniture_tablecloth",
    "Furniture_tablecloth_small", "Furniture_table_round", "Furniture_table_small",
    "Food_apple", "Food_bottle1", "Food_bottle2", "Food_bowler", "Food_bowler1", "Food_bread_1", "Food_bread_2",
    "Food_chicken", "Food_corn", "Food_cup1", "Food_fish", "Food_knife", "Food_pie", "Food_plate", "Food_spoon",
    "storage_bag", "storage_bag_beet", "storage_bag_carrot", "storage_bag_potato", "storage_bag_seeds",
    "storage_barrel", "storage_barrel_drink", "storage_barrel_fish", "storage_barrel_small", "storage_basket",
    "storage_basket_small", "storage_jug", "storage_jug_small",
], "fe")
_reg("fe_", FE_ENV, ["Bush1", "Fern1", "Fern2", "Grass1", "Grass2", "Grass3", "Flower1", "Flower2", "Flower4",
                     "Mushroom1", "Plant1", "Plant2", "Rock1", "Rock2", "Rock3", "Stone1", "Stone2", "Stone3", "Rye"], "fe")
_reg("fe_", FE_TREES + "/FBX", ["Birch_tree1", "Birch_tree2", "Birch_tree3", "Oak_tree1", "Oak_tree2", "Pine_tree1",
                                "Pine_tree2", "Pine_tree3", "Deciduous_tree1", "Deciduous_tree_stump", "Pine_stump"], "fe")
_reg("cp_", CP, ["FloorTorch", "HightTower", "LargePhilar", "MediumPhilar", "SmallPhilar", "Platform", "Ramp",
                 "StoneBridge", "StoneBridgeEntry", "StoneBridgeFlat", "StoneBridgeTwoSided", "WallEntrance",
                 "WallGateHarrow", "WallGateHarrowDuble", "WallStairs", "WallTorch", "WallTower", "WoodenBridge",
                 "WoodenBridgeHanddrails"], "cp")
_reg("pz_", PZ + "/Models", [
    "Arrow_01", "Axe_01", "Axe_02", "Bed_01", "Bed_02", "BagBed_01", "Binoculars", "Bone", "BonesCollection", "Bow_01",
    "Bucket", "Campfire", "CookingSet_01", "CookingSet_02", "Crate", "Cup", "Flare", "Fur", "Hammer_01", "Jug",
    "Kettle", "Knife_01", "Leather", "LeatherPouch", "Map_01", "Map_02", "MidicineBag_01", "Midicine_01",
    "Midicine_02", "MidKit_01", "Nails", "Pickaxe", "Pot_01", "Pot_02", "Quiver", "Rock_01", "Rock_02", "Rock_03",
    "Rope", "Saw", "Skull", "Spear_01", "Spear_02", "Trap", "WoodLog_01", "WoodLog_02", "WoodLog_03", "WoodLog_04",
    "WoodStick_01", "XBow_01", "GlassBottle_01", "GlassBottle_03",
], "pz")
_reg("rk_", RKE + "/Bed and Drawer", ["Bed Single", "Bed 2 people", "Drawer", "FloorBed", "Wooden Stair"], "rk")
_reg("rk_", RKE + "/Fence Wooden and Concrete", ["FireTorch", "Pan", "Wooden Fence Long", "Wooden Fence Short",
                                                 "Wooden Fence Door", "Stone Wall Door", "Stone Wall Long Wall 1"], "rk")
for sub, n in [("Props/Anvil", "Anvil"), ("Props/Barrel", "Barrel"), ("Props/Barrel_Food", "Barrel_Food"),
               ("Props/Crate_Small", "Crate_s"), ("Props/Wood_Stump", "Wood_Stump"),
               ("Props/Building_Props/Door_Wood", "Door_Wood"),
               ("Additional Models/Big_Props/Hanging_Rack", "Hanging_Rack_Wood"),
               ("Additional Models/Big_Props/Wanted_Board_Wood", "Wanted_Board_Wood"),
               ("Additional Models/Big_Props/Wooden_Counter", "Wooden_Counter"),
               ("Additional Models/Big_Props/Fences/Fence_Long", "Fence_Long"),
               ("Additional Models/Big_Props/Fences/Thin_Fence_Long", "Thin_Fence_Long"),
               ("Additional Models/Lantern_Parts/Lamp", "Lamp"),
               ("Additional Models/Small_Props/Bucket_Wood", "Bucket_Wood"),
               ("Additional Models/Small_Props/Bowl_Food_Wood", "Bowl_Food_Wood"),
               ("Additional Models/Small_Props/Potions/Potion_Red", "Potion_Red"),
               ("Additional Models/Small_Props/Potions/Potion_Green", "Potion_Green")]:
    ASSETS["rk_" + n.lower()] = (RKA + "/" + sub + "/" + n + ".fbx", "rk")
ASSETS["cp_harrow"] = ("", "cp")

# Quaternius (CC0): Medieval Village MegaKit — каменная кладка и дома; Fantasy Props MegaKit — реквизит
QV = r"D:/Metroidvany/assets_ext/MedievalVillage/Medieval Village MegaKit[Standard]/glTF"
QP = r"D:/Metroidvany/assets_ext/FantasyProps/Exports/glTF"
for _d, _pre in ((QV, "qv_"), (QP, "qp_")):
    if os.path.isdir(_d):
        for _f in os.listdir(_d):
            if _f.lower().endswith(".gltf"):
                ASSETS[_pre + re.sub(r"[^a-z0-9]+", "_", _f[:-5].lower()).strip("_")] = (_d + "/" + _f, "q")
ASSETS["rk_stall_body"] = (RKA + "/Props/Stall_Small/Stall_Small_Body/Stall_Small_Body.fbx", "rk")
ASSETS["rk_stall_cloth"] = (RKA + "/Props/Stall_Small/Stall_Small_Cloth/Stall_Small_Cloth.fbx", "rk")

PA = VIS + "/POLYART_Ancient Village"
PA_TEX = {"pa_bridge": PA + "/Textures/Extra/Bridge/bridge parts2_PA_BridgeSet_PileMtl_BaseColor.png",
          "pa_rock1": PA + "/Textures/Rock/PA_RockSet1_Pile1/PA_RockSet1_baseColor.png"}
ASSETS["pa_bridge"] = (PA + "/Models/Extra/Bridge/PA_Bridge.fbx", "pa")
ASSETS["pa_bridge_pile1"] = (PA + "/Models/Extra/Bridge/PA_BridgeSet_Pile1.fbx", "pa")
for i in range(1, 8):
    ASSETS["pa_rock1_%d" % i] = (PA + "/Models/Rock/Rockset1/PA_RockSet1_Pile%d.fbx" % i, "pa")

# ---------------------------------------------------------------- materials
FE_TEXMAP = {"WoodenWall": FE_TEX + "/WoodenWall.png", "Wall1": FE_TEX + "/Wall1.png", "Tavern": FE_TEX + "/Tavern.png",
             "Tavern_interior": FE_TEX + "/Tavern_interior.png", "Furniture": FE_TEX + "/Furniture.png",
             "Food": FE_TEX + "/Food.png", "Storage": FE_TEX + "/Storage.png", "House1": FE_TEX + "/House1.png",
             "WoodenHouse": FE_TEX + "/WoodenHouse.png", "Windmill": FE_TEX + "/Windmill.png", "cart": FE_TEX + "/cart.png",
             "Rock1": FE_ENVTEX + "/Rock1.png", "Rock2": FE_ENVTEX + "/Rock2.png", "Rock3": FE_ENVTEX + "/Rock3.png",
             "Stone": FE_ENVTEX + "/Stone.png", "Plant": FE_ENVTEX + "/Plant.tga",
             "PandaMat": PZ + "/Textures/PandaMat.png"}
for t in ["Birch_bark", "Birch_leaves", "Oak_bark", "oak_leaves", "Pine_bark", "Pine_leaves", "Tree_bark1",
          "Tree_bark2", "Tree_leaves1", "Tree_leaves2", "Willow_bark", "Willow_leaves"]:
    ext = ".tga" if "leaves" in t else ".png"
    FE_TEXMAP[t] = FE_TREES + "/Textures/" + t + ext


def tex_material(key, path, alpha=False, box=False, rough=0.85, tint=None):
    """Один общий материал на текстуру (стилизованный: без бликов)."""
    name = "FK_" + key
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    img = bpy.data.images.load(path, check_existing=True)
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = img
    tn.location = (-500, 200)
    if box:  # для ассетов без нормальной развёртки — проекция кубом
        tc = nt.nodes.new("ShaderNodeTexCoord"); tc.location = (-900, 200)
        mp = nt.nodes.new("ShaderNodeMapping"); mp.location = (-720, 200)
        mp.inputs["Scale"].default_value = (0.35, 0.35, 0.35)
        nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
        nt.links.new(mp.outputs["Vector"], tn.inputs["Vector"])
        tn.projection = "BOX"; tn.projection_blend = 0.25
    col = tn.outputs["Color"]
    if tint:
        mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"; mix.blend_type = "MULTIPLY"
        mix.inputs[0].default_value = 1.0
        mix.inputs[7].default_value = (*tint, 1)
        nt.links.new(col, mix.inputs[6]); col = mix.outputs[2]
    nt.links.new(col, bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = rough
    try:
        bsdf.inputs["Specular IOR Level"].default_value = 0.2
    except Exception:
        pass
    if alpha:
        nt.links.new(tn.outputs["Alpha"], bsdf.inputs["Alpha"])
        try:
            m.surface_render_method = "DITHERED"
        except Exception:
            m.blend_method = "CLIP"
    return m


def flat_material(key, rgb, rough=0.9, emit=0.0):
    name = "FK_" + key
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = rough
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*rgb, 1)
        bsdf.inputs["Emission Strength"].default_value = emit
    return m


def _rk_texture(fbx_path):
    folder = os.path.dirname(fbx_path)
    files = os.listdir(folder)
    for sub in files:  # текстуры иногда лежат в подпапке Textures
        if os.path.isdir(folder + "/" + sub) and sub.lower().startswith("tex"):
            files += [sub + "/" + f for f in os.listdir(folder + "/" + sub)]
    for f in files:
        lf = f.lower()
        if lf.endswith((".png", ".tga", ".jpg")) and any(k in lf for k in ("base_color", "basecolor", "albedo", "diffuse", "_d.", "color")):
            return folder + "/" + f
    for f in os.listdir(folder):
        if f.lower().endswith((".png", ".tga", ".jpg")) and "normal" not in f.lower() and "height" not in f.lower() \
                and "metal" not in f.lower() and "rough" not in f.lower() and "ao" not in f.lower():
            return folder + "/" + f
    return None


CP_COLORS = {"stone": (0.42, 0.42, 0.44), "wood": (0.36, 0.2, 0.08), "roof": (0.42, 0.16, 0.08),
             "fire": (1.0, 0.55, 0.15), "metal": (0.25, 0.25, 0.27)}


def _fix_materials(objs, pack, fbx_path):
    for o in objs:
        if o.type != "MESH":
            continue
        for s in o.material_slots:
            mname = s.material.name.split(".")[0].strip() if s.material else ""
            if pack in ("fe", "pz"):
                if mname in FE_TEXMAP:
                    s.material = tex_material(mname, FE_TEXMAP[mname], alpha=("leaves" in mname or mname == "Plant"))
            elif pack == "pa":
                tkey = "pa_bridge" if "Bridge" in fbx_path else "pa_rock1"
                s.material = tex_material(tkey, PA_TEX[tkey])
            elif pack == "rk":
                p = _rk_texture(fbx_path)
                if p:
                    s.material = tex_material("rk_" + os.path.basename(os.path.dirname(fbx_path)), p)
            elif pack == "cp":
                low = mname.lower()
                if "fire" in low:
                    s.material = flat_material("cp_fire", CP_COLORS["fire"], emit=4.0)
                elif "roof" in low:
                    s.material = tex_material("cp_roof", FE_TEXMAP["WoodenWall"], box=True)
                elif any(k in low for k in ("wood", "plank", "002")):
                    s.material = tex_material("cp_wood", FE_TEX + "/WoodenWall.png", box=True)
                else:
                    s.material = tex_material("cp_stone", FE_ENVTEX + "/Stone.png", box=True)


TREE_TEX = {"fe_birch": ("Birch_bark", "Birch_leaves"), "fe_pine": ("Pine_bark", "Pine_leaves"),
            "fe_oak": ("Oak_bark", "oak_leaves"), "fe_deciduous": ("Tree_bark1", "Tree_leaves1"),
            "fe_willow": ("Willow_bark", "Willow_leaves")}


def _fix_trees(key, objs):
    """У деревьев FE материалы безымянные («14 - Default»): слот 0 — кора, слот 1 — листва."""
    if key == "fe_rock1":
        for o in objs:
            for s in o.material_slots:
                s.material = tex_material("Rock1", FE_TEXMAP["Rock1"])
        return
    if key.startswith(("fe_bush", "fe_grass", "fe_fern", "fe_flower", "fe_plant", "fe_mushroom", "fe_rye")):
        for o in objs:
            for s in o.material_slots:
                if not (s.material and s.material.name.startswith("FK_")):
                    s.material = tex_material("Plant", FE_TEXMAP["Plant"], alpha=True)
        return
    for pre, (bark, leaves) in TREE_TEX.items():
        if key.startswith(pre):
            for o in objs:
                if o.type != "MESH":
                    continue
                if len(o.material_slots) > 0:
                    o.material_slots[0].material = tex_material(bark, FE_TEXMAP[bark])
                if len(o.material_slots) > 1:
                    o.material_slots[1].material = tex_material(leaves, FE_TEXMAP[leaves], alpha=True)


# ---------------------------------------------------------------- library
def _coll(name, parent=None):
    c = bpy.data.collections.get(name)
    if not c:
        c = bpy.data.collections.new(name)
        (parent or bpy.context.scene.collection).children.link(c)
    return c


def lib_root():
    root = _coll("_LIB")
    lc = bpy.context.view_layer.layer_collection.children.get("_LIB")
    if lc:
        lc.exclude = True
    return root


def load(key):
    """Импортирует ассет (один раз) в _LIB/<key>, кладёт основание в 0, центр по XY."""
    cname = "A_" + key
    if cname in bpy.data.collections:
        return bpy.data.collections[cname]
    if key == "cp_harrow":  # решётка-герса без арки: отдельный ассет из WallGateHarrow
        src = load("cp_wallgateharrow")
        h = next(o for o in src.objects if o.name.startswith("Harrow"))
        c = bpy.data.collections.new(cname); lib_root().children.link(c)
        hc = h.copy(); hc.data = h.data.copy(); c.objects.link(hc)
        bpy.context.view_layer.update()
        pts = [hc.matrix_world @ Vector(p) for p in hc.bound_box]
        hc.location -= Vector(((min(p.x for p in pts) + max(p.x for p in pts)) / 2,
                               (min(p.y for p in pts) + max(p.y for p in pts)) / 2, min(p.z for p in pts)))
        hc.data.materials.clear(); hc.data.materials.append(flat_material("iron", (0.07, 0.065, 0.06), rough=0.6))
        return c
    path, pack = ASSETS[key]
    root = lib_root()
    lc = bpy.context.view_layer.layer_collection.children.get("_LIB")
    if lc:
        lc.exclude = False
    before = set(bpy.data.objects)
    if path.lower().endswith((".gltf", ".glb")):
        bpy.ops.import_scene.gltf(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    for o in list(new):
        if o.name.startswith("UCX_") or re.search(r"_LOD[1-9]$", o.name) or o.type in ("LIGHT", "CAMERA"):
            new.remove(o)
            bpy.data.objects.remove(o)
    c = bpy.data.collections.new(cname)
    root.children.link(c)
    for o in new:
        for uc in list(o.users_collection):
            uc.objects.unlink(o)
        c.objects.link(o)
    tops = [o for o in new if o.parent is None]
    if pack == "fe":
        for o in tops:
            o.rotation_euler.x += math.radians(90)
    # apply rot/scale
    bpy.ops.object.select_all(action="DESELECT")
    for o in new:
        if o.type in ("MESH", "EMPTY"):
            o.select_set(True)
    if new:
        bpy.context.view_layer.objects.active = new[0]
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.context.view_layer.update()
    # recentre: XY centre, Z min -> 0
    pts = [o.matrix_world @ Vector(cn) for o in new if o.type == "MESH" for cn in o.bound_box]
    if pts:
        cx = (min(p.x for p in pts) + max(p.x for p in pts)) / 2
        cy = (min(p.y for p in pts) + max(p.y for p in pts)) / 2
        mz = min(p.z for p in pts)
        for o in tops:
            o.location -= Vector((cx, cy, mz))
    _fix_materials(new, pack, path)
    _fix_trees(key, new)
    bpy.context.view_layer.update()
    if lc:
        lc.exclude = True
    return c


def size(key):
    c = load(key)
    lc = bpy.context.view_layer.layer_collection.children["_LIB"]
    pts = []
    for o in c.all_objects:
        if o.type == "MESH":
            pts += [o.matrix_world @ Vector(cn) for cn in o.bound_box]
    if not pts:
        return (0, 0, 0)
    return tuple(round(max(getattr(p, a) for p in pts) - min(getattr(p, a) for p in pts), 3) for a in "xyz")


def place(key, loc, rz=0.0, s=1.0, coll="SCENE", name=None, rx=0.0, ry=0.0):
    """Ставит инстанс ассета. s — число или (sx,sy,sz). rz в градусах."""
    c = load(key)
    target = _coll(coll, _coll("FORT")) if coll != "SCENE" else bpy.context.scene.collection
    e = bpy.data.objects.new(name or (key + "_i"), None)
    e.instance_type = "COLLECTION"
    e.instance_collection = c
    e.location = loc
    e.rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
    e.scale = (s, s, s) if isinstance(s, (int, float)) else s
    e.empty_display_size = 0.3
    target.objects.link(e)
    return e


def shot(center, width, direction=(0.35, -1, 0.35), ortho=True, cam="CAM_LOOK", clip=None, res=(1600, 900), dist=None):
    """Ставит служебную камеру: center — точка, width — ширина кадра (м), direction — откуда смотрим.
    clip — расстояние от камеры, до которого всё отсекается (для разреза)."""
    c = bpy.data.objects.get(cam)
    if not c:
        c = bpy.data.objects.new(cam, bpy.data.cameras.new(cam))
        bpy.context.scene.collection.objects.link(c)
    d = Vector(direction).normalized()
    dist = dist or max(width * 1.5, 60)
    c.location = Vector(center) + d * dist
    c.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    c.data.type = "ORTHO" if ortho else "PERSP"
    c.data.ortho_scale = width
    c.data.clip_start = clip if clip else 0.1
    c.data.clip_end = dist * 4
    if not ortho:
        c.data.lens = 35
    bpy.context.scene.camera = c
    bpy.context.scene.render.resolution_x, bpy.context.scene.render.resolution_y = res
    return c


def realize(key, loc, rz=0.0, s=1.0, name=None, coll="SCENE", rx=0.0, ry=0.0):
    """Настоящая копия ассета (одна сетка, трансформ запечён) — чтобы резать в ней проёмы."""
    import bmesh
    from mathutils import Matrix, Euler
    c = load(key)
    sv = (s, s, s) if isinstance(s, (int, float)) else s
    M = Matrix.Translation(loc) @ Euler((math.radians(rx), math.radians(ry), math.radians(rz))).to_matrix().to_4x4() \
        @ Matrix.Diagonal((*sv, 1.0))
    bm = bmesh.new(); mats = []
    for src in c.all_objects:
        if src.type != "MESH":
            continue
        tmp = src.data.copy(); tmp.transform(M @ src.matrix_world)
        if M.determinant() < 0:
            tmp.flip_normals()
        offs = len(mats)
        mats += [sl.material for sl in src.material_slots]
        nf = len(bm.faces)
        bm.from_mesh(tmp)
        bm.faces.ensure_lookup_table()
        for f in bm.faces[nf:]:
            f.material_index += offs
        bpy.data.meshes.remove(tmp)
    me = bpy.data.meshes.new(name or key)
    bm.to_mesh(me); bm.free()
    for m in mats:
        me.materials.append(m)
    o = bpy.data.objects.new(name or key, me)
    target = _coll(coll, _coll("FORT")) if coll != "SCENE" else bpy.context.scene.collection
    target.objects.link(o)
    return o


def cut_box(obj, box):
    """Вырезать проём: рассечь сетку плоскостями коробки и удалить грани внутри. box=(x0,x1,y0,y1,z0,z1)."""
    import bmesh
    x0, x1, y0, y1, z0, z1 = box
    bm = bmesh.new(); bm.from_mesh(obj.data)
    for co, no in (((x0, 0, 0), (1, 0, 0)), ((x1, 0, 0), (1, 0, 0)), ((0, y0, 0), (0, 1, 0)),
                   ((0, y1, 0), (0, 1, 0)), ((0, 0, z0), (0, 0, 1)), ((0, 0, z1), (0, 0, 1))):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
    eps = 1e-4
    dead = [f for f in bm.faces if x0 + eps < f.calc_center_median().x < x1 - eps
            and y0 + eps < f.calc_center_median().y < y1 - eps and z0 + eps < f.calc_center_median().z < z1 - eps]
    bmesh.ops.delete(bm, geom=dead, context="FACES")
    bm.to_mesh(obj.data); bm.free(); obj.data.update()
    return len(dead)


def clear_coll(name):
    c = bpy.data.collections.get(name)
    if not c:
        return
    for o in list(c.all_objects):
        bpy.data.objects.remove(o)
