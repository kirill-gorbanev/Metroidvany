using Fortress.Hero.Proto;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Fortress.EditorTools.HeroProtoBuilder;

namespace Fortress.EditorTools
{
    /// <summary>Арена 2 — двухъярусная полоса: выбор пути на трёх высотах. Логика прототипа не меняется (точка отката — тег proto-surfaceY).
    ///
    ///   ВЕРХ  1.8 м — лестница вверх (слева) → галерея → мост над основной дорогой → лестница вниз к финишу.
    ///   ОСНОВА 0.6 м — основная дорога посередине: обход двух стенок, проход ПОД мостом, мимо лестницы к финишу.
    ///   ТОННЕЛЬ 0 м — справа: съезд вниз → низкий тоннель под перекрытием (просвет ~0.5 м) → подъём к финишу.
    ///   Перила лестницы и галереи: стойки через 1 м, поручень на 0.75 м над ступенью — под ним можно пролезть.
    ///
    /// Почему основа на 0.6, а не на 0: поверхность у прототипа — рельеф + бесконечный пол y = 0, ниже нуля ямы нет.
    /// Корни: «Арена: стены» (препятствия по XZ), «Арена: поверхность» (высота точки), «Арена: декор» (ни то ни другое:
    /// перила и разметка — иначе поручень стал бы «полом» сверху).
    /// Меню Fortress → Hero → Build Arena 2 (ярусы).</summary>
    public static class HeroArenaBuilder
    {
        public const string ScenePath = "Assets/Scenes/HeroArena2.unity";
        const float TUN = 0f, MAIN = 0.6f, UP = 1.8f;
        const float W = 12f, D = 15.5f;            // внутренний размер арены (x, z)
        const float WallTop = MAIN + 0.45f;        // стены — как на первой трассе: 45 см над основой
        const float RailH = 0.75f;                 // поручень над поверхностью

        static readonly Color WallC = new Color(0.55f, 0.45f, 0.36f), MainC = new Color(0.36f, 0.38f, 0.41f);
        static readonly Color StepA = new Color(0.44f, 0.46f, 0.50f), StepB = new Color(0.50f, 0.52f, 0.56f);
        static readonly Color DeckC = new Color(0.47f, 0.42f, 0.36f), TunC = new Color(0.52f, 0.50f, 0.44f);
        static readonly Color RailC = new Color(0.78f, 0.72f, 0.58f), LineC = new Color(0.42f, 0.44f, 0.47f);
        static readonly Color GroundC = new Color(0.20f, 0.21f, 0.22f), FinishC = new Color(0.35f, 0.7f, 0.4f);

        static Transform walls, surf, decor;

