using Fortress.Rig.Human;
using Fortress.Rig.React;
using UnityEngine;

namespace Fortress.Rig.V3
{
    /// <summary>Риг v3, слой L0–L1 на видимой модели: анимация догоняет капсулу (HumanMotor).
    /// • Темп шага — из фактической скорости капсулы (смесь Move: Idle 0, Walk 0.95, Jog 3.2, Sprint 5.5).
    /// • Корпус догоняет поворот капсулы пружиной второго порядка (f, ζ, r).
    /// • Корпус наклоняется в разгон и в поворот (ускорение из того же фильтра скорости).
    /// • Стопы на ступенях и таз — HumanGhostIK (общий для v2 и v3).
    /// • Отбросило — подшагивание: стопы ставятся сами (цели поверх анимации) туда, куда уезжает капсула, — в любую сторону.</summary>
    [DefaultExecutionOrder(-40)]
    [RequireComponent(typeof(Animator))]
    public class HumanAnimate : MonoBehaviour
    {
        [Tooltip("NPC: ход, поворот и шаги ведут клипы (HumanNPC) — здесь ничего не подстраивается")] public bool npcMode;
        [Header("Поворот корпуса (пружина второго порядка)")]
        public float turnF = 2.4f;
        public float turnZeta = 0.8f;
        public float turnR = 0f;
        [Header("Наклон в разгон и в поворот")]
        [Tooltip("Доля угла atan(a/g) вперёд-назад (разгон, торможение)")] public float leanGain = 0.25f;
        [Tooltip("Доля угла вбок (в поворот)")] public float leanRollGain = 0.12f;
        [Tooltip("Предел наклона вперёд-назад (градусы)")] public float maxLean = 12f;
        [Tooltip("Предел наклона вбок (градусы)")] public float maxRoll = 7f;
        public float leanF = 1.6f, leanZeta = 0.7f;
        [Header("Подшагивание (отбросило)")]
        [Tooltip("Отбрасывание сильнее (м/с) — стопы ставятся сами")] public float stepOn = 0.22f;
        [Tooltip("Стопа дальше цели (м) — шагнуть")] public float stepTrigger = 0.14f;
        [Tooltip("Вынос стопы по ходу отбрасывания (с·м/с)")] public float stepLead = 0.3f;
        [Tooltip("Высота переноса стопы (м)")] public float stepArc = 0.07f;
        [Tooltip("Полуширина стойки (м)")] public float stance = 0.11f;

        Animator anim;
        HumanMotor motor;
        HumanGhostIK ik;
        Transform spine;
        SecondOrderAngle yaw;
        SecondOrder velF;
        bool init;
        static readonly int SpeedId = Animator.StringToHash("Speed");

        // наклон (градусы): вперёд и вправо
        public float LeanPitch { get; private set; }
        public float LeanRoll { get; private set; }
        /// <summary>Направление тела (куда смотрит видимая модель), горизонталь.</summary>
        public Vector3 BodyForward => npcMode && motor ? motor.transform.forward : Quaternion.Euler(0f, yaw.y, 0f) * Vector3.forward;
        public float StepWeight => stepW;
        public bool Stepping => stepW > 0.5f;
        /// <summary>Какая стопа сейчас переносится при подшагивании (−1 — обе стоят).</summary>
        public int SwingFoot => swing;
        public int Steps { get; private set; }
        public float AnimSpeed { get; private set; }

        // подшагивание
        float stepW, procHold;
        readonly Vector3[] plant = new Vector3[2];
        int swing = -1, lastSwing = -1;
        float swingT, swingDur;
        Vector3 swingFrom;

        void Awake()
        {
            anim = GetComponent<Animator>();
            motor = GetComponentInParent<HumanMotor>();
            ik = GetComponent<HumanGhostIK>();
            spine = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Spine) : null;
        }

        void Init()
        {
            init = true;
            float y0 = motor ? motor.transform.eulerAngles.y : transform.eulerAngles.y + 180f;
            yaw.Init(turnF, turnZeta, turnR, y0);
            velF.Init(leanF, leanZeta, 0f, Vector3.zero);
        }

