using System.Collections.Generic;
using Fortress.Rig.Human;
using Fortress.Rig.React;
using UnityEngine;

namespace Fortress.Rig.V3
{
    /// <summary>Риг v3, слои L2 (отклик) и L4 (равновесие) поверх видимой анимации.
    ///
    /// Тело — 13 частей по костям модели (массы как в v2, инерция по капсулам-зондам на слое RigProbe).
    /// Удар (контакт с предметом, другим человеком, тычок мышью, толчок стенда) → ArticulatedResponse.ApplyImpulseSupported:
    /// Δq̇ = (M⁻¹ − M⁻¹J_cᵀ(J_cM⁻¹J_cᵀ)⁻¹J_cM⁻¹)·Jvᵀ·J — стопы держат пол, гнутся колени, бёдра, позвоночник.
    /// «Мода отклика»: пружина K = ω²M, D = 2ζωM, поэтому форма отклика сохраняется:
    ///   δq(t) = Δq̇·e^(−ζωt)·sin(ω_d t)/ω_d,  удары складываются.
    /// Отклонения хранятся в осях родителя (снимок при ударе) и прибавляются к позе в LateUpdate — от корня к концам, с пределами углов.
    ///
    /// Контакт с предметом считается здесь же (кинематическое тело дало бы бесконечную массу):
    ///   λ = (1+e)·(−vₙ) / (w_тела + w_предмета),  предмету — импульс λn (честный), человеку — отклик −λn.
    /// Быстрые предметы ловятся заранее (прогноз позы на шаг вперёд) — не пролетают сквозь руку.
    /// Тяжёлое (≥ heavyMass) блокирует ход капсулы; лёгкое едет перед человеком.
    /// Человек с человеком — эффективные массы обоих.
    ///
    /// Равновесие: скорость центра масс из отклика → точка захвата ξ = x + ẋ/ω, ω = √(g/z₀).
    ///   ξ в стопе → качнуться (маятник вокруг опоры);  снаружи → отбрасывание капсулы, стопы подшагивают к ξ (HumanAnimate).</summary>
    [DefaultExecutionOrder(50)]
    public class HumanReaction : MonoBehaviour
    {
        [Header("Части тела (собирает RigHumanV3Builder)")]
        public Transform[] bones;
        public Collider[] probes;
        public int[] parent;
        public float[] mass;
        [Tooltip("Предел отклонения сустава (градусы)")] public float[] maxAngle;
        [Tooltip("Тонус сустава: сопротивление за время удара h·D + h²·K (кг·м²)")] public float[] tone;
        [Tooltip("Рука или голова: не блокирует ход о тяжёлое")] public bool[] limb;
        [Tooltip("Кость Hips аватара (здесь «root»): ею двигается всё тело")] public Transform rootBone;
        public int footL = 9, footR = 12;

        [Header("Отклик")]
        [Tooltip("Частота возврата к анимации (Гц)")] public float modeHz = 1.6f;
        [Tooltip("Затухание возврата")] public float modeZeta = 0.5f;
        [Tooltip("Видимость отклика (1 — честно)")] public float gain = 1.8f;
        [Tooltip("Общий тонус: 0 — обмяк, 1 — бодр, 2 — напряжён")] public float toneScale = 1f;
        [Tooltip("Предел сдвига таза (м)")] public float maxShift = 0.2f;
        [Tooltip("Предел поворота таза (градусы)")] public float maxTilt = 25f;
        [Tooltip("Упругость удара с предметом")] public float restitution = 0.25f;
        [Tooltip("Предел импульса одного контакта (Н·с)")] public float maxImpulse = 150f;
        [Tooltip("Предел импульса между людьми (Н·с)")] public float maxPairImpulse = 25f;
        [Tooltip("Сила, с которой человек давит телом в предмет, упираясь (Н): лёгкое сдвигает, тяжёлое — нет")] public float pushForce = 300f;
        readonly Dictionary<Rigidbody, float> bumpTime = new Dictionary<Rigidbody, float>();

