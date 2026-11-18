#@@SCAR@@
rough_in = base_rgh if base_rgh is not None else 0.7
SCULPT_RES = 2048          # маски, AO, кривизна
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'; sc.cycles.samples = 1
OUT_NODE = next(n for n in N if n.type == 'OUTPUT_MATERIAL')
BSDF_OUT = OUT_NODE.inputs["Surface"].links[0].from_socket


def bake_img(name, res, colorspace='sRGB', socket=None, btype='EMIT', src=None, samples=1, save=True):
    """Запекание в картинку по UV тела. src — высокополигональная копия (selected → active)."""
    img = bpy.data.images.new(name, res, res, alpha=False)
    img.colorspace_settings.name = colorspace
    em = None
    if socket is not None:
        em = N.new("ShaderNodeEmission")
        if socket.type == 'VALUE':
            cv = N.new("ShaderNodeCombineColor")
            for i in range(3):
                L.new(socket, cv.inputs[i])
            socket = cv.outputs[0]
        L.new(socket, em.inputs[0]); L.new(em.outputs[0], OUT_NODE.inputs["Surface"])
    tn = N.new("ShaderNodeTexImage"); tn.image = img
    for n in N:
        n.select = False
    tn.select = True; N.active = tn
    bpy.ops.object.select_all(action='DESELECT')
    if src is not None:
        src.hide_set(False); src.hide_render = False; src.select_set(True)
    body.select_set(True); bpy.context.view_layer.objects.active = body
    sc.cycles.samples = samples
    bpy.ops.object.bake(type=btype, margin=16, use_clear=True, use_selected_to_active=src is not None,
                        cage_extrusion=0.012, max_ray_distance=0.03)
    if src is not None:
        src.select_set(False); src.hide_render = True
    L.new(BSDF_OUT, OUT_NODE.inputs["Surface"])
    N.remove(tn)
    if em:
        N.remove(em)
    if save:
        img.filepath_raw = os.path.join(TEX, name + ".png"); img.file_format = 'PNG'; img.save()
    return img


def img_array(img):
    return np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)


# ---- 3a. маски увечий: R ожог, G плеть, B клеймо-прижигания
mk = N.new("ShaderNodeCombineColor")
L.new(burn, mk.inputs[0]); L.new(whip, mk.inputs[1]); L.new(spots, mk.inputs[2])
img_mask = bake_img("T_Hero_Masks", SCULPT_RES, 'Non-Color', mk.outputs[0])
MASK = img_array(img_mask)

