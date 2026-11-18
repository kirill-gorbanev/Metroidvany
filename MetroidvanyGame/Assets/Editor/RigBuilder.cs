using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fortress.Rig;
using Fortress.Rig.Tests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fortress.EditorTools
{
    /// <summary>Сборка «живого табурета» из готового табурета набора (сиденье + 4 ножки, детали модели уже раздельные)
    /// и тестовой сцены рига. Всё строится во временной сцене — открытая сцена не трогается.</summary>
    public static class RigBuilder
    {
        const string Root = "Assets/Rig";
        const string StoolSrc = "Assets/Kits/Prefabs/Props/Stool.prefab";
        public const string StoolPrefab = Root + "/Prefabs/LivingStool.prefab";
        public const string ScenePath = "Assets/Scenes/RigTests.unity";

        [MenuItem("Fortress/Rig/Build Living Stool + Test Scene")]
        public static void BuildAll()
        {
            var prev = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var stool = BuildStool();
                BuildBench(stool);
                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log($"[Rig] готово: {StoolPrefab}, {ScenePath}");
            }
            finally
            {
                if (prev.IsValid()) SceneManager.SetActiveScene(prev);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Fortress/Rig/Open Test Scene")]
        public static void OpenTestScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        static void Folders()
        {
            foreach (var f in new[] { Root, Root + "/Meshes", Root + "/Prefabs", Root + "/Materials" })
                if (!AssetDatabase.IsValidFolder(f))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(f).Replace('\\', '/'), Path.GetFileName(f));
        }

        class Part
        {
            public readonly List<int> tris = new List<int>();
            public readonly List<int> sub = new List<int>();
            public Bounds b;
            public bool init;
        }

        /// <summary>Табурет набора -> префаб LivingStool: тело-сиденье и 4 ножки на суставах с мышцами.
        /// Нижние распорки отброшены: ожившему табурету они связали бы ножки.</summary>
        public static GameObject BuildStool()
        {
            Folders();
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(StoolSrc);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            var mf = inst.GetComponentInChildren<MeshFilter>();
            var mats = mf.GetComponent<MeshRenderer>().sharedMaterials;
            var mesh = mf.sharedMesh;
            Matrix4x4 M = mf.transform.localToWorldMatrix;
            Vector3[] V = mesh.vertices, N = mesh.normals;
            Vector2[] UV = mesh.uv;
            Vector4[] TG = mesh.tangents;
            int n = V.Length;
            var W = new Vector3[n];
            var WN = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                W[i] = M.MultiplyPoint3x4(V[i]);
                WN[i] = N.Length == n ? M.MultiplyVector(N[i]).normalized : Vector3.up;
            }

            // связные детали модели (по общим вершинам)
            var par = Enumerable.Range(0, n).ToArray();
            int F(int x) { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; }
            void U(int a, int b) { par[F(a)] = F(b); }
            var seen = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < n; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(W[i].x * 10000f), Mathf.RoundToInt(W[i].y * 10000f), Mathf.RoundToInt(W[i].z * 10000f));
                if (seen.TryGetValue(k, out int j)) U(i, j); else seen[k] = i;
            }
            var subTris = new List<int[]>();
            for (int s = 0; s < mesh.subMeshCount; s++) subTris.Add(mesh.GetTriangles(s));
            foreach (var t in subTris)
                for (int i = 0; i < t.Length; i += 3) { U(t[i], t[i + 1]); U(t[i], t[i + 2]); }
            var parts = new Dictionary<int, Part>();
            for (int s = 0; s < subTris.Count; s++)
            {
                var t = subTris[s];
                for (int i = 0; i < t.Length; i += 3)
                {
                    int r = F(t[i]);
                    if (!parts.TryGetValue(r, out var p)) parts[r] = p = new Part();
                    p.tris.Add(t[i]); p.tris.Add(t[i + 1]); p.tris.Add(t[i + 2]);
                    p.sub.Add(s);
                    for (int q = 0; q < 3; q++)
                    {
                        if (!p.init) { p.b = new Bounds(W[t[i + q]], Vector3.zero); p.init = true; }
                        else p.b.Encapsulate(W[t[i + q]]);
                    }
                }
            }
            var legs = parts.Values.Where(p => p.b.size.y > 0.38f).ToList();
            var low = parts.Values.Where(p => p.b.size.y <= 0.38f && p.b.max.y < 0.3f).ToList();
            var seat = parts.Values.Except(legs).Except(low).ToList();
            Debug.Log($"[Rig] табурет: деталей {parts.Count}: ножек {legs.Count}, сиденье {seat.Count}, распорок отброшено {low.Count}");
            if (legs.Count < 3 || seat.Count == 0) { Object.DestroyImmediate(inst); throw new System.Exception("табурет не разобрался на детали"); }

            Mesh Make(string name, IEnumerable<Part> ps, Vector3 pivot, Quaternion rot)
            {
                var inv = Quaternion.Inverse(rot);
                var map = new Dictionary<int, int>();
                var vs = new List<Vector3>(); var ns = new List<Vector3>(); var us = new List<Vector2>(); var ts = new List<Vector4>();
                var subs = Enumerable.Range(0, mesh.subMeshCount).Select(_ => new List<int>()).ToArray();
                foreach (var p in ps)
                    for (int i = 0; i < p.tris.Count; i++)
                    {
                        int idx = p.tris[i];
                        if (!map.TryGetValue(idx, out int m))
                        {
                            m = vs.Count; map[idx] = m;
                            vs.Add(inv * (W[idx] - pivot));
                            ns.Add(inv * WN[idx]);
                            us.Add(UV.Length == n ? UV[idx] : Vector2.zero);
                            if (TG.Length == n)
                            {
                                Vector3 tv = inv * M.MultiplyVector(new Vector3(TG[idx].x, TG[idx].y, TG[idx].z)).normalized;
                                ts.Add(new Vector4(tv.x, tv.y, tv.z, TG[idx].w));
                            }
                        }
                        subs[p.sub[i / 3]].Add(m);
                    }
                var mm = new Mesh { name = name };
                mm.SetVertices(vs); mm.SetNormals(ns); mm.SetUVs(0, us);
                if (ts.Count == vs.Count) mm.SetTangents(ts);
                mm.subMeshCount = subs.Length;
                for (int s = 0; s < subs.Length; s++) mm.SetTriangles(subs[s], s);
                mm.RecalculateBounds();
                string path = $"{Root}/Meshes/{name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mm, path);
                return mm;
            }

            Vector3 Avg(Part p, System.Func<Vector3, bool> pick)
            {
                Vector3 s = Vector3.zero; int c = 0;
                foreach (int i in p.tris.Distinct()) if (pick(W[i])) { s += W[i]; c++; }
                return c > 0 ? s / c : p.b.center;
            }

            // ножки: сустав — верх ножки, стопа — низ; ось ноги вдоль локального −Y
            var legInfo = legs.Select(p =>
            {
                Vector3 hip = Avg(p, v => v.y > p.b.max.y - 0.03f);
                Vector3 foot = Avg(p, v => v.y < p.b.min.y + 0.03f);
                return (p, hip, foot, ang: Mathf.Atan2(foot.z, foot.x));
            }).OrderBy(l => l.ang).ToList();
            float hipY = legInfo.Average(l => l.hip.y);
            var seatPivot = new Vector3(0f, hipY, 0f);

            var grip = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Root + "/Materials/RigGrip.asset");
            if (!grip)
            {
                grip = new PhysicsMaterial("RigGrip")
                {
                    dynamicFriction = 0.9f, staticFriction = 1.1f, bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Minimum,
                };
                AssetDatabase.CreateAsset(grip, Root + "/Materials/RigGrip.asset");
            }

            var root = new GameObject("LivingStool");
            root.transform.position = seatPivot;
            root.AddComponent<MeshFilter>().sharedMesh = Make("LivingStool_Seat", seat, seatPivot, Quaternion.identity);
            root.AddComponent<MeshRenderer>().sharedMaterials = mats;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 2.2f; rb.linearDamping = 0.05f; rb.angularDamping = 0.4f;
            var disc = seat.OrderByDescending(p => p.b.size.x * p.b.size.z).First().b;
            var sc = root.AddComponent<BoxCollider>();
            sc.center = disc.center - seatPivot;
            sc.size = new Vector3(disc.size.x * 0.9f, Mathf.Max(0.04f, disc.size.y), disc.size.z * 0.9f);
            var topMark = new GameObject("SeatTop").transform;
            topMark.SetParent(root.transform, false);
            topMark.localPosition = new Vector3(0f, disc.max.y - seatPivot.y, 0f);

            var gaitLegs = new List<LegGait.Leg>();
            string[] names = { "Восток", "Север", "Запад", "Юг", "5", "6", "7", "8" };
            for (int i = 0; i < legInfo.Count; i++)
            {
                var (p, hip, foot, _) = legInfo[i];
                var rot = Quaternion.FromToRotation(Vector3.down, (foot - hip).normalized);
                float len = Vector3.Distance(hip, foot);
                var g = new GameObject("Ножка_" + names[i]);
                g.transform.SetParent(root.transform, false);
                g.transform.SetPositionAndRotation(hip, rot);
                g.AddComponent<MeshFilter>().sharedMesh = Make("LivingStool_Leg" + i, new[] { p }, hip, rot);
                g.AddComponent<MeshRenderer>().sharedMaterials = mats;
                var lrb = g.AddComponent<Rigidbody>();
                lrb.mass = 0.3f; lrb.angularDamping = 0.5f;
                var cap = g.AddComponent<CapsuleCollider>();
                cap.direction = 1; cap.center = new Vector3(0f, -len / 2f, 0f); cap.height = len; cap.radius = 0.028f;
                var sph = g.AddComponent<SphereCollider>();
                sph.center = new Vector3(0f, -len + 0.03f, 0f); sph.radius = 0.032f; sph.sharedMaterial = grip;
                var j = g.AddComponent<ConfigurableJoint>();
                j.connectedBody = rb;
                j.anchor = Vector3.zero;
                j.autoConfigureConnectedAnchor = true;
                j.axis = Vector3.up; j.secondaryAxis = Vector3.right;
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
                j.angularXMotion = ConfigurableJointMotion.Locked;
                j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
                j.angularYLimit = new SoftJointLimit { limit = 55f };
                j.angularZLimit = new SoftJointLimit { limit = 55f };
                j.enablePreprocessing = false;
                var mus = g.AddComponent<RigMuscle>();
                mus.spring = 180f; mus.damper = 5f; mus.maxForce = 500f;
                gaitLegs.Add(new LegGait.Leg { name = g.name, hip = mus, footLocal = new Vector3(0f, -len, 0f), group = i % 2 });
            }
            var body = root.AddComponent<RigBody>();
            body.title = "Табурет";
            root.AddComponent<LegGait>().legs = gaitLegs.ToArray();

            AssetDatabase.DeleteAsset(StoolPrefab);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, StoolPrefab);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(inst);
            return prefab;
        }

        static GameObject Pf(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(name);

        static void Strip(GameObject g)
        {
            foreach (var c in g.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        /// <summary>Арена из кладки набора (пол 20×20 м, стены), свет, камера стенда и сам стенд.</summary>
        static void BuildBench(GameObject stool)
        {
            var sun = new GameObject("Солнце").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.25f; sun.color = new Color(1f, 0.95f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            var cg = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = cg.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
            cam.nearClipPlane = 0.03f; cam.farClipPlane = 300f; cam.fieldOfView = 55f;
            cg.AddComponent<AudioListener>();
            var bcam = cg.AddComponent<BenchCamera>();

            var arena = new GameObject("Арена").transform;
            var floorPf = Pf("Assets/Kits/Prefabs/Village/Floor_UnevenBrick.prefab");
            for (int x = -5; x < 5; x++)
                for (int z = -5; z < 5; z++)
                {
                    var f = (GameObject)PrefabUtility.InstantiatePrefab(floorPf, arena);
                    f.transform.position = new Vector3(x * 2 + 1, 0f, z * 2 + 1);
                    Strip(f);
                }
            var ground = new GameObject("Пол (коллайдер)").AddComponent<BoxCollider>();
            ground.transform.SetParent(arena);
            ground.center = new Vector3(0f, -0.1f, 0f); ground.size = new Vector3(20f, 0.2f, 20f);
            var wallPf = Pf("Assets/Kits/Prefabs/Village/Wall_UnevenBrick_Straight.prefab");
            for (int side = 0; side < 4; side++)
            {
                float yaw = side * 90f;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                for (int i = 0; i < 10; i++)
                {
                    var w = (GameObject)PrefabUtility.InstantiatePrefab(wallPf, arena);
                    w.transform.SetPositionAndRotation(rot * new Vector3(-9f + i * 2f, 0f, -10f), rot);
                    Strip(w);
                }
                var wc = new GameObject("Стена (коллайдер)").AddComponent<BoxCollider>();
                wc.transform.SetParent(arena);
                wc.transform.rotation = rot;
                wc.transform.position = rot * new Vector3(0f, 1.5f, -10.15f);
                wc.size = new Vector3(20.6f, 3f, 0.4f);
            }

            var ballMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Ball.mat");
            if (!ballMat)
            {
                ballMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Ball" };
                ballMat.SetColor("_BaseColor", new Color(0.75f, 0.2f, 0.15f));
                ballMat.SetFloat("_Smoothness", 0.35f);
                AssetDatabase.CreateAsset(ballMat, Root + "/Materials/Ball.mat");
            }

            var bench = new GameObject("Стенд рига").AddComponent<RigTestBench>();
            bench.stoolPrefab = stool;
            bench.cratePrefab = Pf("Assets/Kits/Prefabs/Props/Crate_Wooden.prefab");
            bench.barrelPrefab = Pf("Assets/Kits/Prefabs/Props/Barrel.prefab");
            bench.bucketPrefab = Pf("Assets/Kits/Prefabs/Props/Bucket_Wooden_1.prefab");
            bench.mugPrefab = Pf("Assets/Kits/Prefabs/Props/Mug.prefab");
            bench.floorMat = floorPf.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            bench.woodMat = stool.GetComponent<MeshRenderer>().sharedMaterial;
            bench.ballMat = ballMat;
            bench.cam = bcam;
            bench.humanPrefabs = new[] { "Peasant_Bearded", "Ranger_Male", "Ranger_Female" }
                .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Human_" + n + ".prefab")).Where(g => g).ToArray();
            AssignEnvironment(bench);
            Showcase(bench);
        }

        /// <summary>Открыть сцену стенда, подключить куски набора для ситуаций «окружение» и поставить витрину людей; сохранить.</summary>
        [MenuItem("Fortress/Rig/Update Test Scene (environment + showcase)")]
        public static void UpdateTestScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var bench = Object.FindFirstObjectByType<RigTestBench>();
            if (!bench) { Debug.LogError("[Rig] нет стенда в сцене"); return; }
            bench.humanPrefabs = new[] { "Peasant_Bearded", "Ranger_Male", "Ranger_Female" }
                .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Human_" + n + ".prefab")).Where(g => g).ToArray();
            AssignEnvironment(bench);
            Showcase(bench);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Rig] сцена стенда обновлена: окружение + витрина людей");
        }

        static void AssignEnvironment(RigTestBench b)
        {
            const string V = "Assets/Kits/Prefabs/Village/", P = "Assets/Kits/Prefabs/Props/";
            b.stairsPrefab = Pf(V + "Stairs_Exterior_Straight.prefab");
            b.platformPrefab = Pf(V + "Stairs_Exterior_Platform.prefab");
            b.doorWallPrefab = Pf(V + "Wall_UnevenBrick_Door_Flat.prefab");
            b.doorFramePrefab = Pf(V + "DoorFrame_Flat_Brick.prefab");
            b.doorPrefab = Pf(V + "Door_1_Flat.prefab");
            b.wallPrefab = Pf(V + "Wall_UnevenBrick_Straight.prefab");
            b.seatPrefab = Pf(P + "Bench.prefab");
            b.tablePrefab = Pf(P + "Table_Large.prefab");
            b.appleCratePrefab = Pf(P + "FarmCrate_Apple.prefab");
            b.metalCratePrefab = Pf(P + "Crate_Metal.prefab");
            EditorUtility.SetDirty(b);
        }

        /// <summary>Витрина: трое людей стоят в сцене стенда в редакторе (видно без Play). В Play стенд её прячет и ставит людей сам.</summary>
        static void Showcase(RigTestBench b)
        {
            var old = GameObject.Find("Люди (витрина)");
            if (old) Object.DestroyImmediate(old);
            if (b.humanPrefabs == null || b.humanPrefabs.Length == 0) return;
            var root = new GameObject("Люди (витрина)");
            root.transform.position = new Vector3(0f, 0f, 4f);
            for (int i = 0; i < b.humanPrefabs.Length; i++)
            {
                var g = (GameObject)PrefabUtility.InstantiatePrefab(b.humanPrefabs[i], root.transform);
                g.transform.localPosition = new Vector3(-1.5f + 1.5f * i, 0f, 0f);
                g.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
        }
    }
}
