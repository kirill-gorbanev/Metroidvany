using UnityEngine;

namespace Fortress.Rig.Human
{
    /// <summary>Человек «как Euphoria»: видимое тело — 13 физических частей с мышцами, которые повторяют позу невидимого
    /// «призрака» (анимации библиотеки). Таз притянут к тазу призрака отдельно по высоте, по горизонтали и по повороту.
    /// Удар отпускает притяжение по горизонтали — тело качается; если центр масс ушёл с опоры, призрак подшагивает под него
    /// (ноги переступают); если не удержался — падение: мышцы обмякают, руки выставляются в сторону падения,
    /// потом подъём (анимация LayToIdle, мышцы и притяжение возвращаются постепенно).</summary>
    [DefaultExecutionOrder(-30)]
    public class HumanPuppet : MonoBehaviour
    {
        public enum State { Active, Stagger, Fallen, GettingUp }

        [Header("Связи (ставит сборщик)")]
        public RigBody body;
        public HumanDriver driver;
        public Animator anim;
        public Transform targetPelvis;
        public Rigidbody head, chest;
        public Rigidbody[] feet;
        public RigMuscle[] arms;          // upperarm_l, lowerarm_l, upperarm_r, lowerarm_r
        public RigMuscle[] legs;

        [Header("Притяжение таза к призраку")]
        [Tooltip("Жёсткость притяжения частей к призраку (1/с²)")] public float pinSpring = 160f;
        [Tooltip("Гашение притяжения (1/с)")] public float pinDamp = 18f;
        [Range(0, 1), Tooltip("Доля веса, которую держит притяжение (остальное — ноги)")] public float gravityComp = 0.6f;

        [Header("Равновесие и удары (м/с удара = импульс / масса тела)")]
        [Tooltip("Удар сильнее — качнулся")] public float swayHit = 0.25f;
        [Tooltip("Удар сильнее — пошёл шагать")] public float staggerHit = 0.9f;
        [Tooltip("Удар сильнее — падает сразу")] public float fallHit = 3.2f;
        [Tooltip("Удар предметом (бочка, ящик) сильнее — сбит с ног")] public float fallHitObject = 1.5f;
        [Tooltip("Удар в голову сильнее — падает")] public float fallHitHead = 1.0f;
        [Tooltip("Центр масс дальше от опоры (м) — не удержаться")] public float loseCom = 0.65f;
        [Tooltip("Наклон корпуса (cos) меньше — не удержаться")] public float loseTilt = 0.45f;
        [Tooltip("Шатание дольше — либо устоял, либо упал")] public float maxStagger = 3f;

        public State state { get; private set; } = State.Active;
        public Rigidbody Chest => chest ? chest : head;
        /// <summary>Сила последнего удара (м/с) и когда — для подписи на стенде.</summary>
        public float LastHit { get; private set; }
        public float LastHitTime { get; private set; } = -99f;
        [Tooltip("Тело отстало от призрака больше чем на столько (м) — что-то держит (зацепился): шатается")] public float snagLag = 0.32f;
        [Tooltip("Стопа упёрлась в грань и отстала от своей кости у призрака больше чем на столько (м) — подножка")] public float tripLag = 0.2f;
        /// <summary>Сколько раз споткнулся (для стенда).</summary>
        public int Trips { get; private set; }
        /// <summary>Журнал: почему зашатался / упал (последние 12 записей) — для отладки на стенде.</summary>
        public readonly System.Collections.Generic.List<string> Events = new System.Collections.Generic.List<string>();
        void Log(string s) { Events.Add($"{Time.time:F2} {s}"); if (Events.Count > 12) Events.RemoveAt(0); }
        public string Mode => state switch
        {
            State.Active => "стоит/идёт", State.Stagger => "шатается", State.Fallen => "упал", _ => "встаёт",
        };

