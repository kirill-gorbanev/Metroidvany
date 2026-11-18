using Fortress.Hero;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fortress.EditorTools
{
    /// <summary>Герой: префаб из Hero.fbx (blender/hero/build_hero.py) + стенд HeroTest.
    /// Корень — CharacterController + HeroController + HeroOutfit, внутри «Модель» (FBX, аниматор выключен) с HeroBody.
    /// Модель разворачивается так, чтобы лицо смотрело в +Z корня.</summary>
    public static class HeroBuilder
    {
        public const string Fbx = "Assets/Characters/Hero/Hero.fbx";
        public const string Prefab = "Assets/Characters/Hero/Hero.prefab";
        public const string ScenePath = "Assets/Scenes/HeroTest.unity";

        [MenuItem("Fortress/Hero/Build Hero Prefab + Test Scene")]
        public static void BuildAll()
        {
            var prefab = BuildPrefab();
            if (!prefab) return;
            var prev = SceneManager.GetActiveScene();
            var open = SceneManager.GetSceneByPath(ScenePath);
            if (open.IsValid() && open.isLoaded)
            {
                // стенд уже открыт — пересобрать на месте (поверх открытой сцены Unity молча не сохраняет)
                foreach (var g in open.GetRootGameObjects()) Object.DestroyImmediate(g);
                SceneManager.SetActiveScene(open);
                BuildBench(prefab);
                EditorSceneManager.SaveScene(open);
                if (prev.IsValid() && prev != open) SceneManager.SetActiveScene(prev);
                Debug.Log($"[Hero] готово (на месте): {Prefab}, {ScenePath}");
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                BuildBench(prefab);
                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log($"[Hero] готово: {Prefab}, {ScenePath}");
            }
            finally
            {
                if (prev.IsValid()) SceneManager.SetActiveScene(prev);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Fortress/Hero/Open Test Scene")]
        public static void OpenTestScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; }
            return null;
        }

        public static GameObject BuildPrefab()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (!fbx) { Debug.LogError("[Hero] нет " + Fbx + " — собери blender/hero/build_hero.py"); return null; }
            var root = new GameObject("Hero");
            try
            {
                var cc = root.AddComponent<CharacterController>();
                cc.height = 0.9f; cc.radius = 0.28f; cc.center = new Vector3(0f, 0.45f, 0f);
                cc.stepOffset = 0.2f; cc.slopeLimit = 50f; cc.skinWidth = 0.02f; cc.minMoveDistance = 0f;
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
                model.name = "Модель";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                var anim = model.GetComponent<Animator>();
                if (anim) anim.enabled = false;                       // поза — процедурная (HeroBody)
                // вперёд — перпендикуляр к линии плеч (глаз смещён вбок — по нему нельзя)
                var shL = FindDeep(model.transform, "upperarm_l");
                var shR = FindDeep(model.transform, "upperarm_r");
                if (shL && shR)
                {
                    Vector3 f = Vector3.Cross(Vector3.ProjectOnPlane(shR.position - shL.position, Vector3.up), Vector3.up);
                    // Cross(right, up) = (1,0,0)×(0,1,0) = +Z — лицо
                    if (f.sqrMagnitude > 1e-6f)
                        model.transform.rotation = Quaternion.FromToRotation(f.normalized, Vector3.forward) * model.transform.rotation;
                }
                foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
                var body = model.AddComponent<HeroBody>();
                body.root = root.transform;
                var ctl = root.AddComponent<HeroController>();
                ctl.body = body;
                var outfit = root.AddComponent<HeroOutfit>();
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name == "Hero_Body" && r is SkinnedMeshRenderer s) outfit.bodyRenderer = s;
                    else if (r.name.StartsWith("Outfit_")) outfit.outfitParts.Add(r);
                }
                var saved = PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                return saved;
            }
            finally { Object.DestroyImmediate(root); }
        }

        static GameObject Box(string name, Vector3 pos, Vector3 size, Color col, Transform parent, float yaw = 0f, float pitch = 0f)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, 0f));
            g.transform.localScale = size;
            var r = g.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh)
            {
                var m = new Material(sh) { name = name };
                m.SetColor("_BaseColor", col);
                r.sharedMaterial = m;
            }
            return g;
        }

        static void Prop(string path, string name, Vector3 pos, float mass, Transform parent)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject g;
            if (src) g = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            else { g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.transform.SetParent(parent, false); g.transform.localScale = Vector3.one * 0.5f; }
            g.name = name;
            // коллайдер: невыпуклые меши заменить коробкой по габаритам
            foreach (var mc in g.GetComponentsInChildren<MeshCollider>(true)) if (!mc.convex) Object.DestroyImmediate(mc);
            if (!g.GetComponentInChildren<Collider>())
            {
                var rs = g.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds(g.transform.position, Vector3.one * 0.5f);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var bc = g.AddComponent<BoxCollider>();
                bc.center = g.transform.InverseTransformPoint(b.center);
                bc.size = g.transform.InverseTransformVector(b.size);
                bc.size = new Vector3(Mathf.Abs(bc.size.x), Mathf.Abs(bc.size.y), Mathf.Abs(bc.size.z));
            }
            var rb = g.GetComponent<Rigidbody>(); if (!rb) rb = g.AddComponent<Rigidbody>();
            rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // поставить на пол: низ коллайдера на pos.y
            g.transform.position = pos;
            Physics.SyncTransforms();
            float bottom = g.GetComponentInChildren<Collider>().bounds.min.y;
            g.transform.position += Vector3.up * (pos.y - bottom + 0.005f);
        }

        static void BuildBench(GameObject prefab)
        {
            var env = new GameObject("Стенд").transform;
            var stone = new Color(0.42f, 0.40f, 0.37f);
            var wood = new Color(0.45f, 0.31f, 0.20f);
            Box("Пол", new Vector3(0f, -0.1f, 0f), new Vector3(16f, 0.2f, 16f), new Color(0.30f, 0.29f, 0.27f), env);
            Box("Ступень 12 см", new Vector3(0f, 0.06f, 2.2f), new Vector3(1.6f, 0.12f, 1.0f), stone, env);
            Box("Ступень 25 см", new Vector3(0f, 0.125f, 3.2f), new Vector3(1.6f, 0.25f, 1.0f), stone, env);
            Box("Пандус", new Vector3(2.6f, 0.15f, 1.5f), new Vector3(1.2f, 0.08f, 2.4f), stone, env, 0f, -8f);
            // стол: под ним только ползком (просвет ~0.6 м)
            var table = new GameObject("Стол (проползти)").transform; table.SetParent(env, false);
            Box("Столешница", new Vector3(-2.6f, 0.62f, 2.0f), new Vector3(1.6f, 0.06f, 0.9f), wood, table);
            foreach (var (x, z) in new[] { (-3.3f, 1.6f), (-1.9f, 1.6f), (-3.3f, 2.4f), (-1.9f, 2.4f) })
                Box("Ножка", new Vector3(x, 0.3f, z), new Vector3(0.07f, 0.6f, 0.07f), wood, table);
            Box("Ящик", new Vector3(1.4f, 0.3f, -1.6f), new Vector3(0.6f, 0.6f, 0.6f), wood, env, 20f);
            Box("Стена", new Vector3(4.5f, 1.0f, 0f), new Vector3(0.3f, 2.0f, 6f), stone, env);
            Box("Брус у стены (хвататься)", new Vector3(4.25f, 0.45f, 0f), new Vector3(0.1f, 0.06f, 3f), wood, env);

            // уступы: 40 см — залезть из сгиба руки, 60 см — «размер человека не даст»
            Box("Уступ 40 см (залезть)", new Vector3(2.4f, 0.20f, -1.4f), new Vector3(1.4f, 0.40f, 1.0f), stone, env);
            Box("Уступ 60 см (не залезть)", new Vector3(-2.4f, 0.30f, -1.6f), new Vector3(1.4f, 0.60f, 1.0f), stone, env);
            // предметы с физикой: лёгкое ведро (взять/бросить/на голову), ящик 15 кг (толкнуть/зацепить плечом), тяжёлый 70 кг
            Prop("Assets/Kits/Prefabs/Props/Bucket_Wooden_1.prefab", "Ведро 2 кг", new Vector3(-0.55f, 0f, 0.65f), 2f, env);
            Prop("Assets/Kits/Prefabs/Props/Crate_Wooden.prefab", "Ящик 15 кг", new Vector3(0.9f, 0f, 1.3f), 15f, env);
            Prop("Assets/Kits/Prefabs/Props/Crate_Metal.prefab", "Ящик 70 кг", new Vector3(-1.3f, 0f, 1.9f), 70f, env);

            var sun = new GameObject("Свет").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.2f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.41f, 0.40f);

            var hero = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            hero.transform.position = Vector3.zero;

            var camGo = new GameObject("Камера");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            var hc = camGo.AddComponent<HeroCamera>();
            hc.target = hero.transform;
            hero.GetComponent<HeroController>().cam = cam;
            camGo.transform.position = new Vector3(1.2f, 1.6f, -2.2f);
            camGo.transform.LookAt(new Vector3(0f, 0.5f, 0f));
        }
    }
}
