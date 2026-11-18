using UnityEngine;

namespace Fortress.Rig
{
    /// <summary>Тело-риг: части на суставах с мышцами и общий «тонус».
    /// Удар (толчок, мяч, падение) отнимает тонус пропорционально силе — мышцы слабеют, тело по-настоящему
    /// отклоняется, оседает или падает; через паузу тонус возвращается. В месте удара мышца слабеет сильнее.</summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(Rigidbody))]
    public class RigBody : MonoBehaviour
    {
        public string title = "Риг";
        [Range(0, 1), Tooltip("Общий тонус мышц: 1 — бодр, 0 — обмяк")] public float tone = 1f;
        [Tooltip("Удар мягче этой скорости (м/с, импульс/масса тела) не оглушает")] public float stunThreshold = 1.2f;
        [Tooltip("Потеря тонуса на 1 м/с сверх порога")] public float stunPerHit = 0.3f;
        [Tooltip("Пауза перед восстановлением, с")] public float recoverDelay = 0.6f;
        [Tooltip("Скорость восстановления тонуса, в секунду")] public float recoverRate = 0.55f;
        public float minTone = 0.03f;

        public RigMuscle[] Muscles { get; private set; }
        public Rigidbody[] Bodies { get; private set; }
        public Rigidbody Root { get; private set; }
        public float Mass { get; private set; }
        /// <summary>Сила последнего удара (м/с) и когда он был — для подписи на стенде.</summary>
        public float LastHit { get; private set; }
        public float LastHitTime { get; private set; } = -99f;
        /// <summary>Время последнего касания чего-либо чужого (для переворота).</summary>
        public float LastContactTime { get; set; } = -99f;
        public string State = "";
        float stunUntil;

        void Awake()
        {
            Root = GetComponent<Rigidbody>();
            Muscles = GetComponentsInChildren<RigMuscle>();
            foreach (var m in Muscles) m.Init();
            Bodies = GetComponentsInChildren<Rigidbody>();
            Mass = 0f;
            foreach (var b in Bodies)
            {
                Mass += b.mass;
                b.solverIterations = 24;
                b.solverVelocityIterations = 8;
                b.maxAngularVelocity = 40f;
                b.interpolation = RigidbodyInterpolation.Interpolate;
                b.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                if (!b.GetComponent<RigPart>()) b.gameObject.AddComponent<RigPart>();
            }
            RigLayers.SetBody(gameObject);
            var cols = GetComponentsInChildren<Collider>();
            for (int i = 0; i < cols.Length; i++)
                for (int j = i + 1; j < cols.Length; j++)
                    Physics.IgnoreCollision(cols[i], cols[j]);
        }

        /// <summary>Свой ли это коллайдер (для лучей опоры).</summary>
        public bool Owns(Collider c) => c && c.attachedRigidbody && c.attachedRigidbody.GetComponentInParent<RigBody>() == this;

        public void OnPartHit(RigPart part, Collision c)
        {
            LastContactTime = Time.time;
            float imp = c.impulse.magnitude;
            Vector3 dir = -c.impulse.normalized;
            var other = c.rigidbody;
            if (other && !other.isKinematic && !Owns(c.collider))
            {
                // удар летящим/движущимся предметом: доля его импульса, которую получает всё тело
                // (притяжение и суставы гасят мгновенный импульс части — считаем по массам и скорости сближения)
                // считаем только скорость, с которой ПРЕДМЕТ идёт на нас: упёрся сам в стоящий ящик — это не удар
                Vector3 n = c.contactCount > 0 ? c.GetContact(0).normal : -dir;          // от предмета к нам
                Vector3 vo = other.GetPointVelocity(c.contactCount > 0 ? c.GetContact(0).point : other.worldCenterOfMass);
                float approach = Mathf.Max(0f, Vector3.Dot(vo, n));
                float share = other.mass / (other.mass + Mass);
                imp = Mathf.Max(imp, approach * share * Mass);
                if (other.linearVelocity.sqrMagnitude > 0.04f) dir = other.linearVelocity.normalized;
            }
            FromCollision = true;
            Hit(part.gameObject, imp, dir);
            FromCollision = false;
        }

        /// <summary>Удар по части тела импульсом (Н·с): столкновение, тычок мышью, пинок.</summary>
        /// <summary>Удар по телу: часть и сила (м/с, импульс / масса тела). Человек решает сам: качнуться, шагнуть, упасть.</summary>
        public event System.Action<GameObject, float, Vector3> Hurt;
        /// <summary>Удар пришёл от столкновения (а не от стенда/скрипта, который сам уже толкнул тело).</summary>
        public bool FromCollision { get; private set; }

        public void Hit(GameObject part, float impulse) => Hit(part, impulse, Vector3.zero);

        public void Hit(GameObject part, float impulse, Vector3 dir)
        {
            float dv = impulse / Mathf.Max(0.1f, Mass);
            Hurt?.Invoke(part, dv, dir);
            if (dv < stunThreshold) return;
            float loss = (dv - stunThreshold) * stunPerHit;
            Stun(loss, recoverDelay + loss * 1.6f);
            var m = part ? part.GetComponent<RigMuscle>() : null;
            if (m) m.weakness = Mathf.Clamp01(m.weakness + loss * 2.5f);
            LastHit = dv; LastHitTime = Time.time;
        }

        /// <summary>Оглушить: отнять тонус и задержать восстановление.</summary>
        public void Stun(float toneLoss, float seconds)
        {
            tone = Mathf.Max(minTone, tone - toneLoss);
            stunUntil = Mathf.Max(stunUntil, Time.time + seconds);
        }

        public bool Stunned => Time.time < stunUntil;

        void FixedUpdate()
        {
            if (!Stunned) tone = Mathf.MoveTowards(tone, 1f, recoverRate * Time.fixedDeltaTime);
            foreach (var m in Muscles) m.Apply(tone);
        }

        /// <summary>Центр масс всего тела.</summary>
        public Vector3 CenterOfMass
        {
            get
            {
                Vector3 s = Vector3.zero; float w = 0f;
                foreach (var b in Bodies) { s += b.worldCenterOfMass * b.mass; w += b.mass; }
                return w > 0 ? s / w : transform.position;
            }
        }
    }
}