        /// <summary>Сидит (на лавке): равновесие не по стопам — толчок качает, но не поднимает на ноги.</summary>
        [System.NonSerialized] public bool seated;
        /// <summary>Предел горизонтального притяжения к призраку (м/с²): толкает тяжёлое — сила ограничена.</summary>
        [System.NonSerialized] public float maxAccelH = float.PositiveInfinity;
        /// <summary>Налегает на вещь (толкает): тело может отставать от призрака — это не «зацепился».</summary>
        [System.NonSerialized] public bool allowLag;
        /// <summary>Упирается руками в стену (для стенда).</summary>
        public bool Bracing { get; private set; }
        HumanGhostIK ik;

        [Header("Шаг")]
        [Tooltip("Нога в переносе тянется за анимацией сильнее во столько раз (и не держит пол трением)")] public float swingBoost = 3f;
        PhysicsMaterial footGrip, footSlick;
        Collider[] footCols;
        readonly bool[] footSwing = new bool[2];

        float pinH = 1f, pinV = 1f, pinR = 1f;      // горизонталь, высота, поворот
        float brakeH = 1f;                          // гашение горизонтальной скорости к скорости призрака
        float stateTime, still, getUpLen = 1.5f;
        [Tooltip("Анимация подъёма: насколько таз лежащего впереди корня (м, замер клипа LayToIdle)")] public float getUpHips = 1.0f;
        Vector3 fallDir;
        static readonly int MoveState = Animator.StringToHash("Move");
        static readonly int GetUpState = Animator.StringToHash("LayToIdle");

        Rigidbody pelvis => body.Root;

        Vector3[] armRest;

        void Awake()
        {
            if (!GetComponent<HumanInteract>()) gameObject.AddComponent<HumanInteract>();
        }