        [Header("Равновесие")]
        [Tooltip("Высота центра масс z₀ (м)")] public float comHeight = 1.0f;
        [Tooltip("Полудлина стопы: точка захвата дальше — шаг (м)")] public float footHalf = 0.12f;
        public float swayHz = 0.8f, swayZeta = 0.4f;
        [Tooltip("Видимость качания")] public float swayGain = 1.5f;
        [Tooltip("Предел качания (градусы)")] public float maxSway = 9f;
        [Tooltip("Множитель отбрасывания (шаги к точке захвата)")] public float stepGain = 1.5f;

        // ---- состояние
        ArticulatedResponse R;
        int N;
        Vector3[] comLocal;
        float[] partRadius;
        float totalMass;
        Vector3 dx, dxv, dphi, dphiv;          // корень: сдвиг и поворот (оси модели)
        Vector3[] th, thv;                     // суставы: поворот (оси родителя, снимок при ударе)
        Vector3 sway, swayV;                   // качание: смещение ц.м. над опорой (мир, горизонталь)
        Vector3[] partVel, prevCom;
        bool havePrev;
        double[] dq;
        float builtFixed = -1f; int builtFrame = -1;
        HumanMotor motor;
        HumanAnimate animate;
        HumanGhostIK ik;
        Transform model;
        readonly Collider[] near = new Collider[48];

        static readonly List<HumanReaction> All = new List<HumanReaction>();
        static float pairsAt = -1f;
        static readonly Dictionary<long, float> pairCool = new Dictionary<long, float>();

        // ---- телеметрия
        public float LastImpulse { get; private set; }
        public float LastHitTime { get; private set; } = -99f;
        public string LastWhat { get; private set; } = "";
        public Vector3 LastComDv { get; private set; }
        public int Hits { get; private set; }
        public float SwayDeg => Mathf.Atan2(sway.magnitude * swayGain, comHeight) * Mathf.Rad2Deg;
        /// <summary>Пики с последнего сброса (телеметрия): отклонение сустава и качание, градусы.</summary>
        [System.NonSerialized] public float PeakHold, SwayHold;
        public readonly List<string> Events = new List<string>();
        public HumanMotor Motor => motor;
        /// <summary>Мозг NPC (ход ведут клипы), если есть.</summary>
        public HumanNPC Npc { get; private set; }
        public HumanAnimate Animate => animate;
        public Transform Chest => bones != null && bones.Length > 1 ? bones[1] : transform;
        public Transform Head => bones != null && bones.Length > 2 ? bones[2] : transform;
        /// <summary>Наибольшее текущее отклонение сустава (градусы).</summary>
        public float PeakAngle
        {
            get
            {
                float m = 0f;
                if (th != null) for (int i = 1; i < N; i++) m = Mathf.Max(m, th[i].magnitude);
                return Mathf.Max(m, dphi.magnitude) * Mathf.Rad2Deg;
            }
        }

