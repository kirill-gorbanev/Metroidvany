using UnityEngine;

namespace Fortress.Rig
{
    /// <summary>Походка для любого числа ног (табурет, скамья, животное): ноги шагают группами по очереди,
    /// опорная стопа стоит на месте (нога доворачивается, пока тело едет над ней), шаг ставится туда,
    /// куда тело несёт — поэтому от толчка тело само переступает в сторону толчка (удержание равновесия).
    /// «Жизнь» тела — мягкая поддержка корпуса (высота, вертикаль, скорость), она слабеет вместе с тонусом:
    /// оглушённый проседает и падает, лёжа — сучит ногами и переворачивается.</summary>
    [DefaultExecutionOrder(-20)]
    [RequireComponent(typeof(RigBody))]
    public class LegGait : MonoBehaviour
    {
        [System.Serializable]
        public class Leg
        {
            public string name;
            public RigMuscle hip;
            [Tooltip("Точка стопы в осях ноги")] public Vector3 footLocal;
            [Tooltip("Ноги одной группы шагают вместе")] public int group;

            [System.NonSerialized] public Vector3 restDir;     // от сустава к стопе, оси родителя, в покое
            [System.NonSerialized] public Vector3 homeLocal;   // стопа в покое, оси тела
            [System.NonSerialized] public float length;
            [System.NonSerialized] public bool swinging, grounded;
            [System.NonSerialized] public Vector3 planted, liftFrom, landAt, target;
            [System.NonSerialized] public float t, lastGround = -9f;
            public Vector3 Foot => hip.transform.TransformPoint(footLocal);
        }

        public Leg[] legs;
        [Header("Шаг")]
        public float stepTime = 0.17f;
        [Tooltip("Стопа отстала больше чем на столько (м) — шагнуть")] public float stepTrigger = 0.06f;
        public float stepHeight = 0.07f;
        [Tooltip("Насколько стопа уходит наружу посреди маха, м")] public float swingOut = 0.08f;
        [Tooltip("Предельный угол ноги при махе (сустав держит до 55°)")] public float maxSwingAngle = 50f;
        [Tooltip("Насколько вперёд по скорости ставить шаг")] public float lead = 1.15f;
        [Header("Ход")]
        public float maxSpeed = 0.9f;
        public float accel = 7f;
        [Header("Поддержка корпуса («жизнь»)")]
        public float heightSpring = 35f, heightDamp = 12f;
        [Range(0, 1), Tooltip("Доля веса, которую несёт «жизнь» (остальное — ноги)")] public float carry = 0.3f;
        public float uprightSpring = 110f, uprightDamp = 14f, yawDamp = 4f, yawSpring = 0f;
        [Tooltip("Касание пола «помнится» столько секунд — короткий отрыв не роняет")] public float contactMemory = 0.18f;
        [Header("Переворот")]
        public float rightingTorque = 40f;
        public float hopSpeed = 1.8f;
        [Tooltip("Рывок-кувырок при прыжке, рад/с")] public float rollKick = 5.5f;
        public float flailRate = 12f, flailAngle = 40f;

        [System.NonSerialized] public Vector3 desiredVelocity;
        [System.NonSerialized] public Vector3 desiredFacing;
        public string Mode { get; private set; } = "";
        public int Groups { get; private set; } = 1;

        RigBody rig;
        Rigidbody root;
        float rideHeight, upsideTime, flail, nextHop, groupDone;
        int lastGroup;
        bool wasWalking, righting;
        readonly RaycastHit[] hits = new RaycastHit[12];

        void Start()
        {
            rig = GetComponent<RigBody>();
            root = rig.Root;
            Vector3 avg = Vector3.zero;
            foreach (var l in legs)
            {
                l.hip.Init();
                Vector3 hipW = l.hip.transform.position, footW = l.Foot;
                l.length = Vector3.Distance(hipW, footW);
                var par = l.hip.Ref;
                l.restDir = (par ? par.InverseTransformDirection(footW - hipW) : footW - hipW).normalized;
                l.homeLocal = transform.InverseTransformPoint(footW);
                l.planted = footW; l.target = footW;
                avg += l.homeLocal;
                Groups = Mathf.Max(Groups, l.group + 1);
            }
            rideHeight = -(avg / legs.Length).y;     // высота корпуса над стопами в покое (в осях тела)
            lastGroup = Groups - 1;
        }

