using System.Collections.Generic;
using Fortress.Rig.Human;
using UnityEngine;

namespace Fortress.Rig.V3
{
    /// <summary>Риг v3, слой L0: капсула движения человека — главная. Ввод (игрок) или воля (ИИ) сразу дают скорость и поворот:
    /// разгон ~25 м/с², поворот капсулы мгновенный. Анимация (HumanAnimate) и отклик (HumanReaction) подстраиваются под неё.
    /// Ступень вверх — stepOffset CharacterController, вниз — «прилипание». Тяжёлые предметы блокируют ход (Block),
    /// удар добавляет к скорости затухающее отбрасывание (AddKnock) — ввод игрока при этом не блокируется.
    /// Порядок кадра: HumanMotor (Update) → HumanAnimate → Animator → OnAnimatorIK → LateUpdate: наклоны → отклик.</summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(CharacterController))]
    public class HumanMotor : MonoBehaviour, IHumanMotion
    {
        [Header("Ход")]
        [Tooltip("Шаг (м/с): смесь анимаций берёт темп из фактической скорости")] public float walkSpeed = 1.25f;
        [Tooltip("Бег (м/с), Shift")] public float runSpeed = 3.4f;
        [Tooltip("Разгон и торможение (м/с²): 25 — ощущается мгновенным")] public float accel = 25f;
        [Tooltip("Насколько вниз «прилипать» к опоре (ступень вниз), м")] public float stepDown = 0.42f;
        [Header("Удары")]
        [Tooltip("Затухание отбрасывания (с): 1/ω точки захвата ≈ 0.32 — сдвиг ровно до точки захвата; больше — шаги шире")] public float knockDecay = 0.45f;
        [Tooltip("NPC расходятся со встречными (сила увода вбок)")] public float avoid = 1.0f;
        [Tooltip("Предел отбрасывания (м/с)")] public float maxKnock = 2.6f;
        [Tooltip("Предмет тяжелее (кг) — не сдвинуть телом: ход в его сторону убирается")] public float heavyMass = 60f;
        [Header("Воля (ИИ)")]
        public Vector3[] points;
        public float radius = 0f;
        public Vector3 center;
        public float arrive = 0.35f, pause = 1.0f;
        public bool idle;
        [Tooltip("NPC: ход ведёт анимация (root motion, HumanNPC), капсула идёт за корнем клипа")] public bool rootMotion;

        /// <summary>Удар в режиме root motion: скорость центра масс, которую надо «отработать» клипом (HumanNPC).</summary>
        public event System.Action<Vector3> Knocked;

        /// <summary>Ведёт игрок: желаемая скорость (мир, м/с) — из клавиш.</summary>
        [System.NonSerialized] public bool possessed;
        [System.NonSerialized] public Vector3 input;
        /// <summary>Подойти к точке (выше обхода).</summary>
        [System.NonSerialized] public Vector3? goTo;
        [System.NonSerialized] public float goToArrive = 0.15f;
        /// <summary>Множитель скорости воли.</summary>
        [System.NonSerialized] public float speedScale = 1f;

        public Transform Root => transform;
        /// <summary>Фактическая горизонтальная скорость капсулы (после стен и ступеней).</summary>
        public Vector3 Velocity { get; private set; }
        /// <summary>Желаемая скорость (ввод или воля), уже с разгоном.</summary>
        public Vector3 Intent { get; private set; }
        /// <summary>Отбрасывание от ударов (затухает).</summary>
        public Vector3 Knock => knock;
        public bool Grounded { get; private set; }
        /// <summary>Упирается в тяжёлый предмет (последние 0.12 с).</summary>
        public bool BlockedByHeavy
        {
            get
            {
                foreach (var b in blocks) if (Time.time - b.w <= 0.12f) return true;
                return false;
            }
        }
        /// <summary>Дошёл до goTo (у NPC с root motion ставит HumanNPC).</summary>
        public bool Arrived { get; set; }
        /// <summary>NPC ловит равновесие: воля молчит, пока отбрасывание не затухло.</summary>
        public bool Recovering => recoverT > 0f;
        public CharacterController Controller => cc;

        CharacterController cc;
        Vector3 vel, knock;
        float vy, wait, recoverT;
        int index;
        Vector3 goal;
        readonly List<Vector4> blocks = new List<Vector4>();     // xyz — нормаль к тяжёлому предмету, w — время