        void Awake()
        {
            motor = GetComponent<HumanMotor>();
            animate = GetComponentInChildren<HumanAnimate>();
            ik = GetComponentInChildren<HumanGhostIK>();
            Npc = GetComponentInChildren<HumanNPC>();
            model = animate ? animate.transform : transform;
            N = bones.Length;
            comLocal = new Vector3[N];
            partRadius = new float[N];
            var diag = new Vector3[N];
            var rot = new Quaternion[N];
            totalMass = 0f;
            for (int i = 0; i < N; i++)
            {
                totalMass += mass[i];
                rot[i] = Quaternion.identity;
                diag[i] = Inertia(probes[i], mass[i], out comLocal[i], out partRadius[i]);
                if (bones[i] != probes[i].transform) comLocal[i] = bones[i].InverseTransformPoint(probes[i].transform.TransformPoint(comLocal[i]));
            }
            R = new ArticulatedResponse(parent, mass, diag, rot);
            dq = new double[R.Dof];
            th = new Vector3[N]; thv = new Vector3[N];
            partVel = new Vector3[N]; prevCom = new Vector3[N];
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>Главные моменты инерции зонда (оси кости), центр в осях кости и радиус охвата.</summary>
        static Vector3 Inertia(Collider c, float m, out Vector3 center, out float radius)
        {
            float s = c.transform.lossyScale.x;
            switch (c)
            {
                case BoxCollider b:
                {
                    center = b.center;
                    Vector3 d = b.size * s;
                    radius = d.magnitude * 0.5f;
                    return m / 12f * new Vector3(d.y * d.y + d.z * d.z, d.x * d.x + d.z * d.z, d.x * d.x + d.y * d.y);
                }
                case SphereCollider sp:
                {
                    center = sp.center;
                    float r = sp.radius * s;
                    radius = r;
                    float i = 0.4f * m * r * r;
                    return new Vector3(i, i, i);
                }
                case CapsuleCollider cc:
                {
                    center = cc.center;
                    float r = cc.radius * s, h = cc.height * s;
                    radius = Mathf.Max(r, h * 0.5f);
                    float ax = 0.5f * m * r * r, pr = m * (3f * r * r + h * h) / 12f;
                    return cc.direction == 0 ? new Vector3(ax, pr, pr) : cc.direction == 1 ? new Vector3(pr, ax, pr) : new Vector3(pr, pr, ax);
                }
            }
            center = Vector3.zero; radius = 0.15f;
            return Vector3.one * 0.01f * m;
        }

        Vector3 ComW(int i) => bones[i].TransformPoint(comLocal[i]);

        /// <summary>Нога в воздухе: при подшагивании — та, что переносится; иначе — по анимации.</summary>
        bool FootAir(int k)
        {
            if (motor && !motor.Grounded) return true;
            if (animate && animate.StepWeight > 0.5f) return animate.SwingFoot == k;
            return ik && ik.Swing[k];
        }

        /// <summary>Собрать матрицу масс по текущей видимой позе (раз в шаг физики / кадр, только если нужен отклик).</summary>
        void EnsureBuilt()
        {
            if (builtFixed == Time.fixedTime && builtFrame == Time.frameCount) return;
            builtFixed = Time.fixedTime; builtFrame = Time.frameCount;
            for (int i = 0; i < N; i++)
            {
                R.Rot[i] = bones[i].rotation;
                R.Com[i] = ComW(i);
                R.Pivot[i] = i == 0 ? R.Com[0] : bones[i].position;
                R.Impedance[i] = (tone != null && i < tone.Length ? tone[i] : 0.5f) * toneScale;
            }
            R.Build();
            var sup = new List<int>(2);
            if (!FootAir(0)) sup.Add(footL);
            if (!FootAir(1)) sup.Add(footR);
            R.SetSupports(sup.ToArray());
        }

        public int PartOf(Collider c)
        {
            for (int i = 0; i < N; i++) if (probes[i] == c) return i;
            return -1;
        }

        public string PartName(int i) => i >= 0 && i < N ? bones[i].name : "?";

        // ================================================================ удары
        /// <summary>Импульс J (Н·с) в точке p части i: отклик тела и равновесие.</summary>
        public void ApplyImpulse(int i, Vector3 p, Vector3 J, string what)
        {
            if (i < 0 || i >= N || J.sqrMagnitude < 1e-8f) return;
            EnsureBuilt();
            System.Array.Clear(dq, 0, dq.Length);
            R.ApplyImpulseSupported(i, p, J, dq);
            // скорость центра масс (до видимого усиления)
            Vector3 vc = Vector3.zero;
            for (int k = 0; k < N; k++) vc += mass[k] * R.PointVelocity(k, R.Com[k], dq);
            vc /= totalMass;
            AddMode(dq);
            Balance(vc);
            LastImpulse = J.magnitude;
            LastHitTime = Time.time;
            LastComDv = vc;
            LastWhat = what;
            Hits++;
            if (Events.Count < 40) Events.Add($"{what} {J.magnitude:F1}Н·с→{bones[i].name} ц.м.{new Vector2(vc.x, vc.z).magnitude:F2}");
        }

        /// <summary>Тычок: точка p части (по коллайдеру-зонду) получает изменение скорости dv (м/с) вдоль dir.</summary>
        public float Poke(Collider c, Vector3 p, Vector3 dir, float dv)
        {
            int i = PartOf(c);
            if (i < 0) return 0f;
            EnsureBuilt();
            dir.Normalize();
            float w = Mathf.Max(R.InvEffMassSupported(i, p, dir), 1f / 300f);
            Vector3 J = dir * (dv / w);
            ApplyImpulse(i, p, J, "тычок");
            return J.magnitude;
        }

        /// <summary>Толчок в грудь импульсом J (Н·с) — точка на груди со стороны толчка.</summary>
        public void PushChest(Vector3 J, string what = "толчок")
        {
            Vector3 c = ComW(1);
            Vector3 p = c - J.normalized * 0.11f + Vector3.up * 0.05f;
            ApplyImpulse(1, p, J, what);
        }

        void AddMode(double[] q)
        {
            float g = gain;
            dxv += model.InverseTransformDirection(new Vector3((float)q[0], (float)q[1], (float)q[2])) * g;
            dphiv += model.InverseTransformDirection(new Vector3((float)q[3], (float)q[4], (float)q[5])) * g;
            for (int c = 1; c < N; c++)
            {
                int k = R.RotIndex(c);
                Vector3 w = new Vector3((float)q[k], (float)q[k + 1], (float)q[k + 2]);
                thv[c] += Quaternion.Inverse(bones[parent[c]].rotation) * w * g;
            }
        }

        void Balance(Vector3 dv)
        {
            dv.y = 0f;
            // NPC (ход ведут клипы): вся скорость центра масс — мозгу NPC, он сам решит, играть ли клип удара
            bool clipDriven = motor && motor.rootMotion;
            if (clipDriven) motor.AddKnock(dv);
            float w0 = Mathf.Sqrt(-Physics.gravity.y / Mathf.Max(0.3f, comHeight));
            Vector3 v1 = swayV + dv;
            Vector3 xi = sway + v1 / w0;
            if (xi.magnitude <= footHalf) { swayV = v1; return; }
            // точка захвата вне стопы: тело качается до края стопы, остальное уносит капсулу — стопы подшагивают к ξ
            Vector3 edge = xi.normalized * footHalf;
            Vector3 vs = (edge - sway) * w0;
            if (Vector3.Dot(vs, dv) < 0f) vs = Vector3.zero;
            Vector3 excess = v1 - vs;
            swayV = vs;
            if (motor && !clipDriven) motor.AddKnock(excess * stepGain);
        }

        // ================================================================ шаг физики
        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Integrate(dt);
            PeakHold = Mathf.Max(PeakHold, PeakAngle);
            SwayHold = Mathf.Max(SwayHold, SwayDeg);
            Items(dt);
            if (pairsAt != Time.fixedTime) { pairsAt = Time.fixedTime; Pairs(); }
        }

