import bpy, bmesh
from mathutils import Vector
for nm in ("Hero_Body","Outfit_Peasant_Legs","Outfit_Peasant_Arms"):
    o=bpy.data.objects[nm]; mw=o.matrix_world
    bm=bmesh.new(); bm.from_mesh(o.data)
    bnd=[e for e in bm.edges if e.is_boundary]
    parent={}
    def r(v):
        while parent.get(v,v) is not v: v=parent[v]
        return v
    for e in bnd:
        a,b=r(e.verts[0]),r(e.verts[1])
        if a is not b: parent[a]=b
    comps={}
    for e in bnd: comps.setdefault(r(e.verts[0]),[]).append(e)
    print(nm, "components", len(comps))
    for es in comps.values():
        c=sum((mw@((e.verts[0].co+e.verts[1].co)/2) for e in es),Vector())/len(es)
        print("   ",len(es),"edges at",tuple(round(x,3) for x in c))