        static readonly List<HumanMotor> All = new List<HumanMotor>();
        float blockedT, sidestepT;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            gameObject.layer = RigLayers.Mover;
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>Направление обхода встречных впереди (лоб в лоб — оба вправо), без «упёрся — шаг вправо».</summary>
        public Vector3 SteerAround(Vector3 dir)
        {
            dir.y = 0f;
            if (avoid <= 0f || dir.sqrMagnitude < 1e-4f) return dir;
            dir.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, dir), steer = Vector3.zero;
            foreach (var o in All)
            {
                if (o == this) continue;
                Vector3 d = o.transform.position - transform.position; d.y = 0f;
                float dist = d.magnitude;
                if (dist > 2.0f || dist < 1e-3f || Vector3.Dot(d, dir) < -0.1f) continue;
                float side = Vector3.Dot(d, right), lat = Mathf.Abs(side);
                if (lat > 0.65f) continue;
                float k = (2.0f - dist) / 2.0f * (0.65f - lat) / 0.65f;
                steer += right * ((side > 0.05f ? -1f : 1f) * k * avoid);
            }
            Vector3 w = dir + steer; w.y = 0f;
            return w.sqrMagnitude > 1e-6f ? w.normalized : dir;
        }

        /// <summary>NPC: увести ход вбок от встречного впереди (лоб в лоб — оба вправо); упёрся в капсулу — шаг вправо.
        /// Расходятся близко — плечами задевают (это и нужно).</summary>
        Vector3 Avoid(Vector3 want, float dt)
        {
            if (possessed || avoid <= 0f || want.sqrMagnitude < 0.01f) return want;
            Vector3 dir = want.normalized, right = Vector3.Cross(Vector3.up, dir);
            Vector3 steer = Vector3.zero;
            foreach (var o in All)
            {
                if (o == this) continue;
                Vector3 d = o.transform.position - transform.position; d.y = 0f;
                float dist = d.magnitude;
                if (dist > 1.6f || dist < 1e-3f) continue;
                if (Vector3.Dot(d, dir) < -0.1f) continue;
                float side = Vector3.Dot(d, right), lat = Mathf.Abs(side);
                if (lat > 0.6f) continue;
                float k = (1.6f - dist) / 1.6f * (0.6f - lat) / 0.6f;
                steer += right * ((side > 0.05f ? -1f : 1f) * k * avoid);
            }
            // упёрся в капсулу (хочет идти, а стоит; не тяжёлый предмет — у того своя «стена») — шаг вправо
            if (blocks.Count == 0 && Velocity.magnitude < 0.3f * want.magnitude && vel.magnitude > 0.3f) blockedT += dt; else blockedT = 0f;
            if (blockedT > 0.35f) { sidestepT = 0.7f; blockedT = 0f; }
            if (sidestepT > 0f) { sidestepT -= dt; steer += right * 1.6f; }
            if (steer.sqrMagnitude < 1e-6f) return want;
            Vector3 w = dir + steer; w.y = 0f;
            return w.normalized * want.magnitude;
        }

        void Start() => Pick();

        void Pick()
        {
            if (points != null && points.Length > 0) { goal = points[index % points.Length]; index++; }
            else if (radius > 0f) { var r = Random.insideUnitCircle * radius; goal = center + new Vector3(r.x, 0f, r.y); }
            else goal = transform.position;
        }

        /// <summary>Удар: добавить отбрасывание (горизонтальная скорость, м/с).</summary>
        public void AddKnock(Vector3 dv)
        {
            dv.y = 0f;
            if (rootMotion) { Knocked?.Invoke(dv); return; }
            knock = Vector3.ClampMagnitude(knock + dv, maxKnock);
            if (!possessed && knock.magnitude > 0.3f) recoverT = Mathf.Max(recoverT, 0.35f);
        }

        /// <summary>Упёрся в тяжёлое: убрать ход в сторону n (горизонталь, от человека к предмету) на ближайшие 0.12 с.</summary>
        public void Block(Vector3 n)
        {
            n.y = 0f;
            if (n.sqrMagnitude < 1e-4f) return;
            n.Normalize();
            blocks.Add(new Vector4(n.x, n.y, n.z, Time.time));
        }

        /// <summary>Поставить в точку (телепорт).</summary>
        public void Place(Vector3 pos, Quaternion rot)
        {
            cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            cc.enabled = true;
            vel = knock = Velocity = Vector3.zero; vy = 0f;
        }

