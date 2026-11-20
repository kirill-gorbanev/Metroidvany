using Fortress.Hero.Proto;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fortress.EditorTools
{
    /// <summary>Стенд прототипа движения одной рукой: только пол и кубики (тело, плечо, локоть, кисть), без физики и анимации.
    /// Меню Fortress → Hero → Build Proto Scene (кубики).</summary>
    public static class HeroProtoBuilder
    {
        public const string ScenePath = "Assets/Scenes/HeroProto.unity";

        [MenuItem("Fortress/Hero/Build Proto Scene (кубики)")]
        public static void Build()
        {
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
            Debug.Log("[Hero] прототип: " + ScenePath);
        }

        internal static Material Mat(string n, Color c)
        {
            return Fortress.Rendering.FortRender.NewLit(c, 0.25f, n);
        }

        internal static Transform Cube(string n, Vector3 pos, Vector3 size, Color c, Transform parent = null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = n;
            Object.DestroyImmediate(g.GetComponent<Collider>());      // без физики
            if (parent) g.transform.SetParent(parent, false);
            g.transform.position = pos; g.transform.localScale = size;
            g.GetComponent<MeshRenderer>().sharedMaterial = Mat(n, c);
            return g.transform;
        }

        const float Cell = 0.6f;
        static readonly string[] CourseMap =
        {
            "####################################",
            "###############################FFFF#",
            "############.................##FFFF#",
            "############..o...o...o...o....FFFF#",
            "############......o...o...o..##FFFF#",
            "############....o...o...o....##FFFF#",
            "############....o...o...o....#######",
            "############..o...o...o...o..#######",
            "############......o...o...o..#######",
            "##########......o...o...o....#######",
            "##########.#########################",
            "###........#########################",
            "###.################################",
            "###........#########################",
            "##########.#########################",
            "###........#########################",
            "###.################################",
            "###.###########........#############",
            "###.###########........#############",
            "###.###########..####..#############",
            "###.###########..####..#############",
            "###..............####..........S####",
            "###.............#####...........####",
            "####################################",
            "####################################"
        };

        /// <summary>Стены — только клетки «#», граничащие с проходом; соседние в ряду склеиваются в один блок.</summary>
        static void BuildCourse(Transform course, out Vector3 start)
        {
            start = Vector3.zero;
            int H = CourseMap.Length, Wd = CourseMap[0].Length;
            char At(int x, int y) => (y < 0 || y >= H || x < 0 || x >= Wd) ? '#' : CourseMap[y][x];
            Vector3 P(float x, float y) => new Vector3(x * Cell, 0f, -y * Cell);      // карта: y вниз → мир: z вверх
            bool Edge(int x, int y)
            {
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    { char c = At(x + dx, y + dy); if (c != '#') return true; }
                return false;
            }
            var wall = new Color(0.55f, 0.45f, 0.36f);
            var pillar = new Color(0.62f, 0.38f, 0.30f);
            float fx0 = 1e9f, fx1 = -1e9f, fy0 = 1e9f, fy1 = -1e9f;
            for (int y = 0; y < H; y++)
            {
                int x = 0;
                while (x < Wd)
                {
                    char c = At(x, y);
                    if (c == '#' && Edge(x, y))
                    {
                        int x1 = x;
                        while (x1 + 1 < Wd && At(x1 + 1, y) == '#' && Edge(x1 + 1, y)) x1++;
                        Vector3 a = P(x, y), b = P(x1 + 1, y + 1);
                        Cube("Стена", new Vector3((a.x + b.x) * 0.5f, 0.225f, (a.z + b.z) * 0.5f), new Vector3(b.x - a.x, 0.45f, a.z - b.z), wall, course);
                        x = x1 + 1; continue;
                    }
                    if (c == 'o') Cube("Столб", P(x + 0.5f, y + 0.5f) + Vector3.up * 0.225f, new Vector3(0.3f, 0.45f, 0.3f), pillar, course);
                    if (c == 'S') start = P(x + 0.5f, y + 0.5f);
                    if (c == 'F') { fx0 = Mathf.Min(fx0, x); fx1 = Mathf.Max(fx1, x + 1); fy0 = Mathf.Min(fy0, y); fy1 = Mathf.Max(fy1, y + 1); }
                    x++;
                }
            }
            if (fx1 > fx0)
            {
                Vector3 a = P(fx0, fy0), b = P(fx1, fy1);
                Cube("Финиш", new Vector3((a.x + b.x) * 0.5f, 0.004f, (a.z + b.z) * 0.5f), new Vector3(b.x - a.x, 0.008f, a.z - b.z), new Color(0.35f, 0.7f, 0.4f));
            }
        }

        // ================= МОДЕЛЬ ВМЕСТО КУБИКОВ =================
        const string ModelName = "Герой (модель)";
        const string HeroPrefab = "Assets/Characters/Hero/Hero.prefab";

        [MenuItem("Fortress/Hero/Proto: обратно к кубикам")]
        public static void RemoveModelMenu()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Hero] кубики: выйди из Play"); return; }
            var scene = SceneManager.GetActiveScene();                    // любая сцена с прототипом (трасса, арены)
            foreach (var old in Object.FindObjectsOfType<ProtoHeroModel>()) Object.DestroyImmediate(old.gameObject);
            var proto = Object.FindObjectOfType<HeroProto>();
            if (proto)
            {
                proto.drawArmLine = true;
                foreach (var t in new[] { proto.bodyCube, proto.shoulderCube, proto.elbowCube, proto.handCube })
                    if (t && t.GetComponent<Renderer>()) t.GetComponent<Renderer>().enabled = true;
                EditorUtility.SetDirty(proto);
            }
            var orbit = Object.FindObjectOfType<ProtoOrbitCamera>();
            if (orbit) { orbit.lookHeight = 0.15f; EditorUtility.SetDirty(orbit); }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Hero] обратно к кубикам");
        }

        [MenuItem("Fortress/Hero/Proto: модель вместо кубиков")]
        public static void AddModelMenu()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Hero] модель: выйди из Play"); return; }
            var scene = SceneManager.GetActiveScene();                    // любая сцена с прототипом (трасса, арены)
            var proto = Object.FindObjectOfType<HeroProto>();
            if (!proto) { Debug.LogWarning("[Hero] модель: нет HeroProto"); return; }
            AddModel(proto);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Hero] модель поставлена вместо кубиков");
        }

        /// <summary>Hero.prefab (без своего управления) + блок ProtoHeroModel на точках прототипа.</summary>
        static GameObject AddModel(HeroProto proto)
        {
            foreach (var old in Object.FindObjectsOfType<ProtoHeroModel>()) Object.DestroyImmediate(old.gameObject);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPrefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = ModelName;
            var root = proto.transform.parent;                            // «Прототип героя»
            if (root) go.transform.SetParent(root, false);
            var cc = go.GetComponent<CharacterController>(); if (cc) Object.DestroyImmediate(cc);
            var b = proto.bodyCube.position;
            go.transform.SetPositionAndRotation(new Vector3(b.x, 0f, b.z),
                Quaternion.LookRotation(Vector3.ProjectOnPlane(proto.bodyCube.forward, Vector3.up).normalized, Vector3.up));
            var m = go.AddComponent<ProtoHeroModel>();
            m.proto = proto;
            m.body = go.GetComponentInChildren<Fortress.Hero.HeroBody>(true);
            if (m.body) m.body.root = go.transform;
            // камера смотрит на корпус сидящего, а не на таз-кубик
            var orbit = Object.FindObjectOfType<ProtoOrbitCamera>();
            if (orbit) { orbit.lookHeight = 0.45f; EditorUtility.SetDirty(orbit); }
            return go;
        }

        // ================= РЕЛЬЕФ ПО Y (только сцена, логика его пока не видит) =================
        // Отдельный корень «Рельеф (Y)», НЕ под obstaclesRoot: иначе каждая ступень стала бы стеной.
        // Клетки — по той же карте (x вправо, y вниз), dir — направление ХОДЬБЫ по маршруту: L/R/U(вверх по карте)/D.
        const string ReliefName = "Рельеф (Y)";
        static readonly Color StepA = new Color(0.44f, 0.46f, 0.50f), StepB = new Color(0.50f, 0.52f, 0.56f);
        static readonly Color RampC = new Color(0.52f, 0.50f, 0.44f), BumpC = new Color(0.40f, 0.36f, 0.32f), HighC = new Color(0.70f, 0.30f, 0.25f);

        [MenuItem("Fortress/Hero/Proto: рельеф Y (пересобрать)")]
        public static void RebuildRelief()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Hero] рельеф: выйди из Play"); return; }
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) { Debug.LogWarning("[Hero] рельеф: открой " + ScenePath); return; }
            foreach (var g in scene.GetRootGameObjects()) if (g.name == ReliefName) Object.DestroyImmediate(g);
            SceneManager.SetActiveScene(scene);
            var relief = BuildRelief();
            var proto = Object.FindObjectOfType<HeroProto>();
            if (proto) { proto.surfaceRoot = relief; EditorUtility.SetDirty(proto); }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Hero] рельеф Y пересобран");
        }

        static Vector3 CellP(float x, float y, Vector3 start) => new Vector3(x * Cell, 0f, -y * Cell) - start;

        static Vector3 MapStart()
        {
            for (int y = 0; y < CourseMap.Length; y++)
            {
                int x = CourseMap[y].IndexOf('S');
                if (x >= 0) return new Vector3((x + 0.5f) * Cell, 0f, -(y + 0.5f) * Cell);
            }
            return Vector3.zero;
        }

        static Vector3 DirW(char d) => d == 'L' ? Vector3.left : d == 'R' ? Vector3.right : d == 'U' ? Vector3.forward : Vector3.back;

        /// <summary>Прямоугольник клеток x0..x1, y0..y1 (включительно) → центр и размер по полу.</summary>
        static void CellRect(int x0, int y0, int x1, int y1, Vector3 st, out Vector3 c, out Vector3 size)
        {
            Vector3 a = CellP(x0, y0, st), b = CellP(x1 + 1, y1 + 1, st);
            c = new Vector3((a.x + b.x) * 0.5f, 0f, (a.z + b.z) * 0.5f);
            size = new Vector3(Mathf.Abs(b.x - a.x), 0f, Mathf.Abs(b.z - a.z));
        }

        /// <summary>Площадка / ступень высотой h на клетках.</summary>
        static void Block(Transform root, string n, int x0, int y0, int x1, int y1, float h, Color col, Vector3 st)
        {
            CellRect(x0, y0, x1, y1, st, out var c, out var s);
            Cube(n, c + Vector3.up * h * 0.5f, new Vector3(s.x, h, s.z), col, root);
        }

        /// <summary>Склон по клеткам: высота меняется от h0 до h1 по направлению ходьбы dir.</summary>
        static void Ramp(Transform root, string n, int x0, int y0, int x1, int y1, char dir, float h0, float h1, Vector3 st)
        {
            CellRect(x0, y0, x1, y1, st, out var c, out var s);
            float lo = Mathf.Min(h0, h1), hi = Mathf.Max(h0, h1);
            if (lo > 0.001f) Cube(n + " (основа)", c + Vector3.up * lo * 0.5f, new Vector3(s.x, lo, s.z), RampC, root);
            Vector3 rise = DirW(dir) * (h1 >= h0 ? 1f : -1f);
            bool alongX = Mathf.Abs(rise.x) > 0.5f;
            var g = new GameObject(n);
            g.transform.SetParent(root, false);
            g.transform.SetPositionAndRotation(c + Vector3.up * lo, Quaternion.LookRotation(rise, Vector3.up));
            g.transform.localScale = new Vector3(alongX ? s.z : s.x, hi - lo, alongX ? s.x : s.z);
            g.AddComponent<MeshFilter>().sharedMesh = Wedge();
            g.AddComponent<MeshRenderer>().sharedMaterial = Mat(n, RampC);
        }

        /// <summary>Лестница: клетки режутся поперёк dir на столько ступеней, сколько высот; первая — та, что ближе по ходьбе.</summary>
        static void Stairs(Transform root, string n, int x0, int y0, int x1, int y1, char dir, float[] hs, Vector3 st)
        {
            CellRect(x0, y0, x1, y1, st, out var c, out var s);
            Vector3 d = DirW(dir);
            bool alongX = Mathf.Abs(d.x) > 0.5f;
            float len = alongX ? s.x : s.z, w = alongX ? s.z : s.x, sl = len / hs.Length;
            for (int i = 0; i < hs.Length; i++)
            {
                Vector3 p = c + d * (-len * 0.5f + sl * (i + 0.5f));
                Vector3 sz = alongX ? new Vector3(sl, hs[i], w) : new Vector3(w, hs[i], sl);
                Cube(n + " " + (i + 1), p + Vector3.up * hs[i] * 0.5f, sz, i % 2 == 0 ? StepA : StepB, root);
            }
        }

        /// <summary>Свободный кубик-помеха (в клетках, можно дробные), высота h.</summary>
        static void Bump(Transform root, string n, float cx, float cy, float sx, float sz, float h, Color col, Vector3 st)
        {
            Vector3 p = CellP(cx, cy, st);
            Cube(n, p + Vector3.up * h * 0.5f, new Vector3(sx, h, sz), col, root);
        }

        static Mesh wedge;
        /// <summary>Клин 1×1×1: основание y = 0, верх поднимается к +Z (высокий торец z = +0.5).</summary>
        internal static Mesh Wedge()
        {
            const string path = "Assets/Characters/Hero/ProtoWedge.asset";
            if (wedge) return wedge;
            wedge = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (wedge) return wedge;
            var v = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            Vector3 center = new Vector3(0f, 0.33f, 0.15f);
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 nrm = Vector3.Cross(b - a, c - a), mid = (a + b + c) / 3f;
                if (Vector3.Dot(nrm, mid - center) < 0f) { var tmp = b; b = c; c = tmp; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            Vector3 b0 = new Vector3(-.5f, 0, -.5f), b1 = new Vector3(.5f, 0, -.5f), b2 = new Vector3(.5f, 0, .5f), b3 = new Vector3(-.5f, 0, .5f);
            Vector3 t2 = new Vector3(.5f, 1, .5f), t3 = new Vector3(-.5f, 1, .5f);
            Tri(b0, b1, b2); Tri(b0, b2, b3);        // дно
            Tri(b3, b2, t2); Tri(b3, t2, t3);        // высокий торец
            Tri(b0, t3, t2); Tri(b0, t2, b1);        // склон
            Tri(b0, b3, t3); Tri(b1, t2, b2);        // боковые треугольники
            wedge = new Mesh { name = "ProtoWedge" };
            wedge.SetVertices(v); wedge.SetTriangles(t, 0);
            wedge.RecalculateNormals(); wedge.RecalculateBounds();
            AssetDatabase.CreateAsset(wedge, path);
            return wedge;
        }

        /// <summary>Рельеф вдоль маршрута. Высоты под сидящего героя: ступень 5–12 см, подъём до 40 см, 60 см — «не залезть» (красное).</summary>
        static Transform BuildRelief()
        {
            var root = new GameObject(ReliefName).transform;
            Vector3 st = MapStart();

            // 1. Стартовый коридор (ряды 21–22), идём влево: склон вверх → площадка → лестница вниз
            Ramp(root, "Склон вверх", 28, 21, 30, 22, 'L', 0f, 0.3f, st);
            Block(root, "Площадка 30 см", 26, 21, 27, 22, 0.3f, StepB, st);
            Stairs(root, "Лестница вниз", 24, 21, 25, 22, 'L', new[] { 0.2f, 0.1f }, st);

            // 2. «Горб»: вверх по ступеням (x 21–22), поверху влево с балкой-порогом, вниз склоном (x 15–16)
            Stairs(root, "Ступени на горб", 21, 19, 22, 20, 'U', new[] { 0.1f, 0.2f }, st);
            Block(root, "Верх горба", 15, 17, 22, 18, 0.25f, StepA, st);
            Bump(root, "Балка-порог", 19f, 18f, 0.15f, 2f * Cell, 0.35f, BumpC, st);
            Ramp(root, "Склон с горба", 15, 19, 16, 20, 'D', 0.25f, 0f, st);

            // 3. Длинный коридор (ряды 21–22, x 3–16), влево: бордюр, россыпь камней, «лежачий полицейский»
            Bump(root, "Бордюр 12 см", 13.5f, 22f, 0.2f, 2f * Cell, 0.12f, BumpC, st);
            Bump(root, "Камень", 10.3f, 21.4f, 0.22f, 0.18f, 0.06f, BumpC, st);
            Bump(root, "Камень", 9.6f, 22.3f, 0.3f, 0.2f, 0.09f, BumpC, st);
            Bump(root, "Камень", 9.1f, 21.7f, 0.16f, 0.24f, 0.05f, BumpC, st);
            Ramp(root, "Бугор вверх", 6, 21, 6, 22, 'L', 0f, 0.15f, st);
            Ramp(root, "Бугор вниз", 5, 21, 5, 22, 'L', 0.15f, 0f, st);

            // 4. Узкий подъём (x 3, ряды 16–20), вверх по карте: лестница до 40 см
            Stairs(root, "Узкая лестница", 3, 16, 3, 20, 'U', new[] { 0.08f, 0.16f, 0.24f, 0.32f, 0.4f }, st);

            // 5. «Змейка»: ряд 15 и поворот x 10 — поверху (40 см), ряд 13 — длинный склон вниз, ряд 11 — ступеньки-пороги
            Block(root, "Змейка поверху", 3, 15, 10, 15, 0.4f, StepB, st);
            Block(root, "Змейка поворот", 10, 13, 10, 14, 0.4f, StepB, st);
            Ramp(root, "Длинный склон вниз", 4, 13, 9, 13, 'L', 0.4f, 0f, st);
            Block(root, "Порог", 5, 11, 5, 11, 0.1f, StepA, st);
            Block(root, "Порог", 7, 11, 7, 11, 0.1f, StepA, st);
            Block(root, "Порог", 9, 11, 9, 11, 0.1f, StepA, st);

            // 6. Зал со столбами: правая половина — терраса 15 см (вход: ступень по всему краю или пандус снизу),
            //    помехи — уступ 35 см и ящик 60 см («не залезть»)
            Block(root, "Терраса 15 см", 20, 2, 28, 9, 0.15f, StepA, st);
            Ramp(root, "Пандус на террасу", 18, 7, 19, 9, 'R', 0f, 0.15f, st);
            Bump(root, "Уступ 35 см", 14.5f, 4f, 0.9f, 0.6f, 0.35f, BumpC, st);
            Bump(root, "Ящик 60 см", 23.5f, 3.5f, 0.6f, 0.6f, 0.6f, HighC, st);

            // 7. Узкий проход к финишу (ряд 3, x 29–30): ступени вниз с террасы
            Stairs(root, "Спуск к финишу", 29, 3, 30, 3, 'R', new[] { 0.1f, 0.05f }, st);
            return root;
        }

        static void Fill()
        {
            var floor = Cube("Пол", new Vector3(-9f, -0.05f, 6f), new Vector3(60f, 0.1f, 60f), new Color(0.26f, 0.27f, 0.29f));
            // сетка-ориентир: полоски каждые 1 м
            var grid = new GameObject("Сетка 1 м").transform;      // не дочерняя полу: пол растянут, полосы были толстыми
            for (int i = -30; i <= 20; i++)
            {
                Cube("Сетка X" + i, new Vector3(i, 0.001f, 6f), new Vector3(0.01f, 0.002f, 60f), new Color(0.33f, 0.34f, 0.36f), grid);
                Cube("Сетка Z" + i, new Vector3(-9f, 0.001f, i), new Vector3(60f, 0.002f, 0.01f), new Color(0.33f, 0.34f, 0.36f), grid);
            }
            var rig = new GameObject("Прототип героя").transform;          // префаб: герой + камера, ссылки внутри
            var root = new GameObject("Герой (прототип)").transform;
            root.SetParent(rig, false);
            var body = Cube("Тело", new Vector3(0f, 0.1f, 0f), new Vector3(0.3f, 0.2f, 0.3f), new Color(0.45f, 0.55f, 0.75f), root);
            var sh = Cube("Плечо", new Vector3(0f, 0.24f, 0f), Vector3.one * 0.07f, new Color(0.95f, 0.85f, 0.55f), root);
            var el = Cube("Локоть", new Vector3(0f, 0.08f, 0.2f), Vector3.one * 0.05f, new Color(0.95f, 0.65f, 0.35f), root);
            var hd = Cube("Кисть", new Vector3(0f, 0.025f, 0.4f), new Vector3(0.07f, 0.05f, 0.09f), new Color(0.95f, 0.45f, 0.35f), root);
            var proto = root.gameObject.AddComponent<HeroProto>();
            proto.bodyCube = body; proto.shoulderCube = sh; proto.elbowCube = el; proto.handCube = hd;

            // ---- полоса препятствий по карте (клетка 0.6 м): # стена, o столб, F финиш, S старт; без физики
            var course = new GameObject("Полоса препятствий").transform;
            BuildCourse(course, out Vector3 startPos);
            // старт: тело в клетке S, лицом вдоль первого коридора (влево по карте)
            Vector3 shift = -startPos;
            foreach (Transform c in course) c.position += shift;
            var fin = GameObject.Find("Финиш"); if (fin) fin.transform.position += shift;
            body.rotation = Quaternion.Euler(0f, -90f, 0f);
            proto.obstaclesRoot = course;
            proto.surfaceRoot = BuildRelief();                            // рельеф по Y — отдельно от стен, высота точек
            // плечо — справа от тела, как у модели героя (upperarm_r ≈ 0.21 м от центра)
            proto.shoulderSide = 0.2f;
            sh.position = body.position + body.right * proto.shoulderSide + Vector3.up * 0.14f;

            var sun = new GameObject("Свет").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(60f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.47f);

            var camGo = new GameObject("Камера"); camGo.tag = "MainCamera";
            camGo.transform.SetParent(rig, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.fieldOfView = 50f;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.13f); cam.clearFlags = CameraClearFlags.SolidColor;
            camGo.transform.position = new Vector3(0f, 2.4f, -1.5f);
            camGo.transform.LookAt(new Vector3(0f, 0f, 0.6f));
            proto.cam = cam;
            var orbit = camGo.AddComponent<ProtoOrbitCamera>();       // ПКМ — наклон, колесо — зум; поворот — вместе с телом
            orbit.target = body;
            orbit.yaw = body.eulerAngles.y;                               // за спиной тела с первого кадра
            orbit.startBehindBody = true;
            {   // поставить камеру за спину сразу (как ProtoOrbitCamera.Apply)
                var rot = Quaternion.Euler(orbit.pitch, orbit.yaw, 0f);
                Vector3 focus = body.position + Vector3.up * orbit.lookHeight;
                camGo.transform.SetPositionAndRotation(focus - rot * Vector3.forward * orbit.distance, rot);
            }
            proto.showHints = true;                                       // подсказки и Gizmos включены
            // 3D-модель вместо кубиков — эксперимент (модель видна только со спины): по меню «Proto: модель вместо кубиков»
            // префаб
            const string PrefabPath = "Assets/Characters/Hero/HeroProto.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(rig.gameObject, PrefabPath, InteractionMode.AutomatedAction);
        }
    }
}
