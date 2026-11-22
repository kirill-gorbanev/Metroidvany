using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ПРЕДМЕТ — маркер: то, что рука берёт и тащит (блок HeroProto.Drag). Кубик или любой объект с рендером.
    /// Не стена и не опора: лежит в декоре зоны. Свой Transform — и есть предмет: тащим его, кладём, дальше — на спину / на голову (LOCK).
    /// Живые (включённые) предметы — в списке Live: выключенная зона арены свои предметы не показывает.</summary>
    public class ProtoItem : MonoBehaviour
    {
        public static readonly List<ProtoItem> Live = new List<ProtoItem>();

        /// <summary>Тащит рука сейчас.</summary>
        [System.NonSerialized] public bool held;

        [Tooltip("Масса (кг): физика падения и трения; лёгкий (не тяжелее HeroProto.pushMaxMass) тело расталкивает, тяжёлый — стена.")]
        public float mass = 1f;

        Renderer rend; MaterialPropertyBlock mpb; Color baseColor = Color.white; bool haveColor;

        void OnEnable() { if (!Live.Contains(this)) Live.Add(this); }
        void OnDisable() { Live.Remove(this); held = false; if (rb) { Destroy(rb); rb = null; } if (Carried == this) Carried = null; }

        // ---- ПАДЕНИЕ ВНЕ РАВНОВЕСИЯ (блок HeroProto.Topple) ----
        // Автор 2026-11-22: «я выбрал конкретно объект А, перетаскал и установил вне равновесия — тогда объект А падает до тех пор,
        // пока не встанет, и тогда объект фиксируется». Физика — только у этого предмета и только на время падения: свой Rigidbody,
        // всё остальное (стены, ступени, тело, другие предметы) — неподвижные коллайдеры: он о них бьётся, но никого не толкает.
        Rigidbody rb; float restT, fallT;
        /// <summary>Что сейчас тащит рука (из-под него можно вытащить стопку). Пока он есть — падающие не засыпают и не фиксируются:
        /// опора под ними двигается (автор 2026-11-22: «чтобы я мог его вытащить — и всё упало; используй физику Unity»).</summary>
        public static ProtoItem Carried;
        /// <summary>Падает (физика включена) — пока не встанет.</summary>
        public bool Toppling => rb != null && !rb.isKinematic;

        // ---- В РУКЕ: кинематическое тело (автор 2026-11-22: «объект сверху лежит и ждёт, когда из-под него уберут, — а трение-то
        // должно быть»). Раньше взятый двигался телепортом — физика не знала его скорости, лежащее сверху не ехало. Теперь
        // взятый — кинематический Rigidbody, двигается MovePosition: трение везёт стопку, рывок или стена — верхние соскальзывают.
        Vector3 carryPos;
        /// <summary>Где взятый предмет по логике руки (тело догонит на шаге физики).</summary>
        public Vector3 CarryPos => carryPos;
        public bool Carrying => rb != null && rb.isKinematic;

        public void BeginCarry()
        {
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.mass = Mathf.Max(0.05f, mass);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.position = transform.position; rb.rotation = transform.rotation;
            carryPos = transform.position;
            held = true;
        }

        public void CarryTo(Vector3 p) { carryPos = p; if (rb) rb.MovePosition(p); }

        /// <summary>Отпустили: встать точно на место руки; topple — дальше падать по физике, иначе — тело снято, лежит.</summary>
        public void EndCarry(bool topple)
        {
            transform.position = carryPos;
            if (!rb) { held = false; return; }
            rb.position = carryPos;
            if (topple)
            {
                rb.isKinematic = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                restT = 0f; fallT = 0f; held = true;
            }
            else { DestroyImmediate(rb); rb = null; held = false; }   // сразу: в том же кадре может понадобиться новое тело
        }
        [Tooltip("Падающий предмет считается вставшим, когда почти не движется столько секунд.")]
        public float restTime = 0.3f;

        /// <summary>Отпустили вне равновесия: включить физику этому предмету (held — ни препятствие, ни опора, ни берётся рукой).</summary>
        public void StartTopple()
        {
            if (rb && !rb.isKinematic) return;
            if (!GetComponent<Collider>()) gameObject.AddComponent<BoxCollider>();
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.mass = Mathf.Max(0.05f, mass);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            held = true; restT = 0f; fallT = 0f;
        }

        void FixedUpdate()
        {
            if (!rb || rb.isKinematic) return;
            if (Carried && Carried != this) { rb.WakeUp(); restT = 0f; fallT = 0f; return; }   // опору тащат — не засыпать
            float dt = Time.fixedDeltaTime;
            fallT += dt;
            restT = rb.linearVelocity.magnitude < 0.05f && rb.angularVelocity.magnitude < 0.2f ? restT + dt : 0f;
            if (restT >= restTime || rb.IsSleeping() || fallT > 6f) EndTopple();
        }

        /// <summary>Встал — физика снята, предмет снова зафиксирован (лежит, как все).</summary>
        public void EndTopple()
        {
            if (!rb) return;
            DestroyImmediate(rb); rb = null; held = false;
        }

        void Awake()
        {
            if (!GetComponent<Collider>()) gameObject.AddComponent<BoxCollider>();   // для падающих соседей — неподвижная преграда
            rend = GetComponentInChildren<Renderer>();
            if (rend && rend.sharedMaterial)
            {
                var m = rend.sharedMaterial;
                if (m.HasProperty("_BaseColor")) { baseColor = m.GetColor("_BaseColor"); haveColor = true; }
                else if (m.HasProperty("_Color")) { baseColor = m.GetColor("_Color"); haveColor = true; }
            }
        }

        /// <summary>Габарит в мире (по рендеру; без рендера — по масштабу единичного кубика).</summary>
        public Bounds WorldBounds => rend ? rend.bounds : new Bounds(transform.position, transform.lossyScale);
        /// <summary>Половина высоты: от центра до низа.</summary>
        public float HalfHeight => WorldBounds.extents.y;
        /// <summary>Радиус следа на полу (для стен и тела): полудиагональ габарита по XZ.</summary>
        public float Radius { get { var e = WorldBounds.extents; return Mathf.Sqrt(e.x * e.x + e.z * e.z); } }
        /// <summary>Центр низа (на поверхности).</summary>
        public Vector3 Bottom { get { var b = WorldBounds; return new Vector3(b.center.x, b.min.y, b.center.z); } }

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

        void OnDrawGizmos()
        {
            var b = WorldBounds; Gizmos.color = held ? new Color(0.3f, 1f, 0.5f) : new Color(0.4f, 0.8f, 1f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
