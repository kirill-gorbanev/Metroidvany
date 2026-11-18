using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fortress.Rig;
using Fortress.Rig.Human;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Сборка физического человека из собранного персонажа (Assets/Characters/Assembled):
    /// «Призрак» — копия с аниматором (анимации UAL) и CharacterController, невидимая;
    /// «Тело» — видимая копия: 13 физических частей на костях (таз, грудь, голова, плечи, предплечья, бёдра, голени, стопы),
    /// суставы с пределами, мышцы повторяют кости призрака. Масса ~70 кг.</summary>
    public static class RigHumanBuilder
    {
        const string Root = "Assets/Rig";
        const string Anim = "Assets/Characters/Quaternius/Anim/";
        public const string Controller = Root + "/Anim/Human.controller";
        /// <summary>Знак сгиба колена/локтя для пределов сустава (проверено на стенде).</summary>
        public static float hingeSign = 1f;

        class BoneSpec
        {
            public string bone, parent, child; public float mass, strength; public string shape; public float radius;
            public bool hinge; public float bendToward;   // +1 вперёд (локоть), −1 назад (колено)
            public float twist, swingY, swingZ, bend;
        }

        static readonly BoneSpec[] Spec =
        {
            new BoneSpec { bone = "pelvis", parent = null, child = "spine_02", mass = 12f, strength = 1f, shape = "pelvis" },
            new BoneSpec { bone = "spine_02", parent = "pelvis", child = "neck_01", mass = 18f, strength = 1.6f, shape = "chest", twist = 20f, swingY = 30f, swingZ = 25f },
            new BoneSpec { bone = "Head", parent = "spine_02", child = null, mass = 5f, strength = 0.45f, shape = "head", twist = 45f, swingY = 35f, swingZ = 30f },
            new BoneSpec { bone = "upperarm_l", parent = "spine_02", child = "lowerarm_l", mass = 2.5f, strength = 0.3f, shape = "capsule", radius = 0.055f, twist = 60f, swingY = 85f, swingZ = 85f },
            new BoneSpec { bone = "lowerarm_l", parent = "upperarm_l", child = "hand_l", mass = 2f, strength = 0.2f, shape = "capsule", radius = 0.045f, hinge = true, bendToward = 1f, bend = 140f },
            new BoneSpec { bone = "upperarm_r", parent = "spine_02", child = "lowerarm_r", mass = 2.5f, strength = 0.3f, shape = "capsule", radius = 0.055f, twist = 60f, swingY = 85f, swingZ = 85f },
            new BoneSpec { bone = "lowerarm_r", parent = "upperarm_r", child = "hand_r", mass = 2f, strength = 0.2f, shape = "capsule", radius = 0.045f, hinge = true, bendToward = 1f, bend = 140f },
            new BoneSpec { bone = "thigh_l", parent = "pelvis", child = "calf_l", mass = 8f, strength = 1.2f, shape = "capsule", radius = 0.075f, twist = 25f, swingY = 80f, swingZ = 40f },
            new BoneSpec { bone = "calf_l", parent = "thigh_l", child = "foot_l", mass = 4f, strength = 0.9f, shape = "capsule", radius = 0.055f, hinge = true, bendToward = -1f, bend = 140f },
            new BoneSpec { bone = "foot_l", parent = "calf_l", child = "ball_l", mass = 1.2f, strength = 0.3f, shape = "foot", twist = 10f, swingY = 30f, swingZ = 25f },
            new BoneSpec { bone = "thigh_r", parent = "pelvis", child = "calf_r", mass = 8f, strength = 1.2f, shape = "capsule", radius = 0.075f, twist = 25f, swingY = 80f, swingZ = 40f },
            new BoneSpec { bone = "calf_r", parent = "thigh_r", child = "foot_r", mass = 4f, strength = 0.9f, shape = "capsule", radius = 0.055f, hinge = true, bendToward = -1f, bend = 140f },
            new BoneSpec { bone = "foot_r", parent = "calf_r", child = "ball_r", mass = 1.2f, strength = 0.3f, shape = "foot", twist = 10f, swingY = 30f, swingZ = 25f },
        };

        [MenuItem("Fortress/Rig/Build Humans (physical puppets)")]
        public static void BuildAll()
        {
            NameLayers();
            BuildController();
            foreach (var n in new[] { "Peasant_Bearded", "Ranger_Male", "Ranger_Female" })
                Build("Assets/Characters/Assembled/" + n + ".prefab", n);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Подписать слои 8/9 в настройках проекта (чтобы в инспекторе было видно, что это).</summary>
        public static void NameLayers()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            void Name(int i, string n)
            {
                var e = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(e.stringValue)) e.stringValue = n;
            }
            Name(RigLayers.Ghost, "RigGhost");
            Name(RigLayers.Body, "RigBody");
            tm.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string f)
        {
            if (AssetDatabase.IsValidFolder(f)) return;
            EnsureFolder(Path.GetDirectoryName(f).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(f).Replace('\\', '/'), Path.GetFileName(f));
        }

        static AnimationClip Clip(string name)
        {
            foreach (var f in new[] { "UAL1_Standard.fbx", "UAL2_Standard.fbx" })
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Anim + f))
                    // не служебные превью-копии («__preview__…»): они не зациклены — шаг застывал на последнем кадре
                    if (o is AnimationClip c && !c.name.StartsWith("__preview__") && c.name.EndsWith("|" + name)) return c;
            Debug.LogWarning("[Human] нет клипа " + name);
            return null;
        }

        public static AnimatorController BuildController()
        {
            EnsureFolder(Root + "/Anim");
            AssetDatabase.DeleteAsset(Controller);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(Controller);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var move = ac.CreateBlendTreeInController("Move", out var tree, 0);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip("Idle_Loop"), 0f);
            tree.AddChild(Clip("Walk_Loop"), 0.95f);     // родная скорость шага (замер по стопе в опоре)
            tree.AddChild(Clip("Jog_Fwd_Loop"), 3.2f);
            tree.AddChild(Clip("Sprint_Loop"), 5.5f);
            var sm = ac.layers[0].stateMachine;
            sm.defaultState = move;
            foreach (var n in new[] { "LayToIdle", "Hit_Chest", "Hit_Knockback", "Push_Loop", "Walk_Carry_Loop",
                                      "Idle_FoldArms_Loop", "Idle_Talking_Loop", "Idle_Torch_Loop", "Idle_Lantern_Loop", "Crouch_Idle_Loop", "Yes", "Idle_No_Loop",
                                      "Sitting_Enter", "Sitting_Idle_Loop", "Sitting_Talking_Loop", "Sitting_Exit", "PickUp_Table", "Interact",
                                      "Idle_Rail_Loop", "Idle_Rail_Call", "Chest_Open", "Fixing_Kneeling", "ClimbUp_1m" })
            {
                var c = Clip(n);
                if (!c) continue;
                var st = sm.AddState(n);
                st.motion = c;
            }
            // слой «Ноша»: только руки, поза «несу перед собой»; вес включает HumanInteract
            ac.AddLayer("Carry");
            var layers = ac.layers;
            layers[0].iKPass = true;                     // стопы на ступени, таз на сиденье
            layers[1].iKPass = true;                     // кисти — к ноше
            layers[1].defaultWeight = 0f;
            layers[1].avatarMask = ArmsMask();
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ac.layers = layers;
            var hold = ac.layers[1].stateMachine.AddState("Hold");
            hold.motion = Clip("Walk_Carry_Loop");
            ac.layers[1].stateMachine.defaultState = hold;
            EditorUtility.SetDirty(ac);
            return ac;
        }

        static AvatarMask ArmsMask()
        {
            string path = Root + "/Anim/Arms.mask";
            var m = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (!m) { m = new AvatarMask(); AssetDatabase.CreateAsset(m, path); }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) m.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var bp in new[] { AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers,
                                       AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK })
                m.SetHumanoidBodyPartActive(bp, true);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Dictionary<string, Transform> Bones(GameObject g) =>
            g.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(x => x.Key, x => x.First());

        public static GameObject Build(string characterPath, string name)
        {
            EnsureFolder(Root + "/Prefabs");
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller) ?? BuildController();
            var root = new GameObject("Human_" + name);

            // призрак: корень (ноги — CharacterController, воля — HumanDriver) и модель с аниматором.
            // Модели Quaternius смотрят в −Z своего корня — модель развёрнута на 180°, чтобы человек смотрел туда, куда идёт.
            var ghost = new GameObject("Призрак");
            ghost.transform.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Модель";
            model.transform.SetParent(ghost.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var anim = model.GetComponent<Animator>();
            anim.runtimeAnimatorController = ac;
            anim.applyRootMotion = false;
            anim.updateMode = AnimatorUpdateMode.Fixed;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var cc = ghost.AddComponent<CharacterController>();
            cc.height = 1.75f; cc.radius = 0.28f; cc.center = new Vector3(0f, 0.9f, 0f); cc.stepOffset = 0.35f; cc.slopeLimit = 50f;
            ghost.layer = RigLayers.Ghost;   // призрак: только статичный мир
            var driver = ghost.AddComponent<HumanDriver>();
            driver.anim = anim;
            model.AddComponent<HumanGhostIK>();

            // тело
            var puppet = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(puppet, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            puppet.name = "Тело";
            puppet.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(puppet.GetComponent<Animator>());
            foreach (var r in puppet.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;

            var pb = Bones(puppet);
            var gb = Bones(model);
            var grip = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Root + "/Materials/RigGrip.asset");
            Vector3 fwd = Vector3.forward;
            var bodies = new Dictionary<string, Rigidbody>();
            foreach (var s in Spec)
            {
                var t = pb[s.bone];
                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.mass = s.mass; rb.linearDamping = 0.05f; rb.angularDamping = 0.6f;
                bodies[s.bone] = rb;
                Vector3 p0 = t.position;
                Vector3 p1 = s.child != null && pb.ContainsKey(s.child) ? pb[s.child].position : p0 + Vector3.up * 0.2f;
                switch (s.shape)
                {
                    case "pelvis":
                    {
                        float w = Vector3.Distance(pb["thigh_l"].position, pb["thigh_r"].position) + 0.14f;
                        Vector3 c = (p0 + pb["spine_01"].position) * 0.5f;
                        WorldBox(t, c, new Vector3(w, 0.22f, 0.22f));
                        break;
                    }
                    case "chest":
                    {
                        float w = Vector3.Distance(pb["upperarm_l"].position, pb["upperarm_r"].position) * 0.85f;
                        float h = Mathf.Abs(p1.y - p0.y) + 0.06f;
                        WorldBox(t, new Vector3(p0.x, (p0.y + p1.y) * 0.5f, p0.z), new Vector3(w, h, 0.24f));
                        break;
                    }
                    case "head":
                    {
                        var sc = t.gameObject.AddComponent<SphereCollider>();
                        sc.radius = 0.11f / t.lossyScale.x;
                        sc.center = t.InverseTransformPoint(p0 + Vector3.up * 0.09f + fwd * 0.02f);
                        break;
                    }
                    case "capsule":
                    {
                        if (s.child != null && s.child.StartsWith("hand")) p1 = p1 + (p1 - p0).normalized * 0.12f;   // кисть в предплечье
                        Capsule(t, p0, p1, s.radius);
                        break;
                    }
                    case "foot":
                    {
                        Vector3 toe = pb.ContainsKey(s.child + "_leaf") ? pb[s.child + "_leaf"].position : (pb.ContainsKey("ball_leaf" + s.bone.Substring(4)) ? pb["ball_leaf" + s.bone.Substring(4)].position : p1);
                        Vector3 heel = new Vector3(p0.x, 0f, p0.z) - fwd * 0.05f;
                        Vector3 tip = new Vector3(toe.x, 0f, toe.z) + fwd * 0.02f;
                        Vector3 c = (heel + tip) * 0.5f + Vector3.up * 0.045f;
                        var bc = WorldBox(t, c, new Vector3(0.1f, 0.09f, Vector3.Distance(heel, tip)));
                        bc.sharedMaterial = grip;
                        break;
                    }
                }
            }
            // суставы и мышцы
            var muscles = new Dictionary<string, RigMuscle>();
            foreach (var s in Spec)
            {
                if (s.parent == null) continue;
                var t = pb[s.bone];
                var j = t.gameObject.AddComponent<ConfigurableJoint>();
                j.connectedBody = bodies[s.parent];
                j.anchor = Vector3.zero;
                j.autoConfigureConnectedAnchor = true;
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
                j.enablePreprocessing = false;
                Vector3 p0 = t.position;
                Vector3 p1 = s.child != null && pb.ContainsKey(s.child) ? pb[s.child].position : p0 + Vector3.up * 0.2f;
                Vector3 dir = (p1 - p0).normalized;
                if (s.hinge)
                {
                    Vector3 bendDir = fwd * s.bendToward;
                    Vector3 axisW = Vector3.Cross(dir, bendDir).normalized;
                    j.axis = t.InverseTransformDirection(axisW);
                    j.secondaryAxis = t.InverseTransformDirection(dir);
                    float lo = hingeSign > 0 ? -5f : -s.bend, hi = hingeSign > 0 ? s.bend : 5f;
                    j.lowAngularXLimit = new SoftJointLimit { limit = lo };
                    j.highAngularXLimit = new SoftJointLimit { limit = hi };
                    j.angularYLimit = new SoftJointLimit { limit = 8f };
                    j.angularZLimit = new SoftJointLimit { limit = 8f };
                }
                else
                {
                    j.axis = t.InverseTransformDirection(dir);
                    Vector3 perp = Vector3.Cross(dir, fwd); if (perp.sqrMagnitude < 1e-3f) perp = Vector3.Cross(dir, Vector3.right);
                    j.secondaryAxis = t.InverseTransformDirection(perp.normalized);
                    j.lowAngularXLimit = new SoftJointLimit { limit = -s.twist };
                    j.highAngularXLimit = new SoftJointLimit { limit = s.twist };
                    j.angularYLimit = new SoftJointLimit { limit = s.swingY };
                    j.angularZLimit = new SoftJointLimit { limit = s.swingZ };
                }
                var m = t.gameObject.AddComponent<RigMuscle>();
                m.spring = 700f; m.damper = 45f; m.maxForce = 4000f; m.strength = s.strength;
                m.follow = gb[s.bone];
                m.followRef = gb[s.parent];
                muscles[s.bone] = m;
            }
            var body = pb["pelvis"].gameObject.AddComponent<RigBody>();
            body.title = name.Replace('_', ' ');
            body.stunThreshold = 99f;     // человека «оглушает» не порог тонуса, а равновесие (HumanPuppet)
            var hp = root.AddComponent<HumanPuppet>();
            hp.body = body; hp.driver = driver; hp.anim = anim; hp.targetPelvis = gb["pelvis"];
            hp.head = bodies["Head"];
            hp.chest = bodies["spine_02"];
            hp.feet = new[] { bodies["foot_l"], bodies["foot_r"] };
            hp.arms = new[] { muscles["upperarm_l"], muscles["lowerarm_l"], muscles["upperarm_r"], muscles["lowerarm_r"] };
            hp.legs = new[] { muscles["thigh_l"], muscles["calf_l"], muscles["thigh_r"], muscles["calf_r"] };
            root.AddComponent<HumanInteract>();

            // поверх старого префаба (ссылки стенда на него сохраняются)
            string path = $"{Root}/Prefabs/Human_{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log($"[Human] собран {path}");
            return prefab;
        }

        /// <summary>Коробка, заданная в мире (центр, размеры по мировым осям), в осях кости.</summary>
        static BoxCollider WorldBox(Transform t, Vector3 centerW, Vector3 sizeW)
        {
            var bc = t.gameObject.AddComponent<BoxCollider>();
            bc.center = t.InverseTransformPoint(centerW);
            Vector3 sx = t.InverseTransformVector(new Vector3(sizeW.x, 0, 0));
            Vector3 sy = t.InverseTransformVector(new Vector3(0, sizeW.y, 0));
            Vector3 sz = t.InverseTransformVector(new Vector3(0, 0, sizeW.z));
            bc.size = new Vector3(Mathf.Abs(sx.x) + Mathf.Abs(sy.x) + Mathf.Abs(sz.x),
                                  Mathf.Abs(sx.y) + Mathf.Abs(sy.y) + Mathf.Abs(sz.y),
                                  Mathf.Abs(sx.z) + Mathf.Abs(sy.z) + Mathf.Abs(sz.z));
            return bc;
        }

        /// <summary>Капсула от p0 к p1 вдоль ближайшей локальной оси кости.</summary>
        static void Capsule(Transform t, Vector3 p0, Vector3 p1, float r)
        {
            var cc = t.gameObject.AddComponent<CapsuleCollider>();
            Vector3 d = t.InverseTransformVector(p1 - p0);
            Vector3 a = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
            cc.direction = a.x >= a.y && a.x >= a.z ? 0 : (a.y >= a.z ? 1 : 2);
            float len = d.magnitude;
            float sc = t.lossyScale.x;
            cc.radius = r / sc;
            cc.height = len + 2f * r / sc;
            cc.center = d * 0.5f;
        }
    }
}
