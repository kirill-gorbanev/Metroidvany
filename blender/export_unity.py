"""Экспорт локации v2 в Unity (FBX + маркеры геймплея).

ROOT_COLLIDE   — вся статика с коллизиями (стены, полы, лестницы, реквизит, земля)
ROOT_NOCOLLIDE — листва, плющ, знамёна, рожь, жерди стремянок, круг
ROOT_WATER     — вода рва и цистерны (без коллизий)
ROOT_MARKERS   — кубики-маркеры: MK_START, VOL_LADDER_*, VOL_WATER_*, VOL_TP_*, MK_TP_*, VOL_BREAK_*, LT_<Вт>_<r>_<g>_<b>
Скрипт меняет открытый файл, поэтому в конце перечитывает .blend с диска.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

BLEND = r"D:/Metroidvany/blender/fort_prison_loc01_v2_3d.blend"
OUT = r"D:/Metroidvany/export/Fortress.fbx"
REAL_COLLS = ("S3_STONE", "S3_FLOORS", "S3_B1", "S3_YARD", "S3_INT", "S3_LIFE", "S3_PATHS", "S3_GARDEN", "S3_NATURE")
NOCOL_HINTS = ("Vine", "Banner", "Rye", "Grass", "Bush", "Fern", "Flower", "Plant", "leaves", "WoodStick",
               "Chandelier", "Lantern", "Torch", "Candle", "Rope", "Arrow", "Birch", "Pine", "Oak", "Tree")


def root(name):
    o = bpy.data.objects.get(name) or bpy.data.objects.new(name, None)
    if o.name not in bpy.context.scene.collection.objects:
        bpy.context.scene.collection.objects.link(o)
    return o


def simple_mat(name, img_path=None, rgb=(0.5, 0.5, 0.5)):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True; nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    if img_path:
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(img_path, check_existing=True)
        nt.links.new(t.outputs[0], b.inputs["Base Color"])
    else:
        b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = 0.95
    return m


def baked_copy(src, name, parent):
    """Меш из вычисленного объекта (модификаторы применены)."""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = src.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, depsgraph=dg)
    me.transform(src.matrix_world)
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.parent = parent
    return o


def ground_uv_and_mats(o):
    tex = r"D:/UnityProjects/Survive Lighthouse/Assets/Visual/FantasyEnvironments/Environments/Textures/"
    mats = [simple_mat("EXP_grass", tex + "ground_grass.png"), simple_mat("EXP_stones", tex + "ground_stones.png"),
            simple_mat("EXP_soil", tex + "ground_soil.png")]
    o.data.materials.clear()
    for m in mats:
        o.data.materials.append(m)
    bm = bmesh.new(); bm.from_mesh(o.data)
    uv = bm.loops.layers.uv.new("UVMap") if not bm.loops.layers.uv else bm.loops.layers.uv[0]
    T = 4.0
    for f in bm.faces:
        n = f.normal; c = f.calc_center_median()
        if n.z > 0.7:
            f.material_index = 0 if c.z > -0.3 else 1
        else:
            f.material_index = 2
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            p = l.vert.co
            l[uv].uv = (p.y / T, p.z / T) if ax == 0 else ((p.x / T, p.z / T) if ax == 1 else (p.x / T, p.y / T))
    bm.to_mesh(o.data); bm.free()


def marker(name, x0, x1, y0, y1, z0, z1, parent):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = x0 + (v.co.x + 0.5) * (x1 - x0); v.co.y = y0 + (v.co.y + 0.5) * (y1 - y0); v.co.z = z0 + (v.co.z + 0.5) * (z1 - z0)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); o.parent = parent
    return o


def run():
    sc = bpy.context.scene
    rc, rn, rw, rm = root("ROOT_COLLIDE"), root("ROOT_NOCOLLIDE"), root("ROOT_WATER"), root("ROOT_MARKERS")
    # люк в тюрьму открыт: решётку не экспортируем
    g = bpy.data.objects.get("HATCH_grate_BOLT_FROM_HALL")
    if g:
        bpy.data.objects.remove(g)
    # 1) инстансы -> настоящие объекты
    made = []
    for cname in REAL_COLLS:
        c = bpy.data.collections.get(cname)
        if not c:
            continue
        def walk(l):
            yield l
            for ch in l.children:
                yield from walk(ch)
        for lc in [l for l in walk(bpy.context.view_layer.layer_collection) if l.collection == c]:
            lc.exclude = False; lc.hide_viewport = False
        c.hide_viewport = False
        inst = [o for o in c.objects if o.type == "EMPTY" and o.instance_type == "COLLECTION" and o.instance_collection]
        if not inst:
            continue
        before = set(bpy.data.objects)
        bpy.ops.object.select_all(action="DESELECT")
        for o in inst:
            o.select_set(True)
        bpy.context.view_layer.objects.active = inst[0]
        bpy.ops.object.duplicates_make_real(use_base_parent=False, use_hierarchy=True)
        new = [o for o in bpy.data.objects if o not in before]
        for o in inst:
            bpy.data.objects.remove(o)
        nocol_coll = cname == "S3_NATURE"
        for o in new:
            if o.parent is None:
                mw = o.matrix_world.copy()
                o.parent = rn if (nocol_coll or any(h.lower() in o.name.lower() for h in NOCOL_HINTS)) else rc
                o.matrix_world = mw
        made += new
    # 2) земля и вода
    gr = bpy.data.objects["GROUND3D"]
    ge = baked_copy(gr, "GROUND_EXPORT", rc); ground_uv_and_mats(ge)
    for wn in ("WATER3D", "WATER3D_cistern"):
        w = bpy.data.objects.get(wn)
        if w:
            wc = baked_copy(w, wn + "_EXPORT", rw)
            wc.data.materials.clear(); wc.data.materials.append(simple_mat("EXP_water", None, (0.04, 0.1, 0.09)))
    circ = bpy.data.objects.get("MAGIC_CIRCLE3D")
    if circ:
        cc = baked_copy(circ, "MAGIC_CIRCLE_EXPORT", rn)
        cc.data.materials.clear(); cc.data.materials.append(simple_mat("EXP_circle", None, (0.25, 0.9, 1.0)))
    # 3) маркеры геймплея
    marker("MK_START", -33.1, -32.9, 9.5, 9.7, -6.0, -5.8, rm)
    for n, a in {"VOL_LADDER_prison": (-24.7, -23.3, 15.2, 16.8, -6.0, 1.0),
                 "VOL_LADDER_pier": (3.3, 4.7, 36.7, 37.9, -3.4, 0.7),
                 "VOL_LADDER_chimney": (-17.2, -16.0, 15.5, 16.9, 24.9, 29.4),
                 "VOL_LADDER_well": (-10.7, -9.3, 1.3, 2.7, -6.0, 0.9),
                 "VOL_LADDER_drain": (-14.7, -13.3, 23.9, 25.0, -6.0, -4.2)}.items():
        marker(n, *a, rm)
    for n, a in {"VOL_WATER_N": (-57, 57, 35, 47), "VOL_WATER_S": (-57, 57, -51, -39),
                 "VOL_WATER_W": (-57, -45, -39, 35), "VOL_WATER_E": (45, 57, -39, 35)}.items():
        marker(n, *a, -4.7, -2.6, rm)
    marker("VOL_TP_chimney", -16.8, -14.4, 15.0, 17.4, 28.8, 30.6, rm)
    marker("MK_TP_chimney", -14.1, -13.9, 15.9, 16.1, 0.1, 0.3, rm)
    marker("VOL_BREAK_bars", -35.0, -19.0, 10.4, 11.6, -6.0, -3.0, rm)
    for L in [o for o in bpy.data.objects if o.type == "LIGHT" and o.data.type == "POINT"]:
        c = L.data.color
        nm = "LT_%d_%d_%d_%d" % (int(L.data.energy), int(c[0] * 255), int(c[1] * 255), int(c[2] * 255))
        p = L.matrix_world.translation
        marker(nm, p.x - 0.05, p.x + 0.05, p.y - 0.05, p.y + 0.05, p.z - 0.05, p.z + 0.05, rm)
    # 4) экспорт
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    roots = [rc, rn, rw, rm]
    for r in roots:
        r.select_set(True)
        for ch in r.children_recursive:
            ch.select_set(True)
    nsel = sum(1 for o in bpy.context.scene.objects if o.select_get())
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"EMPTY", "MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type="FACE",
                             path_mode="COPY", embed_textures=True, add_leaf_bones=False, use_custom_props=False)
    size = os.path.getsize(OUT) // 1024
    # 5) вернуть файл как был
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    return {"exported_objects": nsel, "realized": len(made), "fbx_kb": size}