        void Integrate(float dt)
        {
            float w = 2f * Mathf.PI * modeHz, z = modeZeta;
            Osc(ref dx, ref dxv, w, z, dt);
            Osc(ref dphi, ref dphiv, w, z, dt);
            for (int i = 1; i < N; i++) Osc(ref th[i], ref thv[i], w, z, dt);
            // пределы: отклонение не дальше предела, скорость наружу гасится
            Clamp(ref dx, ref dxv, maxShift);
            Clamp(ref dphi, ref dphiv, maxTilt * Mathf.Deg2Rad);
            for (int i = 1; i < N; i++) Clamp(ref th[i], ref thv[i], (maxAngle != null && i < maxAngle.Length ? maxAngle[i] : 40f) * Mathf.Deg2Rad);
            float ws = 2f * Mathf.PI * swayHz;
            Osc(ref sway, ref swayV, ws, swayZeta, dt);
            Clamp(ref sway, ref swayV, comHeight * Mathf.Tan(maxSway * Mathf.Deg2Rad));
        }

        static void Osc(ref Vector3 x, ref Vector3 v, float w, float z, float dt)
        {
            v += (-w * w * x - 2f * z * w * v) * dt;
            x += v * dt;
        }

        static void Clamp(ref Vector3 x, ref Vector3 v, float max)
        {
            float m = x.magnitude;
            if (m <= max || m < 1e-6f) return;
            Vector3 d = x / m;
            x = d * max;
            float out_ = Vector3.Dot(v, d);
            if (out_ > 0f) v -= d * out_;
        }

