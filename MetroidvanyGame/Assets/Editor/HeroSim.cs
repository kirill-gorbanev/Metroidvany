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
        public static string Stand(string name, Vector3 start, float yaw, Vector3 goal, bool expect, int maxAttempts, out bool ok, out int att)
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
                while (bot.reached == 0 && bot.attempts < maxAttempts && !bot.trapped) { Step(); maxY = Mathf.Max(maxY, H.BodyY); }
                ok = bot.reached > 0; att = bot.attempts;
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
                TP(H.BodyXZ, 0f); sb.Append(feelLog.Length > 0 ? "сторожа:" + feelLog : "сторожа: нарушений нет");
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
