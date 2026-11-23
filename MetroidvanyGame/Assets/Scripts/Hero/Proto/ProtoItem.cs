using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ПРЕДМЕТ — ВСЕГДА ФИЗИЧЕСКОЕ ТЕЛО UNITY (переделка 2026-11-22, автор: «ты усложняешь схему — стоит её перевернуть,
    /// удалить лишнее и получить работающий механизм физичных объектов»).
    ///
    /// · Rigidbody (масса, трение, сон в покое) + BoxCollider. Стопки, падение, наклон, «вытащить снизу», «поставить на наклонный» —
    ///   всё делает физика, своих правил про опоры нет.
    /// · Рука ведёт предмет к цели (CarryStep): желаемая скорость к цели, разгон не больше carryForce / масса — тяжёлый идёт медленнее
    ///   и не поднимается, лёгкий — быстро; сам упирается в стены и ступени, заезжает на них, тащит стопку трением.
    /// · Мир для предметов — неподвижные коллайдеры (HeroProto.EnsureItemWorld): рельеф, стены, пол. Тело героя —
    ///   Rigidbody (HeroProto.PhysicsStep): кто кого — массы и сила тела.
    /// · Живые (включённые) предметы — в списке Live: выключенная зона арены свои предметы не показывает.</summary>
    public class ProtoItem : MonoBehaviour
    {
        public static readonly List<ProtoItem> Live = new List<ProtoItem>();

        [Tooltip("Масса (кг): физика; тело (Rigidbody, HeroProto.bodyMass) толкает предмет по массам; тяжёлый рука тащит медленнее и не поднимает.")]
        public float mass = 1f;
        [Tooltip("Предмет, вошедший в тело или в другой коллайдер, выталкивается не быстрее этого (м/с) — без «запуливания».")]
        public float depenSpeed = 0.5f;

        /// <summary>В руке или на спине — не ловится рукой, не стена для тела.</summary>
        [System.NonSerialized] public bool held;

        Rigidbody rb;
        Renderer rend; MaterialPropertyBlock mpb; Color baseColor = Color.white; bool haveColor;

        public Rigidbody Body => rb;

        void Awake()
        {
            if (!GetComponent<Collider>()) gameObject.AddComponent<BoxCollider>();
            rb = GetComponent<Rigidbody>();
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.05f, mass);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxDepenetrationVelocity = depenSpeed;
            rb.isKinematic = true;                                       // до Start: мир (коллайдеры рельефа и пола) ещё не поставлен
            rend = GetComponentInChildren<Renderer>();
            if (rend && rend.sharedMaterial)
            {
                var m = rend.sharedMaterial;
                if (m.HasProperty("_BaseColor")) { baseColor = m.GetColor("_BaseColor"); haveColor = true; }
                else if (m.HasProperty("_Color")) { baseColor = m.GetColor("_Color"); haveColor = true; }
            }
        }

        void Start()
        {
            var hero = FindAnyObjectByType<HeroProto>();
            if (hero) hero.EnsureItemWorld();                            // коллайдеры мира — до того, как предмет начнёт падать
            if (!held) rb.isKinematic = false;
        }

        /// <summary>Тело героя предмет не подбрасывает: в касании с телом скорость вверх гасится.</summary>
        void OnCollisionStay(Collision c)
        {
            if (rb.isKinematic || c.collider != HeroProto.BodyCollider) return;
            Vector3 v = rb.linearVelocity;
            if (v.y > 0f) rb.linearVelocity = new Vector3(v.x, 0f, v.z);
        }

        void OnEnable() { if (!Live.Contains(this)) Live.Add(this); }
        void OnDisable() { Live.Remove(this); held = false; }

        // ---- в руке ----
        Quaternion holdRot;                  // как рука держит предмет: прямо, поворот вокруг вертикали — тот, что был при хвате

        /// <summary>Рука взяла: держит прямо, с тем поворотом вокруг вертикали, какой был при хвате (автор 2026-11-23:
        /// «в момент подъёма крутится, хотя я его держу — в руке болтаться не должен»).</summary>
        public void BeginCarry()
        {
            held = true; rb.isKinematic = false; rb.WakeUp();
            Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(transform.up, Vector3.up);   // лежал «лицом» вверх/вниз
            holdRot = f.sqrMagnitude < 1e-4f ? Quaternion.identity : Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        /// <summary>Отпустили: дальше — просто физика (встанет, съедет, упадёт).</summary>
        public void EndCarry() { held = false; rb.WakeUp(); }

        /// <summary>Шаг руки: центр предмета — к цели. Желаемая скорость (target − центр) · gain, не быстрее maxSpeed; разгон за шаг —
        /// не больше maxForce / масса, причём поддержка против тяжести тратит ту же силу: тяжёлый не поднимается, только тащится.
        /// dt — шаг героя: несколько шагов за шаг физики складываются в правильный толчок (тяжесть добавляет сама физика).</summary>
        public void CarryStep(Vector3 target, float dt, float gain, float maxSpeed, float maxForce)
        {
            Vector3 c = rb.worldCenterOfMass, v = rb.linearVelocity;
            Vector3 want = Vector3.ClampMagnitude((target - c) * gain, maxSpeed);
            Vector3 acc = (want - v) / Mathf.Max(dt, 1e-4f) - Physics.gravity;   // нужное ускорение сверх тяжести
            acc = Vector3.ClampMagnitude(acc, maxForce / rb.mass);
            rb.linearVelocity = v + acc * dt;
            HoldRotation();
            rb.WakeUp();
        }

        /// <summary>Рука держит ориентацию: угловая скорость — ровно к holdRot (не кувыркается от трения о пол, угла ступени, толчка);
        /// без перескока — довод не быстрее holdTurnSpeed.</summary>
        void HoldRotation()
        {
            const float gain = 25f, holdTurnSpeed = 12f;                  // 1/с, рад/с
            Quaternion d = holdRot * Quaternion.Inverse(rb.rotation);
            if (d.w < 0f) d = new Quaternion(-d.x, -d.y, -d.z, -d.w);     // кратчайший путь
            d.ToAngleAxis(out float ang, out Vector3 axis);
            if (ang < 0.01f || float.IsNaN(axis.x) || float.IsInfinity(axis.x)) { rb.angularVelocity = Vector3.zero; return; }
            rb.angularVelocity = Vector3.ClampMagnitude(axis * (ang * Mathf.Deg2Rad * gain), holdTurnSpeed);
        }

        /// <summary>На спине (блок HeroProto.Back): закреплён, физика выключена; снят — снова физика.</summary>
        public void Lock(bool on)
        {
            held = on;
            rb.isKinematic = on;
            if (!on) rb.WakeUp();
        }

        /// <summary>СЛЕД ПРЕДМЕТА НА ПОЛУ — повёрнутый прямоугольник (автор 2026-11-23: «форма коллайдера» — касание считалось по
        /// габариту, выровненному по осям мира: повёрнутый предмет «касался» тела пустым углом габарита). c — центр (y = 0),
        /// u, v — оси следа (горизонтальные, единичные), h — половины вдоль них.</summary>
        public struct Foot
        {
            public Vector3 c, u, v; public Vector2 h;
            public Foot At(Vector3 nc) { var f = this; f.c = new Vector3(nc.x, 0f, nc.z); return f; }
            /// <summary>Точка следа, ближайшая к p (отступ inset внутрь от края); p внутри — сама p.</summary>
            public Vector3 Closest(Vector3 p, float inset)
            {
                Vector3 d = new Vector3(p.x - c.x, 0f, p.z - c.z);
                float ix = Mathf.Min(inset, h.x), iz = Mathf.Min(inset, h.y);
                float x = Mathf.Clamp(Vector3.Dot(d, u), -h.x + ix, h.x - ix), z = Mathf.Clamp(Vector3.Dot(d, v), -h.y + iz, h.y - iz);
                return c + u * x + v * z;
            }
            /// <summary>Углы следа (k — доля от центра до края).</summary>
            public Vector3[] Corners(float k) => new[] { c + (u * h.x + v * h.y) * k, c + (u * h.x - v * h.y) * k, c + (-u * h.x + v * h.y) * k, c + (-u * h.x - v * h.y) * k };
        }

        /// <summary>След по BoxCollider предмета: самая вертикальная ось коробки — «вверх», две другие, положенные на пол, — оси следа.</summary>
        public Foot FootPrint
        {
            get
            {
                var bc = GetComponent<BoxCollider>();
                return FootOfBox(transform, bc ? bc.center : Vector3.zero, bc ? bc.size : Vector3.one);
            }
        }

        /// <summary>След на полу любой коробки: transform + центр и размер в его локальных координатах (предмет, камень на склоне).</summary>
        public static Foot FootOfBox(Transform transform, Vector3 cen, Vector3 size)
        {
            {
                Vector3 ls = transform.lossyScale; size = new Vector3(Mathf.Abs(size.x * ls.x), Mathf.Abs(size.y * ls.y), Mathf.Abs(size.z * ls.z));
                Vector3[] ax = { transform.right, transform.up, transform.forward };
                float[] hs = { size.x * 0.5f, size.y * 0.5f, size.z * 0.5f };
                int up = 0; for (int i = 1; i < 3; i++) if (Mathf.Abs(ax[i].y) > Mathf.Abs(ax[up].y)) up = i;
                int a = (up + 1) % 3, b = (up + 2) % 3;
                Vector3 fa = new Vector3(ax[a].x, 0f, ax[a].z), fb = new Vector3(ax[b].x, 0f, ax[b].z);
                float la = fa.magnitude, lb = fb.magnitude;
                var f = new Foot { c = transform.TransformPoint(cen) };
                f.c.y = 0f;
                f.u = la > 1e-4f ? fa / la : Vector3.right;
                f.v = lb > 1e-4f ? fb / lb : Vector3.Cross(f.u, Vector3.up);
                // наклонённый предмет: тень на полу шире — добавить вклад вертикальной оси
                Vector3 fu = new Vector3(ax[up].x, 0f, ax[up].z);
                f.h = new Vector2(hs[a] * la + Mathf.Abs(Vector3.Dot(fu, f.u)) * hs[up], hs[b] * lb + Mathf.Abs(Vector3.Dot(fu, f.v)) * hs[up]);
                return f;
            }
        }

        /// <summary>Габарит в мире (по рендеру; без рендера — по масштабу единичного кубика).</summary>
        public Bounds WorldBounds => rend ? rend.bounds : new Bounds(transform.position, transform.lossyScale);

        /// <summary>Подсветка: наведена кисть — светлее, тащим — ещё светлее. Только вид.</summary>
        public void SetLook(bool hover, bool drag, bool toBack = false)
        {
            if (!rend || !haveColor) return;
            mpb ??= new MaterialPropertyBlock();
            Color c = toBack ? Color.Lerp(baseColor, new Color(0.45f, 1f, 0.55f), 0.6f)      // отпустишь — на спину
                : drag ? Color.Lerp(baseColor, Color.white, 0.45f) : hover ? Color.Lerp(baseColor, Color.white, 0.25f) : baseColor;
            rend.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c); mpb.SetColor("_Color", c);
            rend.SetPropertyBlock(mpb);
        }

        void OnValidate() { if (rb) { rb.mass = Mathf.Max(0.05f, mass); rb.maxDepenetrationVelocity = depenSpeed; } }

        void OnDrawGizmos()
        {
            Gizmos.color = held ? new Color(0.3f, 1f, 0.5f) : new Color(0.4f, 0.8f, 1f);
            var k = FootPrint.Corners(1f); float y = WorldBounds.min.y + 0.01f;
            for (int i = 0; i < 4; i++) k[i].y = y;
            Gizmos.DrawLine(k[0], k[1]); Gizmos.DrawLine(k[1], k[3]); Gizmos.DrawLine(k[3], k[2]); Gizmos.DrawLine(k[2], k[0]);
        }
    }
}
