using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Rig.V3
{
    /// <summary>Риг v3, NPC: ходит анимация (клипы Mixamo, root motion), капсула идёт за корнем клипа (HumanMotor.MoveRoot).
    /// Мозг выбирает клип под задачу — так нет скольжения ног и рывков:
    ///   стоит → цель сбоку/сзади: поворот на месте (90° / 180°, клип «доворачивается» до точного угла);
    ///   старт с места (клип Walk_Start без первых долей секунды) → ход (смесь Locomotion: шаг ↔ бег по VelZ, подруливание и обход встречных);
    ///   цель сзади на ходу — разворот 180° на ходу; цель ближе пути остановки — клип Walk_Stop, растянутый точно до точки
    ///   (с бега — сначала сбавить до шага); упёрся в тяжёлое — встал; в человека — шаг вбок.
    /// Скорость: walkSpeed / runSpeed в м/с — темп клипов подстраивается (шаг в клипе 1.51 м/с, бег 4.58 м/с).
    /// Цель: свои точки (HumanMotor.points / radius / goTo) или путь снаружи (SetPath: навмеш стражи — ближайший угол и остаток пути).
    /// Удар (HumanReaction → точка захвата → HumanMotor.Knocked): слабый — только отклик и качание поверх анимации;
    /// сильный — клип удара по направлению толчка, путь клипа повёрнут точно по толчку и масштабирован силой.
    /// Сон: лежит на кровати позой из клипа (Sleep), ничего не делает.
    /// Висит на модели (рядом с Animator), контроллер — Assets/Rig/Anim/HumanNPC.controller (RigHumanV3Builder).</summary>
    [DefaultExecutionOrder(-45)]
    [RequireComponent(typeof(Animator))]
    public class HumanNPC : MonoBehaviour
    {
        [Header("Скорость")]
        [Tooltip("Шаг (м/с). Клип шага — 1.51 м/с, темп подстраивается в пределах 0.75–1.45")] public float walkSpeed = 1.6f;
        [Tooltip("Бег (м/с). Клип бега — 4.58 м/с")] public float runSpeed = 4.5f;
        [Tooltip("Бежать (иначе — шагом)")] public bool run;
        [Tooltip("Разброс темпа между людьми (доля)")] public float tempoSpread = 0.06f;
        [Header("Ход")]
        [Tooltip("Подруливание на ходу (градусы/с)")] public float walkTurnRate = 110f;
        [Tooltip("Цель сбоку больше (градусы) — сначала повернуться на месте")] public float turnInPlace = 50f;
        [Tooltip("Цель сзади больше (градусы) на ходу — разворот на ходу")] public float turnOnTheWay = 140f;
        [Header("Удары")]
        [Tooltip("Скорость центра масс от удара (м/с), с которой играется клип удара")] public float hitThreshold = 0.45f;
        [Tooltip("Скорость центра масс, на которую рассчитан путь клипа удара (м/с)")] public float hitNominal = 0.9f;

        /// <summary>Куда смотреть, стоя на месте (пост стражи): повернётся клипом.</summary>
        [System.NonSerialized] public Vector3? face;

        // клипы (замер кривых корня, см. память проекта): длина, где начинается/кончается движение, поворот, путь
        const float WalkV = 1.51f, RunV = 4.58f;     // скорости Walk и Run (м/с)
        const float StartFrom = 0.35f, StartTo = 1.9f, StartSpeed = 1.3f;
        const float StopTravel = 1.13f, StopTo = 2.0f, StopSpeed = 1.15f;
        const float TurnSpeed = 1.2f;
        const float RunBrake = 3.2f;                 // сбавить с бега до шага — за столько метров до остановки

        public enum Mode { Idle, Turn, Start, Walk, Stop, Blocked, Sidestep, Hit, Sleep, SitDown, Sitting, StandUp }
        public Mode State => mode;
        public bool Busy => mode == Mode.Hit;
        public bool Running => velZ > WalkV + 0.5f;
        public string Say => mode switch
        {
            Mode.Idle => "стоит", Mode.Turn => "поворот", Mode.Start => "трогается", Mode.Walk => Running ? "бежит" : "идёт",
            Mode.Stop => "останавливается", Mode.Blocked => "упёрся", Mode.Sidestep => "уступает", Mode.Hit => "удар: " + hitClip,
            Mode.Sleep => "спит", Mode.SitDown => "садится", Mode.Sitting => "сидит", Mode.StandUp => "встаёт", _ => "",
        };

        Animator anim;
        HumanMotor motor;
        Mode mode = Mode.Idle;
        float modeT, modeLen, jitter = 1f, tempo = 1f;
        float turnWarp = 1f, stopWarp = 1f, hitScale = 1f;
        Quaternion hitTurn = Quaternion.identity;
        string hitClip = "";
        float velZ, velX;
        float blockedT, wait;
        int index;
        Vector3 goal;
        bool haveGoal;
        // путь снаружи (навмеш)
        bool ext;
        Vector3 extSteer, extFinal;
        float extRemaining;
        // сиденье: клип посадки Mixamo снят под стул ~0.47 м, корпус уезжает назад на 0.34 м
        const float ClipSeat = 0.47f, SitBack = 0.34f;
        bool sitRequested, standRequested;
        Vector3 seatPos, seatFace, sitFrom, standFrom;
        float sitW;
        Fortress.Rig.Human.HumanGhostIK ik;
        /// <summary>Сидит (или садится / встаёт) — с места не сдвинуть клипами ударов.</summary>
        public bool OnSeat => mode == Mode.SitDown || mode == Mode.Sitting || mode == Mode.StandUp;
        /// <summary>Сидит (посадка закончилась).</summary>
        public bool Seated => mode == Mode.Sitting;
        // сон
        bool sleepPlaced;
        int sleepFrames;
        Vector3 bedCenter, bedAlong;
        float bedTop;
        static readonly int VelXId = Animator.StringToHash("VelX"), VelZId = Animator.StringToHash("VelZ");

        static readonly string[] HitClips = { "Hit_Front", "Hit_Back", "Hit_Left", "Hit_Right" };
        readonly Dictionary<string, Vector3> hitPath = new Dictionary<string, Vector3>();

        void Awake()
        {
            anim = GetComponent<Animator>();
            motor = GetComponentInParent<HumanMotor>();
            ik = GetComponent<Fortress.Rig.Human.HumanGhostIK>();
            anim.applyRootMotion = true;
            // путь клипов ударов в осях тела (средняя скорость корня клипа)
            if (anim.runtimeAnimatorController)
                foreach (var c in anim.runtimeAnimatorController.animationClips)
                    if (System.Array.IndexOf(HitClips, c.name) >= 0)
                    {
                        Vector3 v = c.averageSpeed; v.y = 0f;
                        if (v.sqrMagnitude > 1e-4f) hitPath[c.name] = v.normalized;
                    }
            if (motor) { motor.rootMotion = true; motor.Knocked += OnKnock; }
        }

        void OnDestroy() { if (motor) motor.Knocked -= OnKnock; }

        void Start()
        {
            jitter = 1f + Random.Range(-tempoSpread, tempoSpread);     // люди ходят немного по-разному
            Pick();
        }

        // ---------------------------------------------------------------- цель
        /// <summary>Путь снаружи: к какой точке держать курс (угол пути), конечная точка и сколько осталось по пути (м).</summary>
        public void SetPath(Vector3 steer, Vector3 final, float remaining)
        {
            ext = true; extSteer = steer; extFinal = final; extRemaining = remaining;
        }

        /// <summary>Пути снаружи нет — стоять (или вернуться к своим точкам, если они заданы).</summary>
        public void ClearPath() { ext = false; }

        /// <summary>Лечь на кровать: центр верха матраса, длинная ось, высота верха.</summary>
        public void Sleep(Vector3 center, Vector3 along, float top)
        {
            bedCenter = center; along.y = 0f; bedAlong = along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.forward; bedTop = top;
            sleepPlaced = false; sleepFrames = 0;
            // стопы на «пол» и таз над ним — только стоя; лёжа IK поднимал тело над матрасом
            var ik = GetComponent<Fortress.Rig.Human.HumanGhostIK>();
            if (ik) ik.feet = false;
            Go(Mode.Sleep, "Sleep", 0f, 0f);
        }

        /// <summary>Сесть: верх сиденья (центр) и куда смотреть сидя. Стоять надо перед сиденьем спиной к нему (≈0.4 м) —
        /// повернётся сам, клип посадки донесёт таз точно на сиденье, тело поднимется/опустится под высоту сиденья.</summary>
        public void SitOn(Vector3 seat, Vector3 faceDir)
        {
            faceDir.y = 0f;
            seatPos = seat; seatFace = faceDir.sqrMagnitude > 1e-4f ? faceDir.normalized : motor.transform.forward;
            sitRequested = true; standRequested = false;
        }

        /// <summary>Встать с сиденья.</summary>
        public void StandUp() { standRequested = true; sitRequested = false; }

        /// <summary>Стоит на месте (дошёл, повернулся, ждёт).</summary>
        public bool Standing => mode == Mode.Idle;

        void Pick()
        {
            haveGoal = true;
            if (motor.points != null && motor.points.Length > 0) { goal = motor.points[index % motor.points.Length]; index++; }
            else if (motor.radius > 0f) { var r = Random.insideUnitCircle * motor.radius; goal = motor.center + new Vector3(r.x, 0f, r.y); }
            else haveGoal = false;
        }

        /// <summary>Куда держать курс (steer), сколько идти до конца (dist) и с какого расстояния «дошёл».</summary>
        bool Target(out Vector3 steer, out float dist, out float arrive)
        {
            Vector3 p = motor.transform.position;
            if (ext)
            {
                steer = extSteer; dist = extRemaining; arrive = 0.3f;
                return true;
            }
            arrive = motor.arrive;
            if (motor.goTo.HasValue) { steer = motor.goTo.Value; arrive = motor.goToArrive; }
            else steer = goal;
            Vector3 d = steer - p; d.y = 0f;
            dist = d.magnitude;
            return motor.goTo.HasValue || (!motor.idle && haveGoal);
        }

        // ---------------------------------------------------------------- решения
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !motor) return;
            if (mode == Mode.Sleep) { anim.speed = 1f; return; }
            if (OnSeat) { SeatUpdate(dt); return; }
            // темп: шаг — под walkSpeed, бег — под runSpeed, между — смесь
            float walkTempo = Mathf.Clamp(walkSpeed / WalkV, 0.75f, 1.45f) * jitter;
            float runTempo = Mathf.Clamp(runSpeed / RunV, 0.75f, 1.3f) * jitter;
            tempo = Mathf.Lerp(walkTempo, runTempo, Mathf.InverseLerp(WalkV, RunV, velZ));
            anim.speed = tempo;
            modeT += dt * tempo;
            bool has = Target(out var t, out float dist, out float arrive);
            Vector3 to = t - motor.transform.position; to.y = 0f;
            Vector3 fwd = motor.transform.forward; fwd.y = 0f;
            float ang = to.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(fwd, to, Vector3.up) : 0f;
            motor.Arrived = motor.goTo.HasValue && dist < motor.goToArrive;

            switch (mode)
            {
                case Mode.Idle:
                    velZ = Mathf.MoveTowards(velZ, 0f, 3f * dt);
                    if (sitRequested && modeT > 0.2f)
                    {
                        // спиной к сиденью: повернуться, потом сесть
                        float sa = Vector3.SignedAngle(fwd, seatFace, Vector3.up);
                        if (Mathf.Abs(sa) > 20f) BeginTurn(sa); else BeginSitDown();
                        break;
                    }
                    if (!has || dist <= arrive)
                    {
                        // дошёл: повернуться, куда велено смотреть
                        if (face.HasValue)
                        {
                            Vector3 f = face.Value; f.y = 0f;
                            float fa = f.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(fwd, f, Vector3.up) : 0f;
                            if (Mathf.Abs(fa) > 30f && modeT > 0.3f) { BeginTurn(fa); break; }
                        }
                        if (!has || motor.goTo.HasValue || ext) break;
                        wait += dt;
                        if (wait > motor.pause) { wait = 0f; Pick(); }
                        break;
                    }
                    if (Mathf.Abs(ang) > turnInPlace) BeginTurn(ang);
                    else if (dist > StopTravel * 0.9f) BeginStart();
                    else BeginWalk(0.6f);                     // близко — пара шагов без разгона
                    break;
                case Mode.Turn:
                    if (modeT >= modeLen)
                    {
                        if (has && dist > arrive) { if (dist > StopTravel * 0.9f) BeginStart(); else BeginWalk(0.6f); }
                        else Go(Mode.Idle, "Locomotion", 0.3f, 0f);
                    }
                    break;
                case Mode.Start:
                    if (!has || dist <= StopTravel) { BeginStop(dist); break; }
                    if (modeT >= modeLen) BeginWalk(WalkV);
                    break;
                case Mode.Walk:
                {
                    if (!has) { BeginStop(StopTravel); break; }
                    if (dist <= StopTravel * Mathf.Clamp(velZ / WalkV, 0.5f, 1f)) { BeginStop(dist); break; }
                    if (Mathf.Abs(ang) > turnOnTheWay && dist > 2f) { BeginWalkTurn(ang); break; }
                    // бег — пока до конца далеко; ближе — сбавить до шага, чтобы остановиться клипом
                    float vt = run && dist > StopTravel + RunBrake ? RunV : WalkV;
                    velZ = Mathf.MoveTowards(velZ, vt, (vt > velZ ? 2.5f : 3.5f) * dt);
                    // упёрся: в тяжёлое или стену — встать; в человека — шагнуть вбок (вправо) и идти дальше
                    blockedT = motor.Velocity.magnitude < 0.25f * velZ * tempo ? blockedT + dt : 0f;
                    if (blockedT > 0.35f)
                    {
                        blockedT = 0f;
                        if (motor.BlockedByHeavy) { Go(Mode.Blocked, "Locomotion", 0.25f, 0f); velZ = 0f; }
                        else { mode = Mode.Sidestep; modeT = 0f; }
                    }
                    break;
                }
                case Mode.Sidestep:
                    // смесь Locomotion: шаг вбок вправо (клип уводит чуть вперёд) — обойти встречного
                    velX = Mathf.MoveTowards(velX, 0.95f, 4f * dt);
                    velZ = Mathf.MoveTowards(velZ, 0.6f, 4f * dt);
                    if (modeT > 0.8f) { mode = Mode.Walk; modeT = 0f; }
                    break;
                case Mode.Stop:
                    if (modeT >= modeLen) Go(Mode.Idle, "Locomotion", 0.4f, 0f);
                    break;
                case Mode.Blocked:
                    velZ = 0f;
                    // цель сменилась (повернуть назад) или путь освободился — снова в путь
                    if (has && dist > arrive && (Mathf.Abs(ang) > turnInPlace || modeT > 2.5f)) mode = Mode.Idle;
                    break;
                case Mode.Hit:
                    if (modeT >= modeLen) Go(Mode.Idle, "Locomotion", 0.35f, 0f);
                    break;
            }
            if (mode != Mode.Sidestep) velX = Mathf.MoveTowards(velX, 0f, 4f * dt);
            anim.SetFloat(VelZId, velZ, 0.12f, dt);
            anim.SetFloat(VelXId, velX, 0.12f, dt);
        }

        void Go(Mode m, string state, float fade, float offset)
        {
            mode = m; modeT = 0f;
            if (fade <= 0f) anim.Play(state, 0, 0f);
            else anim.CrossFadeInFixedTime(state, fade, 0, offset);
        }

        void BeginSitDown()
        {
            sitRequested = false;
            sitFrom = motor.transform.position;
            motor.Controller.enabled = false;           // стул не должен выталкивать
            velZ = 0f; velX = 0f;
            Go(Mode.SitDown, "Sit_Down", 0.25f, 0f);
            modeLen = 2.1f;
        }

        /// <summary>Посадка, сидение, вставание: темп 1, подъём тела под высоту сиденья.</summary>
        void SeatUpdate(float dt)
        {
            anim.speed = 1f;
            modeT += dt;
            float lift = seatPos.y - motor.transform.position.y - ClipSeat;
            switch (mode)
            {
                case Mode.SitDown:
                    sitW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((modeT - 0.5f) / 1.1f));
                    if (modeT >= modeLen) Go(Mode.Sitting, "Sit_Idle", 0.35f, 0f);
                    break;
                case Mode.Sitting:
                    sitW = 1f;
                    if (standRequested) { standRequested = false; standFrom = motor.transform.position; Go(Mode.StandUp, "Sit_Up", 0.25f, 0f); modeLen = 2.05f; }
                    break;
                case Mode.StandUp:
                    sitW = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((modeT - 0.5f) / 1.0f));
                    if (modeT >= modeLen)
                    {
                        sitW = 0f;
                        // капсулу включить только там, где пусто (иначе выталкивало на табурет): шагнуть вперёд до свободного места
                        Vector3 p = motor.transform.position; p.y = sitFrom.y;
                        var cc = motor.Controller;
                        for (int i = 0; i < 12; i++)
                        {
                            Vector3 a = p + Vector3.up * (cc.radius + 0.05f), b = p + Vector3.up * (cc.height - cc.radius);
                            if (!Physics.CheckCapsule(a, b, cc.radius, RigLayers.WorldMask, QueryTriggerInteraction.Ignore)) break;
                            p += seatFace * 0.06f;
                        }
                        motor.Place(p, Quaternion.LookRotation(seatFace, Vector3.up));   // капсула снова на месте
                        Go(Mode.Idle, "Locomotion", 0.3f, 0f);
                    }
                    break;
            }
            if (ik) ik.bodyLift = lift * sitW;
            anim.SetFloat(VelZId, 0f);
            anim.SetFloat(VelXId, 0f);
        }

        void BeginTurn(float ang)
        {
            // 90° клипы: −90 / +101; 180° — −173 / +173 (зеркало)
            float clipAng; string st;
            if (Mathf.Abs(ang) > 125f) { st = ang < 0f ? "Turn_Left_180" : "Turn_Right_180"; clipAng = ang < 0f ? -173f : 173f; modeLen = 1.6f / TurnSpeed; }
            else { st = ang < 0f ? "Turn_Left_90" : "Turn_Right_90"; clipAng = ang < 0f ? -90f : 101f; modeLen = 0.9f / TurnSpeed; }
            turnWarp = Mathf.Clamp(ang / clipAng, 0.3f, 1.6f);
            Go(Mode.Turn, st, 0.2f, 0f);
        }

        void BeginStart()
        {
            Go(Mode.Start, "Walk_Start", 0.2f, StartFrom);
            modeLen = (StartTo - StartFrom) / StartSpeed;
            velZ = WalkV;
        }

        void BeginWalk(float v)
        {
            velZ = Mathf.Max(velZ, v);
            Go(Mode.Walk, "Locomotion", 0.35f, 0f);
        }

        void BeginWalkTurn(float ang)
        {
            // клип: +180 вправо за 1 с; влево — зеркало
            turnWarp = Mathf.Clamp(Mathf.Abs(ang) / 180f, 0.6f, 1.1f);
            Go(Mode.Turn, ang > 0f ? "Walk_Turn_180_R" : "Walk_Turn_180_L", 0.2f, 0f);
            modeLen = 0.95f;
        }

        void BeginStop(float remaining)
        {
            stopWarp = Mathf.Clamp(remaining / StopTravel, 0.3f, 1.5f);
            Go(Mode.Stop, "Walk_Stop", 0.25f, 0f);
            modeLen = StopTo / StopSpeed;
            velZ = 0f;
        }

        /// <summary>Сильный удар: клип по направлению толчка (в осях тела), путь — точно по толчку, длина — по силе.</summary>
        void OnKnock(Vector3 dv)
        {
            if (mode == Mode.Sleep || OnSeat) return;      // сидя — только отклик и качание, без клипа удара
            dv.y = 0f;
            float s = dv.magnitude;
            if (s < hitThreshold) return;
            // клип, чей путь (в осях тела, клипы Mixamo уводят косо) ближе всего к толчку; путь доворачивается точно по толчку
            Vector3 dir = dv / s, clipDir = -motor.transform.forward;
            float best = -2f;
            foreach (var n in HitClips)
            {
                if (!hitPath.TryGetValue(n, out var local)) continue;
                Vector3 w = motor.transform.rotation * local;
                float dot = Vector3.Dot(w, dir);
                if (dot > best) { best = dot; hitClip = n; clipDir = w; }
            }
            modeLen = hitClip == "Hit_Front" ? 1.15f : hitClip == "Hit_Back" ? 1.1f : 1.2f;
            hitTurn = Quaternion.FromToRotation(clipDir, dir);
            hitScale = Mathf.Clamp(s / hitNominal, 0.5f, 1.6f);
            blockedT = 0f;
            velZ = 0f;
            Go(Mode.Hit, hitClip, 0.08f, 0f);
        }

        // ---------------------------------------------------------------- ход: корень клипа → капсула
        void OnAnimatorMove()
        {
            if (!motor || mode == Mode.Sleep) return;
            float dt = Time.deltaTime;
            // корень клипа идёт в осях модели; модель может быть развёрнута внутри капсулы — перевести в оси капсулы
            Vector3 d = motor.transform.rotation * Quaternion.Inverse(transform.rotation) * anim.deltaPosition;
            float dy = anim.deltaRotation.eulerAngles.y;
            if (dy > 180f) dy -= 360f;
            switch (mode)
            {
                case Mode.Turn:
                    dy *= turnWarp;
                    break;
                case Mode.Start:
                case Mode.Walk:
                {
                    if (mode == Mode.Walk && velX > 0.3f) break;
                    if (Target(out var t, out _, out _))
                    {
                        Vector3 to = t - motor.transform.position; to.y = 0f;
                        if (to.sqrMagnitude > 0.01f)
                        {
                            Vector3 want = motor.SteerAround(to);
                            float a = Vector3.SignedAngle(motor.transform.forward, want, Vector3.up);
                            float lim = walkTurnRate * dt * Mathf.Max(1f, tempo);
                            dy += Mathf.Clamp(a * 4f * dt, -lim, lim);
                        }
                    }
                    break;
                }
                case Mode.Stop:
                    d *= stopWarp;
                    break;
                case Mode.Hit:
                    d = hitTurn * d * hitScale;
                    dy = 0f;
                    break;
                case Mode.SitDown:
                {
                    // клип уводит корпус назад на 0.34 м — растянуть путь так, чтобы корпус сел точно над сиденьем
                    Vector3 target = seatPos + seatFace * 0.05f; target.y = sitFrom.y;
                    float back = Mathf.Max(0f, Vector3.Dot(d, -motor.transform.forward));
                    Vector3 path = target - sitFrom; path.y = 0f;
                    d = path * (back / SitBack);
                    dy = 0f;
                    break;
                }
                case Mode.Sitting:
                    d = Vector3.zero; dy = 0f;
                    break;
                case Mode.StandUp:
                {
                    // клип уводит корпус вперёд на 0.34 м — растянуть путь до места, откуда садился (оно свободно)
                    float fwdMove = Mathf.Max(0f, Vector3.Dot(d, motor.transform.forward));
                    Vector3 path = sitFrom - standFrom; path.y = 0f;
                    d = path * (fwdMove / SitBack);
                    dy = 0f;
                    break;
                }
                case Mode.Idle:
                case Mode.Blocked:
                    d *= 0.3f;     // переступания стоя — почти на месте
                    break;
            }
            motor.MoveRoot(d, dy, dt);
        }

        // ---------------------------------------------------------------- сон: уложить позу на кровать
        void LateUpdate()
        {
            if (mode != Mode.Sleep || sleepPlaced || !motor) return;
            // поза клипа появляется через кадр — тогда замерить, где голова и таз, и подогнать корень так,
            // чтобы тело легло вдоль кровати, по её середине, на матрас
            if (++sleepFrames < 3) return;
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var hl = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var hr = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (!head || !hl || !hr) { sleepPlaced = true; return; }
            Vector3 pelvis = (hl.position + hr.position) * 0.5f;
            Vector3 axis = head.position - pelvis; axis.y = 0f;
            if (sleepFrames == 3)
            {
                // голова — к подушке (у кроватей набора она на конце против длинной оси)
                float a = Vector3.SignedAngle(axis, -bedAlong, Vector3.up);
                motor.Place(motor.transform.position, Quaternion.Euler(0f, a, 0f) * motor.transform.rotation);
                return;
            }
            if (sleepFrames == 5)
            {
                // клип снят на наклонной кровати (стопы на ~0.2 м выше головы): положить тело ровно — повернуть вокруг головы
                var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                var rf = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                if (lf && rf)
                {
                    Vector3 a = (lf.position + rf.position) * 0.5f - head.position;
                    Vector3 flat = new Vector3(a.x, 0f, a.z);
                    if (flat.sqrMagnitude > 0.01f)
                    {
                        Quaternion q = Quaternion.FromToRotation(a, flat.normalized * a.magnitude);
                        Vector3 piv = head.position;
                        motor.Place(piv + q * (motor.transform.position - piv), q * motor.transform.rotation);
                    }
                }
                return;
            }
            if (sleepFrames < 8) return;
            // верх матраса — луч вниз над серединой кровати (верх каркаса выше матраса ~0.3 м)
            float mattress = bedTop - 0.29f, best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(new Vector3(bedCenter.x, bedTop + 0.3f, bedCenter.z), Vector3.down, 2f, RigLayers.WorldMask, QueryTriggerInteraction.Ignore))
                if (h.point.y > best) best = h.point.y;
            if (!float.IsNegativeInfinity(best)) mattress = best;
            // спина на матрас: кость груди и тазобедренные суставы лежащего — ~0.12 м над спиной; голова — в 0.74 м от середины тела к подушке
            var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            float backY = Mathf.Min(pelvis.y, chest ? chest.position.y : pelvis.y) - 0.12f;
            Vector3 headAt = bedCenter - bedAlong * 0.74f;
            // −0.04: верх коллайдера кровати чуть выше видимого одеяла, а матрас мягкий — тело чуть утопает
            Vector3 shift = new Vector3(headAt.x - head.position.x, mattress - 0.04f - backY, headAt.z - head.position.z);
            motor.Place(motor.transform.position + shift, motor.transform.rotation);
            motor.Controller.enabled = false;      // лежит — капсула не мешает
            sleepPlaced = true;
        }
    }
}