        // ---------------------------------------------------------------- предметы
        void Items(float dt)
        {
            if (!motor) return;
            Vector3 c0 = ComW(0);
            int n = Physics.OverlapSphereNonAlloc(c0, 1.7f, near, 1 << RigLayers.Body, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                var col = near[k];
                var rb = col.attachedRigidbody;
                if (!rb) continue;
                Vector3 off = rb.isKinematic ? Vector3.zero : rb.linearVelocity * dt;
                Vector3 cpos = col.transform.position, cbc = col.bounds.center;
                Quaternion crot = col.transform.rotation;
                float crad = col.bounds.extents.magnitude;
                for (int i = 0; i < N; i++)
                {
                    Vector3 pc = ComW(i);
                    if ((pc - cbc).magnitude > partRadius[i] + crad + off.magnitude + 0.04f) continue;
                    Vector3 bp = bones[i].position; Quaternion br = bones[i].rotation;
                    bool spec = false;
                    if (!Physics.ComputePenetration(probes[i], bp, br, col, cpos, crot, out var dir, out var depth))
                    {
                        if (off.sqrMagnitude < 1e-6f || !Physics.ComputePenetration(probes[i], bp, br, col, cpos + off, crot, out dir, out depth)) continue;
                        spec = true;
                    }
                    Vector3 nrm = -dir;                // от тела к предмету
                    Vector3 p = Physics.ClosestPoint(spec ? cbc + off : cbc, probes[i], bp, br);
                    Contact(i, col, rb, p, nrm, spec ? 0f : depth);
                }
            }
        }

