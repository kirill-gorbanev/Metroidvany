using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ТОЛЧОК ПОДВИЖНОЙ СТЕНЫ — отдельный блок (шаг 5 долга, часть 1).
    /// Автор: «стена движется насквозь без препятствий — не толкает перса даже в сторону; насквозь проходит, а потом выталкивает»;
    /// «в будущем зажим — это смерть».
    /// Было: подвижный элемент сдвигался, тело оказывалось внутри него, и только выталкивание (Depenetrate) кадром позже
    /// выкидывало тело наружу рывком (лифт арены 6 — до 0.8 м, челнок арены 7 — 0.27 м).
    /// Стало: сразу после шага платформ — если тело задето подвижным элементом, на котором оно не стоит, тело сдвигается
    /// ВМЕСТЕ с ним (на сдвиг элемента в точке тела) со скольжением вдоль неподвижных стен. Сдвинуть некуда (зажало между
    /// подвижным и неподвижным) — «раздавлен»: пока тело возвращается на последнее безопасное место (crushCount),
    /// потом будет смерть.</summary>
    public partial class HeroProto
    {
        [Tooltip("Подвижная стена толкает тело перед собой (а не проходит сквозь). Выключено — как раньше.")]
        public bool moverPush = true;

        Vector3 lastSafe; bool haveSafe;
        [System.NonSerialized] public int crushCount, pushCount;
        public bool DiagCrushed { get; private set; }

        /// <summary>Сразу после шага платформ (Carry): толчок подвижной стеной, зажим.</summary>
        void PushStep()
        {
            DiagCrushed = false;
            Vector3 b = Body;
            if (!moverPush || !Blocked(b, bodyRadius)) { if (!Blocked(b, bodyRadius)) { lastSafe = b; haveSafe = true; } return; }
            // кто задел: подвижный элемент рядом с телом (не тот, на котором стоишь), сдвинувшийся в этом шаге
            Vector3 push = Vector3.zero; bool byMover = false;
            foreach (var ms in movers)
            {
                var m = ms.m;
                if (!m || m == rideMover || !NearMover(ms, b)) continue;
                byMover = true;
                Vector3 v = Flat(m.Delta.MultiplyPoint3x4(b)) - b;
                if (v.sqrMagnitude > push.sqrMagnitude) push = v;
            }
            if (!byMover) return;                                       // не подвижная стена — старое поведение (выталкивание в Tick)
            if (push.sqrMagnitude < 1e-8f)
            {
                // элемент едет вверх / вниз (лифт поднялся рядом — край стал выше шага): не рывком наружу, а плавно — шаг по нормали
                Vector3 n = ContactNormal(b, bodyRadius);
                if (n == Vector3.zero) return;
                push = n * armFollowMax;
            }
            pushCount++;
            // тело — вместе со стеной; чуть дальше (зазор), со скольжением вдоль неподвижных стен
            Vector3 dir = push.normalized;
            Vector3 np = b + push + dir * 0.005f;
            if (Blocked(np, bodyRadius))
            {
                // вдоль неподвижной стены: из точки «вместе со стеной» — вытолкнуть по нормали (несколько мелких шагов)
                Vector3 q = np;
                for (int i = 0; i < 10 && Blocked(q, bodyRadius); i++) { Vector3 n = ContactNormal(q, bodyRadius); if (n == Vector3.zero) break; q += n * 0.02f; }
                if (Blocked(q, bodyRadius) || Vector3.Dot(q - b, dir) < -0.01f)
                {
                    // зажало: подвижное с одной стороны, неподвижное с другой
                    crushCount++; DiagCrushed = true;
                    q = haveSafe && !Blocked(lastSafe, bodyRadius) ? lastSafe : Depenetrate(b);
                }
                np = q;
            }
            bodyCube.position = new Vector3(np.x, bodyCube.position.y, np.z);
            if (mode == Mode.Grip) { float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0); }
            if (!Blocked(Body, bodyRadius)) { lastSafe = Body; haveSafe = true; }
        }

        bool NearMover(MoverSurf ms, Vector3 b)
        {
            foreach (var r in ms.rends)
            {
                if (!r) continue;
                var bb = r.bounds; bb.Expand(new Vector3(bodyRadius * 2f + 0.1f, 10f, bodyRadius * 2f + 0.1f));
                if (bb.Contains(new Vector3(b.x, bb.center.y, b.z))) return true;
            }
            return false;
        }
    }
}
