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
        float sleepDefault;
        /// <summary>В РУКЕ НЕ ЗАСЫПАЕТ (автор 2026-11-24, лаборатория: брусок не крутился): лёгкий узкий предмет при медленном повороте —
        /// энергия ниже порога сна, физика его «усыпляла» прямо в руке. Пока несёшь / крутишь — порог 0; отпустил — как был.</summary>
        void InHand(bool on) { rb.sleepThreshold = on ? 0f : sleepDefault; }

        void Awake()
        {
            if (!GetComponent<Collider>()) gameObject.AddComponent<BoxCollider>();
            rb = GetComponent<Rigidbody>();
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.05f, mass);
            // БЕЗ ИНТЕРПОЛЯЦИИ (автор 2026-11-24: «нет плавности — как будто 30 к/с при 200»): физику шагает герой сам каждый кадр
            // (Script-режим, HeroProto.Tick) — после шага место точное; интерполяция Unity привязана к FixedUpdate и рисовала
            // предмет по своему таймеру
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxDepenetrationVelocity = depenSpeed;
            sleepDefault = rb.sleepThreshold;
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
            if (rb.isKinematic) return;
            if (c.collider != HeroProto.BodyCollider) { if (held) AddContacts(c); return; }
            Vector3 v = rb.linearVelocity;
            if (v.y > 0f) rb.linearVelocity = new Vector3(v.x, 0f, v.z);
        }
        void OnCollisionEnter(Collision c) { if (!rb.isKinematic && held && c.collider != HeroProto.BodyCollider) AddContacts(c); }

        // ---- касания предмета в руке (зоны 3–4 переноса: ход мыши — в плоскость касания) ----
        Vector3 contactSum; int contactCount;
        readonly List<Vector3> stepNormals = new List<Vector3>();   // касания прошлого шага физики — для тяги руки (CarryStep)

        /// <summary>Нормали касаний — от поверхности к предмету (наружу из того, во что упёрлись).</summary>
        void AddContacts(Collision c)
        {
            if (c.rigidbody && !c.rigidbody.isKinematic) return;         // едущее на предмете (живая физика) — не поверхность; стены, пол, застывшие — да
            Vector3 me = rb.worldCenterOfMass;
            for (int i = 0; i < c.contactCount; i++)
            {
                var cp = c.GetContact(i);
                Vector3 n = cp.normal;
                if (Vector3.Dot(n, me - cp.point) < 0f) n = -n;
                contactSum += n; contactCount++;
                if (stepNormals.Count < 16) stepNormals.Add(n);
            }
        }

        /// <summary>Касания с прошлого опроса (шаги физики за кадр): средняя нормаль; сбрасывает накопленное.</summary>
        public bool TakeContact(out Vector3 n)
        {
            n = contactSum; bool any = contactCount > 0 && n.sqrMagnitude > 1e-8f;
            contactSum = Vector3.zero; contactCount = 0;
            if (any) n.Normalize();
            return any;
        }

        void OnEnable() { if (!Live.Contains(this)) Live.Add(this); }
        void OnDisable() { Live.Remove(this); held = false; }

        // ---- в руке ----
        Quaternion holdRot;                  // как рука держит предмет: прямо, поворот вокруг вертикали — тот, что был при хвате

        /// <summary>Рука взяла: держит прямо, с тем поворотом вокруг вертикали, какой был при хвате (автор 2026-11-23:
        /// «в момент подъёма крутится, хотя я его держу — в руке болтаться не должен»).</summary>
        public void BeginCarry()
        {
            held = true; rb.isKinematic = false; InHand(true); rb.WakeUp();
            contactSum = Vector3.zero; contactCount = 0; stepNormals.Clear();
            Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(transform.up, Vector3.up);   // лежал «лицом» вверх/вниз
            holdRot = f.sqrMagnitude < 1e-4f ? Quaternion.identity : Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        /// <summary>Отпустили: дальше — просто физика (встанет, съедет, упадёт).</summary>
        public void EndCarry() { held = false; InHand(false); rb.WakeUp(); }

        /// <summary>Шаг руки: центр предмета идёт С РУКОЙ, БЕЗ ИНЕРЦИИ (автор 2026-11-23: «движение бесшовное, без подёргивания;
        /// двигаем вместе с рукой, масса большая — скорость ниже»): скорость = (цель − центр) / dt — за шаг ровно в цель, не быстрее
        /// maxSpeed (предел уже с учётом массы). Ни силы, ни разгона — нечему перелетать и раскачиваться. Стены и опору решает физика.
        /// canLift = false (рука не держит вес) — высоту не ведёт: тяжесть и пол, только тащится.</summary>
        public void CarryStep(Vector3 target, float dt, float maxSpeed, bool canLift)
        {
            Vector3 c = rb.worldCenterOfMass;
            Vector3 vel = Vector3.ClampMagnitude((target - c) / Mathf.Max(dt, 1e-4f), maxSpeed);
            if (!canLift) vel.y = Mathf.Min(rb.linearVelocity.y, 0f);       // тяжёлый: вверх не поднять, вниз — сам, тяжестью
            // НЕ ДАВИТЬ В ТО, ЧЕГО КАСАЕШЬСЯ (автор 2026-11-24: «поднять могу, но врезаюсь в стену — застрял, упёрся, вместо того чтобы
            // подняться»): цель руки чуть в стене — предмет давил в неё, трение от давления держало. Скорость «в поверхность» убрана
            // для каждого касания прошлого шага (стены, пол, застывшие предметы) — прилегает без давления, вдоль идёт свободно.
            foreach (var n in stepNormals) { float d = Vector3.Dot(vel, n); if (d < 0f) vel -= n * d; }
            stepNormals.Clear();
            rb.linearVelocity = vel;
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

        /// <summary>Кручение на месте (ПКМ, HeroProto): угловая скорость — к target за оставшееся время кадра, не быстрее maxW (рад/с);
        /// по горизонтали стоит (не съезжает), вниз — тяжесть и опора.</summary>
        public void TurnStep(Quaternion target, Vector3 center, float dtLeft, float maxW)
        {
            InHand(true);
            Quaternion d = target * Quaternion.Inverse(rb.rotation);
            if (d.w < 0f) d = new Quaternion(-d.x, -d.y, -d.z, -d.w);
            d.ToAngleAxis(out float ang, out Vector3 axis);
            if (ang < 0.01f || float.IsNaN(axis.x) || float.IsInfinity(axis.x)) rb.angularVelocity = Vector3.zero;
            else rb.angularVelocity = Vector3.ClampMagnitude(axis * (ang * Mathf.Deg2Rad / Mathf.Max(dtLeft, 1e-4f)), maxW);
            // НА МЕСТЕ, НА ВЕСУ 1 см (как несомый): лёжа под весом трение о пол съедало поворот каждый шаг — центр держится в center
            rb.linearVelocity = Vector3.ClampMagnitude((center - rb.worldCenterOfMass) / Mathf.Max(dtLeft, 1e-4f), 0.5f);
            rb.WakeUp();
        }
        /// <summary>Кручение кончилось: не докручивается по инерции.</summary>
        public void EndTurn() { if (!rb.isKinematic) rb.angularVelocity = Vector3.zero; InHand(false); rb.WakeUp(); }

        /// <summary>На спине (блок HeroProto.Back): закреплён, физика выключена; снят — снова физика.</summary>
        public void Lock(bool on)
        {
            held = on;
            rb.isKinematic = on;
            if (!on) rb.WakeUp();
        }

        /// <summary>Застыл, пока герой несёт ДРУГОЙ предмет (автор 2026-11-23: «другие item считаются неподвижными, пока мы рукой
        /// двигаем и пытаемся сверху что-то установить — не расталкиваем ни рукой, ни предметом в руке»): кинематический —
        /// для несомого предмета как стена и опора. Отпустил — снова физика. В руке / на спине — не трогаем.</summary>
        public bool Frozen { get; private set; }
        public void Freeze(bool on)
        {
            if (on == Frozen) return;
            Frozen = on;
            if (held) return;
            if (on) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
            else { rb.isKinematic = false; rb.WakeUp(); }
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
