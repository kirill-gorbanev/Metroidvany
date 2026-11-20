using System.Text;
using Fortress.Hero.Proto;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Симуляция прототипа героя в Play без живых кадров: шаги вручную (платформы → герой → камера → сторожа).
    /// Вызывается из execute_code. Бот-бродяга (Fuzz), маршрут по точкам (Route), сценарий своим вводом (Run).</summary>
    public static class HeroSim
    {
        public const float Dt = 0.02f;
        public static HeroProto H; public static ProtoWatch W; public static ProtoMover[] Movers; public static ProtoOrbitCamera Cam;

        public static bool Setup()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Sim] нужен Play"); return false; }
            H = Object.FindObjectOfType<HeroProto>();
            if (!H) return false;
            Movers = Object.FindObjectsOfType<ProtoMover>();
            Cam = Object.FindObjectOfType<ProtoOrbitCamera>();
            W = new ProtoWatch(H);
            HeroProto.ManualTick = true;
            return true;
        }

        public static void End() { HeroProto.ManualTick = false; if (H) H.driver = null; }

        public static void Step()
        {
            foreach (var m in Movers) if (m) m.Step(Dt);
            H.Tick(Dt);
            if (Cam) Cam.Step(Dt);
            W.Check(Dt);
        }

        /// <summary>Перенести героя в точку (уровень — со своей поверхности), лицом по yaw.</summary>
        public static void Teleport(Vector3 p, float yaw) => H.DiagTeleport(p, yaw);

        /// <summary>Бот-бродяга: seconds симуляции, случайные цели по арене.</summary>
        public static string Fuzz(float seconds, int seed, float randomness = 1f)
        {
            if (!Setup()) return "нет Play / героя";
            try
            {
                var rng = new System.Random(seed * 7919 + 1);
                Bounds sb = H.DiagSurfaceBounds;
                var bot = new ProtoBot(H, H.cam, seed) { randomness = randomness };
                bot.pickTarget = () => new Vector3(Mathf.Lerp(sb.min.x + 0.5f, sb.max.x - 0.5f, (float)rng.NextDouble()), 0f,
                                                   Mathf.Lerp(sb.min.z + 0.5f, sb.max.z - 0.5f, (float)rng.NextDouble()));
                bot.Start(null);
                H.driver = bot.Frame;
                int n = Mathf.RoundToInt(seconds / Dt);
                for (int i = 0; i < n && !bot.trapped; i++) Step();
                return $"[бродяга seed {seed}, {seconds:F0} с] " + bot.Report() + "\n  " + W.Report();
            }
            finally { End(); }
        }

        /// <summary>Маршрут: дойти по точкам (бот без разброса). На каждую — не больше maxAttempts попыток.</summary>
        public static string Route(string name, Vector3[] pts, int maxAttempts = 60)
        {
            if (!Setup()) return "нет Play / героя";
            try
            {
                var sb = new StringBuilder($"[маршрут «{name}»] ");
                var bot = new ProtoBot(H, H.cam, 1) { randomness = 0f, maxTargetAttempts = 100000 };
                H.driver = bot.Frame;
                bool ok = true;
                for (int k = 0; k < pts.Length; k++)
                {
                    bot.Start(pts[k]); bot.reached = 0;
                    int startAtt = bot.attempts;
                    while (bot.reached == 0 && bot.attempts - startAtt < maxAttempts && !bot.trapped) Step();
                    bool hit = bot.reached > 0;
                    sb.Append($"\n  т{k + 1} ({pts[k].x:F1},{pts[k].z:F1}): {(hit ? "дошёл" : "НЕ ДОШЁЛ")} за {bot.attempts - startAtt} поп., тело y {H.GroundY:F2}");
                    if (!hit) { ok = false; break; }
                }
                sb.Insert(0, ok ? "OK " : "ПРОВАЛ ");
                sb.Append("\n  ").Append(bot.Report()).Append("\n  ").Append(W.Report());
                return sb.ToString();
            }
            finally { End(); }
        }

        /// <summary>Один стенд: поставить героя, дойти до цели (с высотой) ботом без разброса. ok — дошёл; att — попыток (жестов).</summary>
        public static string Stand(string name, Vector3 start, float yaw, Vector3 goal, bool expect, int maxAttempts, out bool ok, out int att, bool byHeight = false)
        {
            ok = false; att = 0;
            if (!Setup()) return "нет Play / героя";
            try
            {
                H.DiagTeleport(start, yaw);
                for (int i = 0; i < 5; i++) Step();                     // осесть
                var bot = new ProtoBot(H, H.cam, 3) { randomness = 0f, maxTargetAttempts = 100000 };
                H.driver = bot.Frame;
                bot.Start(goal);
                float maxY = H.BodyY;
                bool high = false;   // byHeight: стенд пройден, когда тело поднялось на высоту цели (бот дальше может уйти в угол)
                while (bot.reached == 0 && bot.attempts < maxAttempts && !bot.trapped && !high) { Step(); maxY = Mathf.Max(maxY, H.BodyY); high = byHeight && H.BodyY >= goal.y - 0.05f; }
                ok = bot.reached > 0 || high; att = bot.attempts;
                string verdict = ok == expect ? "как задумано" : "НЕ КАК ЗАДУМАНО";
                return $"{name}: {(ok ? "дошёл" : "не дошёл")} ({verdict}), жестов {att}, за зацеп {bot.holdAims}, выше всего {maxY:F2}, " +
                       $"конец ({H.BodyXZ.x:F2}, {H.BodyY:F2}, {H.BodyXZ.z:F2}), пустых {bot.dead}" +
                       (bot.events.Count > 0 ? "  " + string.Join("; ", bot.events) : "") + "  | " + W.Report();
            }
            finally { End(); }
        }

        static float X5(int i) => 0.6f + i * 2f + 0.7f;            // центр дорожки i на арене 5

        /// <summary>Все стенды арены 5 (вскарабкивание).</summary>
        public static string Arena5()
        {
            var sb = new StringBuilder("АРЕНА 5 — стенды\n");
            float M = 0.6f;
            (string n, int lane, float top, float goalZ, bool expect)[] st =
            {
                ("1 уступ 0.3", 0, M + 0.3f, 3.8f, true), ("2 уступ 0.5", 1, M + 0.5f, 3.8f, true),
                ("3 уступ 0.7", 2, M + 0.7f, 3.8f, true), ("4 уступ 1.0", 3, M + 1.0f, 3.8f, false),
                ("5 склон 30°", 4, 1.8f, 7f, true), ("6 склон 45° без перил", 5, 1.8f, 7f, false),
                ("7 склон 45° перила справа", 6, 1.8f, 7f, true), ("8 склон 60° перила справа", 7, 1.8f, 7f, true),
                ("9 склон 60° перила слева", 8, 1.8f, 7f, true), ("10 склон 60° без перил", 9, 1.8f, 7f, false),
                ("11 лестница", 10, 1.8f, 7f, true),
            };
            int good = 0;
            foreach (var s in st)
            {
                float x = X5(s.lane) + (s.lane == 8 ? -0.25f : s.lane >= 6 && s.lane <= 7 ? 0.25f : 0f);   // к перилам ближе
                float yaw = s.lane == 8 ? -60f : 0f;                     // перила слева: рука справа — к ним только боком (развернуться)
                sb.Append(Stand(s.n, new Vector3(x, M, 2.3f), yaw,new Vector3(x, s.top, s.goalZ), s.expect, 120, out bool ok, out _)).Append('\n');
                if (ok == s.expect) good++;
            }
            sb.Insert(0, $"как задумано {good}/{st.Length}\n");
            return sb.ToString();
        }

        /// <summary>Арена 6 — полоса целиком: бот идёт по точкам от старта до финиша (без переносов), отчёт по участкам.</summary>
        public static string Course6(bool perLeg = false)
        {
            if (!Setup()) return "нет Play / героя";
            var sb = new StringBuilder("ПОЛОСА (арена 6), бот от старта\n");
            (string n, Vector3[] pts)[] legs =
            {
                ("1 слалом", new[] { new Vector3(4.4f, 0f, 2f) }),
                ("2 ступени", new[] { new Vector3(6.2f, 0.96f, 2f), new Vector3(8.2f, 0.6f, 2f) }),
                ("3 уступ 0.3", new[] { new Vector3(9.2f, 0.9f, 2f) }),
                ("4 уступ 0.5", new[] { new Vector3(11.6f, 1.1f, 2f), new Vector3(13.4f, 0.6f, 2f) }),
                ("5 тоннель", new[] { new Vector3(18.4f, 0.01f, 2f), new Vector3(23.3f, 0.6f, 2f) }),
                ("поворот на ряд B", new[] { new Vector3(23.3f, 0.6f, 6.6f), new Vector3(21.8f, 0.6f, 6.6f) }),
                ("6 уступ 0.7", new[] { new Vector3(19.6f, 1.3f, 6.6f) }),
                ("7 склон 45° с перилами", new[] { new Vector3(14.8f, 1.8f, 7.5f) }),
                ("8 узкий мост", new[] { new Vector3(10.2f, 1.8f, 6.6f), new Vector3(8.4f, 1.8f, 6.6f) }),
                ("9 спуск 60°", new[] { new Vector3(4.4f, 0.6f, 6.6f) }),
                ("поворот на ряд C", new[] { new Vector3(1.2f, 0.6f, 6.6f), new Vector3(1.2f, 0.6f, 11.2f), new Vector3(2.6f, 0.6f, 11.2f) }),
                ("10 пропасть, челнок", new[] { new Vector3(8.0f, 0.6f, 11.2f) }),
                ("11 поворотный круг", new[] { new Vector3(9.4f, 0.6f, 10.0f), new Vector3(11.8f, 0.6f, 10.0f), new Vector3(12.8f, 0.6f, 11.2f) }),
                ("12 лифт", new[] { new Vector3(14.5f, 1.81f, 11.2f), new Vector3(17.6f, 1.8f, 11.2f) }),
                ("13 лестница, финиш", new[] { new Vector3(23f, 0.6f, 11.2f) }),
            };
            try
            {
                var bot = new ProtoBot(H, H.cam, 7) { randomness = 0f, maxTargetAttempts = 100000, reachRadius = 0.6f };
                H.driver = bot.Frame;
                int total = 0; bool ok = true;
                // старт каждого участка (для perLeg: участок проверяется сам по себе, с правильным лицом)
                var starts = new (Vector3 p, float yaw)[]
                {
                    (new Vector3(0.8f, 0.6f, 2f), 90f), (new Vector3(4.4f, 0.6f, 2f), 90f), (new Vector3(8.0f, 0.6f, 2f), 90f),
                    (new Vector3(10.2f, 0.9f, 2f), 90f), (new Vector3(13.4f, 0.6f, 2f), 90f), (new Vector3(23.2f, 0.6f, 2.4f), 0f),
                    (new Vector3(21.6f, 0.6f, 6.6f), -90f), (new Vector3(17.6f, 0.6f, 7.5f), -90f), (new Vector3(14.8f, 1.8f, 6.6f), -90f),
                    (new Vector3(8.4f, 1.8f, 6.6f), -90f), (new Vector3(4.4f, 0.6f, 6.6f), -90f), (new Vector3(2.6f, 0.6f, 11.2f), 90f),
                    (new Vector3(8.0f, 0.6f, 11.2f), 90f), (new Vector3(12.8f, 0.6f, 11.2f), 90f), (new Vector3(17.6f, 1.8f, 11.2f), 90f),
                };
                int li = -1;
                foreach (var leg in legs)
                {
                    li++;
                    if (perLeg) { H.DiagTeleport(starts[li].p, starts[li].yaw); W = new ProtoWatch(H); for (int i = 0; i < 5; i++) Step(); bot.trapped = false; ok = true; }
                    int a0 = bot.attempts, w0 = W.Total; float t0 = W.time;
                    foreach (var p in leg.pts)
                    {
                        bot.Start(p); bot.reached = 0; int s0 = bot.attempts;
                        while (bot.reached == 0 && bot.attempts - s0 < 150 && !bot.trapped) Step();
                        if (bot.reached == 0) { ok = false; break; }
                    }
                    int att = bot.attempts - a0; total += att;
                    sb.Append($"  {leg.n}: {(ok ? "прошёл" : "НЕ ПРОШЁЛ")}, жестов {att}, {W.time - t0:F0} с, тело ({H.BodyXZ.x:F1}, {H.BodyY:F2}, {H.BodyXZ.z:F1})" +
                              (W.Total > w0 ? $", сторожа +{W.Total - w0}" : "") + "\n");
                    if (perLeg && W.Total > w0) sb.Append("    ").Append(W.Report().Replace("\n", "\n    ")).Append('\n');
                    if (!ok && !perLeg) break;
                }
                sb.Insert(0, ok ? $"ФИНИШ: {total} жестов, {W.time:F0} с симуляции\n" : "НЕ ДОШЁЛ\n");
                sb.Append(bot.events.Count > 0 ? "  " + string.Join("; ", bot.events) + "\n" : "").Append("  ").Append(W.Report());
            }
            finally { End(); }
            return sb.ToString();
        }

        static HeroProto.InputFrame Hold(float dy = 0f) => new HeroProto.InputFrame { pressed = true, delta = new Vector2(0f, dy) };
        static HeroProto.InputFrame Free() => new HeroProto.InputFrame { pos = new Vector2(-9999f, -9999f) };

        /// <summary>Ближайшая к плечу точка зацепа (для сценариев).</summary>
        static bool NearestHold(out ProtoHold best, out float bestS)
        {
            best = null; bestS = 0f; float bd = float.MaxValue;
            foreach (var hd in H.Holds)
                for (int i = 0; i <= 60; i++)
                {
                    Vector3 P = hd.At(i / 60f);
                    float d = (P - H.ShoulderPos3).magnitude;
                    if (d < bd && d < H.L - 0.05f && P.z > H.BodyXZ.z) { bd = d; best = hd; bestS = i / 60f; }
                }
            return best;
        }

        static readonly StringBuilder feelLog = new StringBuilder();
        /// <summary>Перенос между замерами: сторожа — заново (перенос — не скачок), нарушения прошлого замера — в журнал.</summary>
        static void TP(Vector3 p, float yaw) { if (W.Total > 0) feelLog.Append('\n').Append("  ").Append(W.Report()); H.DiagTeleport(p, yaw); W = new ProtoWatch(H); }

        /// <summary>Критерии ощущения на арене 5 (цели геймдизайнера).</summary>
        public static string Feel5()
        {
            if (!Setup()) return "нет Play / героя";
            var sb = new StringBuilder("ОЩУЩЕНИЕ (арена 5)\n");
            try
            {
                float M = 0.6f;
                // 1. ВИСЕНИЕ: на середине 60° (перила справа) держит поручень 10 с — тело смещается ≤ 1 см
                float x8 = X5(7) + 0.35f;
                TP(new Vector3(x8, 1.2f, 5.65f), 0f);
                Step();
                if (!NearestHold(out var hd, out float hs)) sb.Append("висение: нет поручня рядом\n");
                else
                {
                    H.BeginGripHold(hd, hs);
                    H.driver = () => Hold();
                    for (int i = 0; i < 10; i++) Step();
                    Vector3 p0 = new Vector3(H.BodyXZ.x, H.BodyY, H.BodyXZ.z);
                    for (int i = 0; i < 500; i++) Step();
                    float drift = (new Vector3(H.BodyXZ.x, H.BodyY, H.BodyXZ.z) - p0).magnitude;
                    sb.Append($"1 висение 10 с на 60° за поручень: смещение {drift * 100f:F1} см (цель ≤ 1), склон под телом {H.SteepAngleUnderBody:F0}°, хват {(H.Gripping ? "держит" : "СОРВАЛСЯ")}\n");
                    // 2. ОТПУСТИЛ на 60°: знак сразу, потеря 0.3 м — не быстрее 0.35 с (60°)
                    H.driver = () => Free();
                    int signTick = -1, lostTick = -1;
                    for (int i = 0; i < 200; i++)
                    {
                        Step();
                        if (signTick < 0 && H.OnSteep && !H.Gripping) signTick = i;
                        if ((new Vector3(H.BodyXZ.x, H.BodyY, H.BodyXZ.z) - p0).magnitude >= 0.3f) { lostTick = i; break; }
                    }
                    sb.Append($"2 отпустил на 60°: знак через {(signTick + 1) * Dt * 1000f:F0} мс (цель ≤ 100), 0.3 м потеряно за {(lostTick < 0 ? "—" : ((lostTick + 1) * Dt).ToString("F2"))} с (цель ≥ 0.35)\n");
                }
                // 2б. то же на 45° (перила справа, дорожка 6)
                {
                    float x7 = X5(6) + 0.35f;
                    TP(new Vector3(x7, 1.0f, 5.3f), 0f); H.driver = () => Free(); Step();
                    Vector3 p0 = new Vector3(H.BodyXZ.x, H.BodyY, H.BodyXZ.z); int lost = -1;
                    for (int i = 0; i < 200; i++) { Step(); if ((new Vector3(H.BodyXZ.x, H.BodyY, H.BodyXZ.z) - p0).magnitude >= 0.3f) { lost = i; break; } }
                    sb.Append($"2б без опоры на 45°: 0.3 м потеряно за {(lost < 0 ? "—" : ((lost + 1) * Dt).ToString("F2"))} с (цель ≥ 0.6), склон {H.SteepAngleUnderBody:F0}°\n");
                }
                // 3. КИСТЬ НА КРУТОМ: хват за склон 45° без перил — съезжает и отпускает
                {
                    float x6 = X5(5);
                    TP(new Vector3(x6, M, 4.55f), 0f); H.driver = () => Free(); Step();
                    Vector3 c = new Vector3(x6 + 0.2f, 0f, 5.05f);
                    H.BeginGrip(c);
                    H.driver = () => Hold();
                    bool slip = H.GripSlipping; int rel = -1; Vector3 h0 = H.HandXZ;
                    for (int i = 0; i < 100; i++) { Step(); if (!H.Gripping) { rel = i; break; } }
                    sb.Append($"3 кисть на 45° без зацепа: съезжает {(slip ? "да" : "НЕТ")}, отпустила через {(rel < 0 ? "—" : ((rel + 1) * Dt).ToString("F2"))} с, съехала {(H.HandXZ - h0).magnitude * 100f:F0} см (цель 5–10)\n");
                }
                // 4. МАГНИТ: курсор на полу в 12–20 см от перил — сколько раз кисть прилипла к перилам (цель ≤ 5%)
                {
                    float x7 = X5(6);
                    TP(new Vector3(x7 + 0.3f, M, 3.9f), 0f); H.driver = () => Free();
                    for (int i = 0; i < 30; i++) Step();
                    var rng = new System.Random(5); int n = 0, snap = 0;
                    float railX = x7 + 1.4f - 0.06f;
                    for (int k = 0; k < 200; k++)
                    {
                        float off = 0.12f + 0.08f * (float)rng.NextDouble();
                        float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                        float z = 4.0f + 0.5f * (float)rng.NextDouble();
                        Vector3 fp = new Vector3(railX + side * off, M, z);
                        if (side > 0f) continue;                                  // по ту сторону перил — стенка дорожек, не пол
                        if (!H.DiagHandOk(fp)) continue;
                        n++;
                        if (H.DiagFindHold(H.cam.WorldToScreenPoint(fp))) snap++;
                    }
                    sb.Append($"4 магнит: курсор на полу в 12–20 см от перил — прилипло {snap} из {n} ({(n > 0 ? 100f * snap / n : 0f):F0}%, цель ≤ 5%)\n");
                }
                // 5. ПОДТЯГИВАНИЕ: сколько пикселей жеста от упора в кромку до перевала (цель: 0.7 м — одним жестом ≤ 600 px)
                foreach (var (lane, hgt) in new[] { (1, 0.5f), (2, 0.7f) })
                {
                    float x = X5(lane);
                    TP(new Vector3(x, M, 2.78f), 0f); H.driver = () => Free();
                    for (int i = 0; i < 20; i++) Step();
                    // кисть — дальше всего на верх уступа по прямой
                    Vector3 c = default; bool any = false;
                    for (float z = 3.6f; z > 3.0f; z -= 0.02f) { var q = new Vector3(H.ShoulderXZ.x, 0f, z); if (H.DiagHandOk(q)) { c = q; any = true; break; } }
                    if (!any) { sb.Append($"5 уступ {hgt}: кромку не взять\n"); continue; }
                    H.BeginGrip(c);
                    int px = 0; float top = M + hgt;
                    H.driver = () => Hold(-10f);
                    for (int i = 0; i < 200 && H.BodyY < top - 0.03f; i++) { Step(); px += 10; }
                    sb.Append($"5 уступ {hgt}: кисть на {c.z - 3f:F2} м за кромкой, перевал за {px} px жеста (цель ≤ 600), тело {H.BodyY:F2}\n");
                }
                TP(new Vector3(H.BodyXZ.x, H.BodyY, H.BodyXZ.z), 0f); sb.Append(feelLog.Length > 0 ? "сторожа:" + feelLog : "сторожа: нарушений нет");
            }
            finally { End(); }
            return sb.ToString();
        }

        // ================= КАРУСЕЛЬ (арена 4) =================
        static ProtoMover Disc(float speed) { foreach (var m in Movers) if (m && Mathf.Abs(m.spinSpeed - speed) < 0.1f) return m; return null; }
        static float R(ProtoMover m) => new Vector2(H.BodyXZ.x - m.transform.position.x, H.BodyXZ.z - m.transform.position.z).magnitude;
        static Vector3 OnDisc(ProtoMover m, float r, float ang) => m.transform.position + Quaternion.AngleAxis(ang, Vector3.up) * Vector3.forward * r + Vector3.up * 0.6f;

        /// <summary>Ближайшая к плечу достижимая точка зацепа этой карусели.</summary>
        static bool HoldOn(ProtoMover m, out ProtoHold best, out float bestS, bool pole)
        {
            best = null; bestS = 0f; float bd = float.MaxValue;
            foreach (var hd in H.Holds)
            {
                if (!hd || hd.GetComponentInParent<ProtoMover>() != m) continue;
                if (pole != hd.name.Contains("Столб")) continue;
                for (int i = 0; i <= 60; i++)
                {
                    Vector3 P = hd.At(i / 60f); float d = (P - H.ShoulderPos3).magnitude;
                    if (d < bd && d < H.L - 0.05f) { bd = d; best = hd; bestS = i / 60f; }
                }
            }
            return best;
        }

        /// <summary>Критерии ощущения карусели (цели геймдизайнера). Нужен Play на арене 4.</summary>
        public static string Spin4()
        {
            if (!Setup()) return "нет Play / героя";
            feelLog.Length = 0;
            var sb = new StringBuilder("КАРУСЕЛЬ (арена 4)\n");
            try
            {
                var d30 = Disc(30f); var d60 = Disc(60f); var d120 = Disc(120f);
                if (!d30 || !d60 || !d120) return "нет каруселей 30/60/120";
                for (int i = 0; i < 5; i++) Step();                      // ω посчитана

                // 1. 120°/с без опоры с r 0.7: знак, слёт, отлёт, угол к касательной
                TP(OnDisc(d120, 0.7f, 90f), 0f); H.driver = () => Free();
                int sign = -1, off = -1, stop = -1; Vector3 leave = default, tangent = default; float maxV = 0f;
                for (int i = 0; i < 400; i++)
                {
                    Vector3 before = H.BodyXZ;
                    Step();
                    if (sign < 0 && H.OnSpin) sign = i;
                    if (off < 0 && H.rideMover != d120)
                    {
                        off = i; leave = H.BodyXZ;
                        Vector3 rr = before - d120.transform.position; rr.y = 0f;
                        tangent = Vector3.Cross(Vector3.up, rr).normalized * Mathf.Sign(-d120.Omega);   // по вращению
                        Vector3 rt = Quaternion.AngleAxis(d120.Omega * Mathf.Rad2Deg * Dt, Vector3.up) * rr - rr; rt.y = 0f;
                        if (rt.sqrMagnitude > 1e-8f) tangent = rt.normalized;
                    }
                    maxV = Mathf.Max(maxV, H.FlingVel.magnitude);
                    if (off >= 0 && i > off + 2 && !H.Flung) { stop = i; break; }
                }
                Vector3 fl = H.BodyXZ - leave; fl.y = 0f;
                float dev = fl.sqrMagnitude > 1e-6f && tangent.sqrMagnitude > 0f ? Vector3.Angle(fl, tangent) : -1f;
                sb.Append($"1 120°/с без опоры, r 0.7: знак {(sign < 0 ? "—" : ((sign + 1) * Dt * 1000f).ToString("F0") + " мс")} (≤ 20), слёт через {(off < 0 ? "—" : ((off + 1) * Dt).ToString("F2") + " с")} (0.6–0.9), отлёт {fl.magnitude:F2} м (0.5–1.0), отклонение от касательной {dev:F0}° (≤ 30), макс. скорость {maxV:F2}\n");

                // 2. 120°/с поручень у края 10 с — сдвиг по радиусу
                ProtoHold h0 = null; foreach (var x in H.Holds) if (x && x.GetComponentInParent<ProtoMover>() == d120 && !x.name.Contains("Столб")) { h0 = x; break; }
                { Vector3 hp = h0.At(0.5f) - d120.transform.position; hp.y = 0f; float ang = Vector3.SignedAngle(Vector3.forward, hp, Vector3.up);
                  TP(OnDisc(d120, 0.8f, ang - 12f), ang); }
                H.driver = () => Free(); Step();
                if (!HoldOn(d120, out var hd, out float hs, false)) sb.Append("2 нет поручня рядом\n");
                else
                {
                    H.BeginGripHold(hd, hs); H.driver = () => Hold();
                    for (int i = 0; i < 5; i++) Step();
                    float r0 = R(d120);
                    for (int i = 0; i < 500; i++) Step();
                    sb.Append($"2 120°/с за поручень 10 с: r {r0:F2} → {R(d120):F2} (сдвиг {(R(d120) - r0) * 100f:F1} см, цель 0), хват {(H.Gripping ? "держит" : "СОРВАЛСЯ")}, на карусели {(H.rideMover == d120 ? "да" : "НЕТ")}\n");
                }

                // 3. без опоры 10 с: 30°/с у края r 1.3, 60°/с r 1.0 — сдвиг 0
                foreach (var (m, r, name) in new[] { (d30, 1.3f, "30°/с у края r 1.3"), (d60, 1.0f, "60°/с r 1.0") })
                {
                    TP(OnDisc(m, r, 0f), 0f); H.driver = () => Free(); Step();
                    float r0 = R(m);
                    for (int i = 0; i < 500; i++) Step();
                    sb.Append($"3 {name} без опоры 10 с: r {r0:F2} → {R(m):F2}, на карусели {(H.rideMover == m ? "да" : "НЕТ")}, зона {(H.OnSpin ? "ДА" : "нет")}\n");
                }
                // 3б. 60°/с у самого края r 1.38 — в зоне, уносит
                {
                    TP(OnDisc(d60, 1.38f, 0f), 0f); H.driver = () => Free(); Step();
                    int off2 = -1; for (int i = 0; i < 300; i++) { Step(); if (H.rideMover != d60) { off2 = i; break; } }
                    sb.Append($"3б 60°/с у кромки r 1.38 без опоры: слетел через {(off2 < 0 ? "— (держит)" : ((off2 + 1) * Dt).ToString("F2") + " с")}\n");
                }

                // 4. кисть на самом диске в зоне (120°/с): тело у центра, кисть на r ~0.9 — съезжает и отпускает
                {
                    TP(OnDisc(d120, 0.25f, 0f), 0f); H.driver = () => Free(); Step();
                    Vector3 c = OnDisc(d120, 0.75f, 0f); c.y = 0f;
                    H.BeginGrip(c); H.driver = () => Hold();
                    int rel = -1; bool slip = false;
                    for (int i = 0; i < 100; i++) { Step(); slip |= H.GripSlipping; if (!H.Gripping) { rel = i; break; } }
                    sb.Append($"4 120°/с кисть на диске r 0.75 (в зоне): съезжает {(slip ? "да" : "НЕТ")}, отпустила через {(rel < 0 ? "—" : ((rel + 1) * Dt).ToString("F2") + " с")}\n");
                }
                // 4б. кисть на диске вне зоны (60°/с, r 0.8) держит тело у края 5 с
                {
                    TP(OnDisc(d60, 1.3f, 0f), 180f); H.driver = () => Free(); Step();
                    Vector3 c = OnDisc(d60, 0.8f, 0f); c.y = 0f;
                    H.BeginGrip(c); H.driver = () => Hold();
                    for (int i = 0; i < 250; i++) Step();
                    sb.Append($"4б 60°/с кисть на диске r 0.8 (вне зоны), тело у края: хват {(H.Gripping ? "держит" : "СОРВАЛСЯ")}, r тела {R(d60):F2}, на карусели {(H.rideMover == d60 ? "да" : "НЕТ")}\n");
                }

                // 5. 120°/с: за столб, жест «на себя» вытягивает к центру из зоны
                {
                    TP(OnDisc(d120, 0.45f, 0f), 180f); H.driver = () => Free(); Step();
                    if (!HoldOn(d120, out var pole, out float ps, true)) sb.Append("5 столб не достать\n");
                    else
                    {
                        H.BeginGripHold(pole, ps); H.driver = () => Hold(-8f);
                        float r0 = R(d120);
                        for (int i = 0; i < 150; i++) Step();
                        sb.Append($"5 120°/с за столб, жест на себя 3 с: r {r0:F2} → {R(d120):F2}, хват {(H.Gripping ? "держит" : "СОРВАЛСЯ")}, зона {(H.OnSpin ? "да" : "нет")}\n");
                    }
                }
                // 6. кисть на неподвижном полу, тело на 120°/с: не длиннее досягаемости (раньше — на кадр длиннее)
                {
                    TP(OnDisc(d120, 1.2f, 180f), 180f); H.driver = () => Free(); Step();
                    Vector3 c = d120.transform.position + Vector3.back * 1.6f; c.y = 0f;
                    H.BeginGrip(c); H.driver = () => Hold();
                    int rel = -1; for (int i = 0; i < 100; i++) { Step(); if (!H.Gripping) { rel = i; break; } }
                    sb.Append($"6 120°/с кисть на неподвижном полу: отпустила через {(rel < 0 ? "— (держит)" : ((rel + 1) * Dt).ToString("F2") + " с")}\n");
                }
                TP(new Vector3(H.BodyXZ.x, H.BodyY, H.BodyXZ.z), 0f); sb.Append(feelLog.Length > 0 ? "сторожа:" + feelLog : "сторожа: нарушений нет");
            }
            finally { End(); }
            return sb.ToString();
        }

        // ================= ВЫСТУПЫ (арена 7) =================
        /// <summary>Ближайший к плечу достижимый камень (имя содержит key), точка впереди тела.</summary>
        static bool NearestNub(string key, out ProtoHold best, out float bestS)
        {
            best = null; bestS = 0f; float bd = float.MaxValue;
            foreach (var hd in H.Holds)
            {
                if (!(hd is ProtoNub) || !hd.name.Contains(key)) continue;
                for (int i = 0; i <= 30; i++)
                {
                    Vector3 P = hd.At(i / 30f); float d = (P - H.ShoulderPos3).magnitude;
                    if (d < bd && d < H.L - 0.05f && H.DiagHoldOk(P)) { bd = d; best = hd; bestS = i / 30f; }
                }
            }
            return best;
        }

        static string Sec(int i) => i < 0 ? "—" : ((i + 1) * Dt).ToString("F2") + " с";

        /// <summary>Критерии блока «хват за выступы» (цели геймдизайнера). Нужен Play на арене 7.</summary>
        public static string Ledge7()
        {
            var sb = new StringBuilder("ВЫСТУПЫ (арена 7)\n");
            float M = 0.6f;
            // A. стенды ботом: склоны с камнями / без, стена 1.2
            (string n, float x, Vector3 goal, bool expect)[] st =
            {
                ("1 60° с камнями", 1.3f, new Vector3(1.3f, 1.8f, 7f), true), ("2 60° без камней", 3.3f, new Vector3(3.3f, 1.8f, 7f), false),
                ("3 45° с камнями", 5.3f, new Vector3(5.3f, 1.8f, 7f), true), ("4 стена 1.2 с камнями", 7.3f, new Vector3(7.3f, 1.8f, 3.8f), false),
            };
            int good = 0;
            foreach (var s in st)
            {
                sb.Append(Stand(s.n, new Vector3(s.x, M, 2.3f), 0f, s.goal, s.expect, 120, out bool ok, out _, true)).Append('\n');
                if (ok == s.expect) good++;
            }
            sb.Insert(sb.ToString().IndexOf('\n') + 1, $"стенды как задумано {good}/{st.Length}\n");

            if (!Setup()) return sb + "нет Play / героя";
            feelLog.Length = 0;
            try
            {
                for (int i = 0; i < 5; i++) Step();
                // B1. ВИС 10 с на камне посреди 60° — смещение ≤ 1 см
                TP(new Vector3(1.4f, 1.2f, 5.45f), 0f); H.driver = () => Free(); Step();
                if (!NearestNub("Склон 1", out var hd, out float hs)) sb.Append("B1 нет камня рядом\n");
                else
                {
                    H.BeginGripHold(hd, hs); H.driver = () => Hold();
                    for (int i = 0; i < 10; i++) Step();
                    Vector3 p0 = new Vector3(H.BodyXZ.x, H.BodyY, H.BodyXZ.z);
                    for (int i = 0; i < 500; i++) Step();
                    float drift = (new Vector3(H.BodyXZ.x, H.BodyY, H.BodyXZ.z) - p0).magnitude;
                    sb.Append($"B1 висение 10 с на камне, склон {H.SteepAngleUnderBody:F0}°: смещение {drift * 100f:F1} см (цель ≤ 1), хват {(H.Gripping ? "держит" : "СОРВАЛСЯ")}\n");
                }
                // B2. ЗНАК: курсор на камень — подсветка и жёлтая кисть в первый же шаг (≤ 20 мс)
                ProtoHold stone5 = null; foreach (var x in H.Holds) if (x is ProtoNub && x.name.Contains("Низкая стена")) stone5 = x;
                ProtoHold rail5 = null; foreach (var x in H.Holds) if (!(x is ProtoNub) && x.name.Contains("Низкая стена")) rail5 = x;
                TP(new Vector3(9.0f, M, 3.0f), 0f); H.driver = () => Free();
                for (int i = 0; i < 20; i++) Step();
                {
                    Vector2 px = H.cam.WorldToScreenPoint(stone5.At(0.5f));
                    H.driver = () => new HeroProto.InputFrame { pos = px };
                    int sign = -1; for (int i = 0; i < 10; i++) { Step(); if (H.HoverNub) { sign = i; break; } }
                    sb.Append($"B2 курсор на камень: подсветка через {(sign < 0 ? "—" : ((sign + 1) * Dt * 1000f).ToString("F0") + " мс")} (цель ≤ 20)\n");
                }
                // C. МАГНИТ: курсор на полу в 8–30 см от камня — камень не крадёт пол (цель ≤ 5%); курсор на камне — 100%; поровну с поручнем — поручень
                {
                    var rng = new System.Random(7); int n = 0, snap = 0;
                    for (int k = 0; k < 300; k++)
                    {
                        float off = 0.08f + 0.22f * (float)rng.NextDouble();
                        Vector3 fp = new Vector3(8.75f + 0.4f * (float)rng.NextDouble(), M, 3.48f - off);
                        if (!H.DiagHandOk(fp)) continue;
                        n++;
                        if (H.DiagHoldAt(H.cam.WorldToScreenPoint(fp)) is ProtoNub) snap++;
                    }
                    int on = 0;
                    for (int k = 0; k <= 10; k++) if (H.DiagHoldAt(H.cam.WorldToScreenPoint(stone5.At(k / 10f))) == stone5) on++;
                    Vector3 mid = (stone5.B + rail5.A) * 0.5f;
                    var tie = H.DiagHoldAt(H.cam.WorldToScreenPoint(mid));
                    sb.Append($"C магнит: пол в 8–30 см от камня — прилипло {snap} из {n} ({(n > 0 ? 100f * snap / n : 0f):F0}%, цель ≤ 5%); курсор на камне — {on}/11 (цель 11); поровну камень/поручень — {(tie == rail5 ? "поручень" : tie == stone5 ? "КАМЕНЬ" : "ничего")} (цель поручень)\n");
                }
                // D. ВИС НА СТЕНЕ 1.2: камень 0.5 — подтянуться, отпустить — назад вниз
                {
                    TP(new Vector3(7.45f, M, 2.55f), 0f); H.driver = () => Free(); for (int i = 0; i < 10; i++) Step();
                    if (!NearestNub("Стена 1.2: камень 0.5", out var st4, out float s4)) sb.Append("D камень 0.5 не достать\n");
                    else
                    {
                        H.BeginGripHold(st4, s4); H.driver = () => Hold(-10f);
                        float maxLift = 0f; int px = 0, reach = -1;
                        for (int i = 0; i < 150 && H.Gripping; i++) { Step(); px += 10; if (H.ClimbLift > maxLift + 1e-4f) { maxLift = H.ClimbLift; reach = px; } }
                        bool held = H.Gripping;
                        H.driver = () => Hold(); for (int i = 0; i < 100; i++) Step();
                        float hang = H.ClimbLift;
                        H.driver = () => Free(); int land = -1;
                        for (int i = 0; i < 100; i++) { Step(); if (H.ClimbLift < 0.01f) { land = i; break; } }
                        sb.Append($"D стена 1.2, камень 0.5: подъём {maxLift * 100f:F0} см за {reach} px, хват {(held ? "держит" : "СОРВАЛСЯ")}, вис 2 с — {hang * 100f:F0} см, отпустил — на полу через {Sec(land)} (цель ≤ 0.5 с), тело {H.BodyY:F2}\n");
                    }
                }
                // E. ЧЕЛНОК: кисть на камне едет с ним; расхождение 0; не достаёт — срыв, сторож «рука длиннее» — 0
                {
                    TP(new Vector3(11.05f, M, 3.05f), 0f); H.driver = () => Free(); for (int i = 0; i < 3; i++) Step();
                    ProtoHold sn = null; float ss = 0f;
                    for (int i = 0; i < 400 && !NearestNub("Челнок", out sn, out ss); i++) Step();   // ждать, пока челнок подъедет (фаза зависит от прошлых прогонов)
                    if (!sn) sb.Append("E камень челнока не достать\n");
                    else
                    {
                        H.BeginGripHold(sn, ss); H.driver = () => Hold();
                        float maxGap = 0f; int rel = -1;
                        for (int i = 0; i < 500; i++)
                        {
                            Step();
                            if (!H.Gripping) { rel = i; break; }
                            Vector3 P = sn.At(ss); maxGap = Mathf.Max(maxGap, new Vector2(H.HandXZ.x - P.x, H.HandXZ.z - P.z).magnitude);
                        }
                        sb.Append($"E челнок: расхождение кисти и камня {maxGap * 100f:F1} см (цель 0), {(rel < 0 ? "держит 10 с" : "сорвался через " + Sec(rel))}, рука {(H.HandXZ - H.ShoulderXZ).magnitude:F2}\n");
                    }
                }
                // F. КАРУСЕЛЬ 120°/с: камень против ручки, 10 с хвата у края
                {
                    ProtoMover disc = Disc(120f);
                    foreach (bool nub in new[] { true, false })
                    {
                        ProtoHold h0 = null; foreach (var x in H.Holds) if (x && x.GetComponentInParent<ProtoMover>() == disc && (x is ProtoNub) == nub) { h0 = x; break; }
                        Vector3 hp = h0.At(0.5f) - disc.transform.position; hp.y = 0f; float ang = Vector3.SignedAngle(Vector3.forward, hp, Vector3.up);
                        TP(OnDisc(disc, 0.8f, ang - 12f), ang); H.driver = () => Free(); Step();
                        ProtoHold best = null; float bs = 0f, bd = float.MaxValue;
                        foreach (var x in H.Holds)
                        {
                            if (!x || x.GetComponentInParent<ProtoMover>() != disc || (x is ProtoNub) != nub) continue;
                            for (int i = 0; i <= 30; i++) { Vector3 P = x.At(i / 30f); float d = (P - H.ShoulderPos3).magnitude; if (d < bd && H.DiagHoldOk(P)) { bd = d; best = x; bs = i / 30f; } }
                        }
                        if (!best) { sb.Append($"F {(nub ? "камень" : "ручка")}: не достать\n"); continue; }
                        H.BeginGripHold(best, bs); H.driver = () => Hold();
                        for (int i = 0; i < 5; i++) Step();
                        float r0 = R(disc); int rel = -1;
                        for (int i = 0; i < 500; i++) { Step(); if (!H.Gripping) { rel = i; break; } }
                        sb.Append($"F 120°/с за {(nub ? "камень" : "ручку")} 10 с: r {r0:F2} → {R(disc):F2}, {(rel < 0 ? "держит" : "сорвался через " + Sec(rel))}, на карусели {(H.rideMover == disc ? "да" : "НЕТ")}\n");
                    }
                }
                TP(new Vector3(H.BodyXZ.x, H.BodyY, H.BodyXZ.z), 0f); sb.Append(feelLog.Length > 0 ? "сторожа:" + feelLog : "сторожа: нарушений нет");
            }
            finally { End(); }
            return sb.ToString();
        }

        /// <summary>Сценарий своим вводом: frame(i) → кадр ввода; шагов n.</summary>
        public static string Run(int n, System.Func<int, HeroProto.InputFrame> frame)
        {
            if (!Setup()) return "нет Play / героя";
            try
            {
                int i = 0;
                H.driver = () => frame(i);
                for (; i < n; i++) Step();
                return W.Report();
            }
            finally { End(); }
        }
    }
}