        Vector3 Want()
        {
            Arrived = false;
            if (possessed) return input * speedScale;
            if (recoverT > 0f) return Vector3.zero;
            if (goTo.HasValue)
            {
                Vector3 d = goTo.Value - transform.position; d.y = 0f;
                if (d.magnitude < goToArrive) { Arrived = true; return Vector3.zero; }
                return d.normalized * walkSpeed * speedScale * Mathf.Clamp(d.magnitude / 0.5f + 0.2f, 0.25f, 1f);
            }
            if (idle) return Vector3.zero;
            Vector3 g = goal - transform.position; g.y = 0f;
            if (g.magnitude < arrive)
            {
                wait += Time.deltaTime;
                if (wait > pause) { wait = 0f; Pick(); }
                return Vector3.zero;
            }
            return g.normalized * walkSpeed * speedScale * Mathf.Clamp01(g.magnitude / 0.6f + 0.35f);
        }

        /// <summary>NPC (root motion): сдвиг корня клипа за кадр (мир, горизонталь) и поворот вокруг вертикали.
        /// Тяжёлое на пути — сдвиг в его сторону убирается; гравитация, ступени и «прилипание» — как у ввода.</summary>
        public void MoveRoot(Vector3 delta, float yawDeg, float dt)
        {
            if (dt <= 0f) return;
            if (Mathf.Abs(yawDeg) > 1e-4f) transform.rotation = Quaternion.Euler(0f, yawDeg, 0f) * transform.rotation;
            delta.y = 0f;
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                var b = blocks[i];
                if (Time.time - b.w > 0.12f) { blocks.RemoveAt(i); continue; }
                Vector3 n = new Vector3(b.x, b.y, b.z);
                float into = Vector3.Dot(delta, n);
                if (into > 0f) delta -= n * into;
            }
            Move(delta, dt);
            Intent = Velocity;
        }

        /// <summary>Сдвинуть капсулу по горизонтали на delta (м) с гравитацией, ступенями и прилипанием вниз.</summary>
        void Move(Vector3 delta, float dt)
        {
            Vector3 p0 = transform.position;
            // капсула выключена (садится на стул, лежит) — двигать без столкновений и гравитации
            if (!cc.enabled)
            {
                delta.y = 0f;
                transform.position = p0 + delta;
                Velocity = delta / dt;
                return;
            }
            bool was = Grounded;
            vy = cc.isGrounded ? -1f : vy + Physics.gravity.y * dt;
            cc.Move(delta + Vector3.up * (vy * dt));
            Grounded = cc.isGrounded;
            // ступенька вниз: была опора и пропала, а под ногами в пределах шага есть пол — встать на него
            if (was && !Grounded && vy <= 0f)
            {
                Vector3 o = transform.position + Vector3.up * (cc.center.y - cc.height * 0.5f + cc.radius);
                if (Physics.SphereCast(o, cc.radius * 0.9f, Vector3.down, out _, stepDown + cc.radius * 0.2f, RigLayers.WorldMask, QueryTriggerInteraction.Ignore))
                {
                    cc.Move(Vector3.down * stepDown);
                    Grounded = cc.isGrounded;
                    vy = -1f;
                }
            }
            Vector3 d = transform.position - p0; d.y = 0f;
            Velocity = d / dt;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (rootMotion) return;      // ход — из OnAnimatorMove (HumanNPC → MoveRoot)
            // отбрасывание затухает; NPC молчит, пока ловит равновесие (+ мгновение после)
            if (knock.magnitude > 0.15f) { if (!possessed) recoverT = Mathf.Max(recoverT, 0.25f); }
            else recoverT = Mathf.Max(0f, recoverT - dt);
            Vector3 want = Want();
            want.y = 0f;
            want = Avoid(want, dt);
            vel = Vector3.MoveTowards(vel, want, accel * dt);
            // поворот капсулы — сразу туда, куда хочет идти
            if (want.sqrMagnitude > 0.0025f) transform.rotation = Quaternion.LookRotation(want, Vector3.up);
            Intent = vel;

            Vector3 total = vel + knock;
            knock *= Mathf.Exp(-dt / Mathf.Max(0.05f, knockDecay));
            // тяжёлые предметы: ход в их сторону убрать
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                var b = blocks[i];
                if (Time.time - b.w > 0.12f) { blocks.RemoveAt(i); continue; }
                Vector3 n = new Vector3(b.x, b.y, b.z);
                float into = Vector3.Dot(total, n);
                if (into > 0f) total -= n * into;
                into = Vector3.Dot(vel, n);
                if (into > 0f) vel -= n * into;
                into = Vector3.Dot(knock, n);
                if (into > 0f) knock -= n * into;
            }
            Move(total * dt, dt);
        }
    }
}
