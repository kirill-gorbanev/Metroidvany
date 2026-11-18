using UnityEngine;

namespace Fortress.Rig
{
    /// <summary>Мышца сустава: тянет часть тела к целевому повороту пружиной сустава.
    /// Поворот считается относительно тела, к которому прикреплён сустав (connectedBody), а если его нет — родителя.
    /// Цель ставит: походка (Aim), анимация (follow — кость невидимого «призрака»), рефлекс (Override).
    /// Сила = spring × strength × тонус тела × (1 − слабость в месте удара).</summary>
    [RequireComponent(typeof(ConfigurableJoint))]
    public class RigMuscle : MonoBehaviour
    {
        [Tooltip("Жёсткость мышцы (момент на радиан)")] public float spring = 200f;
        [Tooltip("Гашение колебаний")] public float damper = 6f;
        public float maxForce = 600f;
        [Tooltip("Множитель силы группы (ноги сильнее рук)")] public float strength = 1f;
        [Range(0, 1), Tooltip("Местная слабость после удара (0 — норма)")] public float weakness;
        public float weaknessRecover = 1.2f;
        [Tooltip("Кость «призрака», позу которой повторяет мышца (анимация)")] public Transform follow;
        [Tooltip("Кость «призрака», соответствующая телу крепления")] public Transform followRef;

        /// <summary>Поворот в покое относительно тела крепления.</summary>
        public Quaternion StartLocal { get; private set; }
        /// <summary>Цель: поворот относительно тела крепления.</summary>
        [System.NonSerialized] public Quaternion targetLocal;
        /// <summary>Рефлекс перекрывает анимацию (руки вперёд при падении и т.п.).</summary>
        [System.NonSerialized] public bool overrideActive;
        [System.NonSerialized] public float toneOverride = -1f;
        /// <summary>Временное усиление (руки держат ношу, толкают) — множитель силы.</summary>
        [System.NonSerialized] public float boost = 1f;

        public ConfigurableJoint Joint { get; private set; }
        public Rigidbody Rb { get; private set; }
        public Transform Ref { get; private set; }
        Quaternion worldToJoint;
        bool ready;

        void Awake() => Init();

        public void Init()
        {
            if (ready) return;
            Joint = GetComponent<ConfigurableJoint>();
            Rb = GetComponent<Rigidbody>();
            Ref = Joint.connectedBody ? Joint.connectedBody.transform : transform.parent;
            StartLocal = Ref ? Quaternion.Inverse(Ref.rotation) * transform.rotation : transform.rotation;
            targetLocal = StartLocal;
            Vector3 right = Joint.axis;
            Vector3 fwd = Vector3.Cross(Joint.axis, Joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(fwd, right).normalized;
            worldToJoint = Quaternion.LookRotation(fwd, up);
            Joint.rotationDriveMode = RotationDriveMode.Slerp;
            Joint.configuredInWorldSpace = false;
            ready = true;
        }

        /// <summary>Применить цель и силу (вызывает RigBody каждый шаг физики).</summary>
        public void Apply(float tone)
        {
            if (follow && !overrideActive)
                targetLocal = followRef ? Quaternion.Inverse(followRef.rotation) * follow.rotation : follow.localRotation;
            float t = toneOverride >= 0f ? toneOverride : tone;
            float s = strength * boost;
            float k = spring * s * t * (1f - weakness);
            Joint.slerpDrive = new JointDrive
            {
                positionSpring = k,
                positionDamper = damper * s * Mathf.Lerp(0.25f, 1f, t),
                maximumForce = maxForce * s,
            };
            Joint.targetRotation = Quaternion.Inverse(worldToJoint) * (Quaternion.Inverse(targetLocal) * StartLocal) * worldToJoint;
            if (weakness > 0f) weakness = Mathf.Max(0f, weakness - weaknessRecover * Time.fixedDeltaTime);
        }

        /// <summary>Нацелить ось «от сустава к концу» (restDirRef — в осях тела крепления в покое) в мировом направлении dirWorld.</summary>
        public void Aim(Vector3 restDirRef, Vector3 dirWorld)
        {
            Vector3 d = Ref ? Ref.InverseTransformDirection(dirWorld) : dirWorld;
            if (d.sqrMagnitude < 1e-6f) return;
            targetLocal = Quaternion.FromToRotation(restDirRef, d.normalized) * StartLocal;
        }
    }
}