        void Contact(int i, Collider col, Rigidbody rb, Vector3 p, Vector3 n, float depth)
        {
            bool kin = rb.isKinematic;
            bool heavy = kin || rb.mass >= motor.heavyMass;
            bool isLimb = limb != null && i < limb.Length && limb[i];
            Vector3 vb = partVel[i];
            Vector3 vi = kin ? Vector3.zero : rb.GetPointVelocity(p);
            float vn = Vector3.Dot(vi - vb, n);
            float vbn = Vector3.Dot(vb, n), vin = Vector3.Dot(vi, n);
            if (heavy && !isLimb && vbn > -0.05f) motor.Block(n);
            if (vn < -0.05f)
            {
                EnsureBuilt();
                // кто в кого: предмет летит в тело — тело держится за пол (опоры, удар в опорную стопу «в пол»);
                // тело само входит в предмет — масса тела свободная, без отскока (шаг, а не таран)
                bool bodyDriven = vbn > -vin;
                float wb = bodyDriven ? R.InvEffMass(i, p, n) : R.InvEffMassSupported(i, p, n);
                float wi = kin ? 0f : InvMassAt(rb, p, n);
                if (wb + wi > 1e-5f)
                {
                    float e = restitution;
                    if (col.sharedMaterial) e = Mathf.Max(e, col.sharedMaterial.bounciness);
                    if (-vn < 0.5f || bodyDriven) e = 0f;            // нажим и шаг в предмет — без отскока
                    float lam = Mathf.Min(-(1f + e) * vn / (wb + wi), maxImpulse);
                    // тело само давит в предмет: первый тычок — полный, дальше (пока упирается) — не сильнее толчка ногами
                    if (bodyDriven)
                    {
                        if (bumpTime.TryGetValue(rb, out var tb) && Time.time - tb < 0.5f) lam = Mathf.Min(lam, pushForce * Time.fixedDeltaTime);
                        else bumpTime[rb] = Time.time;
                    }
                    if (!kin) Kick(rb, p, n * lam);
                    ApplyImpulse(i, p, -n * lam, col.attachedRigidbody.name.Replace("(Clone)", ""));
                }
            }
            // лёгкое — едет перед телом: подогнать его скорость в точке к скорости тела и вытолкнуть из тела
            if (!heavy)
            {
                float wi = InvMassAt(rb, p, n);
                vin = Vector3.Dot(rb.GetPointVelocity(p), n);
                // не быстрее самого человека: маховая стопа не «пинает» ящик вперёд
                float carry = Mathf.Min(vbn, Mathf.Max(0f, Vector3.Dot(motor.Velocity, n)) + 0.1f);
                if (carry > 0.05f && carry > vin + 0.02f && wi > 1e-5f) Kick(rb, p, n * ((carry - vin) / wi));
                if (depth > 0.01f) rb.position += n * ((depth - 0.005f) * 0.6f);
            }
        }

        /// <summary>Обратная эффективная масса предмета в точке p вдоль n: 1/m + (r×n)ᵀ I⁻¹ (r×n).</summary>
        static float InvMassAt(Rigidbody rb, Vector3 p, Vector3 n)
        {
            Vector3 r = p - rb.worldCenterOfMass;
            Quaternion q = rb.rotation * rb.inertiaTensorRotation;
            Vector3 l = Quaternion.Inverse(q) * Vector3.Cross(r, n);
            Vector3 I = rb.inertiaTensor;
            float rot = l.x * l.x / Mathf.Max(I.x, 1e-6f) + l.y * l.y / Mathf.Max(I.y, 1e-6f) + l.z * l.z / Mathf.Max(I.z, 1e-6f);
            return 1f / rb.mass + rot;
        }

        /// <summary>Импульс предмету — сразу в скорости (следующий контакт в этом же шаге видит новую скорость).</summary>
        static void Kick(Rigidbody rb, Vector3 p, Vector3 J)
        {
            Vector3 r = p - rb.worldCenterOfMass;
            Quaternion q = rb.rotation * rb.inertiaTensorRotation;
            Vector3 l = Quaternion.Inverse(q) * Vector3.Cross(r, J);
            Vector3 I = rb.inertiaTensor;
            Vector3 dw = q * new Vector3(l.x / Mathf.Max(I.x, 1e-6f), l.y / Mathf.Max(I.y, 1e-6f), l.z / Mathf.Max(I.z, 1e-6f));
            rb.linearVelocity += J / rb.mass;
            rb.angularVelocity += dw;
        }

