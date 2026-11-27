using UnityEngine;

namespace Fortress.Hero.Arm2
{
    /// <summary>БЛОК «НАМЕРЕНИЕ КИСТИ» (срез 2). Курсор → куда кисть ХОЧЕТ. Ничего не двигает: пишет в состояние цель,
    /// а куда кисть реально попадёт — решает физика (блок «Рука»).
    /// Правила ввода — как у старого героя (проверено автором, design/hero_arm_legacy_dig.md, раздел 4.2):
    /// луч курсора на первую поверхность; ввод из-за спины — зеркально; сектор кисти −50°…+70° от лица (рука правая);
    /// досягаемость — длина руки от плеча, сгиб до упора 0.18 м.
    /// Отличие: поверхность ищется по настоящим коллайдерам (луч физики), а не по своей геометрии; досягаемость — в 3D.
    /// ЦЕЛЬ НЕ ПРЫГАЕТ: курсор на грани стены дальше руки — цель поджимается ПО ТОЙ ЖЕ грани (круг «сфера руки ∩ плоскость»),
    /// а не падает на пол; на полу поджатая точка ищет поверхность под собой и, если та вне руки, подтягивается к плечу.
    /// Урок 8: цель свободной кисти — всегда на поверхности; поверхности нет — цель прежняя.
    /// Запросов к физике: луч курсора + до 3 лучей вниз, только в кадре, не на шаге физики.</summary>
    public class Arm2Intent : MonoBehaviour
    {
        public Arm2Hero hero;
        public Camera cam;

        [Header("Плечо и рука (м) — геометрия автора")]
        [Tooltip("Плечо в координатах тела: 0.2 вбок от центра, 0.24 над полом (центр тела на 0.10).")]
        public Vector3 shoulderLocal = new Vector3(0.2f, 0.14f, 0f);
        public float upperArm = 0.32f, foreArm = 0.30f;
        [Tooltip("Сгиб до упора: кисть не ближе к плечу.")]
        public float dMin = 0.18f;
        [Tooltip("Доля длины руки, дальше которой цель не ставится (рука не вытягивается в струну).")]
        public float reachK = 0.95f;
        [Tooltip("Сектор кисти от лица, градусы: влево и вправо (рука правая — вправо шире).")]
        public float sectorLeft = 50f, sectorRight = 70f;
        [Tooltip("Ввод из-за спины: кисть встаёт перед собой на этом расстоянии от плеча.")]
        public float mirrorReach = 0.22f;
        [Arm2Tune("за спину: полоса перехода, м", 0.02f, 0.3f)]
        [Tooltip("Полоса у линии «бок тела» (в обе стороны): пока курсор идёт через неё, кисть плавно переходит в зеркальное место.")]
        public float mirrorBand = 0.12f;
        public float handRadius = 0.035f;
        [Tooltip("Зазор между целью кисти и своим телом (м).")]
        public float bodyGap = 0.015f;
        [Arm2Tune("кисть: нажим в поверхность, м", 0f, 0.02f)]
        [Tooltip("Цель кисти чуть ниже касания — кисть лежит на поверхности с лёгким нажимом, а не парит над ней.")]
        public float press = 0.003f;

        public float L => upperArm + foreArm;
        public float Reach => L * reachK;
        static readonly RaycastHit[] hits = new RaycastHit[64];

        /// <summary>Плечо в мире — по позе физики тела.</summary>
        public Vector3 ShoulderWorld() { var rb = hero.body.Rb; return rb.position + rb.rotation * shoulderLocal; }

        /// <summary>Постановка на старт: забыть «за спиной».</summary>
        public void ResetIntent() { }

        [Header("Тяга себя (срез 3)")]
        [Tooltip("Спиной тянуть слабее: множитель хода при езде спиной (лицом — 1). Как у старого героя: 0.45.")]
        public float backK = 0.45f;
        [Arm2Tune("тяга: чувствительность жеста", 0.2f, 3f)]
        [Tooltip("1 — как у старого героя: 1 px мыши = столько метров, сколько 1 px занимает линия плечо–кисть на экране.")]
        public float pullGain = 1f;

        [Header("Зацепы (срез 7)")]
        [Arm2Tune("зацеп: радиус камня, м", 0.02f, 0.3f)]
        [Tooltip("Курсор (его луч) ближе этого к камню — кисть идёт на камень.")]
        public float nubRadius = 0.14f;
        [Arm2Tune("зацеп: радиус перил, м", 0.02f, 0.4f)]
        public float railRadius = 0.40f;
        [Tooltip("Радиус перил (м): кисть ложится на них сбоку.")]
        public float railThickness = 0.02f;

        [Arm2Tune("вис: метров на px (экран 1080)", 0.0003f, 0.004f)]
        public float hangMetersPerPx = 0.0012f;