        /// <summary>Пол под точкой (свои части не в счёт).</summary>
        bool Ground(Vector3 at, float above, float below, out Vector3 p)
        {
            int n = Physics.RaycastNonAlloc(at + Vector3.up * above, Vector3.down, hits, above + below, RigLayers.SolidMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; p = at; bool ok = false;
            for (int i = 0; i < n; i++)
            {
                if (rig.Owns(hits[i].collider)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; p = hits[i].point; ok = true; }
            }
            return ok;
        }

        void FixedUpdate()
        {
            if (!rig) return;
            float dt = Time.fixedDeltaTime;
            float upright = Vector3.Dot(transform.up, Vector3.up);
            int grounded = 0; Vector3 gsum = Vector3.zero;
            float now = Time.time;
            foreach (var l in legs)
            {
                l.grounded = false;
                if (!l.swinging && Ground(l.Foot, 0.06f, 0.07f, out var gp)) { l.grounded = true; l.lastGround = now; gsum += gp; }
                else if (!l.swinging && now - l.lastGround < contactMemory) gsum += new Vector3(0f, l.planted.y, 0f);
                else continue;
                grounded++;
            }

            // переворот с гистерезисом: лёг (вертикаль < 0.35) — встаёт, пока не выпрямится (> 0.8)
            if (righting && upright > 0.8f) righting = false;
            if (!righting && upright < 0.35f) righting = true;
            if (righting && rig.tone > 0.5f) { Mode = "переворачивается"; Righting(dt); wasWalking = false; return; }
            upsideTime = 0f;
            if (rig.tone < 0.22f) { Mode = "обмяк"; Limp(); wasWalking = false; return; }
            if (!wasWalking) foreach (var l in legs) { l.planted = l.Foot; l.swinging = false; }
            wasWalking = true;
            Mode = rig.tone < 0.6f ? "шатается" : (desiredVelocity.sqrMagnitude > 0.01f ? "идёт" : "стоит");
            Walk(dt, grounded, gsum);
        }

        void Walk(float dt, int grounded, Vector3 gsum)
        {
            var T = transform;
            Vector3 up = Vector3.up;
            float life = Mathf.Clamp01((rig.tone - 0.25f) / 0.6f);   // ослаб — заметно шатается
            float support = life * Mathf.Clamp01(grounded / 2f);

            // скорость: «хочу идти» — сила к желаемой скорости
            Vector3 v = root.linearVelocity;
            Vector3 vh = new Vector3(v.x, 0f, v.z);
            Vector3 want = Vector3.ClampMagnitude(new Vector3(desiredVelocity.x, 0f, desiredVelocity.z), maxSpeed) * life;
            root.AddForce((want - vh) * accel * rig.Mass * support, ForceMode.Force);

            // высота над опорой и часть веса
            float groundY;
            if (grounded > 0) groundY = gsum.y / grounded;
            else groundY = Ground(T.position, 0.2f, 2f, out var gp) ? gp.y : T.position.y - rideHeight;
            float err = Mathf.Clamp(groundY + rideHeight - T.position.y, -0.06f, 0.06f);
            float fy = (heightSpring * err - heightDamp * v.y) * rig.Mass + (-Physics.gravity.y) * rig.Mass * carry;
            root.AddForce(up * fy * support, ForceMode.Force);

            // вертикаль и рысканье
            Vector3 axis = Vector3.Cross(T.up, up);
            float s = axis.magnitude;
            float ang = Mathf.Asin(Mathf.Clamp01(s));
            Vector3 w = root.angularVelocity;
            Vector3 wTilt = w - Vector3.Project(w, up);
            Vector3 tq = (s > 1e-4f ? axis / s * ang * uprightSpring : Vector3.zero) - wTilt * uprightDamp;
            tq += -up * Vector3.Dot(w, up) * yawDamp;
            if (yawSpring > 0f && desiredFacing.sqrMagnitude > 0.01f)
            {
                Vector3 f = Vector3.ProjectOnPlane(T.forward, up), d = Vector3.ProjectOnPlane(desiredFacing, up);
                tq += up * Vector3.SignedAngle(f, d, up) * Mathf.Deg2Rad * yawSpring;
            }
            root.AddTorque(tq * life * TiltInertia, ForceMode.Force);   // вертикаль — от «жизни», не от касания; момент на всё тело

            // шаги: в полёте нога идёт дугой, по окончании — встала
            bool anySwing = false;
            foreach (var l in legs)
            {
                if (!l.swinging) continue;
                l.t += dt / stepTime;
                float e = Mathf.SmoothStep(0f, 1f, l.t);
                l.target = SwingFoot(l, Mathf.Clamp01(l.t), e);
                if (l.t >= 1f) { l.swinging = false; l.planted = l.landAt; groupDone = Time.time; }
                else anySwing = true;
            }
            // новая группа: по очереди, а если тело сильно унесло — та, что отстала больше (ловит равновесие)
            if (!anySwing && Time.time - groupDone > 0.015f)
            {
                int next = (lastGroup + 1) % Groups, worst = -1;
                float needNext = 0f, needWorst = 0f;
                for (int g = 0; g < Groups; g++)
                {
                    float need = 0f;
                    foreach (var l in legs)
                        if (l.group == g) need = Mathf.Max(need, Flat(l.planted - Home(l, vh)).magnitude);
                    if (g == next) needNext = need;
                    if (need > needWorst) { needWorst = need; worst = g; }
                }
                // стоя на месте — переступать реже (не «топтаться»), при ходьбе — как обычно
                bool still = desiredVelocity.sqrMagnitude < 0.0025f && vh.sqrMagnitude < 0.02f;
                float trig = still ? stepTrigger * 2.5f : stepTrigger;
                int go = needNext > trig ? next : (needWorst > trig * 2.2f ? worst : -1);
                if (go >= 0)
                {
                    lastGroup = go;
                    foreach (var l in legs)
                    {
                        if (l.group != go) continue;
                        Vector3 h = Home(l, vh);
                        l.swinging = true; l.t = 0f;
                        l.liftFrom = l.Foot;
                        l.landAt = PickLanding(l, h, vh);
                    }
                }
            }
            // опорные: держать стопу; если её сорвало далеко — переставить
            foreach (var l in legs)
            {
                if (l.swinging) continue;
                if (Flat(l.Foot - l.planted).magnitude > 0.16f) l.planted = l.Foot;
                l.target = l.planted;
            }
            foreach (var l in legs) l.hip.Aim(l.restDir, l.target - l.hip.transform.position);
        }

        /// <summary>Мах жёсткой ноги: стопа идёт дугой на нужной высоте. Нога не укорачивается, поэтому высоту
        /// даёт угол ноги (до maxSwingAngle), а посередине маха стопа выносится наружу — как у ожившей мебели.</summary>
        Vector3 SwingFoot(Leg l, float tt, float e)
        {
            Vector3 hip = l.hip.transform.position;
            float rise = Mathf.Max(0f, l.landAt.y - l.liftFrom.y);
            float py = Mathf.Lerp(l.liftFrom.y, l.landAt.y, e) + (stepHeight + rise * 0.9f) * Mathf.Sin(Mathf.PI * tt);
            Vector3 a = Flat(l.liftFrom - hip), b = Flat(l.landAt - hip);
            Vector3 outward = Flat(hip - transform.position);
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Flat(transform.forward).normalized;
            Vector3 dh = Vector3.Lerp(a, b, e) + outward * (swingOut * Mathf.Sin(Mathf.PI * tt));
            if (dh.sqrMagnitude < 1e-6f) dh = outward;
            float cosT = Mathf.Clamp((hip.y - py) / l.length, Mathf.Cos(maxSwingAngle * Mathf.Deg2Rad), 1f);
            float sinT = Mathf.Sqrt(1f - cosT * cosT);
            Vector3 arc = hip + dh.normalized * (sinT * l.length) + Vector3.down * (cosT * l.length);
            // к концу маха — точно в точку шага: стопа встаёт на своё место под телом, а не «куда дотянулась»
            // (иначе стойка расползается, тело проседает и до ступеньки уже не достать)
            return Vector3.Lerp(arc, l.landAt, Mathf.SmoothStep(0f, 1f, (tt - 0.6f) / 0.4f));
        }

        /// <summary>Куда ставить ногу: пол под точкой шага; если впереди в пределах маха выше (ступень, доска) —
        /// встать на неё, чтобы не биться о край.</summary>
        Vector3 PickLanding(Leg l, Vector3 home, Vector3 vh)
        {
            Vector3 hip = l.hip.transform.position;
            Vector3 best = Ground(home, 0.4f, 0.6f, out var p) ? p : home;
            Vector3 dir = Flat(desiredVelocity.sqrMagnitude > 0.01f ? desiredVelocity : vh);
            if (dir.sqrMagnitude < 1e-4f) return best;
            dir.Normalize();
            float reach = l.length * Mathf.Sin(maxSwingAngle * Mathf.Deg2Rad);
            float baseY = best.y;
            for (float d = 0.12f; d <= reach + 1e-3f; d += 0.07f)
            {
                Vector3 probe = new Vector3(hip.x, 0f, hip.z) + dir * d;
                probe.y = hip.y;
                if (!Ground(probe, 0.05f, 1.2f, out var q)) continue;
                float up = q.y - baseY;
                if (up > 0.03f && hip.y - q.y > l.length * Mathf.Cos(maxSwingAngle * Mathf.Deg2Rad)) { best = q; baseY = q.y; }
            }
            return best;
        }

        /// <summary>Инерция всего тела на опрокидывание (кг·м²): масса × квадрат высоты центра масс над стопами.</summary>
        float TiltInertia => rig.Mass * (rideHeight * rideHeight * 0.6f + 0.02f);

        Vector3 Home(Leg l, Vector3 vh)
        {
            Vector3 h = transform.TransformPoint(l.homeLocal);
            Vector3 c = transform.position;
            h = new Vector3(h.x, c.y - rideHeight, h.z);
            return h + vh * (stepTime * lead);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void Limp()
        {
            foreach (var l in legs) { l.hip.targetLocal = l.hip.StartLocal; l.swinging = false; l.planted = l.Foot; l.target = l.Foot; }
        }

        void Righting(float dt)
        {
            upsideTime += dt;
            flail += dt * flailRate;
            for (int i = 0; i < legs.Length; i++)
            {
                var l = legs[i];
                Vector3 ax = Vector3.Cross(l.restDir, Vector3.up);
                if (ax.sqrMagnitude < 1e-4f) ax = Vector3.right;
                l.hip.targetLocal = Quaternion.AngleAxis(Mathf.Sin(flail + i * 1.7f) * flailAngle, ax.normalized) * l.hip.StartLocal;
                l.swinging = false; l.planted = l.Foot; l.target = l.Foot;
            }
            bool touching = Time.time - rig.LastContactTime < 0.25f;
            if (!touching || upsideTime < 0.5f) return;      // сначала побарахтаться
            Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
            if (axis.sqrMagnitude < 1e-3f) axis = transform.right;
            float ramp = Mathf.Clamp01((upsideTime - 0.5f) / 0.8f);
            root.AddTorque(axis.normalized * rightingTorque * rig.tone * ramp * TiltInertia, ForceMode.Force);
            if (Time.time > nextHop)
            {   // прыжок с кувырком в сторону выпрямления
                root.AddForce(Vector3.up * hopSpeed * rig.tone, ForceMode.VelocityChange);
                root.AddTorque(axis.normalized * rollKick * rig.tone * TiltInertia, ForceMode.Impulse);
                nextHop = Time.time + 1.3f;
            }
        }

        void Update()
        {
            if (!RigDraw.enabled || legs == null || !rig) return;
            int n = 0; Vector3[] sup = new Vector3[legs.Length];
            foreach (var l in legs)
            {
                if (l.hip == null) continue;
                Vector3 hip = l.hip.transform.position;
                RigDraw.Line(hip, l.target, l.swinging ? new Color(1f, 0.85f, 0.1f) : new Color(0.3f, 1f, 0.4f));
                RigDraw.Cross(l.planted, 0.04f, l.grounded ? Color.green : Color.gray);
                if (l.grounded) sup[n++] = l.planted;
            }
            for (int i = 0; i < n; i++) RigDraw.Line(sup[i], sup[(i + 1) % n], Color.white);
            Vector3 com = rig.CenterOfMass;
            Vector3 g = new Vector3(com.x, transform.position.y - rideHeight, com.z);
            RigDraw.Line(com, g, Color.red); RigDraw.Cross(g, 0.05f, Color.red);
            if (desiredVelocity.sqrMagnitude > 0.001f) RigDraw.Line(g, g + desiredVelocity, new Color(0.3f, 0.6f, 1f));
        }
    }
}
