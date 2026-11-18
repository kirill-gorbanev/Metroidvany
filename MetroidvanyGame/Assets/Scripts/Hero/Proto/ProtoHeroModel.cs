using System.Linq;
using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>Модель героя вместо кубиков. Отдельный блок: читает точки прототипа (тело, кисть, хват) и отдаёт их позе
    /// <see cref="HeroBody"/>. Логику прототипа не трогает — кубики остаются точками алгоритма, только прячутся.
    /// Порядок: после HeroProto.Update, до HeroBody.LateUpdate (200).</summary>
    [DefaultExecutionOrder(150)]
    public class ProtoHeroModel : MonoBehaviour
    {
        public HeroProto proto;
        [Tooltip("Поза модели (на объекте «Модель»). Рамка тела — этот объект (корень героя).")]
        public HeroBody body;

        [Header("Вид")]
        [Tooltip("Показывать кубики-точки (тело, плечо, локоть, кисть) поверх модели.")]
        public bool showCubes = false;
        [Tooltip("Плавность разворота модели за телом (1/с).")]
        public float turnFollow = 20f;

        [Header("Рука")]
        [Tooltip("Сгиб локтя по ходу: рука вытянута (начало хода) → согнута (конец).")]
        public float elbowFar = 0.95f, elbowNear = 0.55f;
        [Tooltip("Ладонь над поверхностью (м).")]
        public float palmLift = 0.02f;
        [Tooltip("Свободная рука: кисть чуть над полом (м), а не волочится.")]
        public float freeLift = 0.06f;

        Renderer[] cubeRenderers;

        void Start()
        {
            if (!proto) proto = FindObjectOfType<HeroProto>();
            if (!body) body = GetComponentInChildren<HeroBody>();
            if (body) body.root = transform;
            if (proto)
            {
                cubeRenderers = new[] { proto.bodyCube, proto.shoulderCube, proto.elbowCube, proto.handCube }
                    .Where(t => t).Select(t => t.GetComponent<Renderer>()).Where(r => r).ToArray();
                Place(1f);
            }
        }

        void LateUpdate()
        {
            if (!proto || !body) return;
            Place(1f - Mathf.Exp(-turnFollow * Time.deltaTime));
        }

        void Place(float k)
        {
            foreach (var r in cubeRenderers) r.enabled = showCubes;
            proto.drawArmLine = showCubes;

            // корень модели: центр тела на поверхности, лицом туда же, куда тело прототипа
            transform.position = proto.BodyGround;
            Vector3 f = Vector3.ProjectOnPlane(proto.bodyCube.forward, Vector3.up);
            if (f.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(f.normalized, Vector3.up), k);

            // кисть: в хвате — ладонь на поверхности; свободна — чуть над ней
            bool grip = proto.Gripping;
            body.handTarget = proto.HandOnSurface + Vector3.up * (grip ? palmLift : freeLift);
            body.handPlanted = grip;
            body.gripping = grip;
            body.handNormal = Vector3.up;
            body.handWeight = 1f;
            body.elbowTarget = grip ? Mathf.Lerp(elbowFar, elbowNear, proto.t) : 0.9f;
            body.effort = grip ? 0.5f : 0f;
            body.lookTarget = proto.HandOnSurface + f.normalized * 0.4f;
        }
    }
}