        [Header("Поворот на месте (срез 4)")]
        [Arm2Tune("поворот: чувствительность", 0.1f, 1.5f)]
        [Tooltip("Как у старого героя: 0.4.")]
        public float turnSensitivity = 0.4f;
        [Tooltip("Кисть ближе этого к плечу — поворот считается как от этого радиуса (м). Как у старого: 0.12.")]
        public float turnMinRadius = 0.12f;
        [Tooltip("За один хват лицо не уходит от направления на кисть дальше этого (градусы). Как у старого: 90.")]
        public float turnLimit = 90f;
        [Tooltip("Желаемый курс не убегает от настоящего дальше этого (градусы) — когда тело упёрлось.")]
        public float turnLead = 6f;
        [Tooltip("Задний ход как в автомате: едешь спиной — руль переворачивается.")]
        public bool reverseSteer = true;
        /// <summary>Доля «за спиной» при последнем расчёте цели (0 — курсор впереди, 1 — за спиной): хват из-за спины — зеркальный.</summary>
        public float LastMirrorT { get; private set; }

        /// <summary>ЖЕСТ ПРИ ХВАТЕ → длина руки, которую хочет игрок. Правило старого героя (проверено автором): мышь вниз — «на себя»
        /// (тело к кисти), вверх — «от себя»; 1 px мыши по вертикали = столько метров, сколько 1 px занимает на экране линия
        /// плечо–кисть; спиной — слабее (0.45), плавно по углу хода к лицу. Пишет только намерение (pullLength); тело тянет привод.</summary>
        public void FixedStep(float dt)
        {
            var st = hero.state;
            if (!st.gripped || !cam || st.frame.rmb) return;
            Vector2 md = st.pendingDelta;
            if (md.sqrMagnitude < 1e-8f) return;
            if (st.hanging)
            {
                // ВИС: «от себя» (мышь вверх) — рука длиннее, тело опускается вдоль стенки; «на себя» — просьба подтянуться
                // (поднимется, только если висит не глубже 0.4 — решает блок «Рука»). Масштаб — как у старого: 0.0012 м на px при высоте экрана 1080.
                int hh = st.frame.screenH > 0 ? st.frame.screenH : Screen.height;
                float up = md.y * (1080f / Mathf.Max(1, hh)) * hangMetersPerPx;
                if (up > 0f) { st.hangLower += up; st.hangPullUp = false; }
                else if (up < 0f) st.hangPullUp = true;
                return;
            }
            // линия хода — в плоскости тяги (опора под телом; у зацепа — как задал хват): от плеча до кисти вдоль неё
            Vector3 hand = hero.arm.HandPos, n = st.pullNormal, S0 = ShoulderWorld();
            Vector3 S = S0 - n * Vector3.Dot(S0 - hand, n), line = hand - S;
            float len = line.magnitude; if (len < 0.01f) return;
            int w = st.frame.screenW > 0 ? st.frame.screenW : Screen.width, h = st.frame.screenH > 0 ? st.frame.screenH : Screen.height;
            Vector3 a = cam.WorldToViewportPoint(S), b = cam.WorldToViewportPoint(hand);
            float axisPx = new Vector2((b.x - a.x) * w, (b.y - a.y) * h).magnitude;
            if (axisPx < 1f) return;                                           // линия «в торец» к камере — жест не исполняется (как у старого)
            float mpp = len / axisPx;
            var rb = hero.body.Rb;
            Vector3 face = rb.rotation * Vector3.forward; face.y = 0f;

            // ТЯГА: мышь вниз — «на себя», вверх — «от себя»
            float push = -md.y;
            if (Mathf.Abs(push) > 1e-4f)
            {
                Vector3 travel = line / len * Mathf.Sign(push); travel.y = 0f;     // куда поедет тело: к кисти или от неё
                float fwd = travel.sqrMagnitude > 1e-6f && face.sqrMagnitude > 1e-6f ? Vector3.Dot(travel.normalized, face.normalized) : 1f;
                // ЗАДНИЙ ХОД КАК В АВТОМАТЕ: передача меняется только от явной езды (ход вперёд-назад сильнее хода вбок)
                if (Mathf.Abs(push) >= 0.5f && Mathf.Abs(push) > Mathf.Abs(md.x))
                {
                    if (fwd < -0.5f) st.reverseGear = true; else if (fwd > 0.5f) st.reverseGear = false;
                }
                float k = Mathf.Lerp(backK, 1f, (fwd + 1f) * 0.5f) * pullGain;
                float hN = Vector3.Dot(S0 - hand, n);                              // плечо над поверхностью хвата
                float far = Mathf.Sqrt(Mathf.Max(dMin * dMin, Reach * Reach - hN * hN));
                float wanted = st.pullLength - push * mpp * k, farLimit = Mathf.Max(far, st.pullMin);
                // рука уже вытянута, а «от себя» продолжается — кисть съезжает по поверхности вслед за телом (блок «Хват»)
                if (push < 0f && wanted > farLimit && len > farLimit - 0.04f) st.slideWant = Mathf.Min(st.slideWant + (wanted - farLimit), 0.1f);
                st.pullLength = Mathf.Clamp(wanted, st.pullMin, farLimit);
                // Тело упёрлось (в стену, в свою кисть) — желаемое расстояние не копится дальше, чем привод может тянуть: иначе после
                // упора жест назад сначала выбирал бы накопленный запас («мёртвый ход»).
                float lead = hero.arm.PullLead;
                st.pullLength = Mathf.Clamp(st.pullLength, len - lead, len + lead);
            }

            // ПОВОРОТ НА МЕСТЕ (срез 4): мышь вбок. Правило старого героя: дуга кисти вокруг тела (в метрах) = боковой ход мыши в метрах
            // экрана × 0.4; знак зафиксирован автором («вправо = поворот вправо; знак не менять») и перенесён по коду старого:
            // мышь вправо — рыскание тела в минус (точка хвата на экране уходит вправо, вслед за мышью). Едешь спиной — руль
            // переворачивается (кроме хвата из-за спины). Пишется только желаемый курс; поворачивает тело момент в блоке «Тело».
            if (Mathf.Abs(md.x) > 1e-4f && face.sqrMagnitude > 1e-6f)
            {
                float r = Mathf.Max(turnMinRadius, len);
                float deg = -md.x * mpp / r * Mathf.Rad2Deg * turnSensitivity;
                if (reverseSteer && st.reverseGear && !st.mirrorGrip) deg = -deg;
                float yaw = hero.body.YawRb(st.yawTarget);
                float target = st.yawTarget + deg;
                // тело упёрлось — желаемый курс не убегает от настоящего дальше turnLead
                target = yaw + Mathf.Clamp(Mathf.DeltaAngle(yaw, target), -turnLead, turnLead);
                // «лицо там, где рука»: за один хват лицо не уходит от направления на кисть дальше 90°
                Vector3 toHand = hand - rb.position; toHand.y = 0f;
                if (toHand.sqrMagnitude > 1e-4f)
                {
                    float yh = Mathf.Atan2(toHand.x, toHand.z) * Mathf.Rad2Deg;
                    target = yh + Mathf.Clamp(Mathf.DeltaAngle(yh, target), -turnLimit, turnLimit);
                }
                st.yawTarget = target;
            }
        }