        void Update()
        {
            if (!motor) return;
            if (npcMode) { stepW = 0f; if (ik) ik.overrideW = 0f; return; }
            if (!init) Init();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            yaw.Tune(turnF, turnZeta, turnR);
            velF.Tune(leanF, leanZeta, 0f);

            // корпус догоняет поворот капсулы (модель Quaternius смотрит в −Z: +180°)
            float y = yaw.Update(dt, motor.transform.eulerAngles.y);
            transform.rotation = Quaternion.Euler(0f, y + 180f, 0f);

            // наклон: ускорение капсулы в осях тела
            velF.Update(dt, motor.Velocity);
            Vector3 a = velF.yd;
            Vector3 f = BodyForward, r = Vector3.Cross(Vector3.up, f);
            float g = -Physics.gravity.y;
            LeanPitch = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(a, f), g) * Mathf.Rad2Deg * leanGain, -maxLean, maxLean);
            LeanRoll = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(a, r), g) * Mathf.Rad2Deg * leanRollGain, -maxRoll, maxRoll);

            Stagger(dt, f, r);

            // темп шага — фактическая скорость капсулы без отбрасывания (его «шагают» стопы сами)
            Vector3 v = motor.Velocity - motor.Knock; v.y = 0f;
            float sp = v.magnitude * (1f - stepW);
            AnimSpeed = sp;
            anim.SetFloat(SpeedId, sp, 0.04f, dt);
            anim.speed = sp > 5.5f ? sp / 5.5f : 1f;
        }

        /// <summary>Подшагивание: пока капсулу уносит отбрасывание, стопы стоят на полу и переставляются к цели под капсулой
        /// (с выносом по ходу — ловит равновесие). Потом стопы плавно возвращаются анимации.</summary>
        void Stagger(float dt, Vector3 f, Vector3 r)
        {
            if (!ik) return;
            Vector3 knock = motor.Knock;
            bool want = knock.magnitude > stepOn && knock.magnitude > 0.7f * motor.Intent.magnitude;
            if (want) procHold = 0.3f; else procHold -= dt;
            bool active = want || procHold > 0f || swing >= 0;
            float before = stepW;
            stepW = Mathf.MoveTowards(stepW, active ? 1f : 0f, dt / (active ? 0.08f : 0.25f));
            if (stepW <= 0f) { ik.overrideW = 0f; swing = -1; return; }
            Vector3 root = motor.transform.position;
            if (before <= 0f)
            {
                // старт: стопы там, где их поставила анимация
                for (int i = 0; i < 2; i++)
                    plant[i] = ik.AnimFoot[i].sqrMagnitude > 0.01f ? ik.AnimFoot[i] : Ground(root + r * (i == 0 ? -stance : stance));
                swing = -1;
            }
            Vector3 lead = knock * stepLead + motor.Intent * 0.1f;
            var target = new Vector3[2];
            // цель — туда, где стопу держит анимация под уехавшей капсулой (+ вынос по ходу): после шагов возврат к анимации без скольжения
            for (int i = 0; i < 2; i++)
            {
                Vector3 anim0 = ik.AnimFoot[i];
                Vector3 baseP = anim0.sqrMagnitude > 0.01f && Flat(anim0 - root) < 0.5f ? anim0 : root + r * (i == 0 ? -stance : stance);
                target[i] = Ground(baseP + lead);
            }
            if (swing < 0 && stepW > 0.5f && (want || procHold > 0f))
            {
                float e0 = Flat(plant[0] - target[0]), e1 = Flat(plant[1] - target[1]);
                // шагает нога, что ближе к стороне отбрасывания (толкнули назад — первой уходит задняя), и та, что дальше от цели
                Vector3 kd = knock.sqrMagnitude > 1e-4f ? knock.normalized : Vector3.zero;
                float s0 = e0 + 0.6f * Vector3.Dot(plant[0] - root, kd), s1 = e1 + 0.6f * Vector3.Dot(plant[1] - root, kd);
                int i = s0 >= s1 ? 0 : 1;
                float e = Mathf.Max(e0, e1);
                // не той же ногой дважды подряд, если другой тоже есть куда шагнуть
                if (i == lastSwing && (i == 0 ? e1 : e0) > stepTrigger * 0.7f) i = 1 - i;
                if (e > stepTrigger)
                {
                    swing = i; swingT = 0f; swingFrom = plant[i];
                    swingDur = Mathf.Clamp(0.3f - 0.05f * knock.magnitude, 0.17f, 0.3f);
                }
            }
            var pos = new Vector3[2] { plant[0], plant[1] };
            if (swing >= 0)
            {
                swingT += dt / swingDur;
                float t = Mathf.Clamp01(swingT);
                float s = t * t * (3f - 2f * t);
                Vector3 to = target[swing];
                Vector3 p = Vector3.Lerp(swingFrom, to, s);
                p.y += (stepArc + Mathf.Max(0f, to.y - swingFrom.y)) * Mathf.Sin(Mathf.PI * t);
                pos[swing] = p;
                if (swingT >= 1f) { plant[swing] = to; lastSwing = swing; swing = -1; Steps++; }
            }
            ik.overrideW = stepW;
            ik.overridePos[0] = pos[0];
            ik.overridePos[1] = pos[1];
        }

        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

        Vector3 Ground(Vector3 p)
        {
            float fb = ik ? ik.FootBottom : 0.08f;
            float baseY = motor ? motor.transform.position.y : p.y;
            if (Physics.Raycast(new Vector3(p.x, baseY + 0.8f, p.z), Vector3.down, out var h, 1.6f, RigLayers.WorldMask, QueryTriggerInteraction.Ignore))
                return new Vector3(p.x, h.point.y + fb, p.z);
            return new Vector3(p.x, baseY + fb, p.z);
        }

        void LateUpdate()
        {
            if (!spine || !motor || npcMode) return;
            // наклон корпуса над тазом: вперёд/назад — разгон, вбок — поворот
            Vector3 f = BodyForward, r = Vector3.Cross(Vector3.up, f);
            if (Mathf.Abs(LeanPitch) + Mathf.Abs(LeanRoll) > 0.05f)
            {
                Quaternion q = Quaternion.AngleAxis(LeanPitch, r) * Quaternion.AngleAxis(-LeanRoll, f);
                spine.rotation = q * spine.rotation;
            }
        }
    }
}
