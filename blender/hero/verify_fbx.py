import bpy
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath="D:/Metroidvany/MetroidvanyGame/Assets/Characters/Hero/Hero.fbx")
for o in bpy.data.objects:
    if o.type=='ARMATURE': print("ARM",o.name,len(o.data.bones),"has eye_r","eye_r" in o.data.bones,"ctrl",[b.name for b in o.data.bones if b.name.startswith("CTRL")])
    if o.type=='MESH':
        sk=[k.name for k in o.data.shape_keys.key_blocks] if o.data.shape_keys else []
        print("MESH",o.name,len(o.data.vertices),"mats",[m.name for m in o.data.materials],"keys",sk)
        d=o.dimensions; print("   dims",tuple(round(x,2) for x in d))
