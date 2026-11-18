import bpy, bmesh
o=bpy.data.objects["Hero_Body"]; mw=o.matrix_world
zs=[(mw@v.co).z for v in o.data.vertices]
print("body z range",min(zs),max(zs),"verts",len(zs))
bm=bmesh.new(); bm.from_mesh(o.data)
for e in bm.edges:
    if e.is_boundary:
        p=mw@((e.verts[0].co+e.verts[1].co)/2)
        if 1.2<p.z<1.3 and abs(p.x)<0.3:
            print("sample boundary edge at",tuple(round(x,3) for x in p), "faces",len(e.link_faces), "mat", e.link_faces[0].material_index); break
print("mats",[m.name for m in o.data.materials])
