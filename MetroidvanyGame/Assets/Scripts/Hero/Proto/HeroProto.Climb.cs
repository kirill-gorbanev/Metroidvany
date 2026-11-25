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
        [Tooltip("Подъём тела у кромки на пиксель жеста «на себя» (м/px при высоте экрана 1080; на другой высоте — в той же доле экрана). 0.0012: уступ 0.5 — ~110 px, 0.7 — ~280 px.")]
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
        /// <summary>До какой высоты поднимает «на себя» у кромки / на камне: уступ — пока не станет ступенькой;
        /// камень только для руки (Ledge) — пока кисть не у пояса (nubHangKeep).</summary>
        public float ClimbNeed => OnNub ? gripTop - groundY - nubHangKeep : gripTop - groundY - maxStep + 0.03f;

        /// <summary>Уровень, от которого тело меряет «шаг вверх»: пол + подъём на кромке.</summary>
        float BodyLevel => groundY + Mathf.Max(climbLift, 0f);
        float ShoulderY => BodyLevel + shoulderLift;
        Vector3 Shoulder3 { get { Vector3 s = Shoulder; return new Vector3(s.x, ShoulderY, s.z); } }

        void CollectHolds()
        {
            holds.Clear(); holds.AddRange(FindObjectsOfType<ProtoHold>());
            // порядок — по имени и месту, а не по внутренним номерам Unity (они меняются при пересборке сцены): в ничьих хват
            // и бот выбирают одно и то же — прогоны сравнимы с эталоном и после пересборки арены
            holds.Sort((a, b) => { int c = string.CompareOrdinal(a.name, b.name); if (c != 0) return c; Vector3 pa = a.transform.position, pb = b.transform.position;
                c = pa.x.CompareTo(pb.x); if (c != 0) return c; c = pa.z.CompareTo(pb.z); return c != 0 ? c : pa.y.CompareTo(pb.y); });
        }

        void ClimbReset()
        {
            gripHold = null; hoverHold = null; climbLift = 0f; gripSlip = false; slipDist = 0f;
            steepTimer = 0f; slideV = 0f; onSteep = false; crestFlash = slipFlash = 0f; gripL = L;
            SpinReset();
            falling = false; stunT = 0f; fallPx = 0f; fallVy = 0f;   // перенос тела — полёт и оглушение прерваны (Fall)
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
            bool wallSolid = WallTopAt(p) >= top - 0.001f;                           // верх стены (тумба) — сплошное от пола
            if (!wallSolid && !(RayHit(new Ray(o, Vector3.up), false, out _, out var nu) && nu.y > 0.1f)) return false;
            Vector3 h3 = new Vector3(p.x, top + handLift, p.z);
            return (h3 - Shoulder3).magnitude <= L - 0.01f;
        }

        // ---------------- зацепы ----------------
        bool HoldOk(Vector3 P, ProtoHold hd = null)
        {
            if ((P - Shoulder3).magnitude > L - 0.01f) return false;
            if (SegCutsSurface(Shoulder3, P, true, armCutDepth)) return false;                   // ниже плеча через край — не дотянуться (лимит учитывает поверхность)
            Vector3 v = Flat(P) - Shoulder;
            if (v.magnitude > 0.03f)                                                // зона — без исключений (блок Zone): камень у ног — только лицом к нему
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
            if (mode == Mode.Free && (dragItem || hoverItem)) return ItemHandY();       // кисть на предмете (блок Drag)
            if (mode == Mode.Free && hoverHold) return hoverHold.At(hoverS).y - handLift;
            float hl = HandLevel(hand);
            return mode != Mode.Free || float.IsNaN(handRiseTo) || float.IsInfinity(hl) ? hl : Mathf.Max(hl, handRiseTo);   // у бока тумбы — к её верху
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
            if (slipDist >= handSlipMax) ReleaseGrip("кисть съехала по крутому", true);   // отпустила
        }

        // ---------------- крутой склон без опоры — сползание ----------------
        // (окно перехвата 0.5 с после камня убрано — автор: «слишком долго зависаю»; после камня — то же окно склона slideDelay, что и без хвата)
        void SlopeStep(float dt)
        {
            float a = Vector3.Angle(bodyNormal, Vector3.up);
            if (!onSteep && a > steepAngle) onSteep = true;
            else if (onSteep && a < steepExit) onSteep = false;
            bool held = mode == Mode.Grip && !gripSlip;
            if (onSteep && !held && !Hanging)
            {
                steepTimer += dt;
                if (steepTimer <= slideDelay) return;
                slideV = Mathf.Min(slideV + slideAccel * Mathf.Sin(a * Mathf.Deg2Rad) * dt, slideMax);
                Vector3 down = Flat(bodyNormal);
                if (down.sqrMagnitude < 1e-6f) return;
                Vector3 np = Slide(Body, down.normalized * slideV * dt);
                if ((np - Body).sqrMagnitude < 1e-10f) { if (SlideOffEdge(down, slideV)) return; slideV = 0f; }   // упёрся: край обрыва — падение (Fall), стена — стоит
                MoveBodyXZ(np.x, np.z);
                if (mode == Mode.Grip)
                {
                    // кисть (съезжающая) не удержит тело дальше руки — отпускает
                    if (Flat(gripHand - Shoulder).magnitude > gripL + 0.01f) ReleaseGrip("тело сползло дальше руки", true);
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
                if (TooHighAt(Body + dir * s)) return true;                  // кромка — уступ выше порога; обрыв (щель) — не кромка
            return false;
        }

        /// <summary>КАМЕНЬ НА СКЛОНЕ ТЯНЕТ ВВЕРХ ПО СКЛОНУ (автор 2026-11-21: «с 3 тянусь к 4 — меня резко дёргает вниз на 2, будто 2 твёрдый»).
        /// Раньше «на себя» на камне поднимало тело вертикально над склоном (вис); отпустил камень, чтобы взять следующий, —
        /// висевшее тело падало на склон рывком. Теперь на склоне тело подтягивается к камню ВДОЛЬ склона (по полу к кисти, высота —
        /// склон под ним): всё время лежит на склоне, только выше. Отпустил — сползает по правилу склона (окно slideDelay, потом разгон),
        /// успеваешь перехватиться. Стоп: камень у пояса (nubHangKeep), рука согнута до dMin (по 3D), стена / уступ, край зоны руки.
        /// Отвесная стена (не склон) — по-прежнему вис вертикально.</summary>
        /// <summary>К камню можно двигаться по поверхности (склон, подножие склона): впереди, к кисти, — поверхность не выше порога шага.
        /// Нет (отвесная стена, уступ) — вис вертикально, как раньше. (Автор: «фиолетовый — значит не прилип»: у подножия под телом
        /// ровный пол, и раньше включался вис в воздухе вместо тяги по склону.)</summary>
        bool NubPathAlongSurface()
        {
            Vector3 to = Flat(GripPoint3) - Body; float dist = to.magnitude;
            if (dist < 0.02f) return false;
            Vector3 q = Body + to / dist * Mathf.Min(0.1f, dist);       // сразу перед телом (у самого камня склон уже выше шага)
            float h = LevelHeight(q, BodyLevel, maxStep);
            if (float.IsInfinity(h) || h <= groundY - maxDrop) return false;
            // КАМЕНЬ НА СТЕНЕ — ВИС, НЕ ТЯГА (автор 2026-11-23: «не могу вертикально зависнуть»): камень торчит из стены, и точка в 10 см
            // перед телом ещё на полу — включалась тяга «по склону», которой некуда идти. Путь от тела до места крепления камня
            // (чуть за ним) — непрерывная поверхность (склон) — тяга; стена (скачок выше порога) — вис.
            return ProbeOk(Body, Flat(GripPoint3) + to / dist * 0.08f);
        }

        void NubClimbAlongSlope(float meters)
        {
            Vector3 P = GripPoint3, to = Flat(P) - Body;
            float dist = to.magnitude;
            if (dist < 0.02f) { pullWhy = "тяга по склону: кисть над телом"; return; }
            // крутизна — поверхности ВПЕРЕДИ, к камню (у подножия под телом ещё ровный пол)
            Vector3 probe = Body + to / dist * Mathf.Min(0.1f, dist);
            LevelHit(probe, BodyLevel, maxStep, out _, out var nAhead);
            // у подножия перед телом ещё ровный пол — наклон склона берём дальше, до камня (иначе «вверх по склону» не определено)
            for (float pd = 0.2f; Vector3.Angle(nAhead, Vector3.up) < 5f && pd <= Mathf.Min(0.5f, dist + 0.2f); pd += 0.1f)
                LevelHit(Body + to / dist * pd, BodyLevel, maxStep, out _, out nAhead);
            float ang = Mathf.Max(Vector3.Angle(nAhead, Vector3.up), Vector3.Angle(bodyNormal, Vector3.up));
            float run = meters / Mathf.Max(0.3f, Mathf.Tan(ang * Mathf.Deg2Rad));   // подъём на meters по склону = столько по полу
            // ВВЕРХ ПО СКЛОНУ (а не прямо в камень): камни сбоку твёрдые — тело идёт между колонками и скользит вдоль камней (Slide)
            Vector3 upDir = -Flat(nAhead); if (upDir.sqrMagnitude < 1e-4f) upDir = to; upDir.Normalize();
            if (Vector3.Dot(upDir, to) < 0f) { pullWhy = "тяга по склону: камень ниже по склону"; return; }   // тянуть вверх незачем
            // ОБХОД КАМНЯ (автор 2026-11-21: «не могу подняться наверх» — тело под колонкой, камень прямо впереди, стенка поперёк хода,
            // скольжению некуда): прямо вверх упёрлось — пробуем под 30° и 60°, сначала в сторону кисти, потом в другую;
            // берём первый ход, который поднимает тело (уходит из-за камня в щель между колонками)
            // К РУКЕ (автор 2026-11-23: «на земле могу согнуть руку целиком, на склоне — нет»; «тянуть к руке»): ход — к кисти вдоль
            // поверхности, как на земле, а не строго вверх по склону; не вышло — ±30° / ±60° от направления к кисти, сначала вверх по склону
            Vector3 toDir = to / dist;
            float sideSign = Mathf.Sign(Vector3.Dot(Vector3.Cross(Vector3.up, toDir), upDir)); if (sideSign == 0f) sideSign = 1f;
            float step = Mathf.Min(run, dist);
            float upAng = Vector3.SignedAngle(toDir, upDir, Vector3.up);       // прямо вверх по склону — тоже попытка (мимо камня)
            float[] tries = { 0f, upAng, 30f * sideSign, -30f * sideSign, 60f * sideSign, -60f * sideSign };
            Vector3 S3now = Shoulder; S3now.y = groundY + shoulderLift;
            float dNow = (P - S3now).magnitude;
            string why0 = "";                                             // ДИАГНОСТИКА: почему не пошёл прямой ход к кисти (строка состояния)
            foreach (float tr in tries)
            {
                Vector3 dir = Quaternion.AngleAxis(tr, Vector3.up) * toDir;    // + — вправо от хода к кисти
                Vector3 np = Slide(Body, dir * step);
                if ((np - Body).sqrMagnitude < 1e-8f) { if (tr == 0f) why0 = "упёрлось: " + BlockWhy(Body + dir * Mathf.Max(step, 0.01f)); continue; }   // другой угол
                float h = LevelHeight(np, BodyLevel, maxStep);
                if (float.IsInfinity(h)) { if (tr == 0f) why0 = "под новым местом уступ выше порога"; continue; }
                if (tr != 0f && h < groundY - 0.002f) continue;           // обход не вниз
                if (P.y - h < nubHangKeep) { pullWhy = "тяга по склону: камень уже у пояса"; return; }
                Vector3 S3n = Flat(np) + SideOff; S3n.y = h + shoulderLift;
                // РУКА СОГНУТА ДО УПОРА — только если шаг ПРИБЛИЖАЕТ плечо к камню ближе dMin (автор 2026-11-23: «надо просто дать руке
                // согнуться — преграды нет»). Шаг, проводящий плечо мимо камня вверх (расстояние не падает), — можно; иначе — другое направление.
                // плечо уже на высоте камня и выше — рука давит вниз (как на кромке): сгибается почти до плеча (5 см), а не до dMin
                float dNext = (P - S3n).magnitude, dLim = S3n.y >= P.y - 0.02f ? 0.05f : dMin;
                if (dNext < dLim && dNext < dNow - 1e-4f) { if (tr == 0f) why0 = $"рука согнута до упора (3D {dNext:F2} < {dMin:F2})"; continue; }
                if (!ZoneAllows(np, 0f)) { if (tr == 0f) why0 = "край зоны руки"; continue; }   // другой угол
                // тело не налезает на камень, за который держится (автор: «я налез поверх»): коснулось его — подтягивание стоп.
                // Другие камни телу не мешают никак (твёрдые камни вырезаны)
                // касание — по ПОВЁРНУТОМУ камню (автор 2026-11-23: «на склоне что-то тормозит»): рамка рендера вдоль осей мира у камня,
                // повёрнутого со склоном 60°, намного больше самого камня — тело вставало, не дойдя до него
                if (gripHold && gripHold.TryGetComponent<MeshFilter>(out var hmf) && hmf.sharedMesh)
                {
                    var lb = hmf.sharedMesh.bounds;
                    // коснулось бы — не тупик: другое направление (вверх по склону, в обход камня); стоп — только если закрыты все
                    var hf = ProtoItem.FootOfBox(gripHold.transform, lb.center, lb.size);
                    if (BoxHitsFoot(np, hf, 0.001f) && !BoxHitsFoot(Body, hf, 0.001f)) { if (tr == 0f) why0 = "тело коснулось камня"; continue; }
                }
                MoveBodyXZ(np.x, np.z);
                float d1 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d1);
                pullWhy = tr == 0f ? "" : $"тяга по склону: в обход {tr:F0}° (прямо: {why0})";
                return;
            }
            pullWhy = "тяга по склону: дальше не идёт — перехватись (прямо: " + why0 + ")";
        }

        /// <summary>ДИАГНОСТИКА (автор 2026-11-23: «не даёт руке согнуться, преграды нет — проверь лучи и определения препятствий»):
        /// какая проверка Blocked не пускает тело в p.</summary>
        string BlockWhy(Vector3 p)
        {
            // ТО ЖЕ, ЧТО Blocked, ПО ПОРЯДКУ (автор 2026-11-25: «разберись, где ограничения для тела и руки»): строка — ровно то правило,
            // что не пустило; прежняя проверяла обрыв как стену (до правила «опора по центру») и врала
            p = Flat(p);
            if (!PhysBodyActive) foreach (var o in tallWalls) if (BoxHitsRect(p, o, 0.001f)) return "стена (препятствие)";
            if (shoulderSide != 0f)
            {
                Vector3 S = p + SideOff;
                foreach (var o in tallWalls)
                {
                    float cx = Mathf.Clamp(S.x, o.xMin, o.xMax), cz = Mathf.Clamp(S.z, o.yMin, o.yMax);
                    if ((S.x - cx) * (S.x - cx) + (S.z - cz) * (S.z - cz) < ShoulderR * ShoulderR) return "плечо в стене";
                }
                if (HasSurface && !ProbeOk(p, S, true)) return "плечо: уступ выше порога";
            }
            if (HasSurface && float.IsInfinity(LevelHeight(p, BodyLevel, maxStep))) return "центр тела: уступ выше порога";
            if (HasSurface && climbLift > 0.01f && HangOverlaps(p)) return "вис: тело вошло бы в уступ";
            if (HasSurface) { int i = 0; foreach (var q in BodyProbes(p)) { if (!ProbeOk(p, q, true)) return $"угол/бок кубика №{i}: уступ выше порога"; i++; } }
            if (PhysBodyActive) return "не логика: физика тела (стена) / рука / ход";
            if (mode == Mode.Grip && armKeepOut > 0f && ArmToBodyGrip(p, p + SideOff) < armKeepOut) return "рука сквозь тело";
            return "скольжение не нашло хода";
        }

        /// <summary>Жест у кромки (в пикселях вдоль): «на себя» — поднять тело, «от себя» — опустить. true — жест ушёл в подъём.</summary>
        bool ClimbConvert(float px)
        {
            float meters = px * Px1080 * (OnNub ? nubClimbRate : climbPerPx);   // камень — быстрее кромки, как езда (Ledge)
            if (!ClimbGrip) return false;
            if (meters > 0f) hangFromTop = false;                          // «на себя» — снова подъём (перевал работает)
            float need = ClimbNeed;                                       // уступ — пока не станет ступенькой; камень только для руки — кисть у пояса (Ledge)
            if (meters > 0f)
            {
                if (climbLift >= need) return false;                      // уже поднялся — дальше обычный ход на уступ
                if (!(OnNub ? NubAtWall() : AtLedge()) && climbLift <= 0.001f) return false;      // ещё не у кромки — едет к ней
                if (OnNub && climbLift <= 0.001f && NubPathAlongSurface()) { NubClimbAlongSlope(meters); return true; }   // склон: вверх ПО склону, не над ним
                climbLift = ZoneLiftLimit(Mathf.Min(climbLift + meters, need));   // не выше края зоны руки (блок Zone): кисть не уходит за спину
                return true;
            }
            if (climbLift > 0.001f) { LowerHang(Mathf.Max(0f, climbLift + meters)); return true; }
            return false;
        }

        /// <summary>ПЕРЕВАЛ: поднялся настолько, что уступ стал ступенькой, — тело само заходит на него к кисти (пока держишься).
        /// Иначе короткая рука на высоком уступе (кисть у самой кромки) не довела бы тело за кромку.</summary>
        /// <summary>ВНИЗ — ВДОЛЬ СТЕНКИ, НЕ В НЕЁ (автор 2026-11-25: «после спуска оказался внутри блока»; «рывком, скачком»; «внутрь,
        /// потом провалился»): вис опускается до lift; на новой высоте тело зашло бы в уступ — сначала отходит от стенки на тот же шаг
        /// (одно движение «вниз и от стенки»). Для жеста «от себя» и для «отпустил — назад вниз».</summary>
        void LowerHang(float lift)
        {
            float step = climbLift - lift; if (step <= 0f) { climbLift = lift; return; }
            float was = climbLift;
            climbLift = lift;
            if (!HangOverlaps(Body)) return;
            climbLift = was;
            Vector3 away = Flat(Body - (mode == Mode.Grip ? gripHand : hand)); float am = away.magnitude;
            if (am < 1e-4f) away = -Face; else away /= am;
            Vector3 np = Body + away * step;
            MoveBodyXZ(np.x, np.z);
            if (mode == Mode.Grip) { float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0); }
        }

        /// <summary>Висящее тело (кубик на высоте BodyLevel) перекрылось бы с уступом: под центром / углами / сторонами верх выше низа тела.</summary>
        bool HangOverlaps(Vector3 c)
        {
            if (!HasSurface) return false;
            // ИЗНУТРИ, ОТ НИЗА ТЕЛА (не с высоты 50 м): луч вверх от низа — первой встретилась верхняя грань (нормаль вверх) — низ тела
            // внутри твёрдого (уступ). Крыша / мост над телом — луч встречает их нижнюю грань — не «перекрытие»
            float bottom = BodyLevel + 0.005f;
            System.Func<Vector3, bool> inSolid = q => RayHit(new Ray(new Vector3(q.x, bottom, q.z), Vector3.up), false, out _, out var nu) && nu.y > 0.1f;
            if (inSolid(c)) return true;
            foreach (var q in BodyProbes(c)) if (inSolid(q)) return true;
            return false;
        }

        [Tooltip("Отпустил вис у стенки: тело отходит от неё ровно на перекрытие (чтобы не приземлиться в уступ) с этой скоростью (м/с).")]
        public float hangReleaseClearSpeed = 0.6f;
        [Tooltip("Перевал: тело поднимается до верха уступа (не входя в него) с этой скоростью (м/с), потом заходит на уступ.")]
        public float mantleLiftSpeed = 0.6f;
        [Tooltip("Отпустил низкий вис (не выше maxDrop) — тело опускается на опору не быстрее этого (м/с).")]
        public float hangDropSpeed = 0.8f;

        /// <summary>Вис — после схода с края (спуск): перевал не тянет обратно на уступ, пока не потянешь «на себя».</summary>
        bool hangFromTop;

        void MantleStep(float dt)
        {
            if (mode != Mode.Grip || climbLift <= 0.001f) hangFromTop = false;
            if (!ClimbGrip || OnNub || hangFromTop) return;               // через камень не перевалить (Ledge); сошёл с края — висишь
            float need = gripTop - groundY - maxStep + 0.03f;
            if (climbLift < need - 1e-3f) return;
            Vector3 dir = Flat(gripHand - Body); float d = dir.magnitude;
            if (d < 0.03f) return;
            dir /= d;
            Vector3 np = Body + dir * Mathf.Min(mantleSpeed * dt, d - 0.03f);
            // ПЕРЕВАЛ — СНАЧАЛА ВВЕРХ, ПОТОМ НА УСТУП (автор 2026-11-25: «при подъёме тянет внутрь по кратчайшему пути, как будто нет
            // твёрдой преграды»): тело на новом месте вошло бы в уступ — сначала поднимается (плавно) до верха того, что под ним, потом идёт
            if (HangOverlaps(np))
            {
                float topUnder = Height(np);
                foreach (var q in BodyProbes(np)) topUnder = Mathf.Max(topUnder, Height(q));
                float want = topUnder - groundY + 0.005f;
                if (want > climbLift) { climbLift = Mathf.MoveTowards(climbLift, want, mantleLiftSpeed * dt); return; }
            }
            np = Slide(Body, np - Body);
            if (!ZoneAllows(np, climbLift)) return;                       // кисть вышла бы из зоны — перевал стоит (блок Zone)
            MoveBodyXZ(np.x, np.z);
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
            // ОТПУСТИЛ ВИС, ПОДНЯВШИСЬ ДО ВЕРХА, А КУБИК УЖЕ НАД НИМ (автор 2026-11-25: «залез на столбик и скатился»): встаёт на верх
            // и садится на него (Place: «утвердиться на верху»), не падает
            if (climbLift > maxDrop + 0.01f && FootSupportDir(groundY + climbLift).sqrMagnitude > 1e-8f)
            { groundY += climbLift; climbLift = 0f; crestFlash = 0.4f; return; }
            if (allowFall && climbLift > maxDrop + 0.01f)
            {
                // ОТПУСТИЛ ВИС — БЕЗ «ВЫСТРЕЛА» (автор 2026-11-25: «на краю отпустить ЛКМ — выстреливает назад, сильное отталкивание —
                // убрать»): от стенки — ровно столько, чтобы внизу не оказаться в уступе (0, если не перекрыт), плавно за время полёта
                Vector3 away = Flat(Body - gripHand); away = away.sqrMagnitude > 1e-6f ? away.normalized : -Face;
                float lift = climbLift, need = 0f;
                climbLift = 0f;                                                   // как будет внизу
                while (need < 0.3f && HangOverlaps(Body + away * need)) need += 0.01f;
                climbLift = lift;
                StartFall("отпустил вис", Vector3.zero, away, need, need / hangReleaseClearSpeed);   // спокойно, не «выстрел»
                return;
            }   // над полом глубже порога — падение (Fall)
            if (Mathf.Abs(climbLift) < 0.002f) { climbLift = 0f; return; }
            // назад вниз / докатиться на уступ — плавно, не быстрее hangDropSpeed (автор 2026-11-25: «спуск рывком — должно в одно движение»)
            LowerHang(Mathf.MoveTowards(climbLift, Mathf.Lerp(climbLift, 0f, 1f - Mathf.Exp(-fallBack * dt)), hangDropSpeed * dt));
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
            MoveBodyXZ(np.x, np.z);
            float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0);
            if (Flat(gripHand - Shoulder).magnitude > gripL + 0.03f) ReleaseGrip("рука не достаёт (тело ниже зацепа)", true);
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
            if (LetGoProgress > 0f) h = Color.Lerp(h, Color.white, 0.25f + 0.75f * LetGoProgress);   // «от себя» копится — кисть бледнеет, разжимается
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
