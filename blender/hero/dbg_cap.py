import bpy
from mathutils import Vector
rig=bpy.data.objects["Hero_Rig"]
SA=rig.matrix_world@rig.data.bones["upperarm_l"].head_local; DA=(rig.matrix_world@rig.data.bones["lowerarm_l"].head_local-SA).normalized()
for o in bpy.data.objects:
    if o.type!='MESH' or o.name.startswith("Hero_Body_Sculpt"): continue
    mw=o.matrix_world; n3=mw.to_3x3()
    tip=[p for p in o.data.polygons if ((mw@p.center)-SA).dot(DA)>0.04 and (mw@p.center).x>0.15]
    if not tip: continue
    out=sum(1 for p in tip if (n3@p.normal).dot((mw@p.center)-(SA+DA*0.0))>0)
    # boundary edges remaining near tip
    import bmesh
    bm=bmesh.new(); bm.from_mesh(o.data)
    bnd=[e for e in bm.edges if e.is_boundary and ((mw@((e.verts[0].co+e.verts[1].co)/2))-SA).dot(DA)>0.0 and (mw@e.verts[0].co).x>0.15]
    print(o.name,"tip faces",len(tip),"outward",out,"boundary edges near tip",len(bnd))
