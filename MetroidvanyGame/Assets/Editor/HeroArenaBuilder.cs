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

        /// <summary>Поручень — тонкий брус от точки a до b (не опора, не стена) — ЗАЦЕП для кисти (ProtoHold).</summary>
        static void Rail(string n, Vector3 a, Vector3 b)
        {
            var t = Cube(n, (a + b) * 0.5f, new Vector3(0.05f, 0.05f, (b - a).magnitude), RailC, decor);
            t.rotation = Quaternion.LookRotation(b - a, Vector3.up);
            t.gameObject.AddComponent<ProtoHold>();
        }

        /// <summary>Столб / ручка-цилиндр — зацеп по своей оси.</summary>
        static void HoldCyl(Transform t) { var h = t.gameObject.AddComponent<ProtoHold>(); h.a = new Vector3(0f, -1f, 0f); h.b = new Vector3(0f, 1f, 0f); }

        /// <summary>Табличка-подпись на полу (читается от камеры за спиной героя, лицом на север).</summary>
        static void Sign(string text, Vector3 pos, float size = 1f, float yaw = 0f)
        {
            var g = new GameObject("Подпись: " + text.Split('\n')[0]);
            g.transform.SetParent(decor, false);
            g.transform.SetPositionAndRotation(pos + Vector3.up * 0.01f, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(70f, 0f, 0f));   // yaw — куда смотрит камера, читающая подпись
            var tm = g.AddComponent<TextMesh>();
            tm.text = text; tm.fontSize = 64; tm.characterSize = 0.026f * size; tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center; tm.color = new Color(0.95f, 0.93f, 0.85f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            g.GetComponent<MeshRenderer>().sharedMaterial = font.material;
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

        // ================= АРЕНА 3 — ПОДВИЖНЫЕ ЭЛЕМЕНТЫ =================
        public const string Scene3Path = "Assets/Scenes/HeroArena3.unity";
        const float PlatTop = MAIN + 0.01f;        // платформы на 1 см выше пола: вровень — герой стоит НА платформе, а не на полу под ней
        static readonly Color PlatC = new Color(0.30f, 0.55f, 0.70f), StripeC = new Color(0.95f, 0.80f, 0.30f);

        /// <summary>Арена 3: лифт на галерею, поворотный круг, два челнока через пропасть. Блок ProtoMover.
        ///   Лифт 0.6 ↔ 1.8 (галерея только через него; с галереи — лестница вниз).
        ///   Поворотный круг вровень с полом, 25 °/с — тело крутится вместе с ним.
        ///   Пропасть 4 м (вниз 0.6 — за порогом): перейти только на челноке; за ней финиш.</summary>
        [MenuItem("Fortress/Hero/Build Arena 3 (подвижные)")]
        public static void Build3()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Hero] арена 3: выйди из Play"); return; }
            var open = SceneManager.GetSceneByPath(Scene3Path);
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
            Fill3();
            EditorSceneManager.SaveScene(scene, Scene3Path);
            Debug.Log("[Hero] арена 3: " + Scene3Path);
        }

        static Transform Mover(string n, Vector3 pos, Vector3 offset, float travel, float wait, float spin, float phase)
        {
            var g = new GameObject(n);
            g.transform.SetParent(surf, false);
            g.transform.position = pos;
            var m = g.AddComponent<ProtoMover>();
            m.offset = offset; m.travelTime = travel; m.wait = wait; m.spinSpeed = spin; m.phase = phase;
            return g.transform;
        }

        static void Fill3()
        {
            const float W3 = 14f, D3 = 18f, ChasmA = 12f, ChasmB = 16f;
            walls = new GameObject("Арена: стены").transform;
            surf = new GameObject("Арена: поверхность").transform;
            decor = new GameObject("Арена: декор (не опора)").transform;

            Box("Земля", -2f, W3 + 2f, -0.05f, 0f, -2f, D3 + 2f, GroundC, decor);   // дно пропасти — y 0

            // основа 0.6 до пропасти и за ней (финиш)
            Box("Основа", 0f, W3, 0f, MAIN, 0f, ChasmA, MainC, surf);
            Box("Основа за пропастью", 0f, W3, 0f, MAIN, ChasmB, D3, MainC, surf);
            for (int i = 1; i < W3; i++) Box("Разметка X" + i, i - 0.005f, i + 0.005f, MAIN, MAIN + 0.002f, 0f, ChasmA, LineC, decor);
            for (int i = 1; i < ChasmA; i++) Box("Разметка Z" + i, 0f, W3, MAIN, MAIN + 0.002f, i - 0.005f, i + 0.005f, LineC, decor);
            Box("Финиш", 3f, 11f, MAIN, MAIN + 0.004f, 16.6f, 17.6f, FinishC, decor);

            // стены по периметру (45 см над основой)
            Box("Стена юг", -0.3f, W3 + 0.3f, 0f, WallTop, -0.3f, 0f, WallC, walls);
            Box("Стена север", -0.3f, W3 + 0.3f, 0f, WallTop, D3, D3 + 0.3f, WallC, walls);
            Box("Стена запад", -0.3f, 0f, 0f, WallTop, 0f, D3, WallC, walls);
            Box("Стена восток", W3, W3 + 0.3f, 0f, WallTop, 0f, D3, WallC, walls);

            // ---- ЛИФТ на галерею 1.8 (галерея — только через лифт), с галереи — лестница вниз на север
            Box("Галерея", 0f, 5f, 0f, UP, 5f, 9f, DeckC, surf);
            var lift = Mover("Лифт", new Vector3(2.5f, 0f, 4f), new Vector3(0f, UP - MAIN, 0f), 3f, 4f, 0f, 0f);
            // вплотную к галерее: щель даже 10 см наверху — обрыв за порогом спуска, сойти нельзя
            Box("Лифт: площадка", 1.6f, 3.4f, PlatTop - 0.15f, PlatTop, 3.2f, 5f, PlatC, lift);
            Box("Лифт: метка", 2.4f, 2.6f, PlatTop, PlatTop + 0.003f, 3.3f, 4.9f, StripeC, lift);
            var down = new float[9]; for (int i = 0; i < 9; i++) down[i] = UP - 0.12f * (i + 1);
            StairsZ("Лестница с галереи", 1.5f, 3.5f, 9f, 0.3f, down, surf);
            // стойки по краю галереи у лестницы — чтобы видно было, где край
            foreach (float z in new[] { 5.5f, 6.5f, 7.5f, 8.5f }) Post("Стойка галереи", 4.97f, z, UP, UP + RailH, false);
            Rail("Поручень галереи", new Vector3(4.97f, UP + RailH, 5f), new Vector3(4.97f, UP + RailH, 9f));

            // ---- ПОВОРОТНЫЙ КРУГ: вровень с полом, 25 °/с, с полосой — видно вращение
            var disc = Mover("Поворотный круг", new Vector3(10.5f, 0f, 5f), Vector3.zero, 1f, 0f, 25f, 0f);
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "Круг"; Object.DestroyImmediate(cyl.GetComponent<Collider>());
            cyl.transform.SetParent(disc, false);
            cyl.transform.localPosition = new Vector3(0f, PlatTop - 0.02f, 0f); cyl.transform.localScale = new Vector3(2.8f, 0.02f, 2.8f);
            cyl.GetComponent<MeshRenderer>().sharedMaterial = Mat("Круг", PlatC);
            Box("Круг: полоса", 10.4f, 10.6f, PlatTop, PlatTop + 0.003f, 3.7f, 6.3f, StripeC, disc);

            // ---- ПРОПАСТЬ 4 м и два челнока (2×2), ходят не в такт
            var s1 = Mover("Челнок 1", new Vector3(3f, 0f, 13f), new Vector3(0f, 0f, 2f), 3f, 4f, 0f, 0f);
            Box("Челнок 1: площадка", 2f, 4f, PlatTop - 0.15f, PlatTop, 12f, 14f, PlatC, s1);
            Box("Челнок 1: метка", 2.9f, 3.1f, PlatTop, PlatTop + 0.003f, 12.1f, 13.9f, StripeC, s1);
            var s2 = Mover("Челнок 2", new Vector3(7f, 0f, 13f), new Vector3(0f, 0f, 2f), 4f, 3.5f, 0f, 3f);
            Box("Челнок 2: площадка", 6f, 8f, PlatTop - 0.15f, PlatTop, 12f, 14f, PlatC, s2);
            Box("Челнок 2: метка", 6.9f, 7.1f, PlatTop, PlatTop + 0.003f, 12.1f, 13.9f, StripeC, s2);

            // ---- ВРАЩАЮЩИЙСЯ МОСТ: доска 4.6 × 1 над пропастью, поворачивается рывками по 90° (3 с) со стоянкой 4 с.
            // Через раз встаёт вдоль пролёта — концы на обоих краях, переходишь; поперёк и в повороте — концы над пропастью.
            var rb = Mover("Вращающийся мост", new Vector3(11.2f, 0f, 14f), Vector3.zero, 3f, 4f, 0f, 0f);
            rb.GetComponent<ProtoMover>().spinStep = 90f;
            Box("Мост: доска", 10.7f, 11.7f, PlatTop - 0.12f, PlatTop, 11.7f, 16.3f, PlatC, rb);
            Box("Мост: метка", 11.1f, 11.3f, PlatTop, PlatTop + 0.003f, 11.8f, 16.2f, StripeC, rb);
            Box("Мост: ось", 11.05f, 11.35f, 0f, PlatTop - 0.12f, 13.85f, 14.15f, RailC, rb);

            var sun = new GameObject("Свет").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.47f);

            PlaceHero(new Vector3(7f, 0f, 1.2f));
        }

        // ================= АРЕНА 4 — КАРУСЕЛИ И КРУТЫЕ СКЛОНЫ (только локация, механика — потом) =================
        public const string Scene4Path = "Assets/Scenes/HeroArena4.unity";

        /// <summary>Арена 4 — для будущих механик: сваливание с быстрой карусели, зацеп рукой на развороте,
        /// подъём по крутому склону только с захватом за перила. Логика героя здесь пока прежняя.
        ///   Юг: три карусели 30 / 60 / 120 °/с — столб по центру и 4 поручня по краю (будущие зацепы).
        ///   Север: уступ 1.8; к нему склоны 30° и 45° (перила с двух сторон), 60° перила справа (сторона руки),
        ///   60° перила слева (через грудь), 60° без перил; лестница — вернуться.</summary>
        [MenuItem("Fortress/Hero/Build Arena 4 (карусели и склоны)")]
        public static void Build4()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Hero] арена 4: выйди из Play"); return; }
            var open = SceneManager.GetSceneByPath(Scene4Path);
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
            Fill4();
            EditorSceneManager.SaveScene(scene, Scene4Path);
            Debug.Log("[Hero] арена 4: " + Scene4Path);
        }

        static Transform Cyl(string n, Vector3 bottom, float radius, float height, Color c, Transform parent)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = n; Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.position = bottom + Vector3.up * height * 0.5f;
            g.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            g.GetComponent<MeshRenderer>().sharedMaterial = Mat(n, c);
            return g.transform;
        }

        /// <summary>Склон вверх на север от основы до уступа: угол ang, ширина x0..x1, верх — у z = top.
        /// Перила: стойки-препятствия вдоль края (между ними пролезают) + наклонный поручень (декор).</summary>
        static void SteepRamp(string n, float x0, float x1, float topZ, float ang, bool railLeft, bool railRight)
        {
            float rise = UP - MAIN, run = rise / Mathf.Tan(ang * Mathf.Deg2Rad), z0 = topZ - run;
            Wedge(n, x0, x1, z0, topZ, MAIN, rise, Vector3.forward, TunC, surf);
            float H(float z) => MAIN + rise * Mathf.Clamp01((z - z0) / run);
            void Railing(float x, string side)
            {
                int posts = Mathf.Max(2, Mathf.CeilToInt(run / 0.6f) + 1);
                for (int i = 0; i < posts; i++)
                {
                    float z = Mathf.Lerp(z0 + 0.03f, topZ - 0.03f, i / (float)(posts - 1));
                    Post(n + ": стойка " + side, x, z, MAIN, H(z) + RailH, true);
                }
                Rail(n + ": поручень " + side, new Vector3(x, MAIN + RailH, z0 - 0.3f), new Vector3(x, UP + RailH, topZ));
                Rail(n + ": поручень " + side + " (уступ)", new Vector3(x, UP + RailH, topZ), new Vector3(x, UP + RailH, topZ + 0.8f));
                Post(n + ": стойка " + side + " (низ)", x, z0 - 0.3f, MAIN, MAIN + RailH, true);
            }
            if (railLeft) Railing(x0 - 0.04f, "слева");
            if (railRight) Railing(x1 + 0.04f, "справа");
        }

        static void Fill4()
        {
            const float W4 = 17f, D4 = 14f, LedgeZ = 10f;
            walls = new GameObject("Арена: стены").transform;
            surf = new GameObject("Арена: поверхность").transform;
            decor = new GameObject("Арена: декор (не опора)").transform;

            Box("Земля", -2f, W4 + 2f, -0.05f, 0f, -2f, D4 + 2f, GroundC, decor);
            Box("Основа", 0f, W4, 0f, MAIN, 0f, LedgeZ, MainC, surf);
            Box("Уступ 1.8", 0f, W4, 0f, UP, LedgeZ, D4, DeckC, surf);
            for (int i = 1; i < W4; i++) Box("Разметка X" + i, i - 0.005f, i + 0.005f, MAIN, MAIN + 0.002f, 0f, LedgeZ, LineC, decor);
            for (int i = 1; i < LedgeZ; i++) Box("Разметка Z" + i, 0f, W4, MAIN, MAIN + 0.002f, i - 0.005f, i + 0.005f, LineC, decor);
            Box("Финиш", 6f, 11f, UP, UP + 0.004f, 12.6f, 13.6f, FinishC, decor);

            Box("Стена юг", -0.3f, W4 + 0.3f, 0f, WallTop, -0.3f, 0f, WallC, walls);
            Box("Стена север", -0.3f, W4 + 0.3f, 0f, UP + 0.45f, D4, D4 + 0.3f, WallC, walls);
            Box("Стена запад", -0.3f, 0f, 0f, UP + 0.45f, 0f, D4, WallC, walls);
            Box("Стена восток", W4, W4 + 0.3f, 0f, UP + 0.45f, 0f, D4, WallC, walls);

            // ---- КАРУСЕЛИ: 30 / 60 / 120 °/с, r 1.4; столб по центру + 4 поручня по краю (едут с кругом, пока декор)
            float[] speeds = { 30f, 60f, 120f };
            float[] xs = { 3.5f, 8.5f, 13.5f };
            for (int k = 0; k < 3; k++)
            {
                var c = new Vector3(xs[k], 0f, 4f);
                var disc = Mover("Карусель " + speeds[k] + "°/с", c, Vector3.zero, 1f, 0f, speeds[k], 0f);
                Cyl("Круг", new Vector3(c.x, PlatTop - 0.04f, c.z), 1.4f, 0.04f, PlatC, disc);
                Box("Полоса", c.x - 0.1f, c.x + 0.1f, PlatTop, PlatTop + 0.003f, c.z - 1.3f, c.z + 1.3f, StripeC, disc);
                HoldCyl(Cyl("Столб", new Vector3(c.x, PlatTop, c.z), 0.06f, 1.0f, RailC, disc));
                for (int i = 0; i < 4; i++)
                {
                    Vector3 d = Quaternion.AngleAxis(45f + 90f * i, Vector3.up) * Vector3.forward * 1.22f;
                    HoldCyl(Cyl("Поручень", new Vector3(c.x + d.x, PlatTop, c.z + d.z), 0.035f, 0.5f, RailC, disc));
                }
            }

            // ---- СКЛОНЫ к уступу (верх у z = 10), слева направо: 30°, 45°, 60° перила справа, 60° перила слева, 60° без перил
            SteepRamp("Склон 30°", 0.6f, 2.4f, LedgeZ, 30f, true, true);
            SteepRamp("Склон 45°", 3.6f, 5.4f, LedgeZ, 45f, true, true);
            SteepRamp("Склон 60° перила справа", 6.6f, 8.4f, LedgeZ, 60f, false, true);
            SteepRamp("Склон 60° перила слева", 9.6f, 11.4f, LedgeZ, 60f, true, false);
            SteepRamp("Склон 60° без перил", 12.6f, 14.4f, LedgeZ, 60f, false, false);
            // лестница — вернуться/подняться без склонов
            var up = new float[10]; for (int i = 0; i < 10; i++) up[i] = MAIN + 0.12f * (i + 1);
            StairsZ("Лестница на уступ", 15.3f, 16.7f, 7f, 0.3f, up, surf);

            var sun = new GameObject("Свет").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.47f);

            PlaceHero(new Vector3(8.5f, 0f, 1f));
        }

        // ================= АРЕНА 5 — ВСКАРАБКИВАНИЕ (стенды с подписями) =================
        public const string Scene5Path = "Assets/Scenes/HeroArena5.unity";

        /// <summary>Арена 5 — стенды механики «вскарабкивание», каждый подписан номером и тем, что должно получиться.
        ///   Запад — уступы 0.3 (шаг), 0.5 и 0.7 (подтянуться), 1.0 (не достать); за уступом ступени вниз, коридор назад.
        ///   Восток — склоны на настил 1.8: 30° как пол, 45° без перил (сползает), 45° и 60° с перилами справа,
        ///   60° с перилами слева (через грудь), 60° без перил (нельзя); лестница — спуститься.
        ///   Дорожки разделены стенками (препятствия), у каждого уступа — угол «стенка + уступ».</summary>
        [MenuItem("Fortress/Hero/Build Arena 5 (вскарабкивание)")]
        public static void Build5()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Hero] арена 5: выйди из Play"); return; }
            var open = SceneManager.GetSceneByPath(Scene5Path);
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
            Fill5();
            EditorSceneManager.SaveScene(scene, Scene5Path);
            Debug.Log("[Hero] арена 5: " + Scene5Path);
        }

        const float Rail5 = 0.45f;                  // поручни на стендах ниже, чем в арене 4 (по росту сидящего)
        const float Lane = 1.4f, Sep = 0.6f;        // ширина дорожки и стенки между ними
        const float StandZ0 = 1.6f, StandZ1 = 6f;   // дорожки (стенки) от и до; дальше — коридор / настил

        /// <summary>Склон на настил: ширина x0..x1, верх у topZ на высоте top, угол ang; перила слева / справа (зацепы, стойки — препятствия).</summary>
        static void Ramp5(string n, float x0, float x1, float topZ, float top, float ang, bool railLeft, bool railRight)
        {
            float rise = top - MAIN, run = rise / Mathf.Tan(ang * Mathf.Deg2Rad), z0 = topZ - run;
            Wedge(n, x0, x1, z0, topZ, MAIN, rise, Vector3.forward, TunC, surf);
            float H(float z) => MAIN + rise * Mathf.Clamp01((z - z0) / run);
            void Railing(float x, string side)
            {
                int posts = Mathf.Max(2, Mathf.CeilToInt(run / 0.5f) + 1);
                for (int i = 0; i < posts; i++)
                {
                    float z = Mathf.Lerp(z0 + 0.03f, topZ - 0.03f, i / (float)(posts - 1));
                    Post(n + ": стойка " + side, x, z, MAIN, H(z) + Rail5, true);
                }
                Post(n + ": стойка " + side + " (низ)", x, z0 - 0.4f, MAIN, MAIN + Rail5, true);
                Rail(n + ": поручень " + side + " (подход)", new Vector3(x, MAIN + Rail5, z0 - 0.4f), new Vector3(x, MAIN + Rail5, z0));
                Rail(n + ": поручень " + side, new Vector3(x, MAIN + Rail5, z0), new Vector3(x, top + Rail5, topZ));
                Rail(n + ": поручень " + side + " (настил)", new Vector3(x, top + Rail5, topZ), new Vector3(x, top + Rail5, topZ + 0.8f));
            }
            if (railLeft) Railing(x0 + 0.06f, "слева");
            if (railRight) Railing(x1 - 0.06f, "справа");
        }

        static void Fill5()
        {
            const int Lanes = 11;
            float W5 = Lanes * (Lane + Sep) + Sep, D5 = 9.5f, Deck = UP;
            walls = new GameObject("Арена: стены").transform;
            surf = new GameObject("Арена: поверхность").transform;
            decor = new GameObject("Арена: декор (не опора)").transform;
            float X(int i) => Sep + i * (Lane + Sep);          // левый край дорожки i

            Box("Земля", -2f, W5 + 2f, -0.05f, 0f, -2f, D5 + 2f, GroundC, decor);
            Box("Основа", 0f, W5, 0f, MAIN, 0f, D5, MainC, surf);
            for (int i = 1; i < 10; i++) Box("Разметка Z" + i, 0f, W5, MAIN, MAIN + 0.002f, i - 0.005f, i + 0.005f, LineC, decor);

            // периметр
            Box("Стена юг", -0.3f, W5 + 0.3f, 0f, WallTop, -0.3f, 0f, WallC, walls);
            Box("Стена север", -0.3f, W5 + 0.3f, 0f, Deck + 0.45f, D5, D5 + 0.3f, WallC, walls);
            Box("Стена запад", -0.3f, 0f, 0f, WallTop, 0f, D5, WallC, walls);
            Box("Стена восток", W5, W5 + 0.3f, 0f, Deck + 0.45f, 0f, D5, WallC, walls);
            // стенки между дорожками (препятствия)
            for (int i = 0; i <= Lanes; i++)
            {
                float x = i == 0 ? 0f : X(i) - Sep;
                if (i == 0) continue;                                   // у западной — периметр
                Box("Стенка дорожек " + i, x, x + Sep, MAIN, MAIN + 0.45f, StandZ0, StandZ1, WallC, walls);
            }

            Sign("СТЕНДЫ: ВСКАРАБКИВАНИЕ\nуступы — запад, склоны — восток", new Vector3(W5 * 0.5f, MAIN, 0.25f), 1.2f);

            // ---- УСТУПЫ (дорожки 0–3): верх z 3–4.5, за ним ступень вниз, дальше коридор z 6–9.5 — обойти назад
            float[] hs = { 0.3f, 0.5f, 0.7f, 1.0f };
            string[] ls = { "1 · уступ 0.3\nшагом", "2 · уступ 0.5\nподтянуться", "3 · уступ 0.7\nтяжело", "4 · уступ 1.0\nне достать" };
            for (int i = 0; i < 4; i++)
            {
                float x0 = X(i), x1 = x0 + Lane;
                Box("Уступ " + hs[i], x0, x1, 0f, MAIN + hs[i], 3f, 4.5f, DeckC, surf);
                if (hs[i] > 0.4f && hs[i] < 0.9f) Box("Уступ " + hs[i] + ": ступень вниз", x0, x1, 0f, MAIN + hs[i] * 0.5f, 4.5f, 5.0f, StepA, surf);
                Sign(ls[i], new Vector3((x0 + x1) * 0.5f, MAIN, 2.0f));
            }
            // коридор за уступами — вернуться на старт (вдоль запада)
            Sign("коридор назад", new Vector3(X(0) + Lane * 0.5f, MAIN, 6.6f), 0.8f);

            // ---- СКЛОНЫ (дорожки 4–9) на настил 1.8 (z 6–9.5) и лестница (10)
            int s0 = 4;
            float deckX0 = X(s0) - Sep;
            Box("Настил 1.8", deckX0, W5, 0f, Deck, StandZ1, D5, DeckC, surf);
            Box("Финиш (настил)", deckX0 + 0.5f, W5 - 0.5f, Deck, Deck + 0.004f, 8.4f, 9.2f, FinishC, decor);
            var ramps = new (string label, float ang, bool l, bool r)[]
            {
                ("5 · склон 30°\nкак пол", 30f, false, false),
                ("6 · 45° без перил\nсползает", 45f, false, false),
                ("7 · 45°\nперила справа", 45f, false, true),
                ("8 · 60°\nперила справа", 60f, false, true),
                ("9 · 60°\nперила слева", 60f, true, false),
                ("10 · 60° без перил\nнельзя", 60f, false, false),
            };
            for (int k = 0; k < ramps.Length; k++)
            {
                float x0 = X(s0 + k), x1 = x0 + Lane;
                Ramp5("Склон " + ramps[k].label.Split('\n')[0], x0, x1, StandZ1, Deck, ramps[k].ang, ramps[k].l, ramps[k].r);
                Sign(ramps[k].label, new Vector3((x0 + x1) * 0.5f, MAIN, 2.0f));
            }
            {
                float x0 = X(s0 + ramps.Length), x1 = x0 + Lane;
                var up = new float[10]; for (int i = 0; i < 10; i++) up[i] = MAIN + 0.12f * (i + 1);
                StairsZ("Лестница на настил", x0, x1, 3f, 0.3f, up, surf);
                Sign("11 · лестница", new Vector3((x0 + x1) * 0.5f, MAIN, 2.0f));
            }

            var sun = new GameObject("Свет").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.47f);

            PlaceHero(new Vector3(X(2) + Lane + Sep * 0.5f, 0f, 0.8f));
        }

        // ================= АРЕНА 6 — ПОЛОСА ПРЕПЯТСТВИЙ (все механики подряд) =================
        public const string Scene6Path = "Assets/Scenes/HeroArena6.unity";

        /// <summary>Наклонный пандус по X: от низа xLow до верха xHigh (любой порядок), ширина z0..z1, от y0 на высоту h.</summary>
        static void WedgeX(string n, float xLow, float xHigh, float z0, float z1, float y0, float h, Color c, Transform root)
            => Wedge(n, Mathf.Min(xLow, xHigh), Mathf.Max(xLow, xHigh), z0, z1, y0, h, xHigh > xLow ? Vector3.right : Vector3.left, c, root);

        /// <summary>Арена 6 — полоса препятствий: одна трасса змейкой через все механики, каждый участок подписан.
        ///   Ряд A (восток):  слалом → ступени → уступ 0.3 шагом → уступ 0.5 подтянуться → тоннель под перекрытием.
        ///   Ряд B (запад):   уступ 0.7 → склон 45° с перилами справа на настил 1.8 → узкий мост над провалом →
        ///                    крутой спуск 60° без перил (съехать).
        ///   Ряд C (восток):  пропасть с челноком → поворотный круг → лифт на балкон 1.8 → лестница вниз → финиш.
        /// Логика героя не меняется. Меню Fortress → Hero → Build Arena 6 (полоса препятствий).</summary>
        [MenuItem("Fortress/Hero/Build Arena 6 (полоса препятствий)")]
        public static void Build6()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Hero] арена 6: выйди из Play"); return; }
            var open = SceneManager.GetSceneByPath(Scene6Path);
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
            Fill6();
            EditorSceneManager.SaveScene(scene, Scene6Path);
            Debug.Log("[Hero] арена 6: " + Scene6Path);
        }

        static void Fill6()
        {
            const float W6 = 24f, D6 = 13.2f;
            const float AZ0 = 0f, AZ1 = 4f, BZ0 = 4.6f, BZ1 = 8.6f, CZ0 = 9.2f, CZ1 = 13.2f;   // ряды и стенки между ними
            const float E = 90f, Wst = -90f;                                                  // подписи: камера смотрит на восток / запад
            walls = new GameObject("Арена: стены").transform;
            surf = new GameObject("Арена: поверхность").transform;
            decor = new GameObject("Арена: декор (не опора)").transform;

            Box("Земля", -2f, W6 + 2f, -0.05f, 0f, -2f, D6 + 2f, GroundC, decor);
            // основа 0.6: везде, кроме тоннеля (ряд A, x 14–22.6) и пропасти (ряд C, x 3–7)
            Box("Основа A", 0f, 14f, 0f, MAIN, AZ0, BZ0, MainC, surf);
            Box("Основа A (за тоннелем)", 22.6f, W6, 0f, MAIN, AZ0, BZ0, MainC, surf);
            Box("Основа B", 0f, W6, 0f, MAIN, BZ0, CZ0, MainC, surf);
            Box("Основа C (до пропасти)", 0f, 3f, 0f, MAIN, CZ0, D6, MainC, surf);
            Box("Основа C", 7f, W6, 0f, MAIN, CZ0, D6, MainC, surf);

            // периметр и стенки между рядами (проходы в концах) — низкие, 45 см над основой: камера за спиной видит поверх
            Box("Стена юг", -0.3f, W6 + 0.3f, 0f, WallTop, -0.3f, 0f, WallC, walls);
            Box("Стена север", -0.3f, W6 + 0.3f, 0f, WallTop, D6, D6 + 0.3f, WallC, walls);
            Box("Стена запад", -0.3f, 0f, 0f, WallTop, 0f, D6, WallC, walls);
            Box("Стена восток", W6, W6 + 0.3f, 0f, WallTop, 0f, D6, WallC, walls);
            Box("Стенка A|B", 0f, 22.6f, 0f, WallTop, AZ1, BZ0, WallC, walls);           // проход на восток x 22.6–24
            Box("Стенка B|C", 2.4f, W6, 0f, WallTop, BZ1, CZ0, WallC, walls);             // проход на западе x 0–2.4

            Sign("ПОЛОСА ПРЕПЯТСТВИЙ\nстарт", new Vector3(1.2f, MAIN, 3.2f), 1f, E);

            // ===== РЯД A (на восток) =====
            // 1 слалом
            Sign("1 · слалом", new Vector3(1.2f, MAIN, 0.6f), 1f, E);
            foreach (var (x, z) in new[] { (2.0f, 1.3f), (2.9f, 2.7f), (3.8f, 1.3f) })
                Box("Столб", x - 0.15f, x + 0.15f, MAIN, MAIN + 0.6f, z - 0.15f, z + 0.15f, WallC, walls);
            // 2 ступени вверх и вниз по 12 см
            Sign("2 · ступени", new Vector3(4.4f, MAIN, 0.5f), 1f, E);
            float[] st = { 0.12f, 0.24f, 0.36f, 0.24f, 0.12f };
            float[] sx = { 4.8f, 5.2f, 5.6f, 6.8f, 7.2f, 7.6f };
            for (int i = 0; i < st.Length; i++) Box("Ступень " + (i + 1), sx[i], sx[i + 1], 0f, MAIN + st[i], AZ0, AZ1, i % 2 == 0 ? StepA : StepB, surf);
            // 3 уступ 0.3 — шагом
            Sign("3 · уступ 0.3\nшагом", new Vector3(8.0f, MAIN, 0.5f), 1f, E);
            Box("Уступ 0.3", 8.6f, 9.8f, 0f, MAIN + 0.3f, AZ0, AZ1, DeckC, surf);
            // 4 уступ 0.5 — подтянуться
            Sign("4 · уступ 0.5\nподтянуться", new Vector3(10.4f, MAIN, 0.5f), 1f, E);
            Box("Уступ 0.5", 10.9f, 12.4f, 0f, MAIN + 0.5f, AZ0, AZ1, DeckC, surf);
            Box("Уступ 0.5: ступень вниз", 12.4f, 12.8f, 0f, MAIN + 0.25f, AZ0, AZ1, StepA, surf);
            // 5 тоннель: съезд 0.6 → 0, низкое перекрытие, подъём
            Sign("5 · тоннель\nпод перекрытием", new Vector3(13.4f, MAIN, 0.5f), 1f, E);
            WedgeX("Съезд в тоннель", 16.8f, 14f, AZ0, AZ1, 0f, MAIN, TunC, surf);
            Box("Перекрытие тоннеля", 17.2f, 19.6f, MAIN - 0.08f, MAIN, AZ0, AZ1, MainC, surf);   // просвет 0.52
            WedgeX("Подъём из тоннеля", 20f, 22.6f, AZ0, AZ1, 0f, MAIN, TunC, surf);
            Sign("→ налево", new Vector3(23.3f, MAIN, 3.0f), 0.9f, 0f);

            // ===== РЯД B (на запад) =====
            // 6 уступ 0.7 — тяжело
            Sign("6 · уступ 0.7\nтяжело", new Vector3(21.6f, MAIN, 7.6f), 1f, Wst);
            Box("Уступ 0.7", 18.6f, 20.6f, 0f, MAIN + 0.7f, BZ0, BZ1, DeckC, surf);
            Box("Уступ 0.7: ступень вниз", 18.1f, 18.6f, 0f, MAIN + 0.35f, BZ0, BZ1, StepA, surf);
            // 7 склон 45° с перилами справа (на север — справа, когда смотришь на запад) на настил 1.8
            Sign("7 · склон 45°\nперила справа", new Vector3(17.4f, MAIN, 7.6f), 1f, Wst);
            float rz0 = 6.6f, rz1 = 8.0f, rTop = 15.6f, rBot = rTop + (UP - MAIN);       // 45°: ход = подъём
            WedgeX("Склон 45°", rBot, rTop, rz0, rz1, MAIN, UP - MAIN, TunC, surf);
            {
                float rx = rz1 - 0.06f;
                float H(float x) => MAIN + (UP - MAIN) * Mathf.Clamp01((rBot - x) / (rBot - rTop));
                for (int i = 0; i <= 3; i++) { float x = Mathf.Lerp(rBot - 0.03f, rTop + 0.03f, i / 3f); Post("Склон 45°: стойка", x, rx, MAIN, H(x) + Rail5, true); }
                Post("Склон 45°: стойка (низ)", rBot + 0.4f, rx, MAIN, MAIN + Rail5, true);
                Rail("Склон 45°: поручень (подход)", new Vector3(rBot + 0.4f, MAIN + Rail5, rx), new Vector3(rBot, MAIN + Rail5, rx));
                Rail("Склон 45°: поручень", new Vector3(rBot, MAIN + Rail5, rx), new Vector3(rTop, UP + Rail5, rx));
                Rail("Склон 45°: поручень (настил)", new Vector3(rTop, UP + Rail5, rx), new Vector3(rTop - 0.8f, UP + Rail5, rx));
            }
            // настил 1.8 в два куска с провалом между ними; 8 — узкий мост с перилами (зацепы)
            Box("Настил (восток)", 10.8f, rTop, 0f, UP, BZ0, BZ1, DeckC, surf);
            Box("Настил (запад)", 6f, 9.6f, 0f, UP, BZ0, BZ1, DeckC, surf);
            Sign("8 · узкий мост", new Vector3(11.6f, UP, 7.9f), 1f, Wst);
            Box("Мост", 9.6f, 10.8f, UP - 0.15f, UP, 6.1f, 7.1f, DeckC, surf);
            foreach (float z in new[] { 6.13f, 7.07f })
            {
                Rail("Мост: поручень", new Vector3(10.8f, UP + Rail5, z), new Vector3(9.6f, UP + Rail5, z));
                foreach (float x in new[] { 9.63f, 10.77f }) Post("Мост: стойка", x, z, UP, UP + Rail5, false);
            }
            // 9 крутой спуск 60° без перил — съехать
            Sign("9 · спуск 60°\nсъехать", new Vector3(6.9f, UP, 7.9f), 1f, Wst);
            WedgeX("Спуск 60°", 6f - (UP - MAIN) / Mathf.Tan(60f * Mathf.Deg2Rad), 6f, 5.0f, 8.2f, MAIN, UP - MAIN, TunC, surf);
            Sign("→ направо", new Vector3(1.2f, MAIN, 6.0f), 0.9f, Wst);

            // ===== РЯД C (на восток) =====
            // 10 пропасть с челноком
            Sign("10 · пропасть\nждать челнок", new Vector3(1.5f, MAIN, 12.6f), 1f, E);
            var sh = Mover("Челнок", new Vector3(4f, 0f, 11.2f), new Vector3(2f, 0f, 0f), 3f, 4f, 0f, 0f);
            Box("Челнок: площадка", 3f, 5f, PlatTop - 0.15f, PlatTop, 10.2f, 12.2f, PlatC, sh);
            Box("Челнок: метка", 3.1f, 4.9f, PlatTop, PlatTop + 0.003f, 11.1f, 11.3f, StripeC, sh);
            // 11 поворотный круг
            Sign("11 · поворотный круг", new Vector3(8.0f, MAIN, 12.7f), 1f, E);
            var disc = Mover("Поворотный круг", new Vector3(10.5f, 0f, 11.2f), Vector3.zero, 1f, 0f, 25f, 0f);
            HoldCyl(Cyl("Круг: столб", new Vector3(10.5f, PlatTop, 11.2f), 0.06f, 0.8f, RailC, disc));
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "Круг"; Object.DestroyImmediate(cyl.GetComponent<Collider>());
            cyl.transform.SetParent(disc, false);
            cyl.transform.localPosition = new Vector3(0f, PlatTop - 0.02f, 0f); cyl.transform.localScale = new Vector3(2.8f, 0.02f, 2.8f);
            cyl.GetComponent<MeshRenderer>().sharedMaterial = Mat("Круг", PlatC);
            Box("Круг: полоса", 10.4f, 10.6f, PlatTop, PlatTop + 0.003f, 9.9f, 12.5f, StripeC, disc);
            // 12 лифт на балкон 1.8 (балкон перегораживает ряд — только лифтом)
            Sign("12 · лифт", new Vector3(13.0f, MAIN, 12.7f), 1f, E);
            var lift = Mover("Лифт", new Vector3(14.5f, 0f, 11.2f), new Vector3(0f, UP - MAIN, 0f), 3f, 4f, 0f, 0f);
            Box("Лифт: площадка", 13.6f, 15.4f, PlatTop - 0.15f, PlatTop, 10.2f, 12.2f, PlatC, lift);
            Box("Лифт: метка", 14.4f, 14.6f, PlatTop, PlatTop + 0.003f, 10.3f, 12.1f, StripeC, lift);
            Box("Балкон 1.8", 15.4f, 19f, 0f, UP, CZ0, CZ1, DeckC, surf);
            // 13 лестница вниз и финиш
            var down = new float[10]; for (int i = 0; i < 10; i++) down[i] = UP - 0.12f * (i + 1);
            for (int i = 0; i < 10; i++) Box("Лестница вниз " + (i + 1), 19f + 0.3f * i, 19.3f + 0.3f * i, 0f, down[i], CZ0, CZ1, i % 2 == 0 ? StepA : StepB, surf);
            Sign("13 · лестница вниз", new Vector3(17.2f, UP, 12.7f), 1f, E);
            Box("Финиш", 22.2f, 23.8f, MAIN, MAIN + 0.004f, CZ0 + 0.2f, CZ1 - 0.2f, FinishC, decor);
            Sign("ФИНИШ", new Vector3(23f, MAIN, 11.2f), 1.2f, E);

            var sun = new GameObject("Свет").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.47f);

            PlaceHero(new Vector3(0.8f, 0f, 2.0f), 90f);
        }

        /// <summary>Герой — тот же префаб прототипа (герой + камера), на старте лицом на север, камера за спиной.</summary>
        static void PlaceHero(Vector3 start, float yaw = 0f)
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
            body.rotation = Quaternion.Euler(0f, yaw, 0f);                // лицом по yaw (0 — на север, +Z); камера встанет за спину
            proto.obstaclesRoot = walls;
            proto.surfaceRoot = surf;
            proto.showHints = false;                                      // подсказки выключены (автор: «вырубить индикаторы»)
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
