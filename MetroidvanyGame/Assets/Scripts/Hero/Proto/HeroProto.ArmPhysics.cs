using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>РУКА НА ФИЗИКЕ UNITY (автор 2026-11-25: «лучше использовать физику Unity, и тогда коллайдер сам не даст такого»;
    /// «убери подрагивание — используй rigidbody position, чтобы использовать физическое перемещение, и убрать столкновения коллизий»).
    /// ПРАВИЛА 1, 3, 4 контроллера.
    ///
    /// · Рука — два звена (плечо → локоть, локоть → кисть), у каждого кинематический Rigidbody и капсула-коллайдер на слое HeroArm.
    ///   Двигаются физически — Rigidbody.MovePosition / MoveRotation на каждом шаге физики. Суставов и скоростей нет: звенья не
    ///   спорят ни друг с другом, ни с касаниями — нечему дрожать (была дрожь: сустав и скорость тянули звено в разные стороны).
    /// · Каждый шаг поза руки идёт к позе логики (решатель локтя): плечо — с телом, локоть и кисть — по доле пути от прошлой позы
    ///   к цели. Капсула звена на новой позе задела бы твёрдое (стены, блоки, тумбы, уступы, пол) — рука доходит только до касания
    ///   (наибольшая доля пути, при которой звенья свободны). Длины звеньев сохраняются.
    /// · Предметы руке не мешают и рукой не толкаются (слои HeroArm и Item не сталкиваются; кинематика никого не толкает).
    /// · Рисуется рука по этой позе. Свободная кисть идёт за ней: упёрлась — логика кисти там же (дальше — от этого места).</summary>
    public partial class HeroProto
    {
        [Header("Рука на физике (ПРАВИЛА 1, 3, 4)")]
        [Tooltip("Рука — звенья-коллайдеры, движутся физически (MovePosition): твёрдое не пропускают, не дрожат.")]
        public bool armPhysics = true;
        [Tooltip("Толщина руки (радиус капсулы звена), м.")]
        public float armThick = 0.02f;
        [Tooltip("Свободная кисть логики идёт за физической (упёрлась — там и стоит).")]
        public bool armHandFollow = true;

        Rigidbody armUpper, armFore; CapsuleCollider armUpperCol, armForeCol;
        Vector3 armTgtS, armTgtE, armTgtH; bool armTgtHave;
        Vector3 armCurS, armCurE, armCurH; bool armCurHave;     // поза руки, где она на самом деле (по шагам физики)
        float armDragStuckT;                 // перенос: сколько кисть не достаёт до предмета (рука упёрлась)
        [Tooltip("Перенос: рука упёрлась и кисть дальше этого (м) от точки хвата на предмете дольше armDragStuckTime — предмет выпадает из руки.")]
        public float armDragBreak = 0.08f, armDragStuckTime = 0.15f;

        bool ArmPhysOn => armPhysics && itemPhysics && bodyRb;

        /// <summary>Поза, которую хочет логика (из Place): плечо, локоть, кисть.</summary>
        void ArmSetTarget(Vector3 S3, Vector3 E, Vector3 H3)
        {
            // ЛОКОТЬ — ПЛАВНО (ПРАВИЛО 3): решатель выбирает позу локтя из нескольких (мимо угла уступа, мимо стены) и между кадрами
            // может перескочить на другую — цель локтя переходит к новой позе во времени (armElbowFollow), не скачком
            // КИСТЬ — БЕЗ ОТСТАВАНИЯ (автор 2026-11-25: «рука заторможенной стала — не успевает; лучше, чтобы рука как индикатор
            // поспевала»): кисть всегда ровно в цели логики. Сглаживается только локоть, и он ставится на окружность, где обе длины
            // руки сходятся с кистью в цели (ближайшая к сглаженному локтю точка) — раньше кисть достраивалась от отстающего локтя.
            if (armTgtHave && (armTgtS - S3).magnitude < 0.5f)
            {
                Vector3 rel = Vector3.Lerp(armTgtE - armTgtS, E - S3, 1f - Mathf.Exp(-armElbowFollow * Mathf.Max(dtNow, 1e-4f)));
                E = ElbowOnCircle(S3, H3, S3 + rel, E);
            }
            armTgtS = S3; armTgtE = E; armTgtH = H3; armTgtHave = true;
        }
        /// <summary>Локоть при плече S и кисти H (длины upperArm / foreArm): точка окружности возможных локтей, ближайшая к want.
        /// Рука прямая или вырожденная — fallback.</summary>
        Vector3 ElbowOnCircle(Vector3 S, Vector3 H, Vector3 want, Vector3 fallback)
        {
            Vector3 v = H - S; float d = v.magnitude;
            if (d < 1e-4f) return fallback;
            Vector3 dn = v / d;
            d = Mathf.Min(d, (upperArm + foreArm) * 0.999f);
            float a = upperArm, b = foreArm;
            float ca = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            Vector3 C = S + dn * (a * ca);
            float r = a * Mathf.Sqrt(Mathf.Max(0f, 1f - ca * ca));
            Vector3 w = want - C; w -= dn * Vector3.Dot(w, dn);
            if (w.sqrMagnitude < 1e-8f || r < 1e-4f) return fallback;
            return C + w.normalized * r;
        }

        [Tooltip("Как быстро локоть переходит в новую позу (1/с): больше — резче.")]
        public float armElbowFollow = 18f;

        void ArmBuild()
        {
            var root = new GameObject("Рука (физика)");
            Rigidbody Seg(string n, float len, out CapsuleCollider col)
            {
                var go = new GameObject(n); go.transform.SetParent(root.transform, false);
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true; rb.useGravity = false;                       // двигается MovePosition, никого не толкает
                rb.interpolation = RigidbodyInterpolation.None;
                col = go.AddComponent<CapsuleCollider>();
                col.direction = 2; col.radius = armThick; col.height = len;
                col.center = new Vector3(0f, 0f, len * 0.5f);
                return rb;
            }
            armUpper = Seg("Плечо → локоть", upperArm, out armUpperCol);
            armFore = Seg("Локоть → кисть", foreArm, out armForeCol);
            int armL = LayerMask.NameToLayer("HeroArm"), itemL = LayerMask.NameToLayer("Item");
            if (armL >= 0)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = armL;
                if (itemL >= 0) Physics.IgnoreLayerCollision(armL, itemL, true);   // предметы рука не толкает (автор: «в игнор по слоям»)
            }
            if (bodyBox) { Physics.IgnoreCollision(armUpperCol, bodyBox); Physics.IgnoreCollision(armForeCol, bodyBox); }
            ArmPlace(armTgtS, armTgtE, armTgtH, true);
        }

        static Quaternion SegRot(Vector3 d)
        {
            if (d.sqrMagnitude < 1e-8f) d = Vector3.forward;
            Vector3 up = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(d, up);
        }

        /// <summary>Поставить звенья в позу S → E → H: физически (MovePosition / MoveRotation) или сразу (перенос героя).</summary>
        void ArmPlace(Vector3 S, Vector3 E, Vector3 H, bool snap)
        {
            Quaternion ru = SegRot(E - S), rf = SegRot(H - E);
            if (snap)
            {
                armUpper.position = S; armUpper.rotation = ru; armUpper.transform.SetPositionAndRotation(S, ru);
                armFore.position = E; armFore.rotation = rf; armFore.transform.SetPositionAndRotation(E, rf);
            }
            else
            {
                armUpper.MovePosition(S); armUpper.MoveRotation(ru);
                armFore.MovePosition(E); armFore.MoveRotation(rf);
            }
            armCurS = S; armCurE = E; armCurH = H; armCurHave = true;
        }

        /// <summary>Твёрдое для звена: всё, кроме тела, частей героя, своей руки, предметов (слой Item) и триггеров.</summary>
        bool ArmBlocker(Collider c)
        {
            // своё ТЕЛО — твёрдое для руки (автор 2026-11-25, скрин: плечевое звено уходит внутрь тела, предплечье выходит снизу —
            // «давай такое ограничим»): плечо в нескольких см от бока тела, начало звена у плеча проверка и так пропускает
            if (!c || c.isTrigger || c == armUpperCol || c == armForeCol) return false;
            if (c.attachedRigidbody && c.attachedRigidbody.GetComponent<ProtoItem>()) return false;
            return !(bodyCube && c.transform.IsChildOf(bodyCube.root));
        }

        /// <summary>Звено a → b (капсула чуть тоньше руки, кисть и плечо — без концов: кисть лежит на поверхности) не в твёрдом.</summary>
        bool ArmSegClear(Vector3 a, Vector3 b, bool trimEnd)
        {
            Vector3 d = b - a; float len = d.magnitude;
            if (len < 1e-4f) return true;
            d /= len;
            float r = armThick * 0.75f;
            Vector3 p0 = a + d * Mathf.Min(0.03f, len * 0.3f), p1 = trimEnd ? b - d * Mathf.Min(0.035f, len * 0.3f) : b;
            foreach (var c in Physics.OverlapCapsule(p0, p1, r, ~0, QueryTriggerInteraction.Ignore))
                if (ArmBlocker(c)) return false;
            return true;
        }

        bool ArmPoseClear(Vector3 S, Vector3 E, Vector3 H) => ArmSegClear(S, E, false) && ArmSegClear(E, H, true);

        /// <summary>Поза на доле f пути от прошлой (сдвинутой вместе с плечом) к цели; длины звеньев — точные.</summary>
        void ArmPoseAt(float f, Vector3 S, Vector3 E0, Vector3 H0, out Vector3 E, out Vector3 H)
        {
            E = Vector3.Lerp(E0, armTgtE, f); H = Vector3.Lerp(H0, armTgtH, f);
            Vector3 u = E - S; E = S + (u.sqrMagnitude > 1e-8f ? u.normalized : Vector3.forward) * upperArm;
            Vector3 v = H - E; H = E + (v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward) * foreArm;
        }

        /// <summary>Перед каждым шагом физики: рука — к позе логики, но не в твёрдое (доходит до касания). Перенос героя — сразу.</summary>
        void ArmDrive(float dt)
        {
            if (!ArmPhysOn || !armTgtHave || dt <= 0f) return;
            if (!armUpper) ArmBuild();
            ArmItemsOnLayer();
            Vector3 S = armTgtS;
            if (!armCurHave || (armCurS - S).magnitude > 1f) { ArmPlace(S, armTgtE, armTgtH, true); return; }   // перенос героя (зона, старт)
            Vector3 sh = S - armCurS, E0 = armCurE + sh, H0 = armCurH + sh;   // прошлая поза едет с плечом
            ArmPoseAt(0f, S, E0, H0, out E0, out H0);
            ArmPoseAt(1f, S, E0, H0, out var E1, out var H1);
            // кисть — сразу в цели; локоть — к углу позы логики на окружности возможных локтей, не быстрее armElbowSpin (не прыгает)
            if (ArmElbowToward(S, E0, E1, H1, dt, out var Et)) { ArmPlace(S, Et, H1, false); return; }
            // СТЕПЕНИ СВОБОДЫ (автор 2026-11-25: «руку всегда можно развернуть под разными углами»): поза упёрлась — кисть остаётся
            // в цели, локоть проворачивается вокруг оси плечо → кисть к ближайшему углу, где звенья свободны (плавно, armElbowSpin)
            if (ArmElbowSwing(S, E0, H1, dt, out var Es)) { ArmPlace(S, Es, H1, false); return; }
            // прошлая поза сама в твёрдом (стена наехала, рука была в теле) и выхода поворотом нет — не держать её, к цели
            if (!ArmPoseClear(S, E0, H0)) { ArmPlace(S, E1, H1, false); return; }
            // и так не пройти: КИСТЬ ОСТАЁТСЯ, ГДЕ БЫЛА (автор 2026-11-25, скрин: «левитация за пределами руки — если рука
            // заблокирована, кисть просто висит, неправильно»). Было: рука доходила до касания по прямой между прошлой и новой позой —
            // этот путь идёт по воздуху, кисть зависала над пустотой. Теперь кисть — на прошлом месте (на поверхности, в мире), локоть —
            // под нынешнее плечо; не выходит — вся прошлая поза едет с плечом (она свободна — проверено выше).
            if (ArmElbowSwing(S, E0, armCurH, dt, out var Eh)) { ArmPlace(S, Eh, armCurH, false); return; }
            ArmPlace(S, E0, H0, false);
        }

        [Tooltip("Степени свободы: как быстро локоть проворачивается вокруг оси плечо → кисть, обходя преграду (°/с).")]
        public float armElbowSpin = 900f;

        /// <summary>Окружность возможных локтей при плече S и кисти H: центр C, ось dn, радиус r; u — к нынешнему локтю, w — поперёк.</summary>
        bool ArmCircle(Vector3 S, Vector3 H, Vector3 Ecur, out Vector3 C, out Vector3 u, out Vector3 w, out float r)
        {
            C = u = w = Vector3.zero; r = 0f;
            Vector3 v = H - S; float d = v.magnitude;
            if (d < 1e-3f || d >= (upperArm + foreArm) * 0.998f) return false;         // рука прямая — поворачивать нечего
            Vector3 dn = v / d;
            float a = upperArm, b = foreArm;
            float ca = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            C = S + dn * (a * ca);
            r = a * Mathf.Sqrt(Mathf.Max(0f, 1f - ca * ca));
            if (r < 0.01f) return false;
            Vector3 p = Ecur - C; p -= dn * Vector3.Dot(p, dn);
            if (p.sqrMagnitude < 1e-8f) return false;
            u = p.normalized; w = Vector3.Cross(dn, u);
            return true;
        }

        /// <summary>Локоть от нынешнего (Ecur) к локтю позы логики (Etgt) по окружности, не быстрее armElbowSpin; кисть в H.
        /// true — поза на этом шаге свободна (Et).</summary>
        bool ArmElbowToward(Vector3 S, Vector3 Ecur, Vector3 Etgt, Vector3 H, float dt, out Vector3 Et)
        {
            Et = Etgt;
            if (!ArmCircle(S, H, Ecur, out var C, out var u, out var w, out float r)) return ArmPoseClear(S, Et, H);
            Vector3 q = Etgt - C;
            float ang = Mathf.Atan2(Vector3.Dot(q, w), Vector3.Dot(q, u)) * Mathf.Rad2Deg;
            float st = Mathf.Clamp(ang, -armElbowSpin * dt, armElbowSpin * dt) * Mathf.Deg2Rad;
            Et = C + (u * Mathf.Cos(st) + w * Mathf.Sin(st)) * r;
            return ArmPoseClear(S, Et, H);
        }

        /// <summary>Локоть на окружности возможных локтей (плечо S, кисть H): к ближайшему от нынешнего углу, при котором звенья
        /// свободны; за шаг — не больше armElbowSpin·dt. true — поза на этом шаге свободна (Es).</summary>
        bool ArmElbowSwing(Vector3 S, Vector3 Ecur, Vector3 H, float dt, out Vector3 Es)
        {
            Es = Ecur;
            if (!ArmCircle(S, H, Ecur, out var C, out var u, out var w, out float r)) return false;
            Vector3 At(float deg) { float q = deg * Mathf.Deg2Rad; return C + (u * Mathf.Cos(q) + w * Mathf.Sin(q)) * r; }
            // ближайший к нынешнему (угол 0) свободный угол
            float best = float.NaN;
            for (int k = 0; k <= 12 && float.IsNaN(best); k++)
                foreach (float sg in k == 0 || k == 12 ? new[] { 1f } : new[] { 1f, -1f })
                    if (ArmPoseClear(S, At(sg * k * 15f), H)) { best = sg * k * 15f; break; }
            if (float.IsNaN(best)) return false;
            float stepDeg = Mathf.Clamp(best, -armElbowSpin * dt, armElbowSpin * dt);
            Es = At(stepDeg);
            return ArmPoseClear(S, Es, H);
        }

        int armItemsLayered = -1;
        /// <summary>Все живые предметы — на слое Item (рука их не толкает); новые — как появятся.</summary>
        void ArmItemsOnLayer()
        {
            if (ProtoItem.Live.Count == armItemsLayered) return;
            armItemsLayered = ProtoItem.Live.Count;
            int itemL = LayerMask.NameToLayer("Item"); if (itemL < 0) return;
            foreach (var it in ProtoItem.Live)
                if (it) foreach (var c in it.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = itemL;
        }

        /// <summary>После шагов физики кадра: рука рисуется по своей позе; свободная кисть — там, где рука (упёрлась — там и стоит).</summary>
        void ArmReadback()
        {
            if (!ArmPhysOn || !armCurHave || !armTgtHave) return;
            Vector3 E = armCurE, H = armCurH;
            elbowCube.position = E;
            handCube.position = H;
            if (lnArm && lnArm.positionCount == 3) lnArm.SetPositions(new[] { shoulderCube.position, E, H });
            float lag = (H - armTgtH).magnitude;
            // ПЕРЕНОС: рука упёрлась — предмет не уходит от кисти (его цель сдвигается туда, куда рука дошла, блок Drag)
            // ПЕРЕНОС: рука упёрлась (стена, своё тело) и кисть не достаёт до предмета — предмет ВЫПАДАЕТ из руки (автор 2026-11-25,
            // скрин: «я и без руки притянул — и теперь блок меня в спину толкает»). Было: цель предмета тянулась к застрявшей кисти —
            // у бока тела предмет шёл в тело и толкал его, хотя рука его не держала.
            armDragStuckT = dragItem && lag > armDragBreak ? armDragStuckT + dtNow : 0f;
            if (dragItem && armDragStuckT > armDragStuckTime) { armDragStuckT = 0f; EndDrag("рука упёрлась — не достаёт до предмета"); }
            // СВОБОДНАЯ КИСТЬ ЗА РУКОЙ: упёрлась в твёрдое — логика кисти там же (ход дальше — от этого места)
            if (armHandFollow && mode == Mode.Free && !dragItem && lag > 0.02f)
            {
                hand = Flat(H);
                handY = H.y - handLift;
            }
        }

        /// <summary>Замер: насколько кисть руки отстала от позы логики (м) — рука упёрлась.</summary>
        public float DiagArmPhysLag => armCurHave && armTgtHave ? (armCurH - armTgtH).magnitude : 0f;
    }
}
