using System.Text;
using Fortress.Hero.Arm2;
using UnityEditor;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Замеры нового героя (Arm2) в Play: ручные шаги НАСТОЯЩЕЙ физики (Physics.Simulate) — повторяемо и без живых кадров.
    /// Каждый замер начинается с проверки Play и героя (иначе прогон портит сохранённую сцену). Сторожа — по инвариантам
    /// из design/hero_arm_knowledge.md: И1 не в твёрдом (ComputePenetration теми же коллайдерами), И2 нет скачков, И3 нет дрожи.
    /// Позы берутся из Rigidbody (поза физики), не из Transform: при ручном шаге интерполированный Transform может отставать.</summary>
    public static class HeroArm2Sim
    {
        static Arm2Hero H; static float Dt;
        static SimulationMode savedMode; static float savedSleep;

        /// <summary>Для отладки: строка состояния хвата и подъёма.</summary>
        public static string GripLine() => H ? $"хват {H.state.gripped} вдоль {(H.state.gripped ? H.intent.TangentLength(H.arm.HandPos, H.state.gripNormal) : 0f):F2} хочет {H.state.pullLength:F2} мин {H.state.pullMin:F2} подъём {H.arm.Climbing} {H.state.liftForce:F0} Н тяга {H.state.pullVec.magnitude:F0} Н" : "";

        static string Begin()
        {
            if (!EditorApplication.isPlaying) return "нужен Play (сцена HeroArm2Sandbox)";
            H = Arm2Hero.Live;
            if (!H || !H.body || !H.body.Rb) return "нового героя (Arm2Hero) в сцене нет";
            Application.runInBackground = true;
            Dt = Time.fixedDeltaTime;
            savedMode = Physics.simulationMode; savedSleep = H.body.Rb.sleepThreshold;
            H.body.Rb.sleepThreshold = 0f;                          // сон прятал бы медленное сползание: с рукой на суставах тело спать не будет
            Arm2Hero.Manual = true; Physics.simulationMode = SimulationMode.Script;
            return null;
        }

        static void End()
        {
            foreach (var m in Arm2Mover.All) m.ResetPhase();
            if (H) { H.body.Rb.sleepThreshold = savedSleep; Arm2Sandbox.Place(H, Arm2Sandbox.Stations()[0]); }
            Physics.simulationMode = savedMode == SimulationMode.Script ? SimulationMode.FixedUpdate : savedMode;
            Arm2Hero.Manual = false;
        }

        /// <summary>Один шаг: платформы (порядок −100) → блоки героя (−50) → физика. Порядок — как в игре.</summary>
        static void Step()
        {
            foreach (var m in Arm2Mover.All) m.FixedStep(Dt);
            H.FixedStep(Dt);
            Physics.Simulate(Dt);
        }

        static readonly Collider[] hits = new Collider[32];
        /// <summary>И1: глубина наложения коробки тела с чужими коллайдерами (м) — тем же коллайдером, что двигается.</summary>
        static float BodyPenetration()
        {
            var rb = H.body.Rb; float worst = 0f;
            Vector3 pos = rb.position; Quaternion rot = rb.rotation;
            int n = Physics.OverlapSphereNonAlloc(pos, 0.3f, hits, ~0, QueryTriggerInteraction.Ignore);
            foreach (var mine in H.body.GetComponents<Collider>())
                for (int i = 0; i < n; i++)
                {
                    var c = hits[i]; if (!c || c.transform.IsChildOf(H.transform) || c.GetComponentInParent<Fortress.Hero.Proto.ProtoHold>()) continue;   // зацепы — только для руки, тело сквозь них проходит по задумке
                    var crb = c.attachedRigidbody;
                    Vector3 cp = crb ? crb.position : c.transform.position; Quaternion cr = crb ? crb.rotation : c.transform.rotation;
                    if (Physics.ComputePenetration(mine, pos, rot, c, cp, cr, out _, out float d)) worst = Mathf.Max(worst, d);
                }
            return worst;
        }

        class Stat
        {
            public float drift, maxDrift, maxStep, maxTilt, maxYaw, maxPen, landPen, swing, ms; public int jitter, steps;
        }

        /// <summary>Счётчик дрожи по одной оси (И3): разворот хода считается дрожью, если размах между разворотами > 2 мм
        /// (для углов — > 0.5°) и разворотов подряд два и больше за 0.25 с. Малые шаги направление не затирают.</summary>
        class Wobble
        {
            readonly float minSwing; float extreme, last; int dir, sinceTurn = 9999; bool has;
            public int count; public float maxSwing;
            /// <summary>Новый отрезок замера (цель сдвинулась): прошлое место — не разворот.</summary>
            public void Reset() { has = false; dir = 0; sinceTurn = 9999; }
            public Wobble(float minSwing) { this.minSwing = minSwing; }
            public void Add(float x, int window)
            {
                if (!has) { has = true; extreme = last = x; return; }
                sinceTurn++;
                int d = x > last ? 1 : x < last ? -1 : 0; last = x;
                if (d == 0) return;
                if (dir == 0) { dir = d; extreme = x; return; }
                if (d == dir) { extreme = x; return; }
                // разворот: размах от прошлого разворота
                float swing = Mathf.Abs(x - extreme);
                if (swing < minSwing * 0.1f) return;                 // шум в десятую порога — не разворот
                if (swing >= minSwing) { maxSwing = Mathf.Max(maxSwing, swing); if (sinceTurn <= window) count++; sinceTurn = 0; }
                dir = d; extreme = x;
            }
        }

        /// <summary>Прогон: settle шагов успокоения (наложение при посадке меряется), потом n шагов замера;
        /// место тела — в системе платформы (если есть), иначе в мире.</summary>
        static Stat Run(int settle, int n, Arm2Mover mover, Vector3 upExpected, float tiltWithin = 0f)
        {
            var s = new Stat(); var rb = H.body.Rb; var frame = mover ? mover.Rb : null;
            for (int i = 0; i < settle; i++) { Step(); s.landPen = Mathf.Max(s.landPen, BodyPenetration()); }
            Vector3 Rel() => frame ? Quaternion.Inverse(frame.rotation) * (rb.position - frame.position) : rb.position;
            Quaternion RelRot() => frame ? Quaternion.Inverse(frame.rotation) * rb.rotation : rb.rotation;
            float YawOf(Quaternion q) { Vector3 f = q * Vector3.forward; return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; }
            Vector3 start = Rel(), prev = start; float yaw0 = YawOf(RelRot());
            int window = Mathf.RoundToInt(0.25f / Dt);
            var wx = new Wobble(0.002f); var wy = new Wobble(0.002f); var wz = new Wobble(0.002f); var wt = new Wobble(0.5f);
            var sw = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < n; i++)
            {
                sw.Start(); Step(); sw.Stop();
                Vector3 p = Rel(); Quaternion q = RelRot();
                s.maxStep = Mathf.Max(s.maxStep, (p - prev).magnitude);
                s.maxDrift = Mathf.Max(s.maxDrift, (p - start).magnitude);
                float tilt = Vector3.Angle(q * Vector3.up, upExpected);
                if (tiltWithin <= 0f || (p - start).magnitude < tiltWithin) s.maxTilt = Mathf.Max(s.maxTilt, tilt);   // сползание: наклон — пока тело на склоне, не на переходе к полу
                s.maxYaw = Mathf.Max(s.maxYaw, Mathf.Abs(Mathf.DeltaAngle(yaw0, YawOf(q))));
                wx.Add(p.x, window); wy.Add(p.y, window); wz.Add(p.z, window); wt.Add(tilt, window);
                s.maxPen = Mathf.Max(s.maxPen, BodyPenetration());
                prev = p;
            }
            s.jitter = wx.count + wy.count + wz.count + wt.count;
            s.swing = Mathf.Max(wx.maxSwing, Mathf.Max(wy.maxSwing, wz.maxSwing));
            s.drift = (Rel() - start).magnitude; s.steps = n; s.ms = (float)sw.Elapsed.TotalMilliseconds / n;
            return s;
        }

        static string Line(string name, Stat s, string verdict) =>
            $"{name}: {verdict} | уход макс {s.maxDrift * 1000f:F1} мм | шаг за шаг физики макс {s.maxStep * 1000f:F2} мм | дрожь {s.jitter} | наклон макс {s.maxTilt:F1}° | " +
            $"рыскание макс {s.maxYaw:F1}° | в твёрдом макс {s.maxPen * 1000f:F1} мм (при посадке {s.landPen * 1000f:F1}) | {s.ms:F3} мс/шаг";

        /// <summary>СРЕЗ 1: тело на физике стоит. Станции песочницы: пол, ступень, уступ, склоны 20 / 32 / 36 / 45°, челнок, карусель;
        /// плюс падение с 0.4 м и край уступа (центр масс над опорой — стоит, за краем — падает).</summary>
        [MenuItem("Fortress/Hero2/Срез 1: замер тела (в Play)")]
        public static void Slice1Menu()
        {
            string r = Slice1(); Debug.Log(r);
            try { System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../tools/herosim/out/arm2_slice1.txt")), r); } catch { }
        }

        public static string Slice1()
        {
            string err = Begin(); if (err != null) return err;
            var sb = new StringBuilder($"СРЕЗ 1 — ТЕЛО НА ФИЗИКЕ (песочница), шаг физики {Dt * 1000f:F1} мс, трение тела {H.body.friction:F2}\n");
            int bad = 0, sec = Mathf.RoundToInt(1f / Dt);
            float standStep = 0.2f * Dt;                              // «стоит»: не быстрее 0.2 м/с ни на одном шаге (1 мм при шаге 5 мс)
            float moveStep = 4f * Dt;                                 // И2: движение — не быстрее 4 м/с
            try
            {
                foreach (var st in Arm2Sandbox.Stations())
                {
                    foreach (var m in Arm2Mover.All) m.ResetPhase();
                    Physics.SyncTransforms();
                    Arm2Sandbox.Place(H, st);
                    Arm2Mover mover = string.IsNullOrEmpty(st.mover) ? null : GameObject.Find(st.mover).GetComponent<Arm2Mover>();
                    Vector3 up = mover ? Vector3.up : st.rot * Vector3.up;
                    // успокоение 0.15 с (падение с 1 см зазора постановки); сползание — 0.8 с, пока тело ещё на склоне; платформы — 30 с
                    var s = Run(Mathf.RoundToInt(0.15f * sec), st.slides ? Mathf.RoundToInt(0.8f * sec) : (mover ? 30 : 4) * sec, mover, up, st.slides ? 0.6f : 0f);
                    bool ok = st.slides
                        ? s.maxDrift > 0.1f && s.maxStep < moveStep && s.maxTilt < 12f          // съехало, а не кувыркнулось (на склонах с камнями рука их задевает: наклон до 7°)
                        : s.maxDrift < 0.02f && s.maxStep < standStep && s.maxTilt < 2f && s.maxYaw < 2f && s.jitter == 0;
                    ok &= s.maxPen < 0.005f && s.landPen < 0.005f;
                    if (!ok) bad++;
                    sb.Append(Line(st.name, s, (ok ? "ок" : "ПЛОХО") + (st.slides ? " (должно сползти)" : " (должно стоять)"))).Append('\n');
                }
                // падение с 0.4 м на пол
                {
                    H.PlaceAtStart(new Vector3(0f, 0.4f + Arm2Body.Height * 0.5f, -0.8f), Quaternion.identity);
                    var s = Run(0, 2 * sec, null, Vector3.up);
                    float endY = H.body.Rb.position.y - Arm2Body.Height * 0.5f;
                    float endTilt = Vector3.Angle(H.body.Rb.rotation * Vector3.up, Vector3.up);      // в полёте тело с рукой чуть наклоняется — судим, как легло
                    bool ok = s.maxPen < 0.005f && s.maxStep < moveStep && s.jitter == 0 && Mathf.Abs(endY) < 0.005f && endTilt < 2f && s.maxTilt < 10f;
                    if (!ok) bad++;
                    sb.Append(Line("падение с 0.4", s, ok ? "ок (лёг ровно, без отскока)" : "ПЛОХО")).Append($" | низ тела {endY * 1000f:F1} мм над полом\n");
                }
                // край уступа 0.5 (южная грань z = 2.0): центр масс в 5 см от края над опорой — стоит; 3 см за краем — падает
                foreach (float off in new[] { 0.05f, -0.03f })
                {
                    H.PlaceAtStart(new Vector3(-1.4f, 0.5f + Arm2Body.Height * 0.5f + 0.005f, 2f + off), Quaternion.identity);
                    var s = Run(0, 2 * sec, null, Vector3.up);
                    bool fell = H.body.Rb.position.y < 0.4f, want = off < 0f;
                    bool ok = fell == want && s.maxPen < 0.005f && s.maxStep < moveStep; if (!ok) bad++;
                    sb.Append($"край уступа, центр масс {(off > 0f ? "над опорой " : "за краем ")}{Mathf.Abs(off) * 100f:F0} см: {(fell ? "упал" : "стоит")} — {(ok ? "ок" : "ПЛОХО")} | " +
                              $"шаг макс {s.maxStep * 1000f:F2} мм | в твёрдом макс {s.maxPen * 1000f:F1} мм\n");
                }
                // НЕВАЛЯШКА: тело положили на бок, вверх дном, поставили на попа — само встаёт (автор: «не нравится, как он сильно переворачивается»)
                foreach (var (name, rot) in new[] { ("на правом боку (крен 90°)", Quaternion.Euler(0f, 0f, -90f)), ("на левом боку (крен −90°)", Quaternion.Euler(0f, 0f, 90f)),
                                                    ("вверх дном (крен 178°)", Quaternion.Euler(0f, 0f, 178f)), ("на попа, лицом вверх", Quaternion.Euler(-88f, 0f, 0f)),
                                                    ("на попа, лицом вниз", Quaternion.Euler(88f, 0f, 0f)) })
                {
                    H.PlaceAtStart(new Vector3(0f, 0.3f, -0.8f), rot);
                    float tUp = -1f, tilt = 0f; var rb = H.body.Rb; float maxPen = 0f;
                    for (int i = 0; i < 4 * sec; i++)
                    {
                        Step(); tilt = Vector3.Angle(rb.rotation * Vector3.up, Vector3.up); maxPen = Mathf.Max(maxPen, BodyPenetration());
                        if (tilt < 10f) { if (tUp < 0f) tUp = i * Dt; } else tUp = -1f;
                    }
                    bool ok = tilt < 5f && tUp >= 0f && tUp < 2.5f && maxPen < 0.005f; if (!ok) bad++;
                    sb.Append($"неваляшка, {name}: {(ok ? "ок" : "ПЛОХО")} | {(tUp >= 0f ? $"встал за {tUp:F2} с" : "НЕ ВСТАЛ")}, через 4 с наклон {tilt:F1}° | в твёрдом макс {maxPen * 1000f:F1} мм\n");
                }
                sb.Append(bad == 0 ? "ИТОГО: всё ок\n" : $"ИТОГО: плохо в {bad} строках\n");
                sb.Append($"пороги: стоит — уход макс < 20 мм, шаг < {standStep * 1000f:F1} мм, наклон и рыскание < 2°, дрожь 0 (И3: размах > 2 мм или > 0.5°); " +
                          $"сползает — уход > 100 мм за 0.8 с, наклон < 5°; в твёрдом < 5 мм (И1); шаг < {moveStep * 1000f:F0} мм (И2); сон тела на время замера выключен\n");
            }
            finally { End(); }
            return sb.ToString();
        }

        /// <summary>ТРЕНИЕ: с какого угла коробка тела сползает (наклон силы тяжести на ровном полу = склон). Калибровка числа friction.</summary>
        public static string FrictionSweep()
        {
            string err = Begin(); if (err != null) return err;
            var sb = new StringBuilder($"ТРЕНИЕ: порог сползания, improvedPatchFriction = {Physics.improvedPatchFriction}\n");
            Vector3 g0 = Physics.gravity; float f0 = H.body.friction;
            try
            {
                foreach (float mu in new[] { 0.4f, 0.6f, 0.64f, 0.66f, 0.68f, 0.70f, 0.72f })
                    foreach (float yaw in new[] { 0f, 45f })
                    {
                        float thr = -1f;
                        for (float a = 10f; a <= 60f && thr < 0f; a += 1f)
                        {
                            H.body.friction = mu; H.body.ApplyTuning(); H.body.Rb.sleepThreshold = 0f;
                            Physics.gravity = Quaternion.Euler(a, 0f, 0f) * Vector3.down * 9.81f;
                            H.PlaceAtStart(new Vector3(0f, Arm2Body.Height * 0.5f + 0.005f, 0f), Quaternion.Euler(0f, yaw, 0f));
                            Vector3 p0 = H.body.Rb.position;
                            for (int i = 0; i < 300; i++) Physics.Simulate(Dt);
                            if ((H.body.Rb.position - p0).magnitude > 0.1f) thr = a;
                        }
                        sb.Append($"трение {mu:F2}, тело повёрнуто {yaw:0}°: сползает с {thr:0}° (по формуле {Mathf.Atan(mu) * Mathf.Rad2Deg:0}°)\n");
                    }
            }
            finally { Physics.gravity = g0; H.body.friction = f0; H.body.ApplyTuning(); End(); }
            return sb.ToString();
        }

        /// <summary>ПЛАВНОСТЬ ВЖИВУЮ: тело на челнок, замер в настоящих кадрах duration секунд. Потом — SmoothReport().</summary>
        public static string SmoothStart(float duration)
        {
            if (!EditorApplication.isPlaying) return "нужен Play";
            H = Arm2Hero.Live; if (!H) return "нового героя нет";
            Application.runInBackground = true;
            Arm2Sandbox.Station st = default;
            foreach (var x in Arm2Sandbox.Stations()) if (x.mover == Arm2Sandbox.ShuttleName) st = x;
            Arm2Sandbox.Place(H, st);
            var old = H.GetComponent<Arm2SmoothProbe>(); if (old) Object.Destroy(old);
            var p = H.gameObject.AddComponent<Arm2SmoothProbe>();
            p.body = H.body; p.mover = GameObject.Find(st.mover).GetComponent<Arm2Mover>(); p.duration = duration;
            return "замер плавности начат";
        }

        public static string SmoothReport()
        {
            var p = Arm2Hero.Live ? Arm2Hero.Live.GetComponent<Arm2SmoothProbe>() : null;
            if (!p) return "замер не начат";
            return (p.Done ? "" : "(ещё идёт) ") + p.Report();
        }

        static void RestoreStart(Arm2Recording rec)
        {
            foreach (var m in Arm2Mover.All) m.ResetPhase();          // фаза платформ в запись пока не пишется — добавить к срезу 8
            H.PlaceAtStart(rec.bodyPos, rec.bodyRot);
            // запись начата с уже зажатой ЛКМ: нажатия в ней нет — считаем, что кнопку нажали в первом кадре (иначе хват в повторе не запросится вовсе)
            if (rec.frames.Count > 0 && rec.frames[0].lmb && !rec.frames[0].lmbDown) { var f0 = rec.frames[0]; f0.lmbDown = true; rec.frames[0] = f0; }
            if (H.cam) { if (rec.hasCam) H.cam.SetView(rec.camYaw, rec.camPitch, rec.camDist); else H.cam.ResetView(); }
        }

        /// <summary>ПОВТОР ЗАПИСИ ВВОДА (последняя запись автора, F9) на новом герое — вживую, смотреть глазами.</summary>
        [MenuItem("Fortress/Hero2/Повтор последней записи ввода (в Play)")]
        public static void ReplayLatestMenu() { Debug.Log(ReplayLive(Arm2Recording.Latest())); }

        public static string ReplayLive(string file)
        {
            if (!EditorApplication.isPlaying) return "нужен Play";
            H = Arm2Hero.Live; if (!H) return "нового героя нет";
            if (string.IsNullOrEmpty(file)) return "записей нет: в игре F9 — начать и закончить запись";
            var rec = Arm2Recording.Load(file);
            RestoreStart(rec);
            H.input.StartPlayback(rec.frames);
            return $"повтор: {System.IO.Path.GetFileName(file)} — {rec.frames.Count} кадров, {rec.Duration:F1} с (записано на герое {rec.hero}, сцена {rec.scene})";
        }

        /// <summary>ПОВТОР ЗАПИСИ ручными шагами (повторяемый тест). Порядок — как в игре: шаги физики кадра тратят ввод
        /// ПРЕДЫДУЩЕГО кадра (FixedUpdate идёт до Update), время кадра обрезано до maxFrame.
        /// Возвращает метрики тела. По мере срезов сюда добавляются метрики руки.</summary>
        public static string ReplayMeasured(string file)
        {
            if (string.IsNullOrEmpty(file)) return "записей нет";
            string err = Begin(); if (err != null) return err;
            try
            {
                var rec = Arm2Recording.Load(file);
                RestoreStart(rec);
                float acc = 0f, maxStep = 0f, maxPen = 0f; int steps = 0; Vector3 prev = H.body.Rb.position, start = prev;
                foreach (var f in rec.frames)
                {
                    acc += Mathf.Min(f.dt, H.maxFrame);
                    while (acc >= Dt)
                    {
                        acc -= Dt; Step(); steps++;
                        Vector3 p = H.body.Rb.position; maxStep = Mathf.Max(maxStep, (p - prev).magnitude); prev = p;
                        maxPen = Mathf.Max(maxPen, BodyPenetration());
                    }
                    H.FrameStep(f);
                    H.LateStep(f.dt);
                }
                Vector3 e = H.body.Rb.position;
                return $"повтор {System.IO.Path.GetFileName(file)}: кадров {rec.frames.Count}, шагов физики {steps} | тело прошло {(e - start).magnitude:F3} м, конец ({e.x:F2}, {e.y:F2}, {e.z:F2}) | " +
                       $"шаг макс {maxStep * 1000f:F2} мм | в твёрдом макс {maxPen * 1000f:F1} мм";
            }
            finally { End(); }
        }

        // ================= СРЕЗ 2: РУКА-ЭФФЕКТОР БЕЗ ХВАТА =================
        const int ScreenW = 1920, ScreenH = 1080, StepsPerFrame = 2;       // кадр бота = 2 шага физики (100 к/с при шаге 5 мс)

        /// <summary>И1 для руки: глубина наложения кисти и обоих звеньев с миром, предметами и своим телом (м) — теми же коллайдерами.</summary>
        static float ArmPenetration(out string where)
        {
            float worst = 0f; where = "";
            foreach (var c in H.arm.colliders)
            {
                var rb = c.attachedRigidbody; Vector3 pos = rb.position; Quaternion rot = rb.rotation;
                int n = Physics.OverlapSphereNonAlloc(pos + rot * new Vector3(0f, 0f, 0.16f), 0.26f, hits, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var o = hits[i]; if (!o || o.attachedRigidbody == H.arm.upper || o.attachedRigidbody == H.arm.fore) continue;
                    var orb = o.attachedRigidbody;
                    Vector3 op = orb ? orb.position : o.transform.position; Quaternion oq = orb ? orb.rotation : o.transform.rotation;
                    if (Physics.ComputePenetration(c, pos, rot, o, op, oq, out _, out float d) && d > worst) { worst = d; where = o.name; }
                }
            }
            return worst;
        }

        /// <summary>И4: кисть в воздухе — в 8 см вокруг кисти нет ни одной поверхности (пол, стена, предмет; своё тело не в счёт).</summary>
        static bool HandFloating()
        {
            int n = Physics.OverlapSphereNonAlloc(H.arm.HandPos, H.intent.handRadius + 0.08f, hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (hits[i] && !hits[i].transform.IsChildOf(H.transform)) return false;
            return true;
        }

        class ArmStat
        {
            public string name; public int steps, floating, jitter, missed, settles, targetJumps;
            public float elbowWander, maxElbowStep, maxTargetJump, maxPen, maxHandStep, bodyMove, bodyYaw, itemMove, lagSettled, lagMax, arrive, msStep, msFrame; public string penWhere = "", note = "";
            public bool Ok(float stepLimit) => maxPen < 0.005f && maxHandStep < stepLimit && maxElbowStep < stepLimit && elbowWander < 0.01f && bodyMove < 0.01f && bodyYaw < 1f && itemMove < 0.01f && jitter == 0 && floating == 0;
            public string Line(bool ok) =>
                $"{name}: {(ok ? "ок" : "ПЛОХО")} | в твёрдом макс {maxPen * 1000f:F1} мм{(maxPen >= 0.005f ? " (" + penWhere + ")" : "")} | шаг кисти макс {maxHandStep * 1000f:F1} мм | " +
                $"шаг локтя макс {maxElbowStep * 1000f:F1} мм | локоть гуляет при стоящей цели {elbowWander * 1000f:F1} мм | скачков цели {targetJumps} (макс {maxTargetJump * 1000f:F0} мм) | дрожь {jitter} | кисть в воздухе {floating} шагов | тело сдвинуто {bodyMove * 1000f:F1} мм, {bodyYaw:F1}° | предметы сдвинуты {itemMove * 1000f:F1} мм | " +
                $"отставание: на месте {lagSettled * 1000f:F0} мм, в пути макс {lagMax * 1000f:F0} мм, догон {(settles > 0 ? arrive / settles * 1000f : 0f):F0} мс | " +
                $"{msStep:F3} мс/шаг, {msFrame:F3} мс/кадр намерения{note}";
        }

        class ArmRun
        {
            public ArmStat s = new ArmStat();
            Vector3 body0, prevHand, prevElbow, stillAt, lastTarget, prevCursor, prevFrameTarget; bool hasPrevCursor; float yaw0, still, sinceMove; bool arrived = true;
            Rigidbody[] items; Vector3[] items0;
            Wobble wx = new Wobble(0.002f), wy = new Wobble(0.002f), wz = new Wobble(0.002f); Box3 elbowBox;
            System.Diagnostics.Stopwatch swS = new System.Diagnostics.Stopwatch(), swF = new System.Diagnostics.Stopwatch(); int frames;

            public ArmRun(string name, Vector3 feet, float yaw) : this(name, feet + Vector3.up * (Arm2Body.Height * 0.5f + 0.005f), Quaternion.Euler(0f, yaw, 0f)) { }

            public ArmRun(string name, Vector3 centre, Quaternion rot)
            {
                s.name = name;
                foreach (var m in Arm2Mover.All) m.ResetPhase();
                H.PlaceAtStart(centre, rot);
                if (H.cam) H.cam.ResetView();
                int il = LayerMask.NameToLayer("Item"); var list = new System.Collections.Generic.List<Rigidbody>();
                foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude)) if (rb.gameObject.layer == il) list.Add(rb);
                items = list.ToArray();
                for (int i = 0; i < 60; i++) Step();                                           // успокоение 0.3 с: тело и рука легли
                body0 = H.body.Rb.position; yaw0 = H.body.Yaw(0f); prevHand = H.arm.HandPos; prevElbow = H.arm.ElbowPos; lastTarget = prevHand;
                items0 = new Vector3[items.Length]; for (int i = 0; i < items.Length; i++) items0[i] = items[i].position;
            }

            /// <summary>Кадр бота: курсор на мировую точку (или «мышь не трогали»), потом шаги физики под замером.
            /// expect — куда курсор должен попасть (проверка, что луч не заслонён); null — не проверять.</summary>
            bool lastLmb;
            public void Frame(Vector3 cursorWorld, bool checkCursor = true, bool lmb = false, Vector2 delta = default)
            {
                Vector3 vp = H.intent.cam.WorldToViewportPoint(cursorWorld);
                var f = new Arm2InputFrame { dt = Dt * StepsPerFrame, pos = new Vector2(vp.x * ScreenW, vp.y * ScreenH), screenW = ScreenW, screenH = ScreenH, lmb = lmb, lmbDown = lmb && !lastLmb, delta = delta };
                lastLmb = lmb;
                swF.Start(); H.FrameStep(f); swF.Stop(); frames++;
                if (checkCursor && (!H.state.cursorValid || Vector3.Distance(H.state.cursorPoint, cursorWorld) > 0.03f)) s.missed++;
                // скачок цели: курсор сдвинулся мало, а цель — намного дальше (срыв со стены на пол, зеркало из-за спины)
                if (hasPrevCursor && H.state.handTargetValid)
                {
                    Vector3 cur = H.state.cursorValid ? H.state.cursorPoint : cursorWorld;        // настоящая точка под курсором: переход с пола на грань столба — не скачок
                    float jump = (H.state.handTarget - prevFrameTarget).magnitude - 3f * (cur - prevCursor).magnitude;
                    if (jump > 0.05f) { s.targetJumps++; s.maxTargetJump = Mathf.Max(s.maxTargetJump, jump); }
                }
                prevCursor = H.state.cursorValid ? H.state.cursorPoint : cursorWorld; prevFrameTarget = H.state.handTarget; hasPrevCursor = H.state.handTargetValid;
                H.LateStep(f.dt);
                for (int k = 0; k < StepsPerFrame; k++)
                {
                    swS.Start(); Step(); swS.Stop(); s.steps++;
                    Vector3 hp = H.arm.HandPos, tg = H.state.handTarget;
                    s.maxHandStep = Mathf.Max(s.maxHandStep, (hp - prevHand).magnitude); prevHand = hp;
                    Vector3 ep = H.arm.ElbowPos; s.maxElbowStep = Mathf.Max(s.maxElbowStep, (ep - prevElbow).magnitude); prevElbow = ep;
                    float pen = ArmPenetration(out string w); if (pen > s.maxPen) { s.maxPen = pen; s.penWhere = w; }
                    pen = BodyPenetration(); if (pen > s.maxPen) { s.maxPen = pen; s.penWhere = "тело"; }
                    s.bodyMove = Mathf.Max(s.bodyMove, (H.body.Rb.position - body0).magnitude);
                    s.bodyYaw = Mathf.Max(s.bodyYaw, Mathf.Abs(Mathf.DeltaAngle(yaw0, H.body.Yaw(yaw0))));
                    for (int i = 0; i < items.Length; i++) s.itemMove = Mathf.Max(s.itemMove, (items[i].position - items0[i]).magnitude);
                    float lag = Vector3.Distance(hp, tg);
                    // обход преграды (Arm2Arm.Detouring) — намеренный ход руки, не покой
                    if ((tg - stillAt).magnitude > 0.002f || H.arm.Detouring) { stillAt = tg; still = 0f; wx.Reset(); wy.Reset(); wz.Reset(); elbowBox = default;   // «цель стоит» — от места, где встала (медленный обвод — не покой)
                         if (arrived && lag > 0.05f) { arrived = false; sinceMove = 0f; } }
                    else still += Dt;
                    lastTarget = tg; sinceMove += Dt;
                    if (!arrived && lag < 0.03f) { arrived = true; s.arrive += sinceMove; s.settles++; }
                    s.lagMax = Mathf.Max(s.lagMax, lag);
                    if (still > 0.5f && HandFloating()) s.floating++;                    // И4: кисть ВСТАЛА и висит в воздухе (в пути над уступом — не в счёт)
                    if (still > 0.8f) { elbowBox.Add(H.arm.ElbowPos); s.elbowWander = Mathf.Max(s.elbowWander, elbowBox.Size); }
                    if (still > 0.5f) { s.lagSettled = Mathf.Max(s.lagSettled, lag); wx.Add(hp.x, 50); wy.Add(hp.y, 50); wz.Add(hp.z, 50); }
                }
            }

            public void Hold(Vector3 cursorWorld, float seconds, bool checkCursor = true)
            { int n = Mathf.RoundToInt(seconds / (Dt * StepsPerFrame)); for (int i = 0; i < n; i++) Frame(cursorWorld, checkCursor); }

            public void Sweep(Vector3 a, Vector3 b, float seconds, bool checkCursor = true)
            { int n = Mathf.RoundToInt(seconds / (Dt * StepsPerFrame)); for (int i = 0; i < n; i++) Frame(Vector3.Lerp(a, b, (i + 1f) / n), checkCursor); }

            public ArmStat Done()
            {
                s.jitter = wx.count + wy.count + wz.count;
                s.msStep = s.steps > 0 ? (float)swS.Elapsed.TotalMilliseconds / s.steps : 0f;
                s.msFrame = frames > 0 ? (float)swF.Elapsed.TotalMilliseconds / frames : 0f;
                if (s.missed > 0) s.note += $" | курсор заслонён {s.missed} кадров";
                return s;
            }
        }

        [MenuItem("Fortress/Hero2/Срез 2: замер руки (в Play)")]
        public static void Slice2Menu()
        {
            string r = Slice2(); Debug.Log(r);
            try { System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../tools/herosim/out/arm2_slice2.txt")), r); } catch { }
        }

        /// <summary>СРЕЗ 2: рука-эффектор без хвата. Сценарии песочницы: обвод по полу, рывки, стена (ощупать вверх), столб,
        /// ступень и уступ сверху, предметы, своё тело. Сторожа на каждый шаг физики: И1 (рука и тело не в твёрдом), И2 (шаг кисти),
        /// И3 (дрожь при неподвижной цели), И4 (кисть не в воздухе), И5 (тело и предметы не сдвинуты).</summary>
        public static string Slice2()
        {
            string err = Begin(); if (err != null) return err;
            if (!H.arm || !H.intent) { End(); return "у героя нет руки (пересобрать префаб)"; }
            var sb = new StringBuilder($"СРЕЗ 2 — РУКА БЕЗ ХВАТА (песочница), шаг физики {Dt * 1000f:F1} мс; привод: жёсткость {H.arm.stiffness:F0}, демпфер {H.arm.damping:F0}, сила {H.arm.maxForce:F0} Н\n");
            int bad = 0; float stepLimit = 6f * Dt;                                    // И2: кисть не быстрее 6 м/с (3 см за шаг 5 мс)
            float r = H.intent.handRadius;
            void Add(ArmStat s, bool extra = true, string extraText = "")
            { bool ok = s.Ok(stepLimit) && extra; if (!ok) bad++; sb.Append(s.Line(ok)).Append(extraText).Append('\n'); }
            try
            {
                Vector3 Sh() => H.intent.ShoulderWorld();
                Vector3 Floor(float deg, float rad) { Vector3 s = Sh(); var d = Quaternion.AngleAxis(deg, Vector3.up) * Vector3.forward * rad; return new Vector3(s.x + d.x, 0f, s.z + d.z); }

                {   // 1 — обвод по полу: кисть ходит по точкам перед плечом и стоит на каждой
                    var run = new ArmRun("1 пол: обвод", Vector3.zero, 0f);
                    foreach (float rad in new[] { 0.30f, 0.50f }) foreach (float deg in new[] { -40f, 0f, 30f, 60f }) run.Hold(Floor(deg, rad), 0.6f);
                    run.Sweep(Floor(60f, 0.5f), Floor(-40f, 0.5f), 1.5f);
                    var s = run.Done(); Add(s, s.lagSettled < 0.03f && s.settles > 0 && s.arrive / s.settles < 0.3f);
                }
                {   // 2 — рывки: цель прыгает через весь сектор
                    var run = new ArmRun("2 пол: рывки", Vector3.zero, 0f);
                    for (int i = 0; i < 6; i++) { run.Hold(Floor(60f, 0.5f), 0.5f); run.Hold(Floor(-40f, 0.5f), 0.5f); }
                    var s = run.Done(); Add(s, s.lagSettled < 0.03f && s.settles > 0 && s.arrive / s.settles < 0.3f);
                }
                {   // 3 — стена: курсор ведут вверх по грани, кисть скользит по ней (п. 4 списка автора)
                    var run = new ArmRun("3 стена: ощупать вверх", new Vector3(0f, 0f, 3.55f), 0f);
                    float x = Sh().x + 0.05f; float worstZ = 0f, worstY = 0f;
                    run.Hold(new Vector3(x, 0.06f, 4f), 0.6f);
                    int n = 300; float topY = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float y = Mathf.Lerp(0.06f, 1.0f, (i + 1f) / n);                      // выше 0.63 рука уже не достаёт: кисть должна остаться на грани, не сорваться на пол
                        run.Frame(new Vector3(x, y, 4f));
                        Vector3 hp = H.arm.HandPos; worstZ = Mathf.Max(worstZ, Mathf.Abs(hp.z - (4f - r)));
                        if (y < 0.55f) worstY = Mathf.Max(worstY, Mathf.Abs(hp.y - y));
                        topY = Mathf.Max(topY, hp.y);
                    }
                    run.Hold(new Vector3(x, 1.0f, 4f), 1.6f);
                    float endY = H.arm.HandPos.y;
                    run.Sweep(new Vector3(x, 1.0f, 4f), new Vector3(x, 0.6f, 4f), 0.5f);
                    run.Sweep(new Vector3(x, 0.6f, 4f), new Vector3(x - 0.3f, 0.3f, 4f), 1f);
                    var s = run.Done();
                    Add(s, worstZ < 0.03f && worstY < 0.05f && s.lagSettled < 0.03f && s.targetJumps == 0 && endY > 0.55f, $" | от грани стены макс {worstZ * 1000f:F0} мм, от высоты курсора макс {worstY * 1000f:F0} мм, курсор на 1.0 м — кисть держится на {endY:F2} м");
                }
                {   // 4 — столб: курсор ведут по полу «сквозь» столб и держат на его грани
                    var run = new ArmRun("4 столб", new Vector3(0.1f, 0f, 0.55f), 0f);
                    run.Sweep(new Vector3(0.25f, 0f, 1.0f), new Vector3(0.85f, 0f, 1.0f), 1.5f, false);
                    run.Sweep(new Vector3(0.85f, 0f, 1.0f), new Vector3(0.25f, 0f, 1.0f), 1.5f, false);
                    run.Hold(new Vector3(0.55f, 0.3f, 0.9f), 0.8f, false);
                    var s = run.Done(); Add(s);
                }
                {   // 5 — ступень 0.3 и уступ 0.5: кисть ложится на верх
                    var run = new ArmRun("5 ступень 0.3 сверху", new Vector3(-3f, 0f, 1.55f), 0f);
                    run.Hold(new Vector3(-2.8f, 0f, 1.9f), 0.5f); run.Hold(new Vector3(-2.8f, 0.3f, 2.12f), 0.8f);
                    float y1 = H.arm.HandPos.y; run.Hold(new Vector3(-2.8f, 0f, 1.9f), 0.6f);
                    var s = run.Done(); Add(s, Mathf.Abs(y1 - (0.3f + r)) < 0.02f && s.lagSettled < 0.03f, $" | кисть на верхе: {y1:F3} (верх + радиус {0.3f + r:F3})");
                    run = new ArmRun("5 уступ 0.5 сверху", new Vector3(-1.4f, 0f, 1.55f), 0f);
                    run.Hold(new Vector3(-1.2f, 0f, 1.9f), 0.5f); run.Hold(new Vector3(-1.2f, 0.5f, 2.07f), 0.8f);
                    y1 = H.arm.HandPos.y; run.Hold(new Vector3(-1.2f, 0f, 1.9f), 0.6f);
                    s = run.Done(); Add(s, Mathf.Abs(y1 - (0.5f + r)) < 0.02f && s.lagSettled < 0.03f, $" | кисть на верхе: {y1:F3} (верх + радиус {0.5f + r:F3})");
                }
                {   // 6 — предметы: кисть ведут по полу через кубик и кладут на него (решение автора: твёрдые, но несдвигаемые)
                    var run = new ArmRun("6 предметы", new Vector3(-0.3f, 0f, 0.45f), 0f);
                    run.Sweep(new Vector3(-0.2f, 0f, 0.8f), new Vector3(-0.62f, 0f, 0.8f), 1.2f, false);
                    run.Sweep(new Vector3(-0.62f, 0f, 0.8f), new Vector3(-0.2f, 0f, 0.8f), 1.2f, false);
                    run.Hold(new Vector3(-0.45f, 0.12f, 0.8f), 0.8f, false);
                    string top = $" | на кубике: локоть {H.arm.ElbowPos:F3} кисть {H.arm.HandPos:F3} цель {H.state.handTarget:F3} курсор {H.state.cursorPoint:F3}";
                    run.Sweep(new Vector3(-0.45f, 0f, 0.95f), new Vector3(-0.45f, 0f, 0.6f), 1.2f, false);
                    var s = run.Done(); Add(s, true, top + $" | конец: кисть {H.arm.HandPos:F3}, цель {H.state.handTarget:F3}");
                }
                {   // 7 — своё тело: курсор слева за сектором, за спиной (зеркало), на самом теле
                    var run = new ArmRun("7 своё тело", Vector3.zero, 0f);
                    run.Hold(new Vector3(-0.5f, 0f, 0.1f), 0.7f, false); run.Hold(new Vector3(0f, 0f, -0.5f), 0.7f, false);
                    run.Hold(new Vector3(0f, 0.2f, 0f), 0.7f, false); run.Hold(new Vector3(-0.25f, 0f, 0.25f), 0.7f, false);
                    run.Sweep(new Vector3(-0.5f, 0f, 0.3f), new Vector3(0.6f, 0f, -0.4f), 1.5f, false);
                    var s = run.Done(); Add(s);
                }
                {   // 8 — склон 32° (запас трения тела самый малый): рывки руки не должны стронуть тело
                    Arm2Sandbox.Station st32 = default; int ri = 0;
                    for (int i = 0; i < Arm2Sandbox.RampAngles.Length; i++) if (Mathf.Approximately(Arm2Sandbox.RampAngles[i], 32f)) ri = i;
                    foreach (var x in Arm2Sandbox.Stations()) if (x.name.Contains("склон 32")) st32 = x;
                    var run = new ArmRun("8 склон 32°: рывки", st32.center, st32.rot);
                    Vector3 top = Arm2Sandbox.RampTop(ri, out Vector3 nrm);
                    Vector3 OnRamp(float deg, float rad)
                    {
                        Vector3 sh = Sh(); Vector3 basePt = sh - nrm * Vector3.Dot(sh - top, nrm);
                        return basePt + st32.rot * (Quaternion.AngleAxis(deg, Vector3.up) * Vector3.forward * rad);
                    }
                    for (int i = 0; i < 5; i++) { run.Hold(OnRamp(60f, 0.45f), 0.5f); run.Hold(OnRamp(-40f, 0.45f), 0.5f); }
                    var s = run.Done(); Add(s, s.lagSettled < 0.03f);
                }
                // 9 — ПРЕГРАДЫ (станции Z X C V B): бот водит курсор по случайным точкам вокруг — по преградам, в щели, под полку, за край.
                // Успеха здесь нет — только сторожа: не в твёрдом, без скачков и дрожи, тело на месте.
                {
                    var obst = Arm2Sandbox.ObstacleStations();
                    for (int oi = 0; oi < obst.Length; oi++)
                    {
                        Vector3 O = Arm2Sandbox.ObstacleOrigin(oi);
                        var run = new ArmRun("9 преграды " + obst[oi].name, O, 0f);
                        var rng = new System.Random(1234 + oi);
                        Vector3 P() => O + new Vector3(Mathf.Lerp(-0.45f, 0.8f, (float)rng.NextDouble()), new[] { 0f, 0.1f, 0.25f, 0.4f }[rng.Next(4)], Mathf.Lerp(0.05f, 0.8f, (float)rng.NextDouble()));
                        Vector3 from = P();
                        for (int k = 0; k < 24; k++)
                        {
                            Vector3 to = P(); float sp = k % 3 == 0 ? 3f : 0.8f;                       // каждый третий ход — рывок
                            run.Sweep(from, to, Mathf.Max(0.1f, Vector3.Distance(from, to) / sp), false);
                            if (k % 4 == 3) run.Hold(to, 1.0f, false);
                            from = to;
                        }
                        var s = run.Done(); Add(s);
                    }
                }
                sb.Append(bad == 0 ? "ИТОГО: всё ок\n" : $"ИТОГО: плохо в {bad} строках\n");
                sb.Append($"пороги: в твёрдом < 5 мм (И1); шаг кисти < {stepLimit * 1000f:F0} мм (И2); дрожь 0 и локоть гуляет < 10 мм при неподвижной цели (И3); кисть в воздухе 0 (И4, зазор > 8 см); " +
                          "тело < 10 мм и < 1°, предметы < 10 мм (И5); отставание на месте < 30 мм; догон в сценариях 1–2 < 300 мс; на стене скачков цели 0\n");
            }
            finally { End(); }
            return sb.ToString();
        }

        // ================= ПОВТОР ЗАПИСИ АВТОРА С ЗАМЕРОМ РУКИ =================
        struct Box3
        {
            Vector3 lo, hi; bool has;
            public void Add(Vector3 p) { if (!has) { lo = hi = p; has = true; } else { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); } }
            public float Size => has ? (hi - lo).magnitude : 0f;
        }

        /// <summary>Повтор записи ввода ручными шагами (как в игре: шаги кадра тратят ввод прошлого кадра, время кадра — из записи).
        /// ТРЯСКА: пока мышь стоит (после 0.15 с покоя) — размах кисти, локтя, цели, призрака, тела. Покажет, кто трясёт: цель или физика.
        /// СКАЧКИ: кадры, где цель ушла намного дальше, чем точка под курсором, — время, размер, откуда и куда.</summary>
        public static string ReplayArm(string file)
        {
            if (string.IsNullOrEmpty(file)) return "записей нет";
            string err = Begin(); if (err != null) return err;
            var sb = new StringBuilder();
            try
            {
                var rec = Arm2Recording.Load(file);
                RestoreStart(rec);
                float acc = 0f, t = 0f, still = 0f; int steps = 0, stillPeriods = 0;
                Box3 bHand = default, bElbow = default, bTarget = default, bGhost = default, bBody = default;
                float wHand = 0f, wElbow = 0f, wTarget = 0f, wGhost = 0f, wBody = 0f, wYaw = 0f, yawLo = 0f, yawHi = 0f, maxHandStep = 0f, maxPen = 0f;
                Vector3 prevHand = H.arm.HandPos, prevTarget = H.state.handTarget, prevCursor = Vector3.zero; bool hasPrev = false;
                var jumps = new System.Collections.Generic.List<string>(); int jumpCount = 0; float lagStillMax = 0f, maxTilt = 0f, tiltTime = 0f, grippedTime = 0f;
                void ClosePeriod()
                {
                    if (still > 0.3f)
                    {
                        stillPeriods++;
                        wHand = Mathf.Max(wHand, bHand.Size); wElbow = Mathf.Max(wElbow, bElbow.Size); wTarget = Mathf.Max(wTarget, bTarget.Size);
                        wGhost = Mathf.Max(wGhost, bGhost.Size); wBody = Mathf.Max(wBody, bBody.Size); wYaw = Mathf.Max(wYaw, yawHi - yawLo);
                    }
                    bHand = default; bElbow = default; bTarget = default; bGhost = default; bBody = default; still = 0f;
                }
                foreach (var f in rec.frames)
                {
                    acc += Mathf.Min(f.dt, H.maxFrame); t += f.dt;
                    while (acc >= Dt)
                    {
                        acc -= Dt; Step(); steps++;
                        Vector3 hp = H.arm.HandPos; maxHandStep = Mathf.Max(maxHandStep, (hp - prevHand).magnitude); prevHand = hp;
                        float pen = ArmPenetration(out _); maxPen = Mathf.Max(maxPen, pen);
                        float tiltNow = Vector3.Angle(H.body.Rb.rotation * Vector3.up, Vector3.up); maxTilt = Mathf.Max(maxTilt, tiltNow); if (tiltNow > 30f) tiltTime += Dt;
                        if (H.state.gripped) grippedTime += Dt;
                        if (still > 0.15f)
                        {
                            float yaw = H.body.Yaw(0f);
                            if (bHand.Size == 0f && bBody.Size == 0f) { yawLo = yawHi = yaw; }
                            yawLo = Mathf.Min(yawLo, yaw); yawHi = Mathf.Max(yawHi, yaw);
                            bHand.Add(hp); bElbow.Add(H.arm.ElbowPos); bTarget.Add(H.state.handTarget); bGhost.Add(H.arm.GhostPos); bBody.Add(H.body.Rb.position);
                            lagStillMax = Mathf.Max(lagStillMax, Vector3.Distance(hp, H.state.handTarget));
                        }
                    }
                    H.FrameStep(f);
                    if (f.delta.sqrMagnitude > 0f) ClosePeriod(); else still += f.dt;
                    Vector3 cur = H.state.cursorPoint;
                    if (hasPrev && H.state.handTargetValid)
                    {
                        float jump = (H.state.handTarget - prevTarget).magnitude - 3f * (cur - prevCursor).magnitude;
                        if (jump > 0.05f)
                        {
                            jumpCount++;
                            if (jumps.Count < 12) jumps.Add($"   {t:F2} с: цель {prevTarget:F2} → {H.state.handTarget:F2} (скачок {(H.state.handTarget - prevTarget).magnitude * 100f:F0} см), точка под курсором {prevCursor:F2} → {cur:F2}");
                        }
                    }
                    prevTarget = H.state.handTarget; prevCursor = cur; hasPrev = H.state.handTargetValid;
                    H.LateStep(f.dt);
                }
                ClosePeriod();
                sb.Append($"ПОВТОР {System.IO.Path.GetFileName(file)}: {rec.frames.Count} кадров, {t:F1} с, шагов физики {steps}\n");
                sb.Append($"  мышь стоит ({stillPeriods} пауз > 0.3 с) — размах: кисть {wHand * 1000f:F1} мм, локоть {wElbow * 1000f:F1} мм, цель {wTarget * 1000f:F1} мм, призрак {wGhost * 1000f:F1} мм, тело {wBody * 1000f:F2} мм и {wYaw:F3}° | отставание кисти в паузе макс {lagStillMax * 1000f:F0} мм\n");
                sb.Append($"  шаг кисти макс {maxHandStep * 1000f:F1} мм | в твёрдом макс {maxPen * 1000f:F1} мм | скачков цели {jumpCount}\n");
                Vector3 eb = H.body.Rb.position;
                sb.Append($"  тело: наклон макс {maxTilt:F0}°, дольше 30° — {tiltTime:F2} с, в конце {Vector3.Angle(H.body.Rb.rotation * Vector3.up, Vector3.up):F0}° | хват держали {grippedTime:F1} с | конец ({eb.x:F2}, {eb.y:F2}, {eb.z:F2})\n");
                foreach (var j in jumps) sb.Append(j).Append('\n');
            }
            finally { End(); }
            return sb.ToString();
        }

        /// <summary>Все записи из tools/herosim/rec — повтор с замером руки; отчёт в tools/herosim/out/arm2_replay.txt.</summary>
        [MenuItem("Fortress/Hero2/Повтор всех записей с замером руки (в Play)")]
        public static void ReplayAllMenu()
        {
            var sb = new StringBuilder();
            if (System.IO.Directory.Exists(Arm2Recording.Dir))
                foreach (var f in System.IO.Directory.GetFiles(Arm2Recording.Dir, "*.txt")) sb.Append(ReplayArm(f));
            string r = sb.Length > 0 ? sb.ToString() : "записей нет";
            Debug.Log(r);
            try { System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../tools/herosim/out/arm2_replay.txt")), r); } catch { }
        }

        /// <summary>СЛЕД ПОВТОРА: запись ввода → таблица по шагам физики (время; точка под курсором; цель; призрак; кисть; локоть; тело)
        /// в tools/herosim/out/arm2_trace.csv — чтобы разобрать по шагам то, что автор увидел глазами.</summary>
        public static string ReplayTrace(string file)
        {
            if (string.IsNullOrEmpty(file)) return "записей нет";
            string err = Begin(); if (err != null) return err;
            var sb = new StringBuilder("t;cx;cy;cz;tx;ty;tz;gx;gy;gz;hx;hy;hz;ex;ey;ez;bx;by;bz;yaw;lmb;grip;hang;climb;tilt;roll;lift;pull;want;along;stand;hold;slip;thold;why;sx;sy;sz;tn\n");
            var inv = System.Globalization.CultureInfo.InvariantCulture; int steps = 0;
            try
            {
                var rec = Arm2Recording.Load(file);
                RestoreStart(rec);
                float acc = 0f, t = 0f;
                foreach (var f in rec.frames)
                {
                    acc += Mathf.Min(f.dt, H.maxFrame);
                    while (acc >= Dt)
                    {
                        acc -= Dt; Step(); steps++; t += Dt;
                        Vector3 c = H.state.cursorPoint, tg = H.state.handTarget, g = H.arm.GhostPos, h = H.arm.HandPos, e = H.arm.ElbowPos, b = H.body.Rb.position;
                        sb.AppendFormat(inv, "{0:F3};{1:F4};{2:F4};{3:F4};{4:F4};{5:F4};{6:F4};{7:F4};{8:F4};{9:F4};{10:F4};{11:F4};{12:F4};{13:F4};{14:F4};{15:F4};{16:F4};{17:F4};{18:F4};{19:F3};{20}\n",
                            t, c.x, c.y, c.z, tg.x, tg.y, tg.z, g.x, g.y, g.z, h.x, h.y, h.z, e.x, e.y, e.z, b.x, b.y, b.z, H.body.Yaw(0f), f.lmb ? 1 : 0);
                        {
                            var st = H.state; Quaternion q = H.body.Rb.rotation; Vector3 up = q * Vector3.up, right = q * Vector3.right;
                            sb.Length -= 1;
                            sb.AppendFormat(inv, ";{0};{1};{2};{3:F1};{4:F1};{5:F0};{6:F0};{7:F3};{8:F3};{9};{10};{11};{12}\n", st.gripped ? 1 : 0, st.hanging ? 1 : 0, H.arm.Climbing ? 1 : 0,
                                Vector3.Angle(up, Vector3.up), Mathf.Asin(Mathf.Clamp(right.y, -1f, 1f)) * Mathf.Rad2Deg, st.liftForce, st.pullVec.magnitude, st.pullLength,
                                st.gripped ? H.intent.TangentLength(h, st.gripNormal) : 0f, H.arm.Supported ? 1 : 0, st.gripOnHold ? 1 : 0, H.grip.Slipping ? 1 : 0, st.targetHold ? st.targetHold.name.Replace(';', ' ') : "-");
                            { Vector3 S = H.intent.ShoulderWorld(); sb.Length -= 1; sb.AppendFormat(inv, ";{0};{1:F3};{2:F3};{3:F3};{4:F2}\n", Arm2Grip.Last, S.x, S.y, S.z, st.gripped ? st.gripNormal.y : 0f); Arm2Grip.Last = "-"; }
                        }
                    }
                    H.FrameStep(f); H.LateStep(f.dt);
                }
                System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../tools/herosim/out/arm2_trace.csv")), sb.ToString());
            }
            finally { End(); }
            return $"след: {steps} шагов → tools/herosim/out/arm2_trace.csv";
        }

        // ================= СРЕЗ 3: ХВАТ И ТЯГА СЕБЯ =================
        [MenuItem("Fortress/Hero2/Срез 3: замер хвата и тяги (в Play)")]
        public static void Slice3Menu()
        {
            string r = Slice3(); Debug.Log(r);
            try { System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../tools/herosim/out/arm2_slice3.txt")), r); } catch { }
        }

        /// <summary>СРЕЗ 3: хват за пол и тяга себя. Бот: кисть на точку, ЛКМ, жест мышью вниз («на себя») и вверх («от себя»), отпустить.
        /// Сторожа: кисть в хвате не съезжает, тело не в твёрдом, без скачков (И2: тело ≤ 4 м/с), курс и наклон тела держатся,
        /// после отпускания тело стоит. Правила: не выше 0.4, не за стену, предметы — не в этом срезе.</summary>
        public static string Slice3()
        {
            string err = Begin(); if (err != null) return err;
            if (!H.arm || !H.grip) { End(); return "у героя нет блока хвата (пересобрать префаб)"; }
            var sb = new StringBuilder($"СРЕЗ 3 — ХВАТ И ТЯГА СЕБЯ (песочница), шаг физики {Dt * 1000f:F1} мс; тяга: жёсткость {H.arm.pullStiffness:F0}, демпфер {H.arm.pullDamping:F0}, сила {H.arm.pullForce:F0} Н\n");
            int bad = 0; float frame = Dt * StepsPerFrame, bodyStepLimit = 4f * Dt;
            try
            {
                Vector3 Sh() => H.intent.ShoulderWorld();
                Vector3 Floor(float deg, float rad) { Vector3 s = Sh(); var d = Quaternion.AngleAxis(deg, Vector3.up) * Vector3.forward * rad; return new Vector3(s.x + d.x, 0f, s.z + d.z); }
                float Arm() => H.state.gripped ? H.intent.TangentLength(H.arm.HandPos, H.state.gripNormal) : Vector3.Distance(Sh(), H.arm.HandPos);

                // Тяга: жест dyPx на кадр в течение seconds; меряет ход тела, съезд кисти, курс, наклон, шаг тела, наложение
                (float travel, float slip, float yaw, float tilt, float step, float pen) Drag(ArmRun run, Vector3 cursor, float seconds, float dyPx, bool lmb = true)
                {
                    var rb = H.body.Rb; Vector3 b0 = rb.position, h0 = H.arm.HandPos, prev = b0; float yaw0 = H.body.YawRb(0f);
                    float slip = 0f, yaw = 0f, tilt = 0f, step = 0f, pen = 0f;
                    int n = Mathf.RoundToInt(seconds / frame);
                    for (int i = 0; i < n; i++)
                    {
                        run.Frame(cursor, false, lmb, new Vector2(0f, dyPx));
                        Vector3 p = rb.position; step = Mathf.Max(step, (p - prev).magnitude / StepsPerFrame); prev = p;
                        if (lmb && H.state.gripped) slip = Mathf.Max(slip, (H.arm.HandPos - h0).magnitude);
                        yaw = Mathf.Max(yaw, Mathf.Abs(Mathf.DeltaAngle(yaw0, H.body.YawRb(yaw0))));
                        tilt = Mathf.Max(tilt, Vector3.Angle(rb.rotation * Vector3.up, Vector3.up));
                        pen = Mathf.Max(pen, Mathf.Max(BodyPenetration(), ArmPenetration(out _)));
                    }
                    return ((rb.position - b0).magnitude, slip, yaw, tilt, step, pen);
                }
                string Row(string name, bool ok, string text) { if (!ok) bad++; return $"{name}: {(ok ? "ок" : "ПЛОХО")} | {text}\n"; }

                {   // 1 — пол: взяться, «на себя», «от себя», отпустить
                    var run = new ArmRun("пол", Vector3.zero, 0f);
                    Vector3 c = Floor(10f, 0.5f);
                    run.Hold(c, 0.5f);
                    Drag(run, c, 0.25f, 0f);
                    bool got = H.state.gripped; float arm0 = Arm();
                    sb.Append(Row("1 пол: хват", got, $"кисть сцеплена: {(got ? "да" : "НЕТ")}, рука {arm0:F2} м"));
                    var a = Drag(run, c, 1.2f, -3f); var a2 = Drag(run, c, 0.5f, 0f);
                    float arm1 = Arm(), want1 = H.state.pullLength;
                    bool ok = got && a.travel > 0.2f && Mathf.Abs(arm1 - want1) < 0.03f && a.slip < 0.005f && a.yaw < 3f && a.tilt < 5f && a.step < bodyStepLimit && a.pen < 0.005f && a2.travel < 0.03f;
                    sb.Append(Row("1 пол: на себя (360 px)", ok, $"тело прошло {a.travel:F3} м, рука {arm0:F2} → {arm1:F2} м (хочет {want1:F2}) | кисть съехала {a.slip * 1000f:F1} мм | курс ушёл {a.yaw:F1}° | наклон {a.tilt:F1}° | шаг тела макс {a.step * 1000f:F1} мм | в твёрдом {a.pen * 1000f:F1} мм | после жеста доехало ещё {a2.travel * 1000f:F0} мм"));
                    var b = Drag(run, c, 1.2f, 3f); var b2 = Drag(run, c, 0.5f, 0f);
                    float arm2 = Arm(), want2 = H.state.pullLength;
                    ok = b.travel > 0.2f && Mathf.Abs(arm2 - want2) < 0.03f && b.yaw < 3f && b.tilt < 5f && b.step < bodyStepLimit && b.pen < 0.005f;
                    sb.Append(Row("1 пол: от себя (360 px)", ok, $"тело прошло {b.travel:F3} м, рука {arm1:F2} → {arm2:F2} м (хочет {want2:F2}) | кисть съехала {b.slip * 1000f:F1} мм | курс ушёл {b.yaw:F1}° | наклон {b.tilt:F1}° | шаг тела макс {b.step * 1000f:F1} мм | в твёрдом {b.pen * 1000f:F1} мм"));
                    var r = Drag(run, c, 0.6f, 0f, false);
                    ok = !H.state.gripped && r.travel < 0.02f && r.step < bodyStepLimit;
                    sb.Append(Row("1 пол: отпустить", ok, $"хват снят: {(!H.state.gripped ? "да" : "НЕТ")} | тело после отпускания ушло {r.travel * 1000f:F1} мм"));
                    // короткие ходы туда-обратно: дрожи тела быть не должно
                    run.Hold(c, 0.4f); Drag(run, c, 0.25f, 0f);
                    float sw = 0f; for (int k = 0; k < 6; k++) { var q = Drag(run, c, 0.15f, k % 2 == 0 ? -4f : 4f); sw = Mathf.Max(sw, q.step); }
                    var coast = Drag(run, c, 0.6f, 0f); var still = Drag(run, c, 0.4f, 0f);
                    ok = still.travel < 0.002f && sw < bodyStepLimit;
                    sb.Append(Row("1 пол: туда-обратно и стоп", ok, $"шаг тела макс {sw * 1000f:F1} мм | после стопа доехало {coast.travel * 1000f:F1} мм, потом (0.6–1.0 с) ушло {still.travel * 1000f:F2} мм"));
                    Drag(run, c, 0.2f, 0f, false);
                }
                {   // 1б — точность жеста: 100 px «на себя» = 100 · (метров на px линии плечо–кисть на экране); мёртвого хода быть не должно
                    var run = new ArmRun("пол", Vector3.zero, 0f);
                    Vector3 c = Floor(10f, 0.5f); run.Hold(c, 0.6f); Drag(run, c, 0.25f, 0f);
                    Vector3 hand = H.arm.HandPos, s0 = Sh(), sp = new Vector3(s0.x, hand.y, s0.z);
                    Vector3 va = H.intent.cam.WorldToViewportPoint(sp), vb = H.intent.cam.WorldToViewportPoint(hand);
                    float px = new Vector2((vb.x - va.x) * ScreenW, (vb.y - va.y) * ScreenH).magnitude, mpp = Vector3.Distance(sp, hand) / Mathf.Max(1f, px);
                    var a1 = Drag(run, c, 0.25f, -2f); var a1s = Drag(run, c, 0.6f, 0f); var a1q = Drag(run, c, 0.4f, 0f);
                    float got1 = a1.travel + a1s.travel + a1q.travel, exp1 = 50f * mpp;
                    var a2 = Drag(run, c, 0.05f, -2f); var a2s = Drag(run, c, 0.8f, 0f);             // малый жест 10 px
                    float got2 = a2.travel + a2s.travel, exp2 = 10f * mpp;
                    var a3 = Drag(run, c, 0.05f, 2f); var a3s = Drag(run, c, 0.8f, 0f);              // и назад 10 px — люфт
                    float got3 = a3.travel + a3s.travel;
                    bool ok = Mathf.Abs(got1 - exp1) < exp1 * 0.25f && got2 > exp2 * 0.5f && got3 > exp2 * H.intent.backK * 0.5f && a1q.travel < 0.002f;   // назад — спиной: по правилу ход × 0.45
                    sb.Append(Row("1б пол: точность жеста", ok, $"50 px: прошло {got1 * 100f:F1} см, по правилу {exp1 * 100f:F1} см | 10 px: {got2 * 100f:F1} см из {exp2 * 100f:F1} | назад 10 px: {got3 * 100f:F1} см из {exp2 * H.intent.backK * 100f:F1} (спиной × 0.45) | в покое (0.6–1.0 с после жеста) тело ушло {a1q.travel * 1000f:F2} мм"));
                    Drag(run, c, 0.2f, 0f, false);
                    // короткий клик (нажал и отпустил в одном кадре) — хват не залипает
                    run.Hold(c, 0.4f);
                    var vp = H.intent.cam.WorldToViewportPoint(c);
                    H.FrameStep(new Arm2InputFrame { dt = frame, pos = new Vector2(vp.x * ScreenW, vp.y * ScreenH), screenW = ScreenW, screenH = ScreenH, lmb = false, lmbDown = true });
                    for (int k = 0; k < 4; k++) Step();
                    run.Hold(c, 0.3f);
                    sb.Append(Row("1б короткий клик", !H.state.gripped && !H.state.wantGrip, !H.state.gripped ? "хват не залип" : "ХВАТ ЗАЛИП"));
                    // кисть слева (−35°): «на себя» до упора — тело не наезжает на свою кисть
                    run = new ArmRun("пол", Vector3.zero, 0f);
                    c = Floor(-35f, 0.45f); run.Hold(c, 0.7f); Drag(run, c, 0.25f, 0f);
                    bool gotL = H.state.gripped;
                    var l1 = Drag(run, c, 1.5f, -3f); var l2 = Drag(run, c, 0.6f, 0f);
                    var l3 = Drag(run, c, 0.3f, 2f);                                                  // назад 60 px: мёртвого хода после упора быть не должно
                    ok = gotL && l1.pen < 0.005f && l1.slip < 0.005f && l2.travel < 0.01f && l2.step < 0.002f && l3.travel > 0.02f && l1.tilt < 5f;
                    sb.Append(Row("1б кисть слева: на себя до упора", ok, $"хват {(gotL ? "да" : "НЕТ")} | тело прошло {l1.travel:F3} м | в твёрдом {l1.pen * 1000f:F1} мм | кисть съехала {l1.slip * 1000f:F1} мм | курс {l1.yaw:F1}° | наклон {l1.tilt:F1}° | в упоре тело ушло {l2.travel * 1000f:F1} мм, шаг {l2.step * 1000f:F2} мм | назад 60 px: {l3.travel * 100f:F1} см"));
                    Drag(run, c, 0.2f, 0f, false);
                }
                {   // 2 — правила хвата: ступень 0.3 — да; уступ 0.5 — нет; стена — нет; предмет — нет (не этот срез)
                    var run = new ArmRun("ступень", new Vector3(-3f, 0f, 1.55f), 0f);
                    Vector3 c = new Vector3(-2.8f, 0.3f, 2.12f); run.Hold(c, 0.7f); Drag(run, c, 0.4f, 0f);
                    sb.Append(Row("2 хват за верх ступени 0.3", H.state.gripped, H.state.gripped ? "взялся (как задумано)" : "НЕ взялся"));
                    run = new ArmRun("уступ", new Vector3(-1.4f, 0f, 1.55f), 0f);
                    c = new Vector3(-1.2f, 0.5f, 2.07f); run.Hold(c, 1.5f); bool onTop = H.arm.HandPos.y > 0.5f; Drag(run, c, 0.4f, 0f);
                    sb.Append(Row("2 хват за верх уступа 0.5", onTop && !H.state.gripped, (onTop ? "кисть на верхе; " : "КИСТЬ НЕ ДОШЛА до верха; ") + (!H.state.gripped ? "не взялся (как задумано: выше 0.4)" : "ВЗЯЛСЯ")));
                    run = new ArmRun("стена", new Vector3(0f, 0f, 3.55f), 0f);
                    c = new Vector3(Sh().x + 0.05f, 0.3f, 4f); run.Hold(c, 1.5f); bool onWall = H.arm.HandPos.y > 0.25f; Drag(run, c, 0.4f, 0f);
                    sb.Append(Row("2 хват за стену", onWall && !H.state.gripped, (onWall ? "кисть на грани; " : "КИСТЬ НЕ ДОШЛА до грани; ") + (!H.state.gripped ? "не взялся (как задумано: грань стены)" : "ВЗЯЛСЯ")));
                    run = new ArmRun("кубик", new Vector3(-0.3f, 0f, 0.45f), 0f);
                    c = new Vector3(-0.45f, 0.12f, 0.8f); run.Hold(c, 2.0f, false); bool onCube = H.arm.HandPos.y > 0.12f; Drag(run, c, 0.4f, 0f);
                    sb.Append(Row("2 хват за кубик", onCube && !H.state.gripped, (onCube ? "кисть на кубике; " : "КИСТЬ НЕ ДОШЛА до кубика; ") + (!H.state.gripped ? "не взялся (предметы — отдельный срез)" : "ВЗЯЛСЯ")));
                }
                {   // 3 — склон 20°: взяться выше по склону и подтянуться; отпустить — тело стоит
                    Arm2Sandbox.Station st20 = default; int ri = 0;
                    for (int i = 0; i < Arm2Sandbox.RampAngles.Length; i++) if (Mathf.Approximately(Arm2Sandbox.RampAngles[i], 20f)) ri = i;
                    foreach (var x in Arm2Sandbox.Stations()) if (x.name.Contains("склон 20")) st20 = x;
                    var run = new ArmRun("склон 20°", st20.center, st20.rot);
                    Vector3 top = Arm2Sandbox.RampTop(ri, out Vector3 nrm);
                    Vector3 sh = Sh(); Vector3 c = sh - nrm * Vector3.Dot(sh - top, nrm) + st20.rot * (Vector3.forward * 0.45f);
                    run.Hold(c, 0.6f, false); Drag(run, c, 0.3f, 0f);
                    bool got = H.state.gripped; float y0 = H.body.Rb.position.y;
                    var a = Drag(run, c, 1.2f, -3f); Drag(run, c, 0.4f, 0f);
                    float up = H.body.Rb.position.y - y0;
                    var r = Drag(run, c, 0.8f, 0f, false);
                    bool ok = got && a.travel > 0.15f && up > 0.04f && a.slip < 0.005f && a.pen < 0.005f && a.step < bodyStepLimit && r.travel < 0.03f;
                    sb.Append(Row("3 склон 20°: на себя вверх", ok, $"хват {(got ? "да" : "НЕТ")} | тело прошло {a.travel:F3} м, поднялось на {up * 100f:F1} см | кисть съехала {a.slip * 1000f:F1} мм | курс {a.yaw:F1}° | шаг тела макс {a.step * 1000f:F1} мм | в твёрдом {a.pen * 1000f:F1} мм | после отпускания ушло {r.travel * 1000f:F1} мм"));
                }
                sb.Append(bad == 0 ? "ИТОГО: всё ок\n" : $"ИТОГО: плохо в {bad} строках\n");
                sb.Append($"пороги: ход тела > 0.2 м на 360 px; рука = желаемой ± 3 см; кисть в хвате съезжает < 5 мм; курс < 3°, наклон < 5°; шаг тела < {bodyStepLimit * 1000f:F0} мм (И2); в твёрдом < 5 мм (И1)\n");
            }
            finally { End(); }
            return sb.ToString();
        }

        // ================= СРЕЗ 4: ПОВОРОТ НА МЕСТЕ =================
        [MenuItem("Fortress/Hero2/Срез 4: замер поворота (в Play)")]
        public static void Slice4Menu()
        {
            string r = Slice4(); Debug.Log(r);
            try { System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../tools/herosim/out/arm2_slice4.txt")), r); } catch { }
        }

        /// <summary>СРЕЗ 4: поворот на месте жестом вбок при хвате. Бот: хват, мышь вправо / влево, большой жест (предел ±90° от кисти),
        /// поворот у стены, поворот вместе с тягой. Сторожа: кисть не съезжает, тело не в твёрдом, без скачков, наклон мал, после жеста стоит.</summary>
        public static string Slice4()
        {
            string err = Begin(); if (err != null) return err;
            if (!H.arm || !H.grip) { End(); return "у героя нет блока хвата (пересобрать префаб)"; }
            var sb = new StringBuilder($"СРЕЗ 4 — ПОВОРОТ НА МЕСТЕ (песочница), шаг физики {Dt * 1000f:F1} мс; курс: жёсткость {H.body.yawStiffness:F0}, демпфер {H.body.yawDamping:F0}, момент {H.body.yawTorque:F0} Н·м; чувствительность {H.intent.turnSensitivity:F2}\n");
            int bad = 0; float frame = Dt * StepsPerFrame;
            try
            {
                Vector3 Sh() => H.intent.ShoulderWorld();
                Vector3 Floor(float deg, float rad) { Vector3 s = Sh(); var d = Quaternion.AngleAxis(deg, Vector3.up) * Vector3.forward * rad; return new Vector3(s.x + d.x, 0f, s.z + d.z); }
                string Row(string name, bool ok, string text) { if (!ok) bad++; return $"{name}: {(ok ? "ок" : "ПЛОХО")} | {text}\n"; }

                // Жест (dx, dy) px на кадр в течение seconds: поворот тела со знаком, ход центра тела, съезд кисти, наклон, шаг курса, наложение
                (float turn, float move, float slip, float tilt, float yawStep, float pen) Turn(ArmRun run, Vector3 cursor, float seconds, float dx, float dy = 0f, bool lmb = true)
                {
                    var rb = H.body.Rb; Vector3 b0 = rb.position, h0 = H.arm.HandPos; float yaw0 = H.body.YawRb(0f), prevYaw = yaw0, acc = 0f;
                    float slip = 0f, tilt = 0f, yawStep = 0f, pen = 0f;
                    int n = Mathf.RoundToInt(seconds / frame);
                    for (int i = 0; i < n; i++)
                    {
                        run.Frame(cursor, false, lmb, new Vector2(dx, dy));
                        float y = H.body.YawRb(prevYaw), d = Mathf.DeltaAngle(prevYaw, y); acc += d; prevYaw = y;
                        yawStep = Mathf.Max(yawStep, Mathf.Abs(d) / StepsPerFrame);
                        if (lmb && H.state.gripped) slip = Mathf.Max(slip, (H.arm.HandPos - h0).magnitude);
                        tilt = Mathf.Max(tilt, Vector3.Angle(rb.rotation * Vector3.up, Vector3.up));
                        pen = Mathf.Max(pen, Mathf.Max(BodyPenetration(), ArmPenetration(out _)));
                    }
                    Vector3 mv = rb.position - b0; mv.y = 0f;
                    return (acc, mv.magnitude, slip, tilt, yawStep, pen);
                }
                // по правилу старого героя: градусы на px = mpp / r · 57.3 · чувствительность (знак: мышь вправо — минус)
                float DegPerPx()
                {
                    Vector3 hand = H.arm.HandPos, s0 = Sh(), sp = new Vector3(s0.x, hand.y, s0.z);
                    Vector3 va = H.intent.cam.WorldToViewportPoint(sp), vb = H.intent.cam.WorldToViewportPoint(hand);
                    float px = new Vector2((vb.x - va.x) * ScreenW, (vb.y - va.y) * ScreenH).magnitude, len = Vector3.Distance(sp, hand);
                    return len / Mathf.Max(1f, px) / Mathf.Max(H.intent.turnMinRadius, len) * Mathf.Rad2Deg * H.intent.turnSensitivity;
                }
                float yawLimit = 6f * Dt * 60f;                                   // шаг курса: не быстрее 360°/с

                {   // 1 — пол: мышь вправо 60 px, стоп; мышь влево 120 px, стоп
                    var run = new ArmRun("пол", Vector3.zero, 0f);
                    Vector3 c = Floor(10f, 0.45f); run.Hold(c, 0.6f); Turn(run, c, 0.25f, 0f);
                    bool got = H.state.gripped; float dpp = DegPerPx();
                    var a = Turn(run, c, 0.3f, 2f); var a1 = Turn(run, c, 0.6f, 0f); var a2 = Turn(run, c, 0.4f, 0f);
                    float turned = a.turn + a1.turn + a2.turn, want = -60f * dpp;
                    bool ok = got && Mathf.Abs(turned - want) < Mathf.Abs(want) * 0.25f + 1f && a.slip < 0.005f && a.tilt < 5f && a.pen < 0.005f && Mathf.Abs(a2.turn) < 0.3f && a2.move < 0.002f && a.yawStep < yawLimit;
                    sb.Append(Row("1 пол: мышь вправо 60 px", ok, $"хват {(got ? "да" : "НЕТ")} | курс {turned:F1}° (по правилу {want:F1}°) | центр тела ушёл {(a.move + a1.move) * 100f:F1} см | кисть съехала {a.slip * 1000f:F1} мм | наклон {a.tilt:F1}° | шаг курса макс {a.yawStep:F2}° | в твёрдом {a.pen * 1000f:F1} мм | в покое курс ушёл {a2.turn:F2}°, тело {a2.move * 1000f:F2} мм"));
                    dpp = DegPerPx();
                    var b = Turn(run, c, 0.6f, -2f); var b1 = Turn(run, c, 0.6f, 0f); var b2 = Turn(run, c, 0.4f, 0f);
                    turned = b.turn + b1.turn + b2.turn; want = 120f * dpp;
                    ok = Mathf.Abs(turned - want) < Mathf.Abs(want) * 0.3f + 1f && b.slip < 0.005f && b.tilt < 5f && b.pen < 0.005f && Mathf.Abs(b2.turn) < 0.3f && b.yawStep < yawLimit;
                    sb.Append(Row("1 пол: мышь влево 120 px", ok, $"курс {turned:F1}° (по правилу {want:F1}°, чувствительность меняется по ходу) | центр тела ушёл {(b.move + b1.move) * 100f:F1} см | кисть съехала {b.slip * 1000f:F1} мм | наклон {b.tilt:F1}° | шаг курса макс {b.yawStep:F2}° | в твёрдом {b.pen * 1000f:F1} мм | в покое курс ушёл {b2.turn:F2}°"));
                    Turn(run, c, 0.2f, 0f, 0f, false);
                }
                {   // 2 — большой жест: лицо не уходит от направления на кисть дальше 90°; ничего не взрывается
                    var run = new ArmRun("пол", Vector3.zero, 0f);
                    Vector3 c = Floor(0f, 0.45f); run.Hold(c, 0.6f); Turn(run, c, 0.25f, 0f);
                    var a = Turn(run, c, 2.5f, -6f); var a1 = Turn(run, c, 0.8f, 0f);
                    Vector3 toHand = H.arm.HandPos - H.body.Rb.position; toHand.y = 0f;
                    float off = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(toHand.x, toHand.z) * Mathf.Rad2Deg, H.body.YawRb(0f)));
                    bool ok = H.state.gripped && off < H.intent.turnLimit + 5f && Mathf.Abs(a.turn) > 30f && a.slip < 0.005f && a.tilt < 5f && a.pen < 0.005f && a.yawStep < yawLimit && Mathf.Abs(a1.turn) < 3f;
                    sb.Append(Row("2 большой жест (1500 px влево)", ok, $"курс {a.turn:F0}° | лицо от кисти на {off:F0}° (предел {H.intent.turnLimit:F0}°) | центр тела ушёл {a.move * 100f:F1} см | кисть съехала {a.slip * 1000f:F1} мм | наклон {a.tilt:F1}° | шаг курса макс {a.yawStep:F2}° | в твёрдом {a.pen * 1000f:F1} мм | после жеста докрутило {a1.turn:F1}°"));
                    Turn(run, c, 0.2f, 0f, 0f, false);
                }
                {   // 3 — у стены: тело в 3 см от стены, поворот — угол тела упирается, тело отъезжает или встаёт, но не входит в стену
                    var run = new ArmRun("стена", new Vector3(0f, 0f, 3.82f - Arm2Body.Length * 0.5f), 90f);     // правым... лицом вдоль стены
                    Vector3 c = Floor(0f, 0.4f); run.Hold(c, 0.6f, false); Turn(run, c, 0.25f, 0f);
                    bool got = H.state.gripped;
                    var a = Turn(run, c, 1.2f, 3f); var a1 = Turn(run, c, 0.6f, 0f);
                    bool ok = got && a.pen < 0.005f && a.slip < 0.005f && a.tilt < 5f && a.yawStep < yawLimit && Mathf.Abs(a1.turn) < 3f;
                    sb.Append(Row("3 у стены: поворот в стену", ok, $"хват {(got ? "да" : "НЕТ")} | курс {a.turn:F1}° | центр тела ушёл {a.move * 100f:F1} см | в твёрдом {a.pen * 1000f:F1} мм | кисть съехала {a.slip * 1000f:F1} мм | наклон {a.tilt:F1}° | после жеста докрутило {a1.turn:F1}°"));
                    Turn(run, c, 0.2f, 0f, 0f, false);
                }
                {   // 4 — поворот вместе с тягой: мышь по диагонали
                    var run = new ArmRun("пол", Vector3.zero, 0f);
                    Vector3 c = Floor(10f, 0.5f); run.Hold(c, 0.6f); Turn(run, c, 0.25f, 0f);
                    float arm0 = H.intent.TangentLength(H.arm.HandPos, H.state.gripNormal);
                    var a = Turn(run, c, 0.6f, 2f, -2f); var a1 = Turn(run, c, 0.8f, 0f);
                    float arm1 = H.intent.TangentLength(H.arm.HandPos, H.state.gripNormal);
                    bool ok = H.state.gripped && Mathf.Abs(a.turn) > 5f && arm0 - arm1 > 0.1f && a.slip < 0.005f && a.tilt < 5f && a.pen < 0.005f && a.yawStep < yawLimit && Mathf.Abs(a1.turn) < 3f;
                    sb.Append(Row("4 поворот вместе с тягой", ok, $"курс {a.turn:F1}° | рука {arm0:F2} → {arm1:F2} м | кисть съехала {a.slip * 1000f:F1} мм | наклон {a.tilt:F1}° | в твёрдом {a.pen * 1000f:F1} мм | после жеста докрутило {a1.turn:F1}°"));
                    Turn(run, c, 0.2f, 0f, 0f, false);
                }
                sb.Append(bad == 0 ? "ИТОГО: всё ок\n" : $"ИТОГО: плохо в {bad} строках\n");
                sb.Append($"пороги: курс = правилу ± 25 %; кисть съезжает < 5 мм; наклон < 5°; шаг курса < {yawLimit:F1}° (360°/с); в твёрдом < 5 мм; в покое курс < 0.3°\n");
            }
            finally { End(); }
            return sb.ToString();
        }

        // ================= СРЕЗ 5: СТУПЕНИ ВВЕРХ И ВНИЗ =================
        [MenuItem("Fortress/Hero2/Срез 5: замер ступеней (в Play)")]
        public static void Slice5Menu()
        {
            string r = Slice5(); Debug.Log(r);
            try { System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../tools/herosim/out/arm2_slice5.txt")), r); } catch { }
        }

        /// <summary>СРЕЗ 5: ступени. Вверх: герой перед ступенью, хват за верх, «на себя», перехват — до 6 жестов; 0.3 и 0.4 — должен
        /// залезть, 0.5 — нет (хват выше 0.4 не берётся). Вниз: с верха ступени 0.3 передом — хват за пол внизу, «на себя».
        /// Сторожа: не в твёрдом, без скачков (тело ≤ 4 м/с), кисть в хвате не съезжает, тело не переворачивается.</summary>
        public static string Slice5()
        {
            string err = Begin(); if (err != null) return err;
            if (!H.arm || !H.grip) { End(); return "у героя нет блока хвата (пересобрать префаб)"; }
            var sb = new StringBuilder($"СРЕЗ 5 — СТУПЕНИ (песочница), шаг физики {Dt * 1000f:F1} мс; подъём: сила {H.arm.liftForce:F0} Н, тяга вперёд при подъёме {H.arm.climbPull:F0} Н\n");
            int bad = 0; float frame = Dt * StepsPerFrame, bodyStepLimit = 4f * Dt;
            try
            {
                Vector3 Sh() => H.intent.ShoulderWorld();
                string Row(string name, bool ok, string text) { if (!ok) bad++; return $"{name}: {(ok ? "ок" : "ПЛОХО")} | {text}\n"; }
                float step = 0f, pen = 0f, slip = 0f, tilt = 0f; bool climbed;
                void Frames(ArmRun run, Vector3 cursor, float seconds, float dy, bool lmb)
                {
                    var rb = H.body.Rb; Vector3 prev = rb.position, h0 = H.arm.HandPos; bool g0 = H.state.gripped;
                    int n = Mathf.RoundToInt(seconds / frame);
                    for (int i = 0; i < n; i++)
                    {
                        run.Frame(cursor, false, lmb, new Vector2(0f, dy));
                        Vector3 p = rb.position; step = Mathf.Max(step, (p - prev).magnitude / StepsPerFrame); prev = p;
                        if (g0 && H.state.gripped) slip = Mathf.Max(slip, (H.arm.HandPos - h0).magnitude);
                        if (!g0 && H.state.gripped) { g0 = true; h0 = H.arm.HandPos; }
                        tilt = Mathf.Max(tilt, Vector3.Angle(rb.rotation * Vector3.up, Vector3.up));
                        pen = Mathf.Max(pen, Mathf.Max(BodyPenetration(), ArmPenetration(out _)));
                        if (H.arm.Climbing) climbed = true;
                    }
                }

                // ВВЕРХ: ступени 0.3, 0.4 и уступ 0.5 (середины по X: −3.0, −4.8, −1.4; грань z = 2.0)
                foreach (var (xc, h, expect) in new[] { (-3.0f, 0.3f, true), (-4.8f, 0.4f, true), (-1.4f, 0.5f, false) })
                {
                    step = pen = slip = tilt = 0f; climbed = false;
                    var run = new ArmRun($"ступень {h:0.0}", new Vector3(xc, 0f, 1.62f), 0f);
                    int gestures = 0, grips = 0; bool up = false;
                    for (int g = 0; g < 6 && !up; g++)
                    {
                        // кисть — на верх ступени перед плечом, как можно дальше (в досягаемости), но не ближе 6 см к ребру
                        Vector3 s = Sh(); float dy = (h + H.intent.handRadius) - s.y, reach = H.intent.Reach - 0.03f;
                        float dz = Mathf.Sqrt(Mathf.Max(0.01f, reach * reach - dy * dy));
                        Vector3 c = new Vector3(s.x, s.y > h + 0.1f ? Mathf.Max(h, H.body.Rb.position.y - 0.1f) : h, Mathf.Max(2.06f, s.z + dz));
                        Frames(run, c, 0.9f, 0f, false);
                        Frames(run, c, 0.3f, 0f, true); gestures++;
                        if (H.state.gripped) grips++;
                        Frames(run, c, 1.6f, -3f, true);
                        Frames(run, c, 0.5f, 0f, true);
                        Frames(run, c, 0.4f, 0f, false);
                        var rb = H.body.Rb;
                        up = rb.position.y - Arm2Body.Height * 0.5f > h - 0.02f && rb.position.z > 2f + Arm2Body.Length * 0.5f - 0.02f;
                    }
                    Vector3 e = H.body.Rb.position;
                    bool ok = up == expect && pen < 0.005f && step < bodyStepLimit && slip < 0.005f && (expect ? tilt < 60f : true);
                    sb.Append(Row($"вверх на {h:0.0}", ok, $"{(up ? "залез" : "не залез")} ({(expect ? "должен залезть" : "не должен: выше 0.4")}) | жестов {gestures}, хватов {grips}, подъём включался: {(climbed ? "да" : "нет")} | конец: низ тела {e.y - Arm2Body.Height * 0.5f:F2} м, z {e.z:F2} | наклон макс {tilt:F0}° | шаг тела макс {step * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм | кисть съехала {slip * 1000f:F1} мм"));
                }

                // ВНИЗ: с верха ступени 0.3 передом (лицом на −Z, к южной грани z = 2.0): хват за пол внизу, «на себя»
                {
                    step = pen = slip = tilt = 0f; climbed = false;
                    var run = new ArmRun("вниз", new Vector3(-3.0f, 0.3f, 2.2f), 180f);
                    bool down = false; int gestures = 0, grips = 0; float maxFall = 0f;
                    for (int g = 0; g < 5 && !down; g++)
                    {
                        Vector3 s = Sh(); bool onTop = H.body.Rb.position.y > 0.3f;
                        // кисть: вперёд (на −Z) как можно дальше; пока плечо над верхом — по верху, у ребра — на пол внизу
                        // курсор — далеко вперёд на видимый пол внизу (у самой грани пол заслонён ступенью); цель подтянет намерение:
                        // плечо у края — кисть ляжет на пол внизу, иначе — на верх ступени перед собой
                        Vector3 c = onTop ? new Vector3(s.x, 0f, 1.45f) : new Vector3(s.x, 0f, s.z - 0.5f);
                        Frames(run, c, 1.0f, 0f, false);
                        Frames(run, c, 0.3f, 0f, true); gestures++; if (H.state.gripped) grips++;
                        float y0 = H.body.Rb.position.y;
                        Frames(run, c, 1.6f, -3f, true);
                        Frames(run, c, 0.6f, 0f, true);
                        Frames(run, c, 0.5f, 0f, false);
                        maxFall = Mathf.Max(maxFall, y0 - H.body.Rb.position.y);
                        down = H.body.Rb.position.y - Arm2Body.Height * 0.5f < 0.02f;
                    }
                    Vector3 e = H.body.Rb.position; float endTilt = Vector3.Angle(H.body.Rb.rotation * Vector3.up, Vector3.up);
                    bool ok = down && endTilt < 10f && pen < 0.005f && step < bodyStepLimit;
                    sb.Append(Row("вниз с 0.3 передом", ok, $"{(down ? "спустился" : "НЕ спустился")} | жестов {gestures}, хватов {grips} | конец: низ тела {e.y - Arm2Body.Height * 0.5f:F2} м, z {e.z:F2}, лежит с наклоном {endTilt:F0}° | наклон в пути макс {tilt:F0}° | шаг тела макс {step * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм"));
                }
                sb.Append(bad == 0 ? "ИТОГО: всё ок\n" : $"ИТОГО: плохо в {bad} строках\n");
                sb.Append($"пороги: 0.3 и 0.4 — залез, 0.5 — нет; шаг тела < {bodyStepLimit * 1000f:F0} мм (И2); в твёрдом < 5 мм (И1); кисть в хвате съезжает < 5 мм; вниз — лёг с наклоном < 10°\n");
            }
            finally { End(); }
            return sb.ToString();
        }

        // ================= СРЕЗ 6: КРАЙ И ВИС =================
        [MenuItem("Fortress/Hero2/Срез 6: замер края и виса (в Play)")]
        public static void Slice6Menu()
        {
            string r = Slice6(); Debug.Log(r);
            try { System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../tools/herosim/out/arm2_slice6.txt")), r); } catch { }
        }

        /// <summary>СРЕЗ 6: край. Спиной с площадки 1.2: хват за пол, «от себя» — тело уходит за край в вис, опускается вдоль стенки,
        /// отпустил — упал и встал. Передом к краю: «на себя» сколько угодно — не падает. Сторожа: без скачков, не в твёрдом,
        /// кисть в хвате не съезжает, в висе тело не болтается.</summary>
        public static string Slice6()
        {
            string err = Begin(); if (err != null) return err;
            if (!H.arm || !H.grip) { End(); return "у героя нет блока хвата (пересобрать префаб)"; }
            var sb = new StringBuilder($"СРЕЗ 6 — КРАЙ И ВИС (песочница), шаг физики {Dt * 1000f:F1} мс\n");
            int bad = 0; float frame = Dt * StepsPerFrame, bodyStepLimit = 4f * Dt;
            float px = Arm2Sandbox.PlatX, ph = Arm2Sandbox.PlatH, pz = Arm2Sandbox.PlatZ0;
            try
            {
                Vector3 Sh() => H.intent.ShoulderWorld();
                string Row(string name, bool ok, string text) { if (!ok) bad++; return $"{name}: {(ok ? "ок" : "ПЛОХО")} | {text}\n"; }
                float step = 0f, pen = 0f, slip = 0f; bool hung;
                void Frames(ArmRun run, Vector3 cursor, float seconds, float dy, bool lmb)
                {
                    var rb = H.body.Rb; Vector3 prev = rb.position, h0 = H.arm.HandPos; bool g0 = H.state.gripped;
                    int n = Mathf.RoundToInt(seconds / frame);
                    for (int i = 0; i < n; i++)
                    {
                        run.Frame(cursor, false, lmb, new Vector2(0f, dy));
                        Vector3 p = rb.position; step = Mathf.Max(step, (p - prev).magnitude / StepsPerFrame); prev = p;
                        if (g0 && H.state.gripped) slip = Mathf.Max(slip, (H.arm.HandPos - h0).magnitude);
                        if (!g0 && H.state.gripped) { g0 = true; h0 = H.arm.HandPos; }
                        pen = Mathf.Max(pen, Mathf.Max(BodyPenetration(), ArmPenetration(out _)));
                        if (H.state.hanging) hung = true;
                    }
                }

                {   // 1 — спиной с площадки 1.2: хват перед собой, «от себя»
                    step = pen = slip = 0f; hung = false;
                    var run = new ArmRun("спиной", new Vector3(px, ph, pz + 0.45f), 0f);
                    Vector3 s = Sh(), c = new Vector3(s.x, ph, s.z + 0.3f);
                    Frames(run, c, 0.7f, 0f, false); Frames(run, c, 0.3f, 0f, true);
                    bool got = H.state.gripped; float tHang = -1f; int n = Mathf.RoundToInt(4f / frame);
                    for (int i = 0; i < n && tHang < 0f; i++) { Frames(run, c, frame, 3f, true); if (H.state.hanging) tHang = i * frame; }
                    float y1 = H.body.Rb.position.y;
                    Frames(run, c, 2.0f, 3f, true);                                       // дальше «от себя» — рука длиннее, тело ниже
                    float y2 = H.body.Rb.position.y, arm2 = Vector3.Distance(Sh(), H.arm.HandPos);
                    Vector3 p0 = H.body.Rb.position; float sway = 0f;
                    // стенка для тела скользкая: тело в висе сначала съезжает вбок под кисть (плечо сбоку от центра) — ждём 2.4 с, меряем последние 0.6 с
                    for (int i = 0; i < Mathf.RoundToInt(3.0f / frame); i++) { Frames(run, c, frame, 0f, true); if (i > 240) sway = Mathf.Max(sway, (H.body.Rb.position - p0).magnitude); else p0 = H.body.Rb.position; }
                    bool still = H.state.gripped && H.state.hanging; float y3 = H.body.Rb.position.y, zHang = H.body.Rb.position.z;
                    bool ok = got && tHang >= 0f && still && y2 < y1 - 0.1f && sway < 0.01f && pen < 0.005f && step < bodyStepLimit && H.grip.Hooked;
                    sb.Append(Row("1 спиной в вис с 1.2", ok, $"хват {(got ? "да" : "НЕТ")} | вис {(tHang >= 0f ? $"через {tHang:F2} с" : "НЕТ")} | тело по высоте: в момент виса {y1:F2}, после «от себя» {y2:F2} (рука {arm2:F2} м), в покое {y3:F2}, z {zHang:F2} (край {pz:F2}) | в покое тело ходит {sway * 1000f:F1} мм | шаг тела макс {step * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм | кисть на кромке: {(H.grip.Hooked ? "да" : "НЕТ")}"));
                    // отпустил — упал; неваляшка встаёт
                    step = pen = 0f;
                    Frames(run, c, 1.5f, 0f, false);
                    var rb = H.body.Rb; float tilt = Vector3.Angle(rb.rotation * Vector3.up, Vector3.up), bottom = rb.position.y - Arm2Body.Height * 0.5f;
                    ok = !H.state.gripped && bottom < 0.03f && tilt < 10f && pen < 0.005f && step < 6f * Dt;
                    sb.Append(Row("1 отпустил в висе", ok, $"хват снят: {(!H.state.gripped ? "да" : "НЕТ")} | низ тела {bottom:F2} м над полом, наклон через 1.5 с {tilt:F0}° | шаг тела макс {step * 1000f:F1} мм (падение) | в твёрдом {pen * 1000f:F1} мм"));
                }
                {   // 2 — передом к краю: «на себя» четыре раза — тело с площадки не падает
                    step = pen = slip = 0f; hung = false;
                    var run = new ArmRun("передом", new Vector3(px, ph, pz + 0.45f), 180f);
                    int grips = 0; float minY = 99f;
                    for (int g = 0; g < 4; g++)
                    {
                        Vector3 s = Sh(), c = new Vector3(s.x, 0f, pz - 0.6f);              // курсор — на пол далеко внизу перед краем
                        Frames(run, c, 0.8f, 0f, false); Frames(run, c, 0.3f, 0f, true); if (H.state.gripped) grips++;
                        Frames(run, c, 1.2f, -3f, true); Frames(run, c, 0.3f, 0f, false);
                        minY = Mathf.Min(minY, H.body.Rb.position.y);
                    }
                    var rb = H.body.Rb; bool onTop = rb.position.y - Arm2Body.Height * 0.5f > ph - 0.03f;
                    float tilt = Vector3.Angle(rb.rotation * Vector3.up, Vector3.up);
                    bool ok = onTop && !hung && tilt < 10f && pen < 0.005f;
                    sb.Append(Row("2 передом к краю 1.2, «на себя» ×4", ok, $"{(onTop ? "остался на площадке" : "УПАЛ")} | хватов {grips} | z {rb.position.z:F2} (край {pz:F2}; перед тела на {rb.position.z - Arm2Body.Length * 0.5f - pz:F2} м от края) | наклон {tilt:F0}° | в твёрдом {pen * 1000f:F1} мм"));
                }
                {   // 3 — спиной со ступени 0.3: «от себя» — съехал вниз и лёг ровно; «на себя» — залез обратно (не выше 0.4)
                    step = pen = slip = 0f; hung = false;
                    var run = new ArmRun("ступень", new Vector3(-3f, 0.3f, 2.4f), 0f);
                    Vector3 s = Sh(), c = new Vector3(s.x, 0.3f, s.z + 0.35f);
                    Frames(run, c, 0.7f, 0f, false); Frames(run, c, 0.3f, 0f, true);
                    Frames(run, c, 3.5f, 3f, true); Frames(run, c, 0.6f, 0f, true);
                    var rb = H.body.Rb; float bottom = rb.position.y - Arm2Body.Height * 0.5f, tilt = Vector3.Angle(rb.rotation * Vector3.up, Vector3.up);
                    bool down = bottom < 0.05f;
                    // обратно: отпустить, взяться за верх ступени заново (кисть после спуска стоит на самой кромке) и «на себя»
                    Frames(run, c, 0.3f, 0f, false);
                    for (int g = 0; g < 3 && rb.position.y - Arm2Body.Height * 0.5f < 0.27f; g++)
                    {
                        Vector3 s2 = Sh(); Vector3 c2 = new Vector3(s2.x, 0.3f, Mathf.Max(2.08f, s2.z + 0.5f));
                        Frames(run, c2, 0.9f, 0f, false); Frames(run, c2, 0.3f, 0f, true); Frames(run, c2, 1.6f, -3f, true); Frames(run, c2, 0.5f, 0f, true); Frames(run, c2, 0.4f, 0f, false);
                    }
                    float bottom2 = rb.position.y - Arm2Body.Height * 0.5f;
                    bool ok = down && tilt < 15f && bottom2 > 0.27f && pen < 0.005f && step < bodyStepLimit;
                    sb.Append(Row("3 спиной со ступени 0.3 и обратно", ok, $"{(down ? "съехал вниз" : "НЕ съехал")} (низ тела {bottom:F2}, наклон {tilt:F0}°) | обратно: низ тела {bottom2:F2} ({(bottom2 > 0.27f ? "залез" : "НЕ залез")}) | шаг тела макс {step * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм"));
                }
                sb.Append(bad == 0 ? "ИТОГО: всё ок\n" : $"ИТОГО: плохо в {bad} строках\n");
                sb.Append($"пороги: вис случился и держится; «от себя» в висе опускает ≥ 10 см; в покое тело ходит < 10 мм; шаг тела < {bodyStepLimit * 1000f:F0} мм (И2; падение — < 30 мм); в твёрдом < 5 мм; кисть съезжает < 5 мм\n");
            }
            finally { End(); }
            return sb.ToString();
        }

        // ================= СРЕЗ 7: СКЛОНЫ И ЗАЦЕПЫ =================
        [MenuItem("Fortress/Hero2/Срез 7: замер склонов и зацепов (в Play)")]
        public static void Slice7Menu()
        {
            string r = Slice7(); Debug.Log(r);
            try { System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../tools/herosim/out/arm2_slice7.txt")), r); } catch { }
        }

        /// <summary>СРЕЗ 7: склоны и зацепы. Склон 45° за камни и склон 36° за перила — бот берёт зацеп выше, подтягивается, перехватывает;
        /// должен подняться. Крутой склон без зацепа — кисть съезжает и отпускает; пологий 20° — держит. Отпустил на крутом — тело
        /// сползает. Сторожа: не в твёрдом, без скачков, кисть в хвате за зацеп не съезжает.</summary>
        public static string Slice7()
        {
            string err = Begin(); if (err != null) return err;
            if (!H.arm || !H.grip) { End(); return "у героя нет блока хвата (пересобрать префаб)"; }
            var sb = new StringBuilder($"СРЕЗ 7 — СКЛОНЫ И ЗАЦЕПЫ (песочница), шаг физики {Dt * 1000f:F1} мс\n");
            int bad = 0; float frame = Dt * StepsPerFrame, bodyStepLimit = 4f * Dt;
            try
            {
                H.intent.RescanHolds();
                Vector3 Sh() => H.intent.ShoulderWorld();
                string Row(string name, bool ok, string text) { if (!ok) bad++; return $"{name}: {(ok ? "ок" : "ПЛОХО")} | {text}\n"; }
                float step = 0f, pen = 0f, slip = 0f;
                void Frames(ArmRun run, Vector3 cursor, float seconds, float dy, bool lmb)
                {
                    var rb = H.body.Rb; Vector3 prev = rb.position, h0 = H.arm.HandPos; bool g0 = H.state.gripped;
                    int n = Mathf.RoundToInt(seconds / frame);
                    for (int i = 0; i < n; i++)
                    {
                        run.Frame(cursor, false, lmb, new Vector2(0f, dy));
                        Vector3 p = rb.position; step = Mathf.Max(step, (p - prev).magnitude / StepsPerFrame); prev = p;
                        if (g0 && H.state.gripped && H.state.gripOnHold) slip = Mathf.Max(slip, (H.arm.HandPos - h0).magnitude);
                        if (!g0 && H.state.gripped) { g0 = true; h0 = H.arm.HandPos; }
                        if (!H.state.gripped) g0 = false;
                        pen = Mathf.Max(pen, Mathf.Max(BodyPenetration(), ArmPenetration(out _)));
                    }
                }
                // подъём по зацепам: points — точки на зацепах снизу вверх (мир); каждый жест — самая верхняя точка в досягаемости
                (bool up, int gestures, int grips, float back) ClimbHolds(ArmRun run, Vector3[] points, float goalY)
                {
                    int gestures = 0, grips = 0; float back = 0f; var rb = H.body.Rb;
                    for (int g = 0; g < 14 && rb.position.y < goalY; g++)
                    {
                        Vector3 s = Sh(); int pick = -1;
                        for (int i = 0; i < points.Length; i++) if ((points[i] - s).magnitude < H.intent.Reach - 0.04f && points[i].y > s.y - 0.25f) pick = i;
                        if (pick < 0) break;
                        float y0 = rb.position.y;
                        Frames(run, points[pick], 0.4f, 0f, false); back = Mathf.Max(back, y0 - rb.position.y);
                        Frames(run, points[pick], 0.25f, 0f, true); gestures++; if (H.state.gripped && H.state.gripOnHold) grips++;
                        Frames(run, points[pick], 1.2f, -3f, true); Frames(run, points[pick], 0.15f, 0f, true);
                        Frames(run, points[pick], 0.05f, 0f, false);
                    }
                    return (rb.position.y >= goalY, gestures, grips, back);
                }

                {   // 1 — склон 45° за камни: с пола у подножия до верхнего камня
                    step = pen = slip = 0f; int ri = Arm2Sandbox.RampIndex(45f);
                    var run = new ArmRun("камни", new Vector3(Arm2Sandbox.RampX(ri), 0f, Arm2Sandbox.RampZ0 - 0.25f), 0f);
                    var pts = new Vector3[Arm2Sandbox.NubAlong.Length]; Arm2Sandbox.RampTop(ri, out Vector3 nrm);
                    for (int i = 0; i < pts.Length; i++) pts[i] = Arm2Sandbox.RampPoint(ri, Arm2Sandbox.NubAlong[i], Arm2Sandbox.NubSide) + nrm * 0.05f;
                    var r = ClimbHolds(run, pts, 0.95f);
                    Vector3 e = H.body.Rb.position;
                    bool ok = r.up && pen < 0.005f && step < bodyStepLimit && slip < 0.005f;
                    sb.Append(Row("1 склон 45° за камни", ok, $"{(r.up ? "поднялся" : "НЕ поднялся")} до высоты {e.y:F2} м (цель 0.95) | жестов {r.gestures}, хватов за камень {r.grips} | между хватами сползал макс на {r.back * 100f:F0} см по высоте | шаг тела макс {step * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм | кисть на камне съехала {slip * 1000f:F1} мм"));
                    // отпустил на крутом — тело сползает
                    float yTop = H.body.Rb.position.y;
                    Frames(run, pts[0], 1.2f, 0f, false);
                    bool slid = yTop - H.body.Rb.position.y > 0.2f; if (!slid) bad++;
                    sb.Append($"1 отпустил на 45°: {(slid ? "ок" : "ПЛОХО")} | тело сползло на {(yTop - H.body.Rb.position.y) * 100f:F0} см по высоте за 1.2 с\n");
                }
                {   // 1б — склон 60° за камни (автор: «60 градусов надо тоже»)
                    step = pen = slip = 0f; int ri = Arm2Sandbox.RampIndex(60f);
                    // старт дальше от склона: ближе (0.25 м) вытянутая рука на постановке попадала ПОД плиту склона 60° и клинила (результат «плавал»)
                    var run = new ArmRun("камни 60", new Vector3(Arm2Sandbox.RampX(ri), 0f, Arm2Sandbox.RampZ0 - 0.35f), 0f);
                    var pts = new Vector3[Arm2Sandbox.NubAlong.Length]; Arm2Sandbox.RampTop(ri, out Vector3 nrm);
                    for (int i = 0; i < pts.Length; i++) pts[i] = Arm2Sandbox.RampPoint(ri, Arm2Sandbox.NubAlong[i], Arm2Sandbox.NubSide) + nrm * 0.05f;
                    var r = ClimbHolds(run, pts, 1.15f);
                    Vector3 e = H.body.Rb.position;
                    bool ok = r.up && pen < 0.005f && step < bodyStepLimit && slip < 0.005f;
                    sb.Append(Row("1б склон 60° за камни", ok, $"{(r.up ? "поднялся" : "НЕ поднялся")} до высоты {e.y:F2} м (цель 1.15) | жестов {r.gestures}, хватов за камень {r.grips} | между хватами сползал макс на {r.back * 100f:F0} см по высоте | шаг тела макс {step * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм | кисть на камне съехала {slip * 1000f:F1} мм"));
                    if (!ok) { float ap = ArmPenetration(out var wh); sb.Append($"   диагностика: рука в твёрдом {ap * 1000f:F0} мм ({wh}), тело в твёрдом {BodyPenetration() * 1000f:F0} мм | тело {H.body.Rb.position} | кисть {H.arm.HandPos} цель {H.state.handTarget} | курсор-предмет {(H.state.cursorItem ? H.state.cursorItem.name : "-")} в руке {(H.state.heldItem ? H.state.heldItem.name : "-")} зацеп-цель {(H.state.targetHold ? H.state.targetHold.name : "-")} хочет {H.state.wantGrip} хват {H.state.gripped} | кубики {(GameObject.Find("Кубик 0.12") ? GameObject.Find("Кубик 0.12").transform.position.ToString() : "-")} {(GameObject.Find("Кубик 0.15") ? GameObject.Find("Кубик 0.15").transform.position.ToString() : "-")}" + "\n"); }
                }
                {   // 1в — склон 45° БЕЗ камней: хват за хватом подняться нельзя (кисть съезжает и не тянет)
                    step = pen = slip = 0f; int ri = Arm2Sandbox.RampIndex(45f);
                    var run = new ArmRun("без камней", new Vector3(Arm2Sandbox.RampX(ri) - 0.35f, 0f, Arm2Sandbox.RampZ0 - 0.25f), 0f);
                    float maxY = 0f;
                    for (int g = 0; g < 10; g++)
                    {
                        Vector3 s = Sh(); Vector3 c = new Vector3(s.x - 0.1f, 0f, 0f); float al = Mathf.Max(0.15f, (s.z - Arm2Sandbox.RampZ0) / 0.707f + 0.4f);
                        c = Arm2Sandbox.RampPoint(ri, al, s.x - 0.1f - Arm2Sandbox.RampX(ri));
                        Frames(run, c, 0.35f, 0f, false); Frames(run, c, 0.4f, -6f, true); Frames(run, c, 0.05f, 0f, false);
                        maxY = Mathf.Max(maxY, H.body.Rb.position.y);
                    }
                    bool ok = maxY < 0.3f;
                    sb.Append(Row("1в склон 45° без камней, 10 хватов подряд", ok, $"тело поднялось макс до {maxY:F2} м ({(ok ? "не залез — как задумано" : "ЗАЛЕЗ без зацепов")})"));
                }
                {   // 2 — склон 36° за перила
                    step = pen = slip = 0f; int ri = Arm2Sandbox.RampIndex(36f);
                    var run = new ArmRun("перила", new Vector3(Arm2Sandbox.RampX(ri) + 0.15f, 0f, Arm2Sandbox.RampZ0 - 0.25f), 0f);
                    Arm2Sandbox.RampTop(ri, out Vector3 nrm); var list = new System.Collections.Generic.List<Vector3>();
                    for (float al = 0.15f; al <= 1.86f; al += 0.1f) list.Add(Arm2Sandbox.RampPoint(ri, al, Arm2Sandbox.RailSide) + nrm * Arm2Sandbox.RailHeight);
                    var r = ClimbHolds(run, list.ToArray(), 0.8f);
                    Vector3 e = H.body.Rb.position;
                    bool ok = r.up && pen < 0.005f && step < bodyStepLimit && slip < 0.005f;
                    sb.Append(Row("2 склон 36° за перила", ok, $"{(r.up ? "поднялся" : "НЕ поднялся")} до высоты {e.y:F2} м (цель 0.80) | жестов {r.gestures}, хватов за перила {r.grips} | между хватами сползал макс на {r.back * 100f:F0} см | шаг тела макс {step * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм | кисть на перилах съехала {slip * 1000f:F1} мм"));
                }
                {   // 2в — как в записи автора 15:03: курсор НА СКЛОНЕ в 30 см сбоку от перил и далеко впереди (вне руки) — жест за жестом вверх
                    step = pen = slip = 0f; int ri = Arm2Sandbox.RampIndex(36f);
                    var run = new ArmRun("перила, курсор сбоку", new Vector3(Arm2Sandbox.RampX(ri) + 0.23f, 0f, Arm2Sandbox.RampZ0 - 0.12f), 0f);
                    int grips = 0, gestures = 0; var rb = H.body.Rb; float cs = Mathf.Cos(36f * Mathf.Deg2Rad);
                    for (int g = 0; g < 14 && rb.position.y < 0.8f; g++)
                    {
                        float al = Mathf.Clamp(Mathf.Max(0f, Sh().z - Arm2Sandbox.RampZ0) / cs + 0.9f, 0.3f, 1.9f);
                        Vector3 c = Arm2Sandbox.RampPoint(ri, al, Arm2Sandbox.RailSide - 0.3f);
                        Frames(run, c, 0.4f, 0f, false); Frames(run, c, 0.25f, 0f, true); gestures++; if (H.state.gripped && H.state.gripOnHold) grips++;
                        Frames(run, c, 1.2f, -3f, true); Frames(run, c, 0.15f, 0f, true); Frames(run, c, 0.05f, 0f, false);
                    }
                    bool up = rb.position.y >= 0.8f, ok = up && grips == gestures && pen < 0.005f && step < bodyStepLimit && slip < 0.005f;
                    sb.Append(Row("2в перила, курсор на склоне в 30 см сбоку", ok, $"{(up ? "поднялся" : "НЕ поднялся")} до высоты {rb.position.y:F2} м (цель 0.80) | жестов {gestures}, из них хват за перила {grips} | шаг тела макс {step * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм | кисть на перилах съехала {slip * 1000f:F1} мм"));
                    Frames(run, Arm2Sandbox.RampPoint(ri, 0.3f, 0f), 0.2f, 0f, false);
                }
                {   // 2б — автозахват перил: курсор на 25 см выше перил (автор: «курсором навожусь выше — не хватает автозацепа») и кисть с дальней стороны
                    int ri = Arm2Sandbox.RampIndex(36f); Arm2Sandbox.RampTop(ri, out Vector3 nrm);
                    var run = new ArmRun("перила выше", new Vector3(Arm2Sandbox.RampX(ri) + 0.15f, 0f, Arm2Sandbox.RampZ0 - 0.25f), 0f);
                    Vector3 onRail = Arm2Sandbox.RampPoint(ri, 0.45f, Arm2Sandbox.RailSide) + nrm * Arm2Sandbox.RailHeight;
                    Frames(run, onRail + Vector3.up * 0.25f, 0.9f, 0f, false);
                    bool aimed = H.state.targetHold != null;
                    Frames(run, onRail + Vector3.up * 0.25f, 0.4f, 0f, true);
                    bool got = H.state.gripped && H.state.gripOnHold;
                    Frames(run, onRail, 0.2f, 0f, false);
                    // с дальней стороны: сначала кисть за перила (вправо от них), потом курсор на перила
                    Vector3 beyond = Arm2Sandbox.RampPoint(ri, 0.35f, Arm2Sandbox.RailSide + 0.12f);
                    Frames(run, beyond, 0.9f, 0f, false); Frames(run, onRail, 0.8f, 0f, false); Frames(run, onRail, 0.5f, 0f, true);
                    bool got2 = H.state.gripped && H.state.gripOnHold;
                    sb.Append(Row("2б автозахват перил", aimed && got && got2, $"курсор на 25 см выше перил: цель на перилах — {(aimed ? "да" : "НЕТ")}, взялся — {(got ? "да" : "НЕТ")} | кисть с дальней стороны перил: взялся — {(got2 ? "да" : "НЕТ")}"));
                    Frames(run, onRail, 0.2f, 0f, false);
                }
                {   // 2в — оттолкнуться от крутого склона (автор: «не получается и отталкиваться»): кисть на 45° без зацепа, жест «от себя»
                    int ri = Arm2Sandbox.RampIndex(45f);
                    var run = new ArmRun("оттолкнуться", new Vector3(Arm2Sandbox.RampX(ri) - 0.35f, 0f, Arm2Sandbox.RampZ0 - 0.25f), 0f);
                    Vector3 c = Arm2Sandbox.RampPoint(ri, 0.25f, -0.15f);
                    Frames(run, c, 0.7f, 0f, false);
                    float z0 = H.body.Rb.position.z; Frames(run, c, 0.8f, 4f, true);
                    float back = z0 - H.body.Rb.position.z;
                    sb.Append(Row("2в оттолкнуться от склона 45°", back > 0.05f, $"тело отъехало назад на {back * 100f:F0} см за 0.8 с"));
                    Frames(run, c, 0.2f, 0f, false);
                }
                {   // 2г — кисть из-под ступени 0.3 на её верх (автор: «рука не попадает сверху»): за сколько доходит
                    var run = new ArmRun("на верх", new Vector3(-3f, 0f, 1.62f), 0f);
                    Vector3 low = new Vector3(Sh().x + 0.1f, 0f, 1.9f), topP = new Vector3(Sh().x, 0.3f, 2.15f);
                    Frames(run, low, 0.6f, 0f, false);
                    float t = 0f; bool on = false;
                    for (int i = 0; i < 150 && !on; i++) { Frames(run, topP, frame, 0f, false); t += frame; on = H.arm.HandPos.y > 0.32f && (H.arm.HandPos - H.state.handTarget).magnitude < 0.03f; }
                    sb.Append(Row("2г кисть на верх ступени 0.3", on && t < 0.5f, on ? $"дошла за {t:F2} с" : "НЕ дошла за 1.5 с"));
                }
                {   // 3 — крутой склон без зацепа: кисть съезжает и отпускает; пологий 20° — держит
                    step = pen = slip = 0f; int ri = Arm2Sandbox.RampIndex(45f);
                    var run = new ArmRun("крутой", new Vector3(Arm2Sandbox.RampX(ri) - 0.35f, 0f, Arm2Sandbox.RampZ0 - 0.25f), 0f);
                    Vector3 c = Arm2Sandbox.RampPoint(ri, 0.3f, -0.15f);
                    Frames(run, c, 0.7f, 0f, false);
                    Vector3 h0 = H.arm.HandPos; Frames(run, c, 0.1f, 0f, true); bool took = H.state.gripped, slipping = H.grip.Slipping;
                    Frames(run, c, 0.6f, 0f, true); float moved = (H.arm.HandPos - h0).magnitude; bool let = !H.state.gripped;
                    bool ok = took && slipping && let && moved < 0.12f;
                    sb.Append(Row("3 кисть на 45° без зацепа", ok, $"взялась: {(took ? "да" : "нет")}, съезжает: {(slipping ? "да" : "НЕТ")}, через 0.6 с отпустила: {(let ? "да" : "НЕТ")} | кисть съехала на {moved * 100f:F1} см"));
                    Frames(run, c, 0.2f, 0f, false);
                    ri = Arm2Sandbox.RampIndex(20f);
                    run = new ArmRun("пологий", new Vector3(Arm2Sandbox.RampX(ri), 0f, Arm2Sandbox.RampZ0 - 0.25f), 0f);
                    c = Arm2Sandbox.RampPoint(ri, 0.3f, 0.2f);
                    Frames(run, c, 0.7f, 0f, false); Frames(run, c, 0.8f, 0f, true);
                    ok = H.state.gripped && !H.grip.Slipping;
                    sb.Append(Row("3 кисть на 20°", ok, ok ? "держит (как задумано)" : "НЕ держит"));
                    Frames(run, c, 0.2f, 0f, false);
                }
                sb.Append(bad == 0 ? "ИТОГО: всё ок\n" : $"ИТОГО: плохо в {bad} строках\n");
                sb.Append($"пороги: поднялся по зацепам до цели; шаг тела < {bodyStepLimit * 1000f:F0} мм (И2); в твёрдом < 5 мм (И1); кисть на зацепе съезжает < 5 мм; на 45° без зацепа кисть отпускает, съехав < 12 см\n");
            }
            finally { End(); }
            return sb.ToString();
        }

        // ================= БОТ-ХАОТИК: ЛОВЛЯ «ПОЛЁТА» =================
        /// <summary>Случайные хваты и жесты у ступеней, склонов и площадки. Сторож: тело не быстрее 4 м/с и не выше, чем может быть
        /// (опора под ним + 0.6 м). Первый взлёт — со следом состояния за 0.3 с до него. Автор 2026-11-26: «я начал летать».</summary>
        public static float FuzzSpeed = 4.5f;
        public static string Fuzz(int seeds, float seconds)
        {
            string err = Begin(); if (err != null) return err;
            var sb = new StringBuilder("БОТ-ХАОТИК\n"); float frame = Dt * StepsPerFrame; int launches = 0;
            try
            {
                H.intent.RescanHolds();
                Vector3[] spots = { new Vector3(-3f, 0f, 1.7f), new Vector3(-4.8f, 0f, 1.7f), new Vector3(-1.4f, 0f, 1.7f), new Vector3(-3f, 0.3f, 2.5f), new Vector3(-4.8f, 0.4f, 2.5f),
                                    new Vector3(Arm2Sandbox.RampX(3), 0f, 1.8f), new Vector3(Arm2Sandbox.RampX(4), 0f, 1.55f), new Vector3(Arm2Sandbox.RampX(2) + 0.15f, 0f, 1.8f),
                                    new Vector3(Arm2Sandbox.PlatX, Arm2Sandbox.PlatH, Arm2Sandbox.PlatZ0 + 0.45f), new Vector3(0f, 0f, 3.5f) };
                for (int seed = 0; seed < seeds; seed++)
                {
                    var rng = new System.Random(977 * seed + 13);
                    Vector3 spot = spots[seed % spots.Length];
                    var run = new ArmRun("хаотик", spot, (float)rng.NextDouble() * 360f * (seed % 3 == 0 ? 1f : 0f));
                    var rb = H.body.Rb; var log = new System.Collections.Generic.Queue<string>(); bool launched = false;
                    Vector3 cur = rb.position + rb.rotation * new Vector3(0.2f, 0f, 0.4f); bool lmb = false; float dx = 0f, dy = 0f; int hold = 0;
                    Vector3 prev = rb.position;
                    int n = Mathf.RoundToInt(seconds / frame);
                    for (int i = 0; i < n && !launched; i++)
                    {
                        if (hold-- <= 0)
                        {
                            hold = rng.Next(5, 60);
                            Vector3 s = H.intent.ShoulderWorld(); Vector3 f = rb.rotation * Vector3.forward; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
                            float a = Mathf.Lerp(-60f, 80f, (float)rng.NextDouble()), r = Mathf.Lerp(0.15f, 0.8f, (float)rng.NextDouble());
                            cur = s + Quaternion.AngleAxis(a, Vector3.up) * f * r; cur.y = new[] { 0f, 0.3f, 0.4f, 0.5f, 0.8f, 1.2f }[rng.Next(6)];
                            if (rng.Next(3) == 0) lmb = !lmb;
                            dy = rng.Next(3) == 0 ? 0f : Mathf.Lerp(-8f, 8f, (float)rng.NextDouble()); dx = rng.Next(4) == 0 ? Mathf.Lerp(-6f, 6f, (float)rng.NextDouble()) : 0f;
                        }
                        run.Frame(cur, false, lmb, new Vector2(dx, dy));
                        // «взлёт» — скорость вбок или вверх; свободное падение с площадки 1.2 (4,5 м/с у пола) — не выстрел
                        Vector3 p = rb.position; Vector3 dp = p - prev; if (dp.y < 0f) dp.y = 0f; float v = dp.magnitude / (Dt * StepsPerFrame); prev = p;
                        var st = H.state;
                        log.Enqueue($"   {i * frame:F2} L{(lmb ? 1 : 0)} хват{(st.gripped ? 1 : 0)} зацеп{(st.gripOnHold ? 1 : 0)} вис{(st.hanging ? 1 : 0)} подъём{(H.arm.Climbing ? 1 : 0)} съезд{(H.grip.Slipping ? 1 : 0)} | тело ({p.x:F2}, {p.y:F2}, {p.z:F2}) v {v:F1} наклон {Vector3.Angle(rb.rotation * Vector3.up, Vector3.up):F0}° | кисть {H.arm.HandPos:F2} | тяга {st.pullVec:F0} подъём {st.liftForce:F0} несёт {st.carryForce:F0} | n {st.gripNormal:F2} хочет {st.pullLength:F2}");
                        if (log.Count > 30) log.Dequeue();
                        // опора под телом: луч вниз
                        float ground = -1f;
                        int hc = Physics.RaycastNonAlloc(new Ray(p + Vector3.up * 0.05f, Vector3.down), new RaycastHit[8], 6f, ~0, QueryTriggerInteraction.Ignore);
                        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit gh, 8f, ~(1 << LayerMask.NameToLayer("HeroArm")), QueryTriggerInteraction.Ignore) && !gh.collider.transform.IsChildOf(H.transform)) ground = gh.point.y;
                        if (v > FuzzSpeed || p.y > 2.2f)
                        {
                            launched = true; launches++;
                            sb.Append($"ВЗЛЁТ: зерно {seed}, место {spot}, t = {i * frame:F2} с, скорость {v:F1} м/с, высота {p.y:F2}\n");
                            foreach (var l in log) sb.Append(l).Append('\n');
                        }
                    }
                }
                sb.Append($"зёрен {seeds} по {seconds:F0} с: взлётов {launches}\n");
            }
            finally { End(); }
            return sb.ToString();
        }

        // ================= СРЕЗ 8: ПОДВИЖНОЕ =================
        [MenuItem("Fortress/Hero2/Срез 8: замер платформ (в Play)")]
        public static void Slice8Menu()
        {
            string r = Slice8(); Debug.Log(r);
            try { System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../tools/herosim/out/arm2_slice8.txt")), r); } catch { }
        }

        /// <summary>СРЕЗ 8: подвижное. На карусели: хват за неё и тяга — относительно карусели, курс держится относительно неё.
        /// С пола за проезжающий челнок: взялся — тело поехало за ним; «на себя» — залез и едет вместе. Сторожа: кисть на платформе не
        /// съезжает, не в твёрдом, без скачков.</summary>
        public static string Slice8()
        {
            string err = Begin(); if (err != null) return err;
            if (!H.arm || !H.grip) { End(); return "у героя нет блока хвата (пересобрать префаб)"; }
            var sb = new StringBuilder($"СРЕЗ 8 — ПЛАТФОРМЫ (песочница), шаг физики {Dt * 1000f:F1} мс\n");
            int bad = 0; float frame = Dt * StepsPerFrame, bodyStepLimit = 4f * Dt;
            try
            {
                Vector3 Sh() => H.intent.ShoulderWorld();
                string Row(string name, bool ok, string text) { if (!ok) bad++; return $"{name}: {(ok ? "ок" : "ПЛОХО")} | {text}\n"; }
                var spinner = GameObject.Find(Arm2Sandbox.SpinnerName).GetComponent<Arm2Mover>();
                var shuttle = GameObject.Find(Arm2Sandbox.ShuttleName).GetComponent<Arm2Mover>();
                Vector3 Local(Arm2Mover m, Vector3 w) => Quaternion.Inverse(m.Rb.rotation) * (w - m.Rb.position);
                Vector3 World(Arm2Mover m, Vector3 l) => m.Rb.position + m.Rb.rotation * l;
                float LocalYaw(Arm2Mover m) => Mathf.DeltaAngle(m.Rb.rotation.eulerAngles.y, H.body.YawRb(0f));

                {   // 1 — карусель (30°/с): стоит на ней, берётся за неё перед собой, «на себя» 120 px, держит 3 с, отпускает
                    foreach (var m in Arm2Mover.All) m.ResetPhase();
                    var run = new ArmRun("карусель", World(spinner, new Vector3(-0.35f, 0.1f + Arm2Body.Height * 0.5f + 0.005f, -0.1f)), Quaternion.identity);
                    // курсор — точка на карусели в её координатах: впереди плеча на 0.45
                    Vector3 cl = Local(spinner, Sh() + H.body.Rb.rotation * Vector3.forward * 0.45f); cl.y = 0.1f;
                    float step = 0f, pen = 0f, slip = 0f; Vector3 prevRel = Local(spinner, H.body.Rb.position), hl0 = Vector3.zero; bool g0 = false;
                    void F(float seconds, float dy, bool lmb)
                    {
                        int n = Mathf.RoundToInt(seconds / frame);
                        for (int i = 0; i < n; i++)
                        {
                            run.Frame(World(spinner, cl), false, lmb, new Vector2(0f, dy));
                            Vector3 rel = Local(spinner, H.body.Rb.position); step = Mathf.Max(step, (rel - prevRel).magnitude / StepsPerFrame); prevRel = rel;
                            if (H.state.gripped) { Vector3 hl = Local(spinner, H.arm.HandPos); if (!g0) { g0 = true; hl0 = hl; } slip = Mathf.Max(slip, (hl - hl0).magnitude); } else g0 = false;
                            pen = Mathf.Max(pen, Mathf.Max(BodyPenetration(), ArmPenetration(out _)));
                        }
                    }
                    F(0.8f, 0f, false); F(0.3f, 0f, true);
                    bool got = H.state.gripped && H.state.gripBody == spinner.Rb;
                    Vector3 r0 = Local(spinner, H.body.Rb.position); float y0 = LocalYaw(spinner);
                    F(0.6f, -2f, true); F(0.6f, 0f, true);
                    float moved = (Local(spinner, H.body.Rb.position) - r0).magnitude;
                    Vector3 r1 = Local(spinner, H.body.Rb.position); float y1 = LocalYaw(spinner);
                    F(3f, 0f, true);
                    float drift = (Local(spinner, H.body.Rb.position) - r1).magnitude, yawDrift = Mathf.Abs(Mathf.DeltaAngle(y1, LocalYaw(spinner)));
                    F(0.3f, 0f, false); Vector3 r2 = Local(spinner, H.body.Rb.position);
                    F(2f, 0f, false);
                    float after = (Local(spinner, H.body.Rb.position) - r2).magnitude;
                    bool ok = got && moved > 0.1f && drift < 0.02f && yawDrift < 5f && slip < 0.005f && pen < 0.005f && step < bodyStepLimit && after < 0.03f;
                    sb.Append(Row("1 карусель: хват, тяга, держать", ok, $"взялся за карусель: {(got ? "да" : "НЕТ")} | «на себя» 120 px: тело прошло по карусели {moved * 100f:F1} см | держит 3 с: уход относительно карусели {drift * 1000f:F1} мм, курс относительно неё {yawDrift:F1}° (за это время она повернулась на 90°) | кисть на карусели съехала {slip * 1000f:F1} мм | шаг тела относительно неё макс {step * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм | после отпускания 2 с: уход {after * 1000f:F1} мм"));
                }
                {   // 2 — челнок (0.5 м/с): герой на полу у его пути, берётся за верх челнока, едет за ним, залезает и едет вместе
                    foreach (var m in Arm2Mover.All) m.ResetPhase();
                    // челнок идёт вдоль +X от x = −3; герой стоит южнее его пути, лицом на +Z, в 1 м впереди по ходу
                    var run = new ArmRun("челнок", new Vector3(-2.0f, 0f, Arm2Sandbox.ShuttlePos.z - 0.6f - 0.4f), 0f);
                    float step = 0f, pen = 0f, slip = 0f; Vector3 prev = H.body.Rb.position, hl0 = Vector3.zero; bool g0 = false;
                    Vector3 cl = new Vector3(0f, 0.1f, -0.5f);          // точка на верхе челнока у его южного края (в его координатах, ось X — по ходу)
                    void F(float seconds, float dy, bool lmb, bool follow)
                    {
                        int n = Mathf.RoundToInt(seconds / frame);
                        for (int i = 0; i < n; i++)
                        {
                            // курсор — на южный край челнока напротив плеча (пока не взялся — где плечо; взялся — та же точка челнока)
                            if (!H.state.gripped && follow) cl.x = Mathf.Clamp(Local(shuttle, Sh()).x, -0.55f, 0.55f);
                            run.Frame(World(shuttle, cl), false, lmb, new Vector2(0f, dy));
                            Vector3 p = H.body.Rb.position; step = Mathf.Max(step, (p - prev).magnitude / StepsPerFrame); prev = p;
                            if (H.state.gripped) { Vector3 hl = Local(shuttle, H.arm.HandPos); if (!g0) { g0 = true; hl0 = hl; } slip = Mathf.Max(slip, (hl - hl0).magnitude); } else g0 = false;
                            pen = Mathf.Max(pen, Mathf.Max(BodyPenetration(), ArmPenetration(out _)));
                        }
                    }
                    // ждём, пока челнок подъедет (его середина напротив героя)
                    for (int i = 0; i < 400 && shuttle.Rb.position.x < H.body.Rb.position.x - 0.3f; i++) F(frame, 0f, false, true);
                    F(0.5f, 0f, false, true); F(0.4f, 0f, true, true);
                    bool got = H.state.gripped && H.state.gripBody == shuttle.Rb; float x0 = H.body.Rb.position.x;
                    F(1.5f, 0f, true, false);
                    float dragged = H.body.Rb.position.x - x0;
                    F(1.6f, -3f, true, false); F(0.5f, 0f, true, false);
                    bool onTop = H.body.Rb.position.y - Arm2Body.Height * 0.5f > 0.17f;
                    F(0.3f, 0f, false, false);
                    Vector3 rel0 = Local(shuttle, H.body.Rb.position);
                    F(3f, 0f, false, false);
                    float ride = (Local(shuttle, H.body.Rb.position) - rel0).magnitude;
                    bool ok = got && dragged > 0.4f && onTop && ride < 0.03f && slip < 0.005f && pen < 0.005f && step < bodyStepLimit;
                    sb.Append(Row("2 челнок: с пола взяться, ехать, залезть", ok, $"взялся за челнок: {(got ? "да" : "НЕТ")} | держась 1.5 с, тело проехало за ним {dragged * 100f:F0} см | «на себя»: {(onTop ? "залез на челнок" : "НЕ залез")} | отпустил, 3 с едет вместе: уход относительно челнока {ride * 1000f:F1} мм | кисть на челноке съехала {slip * 1000f:F1} мм | шаг тела макс {step * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм"));
                }
                sb.Append(bad == 0 ? "ИТОГО: всё ок\n" : $"ИТОГО: плохо в {bad} строках\n");
                sb.Append($"пороги: уход относительно платформы < 20–30 мм; курс относительно карусели < 5°; кисть на платформе съезжает < 5 мм; шаг тела < {bodyStepLimit * 1000f:F0} мм; в твёрдом < 5 мм\n");
            }
            finally { End(); }
            return sb.ToString();
        }

        // ================= СРЕЗ 9: ПРЕДМЕТЫ =================
        [MenuItem("Fortress/Hero2/Срез 9: замер предметов (в Play)")]
        public static void Slice9Menu()
        {
            string r = Slice9(); Debug.Log(r);
            try { System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../tools/herosim/out/arm2_slice9.txt")), r); } catch { }
        }

        /// <summary>СРЕЗ 9, кусок 1 (п. 10–11): взять предмет за любую точку и тащить по полу. Кубик 0.12 (0.5 кг) перед плечом:
        /// взять за верх, провести курсором по полу, отпустить; взять за боковую грань, провести. Сторожа: до хвата кисть предмет
        /// не двигает; предмет в руке (сцепка не растягивается); предмет не отрывается от пола; тело не сдвинуто; не в твёрдом.</summary>
        public static StringBuilder Slice9Trace; static int tr;
        public static string Slice9()
        {
            string err = Begin(); if (err != null) return err;
            if (!H.arm || !H.grip) { End(); return "у героя нет блока хвата (пересобрать префаб)"; }
            var sb = new StringBuilder($"СРЕЗ 9 — ПРЕДМЕТЫ: взять и тащить по полу (песочница), шаг физики {Dt * 1000f:F1} мс\n");
            int bad = 0; float frame = Dt * StepsPerFrame;
            var cubeGo = GameObject.Find("Кубик 0.12"); var otherGo = GameObject.Find("Кубик 0.15");
            if (!cubeGo) { End(); return "в сцене нет «Кубик 0.12»"; }
            var cube = cubeGo.GetComponent<Rigidbody>(); var other = otherGo ? otherGo.GetComponent<Rigidbody>() : null;
            Vector3 cube0 = cube.position; Quaternion cubeR0 = cube.rotation; Vector3 other0 = other ? other.position : Vector3.zero;
            try
            {
                string Row(string name, bool ok, string text) { if (!ok) bad++; return $"{name}: {(ok ? "ок" : "ПЛОХО")} | {text}\n"; }
                const float half = 0.06f;
                for (int variant = 0; variant < 2; variant++)
                {
                    // второй кубик — в сторону, первый — перед плечом в 0.42 м
                    if (other) { other.position = other0 + new Vector3(-1.5f, 0f, 0f); other.linearVelocity = Vector3.zero; }
                    Vector3 feet = new Vector3(-0.65f, 0f, -0.6f);
                    Vector3 start = new Vector3(feet.x + 0.2f, half, feet.z + 0.42f);
                    cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                    var run = new ArmRun(variant == 0 ? "за верх" : "за бок", feet, 0f);
                    Vector3 body0 = H.body.Rb.position; float yaw0 = H.body.Yaw(0f);
                    float pen = 0f, rope = 0f, lift = 0f, tilt = 0f; Vector3 localGrab = Vector3.zero; bool wasHeld = false;
                    void F(Vector3 cursor, float seconds, bool lmb)
                    {
                        int n = Mathf.Max(1, Mathf.RoundToInt(seconds / frame));
                        for (int i = 0; i < n; i++)
                        {
                            run.Frame(cursor, false, lmb);
                            pen = Mathf.Max(pen, Mathf.Max(BodyPenetration(), ArmPenetration(out _)));
                            if (Slice9Trace != null && variant == 0 && (tr++ % 4) == 0) Slice9Trace.AppendLine($"L{(lmb ? 1 : 0)} held{(H.state.heldItem ? 1 : 0)} cube {cube.position.ToString("F3")} tilt {Vector3.Angle(cube.rotation * Vector3.up, Vector3.up):F0} w {cube.angularVelocity.magnitude:F1} | hand {H.arm.HandPos.ToString("F3")} tgt {H.state.handTarget.ToString("F3")}");
                            if (H.state.heldItem == cube)
                            {
                                if (!wasHeld) { wasHeld = true; localGrab = cube.transform.InverseTransformPoint(H.arm.HandPos); }
                                rope = Mathf.Max(rope, (cube.transform.TransformPoint(localGrab) - H.arm.HandPos).magnitude);
                                lift = Mathf.Max(lift, cube.position.y - half);
                                tilt = Mathf.Max(tilt, Vector3.Angle(cube.rotation * Vector3.up, Vector3.up));
                            }
                            else wasHeld = false;
                        }
                    }
                    // курсор — на верх кубика или на его грань, обращённую к герою
                    Vector3 aim = variant == 0 ? start + Vector3.up * half : start + new Vector3(0f, 0f, -half);
                    F(aim, 0.7f, false);
                    float pushed = (cube.position - start).magnitude;                       // кисть легла на предмет — предмет на месте
                    F(aim, 0.35f, true);
                    bool got = H.state.heldItem == cube; Vector3 grabLocal = localGrab; Vector3 c0 = cube.position;
                    // курсор ведут по полу: вправо-ближе на 0.3 м со скоростью 0.4 м/с, потом стоит 0.4 с
                    Vector3 from = H.state.cursorValid ? H.state.cursorPoint : new Vector3(start.x, 0f, start.z), to = from + new Vector3(0.25f, 0f, -0.15f);   // от точки, куда курсор и так смотрит сквозь предмет: без скачка курсора
                    int steps = Mathf.RoundToInt((to - from).magnitude / 0.4f / frame);
                    for (int i = 1; i <= steps; i++) F(Vector3.Lerp(from, to, (float)i / steps), frame, true);
                    F(to, 0.4f, true);
                    Vector3 moved = cube.position - c0; Vector3 want = to - from;
                    float miss = (H.state.handTarget - H.arm.HandPos).magnitude;            // кисть с предметом дошла до цели под курсором
                    bool still = H.state.heldItem == cube;
                    // отпустил НА ХОДУ и сразу ведёт кисть сквозь предмет на 0.25 м дальше (автор: «отпустил ЛКМ и продолжил толкать»)
                    Vector3 c1 = cube.position; Vector3 push = new Vector3(c1.x, 0f, c1.z) + (new Vector3(c1.x, 0f, c1.z) - to).normalized * 0.25f;
                    for (int i = 1; i <= 25; i++) F(Vector3.Lerp(to, push, i / 25f), frame, false);
                    F(push, 0.4f, false);
                    float after = (cube.position - c1).magnitude; bool free = H.state.heldItem == null;
                    float bodyMove = (H.body.Rb.position - body0).magnitude, bodyYaw = Mathf.Abs(Mathf.DeltaAngle(yaw0, H.body.Yaw(0f)));
                    bool ok = pushed < 0.002f && got && still && miss < 0.03f && moved.magnitude > 0.2f && tilt < 5f && rope < 0.005f && lift < 0.01f && after < 0.002f && free && bodyMove < 0.01f && bodyYaw < 2f && pen < 0.005f;
                    sb.Append(Row(variant == 0 ? "1 взять за верх, тащить по полу" : "2 взять за боковую грань, тащить по полу", ok,
                        $"до хвата предмет сдвинут {pushed * 1000f:F1} мм | взял: {(got ? "да" : "НЕТ")}, точка хвата на предмете ({grabLocal.x * 0.12f:F2}, {grabLocal.y * 0.12f:F2}, {grabLocal.z * 0.12f:F2}) м от его центра | курсор по полу на {want.magnitude * 100f:F0} см: предмет проехал {new Vector2(moved.x, moved.z).magnitude * 100f:F1} см, кисть до цели не дошла на {miss * 100f:F1} см | сцепка растянулась макс {rope * 1000f:F1} мм | оторвался от пола макс {lift * 1000f:F1} мм, наклон макс {tilt:F0}° | держал до конца: {(still ? "да" : "НЕТ")} | отпустил и провёл кистью сквозь предмет: отпущен — {(free ? "да" : "НЕТ")}, предмет сдвинут {after * 1000f:F1} мм | тело сдвинуто {bodyMove * 1000f:F1} мм / {bodyYaw:F1}° | в твёрдом {pen * 1000f:F1} мм"));
                }
                {   // 3 — ПО СТЕНЕ (п. 12): кубик на полу у стены; взять за грань, обращённую к герою; курсор ведут вверх по стене
                    // до 0.40 м, держат, ведут вбок на 0.2 м, отпускают — кубик падает на пол
                    const float wallZ = 4f;
                    if (other) { other.position = other0 + new Vector3(-1.5f, 0f, 0f); other.linearVelocity = Vector3.zero; }
                    Vector3 feet = new Vector3(0f, 0f, 3.5f);
                    Vector3 start = new Vector3(0.2f, half, wallZ - half - 0.002f);
                    cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                    var run = new ArmRun("по стене", feet, 0f);
                    Vector3 body0 = H.body.Rb.position; float yaw0 = H.body.Yaw(0f);
                    float pen = 0f, rope = 0f, tilt = 0f, gap = 0f, into = 0f; Vector3 localGrab = Vector3.zero; bool wasHeld = false;
                    void F(Vector3 cursor, float seconds, bool lmb)
                    {
                        int n = Mathf.Max(1, Mathf.RoundToInt(seconds / frame));
                        for (int i = 0; i < n; i++)
                        {
                            run.Frame(cursor, false, lmb);
                            pen = Mathf.Max(pen, Mathf.Max(BodyPenetration(), ArmPenetration(out _)));
                            if (H.state.heldItem == cube)
                            {
                                if (!wasHeld) { wasHeld = true; localGrab = cube.transform.InverseTransformPoint(H.arm.HandPos); }
                                rope = Mathf.Max(rope, (cube.transform.TransformPoint(localGrab) - H.arm.HandPos).magnitude);
                                tilt = Mathf.Max(tilt, Vector3.Angle(cube.rotation * Vector3.up, Vector3.up));
                                float g = wallZ - (cube.position.z + half);             // зазор между гранью кубика и стеной (минус — в стене)
                                if (cube.position.y > half + 0.03f) { gap = Mathf.Max(gap, g); into = Mathf.Max(into, -g); }
                            }
                            else wasHeld = false;
                        }
                    }
                    Vector3 aim = start + new Vector3(0f, 0f, -half);
                    F(aim, 0.7f, false);
                    float pushed = (cube.position - start).magnitude;
                    F(aim, 0.35f, true);
                    bool got = H.state.heldItem == cube;
                    Vector3 w0 = new Vector3(start.x, half, wallZ), w1 = new Vector3(start.x, 0.40f, wallZ), w2 = new Vector3(start.x - 0.2f, 0.40f, wallZ);
                    int steps = Mathf.RoundToInt((w1 - w0).magnitude / 0.3f / frame);
                    for (int i = 1; i <= steps; i++) F(Vector3.Lerp(w0, w1, (float)i / steps), frame, true);
                    F(w1, 0.3f, true);
                    float upY = cube.position.y; Vector3 h0 = cube.position;
                    F(w1, 0.6f, true);
                    float drift = (cube.position - h0).magnitude;
                    steps = Mathf.RoundToInt((w2 - w1).magnitude / 0.3f / frame);
                    for (int i = 1; i <= steps; i++) F(Vector3.Lerp(w1, w2, (float)i / steps), frame, true);
                    F(w2, 0.3f, true);
                    float sideX = h0.x - cube.position.x, sideY = cube.position.y; bool still = H.state.heldItem == cube;
                    F(w2, 0.8f, false);
                    bool free = H.state.heldItem == null; float landY = cube.position.y;
                    float bodyMove = (H.body.Rb.position - body0).magnitude, bodyYaw = Mathf.Abs(Mathf.DeltaAngle(yaw0, H.body.Yaw(0f)));
                    bool ok = pushed < 0.002f && got && still && Mathf.Abs(upY - 0.40f) < 0.03f && drift < 0.005f && Mathf.Abs(sideX - 0.2f) < 0.03f && Mathf.Abs(sideY - 0.40f) < 0.03f
                              && gap < 0.01f && into < 0.003f && tilt < 5f && rope < 0.005f && free && landY < half + 0.01f && bodyMove < 0.01f && bodyYaw < 2f && pen < 0.005f;
                    sb.Append(Row("3 взять у стены, вести по стене вверх и вбок", ok,
                        $"до хвата предмет сдвинут {pushed * 1000f:F1} мм | взял: {(got ? "да" : "НЕТ")} | курсор по стене вверх до 0.40 м: центр предмета на {upY:F2} м | держит 0.6 с: уход {drift * 1000f:F1} мм | вбок на 20 см: предмет прошёл {sideX * 100f:F1} см, высота {sideY:F2} м | зазор до стены макс {gap * 1000f:F1} мм, в стене макс {into * 1000f:F1} мм | наклон макс {tilt:F0}° | сцепка {rope * 1000f:F1} мм | держал до конца: {(still ? "да" : "НЕТ")} | отпустил: {(free ? "да" : "НЕТ")}, предмет упал на пол (центр на {landY:F2} м) | тело сдвинуто {bodyMove * 1000f:F1} мм / {bodyYaw:F1}° | в твёрдом {pen * 1000f:F1} мм"));
                }
                for (int variant = 0; variant < 2; variant++)
                {   // 4 — ПОДНЯТЬ И ОПУСТИТЬ (п. 13): кубик на полу перед плечом; взять (за верх / за ближнюю грань); курсор по полу уводят
                    // дальше, чем достаёт рука (на 0.5 м за край зоны), держат, возвращают в зону — кубик поднялся, висит ровно, лёг обратно
                    if (other) { other.position = other0 + new Vector3(-1.5f, 0f, 0f); other.linearVelocity = Vector3.zero; }
                    Vector3 feet = new Vector3(-0.65f, 0f, -0.6f);
                    Vector3 start = new Vector3(feet.x + 0.2f, half, feet.z + 0.42f);
                    cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                    var run = new ArmRun(variant == 0 ? "поднять за верх" : "поднять за бок", feet, 0f);
                    Vector3 body0 = H.body.Rb.position; float yaw0 = H.body.Yaw(0f);
                    float pen = 0f, rope = 0f, tilt = 0f, maxStep = 0f, maxJump = 0f; Vector3 localGrab = Vector3.zero, prevC = cube.position, prevTg = Vector3.zero; bool wasHeld = false; float tt = 0f, penOwn = 0f; string penAt = "", jumpAt = "";
                    void F(Vector3 cursor, float seconds, bool lmb)
                    {
                        int n = Mathf.Max(1, Mathf.RoundToInt(seconds / frame));
                        for (int i = 0; i < n; i++)
                        {
                            run.Frame(cursor, false, lmb); tt += frame;
                            { float bp = BodyPenetration(), ap = ArmPenetration(out string w); if (bp > pen) { pen = bp; penAt = $"тело, t {tt:F2}"; } if (w == cube.name) penOwn = Mathf.Max(penOwn, ap); else if (ap > pen) { pen = ap; penAt = $"{w}, t {tt:F2}"; } }
                            if (H.state.heldItem == cube)
                            {
                                if (!wasHeld) { wasHeld = true; localGrab = cube.transform.InverseTransformPoint(H.arm.HandPos); prevC = cube.position; prevTg = H.state.handTarget; }
                                if ((H.state.handTarget - prevTg).magnitude > maxJump) jumpAt = $"t {tt:F2}";
                                rope = Mathf.Max(rope, (cube.transform.TransformPoint(localGrab) - H.arm.HandPos).magnitude);
                                tilt = Mathf.Max(tilt, Vector3.Angle(cube.rotation * Vector3.up, Vector3.up));
                                maxStep = Mathf.Max(maxStep, (cube.position - prevC).magnitude / StepsPerFrame);
                                maxJump = Mathf.Max(maxJump, (H.state.handTarget - prevTg).magnitude);
                            }
                            else wasHeld = false;
                            prevC = cube.position; prevTg = H.state.handTarget;
                        }
                    }
                    Vector3 aim = variant == 0 ? start + Vector3.up * half : start + new Vector3(0f, 0f, -half);
                    F(aim, 0.7f, false);
                    F(aim, 0.35f, true);
                    bool got = H.state.heldItem == cube;
                    Vector3 S = H.intent.ShoulderWorld(); float reach = H.intent.Reach;
                    Vector3 near = H.state.cursorValid ? H.state.cursorPoint : new Vector3(start.x, 0f, start.z), far = new Vector3(near.x, 0f, S.z + reach + 0.5f);
                    int steps = Mathf.RoundToInt((far - near).magnitude / 0.5f / frame);
                    for (int i = 1; i <= steps; i++) F(Vector3.Lerp(near, far, (float)i / steps), frame, true);
                    F(far, 0.3f, true);
                    float upY = cube.position.y - half, wantLift = H.intent.HeldLiftNow; Vector3 h0 = cube.position;
                    F(far, 0.6f, true);
                    float drift = (cube.position - h0).magnitude; bool still = H.state.heldItem == cube;
                    for (int i = 1; i <= steps; i++) F(Vector3.Lerp(far, near, (float)i / steps), frame, true);
                    F(near, 0.4f, true);
                    float downY = cube.position.y - half; bool stillDown = H.state.heldItem == cube;
                    F(near, 0.5f, false);
                    bool free = H.state.heldItem == null; float restY = cube.position.y - half;
                    float bodyMove = (H.body.Rb.position - body0).magnitude, bodyYaw = Mathf.Abs(Mathf.DeltaAngle(yaw0, H.body.Yaw(0f)));
                    bool ok = got && still && stillDown && upY > 0.15f && drift < 0.005f && downY < 0.01f && restY < 0.01f && free && tilt < 5f && rope < 0.005f
                              && maxStep < 0.03f && maxJump < 0.06f && bodyMove < 0.01f && bodyYaw < 2f && pen < 0.005f;
                    sb.Append(Row(variant == 0 ? "4 поднять и опустить, хват за верх" : "5 поднять и опустить, хват за ближнюю грань", ok,
                        $"взял: {(got ? "да" : "НЕТ")} | курсор по полу на 0.5 м за край зоны руки: низ предмета на {upY * 100f:F1} см над полом (курсор просит {wantLift * 100f:F1} см) | держит 0.6 с: уход {drift * 1000f:F1} мм | курсор назад в зону: низ на {downY * 1000f:F1} мм | отпустил: {(free ? "да" : "НЕТ")}, низ на {restY * 1000f:F1} мм | наклон макс {tilt:F0}° | сцепка {rope * 1000f:F1} мм | шаг предмета макс {maxStep * 1000f:F1} мм за шаг физики | скачок цели макс {maxJump * 100f:F1} см за кадр ({jumpAt}) | держал до конца: {(still && stillDown ? "да" : "НЕТ")} | тело сдвинуто {bodyMove * 1000f:F1} мм / {bodyYaw:F1}° | в твёрдом {pen * 1000f:F1} мм ({penAt}) | рука в своём предмете {penOwn * 1000f:F1} мм (не в зачёт)"));
                }
                {   // 6–7 — ПРИЗЕМЛИТЬ С ВОЗДУХА НА ДРУГУЮ ПЛОСКОСТЬ (п. 14): кубик на полу перед ступенью 0.3; взять за ближнюю грань, поднять
                    // (курсор далеко за зону руки, по верху ступени), вернуть курсор на верх ступени у кромки — кубик садится на ступень;
                    // отпустить. Потом взять его со ступени и курсором на пол — кубик садится на пол.
                    if (other) { other.position = other0 + new Vector3(-1.5f, 0f, 0f); other.linearVelocity = Vector3.zero; }
                    const float stepZ = 2f, stepH = 0.3f;
                    Vector3 feet = new Vector3(-3.2f, 0f, 1.5f);
                    Vector3 start = new Vector3(feet.x + 0.2f, half, feet.z + 0.42f);
                    cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                    var run = new ArmRun("приземлить", feet, 0f);
                    Vector3 body0 = H.body.Rb.position; float yaw0 = H.body.Yaw(0f);
                    float pen = 0f, penOwn = 0f, rope = 0f, tilt = 0f, maxStep = 0f, maxJump = 0f, tt = 0f; string penAt = "";
                    Vector3 localGrab = Vector3.zero, prevC = cube.position, prevTg = Vector3.zero; bool wasHeld = false;
                    void F(Vector3 cursor, float seconds, bool lmb)
                    {
                        int n = Mathf.Max(1, Mathf.RoundToInt(seconds / frame));
                        for (int i = 0; i < n; i++)
                        {
                            run.Frame(cursor, false, lmb); tt += frame;
                            { float bp = BodyPenetration(), ap = ArmPenetration(out string w); if (bp > pen) { pen = bp; penAt = $"тело, t {tt:F2}"; } if (w == cube.name) penOwn = Mathf.Max(penOwn, ap); else if (ap > pen) { pen = ap; penAt = $"{w}, t {tt:F2}"; } }
                            if (H.state.heldItem == cube)
                            {
                                if (!wasHeld) { wasHeld = true; localGrab = cube.transform.InverseTransformPoint(H.arm.HandPos); prevC = cube.position; prevTg = H.state.handTarget; }
                                rope = Mathf.Max(rope, (cube.transform.TransformPoint(localGrab) - H.arm.HandPos).magnitude);
                                tilt = Mathf.Max(tilt, Vector3.Angle(cube.rotation * Vector3.up, Vector3.up));
                                maxStep = Mathf.Max(maxStep, (cube.position - prevC).magnitude / StepsPerFrame);
                                maxJump = Mathf.Max(maxJump, (H.state.handTarget - prevTg).magnitude);
                            }
                            else wasHeld = false;
                            prevC = cube.position; prevTg = H.state.handTarget;
                        }
                    }
                    void Go(Vector3 a, Vector3 b, float speed) { int k = Mathf.Max(1, Mathf.RoundToInt((b - a).magnitude / speed / frame)); for (int i = 1; i <= k; i++) F(Vector3.Lerp(a, b, (float)i / k), frame, true); }
                    // вверх
                    Vector3 aim = start + new Vector3(0f, 0f, -half);
                    F(aim, 0.7f, false); F(aim, 0.35f, true);
                    bool got = H.state.heldItem == cube;
                    Vector3 farTop = new Vector3(start.x, stepH, stepZ + 1.0f), edgeTop = new Vector3(start.x, stepH, stepZ + 0.07f);
                    Go(aim, farTop, 0.8f); F(farTop, 0.3f, true);
                    float airY = cube.position.y - half;
                    Go(farTop, edgeTop, 0.8f); F(edgeTop, 0.5f, true);
                    bool held1 = H.state.heldItem == cube; Vector3 onStep = cube.position;
                    F(edgeTop, 0.6f, false);
                    Vector3 rest1 = cube.position; float tilt1 = Vector3.Angle(cube.rotation * Vector3.up, Vector3.up);
                    bool ok = got && held1 && airY > stepH + 0.05f && Mathf.Abs(rest1.y - (stepH + half)) < 0.01f && rest1.z > stepZ - 0.02f && tilt1 < 5f && tilt < 5f && rope < 0.005f
                              && maxStep < 0.03f && maxJump < stepH + 0.03f && (H.body.Rb.position - body0).magnitude < 0.01f && pen < 0.005f;      // цель прыгает на высоту самой ступени — это геометрия, не рывок
                    sb.Append(Row("6 с пола поднять и посадить на ступень 0.3", ok,
                        $"взял: {(got ? "да" : "НЕТ")} | курсор далеко по верху ступени: низ предмета на {airY * 100f:F1} см | курсор на верх ступени у кромки: центр предмета ({onStep.x - start.x:F2}, {onStep.y:F2}, {onStep.z - stepZ:F2} от кромки) | отпустил: лежит на высоте {rest1.y:F3} м (ждём {stepH + half:F2}), от кромки {rest1.z - stepZ:F2} м, наклон {tilt1:F0}° | в руке: наклон макс {tilt:F0}°, сцепка {rope * 1000f:F1} мм, шаг предмета макс {maxStep * 1000f:F1} мм, скачок цели макс {maxJump * 100f:F1} см | тело сдвинуто {(H.body.Rb.position - body0).magnitude * 1000f:F1} мм / {Mathf.Abs(Mathf.DeltaAngle(yaw0, H.body.Yaw(0f))):F1}° | в твёрдом {pen * 1000f:F1} мм ({penAt}) | рука в своём предмете {penOwn * 1000f:F1} мм (не в зачёт)"));
                    // вниз
                    pen = 0f; penAt = ""; penOwn = 0f; rope = 0f; tilt = 0f; maxStep = 0f; maxJump = 0f; wasHeld = false;
                    Vector3 aim2 = cube.position + new Vector3(0f, 0f, -half);
                    F(aim2, 0.7f, false); Vector3 before = cube.position; F(aim2, 0.35f, true);
                    bool got2 = H.state.heldItem == cube; float pushed2 = (cube.position - before).magnitude;
                    Vector3 floorPt = new Vector3(start.x, 0f, stepZ - 0.2f);
                    Go(aim2, floorPt, 0.5f); F(floorPt, 0.5f, true);
                    bool held2 = H.state.heldItem == cube;
                    F(floorPt, 0.6f, false);
                    Vector3 rest2 = cube.position; float tilt2 = Vector3.Angle(cube.rotation * Vector3.up, Vector3.up);
                    ok = got2 && held2 && Mathf.Abs(rest2.y - half) < 0.01f && rest2.z < stepZ - half + 0.005f && tilt2 < 5f && tilt < 5f && rope < 0.005f
                         && maxStep < 0.03f && maxJump < stepH + 0.03f && (H.body.Rb.position - body0).magnitude < 0.01f && pen < 0.005f;      // цель прыгает на высоту самой ступени — это геометрия, не рывок
                    sb.Append(Row("7 взять со ступени 0.3 и посадить на пол", ok,
                        $"взял: {(got2 ? "да" : "НЕТ")} (до хвата сдвинут {pushed2 * 1000f:F1} мм) | курсор на пол перед ступенью: отпустил — лежит на высоте {rest2.y:F3} м (ждём {half:F2}), до ступени {stepZ - half - rest2.z:F2} м, наклон {tilt2:F0}° | в руке: наклон макс {tilt:F0}°, сцепка {rope * 1000f:F1} мм, шаг предмета макс {maxStep * 1000f:F1} мм, скачок цели макс {maxJump * 100f:F1} см | держал до конца: {(held2 ? "да" : "НЕТ")} | тело сдвинуто {(H.body.Rb.position - body0).magnitude * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм ({penAt}) | рука в своём предмете {penOwn * 1000f:F1} мм (не в зачёт)"));
                }
                {   // 8 — ПРЕДМЕТ ЗА СПИНУ НЕЛЬЗЯ (автор 27.11): кубик в руке, курсор уводят за спину слева и справа и далеко влево —
                    // кисть с предметом остаётся в секторе руки (−sectorLeft…+sectorRight от лица, вокруг плеча)
                    if (other) { other.position = other0 + new Vector3(-1.5f, 0f, 0f); other.linearVelocity = Vector3.zero; }
                    Vector3 feet = new Vector3(-0.65f, 0f, -0.6f);
                    Vector3 start = new Vector3(feet.x + 0.2f, half, feet.z + 0.42f);
                    cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                    var run = new ArmRun("за спину", feet, 0f);
                    Vector3 body0 = H.body.Rb.position; float yaw0 = H.body.Yaw(0f);
                    float minA = 0f, maxA = 0f, pen = 0f, maxJump = 0f; Vector3 prevTg = Vector3.zero; bool had = false;
                    void F(Vector3 cursor, float seconds, bool lmb)
                    {
                        int n = Mathf.Max(1, Mathf.RoundToInt(seconds / frame));
                        for (int i = 0; i < n; i++)
                        {
                            run.Frame(cursor, false, lmb);
                            { float bp = BodyPenetration(), ap = ArmPenetration(out string w); pen = Mathf.Max(pen, bp); if (w != cube.name) pen = Mathf.Max(pen, ap); }
                            if (H.state.heldItem == cube)
                            {
                                Vector3 S = H.intent.ShoulderWorld(), hz = H.arm.HandPos - S; hz.y = 0f;
                                Vector3 fc = H.body.Rb.rotation * Vector3.forward; fc.y = 0f;
                                if (hz.magnitude > 0.08f) { float a = Vector3.SignedAngle(fc, hz, Vector3.up); minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a); }
                                if (had) maxJump = Mathf.Max(maxJump, (H.state.handTarget - prevTg).magnitude);
                            }
                            prevTg = H.state.handTarget; had = H.state.heldItem == cube;
                        }
                    }
                    void Go(Vector3 a, Vector3 b) { int k = Mathf.Max(1, Mathf.RoundToInt((b - a).magnitude / 0.8f / frame)); for (int i = 1; i <= k; i++) F(Vector3.Lerp(a, b, (float)i / k), frame, true); }
                    Vector3 aim = start + new Vector3(0f, 0f, -half);
                    F(aim, 0.7f, false); F(aim, 0.35f, true);
                    bool got = H.state.heldItem == cube;
                    Vector3 c0 = new Vector3(start.x, 0f, start.z), backR = new Vector3(feet.x + 0.5f, 0f, feet.z - 0.5f), backL = new Vector3(feet.x - 0.4f, 0f, feet.z - 0.5f), farL = new Vector3(feet.x - 0.8f, 0f, feet.z + 0.1f);
                    Go(c0, backR); F(backR, 0.4f, true); Go(backR, backL); F(backL, 0.4f, true); Go(backL, farL); F(farL, 0.4f, true); Go(farL, c0); F(c0, 0.4f, true);
                    bool still = H.state.heldItem == cube; float back = (cube.position - start).magnitude;
                    F(c0, 0.4f, false);
                    float bodyMove = (H.body.Rb.position - body0).magnitude, bodyYaw = Mathf.Abs(Mathf.DeltaAngle(yaw0, H.body.Yaw(0f)));
                    bool ok = got && still && maxA < H.intent.sectorRight + 4f && minA > -H.intent.sectorLeft - 4f && back < 0.05f && bodyMove < 0.01f && bodyYaw < 2f && pen < 0.005f;
                    sb.Append(Row("8 курсор за спину и вбок: предмет остаётся в секторе руки", ok,
                        $"взял: {(got ? "да" : "НЕТ")} | курсор за спину справа, за спину слева, далеко влево: угол кисти от лица {minA:F0}°…{maxA:F0}° (сектор −{H.intent.sectorLeft:F0}°…+{H.intent.sectorRight:F0}°) | скачок цели макс {maxJump * 100f:F1} см за кадр | курсор назад вперёд: предмет в {back * 100f:F1} см от места, где взяли | держал до конца: {(still ? "да" : "НЕТ")} | тело сдвинуто {bodyMove * 1000f:F1} мм / {bodyYaw:F1}° | в твёрдом {pen * 1000f:F1} мм"));
                }
                {   // 9 — ОДНИМ ДВИЖЕНИЕМ ПО ДУГЕ (автор 27.11, запись 18:02: «какой-то барьер, должен в одно движение»): кубик в руке,
                    // курсор далеко (2.5 м от тела) ведут по дуге слева-сзади через перёд направо-назад и обратно. Предмет идёт без скачков.
                    if (other) { other.position = other0 + new Vector3(-1.5f, 0f, 0f); other.linearVelocity = Vector3.zero; }
                    Vector3 feet = new Vector3(-0.65f, 0f, -0.6f);
                    Vector3 start = new Vector3(feet.x + 0.2f, half, feet.z + 0.42f);
                    cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                    var run = new ArmRun("по дуге", feet, 0f);
                    Vector3 body0 = H.body.Rb.position; float yaw0 = H.body.Yaw(0f);
                    float pen = 0f, maxJump = 0f, maxStep = 0f, minY = 9f, maxY = 0f, jumpAtDeg = 0f, curDeg = 0f; Vector3 prevTg = Vector3.zero, prevC = cube.position; bool had = false;
                    void F(Vector3 cursor, float seconds, bool lmb, bool measure)
                    {
                        int n = Mathf.Max(1, Mathf.RoundToInt(seconds / frame));
                        for (int i = 0; i < n; i++)
                        {
                            run.Frame(cursor, false, lmb);
                            { float bp = BodyPenetration(), ap = ArmPenetration(out string w); pen = Mathf.Max(pen, bp); if (w != cube.name) pen = Mathf.Max(pen, ap); }
                            if (measure && H.state.heldItem == cube && had)
                            {
                                float j = (H.state.handTarget - prevTg).magnitude; if (j > maxJump) { maxJump = j; jumpAtDeg = curDeg; }
                                maxStep = Mathf.Max(maxStep, (cube.position - prevC).magnitude / StepsPerFrame);
                                minY = Mathf.Min(minY, cube.position.y - half); maxY = Mathf.Max(maxY, cube.position.y - half);
                            }
                            prevTg = H.state.handTarget; prevC = cube.position; had = H.state.heldItem == cube;
                        }
                    }
                    Vector3 aim = start + new Vector3(0f, 0f, -half);
                    F(aim, 0.7f, false, false); F(aim, 0.35f, true, false);
                    bool got = H.state.heldItem == cube;
                    Vector3 Arc(float deg) => new Vector3(feet.x, 0f, feet.z) + Quaternion.AngleAxis(deg, Vector3.up) * Vector3.forward * 2.5f;
                    // к началу дуги (не в зачёт), потом дуга туда и обратно
                    { Vector3 a0 = new Vector3(start.x, 0f, start.z), a1 = Arc(-120f); int k = 60; for (int i = 1; i <= k; i++) F(Vector3.Lerp(a0, a1, (float)i / k), frame, true, false); F(a1, 0.4f, true, false); }
                    int steps = Mathf.RoundToInt(2.0f / frame);
                    for (int i = 1; i <= steps; i++) { curDeg = Mathf.Lerp(-120f, 120f, (float)i / steps); F(Arc(curDeg), frame, true, true); }
                    for (int i = 1; i <= steps; i++) { curDeg = Mathf.Lerp(120f, -120f, (float)i / steps); F(Arc(curDeg), frame, true, true); }
                    bool still = H.state.heldItem == cube;
                    F(Arc(-120f), 0.3f, false, false);
                    float bodyMove = (H.body.Rb.position - body0).magnitude, bodyYaw = Mathf.Abs(Mathf.DeltaAngle(yaw0, H.body.Yaw(0f)));
                    bool ok = got && still && maxJump < 0.06f && maxStep < 0.03f && bodyMove < 0.01f && bodyYaw < 2f && pen < 0.005f;
                    sb.Append(Row("9 курсор далеко по дуге: предмет идёт одним движением", ok,
                        $"взял: {(got ? "да" : "НЕТ")} | дуга −120°…+120° и обратно за 4 с, курсор в 2.5 м: скачок цели макс {maxJump * 100f:F1} см за кадр (курсор на {jumpAtDeg:F0}°) | шаг предмета макс {maxStep * 1000f:F1} мм за шаг физики | низ предмета над полом {minY * 100f:F0}…{maxY * 100f:F0} см | держал до конца: {(still ? "да" : "НЕТ")} | тело сдвинуто {bodyMove * 1000f:F1} мм / {bodyYaw:F1}° | в твёрдом {pen * 1000f:F1} мм"));
                }
                if (other)
                {   // 10–11 — СТОПКА (п. 15): малый кубик поставить на большой; потом взять БОЛЬШОЙ за ближнюю грань и протащить по полу —
                    // малый едет на нём и не падает
                    const float halfB = 0.075f;
                    Vector3 feet = new Vector3(-0.65f, 0f, -0.6f);
                    Vector3 start = new Vector3(feet.x + 0.2f, half, feet.z + 0.42f), big0 = new Vector3(feet.x + 0.42f, halfB, feet.z + 0.38f);
                    cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                    other.position = big0; other.rotation = Quaternion.identity; other.linearVelocity = Vector3.zero; other.angularVelocity = Vector3.zero;
                    var run = new ArmRun("стопка", feet, 0f);
                    cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                    other.position = big0; other.rotation = Quaternion.identity; other.linearVelocity = Vector3.zero; other.angularVelocity = Vector3.zero;
                    Vector3 body0 = H.body.Rb.position; float pen = 0f, tiltTop = 0f, tiltBig = 0f, tS = 0f, bigLift = 0f, bigSpeed = 0f, slipAt = -1f; int riders = 0;
                    void F(Vector3 cursor, float seconds, bool lmb)
                    {
                        int n = Mathf.Max(1, Mathf.RoundToInt(seconds / frame));
                        for (int i = 0; i < n; i++)
                        {
                            run.Frame(cursor, false, lmb);
                            { float bp = BodyPenetration(), ap = ArmPenetration(out string w); pen = Mathf.Max(pen, bp); if (!H.state.heldItem || w != H.state.heldItem.name) pen = Mathf.Max(pen, ap); }
                            tiltTop = Mathf.Max(tiltTop, Vector3.Angle(cube.rotation * Vector3.up, Vector3.up)); tiltBig = Mathf.Max(tiltBig, Vector3.Angle(other.rotation * Vector3.up, Vector3.up));
                            tS += frame; bigLift = Mathf.Max(bigLift, other.position.y - halfB); bigSpeed = Mathf.Max(bigSpeed, other.linearVelocity.magnitude); riders = Mathf.Max(riders, Arm2ItemGuard.Riders);
                            if (slipAt < 0f && tS > 0.1f && Mathf.Abs((cube.position - other.position).y - (halfB + half)) > 0.02f) slipAt = tS;
                        }
                    }
                    void Go(Vector3 a, Vector3 b, float speed) { int k = Mathf.Max(1, Mathf.RoundToInt((b - a).magnitude / speed / frame)); for (int i = 1; i <= k; i++) F(Vector3.Lerp(a, b, (float)i / k), frame, true); }
                    // 10: поставить
                    Vector3 aim = start + new Vector3(0f, 0f, -half);
                    F(aim, 0.7f, false); F(aim, 0.35f, true);
                    bool got = H.state.heldItem == cube;
                    Vector3 onBig = big0 + Vector3.up * halfB;
                    Go(aim, onBig, 0.5f); F(onBig, 0.6f, true);
                    Vector3 held1 = cube.position; float bigPushed = (other.position - big0).magnitude; string dbg = $"под курсором ({H.state.cursorPoint.x - big0.x:F2}, {H.state.cursorPoint.y:F2}, {H.state.cursorPoint.z - big0.z:F2}) цель кисти ({H.state.handTarget.x - big0.x:F2}, {H.state.handTarget.y:F2}, {H.state.handTarget.z - big0.z:F2}) кисть ({H.arm.HandPos.x - big0.x:F2}, {H.arm.HandPos.y:F2}, {H.arm.HandPos.z - big0.z:F2}) в руке {(H.state.heldItem ? H.state.heldItem.name : "-")}";
                    F(onBig, 0.8f, false);
                    Vector3 rel = cube.position - other.position; float bigMoved = (other.position - big0).magnitude;
                    bool ok = got && Mathf.Abs(rel.y - (halfB + half)) < 0.01f && new Vector2(rel.x, rel.z).magnitude < 0.03f && tiltTop < 5f && tiltBig < 3f && bigMoved < 0.01f && (H.body.Rb.position - body0).magnitude < 0.01f && pen < 0.005f;
                    sb.Append(Row("10 поставить малый кубик на большой", ok,
                        $"взял: {(got ? "да" : "НЕТ")} | в руке над большим: центр малого ({held1.x - big0.x:F2}, {held1.y:F2}, {held1.z - big0.z:F2}) от центра большого, большой сдвинут {bigPushed * 1000f:F1} мм | отпустил: малый лежит на {rel.y:F3} м над центром большого (ждём {halfB + half:F3}), вбок {new Vector2(rel.x, rel.z).magnitude * 100f:F1} см, наклон макс {tiltTop:F0}° | большой сдвинут {bigMoved * 1000f:F1} мм, наклон {tiltBig:F0}° | тело {(H.body.Rb.position - body0).magnitude * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм | {dbg}"));
                    // 11: тащить стопку за нижний
                    pen = 0f; tiltTop = 0f; tiltBig = 0f; tS = 0f; bigLift = 0f; bigSpeed = 0f; slipAt = -1f; riders = 0;
                    Vector3 bigA = other.position, rel0 = cube.position - other.position;
                    Vector3 aim2 = bigA + new Vector3(0f, -0.02f, -halfB);
                    F(aim2, 0.7f, false); float before = (other.position - bigA).magnitude; F(aim2, 0.35f, true);
                    bool got2 = H.state.heldItem == other;
                    Vector3 from = H.state.cursorValid ? H.state.cursorPoint : new Vector3(bigA.x, 0f, bigA.z), to = from + new Vector3(-0.25f, 0f, 0.05f);
                    Go(from, to, 0.3f); F(to, 0.4f, true);
                    bool held2 = H.state.heldItem == other;
                    F(to, 0.6f, false);
                    Vector3 rel1 = cube.position - other.position; float travel = new Vector2(other.position.x - bigA.x, other.position.z - bigA.z).magnitude;
                    float shift = new Vector2(rel1.x - rel0.x, rel1.z - rel0.z).magnitude;
                    ok = got2 && held2 && travel > 0.2f && Mathf.Abs(rel1.y - (halfB + half)) < 0.01f && shift < 0.02f && tiltTop < 5f && tiltBig < 3f && (H.body.Rb.position - body0).magnitude < 0.01f && pen < 0.005f;
                    sb.Append(Row("11 тащить стопку за нижний кубик", ok,
                        $"взял нижний: {(got2 ? "да" : "НЕТ")} (до хвата сдвинут {before * 1000f:F1} мм) | курсор по полу на 25 см: нижний проехал {travel * 100f:F1} см | верхний на нём: съехал на {shift * 100f:F1} см, высота над центром нижнего {rel1.y:F3} м (ждём {halfB + half:F3}), наклон макс {tiltTop:F0}° | нижний наклон макс {tiltBig:F0}° | держал до конца: {(held2 ? "да" : "НЕТ")} | тело {(H.body.Rb.position - body0).magnitude * 1000f:F1} мм | в твёрдом {pen * 1000f:F1} мм | нижний: оторвался от пола макс {bigLift * 1000f:F0} мм, скорость макс {bigSpeed:F2} м/с; едет на нём предметов: {riders}; верхний сорвался на {slipAt:F2} с"));
                }
                sb.Append(bad == 0 ? "ИТОГО: всё ок\n" : $"ИТОГО: плохо в {bad} строках\n");
                sb.Append("пороги: до хвата предмет < 2 мм; кисть до цели < 3 см; наклон предмета < 5°; сцепка < 5 мм; отрыв от пола < 10 мм; после отпускания кистью предмет < 2 мм; тело < 10 мм и < 2°; в твёрдом < 5 мм\n");
            }
            finally
            {
                cube.position = cube0; cube.rotation = cubeR0; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                if (other) { other.position = other0; other.linearVelocity = Vector3.zero; }
                End();
            }
            return sb.ToString();
        }
        /// <summary>ПРОБА «СТУПЕНЬКИ X» (запись автора 27.11 14:10: «не могу по ступенькам подняться»): лесенка 7 × 5 см, проступь 6 см.
        /// Бот: курсор на проступь, ЛКМ, жест «на себя», отпустить — и так cycles раз. Печатает ход тела и причины отказа хвата.</summary>
        public static string StepsProbe(int cycles = 8, float cursorX = 0.1f)
        {
            string err = Begin(); if (err != null) return err;
            var sb = new StringBuilder("ПРОБА СТУПЕНЬКИ X\n");
            try
            {
                Vector3 O = Arm2Sandbox.ObstacleOrigin(1);
                var run = new ArmRun("ступеньки", O + new Vector3(0f, 0f, -0.2f), 0f);       // как в записи автора: рука в покое — перед лесенкой, а не в ней
                for (int c = 0; c < cycles; c++)
                {
                    // самая высокая проступь, до которой рука достаёт
                    Vector3 S = H.intent.ShoulderWorld(); Vector3 aim = Vector3.zero; int pick = -1;
                    for (int k = 6; k >= 0; k--)
                    {
                        Vector3 p = new Vector3(O.x + cursorX, 0.05f * (k + 1), O.z + 0.23f + k * 0.06f);
                        if ((p - S).magnitude < H.intent.Reach - 0.03f && p.y - (H.body.Rb.position.y - 0.1f) < 0.38f) { aim = p; pick = k; break; }
                    }
                    if (pick < 0) { aim = new Vector3(S.x, H.body.Rb.position.y - 0.1f, S.z + 0.45f); }
                    Arm2Grip.Why.Clear();
                    for (int i = 0; i < 40; i++) run.Frame(aim, false, false);
                    Vector3 h0 = H.arm.HandPos;
                    for (int i = 0; i < 30; i++) run.Frame(aim, false, true);
                    bool got = H.state.gripped; Vector3 hg = H.arm.HandPos, b0 = H.body.Rb.position; float ny = H.state.gripNormal.y; int slipSteps = 0, grippedSteps = 0;
                    for (int i = 0; i < 120; i++) { run.Frame(aim, false, true, new Vector2(0f, -3f)); if (H.grip.Slipping) slipSteps++; if (H.state.gripped) grippedSteps++; }
                    Vector3 b1 = H.body.Rb.position; bool held = H.state.gripped;
                    for (int i = 0; i < 60; i++) run.Frame(aim, false, false);
                    Vector3 b2 = H.body.Rb.position;
                    var why = new StringBuilder(); foreach (var kv in Arm2Grip.Why) why.Append($"{kv.Key}×{kv.Value} ");
                    sb.Append($"{c + 1}: проступь {pick + 1} курсор ({aim.x:F2},{aim.y:F2},{aim.z:F2}) кисть до ЛКМ ({h0.x:F2},{h0.y:F2},{h0.z:F2}) под курсором ({H.state.cursorPoint.x:F2},{H.state.cursorPoint.y:F2},{H.state.cursorPoint.z:F2}) цель ({H.state.handTarget.x:F2},{H.state.handTarget.y:F2},{H.state.handTarget.z:F2}) плечо ({S.x:F2},{S.y:F2},{S.z:F2}) | хват {(got ? "да" : "НЕТ")} в ({hg.x:F2},{hg.y:F2},{hg.z:F2}) нормаль.y {ny:F2} | в хвате {grippedSteps}/120, съезд {slipSteps} | тело ({b0.x:F2},{b0.y:F2},{b0.z:F2}) → ({b1.x:F2},{b1.y:F2},{b1.z:F2}) → после отпускания ({b2.x:F2},{b2.y:F2},{b2.z:F2}) наклон {Vector3.Angle(H.body.Rb.rotation * Vector3.up, Vector3.up):F0}° | {why}\n");
                }
            }
            finally { End(); }
            return sb.ToString();
        }

        /// <summary>ПРОБА ПРЕГРАД: тот же ход бота, что в срезе 2 (строка 9), с печатью по кадрам, когда тело сдвинулось или кисть идёт в обход.</summary>
        public static string ObstacleProbe(int oi)
        {
            string err = Begin(); if (err != null) return err;
            var sb = new StringBuilder("ПРОБА ПРЕГРАД " + oi + "\n");
            try
            {
                Vector3 O = Arm2Sandbox.ObstacleOrigin(oi);
                var run = new ArmRun("проба", O, 0f);
                var rng = new System.Random(1234 + oi);
                Vector3 P() => O + new Vector3(Mathf.Lerp(-0.45f, 0.8f, (float)rng.NextDouble()), new[] { 0f, 0.1f, 0.25f, 0.4f }[rng.Next(4)], Mathf.Lerp(0.05f, 0.8f, (float)rng.NextDouble()));
                Vector3 from = P(), b0 = H.body.Rb.position; float t = 0f, frame = Dt * StepsPerFrame, lastMove = 0f; bool wasDet = false; int n = 0;
                void F(Vector3 c)
                {
                    run.Frame(c, false); t += frame; n++;
                    float mv = (H.body.Rb.position - b0).magnitude; bool det = H.arm.Detouring;
                    if (det != wasDet || mv > lastMove + 0.005f || (det && n % 5 == 0))
                    {
                        Vector3 h = H.arm.HandPos, g = H.arm.GhostPos, tg = H.state.handTarget, e = H.arm.ElbowPos;
                        sb.Append($"{t:F2} обход {(det ? 1 : 0)} тело {mv * 1000f:F0} мм | курсор ({c.x - O.x:F2},{c.y:F2},{c.z - O.z:F2}) цель ({tg.x - O.x:F2},{tg.y:F2},{tg.z - O.z:F2}) призрак ({g.x - O.x:F2},{g.y:F2},{g.z - O.z:F2}) кисть ({h.x - O.x:F2},{h.y:F2},{h.z - O.z:F2}) локоть ({e.x - O.x:F2},{e.y:F2},{e.z - O.z:F2})\n");
                        if (mv > lastMove + 0.005f) lastMove = mv;
                    }
                    wasDet = det;
                }
                for (int k = 0; k < 24; k++)
                {
                    Vector3 to = P(); float sp = k % 3 == 0 ? 3f : 0.8f;
                    int m = Mathf.RoundToInt(Mathf.Max(0.1f, Vector3.Distance(from, to) / sp) / frame);
                    for (int i = 0; i < m; i++) F(Vector3.Lerp(from, to, (i + 1f) / m));
                    if (k % 4 == 3) { int hN = Mathf.RoundToInt(1.0f / frame); for (int i = 0; i < hN; i++) F(to); }
                    from = to;
                }
            }
            finally { End(); }
            return sb.ToString();
        }

        /// <summary>ПРОБА «ПРЕДМЕТ ПО ЛЕСЕНКЕ» (запись автора 27.11 15:00: кубик в руке, курсор по ступенькам X — кисть стоит).
        /// Кубик 0.12 на полу перед лесенкой; взять за ближнюю грань (side = false) или за верх (true); курсор — на проступи 3 и 7.</summary>
        public static string CarryProbe(bool top)
        {
            string err = Begin(); if (err != null) return err;
            var sb = new StringBuilder("ПРОБА ПРЕДМЕТ ПО ЛЕСЕНКЕ, хват " + (top ? "за верх" : "за ближнюю грань") + "\n");
            var cube = GameObject.Find("Кубик 0.12").GetComponent<Rigidbody>(); Vector3 cube0 = cube.position; Quaternion cubeR0 = cube.rotation;
            try
            {
                Vector3 O = Arm2Sandbox.ObstacleOrigin(1); const float half = 0.06f; float frame = Dt * StepsPerFrame;
                Vector3 start = new Vector3(O.x, half, O.z + 0.13f);                   // кубик на полу вплотную к первому подступёнку
                cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                var run = new ArmRun("проба", O + new Vector3(-0.03f, 0f, -0.28f), 0f);
                cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                float t = 0f; int n = 0; float maxJump = 0f; int jumps = 0; Vector3 lastTg = Vector3.zero; bool hadTg = false;
                void F(Vector3 c, float seconds, bool lmb, string tag)
                {
                    int m = Mathf.Max(1, Mathf.RoundToInt(seconds / frame));
                    for (int i = 0; i < m; i++)
                    {
                        run.Frame(c, false, lmb); t += frame; n++;
                        if (H.state.heldItem && hadTg) { float j = (H.state.handTarget - lastTg).magnitude; maxJump = Mathf.Max(maxJump, j); if (j > 0.03f) jumps++; }
                        lastTg = H.state.handTarget; hadTg = H.state.heldItem;
                        if (n % 10 == 0 || i == m - 1)
                        {
                            Vector3 h = H.arm.HandPos, g = H.arm.GhostPos, tg = H.state.handTarget, cp = cube.position, e = H.arm.ElbowPos, b = H.body.Rb.position;
                            sb.Append($"{t:F2} {tag} в руке {(H.state.heldItem ? 1 : 0)} обход {(H.arm.Detouring ? 1 : 0)} | цель ({tg.x - O.x:F2},{tg.y:F2},{tg.z - O.z:F2}) призрак ({g.x - O.x:F2},{g.y:F2},{g.z - O.z:F2}) кисть ({h.x - O.x:F2},{h.y:F2},{h.z - O.z:F2}) локоть ({e.x - O.x:F2},{e.y:F2},{e.z - O.z:F2}) | кубик ({cp.x - O.x:F2},{cp.y:F2},{cp.z - O.z:F2}) наклон {Vector3.Angle(cube.rotation * Vector3.up, Vector3.up):F0}° | тело ({b.x - O.x:F2},{b.y:F2},{b.z - O.z:F2})\n");
                        }
                    }
                }
                Vector3 aim = top ? start + Vector3.up * half : start + new Vector3(0f, 0f, -half);
                F(aim, 0.6f, false, "подвод");
                F(aim, 0.35f, true, "хват");
                Vector3 s3 = new Vector3(O.x + 0.05f, 0.15f, O.z + 0.23f + 2 * 0.06f), s7 = new Vector3(O.x + 0.05f, 0.25f, O.z + 0.23f + 4 * 0.06f);
                int steps = 80; for (int i = 1; i <= steps; i++) F(Vector3.Lerp(aim, s3, (float)i / steps), frame, true, "к 3");
                F(s3, 1.0f, true, "на 3");
                for (int i = 1; i <= steps; i++) F(Vector3.Lerp(s3, s7, (float)i / steps), frame, true, "к 7");
                F(s7, 1.2f, true, "на 7");
                sb.Append($"ИТОГ: скачков цели > 3 см за кадр: {jumps}, наибольший {maxJump * 100f:F1} см\n");
                F(s7, 0.5f, false, "отпустил");
            }
            finally { cube.position = cube0; cube.rotation = cubeR0; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero; End(); }
            return sb.ToString();
        }

        /// <summary>ПРОБА «КУБИК ПО ВЕРХАМ ПРЕГРАД Z» (запись автора 27.11 17:40: кубик в руке, курсор по столбику, стенке, блокам —
        /// цель прыгает, кисть застревает). Кубик с пола за ближнюю грань; курсор по очереди: верх столбика 0.5, верх дальней стенки
        /// 0.4, снова столбик, пол. На каждой точке — дошёл ли предмет (где его центр), за сколько, отставание кисти.</summary>
        public static string CarryZProbe()
        {
            string err = Begin(); if (err != null) return err;
            var sb = new StringBuilder("ПРОБА КУБИК ПО ВЕРХАМ ПРЕГРАД Z\n");
            var cube = GameObject.Find("Кубик 0.12").GetComponent<Rigidbody>(); Vector3 cube0 = cube.position; Quaternion cubeR0 = cube.rotation;
            try
            {
                Vector3 O = Arm2Sandbox.ObstacleOrigin(0); const float half = 0.06f; float frame = Dt * StepsPerFrame;
                Vector3 start = new Vector3(O.x + 0.30f, half, O.z + 0.16f);
                cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                var run = new ArmRun("проба", O + new Vector3(-0.02f, 0f, 0f), 0f);          // место автора в записи 17:40
                cube.position = start; cube.rotation = Quaternion.identity; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero;
                int jumps = 0; float maxJump = 0f; Vector3 lastTg = Vector3.zero; bool had = false;
                void F(Vector3 c, bool lmb)
                {
                    run.Frame(c, false, lmb);
                    if (H.state.heldItem && had) { float j = (H.state.handTarget - lastTg).magnitude; maxJump = Mathf.Max(maxJump, j); if (j > 0.10f) jumps++; }
                    lastTg = H.state.handTarget; had = H.state.heldItem;
                }
                Vector3 aim = start + new Vector3(0f, 0f, -half), cur = aim;
                for (int i = 0; i < 70; i++) F(aim, false);
                for (int i = 0; i < 35; i++) F(aim, true);
                sb.Append($"взял: {(H.state.heldItem == cube ? "да" : "НЕТ")}\n");
                void To(string name, Vector3 target, Vector3 wantCentre, float tol)
                {
                    int k = Mathf.Max(1, Mathf.RoundToInt((target - cur).magnitude / 0.8f / frame));
                    for (int i = 1; i <= k; i++) F(Vector3.Lerp(cur, target, (float)i / k), true);
                    cur = target; float arrive = -1f; int hold = Mathf.RoundToInt(1.2f / frame);
                    for (int i = 0; i < hold; i++)
                    {
                        F(target, true);
                        Vector3 d = cube.position - wantCentre;
                        if (arrive < 0f && Mathf.Abs(d.y) < 0.02f && new Vector2(d.x, d.z).magnitude < tol) arrive = (i + 1) * frame;
                    }
                    Vector3 cp = cube.position, tg = H.state.handTarget, h = H.arm.HandPos;
                    sb.Append($"{name}: {(arrive >= 0f ? $"дошёл за {arrive:F2} с" : "НЕ ДОШЁЛ за 1,2 с")} | центр предмета ({cp.x - O.x:F2}, {cp.y:F2}, {cp.z - O.z:F2}), ждём ({wantCentre.x - O.x:F2}, {wantCentre.y:F2}, {wantCentre.z - O.z:F2}) | кисть отстала от цели на {(h - tg).magnitude * 100f:F1} см | наклон {Vector3.Angle(cube.rotation * Vector3.up, Vector3.up):F0}° | в руке: {(H.state.heldItem == cube ? "да" : "НЕТ")} | обход: {(H.arm.Detouring ? 1 : 0)} | цель кисти ({tg.x - O.x:F2}, {tg.y:F2}, {tg.z - O.z:F2}), от плеча до цели {(tg - H.intent.ShoulderWorld()).magnitude:F2} м (рука {H.intent.Reach:F2}), под курсором ({H.state.cursorPoint.x - O.x:F2}, {H.state.cursorPoint.y:F2}, {H.state.cursorPoint.z - O.z:F2})\n");
                }
                Vector3 pillar = O + new Vector3(0.42f, 0.5f, 0.34f), wall = O + new Vector3(0.22f, 0.4f, 0.65f), floor = O + new Vector3(0.30f, 0f, 0.16f), block = O + new Vector3(0.125f, 0.25f, 0.39f);
                To("верх столбика 0.5", pillar, pillar + Vector3.up * half, 0.05f);
                To("верх дальней стенки 0.4", wall, new Vector3(wall.x, 0.4f + half, wall.z), 0.07f);
                To("верх столбика 0.5 (второй раз)", pillar, pillar + Vector3.up * half, 0.05f);
                To("верх блока 0.25", block, block + Vector3.up * half, 0.07f);
                To("пол", floor, floor + Vector3.up * half, 0.05f);
                sb.Append($"ИТОГ: скачков цели > 10 см за кадр: {jumps}, наибольший {maxJump * 100f:F0} см\n");
            }
            finally { cube.position = cube0; cube.rotation = cubeR0; cube.linearVelocity = Vector3.zero; cube.angularVelocity = Vector3.zero; End(); }
            return sb.ToString();
        }
    }
}
