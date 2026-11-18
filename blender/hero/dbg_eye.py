import bpy
from mathutils import Vector
rig=bpy.data.objects["Hero_Rig"]; eyes=bpy.data.objects["Hero_Eyes"]
b=rig.data.bones["eye_r"]; print("eye_r head",rig.matrix_world@b.head_local,"tail",rig.matrix_world@b.tail_local,"parent",b.parent.name)
print("eye groups",[g.name for g in eyes.vertex_groups], "mods",[(m.type,m.object and m.object.name) for m in eyes.modifiers], "parent",eyes.parent and eyes.parent.name)
pb=rig.pose.bones["eye_r"]; bpy.context.view_layer.update()
print("pose rot diff deg", (pb.matrix.to_quaternion().rotation_difference(b.matrix_local.to_quaternion())).angle*57.3)
vs=[eyes.matrix_world@v.co for v in eyes.data.vertices]; c=sum(vs,Vector())/len(vs)
print("eye verts",len(vs),"center",c,"miny",min(v.y for v in vs),"maxy",max(v.y for v in vs))
mat=eyes.data.materials[0]; print("eye mat",mat.name,[ (n.type, getattr(n,'image',None) and n.image.name) for n in mat.node_tree.nodes])
