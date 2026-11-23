using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>КАРУСЕЛЬ — центробежная зона вращающейся платформы. Отдельный блок поверх общего состояния героя.
    ///
    /// Цель ощущения (геймдизайнер): карусель — течение, а не ловушка. Держишься — едешь хозяином; отпустил на быстрой —
    /// она уносит к краю у тебя на глазах и выбрасывает туда, куда ты сам видел заранее.
    ///
    /// Правила (один закон со склоном из «Вскарабкивания»):
    ///  1. ЗОНА: там, где ω²·r выше spinGrip (выход при spinGrip·spinExitK), диск как крутой склон: без опоры тело
    ///     сползает НАРУЖУ. Окно slideDelay (знак — сразу), разгон = ω²·r, предел slideMax. Граница зоны r* = spinGrip/ω²
    ///     нарисована на диске кольцом (30°/с — кольца нет, 60°/с — у самого края, 120°/с — у центра).
    ///  2. ОПОРА: зацеп той же карусели (столб, поручень) держит всегда; кисть на самом диске держит, пока сама вне зоны,
    ///     в зоне — видимо съезжает наружу (handSlipMax) и отпускает (красная). Кисть на неподвижном полу — старое правило
    ///     «разные опоры»: рука не достаёт — хват срывается.
    ///  3. СЛЁТ: сползая, тело сходит с кромки и летит по касательной (скорость с диска + наружу, не быстрее flingMax),
    ///     на полу гаснет (flingDecay). Стена / препятствие — останавливает.</summary>
    public partial class HeroProto
    {
        [Header("Карусель")]
        [Tooltip("Центробежное ускорение (м/с²), выше которого диск без опоры уносит наружу. Как «крутой» у склона.")]
        public float spinGrip = 1.5f;
        [Tooltip("Выход из зоны: ниже spinGrip × это (гистерезис).")]
        public float spinExitK = 0.9f;
        [Tooltip("Слёт с кромки: предел скорости (м/с) и затухание на полу (1/с). 1.5 и 2 → отлёт ~0.75 м.")]
        public float flingMax = 1.5f, flingDecay = 2f;

        bool onSpin, spinSlip;
        float spinTimer, spinV;
        Vector3 carryVel, flingV;
        ProtoMover spinPrevRide; bool spinPrevZone; Vector3 spinPrevVel;

        public bool OnSpin => onSpin;
        public bool SpinSliding => spinV > 0f;
        public bool Flung => flingV.sqrMagnitude > 1e-4f;
        public Vector3 FlingVel => flingV;
        public float SpinAccelHere => rideMover ? rideMover.SpinAccel(Body) : 0f;

        /// <summary>Пол под курсором не держит из-за вращения: точка в зоне уноса своей карусели, или тело само уже в зоне.
        /// Тогда зацепы рядом берутся с большим радиусом, как над склоном (автор: «за камень на карусели ни разу не ухватил»).</summary>
        bool FloorSpins(ProtoMover m, Vector3 p) => spinSnapWide && (onSpin || (m && m.SpinAccel(p) > spinGrip));
        [Tooltip("Над полом карусели в зоне уноса (или в зоне сам) зацепы берутся с большим радиусом, как над склоном.")]
        public bool spinSnapWide = true;

        void SpinReset() { onSpin = spinSlip = false; spinTimer = spinV = 0f; flingV = carryVel = spinPrevVel = Vector3.zero; spinPrevRide = null; spinPrevZone = false; }

        /// <summary>Держит ли опора против течения: зацеп той же карусели или кисть на диске, которая не съезжает.</summary>
        bool SpinHeld(ProtoMover m) => mode == Mode.Grip && m && handMover == m && !spinSlip && !gripSlip;

        /// <summary>Внешняя сила (платформа, унос, слёт) завела руку в тело — хват срывается (тело не крутится сквозь руку).</summary>
        bool ArmCutsBody() => armKeepOut > 0f && mode == Mode.Grip && ArmToBodyGrip(Body, Shoulder) < armKeepOut * 0.7f;

        void ReleaseSpin() => ReleaseGrip("сорвало с диска", true);

        void SpinStep(float dt)
        {
            var m = rideMover;
            // ---- СЛЁТ: в прошлом шаге был в зоне на карусели, теперь сошёл с неё — бросок по касательной
            if (spinPrevRide && m != spinPrevRide && spinPrevZone)
            {
                Vector3 v = spinPrevVel; v.y = 0f;                     // скорость с диска в последнем кадре на нём
                Vector3 outw = Flat(Body - spinPrevRide.transform.position);
                if (outw.sqrMagnitude > 1e-6f) v += outw.normalized * spinV;
                flingV = Vector3.ClampMagnitude(v, flingMax);
            }
            if (Flung)
            {
                Vector3 want = flingV * dt, np = Slide(Body, want);
                if ((np - Body).magnitude < want.magnitude * 0.5f) flingV = Vector3.zero;     // упёрся — стоп
                MoveBodyXZ(np.x, np.z);
                flingV *= Mathf.Exp(-flingDecay * dt);
                if (flingV.magnitude < 0.05f) flingV = Vector3.zero;
                if (mode == Mode.Grip)
                {
                    if (Flat(gripHand - Shoulder).magnitude > ComputeGripL() + 0.01f || ArmCutsBody()) ReleaseSpin();
                    else { float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0); }
                }
            }

            // ---- ЗОНА на этой карусели
            float a = m ? m.SpinAccel(Body) : 0f;
            if (!onSpin && a > spinGrip) onSpin = true;
            else if (onSpin && a < spinGrip * spinExitK) onSpin = false;

            // кисть на самом диске (не зацеп) в зоне — съезжает наружу и отпускает
            if (mode == Mode.Grip && !gripHold && handMover && Mathf.Abs(handMover.Omega) > 1e-3f)
            {
                if (!spinSlip && handMover.SpinAccel(gripHand) > spinGrip) { spinSlip = true; slipDist = 0f; }
                if (spinSlip)
                {
                    Vector3 o = Flat(gripHand - handMover.transform.position);
                    if (o.sqrMagnitude > 1e-6f)
                    {
                        Vector3 d = o.normalized * handSlipSpeed * dt;
                        gripHand += d; farEnd += d; nearEnd += d; hand = gripHand; slipDist += d.magnitude;
                    }
                    if (slipDist >= handSlipMax || Flat(gripHand - Shoulder).magnitude > ComputeGripL() + 0.01f || ArmCutsBody()) ReleaseSpin();
                }
            }
            else spinSlip = false;

            // тело в зоне без опоры — сползает наружу
            if (onSpin && !SpinHeld(m) && !Hanging)
            {
                spinTimer += dt;
                if (spinTimer > slideDelay)
                {
                    spinV = Mathf.Min(spinV + a * dt, slideMax);
                    Vector3 outw = Flat(Body - m.transform.position);
                    if (outw.sqrMagnitude > 1e-6f)
                    {
                        Vector3 np = Slide(Body, outw.normalized * spinV * dt);
                        if ((np - Body).sqrMagnitude < 1e-10f) spinV = 0f;          // упёрся (столб, стена) — стоит
                        MoveBodyXZ(np.x, np.z);
                        if (mode == Mode.Grip)
                        {
                            if (Flat(gripHand - Shoulder).magnitude > ComputeGripL() + 0.01f || ArmCutsBody()) ReleaseSpin();   // рука не удержит — отпустила
                            else { float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0); }
                        }
                    }
                }
            }
            else { spinTimer = 0f; if (!onSpin || SpinHeld(m)) spinV = 0f; }

            spinPrevRide = m; spinPrevZone = onSpin && !SpinHeld(m); spinPrevVel = carryVel;
        }
    }
}