        void Start()
        {
            body.Hurt += OnHurt;
            ik = driver.GetComponentInChildren<HumanGhostIK>();
            // стопы: в опоре — цепкие, в переносе — скользкие (под весом тела стопа иначе «прилипает» к полу)
            footCols = new Collider[feet.Length];
            for (int i = 0; i < feet.Length; i++) footCols[i] = feet[i].GetComponent<Collider>();
            footGrip = footCols.Length > 0 && footCols[0] ? footCols[0].sharedMaterial : null;
            footSlick = new PhysicsMaterial("FootSwing") { dynamicFriction = 0.05f, staticFriction = 0.05f, frictionCombine = PhysicsMaterialCombine.Minimum, bounciness = 0f };
            // направление плеча к локтю в покое (в осях тела крепления) — для рефлекса «руки вперёд»
            armRest = new Vector3[arms.Length];
            for (int i = 0; i + 1 < arms.Length; i += 2)
                armRest[i] = arms[i].Ref.InverseTransformDirection(arms[i + 1].transform.position - arms[i].transform.position).normalized;
            // ноги призрака (CharacterController) не толкаются о собственное тело
            var cc = driver.GetComponent<CharacterController>();
            foreach (var c in body.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(cc, c);
            // сразу встать в позу призрака (физика стартует не из Т-позы)
            if (anim) anim.Update(0f);
            SnapToTarget();
        }

        void OnDestroy() { if (body) body.Hurt -= OnHurt; }

        /// <summary>Скопировать позу призрака в физические части (без скоростей).</summary>
        public void SnapToTarget()
        {
            foreach (var m in body.Muscles)
            {
                if (!m.follow) continue;
                m.transform.SetPositionAndRotation(m.follow.position, m.follow.rotation);
                m.Rb.linearVelocity = Vector3.zero; m.Rb.angularVelocity = Vector3.zero;
            }
            pelvis.transform.SetPositionAndRotation(targetPelvis.position, targetPelvis.rotation);
            pelvis.linearVelocity = Vector3.zero; pelvis.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        void OnHurt(GameObject part, float dv, Vector3 dir)
        {
            if (dv > 0.05f) { LastHit = dv; LastHitTime = Time.time; }
            if (state == State.Fallen || state == State.GettingUp) return;
            if (seated)
            {
                // сидя: качнуло; очень сильно — свалился с лавки
                if (dv >= fallHit * 0.8f || (body.FromCollision && dv >= fallHitObject * 1.3f)) { Fall(); return; }
                if (dv >= swayHit) { pinH = Mathf.Min(pinH, 0.3f); pinR = Mathf.Min(pinR, 0.5f); }
                return;
            }
            // удар предметом: притяжение к призраку «съело» толчок — вернуть его телу, раз стойка сбита
            if (body.FromCollision && dv >= swayHit && dir.sqrMagnitude > 0.5f)
                foreach (var b in body.Bodies) b.AddForce(Flat(dir).normalized * dv * 0.6f, ForceMode.VelocityChange);
            if (dv >= fallHit || (body.FromCollision && dv >= fallHitObject) || (head && part == head.gameObject && dv >= fallHitHead)) { Fall(); return; }
            if (dv >= staggerHit) { Enter(State.Stagger); pinH = 0f; brakeH = 0.15f; return; }
            if (dv >= swayHit) { pinH = Mathf.Min(pinH, 0.25f); pinR = Mathf.Min(pinR, 0.6f); }
        }

        void Enter(State s) { state = s; stateTime = 0f; still = 0f; }

        public void Fall()
        {
            Vector3 v = pelvis.linearVelocity; v.y = 0f;
            fallDir = v.sqrMagnitude > 0.01f ? v.normalized : Flat(pelvis.transform.position - SupportCenter()).normalized;
            Enter(State.Fallen);
            pinH = pinV = pinR = brakeH = 0f;
            body.Stun(1f, 1.2f);
            driver.frozen = true;
            driver.overrideVelocity = null;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        Vector3 SupportCenter()
        {
            Vector3 s = Vector3.zero;
            foreach (var f in feet) s += f.worldCenterOfMass;
            return s / feet.Length;
        }

        float Upright => Vector3.Dot((head.position - pelvis.position).normalized, Vector3.up);

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            stateTime += dt;
            Vector3 com = body.CenterOfMass;
            Vector3 sup = SupportCenter();
            Vector3 off = Flat(com - sup);
            float speed = Flat(pelvis.linearVelocity).magnitude;

            switch (state)
            {
                case State.Active:
                    pinH = Mathf.MoveTowards(pinH, 1f, 1.5f * dt);
                    brakeH = Mathf.MoveTowards(brakeH, 1f, 2f * dt);
                    pinV = Mathf.MoveTowards(pinV, 1f, 2f * dt);
                    pinR = Mathf.MoveTowards(pinR, 1f, 1.5f * dt);
                    if (Bracing) ReleaseArms();
                    if (seated) break;
                    if (off.magnitude > loseCom * 0.55f && pinH < 0.5f) { Log($"com off={off.magnitude:F2}"); Enter(State.Stagger); }
                    // зацепился ногой / упёрся: призрак ушёл, а тело держит препятствие — отпустить и ловить равновесие
                    if (!allowLag && Flat(targetPelvis.position - pelvis.position).magnitude > snagLag) { Log($"snag lag={Flat(targetPelvis.position - pelvis.position).magnitude:F2} at {pelvis.position:F2}"); pinH = 0f; brakeH = 0.15f; Enter(State.Stagger); }
                    // подножка: стопа зацепилась (отстала от анимации), а призрак идёт — корпус летит дальше по инерции
                    else if (driver.Velocity.sqrMagnitude > 0.16f && stateTime > 0.6f)
                        for (int fi = 0; fi < feet.Length; fi++)
                        {
                            var f = feet[fi];
                            var fm = f.GetComponent<RigMuscle>();
                            // зацеп: нога (стопа или голень) упёрлась в крутую грань навстречу ходу (бревно, подступёнок),
                            // и стопа отстала от анимации позади — а тело идёт дальше
                            Vector3 walk = Flat(driver.Velocity).normalized;
                            Vector3 lagV = fm && fm.follow ? Flat(fm.follow.position - f.position) : Vector3.zero;
                            // (на спуске стопа просто не успела вниз: цель ниже стопы — это не зацеп)
                            bool caught = fm && fm.follow && fm.follow.position.y > f.position.y - 0.08f &&
                                          Blocked(fi, walk) && Vector3.Dot(lagV, walk) > tripLag &&
                                          Flat(pelvis.linearVelocity).magnitude > 0.45f;
                            // зацеп держится — не мимолётное касание кромки
                            caughtFor[fi] = caught ? caughtFor[fi] + dt : 0f;
                            if (caughtFor[fi] > 0.06f)
                            {
                                caughtFor[0] = caughtFor[1] = 0f;
                                Trips++;
                                Log($"trip {blockedBy} lag={Flat(fm.follow.position - f.position).magnitude:F2} foot={f.position:F2} ghost={fm.follow.position:F2}");
                                pinH = 0f; brakeH = 0f;
                                var lean = Flat(driver.Velocity).normalized;
                                foreach (var b in body.Bodies) if (b.position.y > pelvis.position.y - 0.05f) b.AddForce(lean * 1.2f, ForceMode.VelocityChange);
                                Enter(State.Stagger);
                                break;
                            }
                        }
                    break;

                case State.Stagger:
                {
                    // точка равновесия: центр масс + скорость × √(h/g); призрак подшагивает туда (ноги переступают),
                    // тело не тянется к призраку, а только тормозится к его скорости — без «разгона» друг другом
                    Vector3 vel = Flat(pelvis.linearVelocity);
                    Vector3 cp = Flat(com) + vel * 0.3f;
                    Vector3 toCp = cp - Flat(driver.transform.position);
                    // шаги ловли равновесия быстро гаснут (~1 с), тормоз крепнет: толчок — это шаг-два, а не пробежка
                    float calm = Mathf.Clamp01(1f - stateTime / 1.0f);
                    driver.overrideVelocity = Vector3.ClampMagnitude(toCp * 5f, 2.2f * calm);
                    pinH = stateTime > 0.6f ? Mathf.MoveTowards(pinH, 0.5f, 1f * dt) : 0f;
                    brakeH = Mathf.MoveTowards(brakeH, stateTime > 0.25f ? 0.9f : 0.3f, 3f * dt);
                    pinV = Mathf.MoveTowards(pinV, 0.9f, 2f * dt);
                    // стена по ходу — упереться в неё руками
                    Brace(vel);
                    bool tooFar = (off.magnitude > loseCom && !Bracing) || Upright < loseTilt;
                    if (tooFar && stateTime > 0.2f) { Log($"fall: off={off.magnitude:F2} up={Upright:F2} brace={Bracing}"); Fall(); break; }
                    still = (toCp.magnitude < 0.2f && vel.magnitude < 0.3f) ? still + dt : 0f;
                    if ((still > 0.3f && stateTime > 0.4f) || stateTime > maxStagger)
                    {
                        driver.overrideVelocity = null;
                        if (Bracing) ReleaseArms();
                        Enter(State.Active);
                    }
                    break;
                }

                case State.Fallen:
                    body.tone = Mathf.Min(body.tone, stateTime < 0.9f ? 0.25f : 0.12f);   // лежит обмякший, не «доской»
                    Reflex(stateTime < 0.9f);
                    still = speed < 0.25f && pelvis.angularVelocity.magnitude < 1.2f ? still + dt : 0f;
                    if (still > 0.8f && stateTime > 1.6f) GetUp();
                    break;

                case State.GettingUp:
                    float k = Mathf.Clamp01(stateTime / getUpLen);
                    body.tone = Mathf.Max(body.tone, Mathf.Lerp(0.35f, 1f, k));
                    pinV = Mathf.SmoothStep(0f, 1f, (k - 0.2f) / 0.6f);
                    pinR = pinV; pinH = pinV; brakeH = pinV;
                    if (stateTime > getUpLen + 0.2f)
                    {
                        driver.frozen = false;
                        anim.CrossFadeInFixedTime(MoveState, 0.25f);
                        Enter(State.Active);
                    }
                    break;
            }
            if (ik) ik.feetWeight = Mathf.MoveTowards(ik.feetWeight, state == State.Active || state == State.Stagger ? 1f : 0f, 4f * dt);
            bool walking = (state == State.Active || state == State.Stagger) && !seated;
            for (int i = 0; i < feet.Length && i < 2; i++)
            {
                bool sw = walking && ik && ik.Swing[i] && ik.feetWeight > 0.5f;
                if (sw != footSwing[i] && footCols[i]) footCols[i].sharedMaterial = sw ? footSlick : footGrip;
                footSwing[i] = sw;
            }
            Pin(dt);
        }

        RigPart[][] legParts;
        readonly float[] caughtFor = new float[2];
        string blockedBy;
        /// <summary>Нога fi (стопа или голень) только что упиралась в крутую грань, обращённую против хода.</summary>
        bool Blocked(int fi, Vector3 walk)
        {
            if (legParts == null)
            {
                legParts = new RigPart[feet.Length][];
                for (int i = 0; i < feet.Length; i++)
                {
                    var fp = feet[i].GetComponent<RigPart>();
                    var calf = feet[i].GetComponent<ConfigurableJoint>()?.connectedBody;
                    var cp = calf ? calf.GetComponent<RigPart>() : null;
                    legParts[i] = new[] { fp, cp };
                }
            }
            foreach (var p in legParts[fi])
                if (p && Time.time - p.SteepTime < 0.08f && Vector3.Dot(Flat(p.SteepNormal), walk) < -0.3f) { blockedBy = p.name + "→" + p.SteepWhat + " n=" + p.SteepNormal.ToString("F2"); return true; }
            return false;
        }

        /// <summary>Стена (неподвижный мир) по ходу в пределах вытянутых рук — ладони на стену, локти чуть согнуты.</summary>
        void Brace(Vector3 vel)
        {
            Vector3 d = Flat(vel);
            if (d.sqrMagnitude < 0.04f) return;
            d.Normalize();
            var c = Chest;
            if (Physics.SphereCast(c.position, 0.12f, d, out var h, 1.05f, RigLayers.WorldMask, QueryTriggerInteraction.Ignore) && Mathf.Abs(h.normal.y) < 0.5f)
            {
                Bracing = true;
                for (int i = 0; i + 1 < arms.Length; i += 2)
                {
                    var up = arms[i]; var lo = arms[i + 1];
                    up.overrideActive = true; up.toneOverride = 0.9f;
                    lo.overrideActive = true; lo.toneOverride = 0.75f;
                    Vector3 sh = up.transform.position;
                    Vector3 lateral = Vector3.ProjectOnPlane(sh - c.position, h.normal);
                    Vector3 target = h.point + lateral * 1.3f + Vector3.up * 0.05f;
                    up.Aim(armRest[i], (target - sh).normalized);
                    lo.targetLocal = lo.StartLocal;
                }
            }
        }

        void ReleaseArms()
        {
            foreach (var m in arms) { m.overrideActive = false; m.toneOverride = -1f; }
            Bracing = false;
        }

        /// <summary>Руки в сторону падения и чуть вниз (поймать себя), пока летит; потом отпустить.</summary>
        void Reflex(bool on)
        {
            // падает на стену — сначала упереться в неё
            if (on && stateTime < 0.5f) { Brace(fallDir * 2f); if (Bracing) return; }
            for (int i = 0; i < arms.Length; i++)
            {
                var m = arms[i];
                m.overrideActive = on;
                m.toneOverride = on ? 0.55f : -1f;
                if (!on) continue;
                if (i % 2 == 0) m.Aim(armRest[i], (fallDir + Vector3.down * 0.6f).normalized);
                else m.targetLocal = m.StartLocal;    // предплечья прямые
            }
        }

        void GetUp()
        {
            ReleaseArms();
            seated = false;
            // лёжа ничком — сначала перевернуться на спину (подъём из анимации — со спины)
            Vector3 chestFwd = pelvis.transform.forward;
            Vector3 headDir = Flat(head.position - pelvis.position);
            Vector3 feetDir = headDir.sqrMagnitude > 1e-4f ? -headDir.normalized : -pelvis.transform.forward;
            var p = pelvis.position; p.y = Mathf.Max(0f, SupportCenter().y - 0.1f);
            if (Physics.Raycast(pelvis.position + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, RigLayers.WorldMask, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y;
            // анимация подъёма начинается лёжа на спине: таз — на 1 м впереди корня, ноги — вперёд
            Vector3 rootAt = new Vector3(p.x, p.y, p.z) - feetDir * getUpHips;
            driver.Place(rootAt, Quaternion.LookRotation(feetDir, Vector3.up));
            anim.Play(GetUpState, 0, 0f);
            anim.Update(0f);
            getUpLen = 1.5f;
            Enter(State.GettingUp);
        }

        Transform[] pinTargets;
        Vector3[] pinLast;
        int[] pinFoot;          // индекс стопы для тела (−1 — не стопа)

        /// <summary>Притяжение к призраку «рукой бога»: каждую физическую часть тянет к её кости у призрака одинаковым
        /// ускорением (масса части не важна — устойчиво для лёгких и тяжёлых). По горизонтали и по высоте — отдельно:
        /// удар отпускает горизонталь (тело качается), падение — всё.</summary>
        void Pin(float dt)
        {
            if (pinTargets == null)
            {
                pinTargets = new Transform[body.Bodies.Length];
                pinLast = new Vector3[body.Bodies.Length];
                pinFoot = new int[body.Bodies.Length];
                for (int i = 0; i < body.Bodies.Length; i++)
                {
                    var rb = body.Bodies[i];
                    pinFoot[i] = System.Array.IndexOf(feet, rb);
                    var m = rb.GetComponent<RigMuscle>();
                    pinTargets[i] = rb == pelvis ? targetPelvis : (m ? m.follow : null);
                    if (pinTargets[i]) pinLast[i] = pinTargets[i].position;
                }
            }
            if (pinH <= 0.001f && pinV <= 0.001f && brakeH <= 0.001f)
            {
                for (int i = 0; i < pinTargets.Length; i++) if (pinTargets[i]) pinLast[i] = pinTargets[i].position;
                return;
            }
            float g = -Physics.gravity.y;
            for (int i = 0; i < pinTargets.Length; i++)
            {
                var t = pinTargets[i];
                if (!t) continue;
                var rb = body.Bodies[i];
                Vector3 tp = t.position;
                Vector3 vt = (tp - pinLast[i]) / dt;
                pinLast[i] = tp;
                Vector3 dx = tp - rb.position;
                Vector3 dv = vt - rb.linearVelocity;
                // нога в переносе: сильнее за анимацией и с полной поддержкой веса — поднимается над ступенью, не волочится
                int fi = pinFoot[i];
                bool sw = fi >= 0 && fi < 2 && footSwing[fi];
                float ks = sw ? pinSpring * swingBoost : pinSpring, kd = sw ? pinDamp * Mathf.Sqrt(swingBoost) : pinDamp;
                float gc = sw ? 1f : gravityComp;
                Vector3 a = dx * ks + dv * kd;
                Vector3 ah = new Vector3(dx.x, 0f, dx.z) * (ks * pinH) + new Vector3(dv.x, 0f, dv.z) * (kd * Mathf.Max(pinH, brakeH));
                if (maxAccelH < 1e4f) ah = Vector3.ClampMagnitude(ah, maxAccelH);
                float av = (a.y + g * gc) * pinV;
                rb.AddForce(ah + Vector3.up * av, ForceMode.Acceleration);
            }
        }

        void Update()
        {
            if (!RigDraw.enabled || !body) return;
            Vector3 com = body.CenterOfMass, sup = SupportCenter();
            RigDraw.Cross(new Vector3(com.x, sup.y, com.z), 0.06f, Color.red);
            RigDraw.Line(com, new Vector3(com.x, sup.y, com.z), Color.red);
            RigDraw.Cross(sup, 0.08f, Color.white);
            foreach (var f in feet) RigDraw.Line(sup, f.worldCenterOfMass, Color.white);
            if (targetPelvis) RigDraw.Line(pelvis.position, targetPelvis.position, new Color(0.3f, 0.6f, 1f));
        }
    }
}
