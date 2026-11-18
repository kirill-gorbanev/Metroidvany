using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fortress.Rig;
using Fortress.Rig.Human;
using Fortress.Rig.Tests;
using Fortress.Rig.V3;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fortress.EditorTools
{
    /// <summary>Сборка человека рига v3 из собранного персонажа (Assets/Characters/Assembled): видимая модель с аниматором —
    /// главная по виду, капсула движения (HumanMotor) — главная по ходу, физика — только отклик (HumanReaction).
    /// Корень: CharacterController (слой RigMover) + HumanMotor + HumanReaction.
    /// «Модель» (развёрнута на 180°: модели Quaternius смотрят в −Z): Animator (Normal) + HumanGhostIK + HumanAnimate,
    /// на 13 костях — зонды (слой RigProbe): коробки таза/груди/стоп, сфера головы, капсулы рук и ног — для контактов и мыши.
    /// v2 (Human_*) не трогается — для сравнения.</summary>
    public static class RigHumanV3Builder
    {
        const string Root = "Assets/Rig";
        const string ScenePath = "Assets/Scenes/RigTests.unity";
        public static readonly string[] Names = { "Peasant_Bearded", "Ranger_Male", "Ranger_Female" };

        class Part
        {
            public string bone, parent, child, shape; public float mass, radius, maxAngle, tone; public bool limb;
        }

        // массы как в v2 (RigHumanBuilder.Spec), ~70 кг; тонус — сопротивление сустава за время удара (кг·м²)
        static readonly Part[] Parts =
        {
            new Part { bone = "pelvis", child = "spine_01", mass = 12f, shape = "pelvis" },
            new Part { bone = "spine_02", parent = "pelvis", child = "neck_01", mass = 18f, shape = "chest", maxAngle = 30f, tone = 1.2f },
            new Part { bone = "Head", parent = "spine_02", mass = 5f, shape = "head", maxAngle = 40f, tone = 0.25f, limb = true },
            new Part { bone = "upperarm_l", parent = "spine_02", child = "lowerarm_l", mass = 2.5f, shape = "capsule", radius = 0.055f, maxAngle = 70f, tone = 0.15f, limb = true },
            new Part { bone = "lowerarm_l", parent = "upperarm_l", child = "hand_l", mass = 2f, shape = "capsule", radius = 0.045f, maxAngle = 60f, tone = 0.08f, limb = true },
            new Part { bone = "upperarm_r", parent = "spine_02", child = "lowerarm_r", mass = 2.5f, shape = "capsule", radius = 0.055f, maxAngle = 70f, tone = 0.15f, limb = true },
            new Part { bone = "lowerarm_r", parent = "upperarm_r", child = "hand_r", mass = 2f, shape = "capsule", radius = 0.045f, maxAngle = 60f, tone = 0.08f, limb = true },
            new Part { bone = "thigh_l", parent = "pelvis", child = "calf_l", mass = 8f, shape = "capsule", radius = 0.075f, maxAngle = 25f, tone = 1.5f },
            new Part { bone = "calf_l", parent = "thigh_l", child = "foot_l", mass = 4f, shape = "capsule", radius = 0.055f, maxAngle = 30f, tone = 1.2f },
            new Part { bone = "foot_l", parent = "calf_l", child = "ball_l", mass = 1.2f, shape = "foot", maxAngle = 20f, tone = 0.4f },
            new Part { bone = "thigh_r", parent = "pelvis", child = "calf_r", mass = 8f, shape = "capsule", radius = 0.075f, maxAngle = 25f, tone = 1.5f },
            new Part { bone = "calf_r", parent = "thigh_r", child = "foot_r", mass = 4f, shape = "capsule", radius = 0.055f, maxAngle = 30f, tone = 1.2f },
            new Part { bone = "foot_r", parent = "calf_r", child = "ball_r", mass = 1.2f, shape = "foot", maxAngle = 20f, tone = 0.4f },
        };

        [MenuItem("Fortress/Rig/Build Humans v3 (reaction on animation)")]
        public static void BuildAll()
        {
            NameLayers();
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(RigHumanBuilder.Controller) ?? RigHumanBuilder.BuildController();
            var npcAc = BuildNpcController();
            // записать контроллер до сборки префабов: иначе его переимпорт после SaveAssets оставлял префабам в памяти пустую ссылку
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(NpcController, ImportAssetOptions.ForceUpdate);
            npcAc = AssetDatabase.LoadAssetAtPath<AnimatorController>(NpcController);
            var made = new List<GameObject>();
            var npcs = new List<GameObject>();
            foreach (var n in Names)
            {
                var g = Build("Assets/Characters/Assembled/" + n + ".prefab", n, ac, false);
                if (g) made.Add(g);
                var c = Build("Assets/Characters/Assembled/" + n + ".prefab", n, npcAc, true);
                if (c) npcs.Add(c);
            }
            AssetDatabase.SaveAssets();
            foreach (var c in npcs) AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(c), ImportAssetOptions.ForceUpdate);
            WireBench(made.ToArray(), npcs.ToArray());
        }

        public const string NpcController = Root + "/Anim/HumanNPC.controller";
        const string Mix = "Assets/Characters/Mixamo/Anim/";

        static AnimationClip MixClip(string file)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Mix + file + ".fbx"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
            Debug.LogWarning("[HumanNPC] нет клипа Mixamo " + file);
            return null;
        }

        /// <summary>Контроллер NPC на клипах Mixamo (root motion): смесь Locomotion по скорости в осях тела (VelX вбок, VelZ вперёд, м/с)
        /// и отдельные клипы — старт, стоп, повороты на месте (180° вправо — зеркало левого), разворот на ходу, удары.
        /// Переходы делает HumanNPC (CrossFade), здесь только состояния и их темп.</summary>
        public static AnimatorController BuildNpcController()
        {
            EnsureFolder(Root + "/Anim");
            // пересобирать на месте (не удалять ассет: префабы, собранные раньше, теряли ссылку на контроллер)
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(NpcController);
            if (!ac) ac = AnimatorController.CreateAnimatorControllerAtPath(NpcController);
            else
            {
                foreach (var p in ac.parameters) ac.RemoveParameter(p);
                var old = ac.layers[0].stateMachine;
                foreach (var cs in old.states)
                {
                    if (cs.state.motion is BlendTree bt) AssetDatabase.RemoveObjectFromAsset(bt);
                    old.RemoveState(cs.state);
                }
                for (int i = ac.layers.Length - 1; i > 0; i--) ac.RemoveLayer(i);
            }
            ac.AddParameter("VelX", AnimatorControllerParameterType.Float);
            ac.AddParameter("VelZ", AnimatorControllerParameterType.Float);
            var loco = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "VelX";
            tree.blendParameterY = "VelZ";
            // точки смеси — реальная средняя скорость клипа в осях тела (у шагов вбок и назад тело развёрнуто к пути)
            void Child(string clip)
            {
                var c = MixClip(clip);
                if (!c) return;
                Vector3 v = c.averageSpeed;
                tree.AddChild(c, new Vector2(v.x, v.z));
            }
            Child("Idle_Breathing");
            Child("Walk");
            Child("Walk_Back");
            Child("Walk_Strafe_Left");
            Child("Walk_Strafe_Right");
            Child("Run");
            var sm = ac.layers[0].stateMachine;
            sm.defaultState = loco;
            void State(string name, string clip, float speed, bool mirror)
            {
                var c = MixClip(clip);
                if (!c) return;
                var st = sm.AddState(name);
                st.motion = c; st.speed = speed; st.mirror = mirror;
            }
            State("Walk_Start", "Walk_Start", 1.3f, false);
            State("Walk_Stop", "Walk_Stop", 1.15f, false);
            State("Turn_Left_90", "Turn_Left_90", 1.2f, false);
            State("Turn_Right_90", "Turn_Right_90", 1.2f, false);
            State("Turn_Left_180", "Turn_Left_180", 1.2f, false);
            State("Turn_Right_180", "Turn_Left_180", 1.2f, true);
            State("Walk_Turn_180_R", "Walk_Turn_180", 1f, false);
            State("Walk_Turn_180_L", "Walk_Turn_180", 1f, true);
            State("Hit_Front", "Hit_Front", 1f, false);
            State("Hit_Back", "Hit_Back", 1f, false);
            State("Hit_Left", "Hit_Left", 1f, false);
            State("Hit_Right", "Hit_Right", 1f, false);
            State("Idle_WeightShift", "Idle_WeightShift", 1f, false);
            // сон: Mixamo «Laying Idle» — лежит на кровати и дышит
            State("Sleep", "Sleep_Laying", 1f, false);
            // сиденье: сесть (корпус назад 0.34 м), сидит, встать
            State("Sit_Down", "Sit_Down", 1f, false);
            State("Sit_Idle", "Sit_Idle", 1f, false);
            State("Sit_Up", "Sit_Up", 1f, false);
            var layers = ac.layers;
            layers[0].iKPass = true;          // стопы на ступенях (HumanGhostIK)
            ac.layers = layers;
            EditorUtility.SetDirty(ac);
            return ac;
        }

        /// <summary>Подписать слои 10/11 (зонды и капсулы движения).</summary>
        public static void NameLayers()
        {
            RigHumanBuilder.NameLayers();
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            void Name(int i, string n)
            {
                var e = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(e.stringValue)) e.stringValue = n;
            }
            Name(RigLayers.Probe, "RigProbe");
            Name(RigLayers.Mover, "RigMover");
            tm.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Подключить префабы к стенду, если открыта сцена стенда (свою сцену автора не открываем и не трогаем).</summary>
        static void WireBench(GameObject[] prefabs, GameObject[] npcs)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) { Debug.Log("[HumanV3] стенд не открыт — префабы подключатся при следующей сборке в сцене стенда"); return; }
            var bench = Object.FindFirstObjectByType<RigTestBench>();
            if (!bench) return;
            bench.humanV3Prefabs = prefabs;
            bench.humanNPCPrefabs = npcs;
            EditorUtility.SetDirty(bench);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[HumanV3] стенд: подключено " + prefabs.Length + " префаба v3");
        }

        static void EnsureFolder(string f)
        {
            if (AssetDatabase.IsValidFolder(f)) return;
            EnsureFolder(Path.GetDirectoryName(f).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(f).Replace('\\', '/'), Path.GetFileName(f));
        }

        static Dictionary<string, Transform> Bones(GameObject g) =>
            g.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(x => x.Key, x => x.First());

        public static GameObject Build(string characterPath, string name, AnimatorController ac, bool npc)
        {
            EnsureFolder(Root + "/Prefabs");
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
            if (!src) { Debug.LogWarning("[HumanV3] нет персонажа " + characterPath); return null; }
            var root = new GameObject((npc ? "HumanNPC_" : "HumanV3_") + name);
            root.layer = RigLayers.Mover;
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.75f; cc.radius = 0.25f; cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.38f; cc.slopeLimit = 50f; cc.skinWidth = 0.03f; cc.minMoveDistance = 0f;
            var motor = root.AddComponent<HumanMotor>();

            // видимая модель: аниматор в режиме Normal (гладко), IK стоп, «анимация догоняет капсулу»
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Модель";
            model.transform.SetParent(root.transform, false);
            // UAL (игрок): клипы сохраняют исходный разворот Quaternius (лицом в −Z модели) — модель развёрнута на 180°.
            // Mixamo (NPC): клипы с ориентацией «по телу» ставят тело лицом в +Z модели — модель не разворачивается
            model.transform.localRotation = npc ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            var anim = model.GetComponent<Animator>();
            anim.runtimeAnimatorController = ac;
            anim.applyRootMotion = npc;
            anim.updateMode = AnimatorUpdateMode.Normal;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
            model.AddComponent<HumanGhostIK>();
            var animate = model.AddComponent<HumanAnimate>();
            if (npc)
            {
                // NPC: ход ведут клипы Mixamo (root motion), капсула идёт за корнем клипа
                motor.rootMotion = true;
                animate.npcMode = true;
                model.AddComponent<HumanNPC>();
            }

            // зонды на костях (мир: модель уже развёрнута, «вперёд» — +Z корня)
            var b = Bones(model);
            Vector3 fwd = npc ? -root.transform.forward : root.transform.forward;   // куда смотрит сетка в позе привязки
            var bones = new Transform[Parts.Length];
            var probes = new Collider[Parts.Length];
            for (int i = 0; i < Parts.Length; i++)
            {
                var s = Parts[i];
                var t = b[s.bone];
                bones[i] = t;
                t.gameObject.layer = RigLayers.Probe;
                Vector3 p0 = t.position;
                Vector3 p1 = s.child != null && b.ContainsKey(s.child) ? b[s.child].position : p0 + Vector3.up * 0.2f;
                switch (s.shape)
                {
                    case "pelvis":
                    {
                        float w = Vector3.Distance(b["thigh_l"].position, b["thigh_r"].position) + 0.14f;
                        probes[i] = WorldBox(t, (p0 + p1) * 0.5f, new Vector3(w, 0.22f, 0.22f));
                        break;
                    }
                    case "chest":
                    {
                        float w = Vector3.Distance(b["upperarm_l"].position, b["upperarm_r"].position) * 0.85f;
                        float h = Mathf.Abs(p1.y - p0.y) + 0.06f;
                        probes[i] = WorldBox(t, new Vector3(p0.x, (p0.y + p1.y) * 0.5f, p0.z), new Vector3(w, h, 0.24f));
                        break;
                    }
                    case "head":
                    {
                        var sc = t.gameObject.AddComponent<SphereCollider>();
                        sc.radius = 0.11f / t.lossyScale.x;
                        sc.center = t.InverseTransformPoint(p0 + Vector3.up * 0.09f + fwd * 0.02f);
                        probes[i] = sc;
                        break;
                    }
                    case "capsule":
                    {
                        if (s.child != null && s.child.StartsWith("hand")) p1 = p1 + (p1 - p0).normalized * 0.12f;   // кисть — в предплечье
                        probes[i] = Capsule(t, p0, p1, s.radius);
                        break;
                    }
                    case "foot":
                    {
                        string side = s.bone.Substring(4);
                        Vector3 toe = b.ContainsKey("ball_leaf" + side) ? b["ball_leaf" + side].position : p1;
                        Vector3 heel = new Vector3(p0.x, 0f, p0.z) - fwd * 0.05f;
                        Vector3 tip = new Vector3(toe.x, 0f, toe.z) + fwd * 0.02f;
                        probes[i] = WorldBox(t, (heel + tip) * 0.5f + Vector3.up * 0.045f, new Vector3(0.1f, 0.09f, Vector3.Distance(heel, tip)));
                        break;
                    }
                }
            }
            var re = root.AddComponent<HumanReaction>();
            re.bones = bones;
            re.probes = probes;
            re.parent = Parts.Select(p => p.parent == null ? -1 : System.Array.FindIndex(Parts, q => q.bone == p.parent)).ToArray();
            re.mass = Parts.Select(p => p.mass).ToArray();
            re.maxAngle = Parts.Select(p => p.maxAngle).ToArray();
            re.tone = Parts.Select(p => p.tone).ToArray();
            re.limb = Parts.Select(p => p.limb).ToArray();
            re.rootBone = b["root"];
            re.footL = System.Array.FindIndex(Parts, q => q.bone == "foot_l");
            re.footR = System.Array.FindIndex(Parts, q => q.bone == "foot_r");

            string path = $"{Root}/Prefabs/{(npc ? "HumanNPC_" : "HumanV3_")}{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log($"[HumanV3] собран {path}");
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
        static CapsuleCollider Capsule(Transform t, Vector3 p0, Vector3 p1, float r)
        {
            var cc = t.gameObject.AddComponent<CapsuleCollider>();
            Vector3 d = t.InverseTransformVector(p1 - p0);
            Vector3 a = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
            cc.direction = a.x >= a.y && a.x >= a.z ? 0 : (a.y >= a.z ? 1 : 2);
            float sc = t.lossyScale.x;
            cc.radius = r / sc;
            cc.height = d.magnitude + 2f * r / sc;
            cc.center = d * 0.5f;
            return cc;
        }
    }
}