        /// <summary>Расстояние плечо–точка вдоль поверхности с нормалью n (мера тяги при хвате).</summary>
        public float TangentLength(Vector3 point, Vector3 n) => Vector3.ProjectOnPlane(point - ShoulderWorld(), n).magnitude;

        [Arm2Tune("хват: кисть садится после нажатия, с", 0f, 1f)]
        [Tooltip("После нажатия ЛКМ цель кисти столько времени не идёт за курсором: кисть садится в точку нажатия и берётся.")]
        public float pressLand = 0.3f;
        float pressFreeze; bool lastLmb;
        /// <summary>Кадр: курсор → цель кисти в состоянии. Владелец полей cursor* и handTarget*.</summary>
        public void FrameStep(Arm2InputFrame f)
        {
            var st = hero.state;
            if (st.gripped) { pressFreeze = 0f; lastLmb = f.lmb; return; }                  // кисть сцеплена — цель свободной кисти не нужна (и лучи тоже)
            // НАЖАЛИ ЛКМ, А КИСТЬ ЕЩЁ В ВОЗДУХЕ (запись автора 27.11 14:46: «нажимаю ЛКМ — инпут не реагирует»). Автор нажимал и сразу
            // вёл мышь вбок (разворот на месте), цель убегала за курсором, кисть летела за ней и пола не касалась — хват не брался
            // всё время нажатия (0,25–0,3 с). Правило: с момента нажатия цель кисти стоит там, где была, пока кисть не сядет и не
            // возьмётся (не дольше pressLand) — потом, если хвата нет (стена, выше 0.4), цель снова идёт за курсором.
            if (f.lmb && !lastLmb) pressFreeze = pressLand;
            if (!f.lmb) pressFreeze = 0f;
            lastLmb = f.lmb;
            if (pressFreeze > 0f && st.handTargetValid && !st.heldItem && !(hero.grip && hero.grip.SlipLocked)) { pressFreeze -= f.dt; return; }
            if (!cam || f.screenW <= 0 || f.screenH <= 0 || f.rmb) return;                 // ПКМ — наклон камеры, рука замирает (как у старого)
            var rb = hero.body.Rb;
            Vector3 c = rb.position, S = ShoulderWorld();
            Vector3 face = rb.rotation * Vector3.forward; face.y = 0f;
            if (face.sqrMagnitude < 0.01f) return;                                          // тело кувыркается — цель прежняя
            face.Normalize();
            float reach = Reach;

            Ray ray = cam.ViewportPointToRay(new Vector3(f.pos.x / f.screenW, f.pos.y / f.screenH, 0f));
            if (!st.heldItem) offItem = null;
            bool onSurface = FirstSurface(ray, 60f, out RaycastHit hit);
            // предмет под курсором, до которого рука достаёт: ЛКМ возьмёт его, а не пол по дороге к нему (срез 9)
            if (itemLayer == -2) itemLayer = LayerMask.NameToLayer("Item");
            st.cursorItem = !Arm2Hero.NoItems && onSurface && hit.collider.gameObject.layer == itemLayer && (hit.point - S).magnitude <= reach + handRadius ? hit.collider.attachedRigidbody : null;
            // ЗАЦЕПЫ (срез 7): курсор рядом с камнем или перилами — кисть идёт на ближайшую достижимую точку зацепа (автозахват,
            // как у старого героя; перила сильнее камня — у них радиус больше). Зацеп берётся на всей досягаемости руки (решение
            // автора 3А), без сектора и без правила 0.4. Запросов к физике нет: зацеп — отрезок.
            st.targetHold = null;
            if (holds != null && !st.heldItem)
            {
                float best = float.MaxValue; Vector3 bestP = default; Proto.ProtoHold bestH = null;
                for (int i = 0; i < holds.Length; i++)
                {
                    var hd = holds[i]; if (!hd || !hd.isActiveAndEnabled) continue;
                    float rad = hd is Proto.ProtoNub ? nubRadius : railRadius;
                    // «Рядом» — по точке, куда попал курсор (поверхность под ним), а не по лучу: с лучом кисть липла к перилам, даже
                    // когда автор целился в пол перед собой (запись 01:03). Курсор в небо — по лучу, вдвое строже.
                    float dist, sp;
                    // Перила подняты над поверхностью: «рядом» меряется ВДОЛЬ поверхности под курсором, без высоты перил. Запись автора
                    // 15:03: курсор на склоне в 0.25 м сбоку от перил, а до оси 0.33 (высота 0.22 съедала радиус) — автозахвата не было,
                    // кисть ложилась на голый склон 36° и съезжала.
                    if (onSurface) { sp = hd.ClosestTo(hit.point); Vector3 dv = hd.At(sp) - hit.point; if (!(hd is Proto.ProtoNub)) dv = Vector3.ProjectOnPlane(dv, hit.normal); dist = dv.magnitude; }
                    else { dist = hd.ClosestToRay(ray, out sp) * 2f; }
                    if (dist > rad) continue;
                    Vector3 hp = HoldHandPoint(hd, hd.At(sp), S);
                    // точка под курсором вне руки — ближайшая к плечу точка зацепа
                    // Запись автора 15:03: брал БЛИЖАЙШУЮ к плечу точку перил — кисть у самого плеча, тянуть некуда, тело на месте.
                    // Теперь — самая дальняя достижимая точка зацепа в сторону курсора (деление отрезка пополам, без запросов к физике).
                    if ((hp - S).magnitude > reach)
                    {
                        float s0 = hd.ClosestTo(S); hp = HoldHandPoint(hd, hd.At(s0), S); if ((hp - S).magnitude > reach) continue;
                        float lo = 0f, hi = 1f, far = reach - 0.02f;
                        for (int k = 0; k < 8; k++)
                        {
                            float mid = (lo + hi) * 0.5f; Vector3 q = HoldHandPoint(hd, hd.At(Mathf.Lerp(s0, sp, mid)), S);
                            if ((q - S).magnitude <= far) { lo = mid; hp = q; } else hi = mid;
                        }
                    }
                    float score = dist / rad;
                    if (score < best) { best = score; bestP = hp; bestH = hd; }
                }
                if (bestH) { st.targetHold = bestH; st.handTarget = bestP; st.handTargetValid = true; st.cursorValid = false; LastMirrorT = 0f; return; }
            }
            Vector3 p, n;
            if (onSurface) { p = hit.point; n = hit.normal; st.cursorPoint = p; st.cursorValid = true; }
            else
            {
                // курсор мимо всего (небо, край мира): плоскость на уровне опоры тела — цель всё равно едет за курсором
                st.cursorValid = false;
                float y0 = c.y - Arm2Body.Height * 0.5f;
                if (ray.direction.y > -0.01f) return;
                p = ray.GetPoint((y0 - ray.origin.y) / ray.direction.y); n = Vector3.up;
            }

            if (st.heldItem) { HeldFrame(st, ray, p, n, S, reach, face); return; }      // предмет в руке — своё короткое правило

            // «ЗА СПИНОЙ» — ПЛАВНО. Автор 2026-11-25: «на переходе между зонами быстрое подёргивание, когда за спину — всегда скачок»
            // (по записи: цель прыгала на 40 см с правого края сектора на левый). Правило то же: курсор позади центра тела — кисть
            // перед собой у тела с другой стороны (точечное зеркало). Но у границы «бок тела» — полоса перехода: пока курсор
            // проходит её, кисть переходит из прямого места в зеркальное через «перед собой», со скоростью курсора.
            Vector3 fromBody = p - c; fromBody.y = 0f;
            float fwd = Vector3.Dot(fromBody, face);
            float mirrorT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mirrorBand, -mirrorBand, fwd));     // 0 — курсор впереди, 1 — за спиной
            LastMirrorT = mirrorT;

