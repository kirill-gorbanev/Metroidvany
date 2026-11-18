using Fortress.Rig.Human;
using Fortress.Walker;
using UnityEngine;

namespace Fortress.Rig.V3
{
    /// <summary>Тело игрока в замке: человек (клипы Mixamo, смесь Locomotion) поверх капсулы PlayerWalker.
    /// Капсула по-прежнему ходит и сталкивается; человек смотрит туда же, куда «Body» капсулы, шагает по фактической скорости
    /// (смесь шаг ↔ бег по VelX/VelZ, быстрее бега — ускоряется темп), стопы на ступенях — HumanGhostIK.
    /// В прыжке и падении стопы не ищут пол (иначе ноги тянутся вниз и тело дёргается).
    /// Висит на корне игрока (рядом с PlayerWalker), модель — ребёнок «Модель» с Animator (RigPlayerBody ставит).</summary>
    [DefaultExecutionOrder(100)]
    public class WalkerBody : MonoBehaviour, IHumanMotion
    {
        public Animator anim;
        public PlayerWalker walker;
        [Tooltip("Сглаживание таза игрока на ступенях (с)")] public float pelvisSmooth = 0.14f;
        [Tooltip("Сглаживание опоры под стопой игрока (с)")] public float footSmooth = 0.08f;

        public Transform Root => transform;
        public Vector3 Velocity { get; private set; }

        Vector3 prev;
        CharacterController cc;
        HumanGhostIK ik;
        float airT, feetW = 1f;
        static readonly int VelXId = Animator.StringToHash("VelX"), VelZId = Animator.StringToHash("VelZ");
        const float RunV = 4.58f;

        void Start()
        {
            if (!walker) walker = GetComponent<PlayerWalker>();
            cc = GetComponent<CharacterController>();
            // капсула-заглушка больше не видна
            if (walker && walker.body)
                foreach (var r in walker.body.GetComponentsInChildren<Renderer>()) r.enabled = false;
            if (anim)
            {
                ik = anim.GetComponent<HumanGhostIK>();
                if (ik) { ik.pelvisSmooth = pelvisSmooth; ik.footSmooth = footSmooth; }
            }
            prev = transform.position;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !anim) return;
            Vector3 d = transform.position - prev; prev = transform.position;
            float vy = d.y / dt;
            d.y = 0f;
            Vector3 v = d / dt;
            if (v.magnitude > 15f) v = Vector3.zero;       // телепорт (R, точки 1–6)
            Velocity = v;
            if (walker && walker.body) anim.transform.rotation = Quaternion.Euler(0f, walker.body.eulerAngles.y, 0f);
            float sp = v.magnitude;
            anim.speed = sp > RunV ? sp / RunV : 1f;
            Vector3 local = Quaternion.Inverse(anim.transform.rotation) * v / anim.speed;
            anim.SetFloat(VelXId, local.x, 0.08f, dt);
            anim.SetFloat(VelZId, local.z, 0.08f, dt);

            // в воздухе стопы не ищут пол: прыжок вверх — сразу, сход вниз — через 0.1 с (кромка ступени не в счёт)
            bool grounded = !cc || !cc.enabled || cc.isGrounded || (walker && walker.Mode.StartsWith("стрем"));
            airT = grounded ? 0f : airT + dt;
            bool air = !grounded && (vy > 1f || airT > 0.1f);
            feetW = Mathf.MoveTowards(feetW, air ? 0f : 1f, dt / (air ? 0.08f : 0.15f));
            if (ik) ik.feetWeight = feetW;
        }
    }
}
