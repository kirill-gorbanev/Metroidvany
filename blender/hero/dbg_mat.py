import bpy
OUTF="D:/Metroidvany/assets_ext/characters/Outfits/Modular Character Outfits - Fantasy[Standard]/Exports/glTF (Godot-Unreal)/Modular Parts/"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=OUTF+"Male_Peasant_Body.gltf")
for m in bpy.data.materials:
    print("MAT",m.name)
    for n in m.node_tree.nodes:
        print("  ",n.type,n.name,getattr(n,'image',None) and (n.image.name,n.image.size[:],n.image.filepath))
m=bpy.data.materials["MI_Peasant"]
for n in m.node_tree.nodes:
    if n.type in('MIX','VERTEX_COLOR'):
        print("NODE",n.type,n.name,getattr(n,'blend_type',None),getattr(n,'layer_name',None))
        for i in n.inputs:
            if i.is_linked: print("   in",i.name,i.identifier,"<-",i.links[0].from_node.type,i.links[0].from_socket.name)
            elif hasattr(i,'default_value') and i.enabled: print("   in",i.name,i.identifier, tuple(i.default_value) if hasattr(i.default_value,'__len__') else i.default_value)
        for o in n.outputs:
            for l in o.links: print("   out",o.name,"->",l.to_node.type,l.to_socket.name)
o=[x for x in bpy.data.objects if x.type=='MESH' and x.name.startswith("Male")][0]
print("color attrs",[ (a.name,a.domain,a.data_type) for a in o.data.color_attributes])
import numpy as np
ca=o.data.color_attributes[0]; arr=np.array([c.color[:] for c in ca.data]); print("vc min",arr.min(0),"max",arr.max(0))
