using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Hero.Proto
{
    /// <summary>Прототип движения одной рукой — «из позиций», без физики и анимации (кубики).
    ///
    /// Три точки: ТЕЛО (таз) B, ПЛЕЧО S (сбоку от тела), КИСТЬ H. Всё в плоскости XZ (высота — потом, по поверхности).
    /// 1. Рука свободна: кисть идёт за курсором по полу, не дальше досягаемости L от плеча (круг).
    /// 2. ЛКМ по полу в досягаемости — кисть ставится в точку. Строится прямая траектория тела вдоль направления плечо→кисть:
    ///      ДАЛЬНИЙ конец — рука вытянута (|S−H| = L),  БЛИЖНИЙ — рука согнута до упора (|S−H| = dMin).
    /// 3. Жест мышью с зажатой ЛКМ берётся ТОЛЬКО вдоль этой траектории на экране:
    ///      на себя (от кисти) — тело к кисти, от себя — тело назад. Боковая часть жеста отбрасывается
    ///      (это разворот — «лопасти вертолёта», пока не считаем). Сколько пикселей протянул вдоль траектории на экране —
    ///      столько тело и проехало по ней (1:1 к экрану), в пределах концов.
    /// 4. Отпустил ЛКМ — тело остаётся, рука снова свободна.
    /// Видно: гизмосы (окно Scene / Game с Gizmos) и линии LineRenderer (окно Game всегда).</summary>
    public partial class HeroProto : MonoBehaviour
    {
        [Header("Точки (кубики)")]
        public Transform bodyCube, shoulderCube, elbowCube, handCube;
        public Camera cam;

        [Header("Размеры руки (м)")]
        [Tooltip("Плечо + предплечье: досягаемость L.")]
        public float upperArm = 0.32f, foreArm = 0.30f;
        [Tooltip("Рука согнута до упора: плечо не ближе этого к кисти.")]
        public float dMin = 0.18f;
        [Tooltip("Плечо — вбок от тела (0 — как на схеме: прямая через центр тела и кисть).")]
        public float shoulderSide = 0.2f;
        [Tooltip("Плечо впереди центра тела (м): тело лёжа — плечо у головы (модель ничком: 0.27 от центра). Стоя — 0.")]
        public float shoulderForward = 0f;

        [Header("Препятствия (без физики)")]
        [Tooltip("Корень препятствий: каждый дочерний куб — прямоугольник на полу (по габаритам).")]
        public Transform obstaclesRoot;
        [Tooltip("Тело — круг такого радиуса (м).")]
        public float bodyRadius = 0.2f;
        readonly System.Collections.Generic.List<Rect> obstacles = new System.Collections.Generic.List<Rect>();

        [Header("Поверхность (Y)")]
        [Tooltip("Корень рельефа: ступени, склоны, выступы. Алгоритм — прежний в XZ, высота точки берётся с поверхности под ней.")]
        public Transform surfaceRoot;
        [Tooltip("Как плавно тело догоняет высоту поверхности (1/с).")]
        public float heightFollow = 14f;
        [Tooltip("ПОРОГ ПОДЪЁМА (м): уступ выше — стена, тело не поднимается. Вниз — с любой высоты.")]
        public float maxStep = 0.4f;
        [Tooltip("Кисть ложится на поверхность не выше своего уровня + столько (м).")]
        public float handUp = 0.5f;
        [Tooltip("ПОРОГ СПУСКА (м): поверхность ниже своего уровня больше этого — край, тело туда не опускается (не падает).")]
        public float maxDrop = 0.4f;
        [Tooltip("Кисть ложится на поверхность не ниже своего уровня − столько (м).")]
        public float handDown = 0.5f;
        readonly System.Collections.Generic.List<Vector3> surfNorm = new System.Collections.Generic.List<Vector3>();
        float handY;
        // треугольники рельефа в мире (рельеф неподвижен — собираются один раз)
        readonly System.Collections.Generic.List<Vector3> surfTris = new System.Collections.Generic.List<Vector3>();
        readonly System.Collections.Generic.List<Bounds> surfBounds = new System.Collections.Generic.List<Bounds>();
        readonly System.Collections.Generic.List<int> surfStart = new System.Collections.Generic.List<int>();
        float bodyLift, shoulderLift, elbowLift, handLift, groundY;

        [Header("Зона ввода")]
        [Tooltip("Зона ввода НИЖЕ персонажа: курсор считается выше на эту долю высоты экрана. Над персонажем управление шло, " +
                 "под ним ломалось (луч в пол за спиной) — теперь мышь работает под персонажем, а рука ставится перед ним. 0 — как раньше.")]
        [Range(0f, 0.5f)] public float inputShift = 0f;   // автор: вернуть как было (сдвиг не подошёл)

        [Header("Подсказки")]
        [Tooltip("Сектор, линия хода, стрелки жеста и лица. Выключено — видны только кубики.")]
        public bool showHints = true;

        [Header("Камера")]
        [Tooltip("Камера едет за телом с тем же смещением.")]
        public bool cameraFollow = true;
        Vector3 camOffset;

        [Header("Вперёд / назад")]
        [Tooltip("Доля жеста, когда тело едет лицом вперёд.")]
        public float forwardSpeed = 1f;
        [Tooltip("Доля жеста, когда тело едет спиной.")]
        public float backSpeed = 0.45f;
        [System.NonSerialized] public float lastSpeedK = 1f;

        [Header("Поворот на месте")]
        [Tooltip("Боковой жест при зажатой ЛКМ крутит тело на месте, кисть обходит тело по кругу.")]
        public bool allowTurn = true;
        [Tooltip("1 — дуга кисти равна боковому жесту на экране.")]
        public float turnGain = 1f;
        [Tooltip("Чувствительность поворота: 1 — дуга кисти равна жесту, меньше — мягче.")]
        [Range(0.05f, 1.5f)] public float turnSensitivity = 0.4f;
        [Tooltip("Плавность поворота (1/с): тело доворачивается к накопленному углу, без скачков.")]
        public float turnSmooth = 10f;
        float pendingTurn;                          // ещё не довёрнутый угол

        [Header("Движение")]
        [Tooltip("Насколько плавно тело догоняет позицию на траектории (1/с).")]
        public float follow = 14f;
        public float handFollow = 18f;

        [Tooltip("Тест без мыши: хват не отпускается.")]
        public bool testHold;

        public enum Mode { Free, Grip }
        public Mode mode;
        [Range(0f, 1f)] public float t;            // позиция тела на траектории: 0 — рука вытянута, 1 — согнута
        float tTarget;

        Vector3 hand, gripHand, farEnd, nearEnd;  // XZ, y = 0
        Vector2 lastGestureAlong, lastGestureSide; // для рисования
        LineRenderer lnArm, lnTraj, lnAxis, lnArrowA, lnArrowB, lnReach, lnGesture, lnSide, lnFace, lnTurnZone;

        public float L => upperArm + foreArm;

        // ---- чтение точек (для модели; логику не меняет) ----
        /// <summary>Тело на поверхности: XZ — центр тела, y — поверхность под ним.</summary>
        public Vector3 BodyGround => new Vector3(bodyCube.position.x, groundY + climbLift, bodyCube.position.z);   // с подъёмом на кромке
        /// <summary>Кисть на поверхности (XZ — точка алгоритма, y — поверхность под ней).</summary>
        public Vector3 HandOnSurface => new Vector3(hand.x, handY, hand.z);

        // ---- диагностика для сторожей (ProtoWatch) и бота; логику не меняет ----
        public float GroundY => groundY;
        /// <summary>Досягаемость в текущем хвате (по XZ).</summary>
        public float GripReach => gripL;
        public float HandY => handY;
        public Vector3 BodyXZ => Body;
        public Vector3 ShoulderXZ => Shoulder;
        public Vector3 HandXZ => hand;
        public Vector3 FaceDir => Face;
        public float LineT => t;
        public float LineTTarget => tTarget;
        public Vector3 FarEnd => farEnd;
        public Vector3 NearEnd => nearEnd;
        /// <summary>Тело заходит в препятствие (прямоугольник) глубже допуска.</summary>
        public bool DiagBodyInWall(float tol) { if (boxBody && bodyCube) { foreach (var o in obstacles) if (BoxHitsRect(Body, o, tol)) return true; return false; } foreach (var o in obstacles) { float cx = Mathf.Clamp(Body.x, o.xMin, o.xMax), cz = Mathf.Clamp(Body.z, o.yMin, o.yMax); float dx = Body.x - cx, dz = Body.z - cz; if (dx * dx + dz * dz < (bodyRadius - tol) * (bodyRadius - tol)) return true; } return false; }
        /// <summary>Центр тела внутри сплошного (уступ / ящик выше порога).</summary>
        public bool DiagBodyInSolid => HasSurface && float.IsInfinity(LevelHeight(Body, BodyLevel, maxStep));
        public bool DiagHandInWall => !gripHold && !hoverHold && sinceHold > 0.3f && InObstacle(hand, -0.005f);   // кисть над стойкой перил после хвата — не в стене
        public float DiagArmToBody => ArmToBody(Body, Shoulder, hand);
        public bool DiagBlocked(Vector3 p) => Blocked(Flat(p), bodyRadius);
        public bool DiagClimbable(Vector3 p) => Climbable(Flat(p));
        /// <summary>Тест: поставить тело в точку p (p.y — уровень, с которого искать поверхность), лицом по yaw; рука свободна.</summary>
        public void DiagTeleport(Vector3 p, float yaw)
        {
            mode = Mode.Free; pendingTurn = 0f; handMover = null;
            bodyCube.rotation = Quaternion.Euler(0f, yaw, 0f);
            float g = LevelHeight(Flat(p), p.y, maxStep, out var rm);
            groundY = float.IsInfinity(g) ? p.y : g; rideMover = rm; rideStep = rm ? rm.StepId : 0;
            SetBodyPos(new Vector3(p.x, groundY + bodyLift, p.z));
            hand = Shoulder + Face * L * 0.6f; handY = groundY;
            ClimbReset();
            Place();
        }
        /// <summary>Симуляция: герой на место старта, всё состояние с нуля — прогоны повторяемы.</summary>
        public void DiagReset()
        {
            DiagTeleport(spawnPos, spawnYaw);
            t = tTarget = 0f; lastGestureAlong = lastGestureSide = Vector2.zero; mouseAcc = default; mouseHave = false; driver = null;
            LedgeReset();
            StateReset();
            FallReset();
            DragReset();
        }
        Vector3 spawnPos; float spawnYaw; bool spawnSet;
        /// <summary>Зоны одной арены (ProtoZones): старт героя в активной зоне.</summary>
        public void DiagSetSpawn(Vector3 p, float yaw) { spawnPos = p; spawnYaw = yaw; spawnSet = true; }
        /// <summary>Зоны одной арены: включилась другая зона — заново собрать стены, поверхность, платформы и зацепы (только активные).</summary>
        public void DiagRecollect() { CollectObstacles(); CollectSurface(); CollectHolds(); rideMover = handMover = null; }
        public Bounds DiagSurfaceBounds { get { var b = new Bounds(Body, Vector3.one); foreach (var s in surfBounds) b.Encapsulate(s); return b; } }

        /// <summary>Поверхность под кистью со своего уровня (не выше handUp); внутри сплошного — TooHigh.</summary>
        float HandLevel(Vector3 p) => HandHit(p, out _, out _);

        /// <summary>Поверхность под кистью: сначала в обычных пределах руки (handUp) — пол под перекрытием остаётся полом;
        /// только если там сплошное (стена уступа) — верх уступа до climbUp (кромка для вскарабкивания).</summary>
        float HandHit(Vector3 p, out ProtoMover mover, out Vector3 normal)
        {
            float h = LevelHit(p, BodyLevel, handUp, out mover, out normal);
            if (!float.IsInfinity(h) || climbUp <= handUp) return h;
            return LevelHit(p, BodyLevel, climbUp, out mover, out normal);
        }
        const float HandGap = 0.03f;      // кисть не ближе этого к стене

        [Header("Рука и тело")]
        [Tooltip("Тело для руки (м от центра): рука (отрезок плечо → кисть) не проходит через тело — в себя не залезть. 0.15 — полкубика.")]
        public float armKeepOut = 0.15f;

        /// <summary>Насколько близко рука (отрезок S → H) подходит к центру тела B, в плоскости XZ.</summary>
        /// <summary>ПЛЕЧО ПОДНИМАЕТ РУКУ НАД ТЕЛОМ (автор 2026-11-23: «на плече не хватает степени свободы — добавь поворот плеча вверх-вниз»).
        /// Рука в хвате сквозь тело считалась по полу: кисть на камне выше тела, рука идёт НАД кубиком — а ход и поворот запрещались,
        /// как будто рука режет грудь. Теперь: в ближайшей к телу точке рука (по высоте — от плеча к кисти) выше верха кубика — не мешает (∞).</summary>
        float ArmToBodyGrip(Vector3 B, Vector3 S)
        {
            Vector3 b = Flat(B), s = Flat(S), h = Flat(gripHand), d = h - s;
            float k = d.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(b - s, d) / d.sqrMagnitude) : 0f;
            float hy = gripHold ? GripPoint3.y : gripTop + handLift;
            float ay = Mathf.Lerp(ShoulderY, hy, k);
            float top = bodyCube ? bodyCube.position.y + Mathf.Abs(bodyCube.lossyScale.y) * 0.5f : ShoulderY;
            if (ay > top + 0.02f) return float.PositiveInfinity;
            return (b - (s + d * k)).magnitude;
        }

        static float ArmToBody(Vector3 B, Vector3 S, Vector3 H)
        {
            Vector3 b = Flat(B), s = Flat(S), h = Flat(H), d = h - s;
            float k = d.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(b - s, d) / d.sqrMagnitude) : 0f;
            return (b - (s + d * k)).magnitude;
        }
        /// <summary>Самая длинная рука по направлению dir (плечо → кисть), при которой отрезок руки не режет тело.
        /// Тело сбоку от плеча и стоит в нём неподвижно, пока тело едет по линии хода: рука через грудь, вытягиваясь
        /// (тело отъезжает от кисти), подходит к центру тела — дальний конец хода там, где рука упрётся в тело.</summary>
        float ArmLenMax(Vector3 dir, float curLen)
        {
            if (armKeepOut <= 0f) return Mathf.Max(gripL, Mathf.Min(curLen, L));
            return Mathf.Max(Mathf.Min(ArmLenMax0(dir), gripL), Mathf.Min(curLen, L));   // уже на границе — не хуже, чем сейчас (без рывка тела на линию)
        }
        float ArmLenMax0(Vector3 dir) => ArmLenMaxAt(SideOff, dir);
        float ArmLenMaxAt(Vector3 sideOff, Vector3 dir)
        {
            Vector3 v = -sideOff;                                   // тело относительно плеча
            float p = Vector3.Dot(v, dir), perp = (v - dir * p).magnitude;
            if (perp >= armKeepOut || p <= 0f) return L;
            return Mathf.Clamp(p - Mathf.Sqrt(armKeepOut * armKeepOut - perp * perp), Mathf.Min(dMin + 0.02f, L), L);
        }
        /// <summary>СВОБОДНАЯ КИСТЬ НЕ СКВОЗЬ ТЕЛО (давний сторож «рука сквозь тело» у свободной кисти): кисть догоняет курсор плавно,
        /// и по пути отрезок плечо → кисть мог пройти через тело. Если так — кисть поворачивается вокруг плеча (длина та же)
        /// в ближайшую сторону, где рука тело не режет. Только свободная кисть: хват и его досягаемость не меняются.</summary>
        Vector3 FreeHandAroundBody(Vector3 h)
        {
            if (armKeepOut <= 0f || !ArmThroughBody(h)) return h;
            Vector3 S = Shoulder, v = Flat(h - S);
            if (v.sqrMagnitude < 1e-6f) return Flat(S + Face * 0.2f);
            for (int k = 1; k <= 18; k++)
                foreach (float sgn in new[] { 1f, -1f })
                {
                    Vector3 q = Flat(S + Quaternion.AngleAxis(sgn * k * 10f, Vector3.up) * v);
                    if (!ArmThroughBody(q)) return q;
                }
            return h;
        }

        /// <summary>Рука сквозь тело?</summary>
        bool ArmThroughBody(Vector3 H) => armKeepOut > 0f && ArmToBody(Body, Shoulder, H) < armKeepOut;

        /// <summary>Верх кубика тела (мир).</summary>
        float BodyTopY => bodyCube ? bodyCube.position.y + Mathf.Abs(bodyCube.lossyScale.y) * 0.5f : ShoulderY;

        /// <summary>РУКА С ПРЕДМЕТОМ НАД ГОЛОВОЙ (автор 2026-11-24: «не хватает сгиба руки и движения — вплоть до над головой»):
        /// «рука сквозь тело» — в 3D: кисть на высоте hy, в ближайшей к телу точке рука выше верха кубика — тело не мешает
        /// (то же правило, что ArmToBodyGrip у хвата за камень).</summary>
        bool ArmThroughBody3(Vector3 H, float hy)
        {
            if (!ArmThroughBody(H)) return false;
            Vector3 b = Body, s = Flat(Shoulder), h = Flat(H), d = h - s;
            float k = d.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(b - s, d) / d.sqrMagnitude) : 0f;
            return Mathf.Lerp(ShoulderY, hy, k) <= BodyTopY + 0.02f;
        }

        /// <summary>Кисть дотягивается только до первой преграды на линии плечо → цель: стены (препятствие) или уступа выше досягаемости.
        /// Рука не проходит сквозь тонкую стену. Ввод не теряется: хват всегда есть, направление то же.</summary>
        /// carry — кисть держит предмет на весу (перенос, блок Drag): обрыв вниз и край, через который кисти не лечь, — не барьер
        /// (автор 2026-11-23: «опустить, спустить — рука замирает»); стены, своё тело и уступ выше — как обычно.
        Vector3 HandAtLedge(Vector3 target, bool carry = false)
        {
            Vector3 S = Shoulder, d = Flat(target - S);
            float len = d.magnitude;
            if (float.IsNaN(len) || float.IsInfinity(len)) return S + Face * 0.05f;   // защита: без конечной цели марш не кончится
            len = Mathf.Min(len, 2f * L);
            if (len < 1e-4f) return target;
            if (InObstacle(S, HandGap) || float.IsInfinity(HandLevel(S))) return PushOut(target);   // плечо само в преграде (не должно) — по-старому
            // с предметом — «сквозь тело» в 3D (кисть на высоте точки хвата: над головой тело не мешает)
            float gy = CarryGrabY;
            System.Func<Vector3, bool> through = carry ? (m => ArmThroughBody3(m, gy)) : (System.Func<Vector3, bool>)ArmThroughBody;
            // рука резала бы тело — доворот направления к лицу до границы, ДЛИНА та же (без скачка кисти к груди)
            if (through(target))
            {
                Vector3 fc = Face;
                float phi = Vector3.SignedAngle(fc, d, Vector3.up), lo = 0f, hi = phi;
                for (int i = 0; i < 12; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (through(S + Quaternion.AngleAxis(mid, Vector3.up) * fc * len)) hi = mid; else lo = mid;
                }
                d = Quaternion.AngleAxis(lo, Vector3.up) * fc * len;
            }
            Vector3 dir = d / len;
            const float step = 0.02f;
            float ok = 0f;
            System.Func<Vector3, bool> stop = carry
                ? (m => InObstacle(m, HandGap) || !HandOkAbove(m) || through(m))
                : (m => InObstacle(m, HandGap) || !HandOk(m) || ArmThroughBody(m) || ArmDownCutsSurface(m));
            for (float s = step; ; s += step)
            {
                float sc = Mathf.Min(s, len);
                Vector3 q = S + dir * sc;
                if (stop(q))
                {
                    // уточнить кромку между ok и sc (стена, уступ, своё тело или край, через который вниз не дотянуться)
                    float lo = ok, hi = sc;
                    for (int i = 0; i < 6; i++) { float mid = (lo + hi) * 0.5f; Vector3 m = S + dir * mid; if (stop(m)) hi = mid; else lo = mid; }
                    ok = lo; break;
                }
                ok = sc;
                if (sc >= len) break;
            }
            Vector3 res = S + dir * Mathf.Max(ok, 0.05f);              // не ближе 5 см к плечу — чтобы у хвата было направление
            if (carry && gy > BodyTopY + 0.02f) return res;            // кисть с предметом над головой — над кубиком можно
            return HandOutOfBody(res);                                 // не внутри кубика тела (Body)
        }

        /// <summary>Точка внутри препятствия (прямоугольник, расширенный на gap)?</summary>
        bool InObstacle(Vector3 p, float gap)
        {
            foreach (var o in obstacles)
                if (p.x > o.xMin - gap && p.x < o.xMax + gap && p.z > o.yMin - gap && p.z < o.yMax + gap) return true;
            return false;
        }

        const float ShoulderR = 0.05f;    // плечо — маленький круг: не входит в стены и уступы выше порога

        /// <summary>Плечо при теле в точке bodyP (текущий разворот) задевает стену или уступ выше порога?</summary>
        bool ShoulderBlocked(Vector3 bodyP)
        {
            if (shoulderSide == 0f) return false;
            Vector3 S = Flat(bodyP) + SideOff;
            foreach (var o in obstacles)
            {
                float cx = Mathf.Clamp(S.x, o.xMin, o.xMax), cz = Mathf.Clamp(S.z, o.yMin, o.yMax);
                float dx = S.x - cx, dz = S.z - cz;
                if (dx * dx + dz * dz < ShoulderR * ShoulderR) return true;
            }
            return HasSurface && !ProbeOk(bodyP, S);                 // плечо на склоне — как край кубика (ProbeOk)
        }

        /// <summary>Высота для подсказок: со своего уровня, а где стена — по верху.</summary>
        float DrawHeight(Vector3 p) { float h = HandLevel(p); return float.IsInfinity(h) ? Height(p) : h; }
        public bool Gripping => mode == Mode.Grip;
        /// <summary>Линия руки «Рука» (по кубикам) — можно спрятать, когда вместо кубиков модель.</summary>
        public bool drawArmLine = true;

        [Header("Два слоя вокруг лица")]
        [Tooltip("ДВИЖЕНИЕ (разброс рук): куда ставится кисть и где жест везёт тело. Через грудь влево, градусы от лица.")]
        public float sectorLeft = 50f;
        [Tooltip("ДВИЖЕНИЕ: вправо (сторона руки), градусы от лица.")]
        public float sectorRight = 70f;
        [Tooltip("ПОВОРОТ НА МЕСТЕ (шире): до какого угла кисти от лица можно довернуть тело за один хват. Влево.")]
        public float turnLeft = 100f;
        [Tooltip("ПОВОРОТ НА МЕСТЕ: вправо.")]
        public float turnRight = 120f;
        [Tooltip("Поворот на месте на 360° — без предела слоя поворота.")]
        public bool turn360 = true;
        [Tooltip("ЗОНА ПОВОРОТА за один хват (градусы, всего): 180 — по 90° в каждую сторону от лица.")]
        public float turnZone = 180f;
        [Tooltip("Езда гаснет до нуля, когда кисть дальше этого угла от лица (влево / вправо). 100 + 100 = 200° — больше 180.")]
        public float moveFadeLeft = 100f, moveFadeRight = 100f;
        [Tooltip("Езда на все 360°: при любом угле кисти от лица (затухание moveFade не действует).")]
        public bool moveAllAround = false;

        Vector3 Face => Vector3.ProjectOnPlane(bodyCube.forward, Vector3.up).normalized;

        /// <summary>Точка курсора → куда может встать кисть: не дальше L и внутри сектора перед лицом.</summary>
        Vector3 Reach(Vector3 p)
        {
            Vector3 S = Shoulder, v = p - S; v.y = 0f;
            float len = Mathf.Min(v.magnitude, L);
            if (len < 1e-4f) return S + Face * 0.05f;
            float a = Vector3.SignedAngle(Face, v, Vector3.up);
            float c = Mathf.Clamp(a, -sectorLeft, sectorRight);
            return S + Quaternion.AngleAxis(c, Vector3.up) * Face * len;
        }
        Vector3 Body => Flat(bodyCube.position);
        /// <summary>Смещение плеча от центра тела: вправо от лица (рука у героя — правая), крутится вместе с телом.</summary>
        Vector3 SideOff => Vector3.Cross(Vector3.up, Face) * shoulderSide + Face * shoulderForward;   // плечо: вбок (справа) и вперёд (к голове, тело лёжа)
        Vector3 SideDir => Vector3.Cross(Vector3.up, Face);
        Vector3 Shoulder => Body + SideOff;

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void Start()
        {
            if (!cam) cam = Camera.main;
            CollectObstacles();
            if (!surfaceRoot) { var r = GameObject.Find("Рельеф (Y)"); if (r) surfaceRoot = r.transform; }
            CollectSurface();
            CollectHolds();
            // высоты кубиков над поверхностью — как их поставил сборщик на ровном полу
            bodyLift = bodyCube.position.y; shoulderLift = shoulderCube.position.y;
            elbowLift = elbowCube.position.y; handLift = handCube.position.y;
            groundY = Height(Body);
            handY = DrawHeight(hand);
            camOffset = cam.transform.position - bodyCube.position;
            if (!spawnSet) { spawnPos = new Vector3(bodyCube.position.x, groundY, bodyCube.position.z); spawnYaw = bodyCube.eulerAngles.y; }
            hand = Shoulder + Face * L * 0.6f;                      // кисть — перед лицом, а не по мировой оси
            lnArm = Line("Рука", new Color(1f, 0.85f, 0.55f), 0.025f);
            lnTraj = Line("Траектория (ход тела)", new Color(0.2f, 0.85f, 0.25f), 0.03f);
            lnAxis = Line("Прямая тело—кисть", new Color(0.2f, 0.85f, 0.25f), 0.012f);
            lnArrowA = Line("Стрелка к кисти", new Color(0.2f, 0.85f, 0.25f), 0.012f);
            lnArrowB = Line("Стрелка от кисти", new Color(0.2f, 0.85f, 0.25f), 0.012f);
            lnReach = Line("Досягаемость", new Color(0.5f, 0.7f, 1f, 0.6f), 0.008f);
            lnFace = Line("Лицо (вперёд)", new Color(1f, 1f, 1f, 0.9f), 0.02f);
            lnTurnZone = Line("Слой поворота на месте", new Color(0.9f, 0.35f, 0.3f, 0.7f), 0.008f);
            lnGesture = Line("Жест вдоль", new Color(0.3f, 1f, 0.4f), 0.015f);
            lnSide = Line("Жест вбок (поворот)", new Color(0.4f, 0.7f, 1f, 0.9f), 0.012f);
        }

        void CollectObstacles()
        {
            obstacles.Clear();
            if (!obstaclesRoot) return;
            foreach (var r in obstaclesRoot.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                obstacles.Add(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z));
            }
        }

        // ---------------- поверхность: точка на плоскости + её высота ----------------
        void CollectSurface()
        {
            surfTris.Clear(); surfBounds.Clear(); surfStart.Clear(); surfNorm.Clear();
            CollectMovers();
            if (!surfaceRoot) return;
            foreach (var mf in surfaceRoot.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.GetComponentInParent<ProtoMover>()) continue;          // подвижные — отдельно (CollectMovers)
                var mesh = mf.sharedMesh; if (!mesh) continue;
                var v = mesh.vertices; var nv = mesh.normals; var tr = mesh.triangles; var m = mf.transform.localToWorldMatrix;
                surfStart.Add(surfTris.Count);
                var b = new Bounds(m.MultiplyPoint3x4(v[tr[0]]), Vector3.zero);
                for (int i = 0; i < tr.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[tr[i]]), bb = m.MultiplyPoint3x4(v[tr[i + 1]]), c = m.MultiplyPoint3x4(v[tr[i + 2]]);
                    surfTris.Add(a); surfTris.Add(bb); surfTris.Add(c);
                    b.Encapsulate(a); b.Encapsulate(bb); b.Encapsulate(c);
                    // нормаль грани наружу: геометрическая, направлена как нормали вершин сетки
                    Vector3 ng = Vector3.Cross(bb - a, c - a).normalized;
                    if (nv != null && nv.Length == v.Length)
                    {
                        Vector3 vn = m.MultiplyVector(nv[tr[i]] + nv[tr[i + 1]] + nv[tr[i + 2]]);
                        if (Vector3.Dot(ng, vn) < 0f) ng = -ng;
                    }
                    surfNorm.Add(ng);
                }
                b.Expand(0.002f);
                surfBounds.Add(b);
            }
        }

        // ---------------- подвижные поверхности (блок ProtoMover) ----------------
        class MoverSurf
        {
            public ProtoMover m;
            public readonly System.Collections.Generic.List<Vector3> tris = new System.Collections.Generic.List<Vector3>();   // в системе платформы
            public readonly System.Collections.Generic.List<Vector3> norms = new System.Collections.Generic.List<Vector3>();
            public Renderer[] rends;
            /// <summary>Детали платформы: рендерер и его треугольники [start, end) — луч проверяет только задетые по рамке.</summary>
            public readonly System.Collections.Generic.List<(Renderer r, int start, int end)> parts = new System.Collections.Generic.List<(Renderer, int, int)>();
        }
        readonly System.Collections.Generic.List<MoverSurf> movers = new System.Collections.Generic.List<MoverSurf>();
        [System.NonSerialized] public ProtoMover rideMover, handMover;   // на чём стоит тело / за что держится кисть
        int rideStep, handStep;                                         // последний применённый шаг платформы

        void CollectMovers()
        {
            movers.Clear();
            var pms = FindObjectsOfType<ProtoMover>();
            System.Array.Sort(pms, (a, b) => string.CompareOrdinal(a.name, b.name));   // порядок по имени — не по внутренним номерам (пересборка сцены)
            foreach (var pm in pms)
            {
                var ms = new MoverSurf { m = pm, rends = pm.GetComponentsInChildren<Renderer>() };
                Matrix4x4 toLocal = pm.transform.worldToLocalMatrix;
                foreach (var mf in pm.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = mf.sharedMesh; if (!mesh) continue;
                    var v = mesh.vertices; var nv = mesh.normals; var tr = mesh.triangles;
                    Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
                    int start = ms.tris.Count;
                    for (int i = 0; i < tr.Length; i += 3)
                    {
                        Vector3 a = m.MultiplyPoint3x4(v[tr[i]]), b = m.MultiplyPoint3x4(v[tr[i + 1]]), c = m.MultiplyPoint3x4(v[tr[i + 2]]);
                        ms.tris.Add(a); ms.tris.Add(b); ms.tris.Add(c);
                        Vector3 ng = Vector3.Cross(b - a, c - a).normalized;
                        if (nv != null && nv.Length == v.Length && Vector3.Dot(ng, m.MultiplyVector(nv[tr[i]] + nv[tr[i + 1]] + nv[tr[i + 2]])) < 0f) ng = -ng;
                        ms.norms.Add(ng);
                    }
                    ms.parts.Add((mf.GetComponent<Renderer>(), start, ms.tris.Count));
                }
                movers.Add(ms);
            }
        }

        bool HasSurface => surfBounds.Count > 0 || movers.Count > 0;

        bool RayHit(Ray ray, bool plane, out float best, out Vector3 normal) => RayHit(ray, plane, out best, out normal, out _);

        /// <summary>Ближайшее пересечение луча с рельефом (обе стороны треугольников), с подвижными платформами или с полом y = 0 (если plane).
        /// normal — наружная нормаль задетой грани (пол — вверх); mover — платформа, если попали в неё.</summary>
        bool RayHit(Ray ray, bool plane, out float best, out Vector3 normal, out ProtoMover mover)
        {
            best = float.MaxValue; normal = Vector3.up; mover = null;
            if (plane && ray.direction.y < -1e-5f) { float d = -ray.origin.y / ray.direction.y; if (d > 0f) best = d; }
            for (int k = 0; k < surfBounds.Count; k++)
            {
                if (!surfBounds[k].IntersectRay(ray, out float bd) || bd > best) continue;
                int end = k + 1 < surfStart.Count ? surfStart[k + 1] : surfTris.Count;
                for (int i = surfStart[k]; i < end; i += 3)
                    if (RayTri(ray.origin, ray.direction, surfTris[i], surfTris[i + 1], surfTris[i + 2], out float d) && d < best) { best = d; normal = surfNorm[i / 3]; mover = null; }
            }
            foreach (var ms in movers)
            {
                if (!ms.m) continue;
                bool local = false; Matrix4x4 l2w = default; Vector3 o = default, dir = default;
                foreach (var part in ms.parts)
                {
                    if (part.r) { var b = part.r.bounds; b.Expand(0.01f); if (!b.IntersectRay(ray, out float bd) || bd > best) continue; }
                    if (!local)
                    {
                        // луч — в систему платформы (параметр t тот же: направление не нормируется)
                        local = true;
                        Matrix4x4 w2l = ms.m.transform.worldToLocalMatrix; l2w = ms.m.transform.localToWorldMatrix;
                        o = w2l.MultiplyPoint3x4(ray.origin); dir = w2l.MultiplyVector(ray.direction);
                    }
                    for (int i = part.start; i < part.end; i += 3)
                        if (RayTri(o, dir, ms.tris[i], ms.tris[i + 1], ms.tris[i + 2], out float d) && d < best)
                        { best = d; normal = l2w.MultiplyVector(ms.norms[i / 3]).normalized; mover = ms.m; }
                }
            }
            return best < float.MaxValue;
        }

        bool SurfaceRay(Ray ray, out Vector3 hit)
        {
            bool ok = RayHit(ray, true, out float d, out _);
            hit = ok ? ray.GetPoint(d) : default;
            return ok;
        }

        static bool RayTri(Vector3 ro, Vector3 rd, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(rd, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det; Vector3 s = ro - a;
            float u = Vector3.Dot(s, p) * inv; if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(rd, q) * inv; if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 1e-5f;
        }

        /// <summary>Высота самой верхней поверхности в точке XZ (рельеф или пол 0) — для старта и подсказок вне уровня.</summary>
        public float Height(Vector3 p)
        {
            if (!HasSurface) return 0f;
            return SurfaceRay(new Ray(new Vector3(p.x, 50f, p.z), Vector3.down), out var h) ? h.y : 0f;
        }

        /// <summary>ВЫСОТА ОТ СВОЕГО УРОВНЯ. Поверхность в точке XZ, на которую можно встать с высоты fromY, поднявшись не больше up:
        /// луч вниз с fromY + up — мост, перекрытие тоннеля и всё, что выше, не мешает; ниже — с любой глубины.
        /// Точка внутри сплошного (бок галереи, ящик) — TooHigh: выше порога, это стена.</summary>
        public float LevelHeight(Vector3 p, float fromY, float up) => LevelHeight(p, fromY, up, out _);

        /// <summary>То же + платформа под точкой (null — неподвижная поверхность или пол).</summary>
        public float LevelHeight(Vector3 p, float fromY, float up, out ProtoMover mover) => LevelHit(p, fromY, up, out mover, out _);

        /// <summary>То же + нормаль поверхности в точке (крутизна склона).</summary>
        public float LevelHit(Vector3 p, float fromY, float up, out ProtoMover mover, out Vector3 normal)
        {
            mover = null; normal = Vector3.up;
            if (!HasSurface) return 0f;
            float top = fromY + up + 0.003f;
            Vector3 o = new Vector3(p.x, top, p.z);
            // внутри сплошного: ближайшая грань сверху смотрит вверх (это её крышка изнутри)
            if (RayHit(new Ray(o, Vector3.up), false, out _, out var nu) && nu.y > 0.1f) return TooHigh;
            return RayHit(new Ray(o, Vector3.down), top > 0f, out float d, out normal, out mover) ? top - d : 0f;
        }
        public const float TooHigh = float.PositiveInfinity;

        /// <summary>Тело может стоять в точке p со своего уровня: не выше порога maxStep и не ниже порога maxDrop (край — как стена).</summary>
        bool Climbable(Vector3 p)
        {
            float h = LevelHeight(p, BodyLevel, maxStep);
            if (float.IsInfinity(h)) return false;
            if (climbLift < -0.01f) return true;                        // докатывается после перевала: кромка за спиной — не обрыв
            return h >= groundY - maxDrop;
        }

        /// <summary>СКЛОН — НЕ СТУПЕНЬ И НЕ ОБРЫВ (автор 2026-11-23: «при определённом повороте поднимался легко и быстро» — на склоне 60°
        /// угол кубика вверх по склону при повороте наискосок выходил выше порога шага, ход запрещался; вниз — ниже порога спуска).
        /// Угол кубика / плечо, не прошедшие порог от уровня тела: от центра к ним шагами по 2 см — между соседними точками вверх
        /// не больше maxStep, вниз не больше maxDrop (как обычный шаг; камень на склоне — не преграда, автор: «лучше, но ситуации есть»)
        /// — это склон, можно; уступ выше порога или обрыв — край, как был.</summary>
        bool ProbeOk(Vector3 c, Vector3 q)
        {
            if (Climbable(q)) return true;
            if (!HasSurface) return true;
            Vector3 a = Flat(c), b = Flat(q);
            float prev = LevelHeight(a, BodyLevel, maxStep);
            if (float.IsInfinity(prev)) return false;
            int n = Mathf.Max(2, Mathf.CeilToInt((b - a).magnitude / 0.02f));
            for (int i = 1; i <= n; i++)
            {
                float h = LevelHeight(Vector3.Lerp(a, b, i / (float)n), prev, maxStep);   // не выше прошлой точки + порог шага
                if (float.IsInfinity(h) || prev - h > maxDrop) return false;               // уступ выше порога или обрыв
                prev = h;
            }
            return true;
        }

        /// <summary>Кисть может лечь в точку p: не выше своего уровня + handUp и не ниже − handDown.</summary>
        bool HandOk(Vector3 p)
        {
            float h = HandLevel(p);
            if (float.IsInfinity(h) || h < groundY - handDown) return false;
            return h <= BodyLevel + maxStep || LedgeGrabOk(p, h);     // выше порога — только кромка уступа в досягаемости (вскарабкивание)
        }

        /// <summary>HandOk без нижнего предела: кисть с предметом на весу — обрыв вниз не барьер, уступ выше — как обычно.</summary>
        bool HandOkAbove(Vector3 p)
        {
            float h = HandLevel(p);
            if (float.IsInfinity(h)) return false;                    // сплошное (уступ выше) — барьер
            return h <= BodyLevel + maxStep || LedgeGrabOk(p, h);     // обрыв вниз (низкий h) — можно
        }

        /// <summary>Круг радиуса r в точке p задевает препятствие или уступ выше порога (центр + 4 точки по краю)?</summary>
        bool Blocked(Vector3 p, float r)
        {
            if (boxBody && r == bodyRadius && bodyCube)
            {
                // тело — своим кубиком (блок Body): стены — пересечение прямоугольников, уступы и обрывы — пробы по углам и сторонам
                if (!PhysBodyActive)                                   // тело в физике — стены решает физика (шаг 4, блок ItemPhysics)
                    foreach (var o in obstacles) if (BoxHitsRect(p, o, 0.001f)) return true;
                if (ShoulderBlocked(p)) return true;
                if (!HasSurface) return false;
                if (!Climbable(p)) return true;
                foreach (var q in BodyProbes(p)) if (!ProbeOk(p, q)) return true;
                return false;
            }
            foreach (var o in obstacles)
            {
                float cx = Mathf.Clamp(p.x, o.xMin, o.xMax), cz = Mathf.Clamp(p.z, o.yMin, o.yMax);
                float dx = p.x - cx, dz = p.z - cz;
                if (dx * dx + dz * dz < r * r) return true;
            }
            if (ShoulderBlocked(p)) return true;                    // плечо сбоку тоже не входит в стену / уступ
            if (!HasSurface) return false;
            if (!Climbable(p)) return true;
            float e = r * 0.8f;
            return !Climbable(p + new Vector3(e, 0f, 0f)) || !Climbable(p - new Vector3(e, 0f, 0f))
                || !Climbable(p + new Vector3(0f, 0f, e)) || !Climbable(p - new Vector3(0f, 0f, e));
        }

        [Header("Материал стен")]
        [Tooltip("Трение о стену: 0 — лёд (полностью скользит вдоль), 1 — липкая (упёрся и стоит).")]
        [Range(0f, 1f)] public float wallFriction = 0.15f;

        [Header("Автообход")]
        [Tooltip("Сила анти-вектора от препятствий в ходе тела: 0 — выкл, 1 — у самого края отталкивание равно ходу.")]
        [Range(0f, 3f)] public float avoidGain = 1f;
        [Tooltip("С какого расстояния от края препятствие начинает отталкивать (м).")]
        public float avoidMargin = 0.12f;

        /// <summary>Анти-вектор: сумма отталкиваний от стен, стоек, уступов выше порога (тело) и стен у плеча.
        /// Сила 0 на расстоянии avoidMargin от края → 1 при касании. В углу складывается наружу по диагонали.</summary>
        Vector3 Repulsion(Vector3 p)
        {
            p = Flat(p);
            Vector3 rep = Vector3.zero;
            void FromRects(Vector3 c, float rad, bool withItems)
            {
                foreach (var o in obstacles)
                {
                    float cx = Mathf.Clamp(c.x, o.xMin, o.xMax), cz = Mathf.Clamp(c.z, o.yMin, o.yMax);
                    Vector3 d = new Vector3(c.x - cx, 0f, c.z - cz);
                    float m = d.magnitude, reach = rad + avoidMargin;
                    if (m >= reach) continue;
                    Vector3 n = m > 1e-5f ? d / m : new Vector3(c.x - o.center.x, 0f, c.z - o.center.y).normalized;
                    rep += n * Mathf.Clamp01((reach - m) / avoidMargin);
                }
                // ЛЕЖАЩИЕ ПРЕДМЕТЫ — тоже отталкивают тело (автор 2026-11-22: «застреваю в items, спотыкаюсь — для углов было
                // автопроскальзывание, сюда тоже надо»; 2026-11-23 — убирал по просьбе, автор: «верни авто обход углов и поворотов»).
                // Только тело: плечо над предметом проходит. След — повёрнутый (ProtoItem.FootPrint), не габарит по осям мира.
                if (withItems)
                    foreach (var it in ProtoItem.Live)
                    {
                        if (!it || it.held) continue;
                        var f = it.FootPrint;
                        Vector3 d = Flat(c - f.Closest(c, 0f));
                        float m = d.magnitude, reach = rad + avoidMargin;
                        if (m >= reach) continue;
                        Vector3 n = m > 1e-5f ? d / m : Flat(c - f.c).normalized;
                        rep += n * Mathf.Clamp01((reach - m) / avoidMargin);
                    }
            }
            FromRects(p, boxBody ? BodyHalf.x : bodyRadius, true);
            if (shoulderSide != 0f) FromRects(p + SideOff, ShoulderR, false);
            // уступы выше порога: пробы по кругу на краю тела и чуть дальше
            if (HasSurface)
                for (int i = 0; i < 8; i++)
                {
                    Vector3 dir = Quaternion.AngleAxis(i * 45f, Vector3.up) * Vector3.forward;
                    if (!Climbable(p + dir * bodyRadius * 0.95f)) rep -= dir * 0.6f;
                    else if (!Climbable(p + dir * (bodyRadius + avoidMargin))) rep -= dir * 0.3f;
                }
            return rep;
        }

        /// <summary>Нормаль от препятствий в точке (сумма от всех задетых, наружу).</summary>
        Vector3 ContactNormal(Vector3 p, float r)
        {
            bool box = boxBody && r == bodyRadius && bodyCube;
            if (box) r = BodyReach + 0.01f;                          // кубик: касание — до угла (блок Body); уступы — пробы на этом радиусе
            Vector3 n = Vector3.zero;
            foreach (var o in obstacles)
            {
                if (box) { if (!PhysBodyActive && BoxRectNormal(p, o, 0.01f, out var bn)) n += bn; continue; }   // по форме кубика (блок Body); в физике — стены решает физика
                float cx = Mathf.Clamp(p.x, o.xMin, o.xMax), cz = Mathf.Clamp(p.z, o.yMin, o.yMax);
                Vector3 d = new Vector3(p.x - cx, 0f, p.z - cz);
                float m = d.magnitude;
                if (m >= r) continue;
                if (m > 1e-5f) n += d / m;
                else n += new Vector3(p.x - o.center.x, 0f, p.z - o.center.y).normalized;   // центр внутри — от середины
            }
            // плечо упёрлось в стену — нормаль от стены к плечу
            if (shoulderSide != 0f)
            {
                Vector3 S = Flat(p) + SideOff;
                foreach (var o in obstacles)
                {
                    float cx = Mathf.Clamp(S.x, o.xMin, o.xMax), cz = Mathf.Clamp(S.z, o.yMin, o.yMax);
                    Vector3 d = new Vector3(S.x - cx, 0f, S.z - cz);
                    float m = d.magnitude;
                    if (m >= ShoulderR * 1.5f) continue;
                    n += m > 1e-5f ? d / m : new Vector3(S.x - o.center.x, 0f, S.z - o.center.y).normalized;
                }
            }
            // уступ выше порога — тоже стена: нормаль от тех сторон, где не залезть
            if (HasSurface)
                for (int i = 0; i < 8; i++)
                {
                    Vector3 dir = Quaternion.AngleAxis(i * 45f, Vector3.up) * Vector3.forward;
                    if (!Climbable(p + dir * r * 0.95f)) n -= dir;
                }
            return n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.zero;
        }

        /// <summary>Куда обходить стену, упёршись в лоб: к кисти (в хвате — точка хвата, её поставила мышь), от тела.
        /// Только кисть на полу своего уровня (автор 2026-11-23: «не могу подтягиваться — рука не сгибается»): держишься за камень,
        /// поручень или кромку на стене — упор в стену и есть подтягивание, тело не уводится вбок (ноль — не обходить).</summary>
        Vector3 SteerSide(Vector3 p) => mode == Mode.Grip && !gripHold && !ClimbGrip && !Hanging ? Flat(gripHand - p) : Vector3.zero;

        /// <summary>Вытолкнуть круг тела из препятствий наружу (по нормали, небольшими шагами).</summary>
        Vector3 Depenetrate(Vector3 p)
        {
            for (int i = 0; i < 40 && Blocked(p, bodyRadius); i++)
            {
                Vector3 n = ContactNormal(p, bodyRadius);
                if (n == Vector3.zero) n = Vector3.forward;
                p += n * 0.02f;
            }
            return p;
        }

        /// <summary>Скольжение круга вдоль стен: убрать составляющую «в стену», остаток ослабить трением; до 3 стен (угол).</summary>
        Vector3 Slide(Vector3 from, Vector3 delta, bool steer = false)
        {
            Vector3 p = from;
            // в хвате рука (плечо → кисть) не должна резать тело на новом месте — глубже, чем сейчас (скольжение вбок уводит плечо с линии)
            float arm0 = mode == Mode.Grip ? ArmToBodyGrip(Flat(from), Flat(from) + SideOff) : 1f;
            bool Stop(Vector3 q)
            {
                if (Blocked(q, bodyRadius)) return true;
                if (mode != Mode.Grip || armKeepOut <= 0f) return false;
                float a = ArmToBodyGrip(Flat(q), Flat(q) + SideOff);
                return a < armKeepOut && a < arm0 - 1e-4f;
            }
            for (int i = 0; i < 3 && delta.sqrMagnitude > 1e-10f; i++)
            {
                Vector3 q = p + delta;
                if (!Stop(q)) return q;
                // до касания — бисекцией
                float lo = 0f, hi = 1f;
                for (int k = 0; k < 10; k++) { float mid = (lo + hi) * 0.5f; if (Stop(p + delta * mid)) hi = mid; else lo = mid; }
                p += delta * lo;
                Vector3 n = ContactNormal(p + delta * hi, bodyRadius);
                if (n == Vector3.zero) break;
                Vector3 rest = delta * (1f - lo);
                float into = Vector3.Dot(rest, n);
                rest -= n * Mathf.Min(0f, into);                       // убрать «в стену»
                // УПЁРСЯ ПОЧТИ В ЛОБ — ХОД ВДОЛЬ СТЕНЫ В СТОРОНУ РУКИ (автор 2026-11-23: «ты можешь сам менять вектор движения, если
                // я втыкаюсь в стены, причём в сторону мышки»; «толкаюсь вперёд в столб — вектор поворачивает в сторону руки»):
                // «в стену» больше, чем вдоль, — остаток хода идёт вдоль стены туда, где кисть. Только ход тела рукой (steer).
                if (steer && into < 0f && rest.magnitude < -into)
                {
                    Vector3 tg = Vector3.Cross(Vector3.up, n); tg.y = 0f;
                    Vector3 side = SteerSide(p);
                    float sd = Vector3.Dot(tg, side);
                    if (side.sqrMagnitude > 1e-8f)                     // кисть на зацепе / кромке — не обходить
                    {
                        if (Mathf.Abs(sd) > 1e-4f) { if (sd < 0f) tg = -tg; }
                        else if (Vector3.Dot(tg, rest) < 0f) tg = -tg;
                        rest = tg.normalized * Mathf.Sqrt(into * into + rest.sqrMagnitude);
                    }
                }
                delta = rest * (1f - wallFriction);
            }
            return p;
        }

        /// <summary>Линия хода заново: через кисть и новое место плеча (рука не длиннее L и не короче dMin).</summary>
        void RebuildLine(Vector3 body)
        {
            body.y = 0f;
            Vector3 dir = gripHand - body; dir.y = 0f;
            float d = dir.magnitude;
            if (d < 1e-4f) return;
            dir /= d;
            gripL = ComputeGripL();
            farEnd = gripHand - dir * ArmLenMax(dir, d);
            nearEnd = gripHand - dir * Mathf.Min(dMin, d);
            float len = (nearEnd - farEnd).magnitude;
            t = Mathf.Clamp01(Vector3.Dot(body - farEnd, dir) / Mathf.Max(len, 1e-4f));
        }

        /// <summary>Кисть не встаёт внутрь препятствия: выталкивается к ближайшему краю (с зазором).</summary>
        Vector3 PushOut(Vector3 p)
        {
            const float gap = 0.03f;
            foreach (var o in obstacles)
            {
                if (p.x <= o.xMin - gap || p.x >= o.xMax + gap || p.z <= o.yMin - gap || p.z >= o.yMax + gap) continue;
                float l = p.x - (o.xMin - gap), r = (o.xMax + gap) - p.x, d = p.z - (o.yMin - gap), u = (o.yMax + gap) - p.z;
                float m = Mathf.Min(Mathf.Min(l, r), Mathf.Min(d, u));
                if (m == l) p.x = o.xMin - gap; else if (m == r) p.x = o.xMax + gap; else if (m == d) p.z = o.yMin - gap; else p.z = o.yMax + gap;
            }
            return p;
        }

        LineRenderer Line(string n, Color c, float w)
        {
            var g = new GameObject(n); g.transform.SetParent(transform, false);
            var l = g.AddComponent<LineRenderer>();
            l.widthMultiplier = w; l.positionCount = 0; l.useWorldSpace = true;
            l.material = new Material(Shader.Find("Sprites/Default")); l.startColor = l.endColor = c;
            return l;
        }

        bool FloorPoint(Vector2 px, out Vector3 p)
        {
            // курсор — луч на ПОВЕРХНОСТЬ (выступ, ступень, пол); дальше алгоритм берёт только XZ этой точки
            // ПЕРЕКРЫТИЕ НАД ГОЛОВОЙ (крыша тоннеля, мост сверху): попали в поверхность выше своего уровня, а под ней есть пол
            // своего уровня — луч идёт сквозь неё дальше, на свой пол. Иначе в тоннеле рука ставилась на 0.5 м ближе к камере.
            p = default;
            Ray r = cam.ScreenPointToRay(px);
            for (int k = 0; k < 4; k++)
            {
                if (!SurfaceRay(r, out var h)) return false;
                if (h.y > BodyLevel + maxStep + 0.05f)
                {
                    float below = LevelHeight(Flat(h), BodyLevel, handUp);
                    if (!float.IsInfinity(below) && below < h.y - 0.1f) { r = new Ray(h + r.direction * 0.003f, r.direction); continue; }
                }
                p = Flat(h); return true;
            }
            return false;
        }

        /// <summary>Кисть уехала с опорой дальше руки — тело подтягивается к ней по полу на излишек (через скольжение: в стену не входит).
        /// Излишек больше armFollowMax за шаг — это рывок опоры, а не езда: рука не выдерживает, хват срывается (как раньше).</summary>
        void ArmFollowsHand()
        {
            float gl = ComputeGripL();
            Vector3 v = Flat(gripHand - Shoulder); float d = v.magnitude;
            float over = d - gl;
            if (over <= 0.002f || d < 1e-4f || over > armFollowMax) return;
            Vector3 np = Slide(Body, v / d * over);
            MoveBodyXZ(np.x, np.z);
        }

        [Tooltip("Держишься за едущее: рука подтягивает тело не больше чем на столько за шаг (м); больше — рывок, хват срывается.")]
        public float armFollowMax = 0.05f;

        /// <summary>ПЛАТФОРМЫ: тело на подвижной поверхности едет с ней (сдвиг, подъём, поворот); кисть, держащаяся за платформу, — тоже.
        /// Тело и кисть на разных опорах: линия хода через новое плечо; кисть уехала — тело едет за ней (буксир); не достаёт (тело упёрлось или рывок) — хват срывается.</summary>
        void Carry()
        {
            bool bodyMoved = false, handMoved = false;
            carryVel = Vector3.zero;
            if (rideMover && rideMover.StepId != rideStep)
            {
                rideStep = rideMover.StepId;
                Matrix4x4 D = rideMover.Delta;
                Vector3 np = D.MultiplyPoint3x4(new Vector3(bodyCube.position.x, groundY, bodyCube.position.z));
                Vector3 f = D.MultiplyVector(bodyCube.forward); f.y = 0f;
                if (f.sqrMagnitude > 1e-6f) bodyCube.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
                carryVel = (np - new Vector3(bodyCube.position.x, groundY, bodyCube.position.z)) / Mathf.Max(dtNow, 1e-4f);   // скорость с платформы (для слёта)
                SetBodyPos(new Vector3(np.x, np.y + bodyLift, np.z));
                groundY = np.y;
                bodyMoved = true;
            }
            if (mode == Mode.Grip && handMover && handMover.StepId != handStep)
            {
                handStep = handMover.StepId;
                Matrix4x4 D = handMover.Delta;
                gripHand = Flat(D.MultiplyPoint3x4(gripHand));
                if (gripHold) gripHand = Flat(gripHold.At(gripHoldS));      // зацеп — точно по его точке
                hand = gripHand;
                farEnd = Flat(D.MultiplyPoint3x4(farEnd)); nearEnd = Flat(D.MultiplyPoint3x4(nearEnd));
                handMoved = true;
            }
            if (mode == Mode.Grip && (bodyMoved || handMoved) && !(bodyMoved && handMoved && rideMover == handMover))
            {
                // ДЕРЖИШЬСЯ ЗА ЕДУЩЕЕ (автор 2026-11-20: «зацепился — и держусь, тело тянется за подвижной стеной»):
                // опора кисти уехала дальше руки — рука ведёт тело за ней по полу (как ArmReachStep на склоне и поворот на месте).
                // Срыв — только если телу некуда идти (стена, уступ, край за порогом). Тело на своей платформе, кисть на неподвижном —
                // по-прежнему срыв (платформа сильнее руки).
                // Автор: «зацепился — и катаешься; тело должно двигаться за стеной, а не стоять»: тело едет ВМЕСТЕ с опорой кисти
                // на тот же сдвиг (буксир), сразу, а не только когда рука натянулась. Упёрлось (стена, край) — доводит рука (ArmFollowsHand),
                // не достаёт — срыв. Сдвиг больше armFollowMax за шаг (рывок опоры) — не буксир.
                if (handMoved && rideMover != handMover)
                {
                    Vector3 b = Body, tow = Flat(handMover.Delta.MultiplyPoint3x4(b)) - b;
                    if (tow.sqrMagnitude > 1e-10f && tow.magnitude <= armFollowMax)
                    {
                        Vector3 np = Slide(b, tow);
                        MoveBodyXZ(np.x, np.z);
                    }
                    ArmFollowsHand();
                }
                float dh = Flat(gripHand - Shoulder).magnitude;
                bool lost = gripHold ? (gripHold.At(gripHoldS) - Shoulder3).magnitude > L + 0.02f : (dh > ComputeGripL() + 0.02f || !HandOk(gripHand));   // досягаемость с учётом высоты (не L)
                lost |= ArmCutsBody();                                   // диск повернул тело, кисть на полу — рука прошла бы сквозь тело
                if (lost) ReleaseGrip("сорвался: опора ушла дальше руки");   // сорвался
                else { float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0); }
            }
        }

        // ---------------- ввод: мышь или внешний водитель (бот / тест) ----------------
        /// <summary>Кадр ввода: курсор (пиксели), сдвиг мыши за кадр, ЛКМ зажата / нажата в этом кадре, облёт камеры.</summary>
        public struct InputFrame { public Vector2 pos, delta; public bool pressed, down, orbit; }
        /// <summary>Не null — ввод берётся отсюда, а не с мыши (бот, сценарии). Путь дальше — тот же, что у мыши.</summary>
        [System.NonSerialized] public System.Func<InputFrame> driver;
        /// <summary>Симуляция: Update ничего не делает, шаги вызываются снаружи Tick(dt) (сторожа, бот).</summary>
        public static bool ManualTick;
        float dtNow = 0.02f;

        // ШАГ НЕ ДЛИННЕЕ maxStepDt, КАЖДЫЙ КАДР (шаг 2 долга). Кадр делится на шаги ≤ 0.02 с (как у бота HeroSim.Dt):
        // подвис кадр на 0.3 с — 15 ровных шагов, а не один длинный: тело не прыгает, стена не проскакивает сквозь тело.
        // Быстрый монитор — один короткий шаг на кадр: картинка гладкая без сглаживания.
        // (Проба FixedUpdate 50/с: на мониторе 247 кадров/с карусель и тело шли ступеньками — автор: «дёргается на вращении»;
        //  сглаживать пришлось бы каждый кубик и каждую линию подсказок.)
        // Платформы — тем же шагом и раньше героя. Подвис дольше maxCatchUp — игра на миг замедлится, но не прыгнет.
        [Tooltip("Самый длинный шаг модели (с). Длинный кадр делится на такие шаги.")]
        public float maxStepDt = 0.02f;
        [Tooltip("Больше этого времени (с) за кадр не догоняем — подвис дольше просто замедляет игру.")]
        public float maxCatchUp = 0.25f;
        /// <summary>Платформы, которые шагает герой (каждый шаг — раньше героя).</summary>
        public static readonly System.Collections.Generic.List<ProtoMover> LiveMovers = new System.Collections.Generic.List<ProtoMover>();
        InputFrame mouseAcc; bool mouseHave;
        public static int LiveHeroes;
        void OnEnable() { LiveHeroes++; }
        void OnDisable() { LiveHeroes = Mathf.Max(0, LiveHeroes - 1); if (bodyRb) Physics.simulationMode = SimulationMode.FixedUpdate; if (cursorConfined) { Cursor.lockState = CursorLockMode.None; cursorConfined = false; } }
        [Tooltip("Пока держишь ЛКМ в хвате, курсор не выходит за окно игры — тяга мышью не обрывается у края.")]
        public bool holdConfinesCursor = true;
        bool cursorConfined;
        [Tooltip("Первые столько секунд после хвата «отпустить» и срыв спиной не копятся: движение мыши, которым целился, не разжимает кисть.")]
        public float letGoGrace = 0.4f;
        float gripAge;

        void Update()
        {
            if (ManualTick) return;
            if (driver == null)
            {
                var m = Mouse.current;
                if (m != null)
                {
                    mouseAcc.pos = m.position.ReadValue();
                    mouseAcc.delta += m.delta.ReadValue();
                    mouseAcc.pressed = m.leftButton.isPressed;
                    mouseAcc.down |= m.leftButton.wasPressedThisFrame;
                    mouseAcc.orbit = ProtoOrbitCamera.Orbiting;
                    mouseHave = true;
                }
                // КУРСОР НЕ УХОДИТ ЗА ОКНО, ПОКА ДЕРЖИШЬ (автор 2026-11-21: «мышка уже за экраном» — тяга «на себя» обрывалась у края
                // окна). В хвате курсор не читается, только сдвиг мыши; отпустил ЛКМ — курсор свободен.
                bool confine = holdConfinesCursor && mouseAcc.pressed && (mode == Mode.Grip || dragItem);
                if (confine != cursorConfined) { Cursor.lockState = confine ? CursorLockMode.Confined : CursorLockMode.None; cursorConfined = confine; }
            }
            float rest = Mathf.Min(Time.deltaTime, maxCatchUp);
            while (rest > 1e-6f)
            {
                float dt = Mathf.Min(rest, Mathf.Max(0.002f, maxStepDt));
                for (int i = 0; i < LiveMovers.Count; i++) if (LiveMovers[i] && LiveMovers[i].autoRun) LiveMovers[i].Step(dt);
                Tick(dt);
                rest -= dt;
            }
        }

        string pullWhy = "";              // диагностика: почему тяга / ход «на себя» стоит (строка состояния)

        void TickLogic(float dt)
        {
            dtNow = dt;
            pullWhy = "";
            gripAge = mode == Mode.Grip ? gripAge + dt : 0f;                // сколько держишься (окно после хвата — letGoGrace)
            Carry();
            PushStep();                                               // подвижная стена толкает тело / зажим (блок Push)
            if (!cam) return;
            InputFrame inp;
            if (driver != null) inp = driver();
            else
            {
                if (!mouseHave) return;
                inp = mouseAcc;
                mouseAcc.delta = Vector2.zero; mouseAcc.down = false;   // потрачено этим шагом; кнопка и курсор — как были
            }
            // зона ввода сдвинута вниз: точка руки = курсор + сдвиг вверх (жесты — по смещению мыши, их сдвиг не меняет)
            Vector2 px = inp.pos + new Vector2(0f, inputShift * Screen.height);
            bool orbit = inp.orbit;                                 // ПКМ — облёт камеры: рука и жест замирают
            if (FallStep(dt, ref inp, px)) { EndDrag("падение — предмет выпал"); BackSlotStep(); ClimbVisuals(dt); StateStep(dt); Draw(); return; }   // полёт / оглушение (блок Fall)

            if (mode == Mode.Free && DragFreeStep(dt, inp, px, orbit))
            {
                // ПРЕДМЕТ (блок Drag): навёлся на предмет / тащишь его — кисть занята предметом, тело стоит
            }
            else if (mode == Mode.Free)
            {
                // ЗАЦЕП: курсор рядом с перилами / столбом — кисть прилипает к ним (иначе — на поверхность, как раньше)
                hoverHold = null;
                if (!orbit && FindHold(px, out var hh, out float hs)) { hoverHold = hh; hoverS = hs; }
                if (!orbit && hoverHold)
                    hand = Vector3.Lerp(hand, Flat(hoverHold.At(hoverS)), 1f - Mathf.Exp(-handFollow * dt));
                else if (!orbit && FloorPoint(px, out var p))
                {
                    Vector3 want = HandAtLedge(Reach(MirrorBehind(p, out _)));   // курсор за спиной — зеркально перед собой, у тела
                    hand = Vector3.Lerp(hand, want, 1f - Mathf.Exp(-handFollow * dt));
                }
                hand = HandOutOfBody(FreeHandAroundBody(hand));          // свободная кисть, догоняя курсор, не проходит сквозь тело и не внутри кубика
                // ВВОД РАБОТАЕТ ВСЕГДА (даже вплотную к уступу): кисть встаёт у кромки, хват есть.
                // Отсекается уже исполнение: тело не въедет в уступ выше порога (Blocked), а отъехать / развернуться — можно.
                if (!orbit && inp.down && !ManualTick) GrabWhy(px);       // журнал хватов (диагностика, только живая игра)
                if (!orbit && inp.down && hoverHold) BeginGripHold(hoverHold, hoverS);
                else if (!orbit && inp.down && FloorPoint(px, out var c))
                {
                    Vector3 cm = MirrorBehind(c, out bool behind);
                    BeginGrip(HandAtLedge(Reach(cm)));
                    mirrorGrip = behind;                                  // только для строки состояния: жест прежний
                }
            }
            else
            {
                if (!inp.pressed && !testHold)
                {
                    ReleaseGrip("отпустил ЛКМ"); lastGestureAlong = lastGestureSide = Vector2.zero;
                }
                else if (!orbit) Gesture(inp.delta);
                HandSlipStep(dt);                                     // кисть на крутом (не зацеп) — съезжает и отпускает
                hand = gripHand;
                // плавный поворот на месте: доворачиваем часть накопленного угла
                if (Mathf.Abs(pendingTurn) > 1e-3f)
                {
                    float step = pendingTurn * (1f - Mathf.Exp(-turnSmooth * dt));
                    Quaternion was = bodyCube.rotation;
                    bool wasBlocked = boxBody && Blocked(Body, bodyRadius);
                    float armBefore = ArmToBodyGrip(Body, Shoulder);
                    float dhWas = Flat(gripHand - Shoulder).magnitude;           // кисть ближе dMin (у плеча) — поворот её не отталкивает
                    bodyCube.rotation = Quaternion.AngleAxis(step, Vector3.up) * was;
                    pendingTurn -= step;
                    // рука вошла бы в тело (и глубже, чем была) — поворот стоп
                    float armAfter = ArmToBodyGrip(Body, Shoulder);
                    if (armKeepOut > 0f && armAfter < armKeepOut && armAfter < armBefore) { bodyCube.rotation = was; pendingTurn = 0f; }
                    else
                    // тело крутится вокруг своего центра, плечо сбоку обходит его — линия хода заново через новое плечо.
                    // Плечо ушло бы от кисти дальше досягаемости или ближе dMin — РУКА ВЕДЁТ ТЕЛО: тело сдвигается вдоль руки
                    // на излишек (подтягивается / отталкивается), поворот идёт дальше. Раньше поворот вставал: при вытянутой
                    // руке (и при жесте с долей «от себя») в одну сторону не крутилось почти совсем (автор: «зона 1 / зона 2»).
                    // Стоп — только если телу некуда сдвинуться (стена, уступ) или плечо в стене.
                    if (shoulderSide != 0f)
                    {
                        Vector3 bodyWas = bodyCube.position;
                        float gl = ComputeGripL();                                    // досягаемость — с учётом высоты зацепа
                        Vector3 v = Flat(gripHand - Shoulder); float dh = v.magnitude;
                        // РЫВОК (автор 2026-11-22: «поставить в центр и повести — первые расстояния он тп»): кисть ближе dMin
                        // (у плеча), и любой поворот сразу отталкивал тело до dMin — до 12 см за шаг. Нижний предел — как было до поворота.
                        float dLo = Mathf.Min(dMin, dhWas);
                        float keep = Mathf.Clamp(dh, dLo, gl);
                        if (Mathf.Abs(dh - keep) > 1e-4f && dh > 1e-4f)
                        {
                            Vector3 np = Slide(Body, v / dh * (dh - keep));            // + к кисти (рука длинна), − от кисти (согнута до упора)
                            MoveBodyXZ(np.x, np.z);
                            dh = Flat(gripHand - Shoulder).magnitude;
                        }
                        bool armIn = armKeepOut > 0f && ArmToBodyGrip(Body, Shoulder) < armKeepOut && ArmToBodyGrip(Body, Shoulder) < armBefore;
                        bool boxIn = boxBody && !wasBlocked && Blocked(Body, bodyRadius);   // угол кубика вошёл бы в стену / уступ (блок Body)
                        if (dh > gl + 0.005f || dh < dLo - 0.005f || ShoulderBlocked(Body) || armIn || boxIn)
                        { bodyCube.rotation = was; SetBodyPos(bodyWas); pendingTurn = 0f; }
                        else { float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0); }
                    }
                }
                // тело — к позиции на траектории (траектория — путь ПЛЕЧА, тело сбоку от него)
                float tn = Mathf.Lerp(t, tTarget, 1f - Mathf.Exp(-follow * dt));
                Vector3 off = SideOff;
                // застряли в стене (старая ошибка / прыжок) — сначала вытолкнуть наружу, линию хода — через новое плечо
                if (Blocked(Body, bodyRadius))
                {
                    Vector3 fixedB = Depenetrate(Body);
                    MoveBodyXZ(fixedB.x, fixedB.z);
                    float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0); tn = Mathf.Lerp(t, tTarget, 1f - Mathf.Exp(-follow * dt));
                }
                Vector3 cur = Body, want = Vector3.Lerp(farEnd, nearEnd, tn) - off;   // от ФАКТИЧЕСКОГО места тела, а не от точки линии
                // АВТООБХОД: анти-вектор от препятствий рядом уводит ход вбок — в углу (стойка + основание) тело не заклинивает
                bool steered = false;
                Vector3 delta = want - cur;
                float dm = delta.magnitude;
                if (avoidGain > 0f && dm > 1e-5f)
                {
                    Vector3 rep = Repulsion(cur);
                    if (rep.sqrMagnitude > 1e-6f)
                    {
                        Vector3 sd = delta + rep * dm * avoidGain;
                        if (sd.sqrMagnitude > 1e-10f) { want = cur + sd.normalized * dm; steered = true; }
                    }
                }
                if (steered || Blocked(want, bodyRadius))
                {
                    // «материал стены»: тело не входит в препятствие, а скользит вдоль него (трение wallFriction);
                    // в углу — по очереди по каждой стене. Потом линия хода перестраивается через кисть и новое место плеча.
                    Vector3 np = Slide(cur, want - cur, true);
                    if ((np - cur).sqrMagnitude < 1e-8f && pullWhy.Length == 0) pullWhy = "ход: " + BlockWhy(want);   // диагностика
                    float rest = tTarget - tn;
                    RebuildLine(np + off);
                    tTarget = Mathf.Clamp01(t + rest);
                    tn = t;
                }
                t = tn;
                Vector3 b = Vector3.Lerp(farEnd, nearEnd, t) - off;   // траектория плеча → тело
                if (Blocked(b, bodyRadius)) b = Slide(Body, b - Body, true); // никогда не ставить тело в стену
                MoveBodyXZ(b.x, b.z);
            }
            SlopeStep(dt);                                            // крутой склон без опоры — сползание
            SpinStep(dt);                                             // карусель: центробежная зона, слёт
            ClimbDecay(dt);                                           // отпустил кромку до перевала — назад вниз
            Place();
            BackSlotStep();                                           // предмет на спине — на место слота (блок Back, LOCK)
            if (mode == Mode.Free && !dragItem) gestureOwner = "";
            StateStep(dt);                                            // состояния: выбрать, записать переход (блок State)
            ClimbVisuals(dt);
            Draw();
        }

        void LateUpdate()
        {
            if (!cameraFollow || !cam || cam.GetComponent<ProtoOrbitCamera>()) return;
            Vector3 want = bodyCube.position + camOffset;
            cam.transform.position = Vector3.Lerp(cam.transform.position, want, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        public void BeginGrip(Vector3 c)
        {
            mode = Mode.Grip; gripHand = c; hand = c; gripHold = null; mirrorGrip = false;          // кисть — сразу в точке хвата (иначе кадр «рука длиннее»)
            gripTop = HandHit(c, out handMover, out var gn);               // кисть на платформе — поедет с ней
            if (float.IsInfinity(gripTop)) gripTop = groundY;
            // кисть на крутом склоне не держит — съедет и отпустит (видно)
            gripSlip = Vector3.Angle(gn, Vector3.up) > steepAngle; slipDist = 0f;
            gripL = ComputeGripL();
            handStep = handMover ? handMover.StepId : 0;
            Vector3 dir = (c - Shoulder); dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            dir.Normalize();
            farEnd = c - dir * ArmLenMax(dir, Flat(c - Shoulder).magnitude);   // плечо при вытянутой руке (или пока рука не упрётся в своё тело)
            // плечо при руке, согнутой до упора; кисть у кромки ближе dMin — ближний конец там, где плечо сейчас (без рывка назад)
            nearEnd = c - dir * Mathf.Min(dMin, Flat(c - Shoulder).magnitude);
            // текущая позиция плеча — проекция на траекторию
            float len = (nearEnd - farEnd).magnitude;
            t = tTarget = Mathf.Clamp01(Vector3.Dot(Shoulder - farEnd, dir) / Mathf.Max(len, 1e-4f));
            // плечо сразу на прямую, тело — сбоку от плеча (не в стену)
            Vector3 b = Vector3.Lerp(farEnd, nearEnd, t) - SideOff;
            if (Blocked(b, bodyRadius)) { b = Slide(Body, b - Body); RebuildLine(b + SideOff); tTarget = t; }
            MoveBodyXZ(b.x, b.z);
        }

        /// <summary>Жест: только составляющая вдоль траектории на экране; 1 пиксель вдоль = столько же метров, сколько он занимает на экране.</summary>
        /// <summary>Тест: протянуть мышь на себя вдоль линии на px пикселей (отрицательное — от себя).</summary>
        public void TestPull(float px)
        {
            Vector2 a = (Vector2)cam.WorldToScreenPoint(nearEnd) - (Vector2)cam.WorldToScreenPoint(farEnd);
            if (a.sqrMagnitude > 1f) Gesture(-a.normalized * px);
        }

        [Tooltip("Задний ход как в автомате: тело едет спиной — поворот мышью переворачивается (мышь вправо — ход уходит вправо).")]
        public bool reverseSteer = true;
        bool reverseGear;
        /// <summary>Сейчас задний ход и руль перевёрнут.</summary>
        // Хват из-за спины (MirrorGrip) — руль прямой ВСЕГДА, при любом жесте (автор 2026-11-22, по экрану: мышь вверх-влево —
        // тело едет назад и уходит ВНИЗ-ВЛЕВО, лицо доворачивает вправо). Раньше ход назад включал задний ход, и на диагонали
        // руль переворачивался через раз (вертикаль ≈ боковой).
        public bool ReverseSteering => reverseSteer && reverseGear && !mirrorGrip && mode == Mode.Grip;

        [Tooltip("Рука через грудь и «от себя» в упор: на столько градусов на пиксель мыши тело доворачивается, выводя руку из груди (ход назад продолжается).")]
        public float backOutDegPerPx = 0.3f;

        /// <summary>РУКА ВЫВОДИТ ТЕЛО (автор 2026-11-22: «поставил руку поближе — не могу сдвинуться ни вперёд, ни назад, стрелочка
        /// совсем маленькая»). Кисть у плеча через грудь: ближе — рука согнута до упора (dMin), дальше — вытянутая рука прошла бы
        /// сквозь тело (ArmLenMax обрезает дальний конец), и линия хода — 2 см. Теперь «от себя» в дальний конец, обрезанный своим
        /// телом, доворачивает тело в ту сторону, где руке свободнее (как поворот на месте: рука ведёт тело, стены и уступы — те же
        /// проверки). Довернулся — линия перестраивается длиннее, и тело едет назад дальше.</summary>
        void BackOutTurn(float along)
        {
            if (along >= 0f || !allowTurn || Hanging || backOutDegPerPx <= 0f || armKeepOut <= 0f) return;
            if (tTarget > 0.02f) return;                                         // ещё не в дальнем конце
            Vector3 v = Flat(gripHand - Shoulder); if (v.sqrMagnitude < 1e-6f) return;
            float now = ArmLenMax0(v.normalized);
            if (now >= gripL - 0.01f) return;                                    // дальний конец — длина руки, не своё тело
            // в какую сторону довернуть: где рука из груди выходит (дальний конец длиннее)
            float best = now, sgn = 0f;
            foreach (float s in new[] { 1f, -1f })
            {
                Quaternion q = Quaternion.AngleAxis(s * 8f, Vector3.up);
                Vector3 so = q * SideOff, d2 = Flat(gripHand - (Body + so));
                if (d2.sqrMagnitude < 1e-6f) continue;
                float m = ArmLenMaxAt(so, d2.normalized);
                if (m > best + 1e-4f) { best = m; sgn = s; }
            }
            if (sgn == 0f) return;
            pendingTurn += sgn * (-along) * backOutDegPerPx;
            gestureOwner = "рука выводит тело";
        }

        [Tooltip("ВВОД ИЗ-ЗА СПИНЫ — ЗЕРКАЛЬНО: курсор за спиной — кисть встаёт перед собой по зеркальному направлению, близко к телу (behindHandDist); мышь снизу вверх («от себя») — тело едет назад на весь ход.")]
        public bool mirrorBehind = true;
        [Tooltip("Курсор за спиной: кисть встаёт перед собой на таком расстоянии от плеча (м) — близко к телу, чтобы ход назад был на всю длину руки.")]
        public float behindHandDist = 0.22f;
        bool mirrorGrip;
        /// <summary>Хват сделан курсором из-за спины (кисть зеркально перед собой, у тела).</summary>
        public bool MirrorGrip => mirrorGrip && mode == Mode.Grip;

        /// <summary>ВВОД ИЗ-ЗА СПИНЫ — ЗЕРКАЛЬНО (автор 2026-11-22: «тяну за персонажем — хочу движение назад»; «рука как раз ставится
        /// ближе к телу, затем жест снизу вверх — значит персонаж двигается вниз»). Курсор позади центра тела (по лицу): направление
        /// плечо → курсор отражается вперёд (через поперечную линию), кисть встаёт перед собой на behindHandDist от плеча — у тела.
        /// Жест прежний: мышь снизу вверх («от себя») — тело уезжает от кисти назад на весь ход руки.
        /// (Две прежние пробы отвергнуты: кисть зеркально далеко + перевёрнутый жест; кисть за спину.)</summary>
        Vector3 MirrorBehind(Vector3 p, out bool behind)
        {
            behind = false;
            if (!mirrorBehind) return p;
            Vector3 f = Face;
            if (Vector3.Dot(Flat(p) - Body, f) >= 0f) return p;
            behind = true;
            // ТОЧЕЧНОЕ ЗЕРКАЛО через центр тела (автор 2026-11-22: «мышь вверх-влево — должно уходить вниз-влево»): тело отъезжает
            // от кисти, поэтому кисть — с другой стороны: курсор сзади-справа → кисть спереди-слева → тело уходит назад-вправо, к курсору.
            // (Было отражение только вперёд-назад: кисть на той же стороне, тело уезжало в сторону, противоположную курсору.)
            Vector3 v = (Body - (Flat(p) - Body)) - Shoulder;                    // от плеча к точке, отражённой через центр тела
            if (Vector3.Dot(v, f) <= 0.01f) v = f;
            return Shoulder + v.normalized * behindHandDist;
        }

        public void Gesture(Vector2 md)
        {
            // Ledge: «от себя» до упора — отпустить; камень ниже плеча — «на себя» спускает к нему. До проверки линии: она бывает короткой на экране.
            float push = -md.y;                                       // на себя (+) — к кисти
            if (LetGoPush(push)) { gestureOwner = "отпустить"; return; }
            if (BackFallPush(push)) { gestureOwner = "срыв спиной"; return; }
            BackOutTurn(push);                                       // рука через грудь, «от себя» в упор — тело доворачивается
            Vector2 s0 = cam.WorldToScreenPoint(farEnd), s1 = cam.WorldToScreenPoint(nearEnd);
            Vector2 axis = s1 - s0;                         // экранное направление «к кисти» (тело едет так)
            float axisLen = axis.magnitude;
            if (axisLen < 1f) return;
            Vector2 a = axis / axisLen;
            // ЛОКАЛЬНАЯ раскладка жеста (экран = камера за спиной = тело): Y — езда, X — поворот.
            // Не зависит от того, где кисть на экране (раньше раскладка крутилась вместе с линией хода — поворот «съезжал»).
            float along = push;                             // мышь вниз (на себя) → тело к кисти (+), вверх — от кисти; зеркало — наоборот
            Vector2 side = new Vector2(md.x, 0f);            // мышь вправо/влево → поворот на месте
            // ВПЕРЁД / НАЗАД: тело едет лицом вперёд — быстрее, спиной — медленнее (плавно по углу хода к «лицу»)
            Vector3 travel = (nearEnd - farEnd).normalized * Mathf.Sign(along);   // куда поедет тело
            Vector3 face = Vector3.ProjectOnPlane(bodyCube.forward, Vector3.up).normalized;
            float fwd = Vector3.Dot(travel, face);                                   // +1 лицом, −1 спиной
            // ПЕРЕДАЧА (задний ход, автор 2026-11-21): тело явно едет спиной — включается «задний», лицом — «передний»;
            // вбок / без хода — передача та же, что была (без дёрганья на границе)
            // ПЕРЕДАЧА — ТОЛЬКО ОТ ЯВНОЙ ЕЗДЫ (автор 2026-11-21: «поворачиваю налево — а он сильнее направо, из-за движения назад»):
            // кисть сбоку (у края сектора, ~70°) — линия хода почти поперёк тела, «от кисти» = чуть назад (fwd −0.34). Полпикселя мыши
            // вверх при повороте включали задний ход (порог был −0.3), руль переворачивался до конца хвата.
            // Теперь передача меняется, только если жест — езда (вертикаль больше боковой) и ход явно спиной / лицом (дальше 120° / ближе 60° от лица).
            if (Mathf.Abs(along) >= 0.5f && Mathf.Abs(along) > Mathf.Abs(md.x))
            { if (fwd < -0.5f) reverseGear = true; else if (fwd > 0.5f) reverseGear = false; }
            float k = Mathf.Lerp(backSpeed, forwardSpeed, (fwd + 1f) * 0.5f);
            // слой ДВИЖЕНИЯ: кисть внутри разброса — едет полностью; в слое поворота — езда гаснет к краю (только поворот)
            Vector3 Bn = Vector3.Lerp(farEnd, nearEnd, t);
            float phi = Vector3.SignedAngle(face, gripHand - Bn, Vector3.up);       // угол кисти от лица (+ вправо)
            float phiMove = phi;
            float moveK = phiMove >= 0f
                ? 1f - Mathf.Clamp01((phiMove - sectorRight) / Mathf.Max(1f, moveFadeRight - sectorRight))
                : 1f - Mathf.Clamp01((-phiMove - sectorLeft) / Mathf.Max(1f, moveFadeLeft - sectorLeft));
            if (!moveAllAround) k *= moveK;                                      // зона — без исключений (блок Zone), лестница тоже                       // лестница вниз: камень у ног / за спиной — езда без затухания (Ledge)
            lastSpeedK = k;
            // ВСКАРАБКИВАНИЕ: у кромки, за которую держишься, жест «на себя» поднимает тело (а не двигает)
            if (!ClimbConvert(along))
            {
                tTarget = Mathf.Clamp01(tTarget + along * k / axisLen);
                if (along != 0f || md.x != 0f) gestureOwner = ReverseSteering ? "задний ход (руль наоборот)" : "езда";
            }
            else gestureOwner = "подъём";
            lastGestureAlong = -a * along * k; lastGestureSide = side;

            // ПОВОРОТ НА МЕСТЕ («лопасти вертолёта»): боковой жест поворачивает тело на месте, кисть стоит на полу —
            // её угол от лица меняется. Ограничен слоем поворота: дальше его края за один хват не довернуть.
            if (!allowTurn || Hanging) return;                    // висит на кромке — не крутится
            float mpp = (nearEnd - farEnd).magnitude / axisLen;                 // метров на пиксель вдоль линии
            float sidePx = md.x;                                                 // + — мышь вправо по экрану (как при кисти впереди)
            float r = Mathf.Max(0.12f, (gripHand - Bn).magnitude);
            // ЗАФИКСИРОВАНО автором (при камере за спиной тела): ЛКМ, мышь вправо = поворот вправо. Знак и расчёт стороны НЕ МЕНЯТЬ.
            float deg = -sidePx * mpp / r * Mathf.Rad2Deg * turnGain * turnSensitivity;
            // ЗАДНИЙ ХОД КАК В АВТОМАТЕ (автор 2026-11-21): едешь спиной — руль сам переворачивается: мышь вправо — ход уходит вправо
            // (тело поворачивается влево). Базовый знак выше не трогается: вперёд и на месте — как зафиксировано.
            if (ReverseSteering) deg = -deg;
            // накопить (вместе с ещё не довёрнутым) и не выйти за слой поворота; тело доворачивается плавно в Update
            pendingTurn += deg;
            float half = turnZone * 0.5f;                                          // зона поворота: ±половина от лица
            pendingTurn = Mathf.Clamp(pendingTurn, phi - half, phi + half);
        }

        [Tooltip("Площадка под краем тела ровнее этого угла (°) — ступенька: тело встаёт на неё, как только задело краем. Круче — склон, высота по центру.")]
        public float flatStepAngle = 10f;

        void Place()
        {
            Vector3 S = Shoulder;
            // ВЫСОТА: тело — на поверхности под своим центром (плавно), плечо — с телом, кисть — на поверхности под собой
            // уровень тела — поверхность под центром СО СВОЕГО уровня (мост/перекрытие сверху не поднимают; вниз — падает)
            float gWant = LevelHit(Body, BodyLevel, maxStep, out var rm, out var bn);
            // ТЕЛО НА СТУПЕНЬКУ СРАЗУ (автор 2026-11-22: «залез полтелом внутрь ступеньки, а должен был сразу оказаться сверху»):
            // высота бралась только под центром — центр над нижним полом, а половина кубика уже в ступеньке. Теперь — самая высокая
            // РОВНАЯ площадка под кубиком тела (центр, углы, середины сторон), не выше порога шага. Склоны (наклонная грань) не в счёт —
            // там как раньше; вскарабкивание на уступ (climbLift) не трогается.
            if (boxBody && bodyCube && climbLift <= 0.01f && !float.IsInfinity(gWant))
                foreach (var q in BodyProbes(Body))
                {
                    float hq = LevelHit(q, BodyLevel, maxStep, out var qm, out var qn);
                    if (!float.IsInfinity(hq) && hq > gWant + 0.02f && Vector3.Angle(qn, Vector3.up) < flatStepAngle) { gWant = hq; rm = qm; bn = qn; }
                }
            if (rm != rideMover) { rideMover = rm; rideStep = rm ? rm.StepId : 0; }   // встал на платформу / сошёл
            // ОПОРА УШЛА (блок Fall): под центром перепад глубже maxDrop, а рука тело не держит — падение
            if (allowFall && !falling && !float.IsInfinity(gWant) && gWant < groundY + climbLift - maxDrop - 0.02f
                && !(mode == Mode.Grip && !gripSlip && (ClimbGrip || gripHold)))
            { StartFall("опора ушла", carryVel, Vector3.zero); return; }
            if (!float.IsInfinity(gWant))
            {
                bodyNormal = bn;
                // ПЕРЕВАЛ: висел на кромке и центр тела уже над уступом — уровень тела = верх уступа, подъём докатывается плавно
                if (climbLift > 0.01f && gWant > groundY + 0.05f) { float total = groundY + climbLift; groundY = gWant; climbLift = total - gWant; crestFlash = 0.4f; }
                else groundY = Mathf.Lerp(groundY, gWant, 1f - Mathf.Exp(-heightFollow * dtNow));
            }
            SetBodyY(groundY + climbLift + bodyLift);
            // кисть: поверхность своего уровня (или зацеп); под ней уступ выше досягаемости — остаётся на прежней высоте (не прыгает наверх).
            // Высота кисти догоняет плавно (без скачка на перила и со ступени).
            float hl = HandTargetY();
            if (dragItem && mode == Mode.Free) handY = hl;                  // кисть с предметом — ровно в точке хвата (не догоняет: предмет не «скользит в руке»)
            else if (!float.IsInfinity(hl)) handY = Mathf.MoveTowards(handY, Mathf.Lerp(handY, hl, 1f - Mathf.Exp(-handFollow * dtNow)), 5f * dtNow);   // не быстрее 5 м/с
            // КУЛАК НЕ В ПОВЕРХНОСТЬ (автор 2026-11-21): высота догоняет плавно — и при быстром ходе вверх по склону кисть отставала
            // и уходила под склон. Плавно — только над поверхностью (на перила, со ступени); ниже поверхности под собой — никогда.
            float handSurf = HandLevel(hand);
            if (!float.IsInfinity(handSurf) && handY < handSurf) handY = handSurf;
            float sy = groundY + climbLift + shoulderLift, hy = handY + handLift;
            shoulderCube.position = new Vector3(S.x, sy, S.z);
            // ЛОКОТЬ — два звена в 3D, ДЛИНЫ ЗВЕНЬЕВ СОХРАНЯЮТСЯ (автор 2026-11-21: «рука научилась растягиваться» — при быстром
            // движении снизу вверх локоть считался по полу, а высота — отдельно: звенья тянулись раньше, чем локоть успевал вывернуться).
            // Теперь как при медленном движении: локоть всегда там, где сходятся плечо (upperArm) и кисть (foreArm).
            // Сгиб — наружу (вправо); локоть ушёл бы в поверхность — плоскость сгиба поворачивается вокруг оси плечо → кисть к «вверх».
            Vector3 S3 = new Vector3(S.x, sy, S.z), H3 = new Vector3(hand.x, hy, hand.z);
            Vector3 v = H3 - S3;
            Vector3 dn = v.sqrMagnitude > 1e-8f ? v.normalized : Face;
            // кисть по высоте ещё догоняет (не быстрее 5 м/с), а рука уже прямая — рисунок кисти на длине руки, звенья не тянутся
            if (v.magnitude > L * 0.999f)
            {
                H3 = S3 + dn * (L * 0.999f);
                float hs2 = HandLevel(Flat(H3));                              // и подтянутая к прямой руке — не в поверхность
                if (!float.IsInfinity(hs2) && H3.y < hs2 + handLift) H3.y = hs2 + handLift;
            }
            handCube.position = H3;
            float d = Mathf.Clamp((H3 - S3).magnitude, 1e-4f, L * 0.999f);
            float a = upperArm, b = foreArm;
            float A = Mathf.Acos(Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f));
            Vector3 outw = Vector3.Cross(Vector3.up, dn);
            if (outw.sqrMagnitude < 1e-6f) outw = SideDir;
            outw = Vector3.ProjectOnPlane(outw, dn).normalized;
            Vector3 upw = Vector3.Cross(dn, outw); if (upw.y < 0f) upw = -upw;
            float lo = Mathf.Min(sy, hy) - 0.3f, up = Mathf.Max(sy, hy) - lo + 0.05f;
            Vector3 E = S3 + (dn * Mathf.Cos(A) + outw * Mathf.Sin(A)) * a;
            for (int k = 1; k <= 9 && (ElbowUnder(E, lo, up) || SegCutsSurface(S3, E, false) || SegCutsSurface(E, H3, false)); k++)
            {
                float ang = k * 10f * Mathf.Deg2Rad;
                E = S3 + (dn * Mathf.Cos(A) + (outw * Mathf.Cos(ang) + upw * Mathf.Sin(ang)) * Mathf.Sin(A)) * a;
            }
            elbowCube.position = E;
        }

        [Tooltip("Рука (плечо → локоть → кисть) не проходит сквозь поверхность: локоть поднимается так, чтобы оба звена шли над ней с таким зазором (м).")]
        public float armClearance = 0.03f;
        /// <summary>Замер: наименьший зазор (м) между звеньями руки (плечо → локоть → кисть, по кубикам) и поверхностью под ними.
        /// Меньше нуля — рука прошла сквозь.</summary>
        public float DiagArmClearance()
        {
            Vector3 S = shoulderCube.position, E = elbowCube.position, H = handCube.position; float min = float.MaxValue;
            float lo = Mathf.Min(S.y, E.y, H.y) - 0.3f, up = Mathf.Max(S.y, E.y, H.y) - lo + 0.05f;
            for (int k = 1; k <= 9; k++) foreach (var seg in new[] { (S, E), (E, H) })
            {
                Vector3 p = Vector3.Lerp(seg.Item1, seg.Item2, k / 10f); float h = LevelHeight(new Vector3(p.x, 0f, p.z), lo, up);
                if (!float.IsInfinity(h)) min = Mathf.Min(min, p.y - h);
            }
            return min;
        }

        /// <summary>ЛИМИТ УЧИТЫВАЕТ ПОВЕРХНОСТЬ (автор 2026-11-21: «лимит не учитывает поверхность» — кисть со склона дотягивалась
        /// вниз через его край на пол, рука шла сквозь склон). Кисть НИЖЕ плеча: прямая плечо → кисть не может пройти под поверхностью.
        /// Кисть выше плеча (кромка, камень) — можно: там рука перегибается через край. q — точка кисти по полу; высота — поверхность под ней.</summary>
        bool ArmDownCutsSurface(Vector3 q)
        {
            float h = HandLevel(q);
            return !float.IsInfinity(h) && SegCutsSurface(Shoulder3, new Vector3(q.x, h + handLift, q.z), true, armCutDepth);   // край ступени — не барьер: только глубоко сквозь
        }

        [Tooltip("Лимит руки по поверхности: кисть ниже плеча не берётся, только если прямая плечо → кисть уходит под поверхность глубже этого (м). Край ступени (несколько см) — не барьер (автор 2026-11-21: «лестница сломалась»).")]
        public float armCutDepth = 0.15f;

        /// <summary>Отрезок a → b проходит под поверхностью (между их высотами; крыша над ними не в счёт)?
        /// onlyDown — только если b ниже a (тянешься вниз); depth — насколько глубоко под поверхностью считается «сквозь».</summary>
        bool SegCutsSurface(Vector3 a, Vector3 b, bool onlyDown, float depth = 0.02f)
        {
            if (!HasSurface || (onlyDown && b.y >= a.y)) return false;
            float lo = Mathf.Min(a.y, b.y) - 0.3f, up = Mathf.Max(a.y, b.y) - lo + 0.05f;
            for (int k = 1; k <= 8; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, k / 9f);
                float h = LevelHeight(new Vector3(p.x, 0f, p.z), lo, up);
                if (!float.IsInfinity(h) && h > p.y + depth) return true;
            }
            return false;
        }

        /// <summary>Локоть ниже поверхности под собой (между высотами плеча и кисти; крыша тоннеля над рукой не в счёт)?
        /// Тогда плоскость сгиба поворачивается к «вверх» (длины звеньев не меняются). Только рисунок.</summary>
        bool ElbowUnder(Vector3 E, float lo, float up)
        {
            if (!HasSurface) return false;
            float h = LevelHeight(new Vector3(E.x, 0f, E.z), lo, up);
            return !float.IsInfinity(h) && E.y < h + armClearance;
        }

        // ---------------- рисование ----------------
        const float Y = 0.02f;
        Vector3 Up(Vector3 v) => new Vector3(v.x, DrawHeight(v) + Y, v.z);   // подсказки лежат на поверхности своего уровня

        /// <summary>Отрезок a→b ломаной по поверхности (через ступени и склоны).</summary>
        void SurfLine(LineRenderer l, Vector3 a, Vector3 b, int n = 24)
        {
            l.positionCount = n + 1;
            for (int i = 0; i <= n; i++) l.SetPosition(i, Up(Vector3.Lerp(a, b, i / (float)n)));
        }

        void Draw()
        {
            Vector3 S = Shoulder;
            lnArm.enabled = drawArmLine;
            lnArm.positionCount = 3;
            lnArm.SetPositions(new[] { shoulderCube.position, elbowCube.position, handCube.position });   // рука — по самим кубикам
            if (!showHints)
            {
                foreach (var l in new[] { lnTraj, lnAxis, lnArrowA, lnArrowB, lnReach, lnGesture, lnSide, lnFace, lnTurnZone }) l.positionCount = 0;
                return;
            }
            // «лицо» тела — белая стрелка вперёд
            Vector3 B0 = Body, fF = Vector3.ProjectOnPlane(bodyCube.forward, Vector3.up).normalized, fR = Vector3.Cross(Vector3.up, fF);
            Vector3 tip = B0 + fF * 0.32f;
            lnFace.positionCount = 5;
            lnFace.SetPositions(new[] { Up(B0 + fF * 0.16f), Up(tip), Up(tip - fF * 0.07f + fR * 0.05f), Up(tip), Up(tip - fF * 0.07f - fR * 0.05f) });
            const int N = 64;
            // сектор досягаемости («пирог» перед лицом): центр → дуга от −sectorLeft до +sectorRight → центр
            lnReach.positionCount = N + 3;
            lnReach.SetPosition(0, Up(S));
            for (int i = 0; i <= N; i++)
            {
                float ang = Mathf.Lerp(-sectorLeft, sectorRight, i / (float)N);
                lnReach.SetPosition(i + 1, Up(S + Quaternion.AngleAxis(ang, Vector3.up) * Face * L));
            }
            lnReach.SetPosition(N + 2, Up(S));
            // слой поворота на месте — шире, на чуть большем радиусе
            float R2 = L * 1.25f;
            lnTurnZone.positionCount = N + 3;
            lnTurnZone.SetPosition(0, Up(S));
            for (int i = 0; i <= N; i++)
                lnTurnZone.SetPosition(i + 1, Up(S + Quaternion.AngleAxis(Mathf.Lerp(-turnLeft, turnRight, i / (float)N), Vector3.up) * Face * R2));
            lnTurnZone.SetPosition(N + 2, Up(S));
            if (mode == Mode.Grip)
            {
                SurfLine(lnTraj, farEnd, nearEnd);
                // как на схеме: зелёная прямая через тело и кисть, стрелки в обе стороны
                Vector3 dir = (nearEnd - farEnd).normalized, side = Vector3.Cross(Vector3.up, dir);
                Vector3 pA = nearEnd, pB = farEnd;                     // зелёная = ход тела: не длиннее C = a + b − сгиб
                SurfLine(lnAxis, pB, pA);
                lnArrowA.positionCount = 3; lnArrowA.SetPositions(new[] { Up(pA - dir * 0.1f + side * 0.06f), Up(pA), Up(pA - dir * 0.1f - side * 0.06f) });
                lnArrowB.positionCount = 3; lnArrowB.SetPositions(new[] { Up(pB + dir * 0.1f + side * 0.06f), Up(pB), Up(pB + dir * 0.1f - side * 0.06f) });
                DrawScreenVec(lnGesture, S, lastGestureAlong * 6f);
                DrawScreenVec(lnSide, S, lastGestureSide * 6f);
            }
            else { lnTraj.positionCount = 0; lnGesture.positionCount = 0; lnSide.positionCount = 0; lnAxis.positionCount = 0; lnArrowA.positionCount = 0; lnArrowB.positionCount = 0; }
        }

        void DrawScreenVec(LineRenderer l, Vector3 from, Vector2 v)
        {
            if (v.sqrMagnitude < 0.01f) { l.positionCount = 0; return; }
            Vector2 s0 = cam.WorldToScreenPoint(Up(from));
            if (!FloorPoint(s0 + v, out var p)) { l.positionCount = 0; return; }
            l.positionCount = 2; l.SetPositions(new[] { Up(from) + Vector3.up * 0.01f, Up(p) + Vector3.up * 0.01f });
        }

        void OnDrawGizmos()
        {
            if (!bodyCube || !showHints) return;
            Vector3 S = Application.isPlaying ? Shoulder : Flat(shoulderCube ? shoulderCube.position : bodyCube.position);
            Gizmos.color = new Color(0.5f, 0.7f, 1f, 0.5f);
            Vector3 fc = bodyCube ? Vector3.ProjectOnPlane(bodyCube.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 prevP = Up(S);
            for (int i = 0; i <= 48; i++)
            {
                Vector3 q = Up(S + Quaternion.AngleAxis(Mathf.Lerp(-sectorLeft, sectorRight, i / 48f), Vector3.up) * fc * L);
                Gizmos.DrawLine(prevP, q); prevP = q;
            }
            Gizmos.DrawLine(prevP, Up(S));
            if (!Application.isPlaying || mode != Mode.Grip) return;
            Gizmos.color = Color.yellow; Gizmos.DrawLine(Up(farEnd), Up(nearEnd));
            Gizmos.color = Color.green; Gizmos.DrawWireSphere(Up(farEnd), 0.035f);
            Gizmos.color = Color.red; Gizmos.DrawWireSphere(Up(nearEnd), 0.035f);
            Gizmos.color = Color.white; Gizmos.DrawSphere(Up(Vector3.Lerp(farEnd, nearEnd, t)), 0.025f);
            Gizmos.color = new Color(1f, 0.6f, 0.2f); Gizmos.DrawLine(Up(Vector3.Lerp(farEnd, nearEnd, tTarget)), Up(gripHand));
        }
    }
}
