using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ВСКАРАБКИВАНИЕ — подъём выше порога ценой руки. Отдельный блок поверх общего состояния героя.
    ///
    /// Цель ощущения (геймдизайнер): каждый сантиметр выше порога куплен рукой. Держишься — на склоне хозяин;
    /// отпустил — склон забирает своё, но не сразу и у тебя на глазах.
    ///
    /// Правила (одно правило — один шаг):
    ///  1. КРУТОЙ СКЛОН (круче steepAngle 35°, выход при 32°): без опоры тело сползает вниз. Окно slideDelay 0.2 с
    ///     (знак — сразу), дальше разгон slideAccel·sin(угла) до slideMax. Опора = хват, который держит.
    ///  2. КИСТЬ НА КРУТОМ (не зацеп) не держит: видимо съезжает handSlipMax и отпускает.
    ///  3. ЗАЦЕПЫ (ProtoHold: перила, поручни, столбы): курсор рядом с любой частью поручня (holdSnapScreen доля экрана, или точка руки ближе holdSnapWorld) — кисть берёт ближайшую достижимую точку; держит на любом склоне.
    ///     Досягаемость — по 3D (рука до L от плеча), по полу ход короче на столько, насколько зацеп выше плеча.
    ///  4. УСТУП выше порога (до climbUp): кисть ложится на кромку, если верх уступа в досягаемости (3D) и под ним сплошное
    ///     (крышу тоннеля снизу не взять). У кромки жест «на себя» ПОДНИМАЕТ тело (climbPerPx м на пиксель), а не двигает.
    ///     Поднялся настолько, что уступ стал «ступенькой» (≤ maxStep) — тело само заходит наверх, пока держишься (перевал, зелёная вспышка).
    ///     Отпустил до перевала — тело опускается туда, откуда поднималось (не падение с края).
    /// Видимый отклик: тело тёплое — сползает (сразу, даже в окне); фиолетовое — висит на кромке; зелёное — перевал;
    /// кисть жёлтая — на зацепе / над зацепом; красная — съезжает (не держит); линия руки красная — рука держит вес.</summary>
    public partial class HeroProto
    {
        [Header("Вскарабкивание")]
        [Tooltip("Склон круче этого (°) — тело без опоры сползает, кисть не держит.")]
        public float steepAngle = 35f;
        [Tooltip("Выход из «крутого» (°) — гистерезис, без дрожи на границе.")]
        public float steepExit = 32f;
        [Tooltip("Окно перед сползанием (с): успеть перехватиться. Знак — сразу.")]
        public float slideDelay = 0.2f;
        [Tooltip("Разгон сползания (м/с² × sin угла) и предел скорости (м/с).")]
        public float slideAccel = 2.5f, slideMax = 1.5f;
        [Tooltip("Кисть на крутом: скорость съезда (м/с) и сколько съедет до отпускания (м).")]
        public float handSlipSpeed = 0.3f, handSlipMax = 0.06f;
        [Tooltip("Автозахват зацепа: курсор ближе этой доли высоты экрана к ЛЮБОЙ части поручня (даже недосягаемой) — кисть берёт ближайшую точку поручня, до которой достаёт. 0.15 ≈ 160 px на 1080.")]
        [Range(0.02f, 0.4f)] public float holdSnapScreen = 0.15f;
        [Tooltip("То же, когда под курсором хороший пол (пологий и достижимый): меньше — чтобы пол рядом с перилами оставался полом. 0.06 ≈ 65 px на 1080.")]
        [Range(0.02f, 0.4f)] public float holdSnapScreenFloor = 0.06f;
        [Tooltip("Автозахват по полу: точка руки на поверхности ближе этого (м) к достижимой точке поручня — тоже захват.")]
        public float holdSnapWorld = 0.3f;
        [Tooltip("Кисть берёт кромку уступа не выше своего уровня + столько (м). Досягаемость всё равно по 3D.")]
        public float climbUp = 0.8f;
        [Tooltip("Подъём тела у кромки на пиксель жеста «на себя» (м/px). 0.0012: уступ 0.5 — ~110 px, 0.7 — ~280 px.")]
        public float climbPerPx = 0.0012f;
        [Tooltip("Перевал: поднялся достаточно — тело само заходит на уступ к кисти (м/с), пока держишься.")]
        public float mantleSpeed = 0.8f;
        [Tooltip("Как быстро тело опускается назад, если отпустил кромку до перевала (1/с).")]
        public float fallBack = 10f;

        [System.NonSerialized] public ProtoHold gripHold;
        float sinceHold = 1f;
        float gripHoldS, gripL = 0.62f, gripTop, climbLift, slipDist, steepTimer, slideV, crestFlash, slipFlash;
        bool gripSlip, onSteep;
        ProtoHold hoverHold; float hoverS;
        Vector3 bodyNormal = Vector3.up;
        readonly List<ProtoHold> holds = new List<ProtoHold>();

        // ---- чтение для сторожей, бота, модели ----
        public float ClimbLift => climbLift;
        public bool Hanging => climbLift > 0.01f;
        public bool OnSteep => onSteep;
        public bool Sliding => slideV > 0f;
        public float SlideSpeed => slideV;
        public bool GripSlipping => (gripSlip || spinSlip) && mode == Mode.Grip;
        public bool OnHold => gripHold && mode == Mode.Grip;
        /// <summary>Видимая высота тела (с подъёмом на кромке).</summary>
        public float BodyY => groundY + climbLift;
        public List<ProtoHold> Holds => holds;
        public Vector3 ShoulderPos3 => Shoulder3;
        public float HandLevelAt(Vector3 p) => HandLevel(Flat(p));
        public float SteepAngleUnderBody => Vector3.Angle(bodyNormal, Vector3.up);
        public bool DiagFindHold(Vector2 px) => FindHold(px, out _, out _);
        public bool DiagHandOk(Vector3 p) => HandOk(Flat(p));
        public float ClimbNeed => gripTop - groundY - maxStep + 0.03f;

        /// <summary>Уровень, от которого тело меряет «шаг вверх»: пол + подъём на кромке.</summary>
        float BodyLevel => groundY + Mathf.Max(climbLift, 0f);
        float ShoulderY => BodyLevel + shoulderLift;
        Vector3 Shoulder3 { get { Vector3 s = Shoulder; return new Vector3(s.x, ShoulderY, s.z); } }

        void CollectHolds() { holds.Clear(); holds.AddRange(FindObjectsOfType<ProtoHold>()); }

        void ClimbReset()
        {
            gripHold = null; hoverHold = null; climbLift = 0f; gripSlip = false; slipDist = 0f;
            steepTimer = 0f; slideV = 0f; onSteep = false; crestFlash = slipFlash = 0f; gripL = L;
            SpinReset();
        }

        // ---------------- досягаемость ----------------
        /// <summary>Досягаемость по полу в текущем хвате: рука L по 3D; точка выше плеча — по полу ближе.</summary>
        float ComputeGripL()
        {
            if (mode != Mode.Grip) return L;
            float py;
            if (gripHold) py = gripHold.At(gripHoldS).y;
            else if (gripTop > BodyLevel + maxStep + 1e-3f) py = gripTop + handLift;   // кромка уступа
            else return L;                                                             // обычный пол / ступень — как раньше
            float dy = py - ShoulderY;
            if (dy <= 0f) return L;
            return Mathf.Sqrt(Mathf.Max(L * L - dy * dy, 0.0004f));
        }

        /// <summary>Кромка уступа выше порога: под точкой сплошное (это уступ, а не крыша над головой) и верх в досягаемости по 3D.</summary>
        bool LedgeGrabOk(Vector3 p, float top)
        {
            // сплошное от своего уровня: луч вверх изнутри упирается в крышку (нормаль вверх)
            Vector3 o = new Vector3(p.x, BodyLevel + 0.03f, p.z);
            if (!(RayHit(new Ray(o, Vector3.up), false, out _, out var nu) && nu.y > 0.1f)) return false;
            Vector3 h3 = new Vector3(p.x, top + handLift, p.z);
            return (h3 - Shoulder3).magnitude <= L - 0.01f;
        }

        // ---------------- зацепы ----------------
        bool HoldOk(Vector3 P, ProtoHold hd = null)
        {
            if ((P - Shoulder3).magnitude > L - 0.01f) return false;
            Vector3 v = Flat(P) - Shoulder;
            if (v.magnitude > 0.03f && !LadderReach(hd, P))                         // стоя на камне — камень ниже берётся и за спиной (Ledge)
            {
                float a = Vector3.SignedAngle(Face, v, Vector3.up);
                if (a < -sectorLeft - 5f || a > sectorRight + 5f) return false;
            }
            return !ArmThroughBody(Flat(P));
        }

        /// <summary>АВТОЗАХВАТ (автор: «перила плохо чувствуются, приходится наводиться»): поручень выбирается, если курсор рядом
        /// с ЛЮБОЙ его частью на экране (holdSnapScreen) или точка руки на полу рядом с ним (holdSnapWorld). Кисть берёт ближайшую
        /// к курсору точку этого поручня, до которой ДОСТАЁТ (досягаемость, сектор, не через тело) — наводиться точно не надо.</summary>
        bool FindHold(Vector2 px, out ProtoHold best, out float bestS)
        {
            best = null; bestS = 0f;
            Vector3 S3 = Shoulder3;
            bool haveFloor = FloorPoint(px, out var fp);
            // под курсором хороший пол (достижим и не крутой) — поручень берётся только рядом; иначе (склон, кромка, пустота) — издалека
            bool floorGood = false;
            if (haveFloor && HandOk(fp)) { HandHit(fp, out var fm, out var fn); floorGood = Vector3.Angle(fn, Vector3.up) <= steepAngle && !FloorSpins(fm, fp); }   // пол карусели в зоне уноса — не хороший пол
            float bestScore = float.MaxValue;
            foreach (var hd in holds)
            {
                if (!hd) continue;
                float R = SnapPx(hd, floorGood), RW = SnapWorld(hd);              // выступ (Ledge) — радиус меньше поручня
                if ((hd.At(hd.ClosestTo(S3)) - S3).magnitude > L) continue;      // весь поручень дальше руки
                int n = Mathf.Clamp(Mathf.CeilToInt((hd.B - hd.A).magnitude / 0.02f), 8, 200);
                float dAny = float.MaxValue, dOk = float.MaxValue, sOk = -1f, wOk = float.MaxValue;
                for (int i = 0; i <= n; i++)
                {
                    float si = i / (float)n; Vector3 P = hd.At(si);
                    Vector3 sp = cam.WorldToScreenPoint(P);
                    float d = sp.z > 0f ? ((Vector2)sp - px).magnitude : float.MaxValue;
                    if (d < dAny) dAny = d;
                    if (!HoldOk(P, hd)) continue;
                    if (d < dOk) { dOk = d; sOk = si; }
                    if (haveFloor) wOk = Mathf.Min(wOk, Flat(P - fp).magnitude);
                }
                if (sOk < 0f) continue;                                          // ни одной достижимой точки
                bool near = dAny <= R || (!floorGood && wOk <= RW);
                if (!near) continue;
                float score = Mathf.Min(dAny, wOk / RW * R) + SnapBias(hd);      // ближе к курсору — тот поручень и берём; поручень сильнее камня
                if (score < bestScore) { bestScore = score; best = hd; bestS = sOk; }
            }
            return best;
        }

        static float DistRay(Ray r, Vector3 p) { Vector3 e = r.direction.normalized; float t = Mathf.Max(0f, Vector3.Dot(p - r.origin, e)); return (p - (r.origin + e * t)).magnitude; }

        public void BeginGripHold(ProtoHold hd, float s)
        {
            Vector3 P = hd.At(s);
            mode = Mode.Grip; gripHold = hd; gripHoldS = s; gripSlip = false; slipDist = 0f;
            gripHand = Flat(P); hand = gripHand; gripTop = P.y - handLift;
            handMover = hd.GetComponentInParent<ProtoMover>();
            handStep = handMover ? handMover.StepId : 0;
            Vector3 dir = gripHand - Shoulder; dir.y = 0f;
            float cur = dir.magnitude;
            if (cur < 1e-4f) { dir = Face; cur = 0f; } else dir /= cur;
            gripL = ComputeGripL();
            farEnd = gripHand - dir * ArmLenMax(dir, cur);
            nearEnd = gripHand - dir * Mathf.Min(dMin, cur);
            float len = (nearEnd - farEnd).magnitude;
            t = tTarget = Mathf.Clamp01(Vector3.Dot(Shoulder - farEnd, dir) / Mathf.Max(len, 1e-4f));
        }

        /// <summary>Высота, к которой тянется кисть: зацеп (в хвате или под курсором) или поверхность своего уровня.</summary>
        float HandTargetY()
        {
            if (mode == Mode.Grip && gripHold) return gripHold.At(gripHoldS).y - handLift;
            if (mode == Mode.Free && hoverHold) return hoverHold.At(hoverS).y - handLift;
            return HandLevel(hand);
        }

        // ---------------- кисть на крутом не держит ----------------
        void HandSlipStep(float dt)
        {
            if (mode != Mode.Grip || !gripSlip) return;
            HandHit(gripHand, out _, out var n);
            Vector3 down = Flat(n);
            if (down.sqrMagnitude < 1e-6f) { gripSlip = false; return; }      // съехала на пологое — держит
            if (Vector3.Angle(n, Vector3.up) < steepExit) { gripSlip = false; return; }
            Vector3 d = down.normalized * handSlipSpeed * dt;
            gripHand += d; farEnd += d; nearEnd += d; hand = gripHand;
            slipDist += d.magnitude;
            if (slipDist >= handSlipMax) { mode = Mode.Free; gripSlip = false; pendingTurn = 0f; slipFlash = 0.5f; }   // отпустила
        }

        // ---------------- крутой склон без опоры — сползание ----------------
        void SlopeStep(float dt)
        {
            float a = Vector3.Angle(bodyNormal, Vector3.up);
            if (!onSteep && a > steepAngle) onSteep = true;
            else if (onSteep && a < steepExit) onSteep = false;
            bool held = (mode == Mode.Grip && !gripSlip) || onNubFoot;      // стоит на камне — тоже опора (Ledge)
            if (onSteep && !held && !Hanging)
            {
                steepTimer += dt;
                if (steepTimer <= slideDelay) return;
                slideV = Mathf.Min(slideV + slideAccel * Mathf.Sin(a * Mathf.Deg2Rad) * dt, slideMax);
                Vector3 down = Flat(bodyNormal);
                if (down.sqrMagnitude < 1e-6f) return;
                Vector3 np = Slide(Body, down.normalized * slideV * dt);
                if ((np - Body).sqrMagnitude < 1e-10f) slideV = 0f;          // упёрся (стена, стойка) — стоит
                bodyCube.position = new Vector3(np.x, bodyCube.position.y, np.z);
                if (mode == Mode.Grip)
                {
                    // кисть (съезжающая) не удержит тело дальше руки — отпускает
                    if (Flat(gripHand - Shoulder).magnitude > gripL + 0.01f) { mode = Mode.Free; gripSlip = false; pendingTurn = 0f; slipFlash = 0.5f; }
                    else { float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0); }
                }
            }
            else { steepTimer = 0f; slideV = 0f; }
        }

        // ---------------- уступ: подтягивание ----------------
        /// <summary>Хват за кромку уступа выше порога.</summary>
        // кромка или вис на выступе (Ledge). Съезжающая кисть (крутой склон) — не кромка: иначе склон без опоры брался
        // «как уступ» — вис, перевал (зелёная вспышка), и цепочкой хватов по диагонали залезал на 60° без перил (автор, 2026-11-20).
        bool ClimbGrip => (mode == Mode.Grip && !gripHold && !gripSlip && gripTop > groundY + maxStep + 0.005f) || NubHang;

        /// <summary>Тело упёрлось в уступ на пути к кисти (не стена-препятствие, а уступ выше шага).</summary>
        bool AtLedge()
        {
            Vector3 dir = Flat(gripHand - Body);
            if (dir.sqrMagnitude < 1e-6f) return true;
            dir.Normalize();
            for (float s = bodyRadius * 0.8f; s <= bodyRadius + 0.12f; s += 0.04f)
                if (!Climbable(Body + dir * s)) return true;
            return false;
        }

        /// <summary>Жест у кромки (в пикселях вдоль): «на себя» — поднять тело, «от себя» — опустить. true — жест ушёл в подъём.</summary>
        bool ClimbConvert(float px)
        {
            float meters = px * climbPerPx;
            if (!ClimbGrip) return false;
            float need = gripTop - groundY - maxStep + 0.03f;            // поднять настолько, чтобы уступ стал ступенькой
            if (meters > 0f)
            {
                if (climbLift >= need) return false;                      // уже поднялся — дальше обычный ход на уступ
                if (!(OnNub ? NubAtWall() : AtLedge()) && climbLift <= 0.001f) return false;      // ещё не у кромки — едет к ней
                climbLift = Mathf.Min(climbLift + meters, need);
                return true;
            }
            if (climbLift > 0.001f) { climbLift = Mathf.Max(0f, climbLift + meters); return true; }
            return false;
        }

        /// <summary>ПЕРЕВАЛ: поднялся настолько, что уступ стал ступенькой, — тело само заходит на него к кисти (пока держишься).
        /// Иначе короткая рука на высоком уступе (кисть у самой кромки) не довела бы тело за кромку.</summary>
        void MantleStep(float dt)
        {
            if (!ClimbGrip || OnNub) return;                              // через камень не перевалить (Ledge)
            float need = gripTop - groundY - maxStep + 0.03f;
            if (climbLift < need - 1e-3f) return;
            Vector3 dir = Flat(gripHand - Body); float d = dir.magnitude;
            if (d < 0.03f) return;
            dir /= d;
            Vector3 np = Slide(Body, dir * Mathf.Min(mantleSpeed * dt, d - 0.03f));
            bodyCube.position = new Vector3(np.x, bodyCube.position.y, np.z);
            float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0);
        }

        void ClimbDecay(float dt)
        {
            sinceHold = (OnHold || (mode == Mode.Free && hoverHold)) ? 0f : sinceHold + dt;
            if (mode == Mode.Grip) ArmReachStep();
            MantleStep(dt);
            crestFlash = Mathf.Max(0f, crestFlash - dt); slipFlash = Mathf.Max(0f, slipFlash - dt);
            bool holding = ClimbGrip && climbLift > 0f;
            if (holding) return;
            if (Mathf.Abs(climbLift) < 0.002f) { climbLift = 0f; return; }
            climbLift = Mathf.Lerp(climbLift, 0f, 1f - Mathf.Exp(-fallBack * dt));    // назад вниз / докатиться на уступ
        }

        /// <summary>РУКА НЕ ДЛИННЕЕ ДОСЯГАЕМОСТИ: досягаемость по полу меняется с высотой (тело опустилось под высоким зацепом,
        /// кромка выше плеча). Рука подтягивает тело к кисти на излишек; упёрлось (стена, уступ) и всё равно не достаёт — хват срывается.</summary>
        void ArmReachStep()
        {
            gripL = ComputeGripL();
            Vector3 v = Flat(gripHand - Shoulder); float d = v.magnitude;
            float over = d - gripL;
            if (over <= 0.005f || d < 1e-4f) return;
            Vector3 np = Slide(Body, v / d * over);
            bodyCube.position = new Vector3(np.x, bodyCube.position.y, np.z);
            float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0);
            if (Flat(gripHand - Shoulder).magnitude > gripL + 0.03f) { mode = Mode.Free; pendingTurn = 0f; gripHold = null; gripSlip = false; slipFlash = 0.5f; }
        }

        // ---------------- видимый отклик ----------------
        Renderer rBody, rHand; Color cBody, cHand, cArm; bool visInit;

        void ClimbVisuals(float dt)
        {
            if (!visInit)
            {
                visInit = true;
                rBody = bodyCube ? bodyCube.GetComponent<Renderer>() : null; rHand = handCube ? handCube.GetComponent<Renderer>() : null;
                if (rBody) cBody = rBody.sharedMaterial ? rBody.sharedMaterial.color : Color.gray;
                if (rHand) cHand = rHand.sharedMaterial ? rHand.sharedMaterial.color : Color.gray;
                if (lnArm) cArm = lnArm.startColor;
            }
            if (!Application.isPlaying) return;
            bool held = mode == Mode.Grip && !gripSlip;
            Color b = cBody;
            if (crestFlash > 0f) b = new Color(0.35f, 0.9f, 0.4f);
            else if (Hanging) b = Color.Lerp(cBody, new Color(0.65f, 0.35f, 0.85f), 0.8f);
            else if ((onSteep && !held) || (onSpin && !SpinHeld(rideMover)) || Flung)
                b = Color.Lerp(cBody, new Color(1f, 0.45f, 0.1f), 0.5f + 0.5f * Mathf.Max(Mathf.Max(slideV, spinV), flingV.magnitude) / Mathf.Max(slideMax, 0.01f));
            Color h = cHand;
            if (GripSlipping || slipFlash > 0f) h = new Color(1f, 0.1f, 0.1f);
            else if (OnHold || (mode == Mode.Free && hoverHold)) h = new Color(1f, 0.9f, 0.15f);
            if (rBody && rBody.material.color != b) rBody.material.color = b;
            if (rHand && rHand.material.color != h) rHand.material.color = h;
            if (lnArm)
            {
                bool loaded = Hanging || (onSteep && held) || (onSpin && SpinHeld(rideMover));
                lnArm.startColor = lnArm.endColor = loaded ? new Color(1f, 0.25f, 0.2f) : cArm;
            }
            NubVisuals();                                                 // выступ: подсветка камня (Ledge)
        }
    }
}