        [MenuItem("Fortress/Hero/Build Arena 2 (ярусы)")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Hero] арена: выйди из Play"); return; }
            var open = SceneManager.GetSceneByPath(ScenePath);
            Scene scene;
            if (open.IsValid() && open.isLoaded)
            {
                scene = open;
                foreach (var g in scene.GetRootGameObjects()) Object.DestroyImmediate(g);
                SceneManager.SetActiveScene(scene);
            }
            else
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Fill();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Hero] арена 2: " + ScenePath);
        }

        // ---------- кирпичи ----------
        /// <summary>Коробка по габаритам: x0..x1, z0..z1, y0..y1.</summary>
        static Transform Box(string n, float x0, float x1, float y0, float y1, float z0, float z1, Color c, Transform root)
            => Cube(n, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, y1 - y0, z1 - z0), c, root);

        /// <summary>Склон-клин на прямоугольнике: основание y0, подъём h в сторону rise (мировое направление по X или Z).</summary>
        static void Wedge(string n, float x0, float x1, float z0, float z1, float y0, float h, Vector3 rise, Color c, Transform root)
        {
            bool alongX = Mathf.Abs(rise.x) > 0.5f;
            var g = new GameObject(n);
            g.transform.SetParent(root, false);
            g.transform.SetPositionAndRotation(new Vector3((x0 + x1) * 0.5f, y0, (z0 + z1) * 0.5f), Quaternion.LookRotation(rise, Vector3.up));
            g.transform.localScale = new Vector3(alongX ? z1 - z0 : x1 - x0, h, alongX ? x1 - x0 : z1 - z0);
            g.AddComponent<MeshFilter>().sharedMesh = HeroProtoBuilder.Wedge();
            g.AddComponent<MeshRenderer>().sharedMaterial = Mat(n, c);
        }

        /// <summary>Лестница вдоль Z: n ступеней от z0 с шагом run, высоты h(i) — сплошные блоки от земли.</summary>
        static void StairsZ(string n, float x0, float x1, float z0, float run, float[] hs, Transform root)
        {
            float dir = Mathf.Sign(run);
            for (int i = 0; i < hs.Length; i++)
            {
                float a = z0 + run * i, b = a + run;
                Box(n + " " + (i + 1), x0, x1, 0f, hs[i], Mathf.Min(a, b), Mathf.Max(a, b), i % 2 == 0 ? StepA : StepB, root);
            }
        }

        /// <summary>Поручень — тонкий брус от точки a до b (декор).</summary>
        static void Rail(string n, Vector3 a, Vector3 b)
        {
            var t = Cube(n, (a + b) * 0.5f, new Vector3(0.05f, 0.05f, (b - a).magnitude), RailC, decor);
            t.rotation = Quaternion.LookRotation(b - a, Vector3.up);
        }

        /// <summary>Стойка перил от y0 до y1. asWall — стойка-препятствие (между ними пролезают), иначе декор.</summary>
        static void Post(string n, float x, float z, float y0, float y1, bool asWall)
            => Box(n, x - 0.03f, x + 0.03f, y0, y1, z - 0.03f, z + 0.03f, RailC, asWall ? walls : decor);

        static float StairAUp(float z) => z <= 2f ? MAIN : z >= 5f ? UP : MAIN + 0.12f * (Mathf.Floor((z - 2f) / 0.3f) + 1f);

        // ---------- арена ----------
        static void Fill()
        {
            walls = new GameObject("Арена: стены").transform;
            surf = new GameObject("Арена: поверхность").transform;
            decor = new GameObject("Арена: декор (не опора)").transform;

            // земля = уровень тоннеля (y 0); видна в тоннеле и за ареной
            Box("Земля", -2f, W + 2f, -0.05f, 0f, -2f, D + 2f, GroundC, decor);

            // ---- ОСНОВА 0.6 м: всё, кроме полосы тоннеля справа (x 8.65–12, z 2–12.4)
            Box("Основа", 0f, 8.65f, 0f, MAIN, 0f, D, MainC, surf);
            Box("Основа (старт справа)", 8.65f, W, 0f, MAIN, 0f, 2f, MainC, surf);
            Box("Основа (финиш справа)", 8.65f, W, 0f, MAIN, 12.4f, D, MainC, surf);
            for (int i = 1; i < 8; i++) Box("Разметка X" + i, i - 0.005f, i + 0.005f, MAIN, MAIN + 0.002f, 0f, D, LineC, decor);
            for (int i = 1; i < 16; i++) Box("Разметка Z" + i, 0f, 8.35f, MAIN, MAIN + 0.002f, i - 0.005f, i + 0.005f, LineC, decor);

            // ---- СТЕНЫ (препятствия по XZ): периметр, стенка основа|тоннель, две стенки-обхода на основе
            Box("Стена юг", -0.3f, W + 0.3f, 0f, WallTop, -0.3f, 0f, WallC, walls);
            Box("Стена север", -0.3f, W + 0.3f, 0f, WallTop, D, D + 0.3f, WallC, walls);
            Box("Стена запад", -0.3f, 0f, 0f, WallTop, 0f, D, WallC, walls);
            Box("Стена восток", W, W + 0.3f, 0f, WallTop, 0f, D, WallC, walls);
            Box("Стена тоннеля", 8.35f, 8.65f, 0f, WallTop, 2f, 12.4f, WallC, walls);
            Box("Обход 1", 4.3f, 6.4f, MAIN, WallTop, 4f, 4.4f, WallC, walls);   // щель у лестницы 0.8 м (было 0.4 — ровно тело, не пройти)
            Box("Обход 2", 5.6f, 8.35f, MAIN, WallTop, 6.6f, 7.0f, WallC, walls);

            // ---- ТОННЕЛЬ 0 м (справа): съезд вниз → под перекрытием → подъём
            Wedge("Съезд в тоннель", 8.65f, W, 2f, 4.4f, TUN, MAIN - TUN, Vector3.back, TunC, surf);   // высокий край — у старта
            Box("Перекрытие тоннеля", 8.65f, W, MAIN - 0.08f, MAIN, 5f, 9f, MainC, surf);               // просвет 0.52 м
            Wedge("Подъём из тоннеля", 8.65f, W, 9.6f, 12.4f, TUN, MAIN - TUN, Vector3.forward, TunC, surf);

            // ---- ВЕРХ 1.8 м (слева): лестница вверх → галерея → мост над основой → лестница вниз к финишу
            var up = new float[10]; for (int i = 0; i < 10; i++) up[i] = MAIN + 0.12f * (i + 1);
            StairsZ("Лестница вверх", 0f, 3.5f, 2f, 0.3f, up, surf);
            Box("Галерея", 0f, 3.5f, 0f, UP, 5f, 11f, DeckC, surf);
            Box("Мост", 3.5f, 8.35f, UP - 0.1f, UP, 9.5f, 11f, DeckC, surf);
            var down = new float[9]; for (int i = 0; i < 9; i++) down[i] = UP - 0.12f * (i + 1);
            StairsZ("Лестница вниз", 5f, 7f, 11f, 0.3f, down, surf);

            // ---- ПЕРИЛА. Стойки вдоль лестницы и галереи — препятствия (между ними пролезают), поручень — декор
            for (int z = 2; z <= 9; z++)
                Post("Стойка", 3.47f, z, MAIN, StairAUp(z) + RailH, true);
            Rail("Поручень лестницы", new Vector3(3.47f, StairAUp(2.01f) + RailH, 2f), new Vector3(3.47f, UP + RailH, 5f));
            Rail("Поручень галереи", new Vector3(3.47f, UP + RailH, 5f), new Vector3(3.47f, UP + RailH, 9.5f));
            Rail("Поручень галереи север", new Vector3(0f, UP + RailH, 10.97f), new Vector3(3.5f, UP + RailH, 10.97f));
            // мост: стойки на настиле — декор (под мостом проходит основа)
            Rail("Поручень моста юг", new Vector3(3.5f, UP + RailH, 9.53f), new Vector3(8.35f, UP + RailH, 9.53f));
            Rail("Поручень моста север", new Vector3(3.5f, UP + RailH, 10.97f), new Vector3(5f, UP + RailH, 10.97f));
            Rail("Поручень моста север", new Vector3(7f, UP + RailH, 10.97f), new Vector3(8.35f, UP + RailH, 10.97f));
            for (float x = 4f; x < 8.4f; x += 1f) { Post("Стойка моста", x, 9.53f, UP, UP + RailH, false); }
            foreach (float x in new[] { 4f, 8f }) Post("Стойка моста", x, 10.97f, UP, UP + RailH, false);
            // лестница вниз: перила с двух сторон (декор)
            foreach (float x in new[] { 5.03f, 6.97f })
            {
                Rail("Поручень спуска", new Vector3(x, UP + RailH, 11f), new Vector3(x, down[8] + RailH, 13.7f));
                for (int i = 0; i < 9; i += 2) Post("Стойка спуска", x, 11.15f + 0.3f * i, down[i], down[i] + RailH, false);
            }

            // ---- финиш
            Box("Финиш", 2f, 10f, MAIN, MAIN + 0.004f, 14f, 15.4f, FinishC, decor);

            // ---- свет
            var sun = new GameObject("Свет").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.47f);

            PlaceHero(new Vector3(6f, 0f, 0.9f));
        }

        /// <summary>Герой — тот же префаб прототипа (герой + камера), на старте лицом на север, камера за спиной.</summary>
        static void PlaceHero(Vector3 start)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Hero/HeroProto.prefab");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(rig, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var proto = rig.GetComponentInChildren<HeroProto>(true);
            var body = proto.bodyCube;
            // материалы кубиков жили в сцене прототипа и в префаб не попали (розовые) — заново, цвета как на первой трассе
            void Paint(Transform t, Color c) { if (t && t.GetComponent<Renderer>()) t.GetComponent<Renderer>().sharedMaterial = Mat(t.name, c); }
            Paint(body, new Color(0.45f, 0.55f, 0.75f));
            Paint(proto.shoulderCube, new Color(0.95f, 0.85f, 0.55f));
            Paint(proto.elbowCube, new Color(0.95f, 0.65f, 0.35f));
            Paint(proto.handCube, new Color(0.95f, 0.45f, 0.35f));
            Vector3 d = start - new Vector3(body.position.x, 0f, body.position.z);
            rig.transform.position += d;
            body.rotation = Quaternion.identity;                          // лицом на север (+Z)
            proto.obstaclesRoot = walls;
            proto.surfaceRoot = surf;
            proto.showHints = true;
            // плечо — справа от тела
            proto.shoulderCube.position = body.position + body.right * proto.shoulderSide + Vector3.up * 0.14f;
            var orbit = rig.GetComponentInChildren<ProtoOrbitCamera>(true);
            if (orbit)
            {
                orbit.target = body; orbit.yaw = 0f; orbit.startBehindBody = true;
                var rot = Quaternion.Euler(orbit.pitch, orbit.yaw, 0f);
                Vector3 focus = body.position + Vector3.up * (MAIN + orbit.lookHeight);
                orbit.transform.SetPositionAndRotation(focus - rot * Vector3.forward * orbit.distance, rot);
            }
        }
    }
}
