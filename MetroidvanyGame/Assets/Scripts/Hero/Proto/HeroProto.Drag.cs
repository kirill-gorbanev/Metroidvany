using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ПЕРЕТАСКИВАНИЕ ПРЕДМЕТА — отдельный блок (частичный класс поверх общего состояния). Шаг 1 механики «предметы / LOCK».
    ///
    /// Курсор на предмете (ProtoItem) в досягаемости — кисть наводится на него, как на камень. ЛКМ — кисть БЕРЁТ ПРЕДМЕТ,
    /// а не ставится на пол: тело не едет, жесты езды и поворота не работают. Пока ЛКМ зажата — кисть свободна и идёт за курсором
    /// теми же правилами, что свободная рука (досягаемость L, сектор перед лицом, не сквозь стену и не сквозь тело), а предмет —
    /// под кистью, на поверхности своего уровня. Отпустил ЛКМ — предмет лёг там, где был.
    ///
    /// КТО В КОГО НЕ ПРОНИКАЕТ (автор 2026-11-21):
    ///   · лежащий предмет — стена для тела: тело не наезжает (Blocked, скольжение вдоль — ContactNormal);
    ///   · предмет в руке — не в стену, не в кубик тела, не в другой предмет (скольжение по осям), не боком в ступень:
    ///     под следом предмета пробуются углы и стороны, низ — по самой высокой точке, ступень под следом — не встаёт.
    /// Не дотащить — стоит, кисть остаётся на нём. Дальше (не сделано): «на себя» у тела — на спину (LOCK, слот), на голову.</summary>
    public partial class HeroProto
    {
        [Header("Предметы (перетаскивание)")]
        [Tooltip("Предмет в руке не ближе этого зазора (м) к телу.")]
        public float itemBodyGap = 0.03f;

        ProtoItem hoverItem, dragItem;
        Vector3 hoverItemHand;               // куда встанет кисть на наведённом предмете (XZ)
        Vector3 dragOff;                     // центр предмета − кисть (XZ), взято при хвате
        float dragItemY;                     // низ предмета (плавно по поверхности)

        public ProtoItem DraggedItem => dragItem;
        public bool Dragging => dragItem != null;

        [Tooltip("ЛОВЛЯ ПРЕДМЕТА (м): кисть (там, куда она идёт за курсором) ближе этого к следу предмета — предмет пойман сразу. " +
                 "Кисть не проходит сквозь предмет: оказалась на нём — он и пойман.")]
        public float itemCatchR = 0.03f;

        /// <summary>ЛОВЛЯ (автор 2026-11-21: «плохо цепляется — навёлся на объект, должен сразу цеплять»; потом — «радиус поменьше,
        /// оставить при наведении на сам коллайдер объекта»): предмет пойман, если курсор на самом предмете (луч в его габарит)
        /// или кисть, идущая за курсором, ближе itemCatchR (3 см) к его следу — чтобы не проходила сквозь предмет на пол.
        /// Захвата по расстоянию на экране нет. Кисть встаёт туда, куда указывает курсор на предмете (или в ближайшую к кисти
        /// точку следа); не достаёт рука — в ближайшую к плечу.</summary>
        bool FindItem(Vector2 px, out ProtoItem best, out Vector3 hand)
        {
            best = null; hand = default;
            if (ProtoItem.Live.Count == 0) return false;
            Ray r = cam.ScreenPointToRay(px);
            bool haveFh = FloorPoint(px, out var fp);
            Vector3 fh = haveFh ? FreeHandAroundBody(HandAtLedge(Reach(fp))) : hand;   // куда пошла бы свободная кисть
            Vector3 S = Shoulder;
            float bestScore = float.MaxValue;
            foreach (var it in ProtoItem.Live)
            {
                if (!it || it.held) continue;
                Bounds b = it.WorldBounds;
                bool onIt = b.IntersectRay(r, out float hitD);                     // курсор на самом предмете
                Vector3 q = ClampToFoot(b, fh, 0.01f);                             // точка следа, ближайшая к кисти
                float gap = Flat(q - fh).magnitude;
                if (!onIt && gap > itemCatchR) continue;
                if (onIt) q = ClampToFoot(b, Flat(r.GetPoint(hitD)), 0.01f);       // кисть — куда указал курсор
                // рука достаёт до точки? нет — ближайшая к плечу точка следа
                if (!HandReaches(q)) { q = ClampToFoot(b, S, 0.01f); if (!HandReaches(q)) continue; }
                float score = onIt ? hitD * 1e-3f : 1f + gap;                     // курсор на предмете — ближний по лучу (верх стопки) первым
                if (score < bestScore) { bestScore = score; best = it; hand = q; }
            }
            return best;
        }

        /// <summary>Точка следа предмета, ближайшая к p (отступ inset внутрь от края).</summary>
        static Vector3 ClampToFoot(Bounds b, Vector3 p, float inset)
        {
            float ix = Mathf.Min(inset, b.extents.x), iz = Mathf.Min(inset, b.extents.z);
            return new Vector3(Mathf.Clamp(p.x, b.min.x + ix, b.max.x - ix), 0f, Mathf.Clamp(p.z, b.min.z + iz, b.max.z - iz));
        }

        /// <summary>Рука достаёт до точки на предмете: не дальше руки (с допуском), не сквозь тело, без стены между.</summary>
        bool HandReaches(Vector3 q)
        {
            Vector3 v = Flat(q - Shoulder);
            if (v.magnitude > L + 0.03f || ArmThroughBody(q)) return false;
            foreach (var o in obstacles)                                          // стена между плечом и точкой
                for (int k = 1; k <= 8; k++) { Vector3 m = Shoulder + v * (k / 8f); if (o.Contains(new Vector2(m.x, m.z))) return false; }
            return true;
        }

        void BeginDrag(ProtoItem it, Vector3 h)
        {
            dragItem = it; it.held = true;
            hand = h;
            Bounds b = it.WorldBounds;
            dragOff = Flat(b.center) - h;
            dragItemY = b.min.y;
            hoverItem = null;
            gestureOwner = "взял предмет";
            CollectRiders(it);
        }

        // ---- СТОПКА: что лежит на взятом предмете, едет вместе с ним ----
        // Автор 2026-11-21: «положить один объект на второй и т. д.». Взял нижний — стопка едет целиком (раньше верхний
        // проваливался сквозь взятый: опора «в руке» не считалась опорой). Разобрать — снимая сверху (курсор берёт верхний).
        readonly System.Collections.Generic.List<ProtoItem> riders = new System.Collections.Generic.List<ProtoItem>();
        Vector2 stackMin, stackMax;          // след всей стопки относительно центра взятого (XZ)

        /// <summary>Всё, что лежит на предмете (и на том, что на нём, и т. д.): низ на верхе опоры (±2 см), следы пересекаются.</summary>
        void CollectRiders(ProtoItem baseIt)
        {
            riders.Clear();
            var queue = new System.Collections.Generic.List<ProtoItem> { baseIt };
            for (int q = 0; q < queue.Count; q++)
            {
                Bounds sb = queue[q].WorldBounds; Rect sr = FootRect(sb);
                foreach (var o in ProtoItem.Live)
                {
                    if (!o || o.held || queue.Contains(o)) continue;
                    Bounds ob = o.WorldBounds;
                    if (Mathf.Abs(ob.min.y - sb.max.y) <= 0.02f && sr.Overlaps(FootRect(ob))) { queue.Add(o); riders.Add(o); o.held = true; }
                }
            }
            Bounds b = baseIt.WorldBounds, u = b;
            foreach (var r in riders) u.Encapsulate(r.WorldBounds);
            stackMin = new Vector2(u.min.x - b.center.x, u.min.z - b.center.z);
            stackMax = new Vector2(u.max.x - b.center.x, u.max.z - b.center.z);
        }

        void ReleaseRiders()
        {
            foreach (var r in riders) if (r) { r.held = false; r.SetLook(false, false); }
            riders.Clear();
        }

        void EndDrag(string why)
        {
            if (!dragItem) return;
            dragItem.held = false; dragItem.SetLook(false, false);
            ReleaseRiders();
            lastRelease = why;
            dragItem = null;
        }

        /// <summary>Шаг свободной руки: наведение на предмет и хват (до зацепов и пола). true — шаг руки забран блоком.</summary>
        bool DragFreeStep(float dt, InputFrame inp, Vector2 px, bool orbit)
        {
            var prevHover = hoverItem;
            if (dragItem)
            {
                if (!inp.pressed || falling) { EndDrag(falling ? "падение — предмет выпал" : "положил предмет"); return true; }
                if (!orbit && FloorPoint(px, out var p))
                {
                    Vector3 want = FreeHandAroundBody(HandAtLedge(Reach(p)));
                    hand = Vector3.Lerp(hand, want, 1f - Mathf.Exp(-handFollow * dt));
                }
                MoveItemToHand(dt);
                gestureOwner = "тащит предмет";
                return true;
            }
            hoverItem = null;
            if (!orbit && FindItem(px, out var it, out var ih)) { hoverItem = it; hoverItemHand = ih; }
            if (prevHover && prevHover != hoverItem) prevHover.SetLook(false, false);
            if (!hoverItem) return false;
            hoverItem.SetLook(true, false);
            hoverHold = null;
            hand = Vector3.Lerp(hand, hoverItemHand, 1f - Mathf.Exp(-handFollow * dt));
            if (inp.down) BeginDrag(hoverItem, hoverItemHand);
            return true;
        }

        /// <summary>Предмет — под кистью (со сдвигом хвата). Не в стену (скольжение по осям), не в тело (наружу),
        /// не на уступ выше досягаемости / в сплошное (стоит). Кисть всегда остаётся на предмете.</summary>
        void MoveItemToHand(float dt)
        {
            var it = dragItem;
            Bounds b = it.WorldBounds;
            Vector3 cur = Flat(b.center), want = hand + dragOff;
            // ПЛАВНО (автор 2026-11-21: «рывками»): раньше ход за шаг пробовался целиком — не влез, предмет стоял весь шаг,
            // потом прыгал. Теперь ход идёт кусками по 1 см до упора; упёрся — остаток вдоль стены (по осям).
            Vector3 next = cur; float g = dragItemY;
            itemFrom = cur;
            if (!ItemFits(cur, b, it, dragItemY, out g)) g = dragItemY;      // уже стоит плохо (не должно) — высоту не трогать
            Vector3 move = want - cur;
            int n = Mathf.Clamp(Mathf.CeilToInt(move.magnitude / 0.015f), 1, 20);
            Vector3 stepV = move / n;
            for (int k = 0; k < n; k++)
            {
                itemFrom = next;
                Vector3 c = next + stepV;
                if (ItemFits(c, b, it, dragItemY, out float gc)) { next = c; g = gc; continue; }
                Vector3 cx = next + new Vector3(stepV.x, 0f, 0f), cz = next + new Vector3(0f, 0f, stepV.z);
                bool okx = Mathf.Abs(stepV.x) > 1e-6f && ItemFits(cx, b, it, dragItemY, out float gx);
                bool okz = Mathf.Abs(stepV.z) > 1e-6f && ItemFits(cz, b, it, dragItemY, out float gz);
                if (okx && (!okz || Mathf.Abs(stepV.x) >= Mathf.Abs(stepV.z))) { next = cx; ItemFits(cx, b, it, dragItemY, out g); }
                else if (okz) { next = cz; ItemFits(cz, b, it, dragItemY, out g); }
                else break;                                                  // упёрся со всех сторон — стоит
            }
            // низ: вверх (на ступень) — сразу, вниз — плавно, но не в поверхность
            dragItemY = g >= dragItemY ? g : Mathf.MoveTowards(dragItemY, g, 3f * dt);
            Vector3 d = new Vector3(next.x - b.center.x, dragItemY - b.min.y, next.z - b.center.z);
            it.transform.position += d;
            foreach (var r in riders) if (r) r.transform.position += d;    // стопка едет с ним
            it.SetLook(false, true);
            hand = next - dragOff;                                          // кисть — всегда на предмете (предмет мог отстать от курсора)
        }

        Vector3 itemFrom;                    // откуда пробуется ход (ItemFits: в зазоре у тела — только не ближе к телу)
        static float RectDist(Vector3 p, Rect r) { float dx = p.x - Mathf.Clamp(p.x, r.xMin, r.xMax), dz = p.z - Mathf.Clamp(p.z, r.yMin, r.yMax); return Mathf.Sqrt(dx * dx + dz * dz); }

        static Rect FootRect(Vector3 c, Vector3 e) => Rect.MinMaxRect(c.x - e.x, c.z - e.z, c.x + e.x, c.z + e.z);
        static Rect FootRect(Bounds b) => Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);

        [Tooltip("НА СТУПЕНЬ: предмет в руке заезжает на поверхность выше его низа не больше чем на столько (м) — подсаживаешь рукой. " +
                 "Выше — бок уступа, стена для предмета.")]
        public float itemLift = 0.45f;

        /// <summary>След предмета (габарит по XZ) с центром в c: не в стене, не в кубике тела, не в другом предмете;
        /// под следом (сетка проб) — поверхность своего уровня без сплошного, не выше низа предмета + itemLift (подсадить рукой).
        /// ground — низ предмета (самая высокая точка под следом).</summary>
        bool ItemFits(Vector3 c, Bounds b, ProtoItem self, float bottom, out float ground)
        {
            ground = float.NegativeInfinity;
            Vector3 e = b.extents;
            Rect r = FootRect(c, e);
            // стены и тело — по следу всей стопки (верхний, свесившийся за край, тоже не входит)
            Rect rs = self == dragItem && riders.Count > 0 ? Rect.MinMaxRect(c.x + stackMin.x, c.z + stackMin.y, c.x + stackMax.x, c.z + stackMax.y) : r;
            foreach (var o in obstacles) if (rs.Overlaps(o)) return false;
            // ТЕЛО: наложение — никогда; в зазоре itemBodyGap — только не ближе, чем было (автор 2026-11-22: «стою плотно — не могу
            // перетаскивать, приходится отдаляться»: тело встаёт к предмету вплотную, а зазор запрещал любой ход, даже от тела)
            if (BoxHitsRect(Body, rs, 0.001f)) return false;
            if (BoxHitsRect(Body, rs, -itemBodyGap))
            {
                Vector3 f = itemFrom - c;
                Rect from = Rect.MinMaxRect(rs.xMin + f.x, rs.yMin + f.z, rs.xMax + f.x, rs.yMax + f.z);
                if (RectDist(Body, rs) < RectDist(Body, from) - 1e-4f) return false;
            }
            // ДРУГОЙ ПРЕДМЕТ под следом (укладка, автор 2026-11-21: «положить один объект на второй и т. д.»): его верх — опора,
            // если рука поднимет туда (не выше низа + itemLift и не выше своего уровня + handUp); выше — стена, как раньше
            float stackTop = float.NegativeInfinity;
            foreach (var other in ProtoItem.Live)
            {
                if (!other || other == self || other.held) continue;
                Bounds ob = other.WorldBounds;
                if (!r.Overlaps(FootRect(ob)))
                {
                    if (rs.Overlaps(FootRect(ob)) && ob.max.y > bottom + 0.005f) return false;   // свесившийся верх стопки — не в соседний предмет
                    continue;
                }
                if (ob.max.y > bottom + itemLift || ob.max.y > BodyLevel + handUp) return false;
                stackTop = Mathf.Max(stackTop, ob.max.y);
            }
            // под следом — сетка проб через ≤ 6 см (край ступени между пробами не проскочит); низ — по самой высокой
            int nx = Mathf.Clamp(Mathf.CeilToInt(2f * e.x / 0.06f), 1, 16), nz = Mathf.Clamp(Mathf.CeilToInt(2f * e.z / 0.06f), 1, 16);
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    float h = HandLevel(new Vector3(c.x - e.x * 0.98f + 1.96f * e.x * i / nx, 0f, c.z - e.z * 0.98f + 1.96f * e.z * j / nz));
                    if (float.IsInfinity(h)) return false;                           // сплошное под следом
                    ground = Mathf.Max(ground, h);
                }
            if (ground > bottom + itemLift) return false;                         // выше, чем поднимет рука, — бок уступа
            ground = Mathf.Max(ground, stackTop);                                 // на другой предмет — на его верх
            return true;
        }

        [Tooltip("Предмет без опоры (выдернули нижний из стопки, столкнули с края) опускается с такой скоростью (м/с).")]
        public float itemFallSpeed = 4f;

        /// <summary>Опора под следом r на высоте fromY и ниже: поверхность (сетка проб) и верхи других лежащих предметов.</summary>
        float ItemSupport(ProtoItem self, Rect r, float fromY)
        {
            float g = 0f;
            int nx = Mathf.Clamp(Mathf.CeilToInt(r.width / 0.06f), 1, 16), nz = Mathf.Clamp(Mathf.CeilToInt(r.height / 0.06f), 1, 16);
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    float h = LevelHeight(new Vector3(r.xMin + r.width * (0.01f + 0.98f * i / nx), 0f, r.yMin + r.height * (0.01f + 0.98f * j / nz)), fromY, 0.02f);
                    if (!float.IsInfinity(h)) g = Mathf.Max(g, h);
                }
            foreach (var other in ProtoItem.Live)
            {
                if (!other || other == self || other.held) continue;
                Bounds ob = other.WorldBounds;
                if (ob.max.y <= fromY + 0.02f && r.Overlaps(FootRect(ob))) g = Mathf.Max(g, ob.max.y);
            }
            return g;
        }

        /// <summary>ОСЕДАНИЕ: лежащий предмет, под которым ушла опора (вытащили нижний из стопки), опускается на то, что под ним.
        /// Только вниз и только по вертикали; снизу вверх (оседание по порядку) — верхний ждёт, пока сядет нижний.</summary>
        void SettleItems(float dt)
        {
            if (ProtoItem.Live.Count == 0) return;
            for (int pass = 0; pass < 2; pass++)
                foreach (var it in ProtoItem.Live)
                {
                    if (!it || it.held) continue;
                    Bounds b = it.WorldBounds;
                    float sup = ItemSupport(it, FootRect(b), b.min.y);
                    if (sup < b.min.y - 0.001f)
                        it.transform.position += Vector3.down * Mathf.Min(b.min.y - sup, itemFallSpeed * dt * 0.5f);
                }
        }

        /// <summary>Лежащие предметы (не тот, что в руке) — стена для тела: кубик тела (или круг r) в точке p задевает след предмета?</summary>
        bool ItemBlocksBody(Vector3 p, float r = -1f)
        {
            foreach (var it in ProtoItem.Live)
            {
                if (!it || it.held) continue;
                Rect o = FootRect(it.WorldBounds);
                if (r < 0f) { if (BoxHitsRect(p, o, 0.001f)) return true; }
                else
                {
                    float cx = Mathf.Clamp(p.x, o.xMin, o.xMax), cz = Mathf.Clamp(p.z, o.yMin, o.yMax);
                    float dx = p.x - cx, dz = p.z - cz;
                    if (dx * dx + dz * dz < r * r) return true;
                }
            }
            return false;
        }

        /// <summary>Нормаль от лежащих предметов (как от стен) — тело скользит вдоль предмета, а не встаёт.</summary>
        Vector3 ItemContactNormal(Vector3 p, float r)
        {
            Vector3 n = Vector3.zero;
            foreach (var it in ProtoItem.Live)
            {
                if (!it || it.held) continue;
                Rect o = FootRect(it.WorldBounds);
                float cx = Mathf.Clamp(p.x, o.xMin, o.xMax), cz = Mathf.Clamp(p.z, o.yMin, o.yMax);
                Vector3 d = new Vector3(p.x - cx, 0f, p.z - cz);
                float m = d.magnitude;
                if (m >= r) continue;
                n += m > 1e-5f ? d / m : new Vector3(p.x - o.center.x, 0f, p.z - o.center.y).normalized;
            }
            return n;
        }

        /// <summary>Высота кисти на предмете (наведена / тащит) — верх предмета; иначе +∞ (не наш случай).</summary>
        float ItemHandY()
        {
            var it = dragItem ? dragItem : hoverItem;
            if (!it) return float.PositiveInfinity;
            return it.WorldBounds.max.y;                                  // кисть лежит на верхе предмета
        }

        void DragReset()
        {
            if (dragItem) { dragItem.held = false; dragItem.SetLook(false, false); }
            ReleaseRiders();
            if (hoverItem) hoverItem.SetLook(false, false);
            dragItem = hoverItem = null;
        }
    }
}
