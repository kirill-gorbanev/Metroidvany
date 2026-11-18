using UnityEngine;

namespace Fortress.Rig.Human
{
    /// <summary>Ноги «призрака»: невидимый аниматор ходит по полу (CharacterController), проигрывает шаг по скорости.
    /// Видимое физическое тело тянется за ним мышцами. Воля: точки обхода, свободное брожение, «иди туда» (goTo) или игрок (WASD).
    /// Когда тело шатается, HumanPuppet перехватывает скорость — призрак подшагивает под центр масс.
    /// На лестнице вниз призрак «прилипает» к следующей ступени, а не слетает с неё.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class HumanDriver : MonoBehaviour, IHumanMotion
    {
        public Transform Root => transform;
        public Animator anim;
        [Tooltip("Шаг — родная скорость анимации шага (~0.95 м/с): быстрее — ноги «проскальзывают»")] public float walkSpeed = 1.0f;
        public float runSpeed = 3.2f;
        public float turnRate = 360f;
        public Vector3[] points;
        public float radius = 0f;
        public Vector3 center;
        public float arrive = 0.4f, pause = 1.2f;
        public bool idle;
        [Tooltip("Насколько вниз «прилипать» к опоре при ходьбе (ступень вниз)")] public float stepDown = 0.42f;
        [System.NonSerialized] public bool possessed;
        [System.NonSerialized] public Vector3 possessVelocity;
        /// <summary>Перехват скорости (шатание, толкание): пока задан, воля молчит.</summary>
        [System.NonSerialized] public Vector3? overrideVelocity;
        [System.NonSerialized] public bool frozen;
        /// <summary>Подойти к точке (выше обхода и брожения). Снимается, когда дошёл (Arrived).</summary>
        [System.NonSerialized] public Vector3? goTo;
        [System.NonSerialized] public float goToArrive = 0.12f;
        /// <summary>Куда повернуться лицом, стоя на месте (берёт ящик, садится спиной к лавке).</summary>
        [System.NonSerialized] public Vector3? face;
        /// <summary>Множитель скорости (тяжёлая ноша — медленнее).</summary>
        [System.NonSerialized] public float speedScale = 1f;
        /// <summary>Не поворачиваться по ходу (толкает ящик, пятится).</summary>
        [System.NonSerialized] public bool keepFacing;
        /// <summary>Стоять на месте (руки заняты делом: берёт, ставит, сидит) — обход и брожение ждут.</summary>
        [System.NonSerialized] public bool hold;

        public Vector3 Velocity { get; private set; }
        public bool Grounded { get; private set; }
        public bool Arrived { get; private set; }
        public bool Facing { get; private set; }
        CharacterController cc;
        int index;
        float wait, vy;
        Vector3 goal;
        static readonly int SpeedId = Animator.StringToHash("Speed");

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            gameObject.layer = RigLayers.Ghost;   // призрак проходит сквозь подвижное, сталкивается только с миром
            if (!anim) anim = GetComponentInChildren<Animator>();
            if (anim && !anim.GetComponent<HumanGhostIK>()) anim.gameObject.AddComponent<HumanGhostIK>();
        }

        void Start() => Pick();

        void Pick()
        {
            if (points != null && points.Length > 0) { goal = points[index % points.Length]; index++; }
            else if (radius > 0f) { var r = Random.insideUnitCircle * radius; goal = center + new Vector3(r.x, 0f, r.y); }
            else goal = transform.position;
        }

        /// <summary>Сдвинуть призрака без столкновений (подсесть к лавке вплотную); пока frozen — так и стоит.</summary>
        public void Nudge(Vector3 pos)
        {
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
        }

        /// <summary>Поставить призрака в точку (подъём с земли, телепорт).</summary>
        public void Place(Vector3 pos, Quaternion rot)
        {
            cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            cc.enabled = true;
            Velocity = Vector3.zero; vy = 0f;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 want = Vector3.zero;
            Arrived = false;
            if (frozen) want = Vector3.zero;
            else if (overrideVelocity.HasValue) want = overrideVelocity.Value;
            else if (goTo.HasValue)
            {
                Vector3 d = goTo.Value - transform.position; d.y = 0f;
                if (d.magnitude < goToArrive) Arrived = true;
                else want = d.normalized * walkSpeed * speedScale * Mathf.Clamp(d.magnitude / 0.6f + 0.15f, 0.2f, 1f);
            }
            else if (hold) want = Vector3.zero;
            else if (possessed) want = possessVelocity * speedScale;
            else if (!idle)
            {
                Vector3 d = goal - transform.position; d.y = 0f;
                if (d.magnitude < arrive)
                {
                    wait += dt;
                    if (wait > pause) { wait = 0f; Pick(); }
                }
                else want = d.normalized * walkSpeed * speedScale * Mathf.Clamp01(d.magnitude / 0.8f + 0.3f);
            }
            want.y = 0f;
            Velocity = Vector3.MoveTowards(Velocity, want, 6f * dt);
            if (!frozen)
            {
                bool was = Grounded;
                vy = cc.isGrounded ? -1f : vy + Physics.gravity.y * dt;
                cc.Move((Velocity + Vector3.up * vy) * dt);
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
                Facing = true;
                Vector3 flatV = new Vector3(Velocity.x, 0f, Velocity.z);
                if (face.HasValue && (flatV.sqrMagnitude < 0.09f || keepFacing))
                {
                    Vector3 f = face.Value - transform.position; f.y = 0f;
                    if (f.sqrMagnitude > 1e-4f)
                    {
                        var look = Quaternion.LookRotation(f, Vector3.up);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnRate * 0.6f * dt);
                        Facing = Quaternion.Angle(transform.rotation, look) < 6f;
                    }
                }
                else if (flatV.sqrMagnitude > 0.04f && !overrideVelocity.HasValue && !keepFacing)
                {
                    var look = Quaternion.LookRotation(flatV, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnRate * dt);
                }
            }
            if (anim)
            {
                // шаг в сторону (шатание) — тоже шаг: в аниматоре скорость без направления
                float sp = new Vector3(Velocity.x, 0f, Velocity.z).magnitude;
                anim.SetFloat(SpeedId, sp, 0.1f, dt);
            }
        }
    }
}