            // ГРАНЬ СТЕНЫ: поджать цель по самой грани — в круг, где до неё достаёт рука
            if (onSurface && mirrorT <= 0f && n.y < 0.7f)
            {
                float dist = Vector3.Dot(S - p, n);
                if (Mathf.Abs(dist) < reach - 0.02f)
                {
                    Vector3 centre = S - n * dist; float rho = Mathf.Sqrt(reach * reach - dist * dist);
                    Vector3 v = p - centre; float vm = v.magnitude;
                    Vector3 q = vm > rho ? centre + v / vm * rho : p;
                    if (Mathf.Abs(dist) < dMin)
                    {
                        float rmin = Mathf.Sqrt(dMin * dMin - dist * dist); Vector3 w = q - centre; float wm = w.magnitude;
                        if (wm < rmin && wm > 1e-4f) q = centre + w / wm * rmin;
                    }
                    Vector3 h = q - S; h.y = 0f;
                    float a = h.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(face, h, Vector3.up) : 0f;
                    if (a >= -sectorLeft && a <= sectorRight) { SetTarget(st, q, n); return; }
                }
                // грань вне руки или вне сектора — ищем пол под поджатой точкой (ниже)
            }

            bool moved = !onSurface || n.y < 0.7f;
            // Место кисти вокруг плеча — угол от лица и расстояние. Прямое: туда, где курсор. Зеркальное: с другой стороны, у тела.
            // Оба — в СЕКТОРЕ −50°…+70° (плюс — вправо); между ними — по доле перехода.
            {
                Vector3 v1 = p - S; v1.y = 0f;
                float a1raw = v1.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(face, v1, Vector3.up) : 0f;
                float a1 = Mathf.Clamp(a1raw, -sectorLeft, sectorRight), r1 = v1.magnitude;
                float a = a1, r = r1;
                if (mirrorT > 0f)
                {
                    Vector3 v2 = (c - fromBody) - S; v2.y = 0f;
                    float a2 = v2.sqrMagnitude > 1e-6f ? Mathf.Clamp(Vector3.SignedAngle(face, v2, Vector3.up), -sectorLeft, sectorRight) : 0f;
                    a = Mathf.Lerp(a1, a2, mirrorT); r = Mathf.Lerp(r1, mirrorReach, mirrorT);
                }
                if (mirrorT > 0f || !Mathf.Approximately(a1raw, a1))
                {
                    p = new Vector3(S.x, p.y, S.z) + Quaternion.AngleAxis(a, Vector3.up) * face * r; moved = true;
                }
            }
            // ДОСЯГАЕМОСТЬ: не дальше руки, не ближе сгиба до упора
            {
                Vector3 v = p - S; float d = v.magnitude;
                if (d > reach) { p = S + v / d * reach; moved = true; }
                else if (d < dMin && d > 1e-4f) { p = S + v / d * dMin; moved = true; }
            }
            if (!moved) { SetTarget(st, p, n); return; }

