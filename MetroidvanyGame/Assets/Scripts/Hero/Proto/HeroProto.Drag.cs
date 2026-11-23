using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ПЕРЕТАСКИВАНИЕ ПРЕДМЕТА — блок поверх общего состояния (переделка 2026-11-22: предметы целиком на физике Unity).
    ///
    /// · НАВЕДЕНИЕ / ЛОВЛЯ (автор 2026-11-22: «хотел двигаться, а зацепился за объект»): только КУРСОР на самом предмете (луч в его
    ///   коллайдер) и рука до него достаёт — кисть наводится на предмет. Кисть, идущая рядом с предметом, его не ловит.
    ///   ЛКМ — кисть берёт его в точке, куда указала (тело не едет, жесты езды и поворота не работают).
    /// · ПЕРЕНОС: пока ЛКМ зажата, цель кисти идёт за курсором теми же правилами, что свободная рука (досягаемость, сектор, не сквозь
    ///   стену и тело). Предмет ведёт к цели физика (ProtoItem.CarryStep): сила с пределом carryForce — тяжёлый медленнее и не
    ///   поднимается; высота цели — по опоре под следом (луч физики: пол, ступень, другой предмет) — предмет заезжает на ступень
    ///   и на другой предмет сам; в стены и ступени упирается сам; стопку на нём везёт трение, рывок её роняет.
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
        [Tooltip("Перенос: желаемая скорость предмета = расстояние до цели × столько (1/с).")]
        public float carryGain = 12f;
        [Tooltip("Перенос: предмет в руке не быстрее этого (м/с).")]
        public float carryMaxSpeed = 2.5f;
        [Tooltip("Перенос: сила руки (Н). Тяжелее силы / g — рука не поднимает, только тащит; тяжёлый разгоняется медленнее.")]
        public float carryForce = 60f;
        [Tooltip("Точка хвата ушла от плеча дальше руки на столько (м) — не удержал, предмет отпущен.")]
        public float carryBreak = 0.25f;

        ProtoItem hoverItem, dragItem;
        Vector3 hoverItemHand;               // куда встанет кисть на наведённом предмете (XZ)
        Vector3 grabLocal;                   // точка хвата в координатах предмета
        Vector3 dragTarget;                  // цель кисти (XZ) — за курсором

        public ProtoItem DraggedItem => dragItem;
        public bool Dragging => dragItem != null;

        /// <summary>Предмет под курсором (луч в его коллайдер), до которого кисть достаёт. hand — точка кисти на нём (XZ).</summary>
        bool FindItem(Vector2 px, out ProtoItem best, out Vector3 hand)
        {
            best = null; hand = default;
            if (ProtoItem.Live.Count == 0) return false;
            Ray r = cam.ScreenPointToRay(px);
            if (FindBackItem(r, out var bh)) { best = backItem; hand = bh; return true; }   // предмет на спине — снять (блок Back)
            Vector3 S = Shoulder;
            float bestD = float.MaxValue;
            foreach (var it in ProtoItem.Live)
            {
                if (!it || it.held) continue;
                Bounds b = it.WorldBounds;
                var col = it.GetComponent<Collider>();
                float hitD;
                if (col) { if (!col.Raycast(r, out var hit, 1000f)) continue; hitD = hit.distance; }
                else if (!b.IntersectRay(r, out hitD)) continue;                 // курсор не на предмете — не ловим
                if (hitD >= bestD) continue;                                       // ближний по лучу (верх стопки) первым
                var f = it.FootPrint;
                Vector3 q = f.Closest(Flat(r.GetPoint(hitD)), 0.01f);            // кисть — куда указал курсор
                if (!HandReaches(q)) { q = f.Closest(S, 0.01f); if (!HandReaches(q)) continue; }   // дальний край — ближний к плечу
                bestD = hitD; best = it; hand = q;
            }
            return best;
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
            if (it == backItem) UnlockFromBack(h);                     // со спины — в руку перед собой (блок Back)
            Bounds b = it.WorldBounds;
            grabLocal = it.transform.InverseTransformPoint(new Vector3(h.x, b.max.y, h.z));   // держит за верх в точке хвата
            dragItem = it;
            it.BeginCarry();
            hand = h; dragTarget = h;
            hoverItem = null;
            gestureOwner = "взял предмет";
        }

        void EndDrag(string why)
        {
            if (!dragItem) return;
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
                if (!orbit && FloorPoint(px, out var p))
                {
                    Vector3 want = FreeHandAroundBody(HandAtLedge(Reach(p)));
                    dragTarget = Vector3.Lerp(dragTarget, want, 1f - Mathf.Exp(-handFollow * dt));
                }
                if (!CarryItem(dt)) return true;
                gestureOwner = "тащит предмет";
                BackArmStep(px);                                       // курсор за спиной — «на спину» (блок Back)
                dragItem.SetLook(false, true, backArmed);
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

        /// <summary>Перенос: точка хвата — к цели кисти; высота цели — по опоре под следом (пол, ступень, другой предмет);
        /// цель не в теле. Кисть рисуется в точке хвата. Ушла дальше руки — не удержал. false — отпустил.</summary>
        bool CarryItem(float dt)
        {
            var it = dragItem;
            Vector3 grabW = it.transform.TransformPoint(grabLocal);
            Vector3 com = it.Body.worldCenterOfMass;
            Bounds b = it.WorldBounds;
            Vector3 tc = dragTarget + Flat(com - grabW);                          // цель центра (XZ): точка хвата — в цель кисти
            tc = KeepOffBody(tc, it.FootPrint);
            float sup = SupportUnder(tc, b, it);                                   // опора под следом в цели
            tc.y = sup + (com.y - b.min.y) + 0.01f;
            it.CarryStep(tc, dt, carryGain, carryMaxSpeed, carryForce);
            hand = Flat(grabW);                                                    // кисть — на предмете
            if (Flat(grabW - Shoulder).magnitude > L + carryBreak) { EndDrag("не удержал — предмет ушёл дальше руки"); return false; }
            return true;
        }

        /// <summary>Самая высокая опора под следом (центр и углы габарита, перенесённого в c) не выше, чем рука поднимет:
        /// луч физики вниз — пол, ступень, другой предмет (кроме самого себя).</summary>
        float SupportUnder(Vector3 c, Bounds b, ProtoItem self)
        {
            float cap = BodyLevel + handUp, top = cap + b.size.y + 0.5f, best = 0f;
            var k = self.FootPrint.At(c).Corners(0.95f);                         // углы повёрнутого следа
            Vector3[] pts = { c, k[0], k[1], k[2], k[3] };
            foreach (var p in pts)
                foreach (var h in Physics.RaycastAll(new Vector3(p.x, top, p.z), Vector3.down, top + 1f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.attachedRigidbody && h.collider.attachedRigidbody == self.Body) continue;
                    if (h.collider == BodyCollider) continue;                     // кубик тела — не опора для предмета
                    if (h.point.y > cap) continue;                                // выше, чем рука поднимет, — бок уступа, не опора
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
            return hoverItem.WorldBounds.max.y;
        }

        void DragReset()
        {
            if (dragItem) { dragItem.EndCarry(); dragItem.SetLook(false, false); }
            BackReset();
            if (hoverItem) hoverItem.SetLook(false, false);
            dragItem = hoverItem = null;
        }
    }
}
