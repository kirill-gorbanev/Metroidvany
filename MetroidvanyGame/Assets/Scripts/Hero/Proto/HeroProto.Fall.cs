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

        void FallReset() { if (falling && bodyRb) RestoreBodyRb(); falling = false; fallVy = 0f; stunT = 0f; fallPx = 0f; fallCount = deathCount = catchCount = 0; lastLanding = ""; }

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
            if (PhysFallOn) { fallClearT = 0f; physFallT = 0f; physFallInit = false; }   // от кромки — сама физика (опрокинется, соскользнёт)
        }

        // ================= ПАДЕНИЕ НА ФИЗИКЕ UNITY (кусок 1) =================
        // Автор 2026-11-25: «оставить физичность — на краю через физику могу упасть; нет разгона от гравитации»; «давай больше на физику
        // Unity». Падение — настоящий Rigidbody: гравитация Unity, вращение свободно, столкновения со всем твёрдым (пол, уступы, стены):
        // центр за краем — тело само опрокидывается через кромку и соскальзывает. Кубик тела рисуется по физике. Затих на опоре —
        // логика снова ставит тело прямо на эту опору (цена — как была, по высоте падения).
        [Tooltip("Падение — физика Unity (гравитация, вращение, столкновения). Выкл — прежний скриптовый полёт.")]
        public bool physicsFall = true;
        [Tooltip("Затухание вращения тела в физическом падении: выше — меньше кувырков.")]
        public float fallSpinDamping = 4f;
        [Tooltip("Тело в физическом падении вращается не быстрее этого (рад/с).")]
        public float fallMaxSpin = 3f;
        bool PhysFallOn => physicsFall && itemPhysics && bodyRb;
        float physFallT; bool physFallInit; float physStillT; PhysicsMaterial fallMat, slipMat;
        Vector3 physStableAt; bool physStableSet;                           // физика оставила тело на той же высоте — здесь устоит
        Quaternion rightFrom; float rightT = 1f, rightYaw; const float RightTime = 0.3f;
        Vector3 rightOff, rightOffShown;                                    // посадка отодвинула от стенки — рисунок догоняет плавно

        /// <summary>После физического падения тело встаёт прямо плавно: рисунок кубика — от позы, в которой легло, к прямой (логика уже
        /// считает его прямым). Вызывается в конце шага, поверх логики.</summary>
        void RightingStep(float dt)
        {
            if (rightT >= 1f || falling) { rightOff = Vector3.zero; return; }
            rightT = Mathf.Min(1f, rightT + dt / RightTime);
            float k = rightT * rightT * (3f - 2f * rightT);
            bodyCube.rotation = Quaternion.Slerp(rightFrom, Quaternion.Euler(0f, rightYaw, 0f), k);   // только рисунок
            rightOffShown = rightOff * (1f - k); bodyCube.position += rightOffShown;
            if (rightT >= 1f) { rightOff = Vector3.zero; }
        }
        /// <summary>Начало шага: логика видит тело прямым (наклон подъёма — только рисунок конца шага).</summary>
        void RightingUndo()
        {
            if (rightOffShown != Vector3.zero) { bodyCube.position -= rightOffShown; rightOffShown = Vector3.zero; }
            if (rightT < 1f && !falling) bodyCube.rotation = Quaternion.Euler(0f, rightYaw, 0f);
        }

        /// <summary>Шаг физики в падении (вместо обычного): тело — Rigidbody с гравитацией; после шага кубик тела — по физике;
        /// затих на опоре — посадка.</summary>
        void PhysFallStep(float dt)
        {
            float hgt = Mathf.Abs(bodyCube.lossyScale.y);
            if (!physFallInit)
            {
                physFallInit = true;
                Vector2 half = BodyHalf;
                bodyBox.size = new Vector3(half.x * 2f, hgt, half.y * 2f);
                bodyRb.constraints = RigidbodyConstraints.None;
                bodyRb.useGravity = true;
                bodyRb.maxDepenetrationVelocity = 1f;
                bodyRb.position = new Vector3(Body.x, fallY + hgt * 0.5f, Body.z);
                bodyRb.rotation = bodyCube.rotation;
                bodyRb.linearVelocity = new Vector3(fallVel.x, -fallVy, fallVel.z);
                bodyRb.angularVelocity = Vector3.zero;
                bodyRb.angularDamping = fallSpinDamping;                    // опрокидывается, а не кувыркается от малейшего толчка
                bodyRb.maxAngularVelocity = fallMaxSpin;                     // не волчком (автор: «вот перевороты»)
                slipMat = bodyBox.sharedMaterial;                           // у стен тело скользкое; в падении — обычное трение (затихает)
                if (fallMat == null) fallMat = new PhysicsMaterial("Тело (падение)") { dynamicFriction = 0.6f, staticFriction = 0.7f, bounciness = 0.05f,
                    frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
                bodyBox.sharedMaterial = fallMat;
                ignoredColliders = -1;                                      // столкновения — со всем твёрдым (IgnoreWorld)
                IgnoreWorld();
            }
            CarryPhysicsStep(dt);
            Physics.Simulate(dt);
            physFallT += dt;
            Vector3 v = bodyRb.linearVelocity;
            if (v.y < -fallMaxSpeed) bodyRb.linearVelocity = new Vector3(v.x, -fallMaxSpeed, v.z);
            bodyCube.SetPositionAndRotation(bodyRb.position, bodyRb.rotation);
            fallY = bodyRb.position.y - hgt * 0.5f;                         // для ловли в полёте и цены падения
            // затих на опоре — посадка (не сразу после старта: тело ещё лежит на кромке, с которой срывается)
            bool still = bodyRb.linearVelocity.magnitude < 0.06f && bodyRb.angularVelocity.magnitude < 0.4f;
            physStillT = still ? physStillT + dt : 0f;
            if ((physFallT > 0.15f && physStillT > 0.08f) || physFallT > 3f) PhysLand();   // затих — или 3 с (застрял, крутится) — посадка
        }

        /// <summary>Посадка после физического падения: тело прямо (поворот — только вокруг вертикали), на опору под собой.</summary>
        void PhysLand()
        {
            float yaw = Vector3.SignedAngle(Vector3.forward, Flat(bodyCube.forward).sqrMagnitude > 1e-4f ? Flat(bodyCube.forward) : Flat(bodyCube.up), Vector3.up);
            float low = bodyRb.position.y - Mathf.Abs(bodyCube.lossyScale.y) * 0.5f;
            rightFrom = bodyCube.rotation; rightT = 0f; rightYaw = yaw;        // встаёт прямо плавно (RightingStep), не скачком
            bodyCube.rotation = Quaternion.Euler(0f, yaw, 0f);
            float ground = LevelHit(Body, low + 0.1f, 0.15f, out var mover, out var n);
            if (float.IsInfinity(ground)) ground = low;
            RestoreBodyRb();
            // легло вплотную к стенке наклонённым — выпрямленное вошло бы в уступ: отодвинуть ровно на перекрытие (рисунок — плавно)
            {
                Vector3 b0 = Body; float save = groundY, saveL = climbLift; groundY = ground; climbLift = 0f;
                Vector3 best = b0; bool clear = !HangOverlaps(b0);
                for (float r = 0.01f; !clear && r <= 0.2f; r += 0.01f)
                    for (int k = 0; k < 16 && !clear; k++)
                    {
                        Vector3 q = b0 + Quaternion.AngleAxis(k * 22.5f, Vector3.up) * Vector3.forward * r;
                        if (!HangOverlaps(q)) { best = q; clear = true; }
                    }
                groundY = save; climbLift = saveL;
                if ((best - b0).sqrMagnitude > 1e-8f) { rightOff = b0 - best; MoveBodyXZ(best.x, best.z); }
            }
            physStableSet = fallStartY - ground < 0.05f; physStableAt = Body;   // не упало — стоит устойчиво (не перепроверять на месте)
            Land(ground, mover, n);
            if (!falling && rightT < 1f) { bodyCube.rotation = rightFrom; bodyCube.position += rightOff; rightOffShown = rightOff; }   // в кадре посадки рисунок — как легло
        }

        /// <summary>Тело снова «логическое»: без гравитации, высоту ставит логика, вращение — только логикой.</summary>
        void RestoreBodyRb()
        {
            if (!bodyRb) return;
            bodyRb.useGravity = false;
            bodyRb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            bodyRb.linearVelocity = Vector3.zero; bodyRb.angularVelocity = Vector3.zero;
            bodyRb.maxDepenetrationVelocity = bodyDepenSpeed;
            bodyRb.maxAngularVelocity = 7f;
            if (slipMat != null && bodyBox) bodyBox.sharedMaterial = slipMat;
            physFallInit = false;
            ignoredColliders = -1;                                          // столкновения — снова только с высокими стенами
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
                    if (PhysFallOn) { bodyCube.rotation = Quaternion.Euler(0f, bodyCube.eulerAngles.y, 0f); RestoreBodyRb(); }   // из физики — снова логика
                    groundY = g; climbLift = Mathf.Max(0f, fallY - g); falling = false; catchCount++;
                    lastLanding = $"поймал «{hh.name}» в полёте ({fallStartY - fallY:F2} м)";
                    BeginGripHold(hh, hs);
                    return;
                }
            }
            hand = Flat(Shoulder + Face * 0.35f);                          // кисть перед плечом и в отступе, и в полёте (не через тело)
            if (PhysFallOn) return;                                         // ПАДЕНИЕ НА ФИЗИКЕ UNITY — полёт и посадку ведёт PhysFallStep
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
