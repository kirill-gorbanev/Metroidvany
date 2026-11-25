using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ПЕРЕТАСКИВАНИЕ ПРЕДМЕТА — блок поверх общего состояния (переделка 2026-11-22: предметы целиком на физике Unity).
    ///
    /// · НАВЕДЕНИЕ / ЛОВЛЯ (автор 2026-11-22: «хотел двигаться, а зацепился за объект»): только КУРСОР на самом предмете (луч в его
    ///   коллайдер) и рука до него достаёт — кисть наводится на предмет. Кисть, идущая рядом с предметом, его не ловит.
    ///   ЛКМ — кисть берёт его в точке, куда указала (тело не едет, жесты езды и поворота не работают).
    /// · ПЕРЕНОС: пока ЛКМ зажата, цель кисти идёт за курсором теми же правилами, что свободная рука (досягаемость, сектор, не сквозь
    ///   стену и тело). Предмет идёт с рукой без инерции (ProtoItem.CarryStep): скорость — в цель за шаг, предел от массы (тяжёлый
    ///   медленнее), тяжелее carryForce / g — не поднимается. ВЫСОТА — курсором за синей зоной: выше зоны на экране — вверх,
    ///   ниже — вниз, внутри зоны держится (не ниже опоры под предметом, не выше руки); в стены и ступени
    ///   упирается сам; стопку на нём везёт трение, рывок её роняет.
    ///   Цель не входит в тело (itemBodyGap). Точка хвата ушла дальше руки (carryBreak) — не удержал, отпустил.
    /// · ОТПУСТИЛ — дальше просто физика: встанет, съедет, упадёт, сядет под углом.
    /// · Лежащий предмет для тела — физика (тело — Rigidbody, блок ItemPhysics): кто кого — массы; автообход огибает предметы.
    /// (Удалено при переделке: габариты для опор и стопок, «влезает ли» предмет, оседание, плавный подъём, выпрямление, «разбудить
    ///  тех, кто сверху», кинематический перенос, проверка равновесия, толкатель тела.)</summary>
    public partial class HeroProto
    {
        [Header("Предметы (перетаскивание)")]
        [Tooltip("Цель предмета в руке — не ближе этого зазора (м) к кубику тела.")]
        public float itemBodyGap = 0.03f;
        [Tooltip("Перенос: лёгкий предмет (не тяжелее carrySpeedMass) в руке не быстрее этого (м/с).")]
        public float carryMaxSpeed = 2.5f;
        [Tooltip("Перенос: до этой массы (кг) — полная скорость; тяжелее — скорость падает как carrySpeedMass / масса (вдвое тяжелее — вдвое медленнее).")]
        public float carrySpeedMass = 1f;
        [Tooltip("Перенос: сила руки (Н). Тяжелее силы / g — рука не поднимает, только тащит.")]
        public float carryForce = 60f;
        [Tooltip("Зона 2 (курсор за синей зоной сверху): ход мыши на 1 пиксель (1080p) — столько метров хода кисти с предметом.")]
        public float carryAirRate = 0.002f;
        [Tooltip("Из воздуха в зону: смещение «кисть − курсор» гаснет по ходу курсора — за 1 м хода курсора в e^N раз (только пока ведёшь мышь).")]
        public float carryOffDecay = 8f;
        [Tooltip("Точка хвата ушла от плеча дальше руки на столько (м) — не удержал, предмет отпущен.")]
        public float carryBreak = 0.25f;

        ProtoItem hoverItem, dragItem;
        Vector3 hoverItemHand;               // куда встанет кисть на наведённом предмете (XZ)
        Vector3 hoverItemPoint;              // точка хвата на поверхности наведённого предмета (3D; y = NaN — за верх)
        Vector3 grabLocal;                   // точка хвата в координатах предмета
        Vector3 dragTarget;                  // цель кисти (XZ) — за курсором
        float carryLift;                     // высота низа предмета в руке от уровня тела — курсором за зоной
        Vector3 carryTc; float carrySpeedNow; bool carryCanLift, carryHave;   // тяга руки — для фиксированных шагов физики
        bool carryAir;                       // зона 2: курсор за синей зоной сверху — ход мыши ведёт кисть в вертикальной плоскости
        Vector3 carryAirGoal; bool carryAirGoalSet; float carryAirOutY;   // зона 2: сырая цель кисти (ход мыши) и последняя выданная высота
        bool carryOnSupport;                 // предмет в руке стоит на опоре (высота упёрлась в опору / голову) — касание сверху
        bool carryOnHead;                    // опора — голова (верх кубика тела)
        Vector3 carryFloorOff, carryCursorPrev;   // из воздуха в зону: смещение «кисть − курсор», гаснет по ходу курсора
        float carryHover;                    // зазор «над опорой» несомого предмета: после хвата нарастает плавно от 0 до 1 см
        bool carryJustGrabbed;               // первый шаг после хвата: кисть — в точке хвата, смещение до курсора гаснет по ходу мыши
        Vector3 carryTouchN;                 // диагностика: нормаль, на плоскость которой повёрнут ход (0 — воздух)

        /// <summary>ЗОНА 2 — ВОЗДУХ: ход мыши (пиксели) — кисть с предметом: вбок — по горизонтали поперёк взгляда камеры,
        /// вверх-вниз — по высоте; глубина держится; кисть — в сфере руки вокруг плеча, не в стене, не сквозь тело.</summary>
        void CarryAirStep(Vector2 delta, float dt)
        {
            var it = dragItem;
            float grabAbove = it.transform.TransformPoint(grabLocal).y - it.WorldBounds.min.y;
            float k = Px1080 * carryAirRate;
            Vector3 right = Flat(cam.transform.right); right = right.sqrMagnitude > 1e-6f ? right.normalized : SideDir;
            Vector3 P = new Vector3(dragTarget.x, BodyLevel + carryLift + grabAbove, dragTarget.z);   // где кисть сейчас (после всех пределов)
            if (!carryAirGoalSet) { carryAirGoal = P; carryAirGoalSet = true; }
            if (P.y > carryAirOutY + 1e-4f) carryAirGoal.y = Mathf.Max(carryAirGoal.y, P.y);   // опора / голова подняли — «долг» вниз не копится
            // СЫРАЯ ЦЕЛЬ — ход мыши; КИСТЬ ДОГОНЯЕТ ЕЁ ПЛАВНО (автор 2026-11-24: «всё ещё рывки, как будто 30 к/с»): мышь присылает ход
            // пачками (частота опроса) — в одних кадрах 0, в другом сразу много; без сглаживания предмет шёл ступеньками
            Vector3 m = right * (delta.x * k) + Vector3.up * (delta.y * k);
            // ЗОНЫ 3–4 — КАСАНИЕ ПОВЕРХНОСТИ (автор 2026-11-24: «опустили на поверхность — скользим по ней; боковая — инпут преобразуется
            // в эту плоскость»): касание — нормаль из физики (стены, предметы, пол) или опора под предметом (верх кубика, пол).
            // Ход давит в поверхность — плоскость хода (воздух: лицом к камере) поворачивается на плоскость касания, как при ходьбе
            // по стенам; ход от поверхности — без поворота (отрыв: с верха кубика мышь вверх — поднимается).
            bool touch = it.TakeContact(out Vector3 n);
            if (!touch && carryOnSupport && !carryOnHead) { touch = true; n = Vector3.up; }   // голова — опора, но не поверхность для скольжения (иначе «вниз» уводит назад, за спину)
            // ПРИЦЕЛ ПРИ СПУСКЕ (автор 2026-11-24, лаборатория: «с камеры кажется, что сейчас поставлю, а рука ещё далеко — поднимаю на
            // максимум и сверху пытаюсь прицельно попасть на слой; пускай луч прицела с руки»): глубина с камеры не видна. Опускаешь —
            // луч из камеры через низ предмета; первым на нём верх поверхности (тумба, полка, ящик) и кисть там достаёт — спуск идёт
            // ПО ЭТОМУ ЛУЧУ (та же скорость по высоте): на экране предмет стоит на месте и садится ровно туда, куда «целится».
            if (!touch && m.y < 0f && CarryAimLanding(it, out Vector3 land, out Vector3 bot))
            {
                float drop = bot.y - land.y;
                if (drop > 1e-3f) m = new Vector3(m.x, 0f, m.z) + (land - bot) * Mathf.Min(1f, -m.y / drop);
            }
            if (touch && m.sqrMagnitude > 1e-12f && Vector3.Dot(m, n) < 0f)
            {
                Vector3 airN = -Flat(cam.transform.forward); airN = airN.sqrMagnitude > 1e-6f ? airN.normalized : -Face;
                m = Quaternion.FromToRotation(airN, n) * m;
                carryTouchN = n;
            }
            else carryTouchN = Vector3.zero;
            carryAirGoal += m;
            if (touch)                                                            // сырая цель не уходит вглубь поверхности (без «долга»)
            {
                Vector3 grabNow = it.transform.TransformPoint(grabLocal);
                float into = Vector3.Dot(carryAirGoal - grabNow, n);
                if (into < 0f) carryAirGoal -= n * into;
            }
            Vector3 S3 = Shoulder3, v = carryAirGoal - S3; float Lr = L * 0.999f;
            if (v.magnitude > Lr) carryAirGoal = S3 + v.normalized * Lr;          // дальше руки — по сфере
            // СЕКТОР ПЕРЕД ЛИЦОМ И В ВОЗДУХЕ (автор 2026-11-24: «влево-вниз — рука поднимается над головой и выкручивает за спину»):
            // кисть — в синей зоне (угол от лица) или над самим телом; за спину — нет
            Vector3 gf = Flat(carryAirGoal);
            if (!(bodyCube && InBodyZone(gf))) { Vector3 rr = Reach(gf); carryAirGoal = new Vector3(rr.x, carryAirGoal.y, rr.z); }
            Vector3 gw = HandAtLedge(Flat(carryAirGoal), carry: true);            // стены
            carryAirGoal = new Vector3(gw.x, carryAirGoal.y, gw.z);
            P = Vector3.Lerp(P, carryAirGoal, 1f - Mathf.Exp(-handFollow * dt));  // как свободная кисть за курсором
            Vector3 want = Flat(P);
            if (ArmThroughBody3(want, P.y)) want = FreeHandAroundBody(want);
            dragTarget = want;
            carryLift = Mathf.Clamp(P.y - grabAbove - BodyLevel, -handDown, shoulderLift + L);
            carryAirOutY = P.y;
        }

        /// <summary>Прицел спуска: луч из камеры через низ предмета (центр низа) — первая поверхность за ним; верх (нормаль вверх),
        /// ниже низа предмета и кисть в точке посадки в пределах руки — land (точка посадки низа), bot — низ предмета сейчас.
        /// Первым попался бок или ничего — прицела нет (спуск по вертикали, как был).</summary>
        bool CarryAimLanding(ProtoItem it, out Vector3 land, out Vector3 bot)
        {
            land = default;
            Bounds b = it.WorldBounds;
            bot = new Vector3(b.center.x, b.min.y, b.center.z);
            Vector3 o = cam.transform.position, d = bot - o;
            float d0 = d.magnitude; if (d0 < 1e-4f) return false;
            var hits = Physics.RaycastAll(new Ray(o, d / d0), d0 + 5f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (var h in hits)
            {
                if (h.distance <= d0 + 1e-3f || !CarrySurfaceCollider(h.collider)) continue;
                if (h.normal.y < 0.5f || h.point.y >= bot.y - 1e-3f) return false;     // первым — бок / выше низа: не верх под предметом
                Vector3 grabW = it.transform.TransformPoint(grabLocal);
                if ((h.point + (grabW - bot) - Shoulder3).magnitude > L) return false;   // кисть там не достаёт
                land = h.point; return true;
            }
            return false;
        }

        /// <summary>Коллайдер — поверхность для кисти с предметом: не несомый предмет, не тело и не части героя, не то, что едет
        /// на несомом (живая физика); пол, рельеф, стены, застывшие предметы — да.</summary>
        bool CarrySurfaceCollider(Collider c)
        {
            if (!c || c.isTrigger || c == BodyCollider || c.transform.IsChildOf(transform)) return false;
            var rb = c.attachedRigidbody;
            if (dragItem && rb == dragItem.Body) return false;
            return !rb || rb.isKinematic;
        }

        /// <summary>Точка под курсором (зона пола при переносе): первая поверхность по лучу — пол, верх застывшего предмета, стена.
        /// Попал в бок — точка чуть внутрь (высота возьмётся с верха у этой кромки).</summary>
        bool CarryCursorPoint(Vector2 px, out Vector3 p) => CarryCursorPoint(px, out p, out _);

        /// <summary>side — курсор на вертикальной грани (стена, бок застывшего предмета): p — у грани снаружи, на высоте курсора.</summary>
        bool CarryCursorPoint(Vector2 px, out Vector3 p, out bool side)
        {
            p = default; side = false;
            Ray ray = cam.ScreenPointToRay(px);
            // ПЛОСКОСТЬ ТЕКУЩЕЙ ВЫСОТЫ (автор 2026-11-24): сошёл курсором с края ящика — луч уходил на пол далеко за ящиком (за зону,
            // в «воздух»). Точка — на плоскости высоты низа предмета; поверхность раньше неё по лучу (верх ящика, уступ) — она.
            // За краем под точкой пол — предмет опускается, плоскость с ним, точка сходится с настоящей на полу плавно.
            float planeY = BodyLevel + carryLift;
            bool havePlane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f)).Raycast(ray, out float dP);
            var hits = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (!CarrySurfaceCollider(h.collider)) continue;
                if (havePlane && h.distance > dP + 0.005f) break;                // поверхность ниже плоскости — точка на плоскости
                p = h.point;
                // ВЕРТИКАЛЬНАЯ ГРАНЬ (автор 2026-11-24: «могу поднять, но врезаюсь в стену — застрял»): курсор на стене / боку — кисть
                // идёт ПО ГРАНИ: у неё снаружи, на высоте курсора (раньше — по полу у её основания, поднять было нельзя)
                if (Mathf.Abs(h.normal.y) < 0.5f) { p += Flat(h.normal).normalized * 0.01f; side = true; }
                return true;
            }
            if (havePlane) { p = ray.GetPoint(dP); return true; }
            return false;
        }

        /// <summary>Верх поверхности под точкой кисти (луч вниз с высоты досягаемости руки); NaN — ничего.</summary>
        float CarrySurfaceAt(Vector3 xz)
        {
            float top = BodyLevel + shoulderLift + L + 0.05f;
            var hits = Physics.RaycastAll(new Vector3(xz.x, top, xz.z), Vector3.down, top + 50f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN, bestD = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.normal.y < 0.3f || !CarrySurfaceCollider(h.collider)) continue;
                if (h.distance < bestD) { bestD = h.distance; best = h.point.y; }
            }
            return best;
        }

        readonly System.Collections.Generic.List<ProtoItem> frozenItems = new System.Collections.Generic.List<ProtoItem>();

        /// <summary>ДРУГИЕ ПРЕДМЕТЫ НЕПОДВИЖНЫ, ПОКА НЕСЁШЬ (автор 2026-11-23: «на них можно положить, и мы их не расталкиваем ни рукой,
        /// ни объектом в руке»): лежащие — застывают (кинематические: для несомого — стена и опора). Не трогаем: то, что ещё движется
        /// (падает, катится), и то, что лежит НА несомом предмете (едет на нём трением, как было).</summary>
        void FreezeOthers(ProtoItem carried)
        {
            UnfreezeOthers();
            Bounds cb = carried.WorldBounds;
            foreach (var o in ProtoItem.Live)
            {
                if (!o || o == carried || o.held) continue;
                if (o.Body.linearVelocity.sqrMagnitude > 0.05f * 0.05f) continue;         // ещё движется — физика
                Bounds ob = o.WorldBounds;
                bool onTop = ob.min.y >= cb.max.y - 0.03f && ob.min.x < cb.max.x && ob.max.x > cb.min.x && ob.min.z < cb.max.z && ob.max.z > cb.min.z;
                if (onTop) continue;                                                       // лежит на несомом — едет на нём
                o.Freeze(true); frozenItems.Add(o);
            }
        }

        void UnfreezeOthers()
        {
            foreach (var o in frozenItems) if (o) o.Freeze(false);
            frozenItems.Clear();
        }

        /// <summary>Фиксированный шаг физики (блок ItemPhysics): рука ведёт предмет к цели, посчитанной логикой в этом кадре.</summary>
        void CarryPhysicsStep(float dt)
        {
            if (dragItem && carryHave) dragItem.CarryStep(carryTc, dt * Mathf.Max(1, physStepsLeft), carrySpeedNow, carryCanLift);   // ход — до конца кадра
        }

        public ProtoItem DraggedItem => dragItem;
        /// <summary>Высота точки хвата предмета в руке (мир); без предмета — плечо.</summary>
        float CarryGrabY => dragItem ? dragItem.transform.TransformPoint(grabLocal).y : ShoulderY;
        public bool Dragging => dragItem != null;

        /// <summary>Предмет под курсором (луч в его коллайдер), до которого кисть достаёт. hand — точка кисти на нём (XZ).</summary>
        /// <summary>ХВАТ ЗА ЛЮБУЮ СТОРОНУ (автор 2026-11-24: «могу взяться только сверху — странно; надо ухватиться за все стороны,
        /// за угол»): point — точка ПОВЕРХНОСТИ предмета в 3D, куда указал курсор (луч в коллайдер: верх, бок, угол). Рука до неё
        /// не достаёт — ближайшая к плечу точка поверхности (3D). point.y = NaN — хват за верх (предмет на спине).</summary>
        bool FindItem(Vector2 px, out ProtoItem best, out Vector3 hand, out Vector3 point)
        {
            best = null; hand = default; point = default;
            if (ProtoItem.Live.Count == 0) return false;
            Ray r = cam.ScreenPointToRay(px);
            if (FindBackItem(r, out var bh)) { best = backItem; hand = bh; point = new Vector3(bh.x, float.NaN, bh.z); return true; }   // предмет на спине — снять (блок Back)
            float bestD = float.MaxValue;
            foreach (var it in ProtoItem.Live)
            {
                if (!it || it.held) continue;
                var col = it.GetComponent<Collider>();
                if (!col || !col.Raycast(r, out var hit, 1000f)) continue;       // курсор не на предмете — не ловим
                if (hit.distance >= bestD) continue;                               // ближний по лучу (верх стопки) первым
                Vector3 p3 = hit.point;                                            // кисть — куда указал курсор (верх, бок, угол)
                if (!HandReaches(p3))
                {
                    p3 = col.ClosestPoint(Shoulder3);                              // дальний край — ближняя к плечу точка поверхности
                    if (!HandReaches(p3)) continue;
                }
                bestD = hit.distance; best = it; hand = Flat(p3); point = p3;
            }
            return best;
        }

        /// <summary>Рука достаёт до точки на предмете: не дальше руки (с допуском), не сквозь тело, без стены между.</summary>
        bool HandReaches(Vector3 q)
        {
            Vector3 v = Flat(q - Shoulder);
            if (v.magnitude > L + 0.03f || ArmThroughBody(q)) return false;
            // стена между плечом и точкой — только если она выше руки над ней (автор 2026-11-24, лаборатория: «без предмета руку не
            // поднять» — предмет на тумбе не ловился: тумба считалась стеной во всю высоту). Рука — прямая от плеча до точки.
            for (int i = 0; i < obstacles.Count; i++)
                for (int k = 1; k <= 8; k++)
                {
                    Vector3 m = Shoulder + v * (k / 8f);
                    float armY = Mathf.Lerp(ShoulderY, q.y, k / 8f);
                    if (obstacles[i].Contains(new Vector2(m.x, m.z)) && obstacleTop[i] > armY + 0.01f) return false;
                }
            return true;
        }

        void BeginDrag(ProtoItem it, Vector3 h, Vector3 point)
        {
            if (it == backItem) UnlockFromBack(h);                     // со спины — в руку перед собой (блок Back)
            Bounds b = it.WorldBounds;
            Vector3 g = float.IsNaN(point.y) ? new Vector3(h.x, b.max.y, h.z) : point;   // точка поверхности под курсором (со спины — верх)
            grabLocal = it.transform.InverseTransformPoint(g);
            dragItem = it;
            carryLift = b.min.y - BodyLevel;                           // высота — та, на которой лежал
            carryHave = false;                                         // тяги ещё нет — до первого шага переноса
            carryAir = false; carryFloorOff = Vector3.zero; carryJustGrabbed = true; carryHover = 0f;
            FreezeOthers(it);
            it.BeginCarry();
            hand = h; dragTarget = h;
            hoverItem = null;
            gestureOwner = "взял предмет";
        }

        void EndDrag(string why)
        {
            if (!dragItem) return;
            UnfreezeOthers();
            dragItem.EndCarry(); dragItem.SetLook(false, false);       // дальше — просто физика
            dragItem = null;
            lastRelease = why;
        }

        /// <summary>Шаг свободной руки: наведение на предмет и хват (до зацепов и пола). true — шаг руки забран блоком.</summary>
        bool DragFreeStep(float dt, InputFrame inp, Vector2 px, bool orbit)
        {
            var prevHover = hoverItem;
            if (dragItem)
            {
                if (!inp.pressed && !falling && TryLockToBack()) return true;   // курсор был за спиной — на спину (блок Back)
                if (!inp.pressed || falling) { EndDrag(falling ? "падение — предмет выпал" : "положил предмет"); return true; }
                bool haveP = CarryCursorPoint(px, out var p, out bool onSide);        // поверхность под курсором: пол, верх предмета, стена
                if (!haveP) haveP = FloorPoint(px, out p);
                if (!haveP && new Plane(Vector3.up, new Vector3(0f, BodyLevel, 0f)).Raycast(cam.ScreenPointToRay(px), out float pe))
                { p = cam.ScreenPointToRay(px).GetPoint(pe); haveP = true; }          // луч мимо поверхностей — плоскость своего уровня
                Vector3 r = haveP ? Reach(p) : default;                                // курсор, прижатый к синей зоне
                // НАД ГОЛОВОЙ (автор 2026-11-24): курсор на теле в досягаемости — тоже место для кисти (зона + над телом);
                // пускает туда «сквозь тело» в 3D — только когда предмет уже выше головы
                if (haveP && bodyCube && InBodyZone(p) && Flat(p - Shoulder).magnitude <= L) r = Flat(p);
                float outBy = haveP ? Flat(p - r).magnitude : 0f;                     // насколько курсор за зоной (м по полу)
                bool outside = haveP && outBy > 0.005f;
                // выше зоны — на экране, НА ОДНОЙ ВЫСОТЕ с точкой курсора (край зоны — на высоте точки: точка на верхе ящика / в плоскости
                // поднятого предмета на экране всегда выше края зоны на полу — сход с края ящика считался уходом вверх)
                float abovePx = haveP && outside ? (px.y - cam.WorldToScreenPoint(new Vector3(r.x, p.y, r.z)).y) * Px1080 : 0f;
                bool above = !haveP || abovePx > 30f;
                // глубоко внутри зоны: точка на 3 см дальше от плеча — тоже в зоне
                bool deepInside = haveP && !outside && Flat(Reach(p + Flat(p - Shoulder).normalized * 0.03f) - (p + Flat(p - Shoulder).normalized * 0.03f)).magnitude < 0.005f;
                // ЗОНА 2 — ВОЗДУХ (автор 2026-11-24: «в зоне 1 двигаю по полу xz, в зоне 2 — уже по (x или z) и y»): курсор ушёл за синюю
                // зону вверх — ход мыши: вбок — горизонталь поперёк взгляда камеры, вверх-вниз — высота; глубина держится; кисть — в сфере руки.
                // Курсор вернулся в зону — снова по полу (высота держится).
                // ЗАПАС НА ГРАНИЦЕ (автор 2026-11-24: «подёргивание — рука утягивается или притягивается резко на переходе»): в воздух —
                // курсор выше края зоны на экране больше 30 px (1080p), обратно — зашёл внутрь на 3 см; на кромке режимы не дребезжат.
                bool wasAir = carryAir;
                if (!orbit && above) { if (!carryAir) carryAirGoalSet = false; carryAir = true; }
                else if (!orbit && deepInside) carryAir = false;
                if (!orbit && carryAir) { CarryAirStep(inp.delta, dt); carryJustGrabbed = false; }
                else if (!orbit && haveP)
                {
                    // ИЗ ВОЗДУХА — БЕЗ ПРЫЖКА К КУРСОРУ: в воздухе кисть вёл ход мыши, курсор ушёл в сторону. Смещение «кисть − курсор»
                    // запоминается и гаснет только пока ведёшь мышь (сама кисть не едет) — сходится с курсором плавно.
                    // ХВАТ — ТОЖЕ БЕЗ ПРЫЖКА (автор 2026-11-24: «схватил второй-третий в башне — улетает всё, в момент хвата дёргается»):
                    // луч курсора идёт СКВОЗЬ предмет в руке — точка курсора за ним (бок нижнего, пол позади), кисть ехала туда со всей
                    // скоростью и сбивала стопку. Сразу после хвата кисть остаётся в точке хвата, смещение до курсора гаснет по ходу мыши.
                    if (wasAir || carryJustGrabbed) { carryFloorOff = Flat(dragTarget) - r; carryCursorPrev = r; }
                    // гаснет по ходу курсора, но не быстрее самого хода (большое смещение — хват за бок: точка курсора за предметом —
                    // кисть не летит к ней на пределе скорости, а идёт не быстрее двух ходов курсора)
                    float cMove = Flat(r - carryCursorPrev).magnitude, offM = carryFloorOff.magnitude;
                    if (offM > 1e-6f) carryFloorOff *= Mathf.Max(Mathf.Exp(-carryOffDecay * cMove), offM - cMove > 0f ? (offM - cMove) / offM : 0f);
                    carryCursorPrev = r; carryJustGrabbed = false;
                    Vector3 rr = r + carryFloorOff;
                    // в зону прижат курсор (r), смещение к нему гаснет по ходу мыши: схватил у самого тела (ближе края зоны) — кисть
                    // не сдёргивается на край зоны в первый же кадр, приходит в зону вместе с ходом мыши
                    if (carryFloorOff.sqrMagnitude < 1e-6f && !(bodyCube && InBodyZone(rr))) rr = Reach(rr);
                    Vector3 want = HandAtLedge(rr, carry: true);                           // с предметом на весу — через край вниз можно
                    if (ArmThroughBody3(want, CarryGrabY)) want = FreeHandAroundBody(want);
                    dragTarget = Vector3.Lerp(dragTarget, want, 1f - Mathf.Exp(-handFollow * dt));
                    // ВЫСОТА — ПО ПОВЕРХНОСТИ ПОД КИСТЬЮ (автор 2026-11-24: «опустил курсор сверху на поверхность и двигаю по ней — рука
                    // должна тоже по ней ходить»): низ предмета — на верх того, что под кистью (пол, верх застывшего предмета, уступ);
                    // догоняет плавно: навёл на ящик — предмет поднимается на его верх, увёл на пол — опускается, за край — вниз.
                    // на вертикальной грани (стена, бок) — кисть на высоте курсора: низ предмета = высота курсора − точка хвата над низом
                    float surf = onSide ? p.y - (CarryGrabY - dragItem.WorldBounds.min.y) : CarrySurfaceAt(want);
                    // курсор на верху стены (тумба, полка), а кисть ещё у её бока — высота по верху под курсором: предмет поднимается
                    // над стеной, и кисть проходит над ней (HandAtLedge: стена ниже низа предмета — не преграда)
                    if (!onSide) { float sc = CarrySurfaceAt(rr); if (!float.IsNaN(sc) && (float.IsNaN(surf) || sc > surf)) surf = sc; }
                    if (onSide && !float.IsNaN(CarrySurfaceAt(want))) surf = Mathf.Max(surf, CarrySurfaceAt(want));   // не ниже того, что под кистью
                    if (!float.IsNaN(surf))
                    {
                        float liftWant = Mathf.Clamp(surf - BodyLevel + 0.005f, -handDown, shoulderLift + L);   // сверху — длина руки (шар в CarryItem)
                        carryLift = Mathf.Lerp(carryLift, liftWant, 1f - Mathf.Exp(-handFollow * dt));
                    }
                }
                if (!CarryItem(dt)) return true;
                gestureOwner = "тащит предмет";
                BackArmStep(px);                                       // курсор за спиной — «на спину» (блок Back)
                dragItem.SetLook(false, true, backArmed);
                return true;
            }
            hoverItem = null;
            if (!orbit && FindItem(px, out var it, out var ih, out var ip)) { hoverItem = it; hoverItemHand = ih; hoverItemPoint = ip; }
            if (prevHover && prevHover != hoverItem) prevHover.SetLook(false, false);
            if (!hoverItem) return false;
            hoverItem.SetLook(true, false);
            hoverHold = null;
            hand = Vector3.Lerp(hand, hoverItemHand, 1f - Mathf.Exp(-handFollow * dt));
            if (inp.down) BeginDrag(hoverItem, hoverItemHand, hoverItemPoint);
            return true;
        }

        /// <summary>Перенос: точка хвата — к цели кисти; высота — курсором за зоной (carryLift), не ниже опоры под предметом;
        /// цель не в теле. Кисть рисуется в точке хвата. Ушла дальше руки — не удержал. false — отпустил.</summary>
        bool CarryItem(float dt)
        {
            var it = dragItem;
            Vector3 grabW = it.transform.TransformPoint(grabLocal);
            Vector3 com = it.Body.worldCenterOfMass;
            Bounds b = it.WorldBounds;
            // ТОЧКА ХВАТА — В ПРЕДЕЛАХ РУКИ (автор 2026-11-24: «взялись за точку — именно она двигается с рукой, а объект сам
            // скользит в руке»): высота точки хвата — внутри шара руки вокруг плеча. ВЫСОТА ГЛАВНЕЕ ДАЛИ (автор: «мышкой двигаю
            // выше — рука не двигается, лимит высоты»): вытянутая рука, поднимаясь, подтягивает кисть к плечу по дуге,
            // а не упирается в потолок «по горизонтальной дали».
            float grabAbove = grabW.y - b.min.y, sy = ShoulderY, Lr = L * 0.999f;
            float gy = Mathf.Clamp(BodyLevel + carryLift + grabAbove, sy - Lr, sy + Lr);
            carryLift = gy - grabAbove - BodyLevel;
            Vector3 aim = dragTarget, toAim = Flat(aim - Shoulder);
            float dhMax = Mathf.Sqrt(Mathf.Max(0f, Lr * Lr - (gy - sy) * (gy - sy)));
            if (toAim.magnitude > dhMax) aim = Flat(Shoulder) + toAim.normalized * dhMax;
            Vector3 tc = aim + Flat(com - grabW);                                  // цель центра (XZ): точка хвата — в цель кисти
            // над головой (низ предмета выше верха тела) — от тела не отодвигать; ниже — не в тело
            float top = BodyTopY;
            if (b.min.y < top + itemBodyGap) tc = KeepOffBody(tc, it.FootPrint);
            // высота низа предмета — та, что задал курсор за зоной (carryLift от уровня тела), но не ниже опоры под предметом СЕЙЧАС:
            // не вдавливать в пол / уступ (трение от давления гасило ход вбок); упёрся вниз — запас спуска не копится
            float sup = SupportUnder(com, b, it, out _);
            float floorLift = sup - BodyLevel;
            // над головой опускаешь — встаёт на голову, не сквозь (след предмета над кубиком тела)
            carryOnHead = false;
            if (b.min.y >= top - 0.01f && BoxHitsFoot(Body, it.FootPrint.At(Flat(tc)), 0f))
            {
                float headLift = top + itemBodyGap - BodyLevel;
                if (headLift > floorLift) { floorLift = headLift; carryOnHead = true; }
            }
            carryOnSupport = carryLift <= floorLift + 0.005f;                    // стоит на опоре — касание сверху (зона 3)
            carryLift = Mathf.Max(carryLift, floorLift);
            // ЗАЗОР НАД ОПОРОЙ — ПЛАВНО (автор 2026-11-24: «схватил второй-третий в башне — всё улетает»): несомый висит на 1 см над
            // опорой (не трётся); раньше этот сантиметр брался за один кадр — толчок вверх ~1 м/с подбрасывал то, что лежит сверху
            carryHover = Mathf.MoveTowards(carryHover, 0.01f, 0.1f * dt);
            tc.y = BodyLevel + carryLift + (com.y - b.min.y) + carryHover;
            float m = Mathf.Max(0.05f, it.Body.mass);
            carryTc = tc; carryHave = true;                                                        // тягу задаёт каждый фиксированный шаг физики (CarryPhysicsStep)
            carrySpeedNow = carryMaxSpeed * Mathf.Min(1f, carrySpeedMass / m);
            carryCanLift = m * -Physics.gravity.y < carryForce;
            hand = Flat(grabW);                                                    // кисть — на предмете
            if (Flat(grabW - Shoulder).magnitude > L + carryBreak) { EndDrag("не удержал — предмет ушёл дальше руки"); return false; }
            return true;
        }

        /// <summary>Самая высокая опора под следом (центр и углы габарита, перенесённого в c) не выше, чем рука поднимет:
        /// луч физики вниз — пол, ступень, другой предмет (кроме самого себя). tall — под следом есть что-то выше руки (преграда).</summary>
        float SupportUnder(Vector3 c, Bounds b, ProtoItem self, out bool tall)
        {
            float cap = BodyLevel + handUp, top = cap + b.size.y + 0.5f, best = 0f;
            tall = false;
            var k = self.FootPrint.At(c).Corners(0.95f);                         // углы повёрнутого следа
            Vector3[] pts = { c, k[0], k[1], k[2], k[3] };
            foreach (var p in pts)
                foreach (var h in Physics.RaycastAll(new Vector3(p.x, top, p.z), Vector3.down, top + 1f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.attachedRigidbody && h.collider.attachedRigidbody == self.Body) continue;
                    if (h.collider == BodyCollider) continue;                     // кубик тела — не опора для предмета
                    if (h.point.y > cap) { tall = true; continue; }               // выше, чем рука поднимет, — бок уступа, не опора
                    if (h.point.y > b.min.y + 0.02f) continue;                    // выше низа предмета — не под ним (то, что лежит НА нём, — не опора)
                    if (h.point.y > best) best = h.point.y;
                }
            return best;
        }

        /// <summary>Цель центра предмета не в кубике тела: след (повёрнутый, перенесённый в c) ближе itemBodyGap — отодвинуть от тела.</summary>
        Vector3 KeepOffBody(Vector3 c, ProtoItem.Foot f)
        {
            for (int k = 0; k < 30; k++)
            {
                if (!BoxHitsFoot(Body, f.At(c), -itemBodyGap)) break;
                Vector3 d = Flat(c) - Body; if (d.sqrMagnitude < 1e-6f) d = Face;
                c += d.normalized * 0.01f;
            }
            return c;
        }

        /// <summary>Высота кисти на предмете: тащит — точка хвата; наведена — верх предмета; иначе +∞ (не наш случай).</summary>
        float ItemHandY()
        {
            if (dragItem) return dragItem.transform.TransformPoint(grabLocal).y - handLift;
            if (!hoverItem) return float.PositiveInfinity;
            if (hoverItem == backItem) return HandLevel(hand);                // снимаешь со спины — кисть перед собой, на полу
            return float.IsNaN(hoverItemPoint.y) ? hoverItem.WorldBounds.max.y : hoverItemPoint.y - handLift;   // кисть — в точке хвата (бок, угол, верх)
        }

        void DragReset()
        {
            UnfreezeOthers();
            if (dragItem) { dragItem.EndCarry(); dragItem.SetLook(false, false); }
            BackReset();
            if (hoverItem) hoverItem.SetLook(false, false);
            dragItem = hoverItem = null;
        }
    }
}