        // ---------------------------------------------------------------- человек с человеком
        static void Pairs()
        {
            float now = Time.time;
            for (int a = 0; a < All.Count; a++)
                for (int b = a + 1; b < All.Count; b++)
                {
                    var A = All[a]; var B = All[b];
                    Vector3 d = A.transform.position - B.transform.position; d.y = 0f;
                    if (d.magnitude > 1.6f) continue;
                    for (int i = 0; i < A.N; i++)
                    {
                        Vector3 ca = A.ComW(i);
                        for (int j = 0; j < B.N; j++)
                        {
                            Vector3 cb = B.ComW(j);
                            if ((ca - cb).magnitude > A.partRadius[i] + B.partRadius[j] + 0.03f) continue;
                            if (!Physics.ComputePenetration(A.probes[i], A.bones[i].position, A.bones[i].rotation,
                                                            B.probes[j], B.bones[j].position, B.bones[j].rotation, out var dir, out _)) continue;
                            long key = ((long)A.GetHashCode() << 32) ^ ((long)B.GetHashCode() * 1000 + i * 16 + j);
                            if (pairCool.TryGetValue(key, out var t) && now - t < 0.3f) continue;
                            Vector3 n = -dir;               // от A к B
                            Vector3 p = Physics.ClosestPoint(cb, A.probes[i], A.bones[i].position, A.bones[i].rotation);
                            float vn = Vector3.Dot(B.partVel[j] - A.partVel[i], n);
                            if (vn > -0.1f) continue;
                            A.EnsureBuilt(); B.EnsureBuilt();
                            // оба идут сами — массы свободные (опорная стопа не «стена»); импульс — не больше толчка плечом
                            float wa = A.R.InvEffMass(i, p, n), wb = B.R.InvEffMass(j, p, -n);
                            if (wa + wb < 0.01f) continue;
                            float lam = Mathf.Min(-1.1f * vn / (wa + wb), Mathf.Min(A.maxPairImpulse, B.maxPairImpulse));
                            A.ApplyImpulse(i, p, -n * lam, B.name);
                            B.ApplyImpulse(j, p, n * lam, A.name);
                            pairCool[key] = now;
                        }
                    }
                }
        }

        // ================================================================ поза
        void LateUpdate()
        {
            if (!rootBone || N == 0) return;
            // 1) качание: всё тело вокруг опоры (точка на полу под капсулой)
            if (sway.sqrMagnitude > 1e-8f && motor)
            {
                Vector3 G = motor.transform.position;
                float ang = SwayDeg;
                Quaternion q = Quaternion.AngleAxis(ang, Vector3.Cross(Vector3.up, sway.normalized));
                rootBone.SetPositionAndRotation(G + q * (rootBone.position - G), q * rootBone.rotation);
            }
            // 2) корень: сдвиг и поворот таза вокруг его центра масс
            if (dx.sqrMagnitude + dphi.sqrMagnitude > 1e-10f)
            {
                Vector3 piv = ComW(0);
                Quaternion q = RotVec(model.TransformDirection(dphi));
                rootBone.SetPositionAndRotation(piv + q * (rootBone.position - piv) + model.TransformDirection(dx), q * rootBone.rotation);
            }
            // 3) суставы от корня к концам: поворот в осях родителя
            for (int c = 1; c < N; c++)
            {
                if (th[c].sqrMagnitude < 1e-10f) continue;
                Quaternion q = RotVec(bones[parent[c]].rotation * th[c]);
                bones[c].rotation = q * bones[c].rotation;
            }
            // скорости частей видимой позы (для контактов)
            float dt = Time.deltaTime;
            for (int i = 0; i < N; i++)
            {
                Vector3 p = ComW(i);
                partVel[i] = havePrev && dt > 0f ? (p - prevCom[i]) / dt : Vector3.zero;
                if (partVel[i].sqrMagnitude > 400f) partVel[i] = Vector3.zero;   // телепорт
                prevCom[i] = p;
            }
            havePrev = true;
        }

        static Quaternion RotVec(Vector3 v)
        {
            float a = v.magnitude;
            return a < 1e-7f ? Quaternion.identity : Quaternion.AngleAxis(a * Mathf.Rad2Deg, v / a);
        }

        /// <summary>Сбросить отклик (телепорт, перезапуск ситуации).</summary>
        public void ResetState()
        {
            dx = dxv = dphi = dphiv = sway = swayV = Vector3.zero;
            if (th != null) for (int i = 0; i < N; i++) { th[i] = thv[i] = Vector3.zero; }
            havePrev = false;
        }
    }
}