# ---- 3b. скульпт: высокополигональная копия + рельеф (рубцы, морщины, складки)
from mathutils import noise as mn
hp = body.copy(); hp.data = body.data.copy(); hp.name = hp.data.name = "Hero_Body_Sculpt"
bpy.context.scene.collection.objects.link(hp)
hp.parent = None; hp.matrix_world = body.matrix_world.copy()
hp.modifiers.clear(); hp.vertex_groups.clear()
sub = hp.modifiers.new("Sub", 'SUBSURF'); sub.levels = sub.render_levels = 3
bpy.ops.object.select_all(action='DESELECT'); hp.select_set(True); bpy.context.view_layer.objects.active = hp
bpy.ops.object.modifier_apply(modifier="Sub")
me = hp.data; nV = len(me.vertices)
co = np.empty(nV * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
nr = np.empty(nV * 3, np.float32); me.vertices.foreach_get("normal", nr); nr = nr.reshape(-1, 3)
mwn = np.array(hp.matrix_world, dtype=np.float32)
W = co @ mwn[:3, :3].T + mwn[:3, 3]
li = np.empty(len(me.loops), np.int64); me.loops.foreach_get("vertex_index", li)
uvs = np.empty(len(me.loops) * 2, np.float32); me.uv_layers.active.data.foreach_get("uv", uvs)
vuv = np.zeros((nV, 2), np.float32); vuv[li] = uvs.reshape(-1, 2)
px = np.clip((vuv * SCULPT_RES).astype(np.int64), 0, SCULPT_RES - 1)
mB = MASK[px[:, 1], px[:, 0], 0]; mW = MASK[px[:, 1], px[:, 0], 1]; mS = MASK[px[:, 1], px[:, 0], 2]
print("sculpt verts", nV)

nz1 = np.array([mn.noise(Vector(p) * 90.0) for p in W], np.float32)
nz2 = np.array([mn.noise(Vector(p) * 30.0 + Vector((7, 3, 1))) for p in W], np.float32)
nz3 = np.array([mn.noise(Vector(p) * 600.0) for p in W], np.float32)
x, y, z = W[:, 0], W[:, 1], W[:, 2]
h = np.zeros(nV, np.float32)
# ожог: стянутая бугристая рубцовая ткань, валик по краю
ridged = 1.0 - np.abs(nz1)
edge = np.clip(mB * (1 - mB) * 4, 0, 1)
h += mB * (0.0011 * ridged + 0.0006 * nz2 - 0.0004) + 0.0013 * edge ** 2
# плеть — вздутые рубцы; клеймо — кратеры
h += 0.0010 * mW
h -= 0.0008 * mS
# кожа: поры
h += 0.00012 * nz3 * (1 - mB)
# лицо (жёстче): носогубные складки, лоб, межбровье, мешок и носослёзная борозда, «гусиные лапки»
front = (y < EY + 0.03) & (z > H.z - 0.02)


def seg_dist2d(ax, az, bx, bz):
    dx, dz = bx - ax, bz - az
    t = np.clip(((x - ax) * dx + (z - az) * dz) / (dx * dx + dz * dz), 0, 1)
    return np.hypot(x - (ax + t * dx), z - (az + t * dz))


for sx in (-1, 1):
    d = seg_dist2d(sx * 0.020, MZ + 0.032, sx * 0.037, MZ - 0.014)
    h -= front * 0.0016 * np.exp(-(d / 0.0035) ** 2)
fz = (z - (EZ + 0.028)) / 0.013
fh = np.clip(1 - np.abs(x) / 0.05, 0, 1) * ((z > EZ + 0.026) & (z < EZ + 0.078))
h -= front * fh * 0.00045 * np.clip(np.cos(fz * 2 * np.pi), 0, 1) ** 6
gl = (z > EZ + 0.006) & (z < EZ + 0.032)
h -= front * gl * 0.0007 * np.exp(-((np.abs(x) - 0.005) / 0.0018) ** 2)
ex, ez = EYE_R.x, EZ
d = seg_dist2d(ex + SIDE * 0.012, ez - 0.019, ex - SIDE * 0.016, ez - 0.013)
h -= front * 0.0008 * np.exp(-(d / 0.003) ** 2)
d = seg_dist2d(ex + SIDE * 0.010, ez - 0.012, ex - SIDE * 0.013, ez - 0.008)
h += front * 0.0005 * np.exp(-(d / 0.003) ** 2)
oc = ex - SIDE * 0.019
for k in (-1, 0, 1):
    d = seg_dist2d(oc, ez + k * 0.002, oc - SIDE * 0.012, ez + k * 0.006)
    h -= front * 0.0003 * np.exp(-(d / 0.0012) ** 2)
# шрам-порез через правую щёку — валик
pa, pb_ = np.array(sa_, np.float32), np.array(sb_, np.float32)
dv_ = pb_ - pa
t = np.clip(((W - pa) @ dv_) / (dv_ @ dv_), 0, 1)
d = np.linalg.norm(W - (pa + t[:, None] * dv_), axis=1)
h += (y < EY + 0.03) * 0.0007 * np.exp(-(d / 0.0016) ** 2)
# рёбра (передние бока) — дуги рёбер и межрёберные впадины
rz = z + 0.25 * np.abs(x)
ribz = (z > bone("spine_02").z - 0.10) & (z < bone("spine_03").z + 0.04)
flk = np.clip((np.abs(x) - 0.04) / 0.03, 0, 1) * np.clip((0.16 - np.abs(x)) / 0.03, 0, 1)
fr = np.array([axis_y(zz) for zz in z], np.float32)
h += ribz * flk * (y < fr) * 0.0016 * (np.cos(rz * 2 * np.pi / 0.032))
# культи — сморщенный шов
tip = np.clip((STUMP_Z + 0.03 - z) / 0.03, 0, 1) + np.clip(((W - np.array(SA)) @ np.array(DA) - (ARM_LEN - 0.01)) / 0.03, 0, 1)
h -= np.clip(tip, 0, 1) * 0.0009 * np.abs(nz1)
co += nr * h[:, None]
me.vertices.foreach_set("co", co.ravel()); me.update()
print("sculpt height range", float(h.min()), float(h.max()))
# материал копии: кривизна (pointiness) для запекания
cm = bpy.data.materials.new("Sculpt_Curvature"); cm.use_nodes = True
ct = cm.node_tree
ct.nodes.remove(next(n for n in ct.nodes if n.type == "BSDF_PRINCIPLED"))
geo = ct.nodes.new("ShaderNodeNewGeometry")
cr_ = ct.nodes.new("ShaderNodeValToRGB"); cr_.color_ramp.elements[0].position = 0.42; cr_.color_ramp.elements[1].position = 0.58
ce = ct.nodes.new("ShaderNodeEmission")
ct.links.new(geo.outputs["Pointiness"], cr_.inputs[0]); ct.links.new(cr_.outputs[0], ce.inputs[0])
ct.links.new(ce.outputs[0], next(n for n in ct.nodes if n.type == "OUTPUT_MATERIAL").inputs[0])
hp.data.materials.clear(); hp.data.materials.append(cm)
scol = bpy.data.collections.new("Sculpt (bake source)"); bpy.context.scene.collection.children.link(scol)
bpy.context.scene.collection.objects.unlink(hp); scol.objects.link(hp)

# ---- 3c. запекание скульпта на игровую модель
img_hpn = bake_img("T_Hero_SculptNormal", TEX_RES, 'Non-Color', btype='NORMAL', src=hp, save=False)
img_ao = bake_img("T_Hero_AO", SCULPT_RES, 'Non-Color', btype='AO', src=hp, samples=24)
img_cv = bake_img("T_Hero_Curvature", SCULPT_RES, 'Non-Color', btype='EMIT', src=hp)


def tex(img):
    t = N.new("ShaderNodeTexImage"); t.image = img; t.interpolation = 'Linear'
    return t.outputs[0]


AO = M("ADD", tex(img_ao), 0.0)
CV = M("ADD", tex(img_cv), 0.0)
concave = clamp(M("MULTIPLY", M("SUBTRACT", 0.5, CV), 4.0))
convex = clamp(M("MULTIPLY", M("SUBTRACT", CV, 0.5), 4.0))

# ---- 3d. цвет: болезненная кожа, грязь в складках, синяки, многослойный ожог
c = mix(0.38, base_col, (0.60, 0.52, 0.43, 1))                 # бледность
c = mix(ribs, c, (0.55, 0.40, 0.36, 1), 'MULTIPLY')
c = mix(M("MULTIPLY", sock, 0.55), c, (0.42, 0.28, 0.27, 1), 'MULTIPLY')
c = mix(stub, c, (0.16, 0.13, 0.11, 1))
c = mix(grime, c, (0.55, 0.47, 0.40, 1), 'MULTIPLY')
c = mix(cuff, c, (0.45, 0.17, 0.14, 1))
# синяки: правые рёбра, правое плечо, у культи — фиолетовый центр, жёлто-зелёный край
for ctr, rad in ((Vector((-SIDE * 0.10, bone("spine_02").y - 0.06, bone("spine_02").z)), 0.06),
                 (bone("upperarm_r") + Vector((-SIDE * 0.10, 0, -0.02)), 0.05),
                 (SA + Vector((-SIDE * 0.05, -0.05, -0.06)), 0.05)):
    bd = clamp(M("SUBTRACT", 1.0, M("DIVIDE", M("ADD", dist_to(ctr), M("MULTIPLY", M("SUBTRACT", noise(25, 3), 0.5), 0.03)), rad)))
    bcol = ramp(bd, [(0.0, (0.62, 0.58, 0.36, 1)), (0.45, (0.50, 0.40, 0.42, 1)), (0.85, (0.33, 0.18, 0.28, 1))])
    c = mix(M("MULTIPLY", clamp(M("MULTIPLY", bd, 3.0)), 0.45), c, bcol)
c = mix(M("MULTIPLY", hairm, 0.93), c, hcol)
# ожог: тёмное вино в складках → красный → розовый блестящий на гребнях; белёсые зажившие пятна; тёмный край
bn = noise(9, 6, 0.6)
burn_base = ramp(bn, [(0.30, (0.36, 0.10, 0.09, 1)), (0.50, (0.60, 0.22, 0.19, 1)), (0.70, (0.80, 0.45, 0.40, 1))])
bc = mix(M("MULTIPLY", concave, 0.8), burn_base, (0.24, 0.06, 0.07, 1))
bc = mix(M("MULTIPLY", convex, 0.7), bc, (0.93, 0.66, 0.60, 1))
pale = M("MULTIPLY", clamp(M("MULTIPLY", M("SUBTRACT", noise(4.5, 4), 0.58), 8.0)), 0.75)
bc = mix(pale, bc, (0.86, 0.72, 0.66, 1))
c = mix(burn, c, bc)
c = mix(M("MULTIPLY", burn_edge, 0.55), c, (0.30, 0.09, 0.07, 1))
c = mix(spots, c, (0.30, 0.10, 0.08, 1))
c = mix(M("MULTIPLY", whip, 0.75), c, (0.70, 0.42, 0.39, 1))
c = mix(scar, c, (0.62, 0.30, 0.28, 1))
# объём со скульпта: AO, грязь в складках, светлые гребни
c = mix(0.6, c, M("ADD", M("MULTIPLY", AO, 0.5), 0.5), 'MULTIPLY')
c = mix(M("MULTIPLY", concave, 0.45), c, (0.20, 0.15, 0.12, 1), 'MULTIPLY')
c = mix(M("MULTIPLY", convex, 0.18), c, (1.0, 0.92, 0.85, 1), 'SCREEN')
rgh = clamp(M("ADD", M("SUBTRACT", rough_in, M("MULTIPLY", burn, M("ADD", 0.22, M("MULTIPLY", convex, 0.25)))),
              M("MULTIPLY", concave, 0.15)))

img_col = bake_img("T_Hero_Body_BaseColor", TEX_RES, 'sRGB', c)
img_rgh = bake_img("T_Hero_Body_Roughness", TEX_RES, 'Non-Color', rgh)

# ---- 3e. нормали: скульпт + приглушённая деталь исходника (whiteout blend)
src_normal.reload()
sn = src_normal.copy(); sn.scale(TEX_RES, TEX_RES)
a = img_array(sn)[:, :, :3] * 2 - 1
a = a * 0.35 + np.array([0, 0, 1], np.float32) * 0.65
b = img_array(img_hpn)[:, :, :3] * 2 - 1
n_ = np.dstack([a[:, :, 0] + b[:, :, 0], a[:, :, 1] + b[:, :, 1], a[:, :, 2] * b[:, :, 2]])
n_ /= np.linalg.norm(n_, axis=2, keepdims=True)
out = np.ones((TEX_RES, TEX_RES, 4), np.float32); out[:, :, :3] = n_ * 0.5 + 0.5
img_nrm = bpy.data.images.new("T_Hero_Body_Normal", TEX_RES, TEX_RES, alpha=False)
img_nrm.colorspace_settings.name = 'Non-Color'
img_nrm.pixels[:] = out.ravel()
img_nrm.filepath_raw = os.path.join(TEX, "T_Hero_Body_Normal.png"); img_nrm.file_format = 'PNG'; img_nrm.save()
bpy.data.images.remove(sn)
hp.hide_set(True); hp.hide_render = True
