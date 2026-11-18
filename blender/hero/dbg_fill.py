import bpy, bmesh
B="D:/Metroidvany/assets_ext/characters/BaseChars/Universal Base Characters[Standard]/"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=B+"Base Characters/Godot - UE/Superhero_Male_FullBody.gltf")
o=bpy.data.objects["SuperHero_Male"]; mw=o.matrix_world
bm=bmesh.new(); bm.from_mesh(o.data)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if (mw@v.co).z<0.75], context='VERTS')
edges=[e for e in bm.edges if e.is_boundary and (mw@e.verts[0].co).z<0.8]
print("edges",len(edges))
for sides in (0,80,1000):
    bm2=bm.copy(); e2=[e for e in bm2.edges if e.is_boundary and (mw@e.verts[0].co).z<0.8]
    r=bmesh.ops.holes_fill(bm2, edges=e2, sides=sides); print("sides",sides,"->",len(r["faces"]))
for name in ("holes_fill","edgeloop_fill","contextual_create","grid_fill"):
    bm2=bm.copy(); n0=len(bm2.faces); e2=[e for e in bm2.edges if e.is_boundary and (mw@e.verts[0].co).z<0.8]
    try:
        if name=="holes_fill": r=bmesh.ops.holes_fill(bm2, edges=e2, sides=1000)
        elif name=="edgeloop_fill": r=bmesh.ops.edgeloop_fill(bm2, edges=e2)
        elif name=="contextual_create": r=bmesh.ops.contextual_create(bm2, geom=e2)
        else: r=bmesh.ops.grid_fill(bm2, edges=e2)
        print(name,"faces delta",len(bm2.faces)-n0, {k:len(v) for k,v in r.items()})
    except Exception as ex: print(name,"ERR",ex)
# loops structure
import collections
deg=collections.Counter(v for e in edges for v in e.verts)
print("vert boundary degree histogram",collections.Counter(deg.values()))
