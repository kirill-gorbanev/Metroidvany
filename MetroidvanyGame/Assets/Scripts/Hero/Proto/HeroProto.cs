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
    public class HeroProto : MonoBehaviour
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
        // треугольники рельефа в мире (рельеф неподвижен — собираются один раз)
        readonly System.Collections.Generic.List<Vector3> surfTris = new System.Collections.Generic.List<Vector3>();
        readonly System.Collections.Generic.List<Bounds> surfBounds = new System.Collections.Generic.List<Bounds>();
        readonly System.Collections.Generic.List<int> surfStart = new System.Collections.Generic.List<int>();
        float bodyLift, shoulderLift, elbowLift, handLift, groundY;

        [Header("Подсказки")]
        [Tooltip("Сектор, линия хода, стрелки жеста и лица. Выключено — видны только кубики.")]
        public bool showHints = false;

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
        public Vector3 BodyGround => new Vector3(bodyCube.position.x, groundY, bodyCube.position.z);
        /// <summary>Кисть на поверхности (XZ — точка алгоритма, y — поверхность под ней).</summary>
        public Vector3 HandOnSurface => new Vector3(hand.x, Height(hand), hand.z);
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
        Vector3 SideOff => Vector3.Cross(Vector3.up, Face) * shoulderSide;
        Vector3 Shoulder => Body + SideOff;

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void Start()
        {
            if (!cam) cam = Camera.main;
            CollectObstacles();
            if (!surfaceRoot) { var r = GameObject.Find("Рельеф (Y)"); if (r) surfaceRoot = r.transform; }
            CollectSurface();
            // высоты кубиков над поверхностью — как их поставил сборщик на ровном полу
            bodyLift = bodyCube.position.y; shoulderLift = shoulderCube.position.y;
            elbowLift = elbowCube.position.y; handLift = handCube.position.y;
            groundY = Height(Body);
            camOffset = cam.transform.position - bodyCube.position;
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
            surfTris.Clear(); surfBounds.Clear(); surfStart.Clear();
            if (!surfaceRoot) return;
            foreach (var mf in surfaceRoot.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh; if (!mesh) continue;
                var v = mesh.vertices; var tr = mesh.triangles; var m = mf.transform.localToWorldMatrix;
                surfStart.Add(surfTris.Count);
                var b = new Bounds(m.MultiplyPoint3x4(v[tr[0]]), Vector3.zero);
                for (int i = 0; i < tr.Length; i++) { var w = m.MultiplyPoint3x4(v[tr[i]]); surfTris.Add(w); b.Encapsulate(w); }
                b.Expand(0.002f);
                surfBounds.Add(b);
            }
        }

        /// <summary>Ближайшее пересечение луча с рельефом (обе стороны треугольников) или с полом y = 0.</summary>
        bool SurfaceRay(Ray ray, out Vector3 hit)
        {
            float best = float.MaxValue;
            if (ray.direction.y < -1e-5f) { float d = -ray.origin.y / ray.direction.y; if (d > 0f) best = d; }
            for (int k = 0; k < surfBounds.Count; k++)
            {
                if (!surfBounds[k].IntersectRay(ray, out float bd) || bd > best) continue;
                int end = k + 1 < surfStart.Count ? surfStart[k + 1] : surfTris.Count;
                for (int i = surfStart[k]; i < end; i += 3)
                    if (RayTri(ray, surfTris[i], surfTris[i + 1], surfTris[i + 2], out float d) && d < best) best = d;
            }
            hit = best < float.MaxValue ? ray.GetPoint(best) : default;
            return best < float.MaxValue;
        }

        static bool RayTri(Ray r, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(r.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det; Vector3 s = r.origin - a;
            float u = Vector3.Dot(s, p) * inv; if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(r.direction, q) * inv; if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 1e-5f;
        }

        /// <summary>Высота поверхности в точке XZ: верхняя из рельефа или пол 0. Выступ, пол, углубление — всё одна точка.</summary>
        public float Height(Vector3 p)
        {
            if (surfBounds.Count == 0) return 0f;
            return SurfaceRay(new Ray(new Vector3(p.x, 50f, p.z), Vector3.down), out var h) ? h.y : 0f;
        }

        /// <summary>Круг радиуса r в точке p задевает препятствие?</summary>
        bool Blocked(Vector3 p, float r)
        {
            foreach (var o in obstacles)
            {
                float cx = Mathf.Clamp(p.x, o.xMin, o.xMax), cz = Mathf.Clamp(p.z, o.yMin, o.yMax);
                float dx = p.x - cx, dz = p.z - cz;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        [Header("Материал стен")]
        [Tooltip("Трение о стену: 0 — лёд (полностью скользит вдоль), 1 — липкая (упёрся и стоит).")]
        [Range(0f, 1f)] public float wallFriction = 0.15f;

        /// <summary>Нормаль от препятствий в точке (сумма от всех задетых, наружу).</summary>
        Vector3 ContactNormal(Vector3 p, float r)
        {
            Vector3 n = Vector3.zero;
            foreach (var o in obstacles)
            {
                float cx = Mathf.Clamp(p.x, o.xMin, o.xMax), cz = Mathf.Clamp(p.z, o.yMin, o.yMax);
                Vector3 d = new Vector3(p.x - cx, 0f, p.z - cz);
                float m = d.magnitude;
                if (m >= r) continue;
                if (m > 1e-5f) n += d / m;
                else n += new Vector3(p.x - o.center.x, 0f, p.z - o.center.y).normalized;   // центр внутри — от середины
            }
            return n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.zero;
        }

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
        Vector3 Slide(Vector3 from, Vector3 delta)
        {
            Vector3 p = from;
            for (int i = 0; i < 3 && delta.sqrMagnitude > 1e-10f; i++)
            {
                Vector3 q = p + delta;
                if (!Blocked(q, bodyRadius)) return q;
                // до касания — бисекцией
                float lo = 0f, hi = 1f;
                for (int k = 0; k < 10; k++) { float mid = (lo + hi) * 0.5f; if (Blocked(p + delta * mid, bodyRadius)) hi = mid; else lo = mid; }
                p += delta * lo;
                Vector3 n = ContactNormal(p + delta * hi, bodyRadius);
                if (n == Vector3.zero) break;
                Vector3 rest = delta * (1f - lo);
                rest -= n * Mathf.Min(0f, Vector3.Dot(rest, n));       // убрать «в стену»
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
            farEnd = gripHand - dir * L;
            nearEnd = gripHand - dir * dMin;
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
            p = default;
            if (!SurfaceRay(cam.ScreenPointToRay(px), out var h)) return false;
            p = Flat(h); return true;
        }

        void Update()
        {
            var m = Mouse.current; if (m == null || !cam) return;
            Vector2 px = m.position.ReadValue();
            float dt = Time.deltaTime;
            bool orbit = ProtoOrbitCamera.Orbiting;                 // ПКМ — облёт камеры: рука и жест замирают

            if (mode == Mode.Free)
            {
                if (!orbit && FloorPoint(px, out var p))
                {
                    Vector3 want = PushOut(Reach(p));
                    hand = Vector3.Lerp(hand, want, 1f - Mathf.Exp(-handFollow * dt));
                }
                // точка — просто луч курсора на плоскость; дальше досягаемости — на краю круга
                if (!orbit && m.leftButton.wasPressedThisFrame && FloorPoint(px, out var c))
                    BeginGrip(PushOut(Reach(c)));
            }
            else
            {
                if (!m.leftButton.isPressed && !testHold) { mode = Mode.Free; pendingTurn = 0f; lastGestureAlong = lastGestureSide = Vector2.zero; }
                else if (!orbit) Gesture(m.delta.ReadValue());
                hand = gripHand;
                // плавный поворот на месте: доворачиваем часть накопленного угла
                if (Mathf.Abs(pendingTurn) > 1e-3f)
                {
                    float step = pendingTurn * (1f - Mathf.Exp(-turnSmooth * dt));
                    Quaternion was = bodyCube.rotation;
                    bodyCube.rotation = Quaternion.AngleAxis(step, Vector3.up) * was;
                    pendingTurn -= step;
                    // тело крутится вокруг своего центра, плечо сбоку обходит его — линия хода заново через новое плечо.
                    // Плечо не уходит от кисти дальше L и не ближе dMin: иначе линия хода «дёрнет» тело (в стену). Тогда поворот стоп.
                    if (shoulderSide != 0f)
                    {
                        float dh = Flat(gripHand - Shoulder).magnitude;
                        if (dh > L || dh < dMin) { bodyCube.rotation = was; pendingTurn = 0f; }
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
                    bodyCube.position = new Vector3(fixedB.x, bodyCube.position.y, fixedB.z);
                    float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0); tn = Mathf.Lerp(t, tTarget, 1f - Mathf.Exp(-follow * dt));
                }
                Vector3 cur = Body, want = Vector3.Lerp(farEnd, nearEnd, tn) - off;   // от ФАКТИЧЕСКОГО места тела, а не от точки линии
                if (Blocked(want, bodyRadius))
                {
                    // «материал стены»: тело не входит в препятствие, а скользит вдоль него (трение wallFriction);
                    // в углу — по очереди по каждой стене. Потом линия хода перестраивается через кисть и новое место плеча.
                    Vector3 np = Slide(cur, want - cur);
                    float rest = tTarget - tn;
                    RebuildLine(np + off);
                    tTarget = Mathf.Clamp01(t + rest);
                    tn = t;
                }
                t = tn;
                Vector3 b = Vector3.Lerp(farEnd, nearEnd, t) - off;   // траектория плеча → тело
                if (Blocked(b, bodyRadius)) b = Slide(Body, b - Body); // никогда не ставить тело в стену
                bodyCube.position = new Vector3(b.x, bodyCube.position.y, b.z);
            }
            Place();
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
            mode = Mode.Grip; gripHand = c;
            Vector3 dir = (c - Shoulder); dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            dir.Normalize();
            farEnd = c - dir * L;          // плечо при вытянутой руке
            nearEnd = c - dir * dMin;      // плечо при руке, согнутой до упора
            // текущая позиция плеча — проекция на траекторию
            float len = (nearEnd - farEnd).magnitude;
            t = tTarget = Mathf.Clamp01(Vector3.Dot(Shoulder - farEnd, dir) / Mathf.Max(len, 1e-4f));
            // плечо сразу на прямую, тело — сбоку от плеча (не в стену)
            Vector3 b = Vector3.Lerp(farEnd, nearEnd, t) - SideOff;
            if (Blocked(b, bodyRadius)) { b = Slide(Body, b - Body); RebuildLine(b + SideOff); tTarget = t; }
            bodyCube.position = new Vector3(b.x, bodyCube.position.y, b.z);
        }

        /// <summary>Жест: только составляющая вдоль траектории на экране; 1 пиксель вдоль = столько же метров, сколько он занимает на экране.</summary>
        /// <summary>Тест: протянуть мышь на себя вдоль линии на px пикселей (отрицательное — от себя).</summary>
        public void TestPull(float px)
        {
            Vector2 a = (Vector2)cam.WorldToScreenPoint(nearEnd) - (Vector2)cam.WorldToScreenPoint(farEnd);
            if (a.sqrMagnitude > 1f) Gesture(-a.normalized * px);
        }

        public void Gesture(Vector2 md)
        {
            Vector2 s0 = cam.WorldToScreenPoint(farEnd), s1 = cam.WorldToScreenPoint(nearEnd);
            Vector2 axis = s1 - s0;                         // экранное направление «к кисти» (тело едет так)
            float axisLen = axis.magnitude;
            if (axisLen < 1f) return;
            Vector2 a = axis / axisLen;
            // ЛОКАЛЬНАЯ раскладка жеста (экран = камера за спиной = тело): Y — езда, X — поворот.
            // Не зависит от того, где кисть на экране (раньше раскладка крутилась вместе с линией хода — поворот «съезжал»).
            float along = -md.y;                            // мышь вниз (на себя) → тело к кисти (+), вверх — от кисти
            Vector2 side = new Vector2(md.x, 0f);            // мышь вправо/влево → поворот на месте
            // ВПЕРЁД / НАЗАД: тело едет лицом вперёд — быстрее, спиной — медленнее (плавно по углу хода к «лицу»)
            Vector3 travel = (nearEnd - farEnd).normalized * Mathf.Sign(along);   // куда поедет тело
            Vector3 face = Vector3.ProjectOnPlane(bodyCube.forward, Vector3.up).normalized;
            float fwd = Vector3.Dot(travel, face);                                   // +1 лицом, −1 спиной
            float k = Mathf.Lerp(backSpeed, forwardSpeed, (fwd + 1f) * 0.5f);
            // слой ДВИЖЕНИЯ: кисть внутри разброса — едет полностью; в слое поворота — езда гаснет к краю (только поворот)
            Vector3 Bn = Vector3.Lerp(farEnd, nearEnd, t);
            float phi = Vector3.SignedAngle(face, gripHand - Bn, Vector3.up);       // угол кисти от лица (+ вправо)
            float moveK = phi >= 0f
                ? 1f - Mathf.Clamp01((phi - sectorRight) / Mathf.Max(1f, moveFadeRight - sectorRight))
                : 1f - Mathf.Clamp01((-phi - sectorLeft) / Mathf.Max(1f, moveFadeLeft - sectorLeft));
            if (!moveAllAround) k *= moveK;
            lastSpeedK = k;
            tTarget = Mathf.Clamp01(tTarget + along * k / axisLen);
            lastGestureAlong = -a * along * k; lastGestureSide = side;

            // ПОВОРОТ НА МЕСТЕ («лопасти вертолёта»): боковой жест поворачивает тело на месте, кисть стоит на полу —
            // её угол от лица меняется. Ограничен слоем поворота: дальше его края за один хват не довернуть.
            if (!allowTurn) return;
            float mpp = (nearEnd - farEnd).magnitude / axisLen;                 // метров на пиксель вдоль линии
            float sidePx = md.x;                                                 // + — мышь вправо по экрану (как при кисти впереди)
            float r = Mathf.Max(0.12f, (gripHand - Bn).magnitude);
            // ЗАФИКСИРОВАНО автором (при камере за спиной тела): ЛКМ, мышь вправо = поворот вправо. Знак и расчёт стороны НЕ МЕНЯТЬ.
            float deg = -sidePx * mpp / r * Mathf.Rad2Deg * turnGain * turnSensitivity;
            // накопить (вместе с ещё не довёрнутым) и не выйти за слой поворота; тело доворачивается плавно в Update
            pendingTurn += deg;
            float half = turnZone * 0.5f;                                          // зона поворота: ±половина от лица
            pendingTurn = Mathf.Clamp(pendingTurn, phi - half, phi + half);
        }

        void Place()
        {
            Vector3 S = Shoulder;
            // ВЫСОТА: тело — на поверхности под своим центром (плавно), плечо — с телом, кисть — на поверхности под собой
            groundY = Mathf.Lerp(groundY, Height(Body), 1f - Mathf.Exp(-heightFollow * Time.deltaTime));
            bodyCube.position = new Vector3(bodyCube.position.x, groundY + bodyLift, bodyCube.position.z);
            float sy = groundY + shoulderLift, hy = Height(hand) + handLift;
            shoulderCube.position = new Vector3(S.x, sy, S.z);
            handCube.position = new Vector3(hand.x, hy, hand.z);
            // локоть: два звена, сгиб наружу (вправо от направления плечо→кисть)
            Vector3 v = hand - S; float d = Mathf.Clamp(v.magnitude, 1e-4f, L * 0.999f);
            Vector3 dn = v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
            Vector3 outw = Vector3.Cross(Vector3.up, dn);
            float a = upperArm, b = foreArm;
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float A = Mathf.Acos(cosA);
            Vector3 E = S + (dn * Mathf.Cos(A) + outw * Mathf.Sin(A)) * a;
            float ey = Mathf.Max(Height(E) + elbowLift, Mathf.Lerp(sy, hy, 0.5f) - 0.05f);   // не под поверхностью
            elbowCube.position = new Vector3(E.x, ey, E.z);
        }

        // ---------------- рисование ----------------
        const float Y = 0.02f;
        Vector3 Up(Vector3 v) => new Vector3(v.x, Height(v) + Y, v.z);   // подсказки лежат на поверхности

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
