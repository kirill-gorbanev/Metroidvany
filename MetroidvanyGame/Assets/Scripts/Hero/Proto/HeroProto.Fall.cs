using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ПАДЕНИЕ — отдельный блок (шаг 7 долга). Автор: «падать можно спиной вперёд!». Правила — геймдизайнер (2026-11-21).
    ///
    /// Цель ощущения: обрыв никогда не забирает случайно — падение твоё решение (шаг назад и дожим) или цена отпущенной руки.
    ///
    /// Начинается (под центром тела перепад больше maxDrop):
    ///   · СПИНОЙ С КРАЯ — тело ходом назад (от лица) упёрлось в край обрыва, жест «от себя» продолжается fallPushPx — сорвался;
    ///     лицом вперёд край остаётся стеной (лицо там, где рука; «на себя» — главный жест, за него не наказываем);
    ///   · СПОЛЗ — сползание по крутому дошло до края обрыва;
    ///   · ОПОРА УШЛА — платформа / диск / мост уехали из-под центра тела (тело не держится рукой);
    ///   · ОТПУСТИЛ ВИС — над полом глубже maxDrop.
    /// Полёт (позиции, не физика): отступ от кромки fallClearDist за fallClearTime (тело не цепляет край), по горизонтали —
    /// скорость опоры в момент потери (не больше fallCarryMax), по высоте y = y0 − ½·g·t² (не быстрее fallMaxSpeed).
    /// Ввод не исполняется, кроме хвата: в первые catchWindow метров полёта клик по зацепу / камню в досягаемости — вис.
    /// Приземление — первая поверхность под центром. Цена — по высоте падения × масса (heroMass):
    ///   ≤ maxDrop — ничего; ≤ landSoftH — присел (0.25 с); ≤ stunH — оглушение (1 с); ≤ heavyH — тяжёлое (2.5 с); выше — смерть
    ///   (пока — на старт зоны). Та же шкала «масса × величина» — потом для зажима.</summary>
    public partial class HeroProto
    {
        [Header("Падение (блок Fall)")]
        public bool allowFall = true;
        [Tooltip("Спиной у края: столько px жеста «от себя» (при высоте экрана 1080) после упора — и сорвался.")]
        public float fallPushPx = 60f;
        public float fallClearDist = 0.3f, fallClearTime = 0.12f;
        public float fallG = 20f, fallMaxSpeed = 12f, fallCarryMax = 3f;
        [Tooltip("Хват в полёте — пока пролетел не больше этого (м).")]
        public float catchWindow = 1f;
        public float landSoftH = 1.5f, stunH = 3f, heavyH = 4.5f;
        public float landSoftTime = 0.25f, stunTime = 1f, heavyTime = 2.5f;
        public float heroMass = 1f;

        bool falling; float fallY, fallVy, fallStartY, fallClearT; Vector3 fallVel, fallClearDir; string fallCause = "";
        float stunT, fallPx; Vector3 fallPushFrom;
        [System.NonSerialized] public int fallCount, deathCount, catchCount;
        [System.NonSerialized] public string lastLanding = "";
        public bool Falling => falling;
        public bool Stunned => stunT > 0f;
        public bool DiagRespawned { get; private set; }

        void FallReset() { falling = false; fallVy = 0f; stunT = 0f; fallPx = 0f; fallCount = deathCount = catchCount = 0; lastLanding = ""; }

        /// <summary>Перепад под точкой глубже maxDrop (обрыв), а не стена и не ступень.</summary>
        bool DropAt(Vector3 q)
        {
            if (!HasSurface) return false;
            float h = LevelHeight(q, BodyLevel, maxStep);
            return !float.IsInfinity(h) && h < groundY - maxDrop;
        }

        float fallClearNow = -1f, fallClearTimeNow;                     // отступ этого падения (м) и за сколько (с); −1 — обычный fallClearDist

        /// <summary>Падение с отступом от стенки ровно на clearDist (м) за clearTime (с) — без «выстрела» (отпустил вис).</summary>
        void StartFall(string cause, Vector3 vel, Vector3 clearDir, float clearDist, float clearTime)
        {
            StartFall(cause, vel, clearDist > 1e-3f ? clearDir : Vector3.zero);
            if (!falling || clearDist <= 1e-3f) return;
            fallClearNow = clearDist; fallClearTimeNow = Mathf.Max(0.05f, clearTime); fallClearT = fallClearTimeNow;
        }

        void StartFall(string cause, Vector3 vel, Vector3 clearDir)
        {
            fallClearNow = -1f;
            if (!allowFall || falling || float.IsInfinity(groundY + climbLift)) return;
            if (mode == Mode.Grip) ReleaseGrip("падение: " + cause);
            falling = true; fallCause = cause; fallCount++;
            fallY = groundY + climbLift; fallStartY = fallY; fallVy = 0f; climbLift = 0f;
            fallVel = Vector3.ClampMagnitude(Flat(vel), fallCarryMax);
            fallClearDir = Flat(clearDir); if (fallClearDir.sqrMagnitude > 1e-6f) fallClearDir.Normalize();
            fallClearT = fallClearDir.sqrMagnitude > 0f ? fallClearTime : 0f;
            rideMover = null; slideV = 0f; steepTimer = 0f; fallPx = 0f;
            hand = Flat(Shoulder + Face * 0.35f);                          // кисть сразу перед плечом: не тянется через тело с пола
            SetBodyY(fallY + bodyLift);                                    // в кадре старта — на высоте, с которой падает (не мигает к полу)
        }

        /// <summary>СПИНОЙ С КРАЯ: жест «от себя» ведёт тело назад (от лица), тело упёрлось в обрыв — копим px, дожал — сорвался.
        /// true — жест ушёл в дожим (тело не едет).</summary>
        bool BackFallPush(float alongPx)
        {
            if (!allowFall || mode != Mode.Grip || Hanging || alongPx >= 0f || gripAge < letGoGrace) { if (alongPx > 0f || gripAge < letGoGrace) fallPx = 0f; return false; }
            Vector3 away = Flat(farEnd - nearEnd);                          // «от себя» — от кисти
            if (away.sqrMagnitude < 1e-6f) return false;
            away.Normalize();
            if (Vector3.Dot(away, Face) >= 0f) { fallPx = 0f; return false; }   // лицом вперёд — край стена
            // ДЕРЖИШЬСЯ — НЕ СРЫВАЕШЬСЯ (автор 2026-11-25, спуск, кусок 1: «плавно, без рывков»): кисть на верху твоего уступа — за край
            // уходишь в вис (TickLogic), рука выпрямилась раньше края — упор; падение — только отпустив (кусок 3)
            if (!gripHold && gripTop >= groundY + climbLift - 0.03f) { fallPx = 0f; return false; }
            bool drop = DropAt(Body + away * (BodyReach + 0.08f));
            if (!drop) { fallPx = 0f; return false; }
            if (fallPx <= 0f) fallPushFrom = Body;
            if ((Body - fallPushFrom).magnitude > 0.02f) { fallPushFrom = Body; fallPx = 0f; return false; }   // ещё едет к краю
            fallPx += -alongPx * Px1080;
            if (fallPx < fallPushPx) return false;                          // упор: тело стоит у края, тёплое (ClimbVisuals)
            StartFall("спиной с края", Vector3.zero, away);
            return true;
        }

        /// <summary>Сползание упёрлось в край обрыва — падение со скоростью сползания.</summary>
        bool SlideOffEdge(Vector3 downDir, float speed)
        {
            if (!allowFall || downDir.sqrMagnitude < 1e-6f) return false;
            Vector3 d = Flat(downDir).normalized;
            if (!DropAt(Body + d * (BodyReach + 0.08f))) return false;
            StartFall("сполз с края", d * speed, d);
            return true;
        }

        /// <summary>Шаг полёта: ловля, горизонталь, высота, приземление и его цена. Вызывается вместо обычного шага.</summary>
        void FallTick(float dt, InputFrame inp, Vector2 px)
        {
            // ХВАТ В ПОЛЁТЕ: первые catchWindow метров — клик по зацепу / камню в досягаемости
            hoverHold = null;
            if (fallStartY - fallY <= catchWindow && FindHold(px, out var hh, out float hs))
            {
                hoverHold = hh; hoverS = hs;
                if (inp.down)
                {
                    float g = LevelHit(Body, fallY, 0.01f, out _, out _);
                    groundY = g; climbLift = Mathf.Max(0f, fallY - g); falling = false; catchCount++;
                    lastLanding = $"поймал «{hh.name}» в полёте ({fallStartY - fallY:F2} м)";
                    BeginGripHold(hh, hs);
                    return;
                }
            }
            hand = Flat(Shoulder + Face * 0.35f);                          // кисть перед плечом и в отступе, и в полёте (не через тело)
            // горизонталь: отступ от кромки + перенесённая скорость; стены — препятствия и всё выше текущей высоты
            Vector3 move = fallVel * dt;
            if (fallClearT > 0f)
            {
                float dist = fallClearNow >= 0f ? fallClearNow : fallClearDist, time = fallClearNow >= 0f ? fallClearTimeNow : fallClearTime;
                float k = Mathf.Min(dt, fallClearT); move += fallClearDir * (dist / time) * k; fallClearT -= dt;
            }
            if (move.sqrMagnitude > 1e-12f)
            {
                Vector3 b = Body;
                foreach (var m in new[] { move, new Vector3(move.x, 0f, 0f), new Vector3(0f, 0f, move.z) })
                    if (!FlyBlocked(b + m)) { MoveBodyXZ(b.x + m.x, b.z + m.z); break; }
            }
            // высота: пока тело отступает от кромки — держится (иначе «приземлится» на край, с которого сорвалось)
            if (fallClearT > 0f) { SetBodyY(fallY + bodyLift); return; }
            float prevY = fallY;
            fallVy = Mathf.Min(fallVy + fallG * dt, fallMaxSpeed);
            fallY -= fallVy * dt;
            float ground = LevelHit(Body, prevY, 0.001f, out var mover, out var n);
            if (float.IsInfinity(ground))
            {
                // тело уже «утоплено» в поверхность (высота догоняла склон и отстала) — приземлиться на её верх, а не в бесконечность
                ground = LevelHit(Body, prevY + 1f, 0.001f, out mover, out n);
                if (float.IsInfinity(ground)) ground = prevY;
                Land(ground, mover, n);
                return;
            }
            if (fallY <= ground)
            {
                Land(ground, mover, n);
                return;
            }
            SetBodyY(fallY + bodyLift);
        }

        bool FlyBlocked(Vector3 p)
        {
            foreach (var o in tallWalls) if (boxBody ? BoxHitsRect(p, o, 0.001f) : InObstacle(p, bodyRadius)) return true;
            if (!HasSurface) return false;
            return float.IsInfinity(LevelHeight(p, fallY, 0.02f));        // внутри сплошного выше текущей высоты — стена
        }

        /// <summary>Журнал падений: причина, откуда (x, y, z), высота, исход — последние 20.</summary>
        public readonly System.Collections.Generic.List<string> fallLog = new System.Collections.Generic.List<string>();
        void Land(float ground, ProtoMover mover, Vector3 n)
        {
            fallLog.Add($"{fallCause} у ({Body.x:F2}, {fallStartY:F2}, {Body.z:F2}) на {ground:F2}"); if (fallLog.Count > 20) fallLog.RemoveAt(0);
            falling = false;
            float H = (fallStartY - ground) * heroMass;
            groundY = ground; climbLift = 0f; fallVy = 0f;
            rideMover = mover; rideStep = mover ? mover.StepId : 0; bodyNormal = n;
            SetBodyY(ground + bodyLift);
            string what;
            if (H <= maxDrop) what = "мягко";
            else if (H <= landSoftH) { stunT = landSoftTime; what = "присел"; }
            else if (H <= stunH) { stunT = stunTime; what = "оглушение"; }
            else if (H <= heavyH) { stunT = heavyTime; what = "тяжёлое оглушение"; }
            else
            {
                what = "СМЕРТЬ";
                string log = $"{fallCause}: {H:F2} м — {what} (на старт зоны)";
                int fc = fallCount, dc = deathCount + 1, cc = catchCount;
                DiagReset(); DiagRespawned = true;                         // на старт зоны; счёт и запись — сохранить
                fallCount = fc; deathCount = dc; catchCount = cc; lastLanding = log;
                return;
            }
            lastLanding = $"{fallCause}: {H:F2} м — {what}";
        }

        /// <summary>Начало шага: оглушение гасит ввод; полёт — вместо обычного шага (true — шаг сделан).</summary>
        bool FallStep(float dt, ref InputFrame inp, Vector2 px)
        {
            DiagRespawned = false;
            if (stunT > 0f) { stunT -= dt; inp.down = false; inp.pressed = false; inp.delta = Vector2.zero; }
            if (!falling) return false;
            FallTick(dt, inp, px);
            return true;
        }
    }
}