            // Цель поджали — она уже не на поверхности под курсором: найти поверхность под ней (урок 8: кисть не в воздухе).
            // Поверхность под точкой вне руки (ниже края уступа, выше плеча) — подтянуть точку к плечу и искать снова.
            // Подтяжка — ровно до круга, где рука достаёт до поверхности ЭТОЙ высоты (не «на четверть ближе»): у края досягаемости
            // цель идёт по кругу без прыжка (автор 2026-11-25: «когда за зону вверх — всегда есть скачок»; по записи — 14 см).
            Vector3 hxz = new Vector3(p.x - S.x, 0f, p.z - S.z);
            Vector3 hdir = hxz.sqrMagnitude > 1e-8f ? hxz.normalized : face; float hr = hxz.magnitude;
            float top = S.y + 0.4f, len = 0.4f + reach + 0.05f;
            for (int k = 0; k < 3; k++)
            {
                var down = new Ray(new Vector3(S.x + hdir.x * hr, top, S.z + hdir.z * hr), Vector3.down);
                if (!FirstSurface(down, len, out RaycastHit h2)) { hr *= 0.75f; continue; }      // под точкой пусто (за краем) — ближе к плечу
                float dy = h2.point.y - S.y, d = (h2.point - S).magnitude;
                if (d <= reach + 0.002f) { if (d >= dMin * 0.5f) { SetTarget(st, h2.point, h2.normal); return; } break; }
                if (Mathf.Abs(dy) >= reach) { hr *= 0.75f; continue; }                           // поверхность выше или ниже руки
                hr = Mathf.Sqrt(reach * reach - dy * dy) - 0.001f;                               // круг досягаемости на этой высоте
            }
            // поверхности в досягаемости нет — цель прежняя (кисть не уходит в воздух)
        }

        // ---------- ПРЕДМЕТ В РУКЕ: ОДНО ПРАВИЛО (2026-11-27, автор: «есть трудности — стоит упростить») ----------
        // ПРЕДМЕТ ИДЁТ ПО ЛУЧУ КУРСОРА. Лежит там, куда курсор показывает (на том, что там под ним). Рука туда не достаёт —
        // висит на луче курсора так далеко, как рука достаёт. Больше ничего: сектора, зеркала «за спиной», поджатия по грани
        // стены и отдельного «подъёма за зоной» у предмета нет.
        // Было (запись 17:40 и проба CarryZProbe): курсор на верх столбика — предмет висел в 7 см над ним и на 11 см ближе;
        // на верх дальней стенки — висел в воздухе в полуметре от неё. Причины: 1) «достаёт / не достаёт» считалось для точки
        // под курсором, а не для места КИСТИ при этом положении предмета (кисть держит его за ближнюю грань и достаёт дальше);
        // 2) сдвиг «предмет − курсор», запомненный при хвате, держался всё время — предмет ложился мимо того, на что показывали.
        // Сдвиг остался только чтобы предмет не прыгал в момент хвата: он гаснет по ходу курсора (как у старого героя).
        [Arm2Tune("предмет: сдвиг от хвата гаснет, 1/м", 0f, 40f)]
        [Tooltip("В момент хвата предмет остаётся на месте (курсор смотрит сквозь него в пол за ним). Этот сдвиг гаснет по ходу курсора: больше — быстрее предмет встаёт точно под курсор.")]
        public float carryOffDecay = 12f;
        [Arm2Tune("предмет: подъём — курсор за зоной на, м", 0.05f, 1f)]
        [Tooltip("Рука не достаёт до места под курсором меньше чем на это — предмет ложится как можно ближе к нему; дальше — поднимается по лучу курсора.")]
        public float carryLiftStart = 0.3f;
        static readonly RaycastHit[] heldHits = new RaycastHit[16];
        Rigidbody offItem; Vector3 heldOff, offPrev;
        /// <summary>На сколько низ предмета в руке сейчас выше места под курсором (для замеров).</summary>
        public float HeldLiftNow { get; private set; }

        void HeldFrame(Arm2State st, Ray ray, Vector3 P, Vector3 n, Vector3 S, float reach, Vector3 face)
        {
            var item = st.heldItem;
            if (item != liftItem) { liftItem = item; liftCollider = item.GetComponentInChildren<Collider>(); }
            if (!liftCollider) return;
            Bounds b = liftCollider.bounds; Vector3 e = b.extents, box = e * 0.97f, up = Vector3.up;
            Vector3 off = hero.arm.HandPos - b.center;                              // как кисть держит предмет
            if (item != offItem) { offItem = item; heldOff = new Vector3(b.center.x - P.x, 0f, b.center.z - P.z); if (heldOff.magnitude > 0.2f) heldOff = Vector3.zero; }
            else heldOff *= Mathf.Exp(-carryOffDecay * Mathf.Min((P - offPrev).magnitude, 0.05f));
            offPrev = P;
            Vector3 Pc = P + heldOff;

            // 1) место предмета под курсором: коробка опускается сверху; над точкой места нет (настоящая стена) — к стене на высоте курсора
            const float D = 0.3f;
            Vector3 top = new Vector3(Pc.x, Pc.y + D + e.y, Pc.z), place;
            if (BoxFree(top, box, item)) place = top - up * DropBox(top, box, D + e.y + 0.1f, item);
            else
            {
                float ext = Mathf.Abs(n.x) * e.x + Mathf.Abs(n.y) * e.y + Mathf.Abs(n.z) * e.z;
                Vector3 c = Pc + n * ext;
                place = c + up * (D - DropBox(c + up * D, box, D, item));
            }
            Vector3 hand = place + off;

            // 2) кисть туда не достаёт. Чуть-чуть не достаёт — предмет как можно ближе к этому месту (кисть — по прямой к плечу).
            //    Курсор далеко за зоной руки — предмет на луче курсора, так далеко, как рука достаёт (то есть поднят: п. 13).
            //    Между ними — плавно, по тому, на сколько не достаёт (carryLiftStart).
            float miss = (hand - S).magnitude - reach;
            if (miss > 0f)
            {
                Vector3 nearHand = S + (hand - S).normalized * reach, nearCentre = nearHand - off;
                nearCentre.y += D - DropBox(nearCentre + up * D, box, D, item);
                Vector3 dir = ray.direction, o = ray.origin + heldOff + up * e.y + off;          // место кисти, когда низ предмета на луче
                float tHit = Vector3.Dot(P - ray.origin, dir);
                Vector3 m = o - S; float bq = Vector3.Dot(m, dir), disc = bq * bq - (m.sqrMagnitude - reach * reach);
                float t = disc >= 0f ? Mathf.Min(-bq + Mathf.Sqrt(disc), tHit) : Mathf.Clamp(-bq, 0f, tHit);
                Vector3 centre = o + dir * t - off;
                centre.y += D - DropBox(centre + up * D, box, D, item);                           // не ниже того, что под ним
                hand = centre + off;
                Vector3 v = hand - S;
                if (v.magnitude > reach)
                {
                    // высота главнее дали (правило старого героя R39): поджимается горизонталь
                    float dy = Mathf.Clamp(v.y, -reach * 0.98f, reach * 0.98f); Vector3 hz = new Vector3(v.x, 0f, v.z);
                    hand = S + (hz.sqrMagnitude > 1e-8f ? hz.normalized : Vector3.forward) * Mathf.Sqrt(reach * reach - dy * dy) + up * dy;
                }
                hand = Vector3.Lerp(nearCentre + off, hand, Mathf.SmoothStep(0f, 1f, miss / Mathf.Max(0.01f, carryLiftStart)));
                place = hand - off;
            }

            // 3) ЗОНА РУКИ — ТА ЖЕ, ЧТО У ПУСТОЙ КИСТИ (автор 27.11, запись 17:57: «за спиной так делать нельзя»): кисть с предметом не
            //    уходит за сектор −sectorLeft…+sectorRight от лица. Место кисти, найденное выше, ПОВОРАЧИВАЕТСЯ вокруг плеча к краю
            //    сектора — с той же далью и ТОЙ ЖЕ ВЫСОТОЙ. (Запись 18:02, «какой-то барьер»: раньше сектор применялся до правила
            //    подъёма и подъём вне сектора выключался — на краю сектора предмет прыгал с пола на 0,6 м вверх и обратно.)
            //    Курсор проходит точно за спиной (средняя треть дуги «сзади») — место плавно переходит с правого края на левый.
            {
                Vector3 hxz = new Vector3(hand.x - S.x, 0f, hand.z - S.z);
                if (hxz.sqrMagnitude > 1e-6f)
                {
                    float a = Vector3.SignedAngle(face, hxz, Vector3.up), ac = a;
                    if (a > sectorRight || a < -sectorLeft)
                    {
                        float arc = 360f - sectorRight - sectorLeft, u = ((a > 0f ? a : a + 360f) - sectorRight) / arc;
                        ac = Mathf.Lerp(sectorRight, -sectorLeft, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, u)));
                    }
                    if (!Mathf.Approximately(a, ac))
                    {
                        hand = S + Quaternion.AngleAxis(ac, Vector3.up) * face * hxz.magnitude + up * (hand.y - S.y);
                        Vector3 centre = hand - off, above = centre + up * D;
                        // предмет лежал (не поднят курсором) — на новом месте тоже ложится на то, что под ним; поднят — не ниже опоры
                        if (miss <= 0f && BoxFree(above, box, item)) centre = above - up * DropBox(above, box, D + reach + 0.6f, item);
                        else centre.y += D - DropBox(above, box, D, item);
                        hand = centre + off; place = centre;
                    }
                }
            }
            HeldLiftNow = Mathf.Max(0f, place.y - e.y - Pc.y);
            LastMirrorT = 0f;
            SetCentre(st, hand);
        }

        static readonly Collider[] heldOverlap = new Collider[16];
        // то, что лежит НА предмете в руке, едет с ним: для примерки его места это не опора (иначе нижний кубик стопки «ставился»
        // на свой же верхний — замер: при хвате нижний подпрыгивал на 7 см со скоростью 2 м/с, и стопка разваливалась)
        bool Rider(Collider c) => hero.guard && hero.guard.IsRider(c.attachedRigidbody);
        bool BoxFree(Vector3 c, Vector3 half, Rigidbody item)
        {
            int cnt = Physics.OverlapBoxNonAlloc(c, half, heldOverlap, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < cnt; i++) if (heldOverlap[i].attachedRigidbody != item && !heldOverlap[i].transform.IsChildOf(hero.transform) && !Rider(heldOverlap[i])) return false;
            return true;
        }
        /// <summary>На сколько коробка опустится из точки from до первой опоры (не дальше dist).</summary>
        float DropBox(Vector3 from, Vector3 half, float dist, Rigidbody item)
        {
            int cnt = Physics.BoxCastNonAlloc(from, half, Vector3.down, heldHits, Quaternion.identity, dist, ~0, QueryTriggerInteraction.Ignore);
            float d = dist;
            for (int i = 0; i < cnt; i++)
            {
                var h = heldHits[i];
                if (h.collider.attachedRigidbody == item || h.collider.transform.IsChildOf(hero.transform) || h.distance <= 0f || Rider(h.collider)) continue;
                if (h.distance < d) d = h.distance;
            }
            return d;
        }

        void SetTarget(Arm2State st, Vector3 p, Vector3 n)
        {
            SetCentre(st, p + n * (handRadius - press));
        }

        void SetCentre(Arm2State st, Vector3 centre)
        {
            // ЦЕЛЬ НЕ В СВОЁМ ТЕЛЕ. Автор 2026-11-25: «двинул рукой в нижнюю зону — рука подёргивалась». По записи: зеркальное место
            // кисти (0.22 м от плеча, влево до −50°) попадало в контур тела — кисть упиралась в тело с полной силой привода и дрожала.
            // Цель сдвигается к ближайшей грани тела наружу (перед телом — вперёд); в тело кисть и так не войдёт — это делает физика.
            var rb = hero.body.Rb; Quaternion inv = Quaternion.Inverse(rb.rotation);
            Vector3 l = inv * (centre - rb.position);
            float hx = Arm2Body.Width * 0.5f + handRadius + bodyGap, hz = Arm2Body.Length * 0.5f + handRadius + bodyGap, hy = Arm2Body.Height * 0.5f + handRadius;
            if (Mathf.Abs(l.x) < hx && Mathf.Abs(l.z) < hz && Mathf.Abs(l.y) < hy)
            {
                float toFront = hz - l.z, toSide = hx - Mathf.Abs(l.x);
                if (l.z > -Arm2Body.Length * 0.25f || toFront < toSide) l.z = hz; else l.x = Mathf.Sign(l.x) * hx;
                centre = rb.position + rb.rotation * l;
            }
            st.handTarget = centre;
            st.handTargetValid = true;
        }

        /// <summary>Где быть центру кисти на зацепе: камень — над его верхней гранью; перила — сбоку от оси, со стороны плеча.</summary>
        public Vector3 HoldHandPoint(Proto.ProtoHold hd, Vector3 onSegment, Vector3 shoulder)
        {
            if (hd is Proto.ProtoNub) return onSegment + hd.transform.up * (handRadius - press);
            // с той стороны перил, где кисть уже находится (если она рядом), иначе — со стороны плеча: кисть не должна лезть сквозь перила
            Vector3 axis = (hd.B - hd.A).normalized, hand = hero.arm ? hero.arm.HandPos : shoulder;
            Vector3 fromHand = Vector3.ProjectOnPlane(hand - onSegment, axis), along = Vector3.Project(hand - onSegment, axis);
            Vector3 side = fromHand.magnitude < 0.2f && fromHand.magnitude > 0.01f && along.magnitude < 0.3f ? fromHand : Vector3.ProjectOnPlane(shoulder - onSegment, axis);
            side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.up;
            return onSegment + side * (railThickness + handRadius - press);
        }

        Proto.ProtoHold[] holds;
        void Start() { RescanHolds(); }
        /// <summary>Собрать зацепы сцены и сделать их «только для руки»: тело сквозь камни и перила проходит (стоять на них нельзя —
        /// как у старого героя, у которого зацепы были без коллайдеров), а кисть и звенья руки в них упираются.</summary>
        public void RescanHolds()
        {
            holds = FindObjectsByType<Proto.ProtoHold>(FindObjectsInactive.Exclude);
            var mine = hero.body.GetComponents<Collider>();
            foreach (var hd in holds)
                foreach (var hc in hd.GetComponentsInChildren<Collider>())
                    foreach (var bc in mine) Physics.IgnoreCollision(bc, hc, true);
        }

        /// <summary>Первая поверхность по лучу, кроме самого героя (тело, рука) и триггеров.</summary>
        Rigidbody liftItem; Collider liftCollider;
        int itemLayer = -2;
        bool FirstSurface(Ray ray, float dist, out RaycastHit best)
        {
            int n = Physics.RaycastNonAlloc(ray, hits, dist, ~0, QueryTriggerInteraction.Ignore);
            best = default; float bd = float.MaxValue; bool any = false; var held = hero.state.heldItem;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.distance >= bd || h.collider.transform.IsChildOf(hero.transform)) continue;
                if (held && h.collider.attachedRigidbody == held) continue;       // предмет в руке курсор не заслоняет: цель — поверхность под ним
                best = h; bd = h.distance; any = true;
            }
            return any;
        }
    }
}
