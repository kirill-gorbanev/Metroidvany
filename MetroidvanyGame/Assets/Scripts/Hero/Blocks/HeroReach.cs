using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Блок 20 · КУДА ТЯНЕТСЯ СВОБОДНАЯ РУКА (и рука с ношей).
    /// Цель: перехват (если идёт) → автонаведение на выступ у курсора → поверхность под курсором → направление курсора.
    /// «Под» поверхность (после петли): цель — первое попадание ниже столешницы, рука заходит снизу.
    /// Плечо гомологичное: вперёд и вправо (сторона руки), через грудь — немного; у самого тела — спереди/справа без ограничения.
    /// Рука не проходит сквозь предметы (отсечение плоскостями).</summary>
    public class HeroReach : HeroBlock
    {
        public override int Order => 20;
        public float handSmooth = 0.10f, handMaxSpeed = 3f;
        [Tooltip("Радиус автонаведения на выступ/поверхность у курсора (м).")]
        public float assistRadius = 0.30f;
        [Tooltip("Плечо: через грудь влево / вправо-назад (градусы от «вперёд»).")]
        public float shoulderAcross = 45f, shoulderOut = 150f;
        public float carryLift = 0.12f;
        Vector3 vel;

        public override void Tick(HeroController h, float dt)
        {
            if (h.mode == HeroController.Mode.Grip || h.orbiting) return;
            bool free = h.mode == HeroController.Mode.Free;
            Vector3 S = h.S;
            Vector3 cursorP = h.hit ? h.rh.point : h.ray.GetPoint(3f);
            Vector3 want;
            h.contact = false;
            if (free && h.reachTo.HasValue)
            {
                want = S + Vector3.ClampMagnitude(h.reachTo.Value + h.reachToN * 0.025f - S, h.reachMax);
                Contact(h, h.reachTo.Value, h.reachToN);
            }
            else if (free && Vector3.Distance(S, cursorP) <= h.reachMax + assistRadius
                     && h.FindGrip(cursorP, assistRadius, out var ap, out var an, out _) && Vector3.Distance(S, ap) <= h.reachMax)
            {
                want = ap + an * 0.025f;                                   // автонаведение
                Contact(h, ap, an);
            }
            else
            {
                Vector3 p = cursorP;
                if (!free)
                {
                    p += Vector3.up * carryLift;
                    Vector3 top = h.body.HeadTop;                          // курсор на голове — ношу к макушке
                    if (Vector3.Cross(h.ray.direction, top - h.ray.origin).magnitude < 0.22f) p = top + Vector3.up * 0.10f;
                }
                want = S + Vector3.ClampMagnitude(p - S, h.reachMax);
                float gy = h.GroundY() + 0.03f;
                if (want.y < gy) want.y = gy;
            }
            want = ShoulderCone(h, S, want);
            // «под»: рука идёт к цели снизу столешницы — отсекаем от точки ниже первой поверхности
            Vector3 from = S;
            if (h.preferUnder && h.RayWorld(h.ray, out var top1, false) && top1.point.y > want.y + 0.1f)
                from.y = Mathf.Min(S.y, top1.point.y - 0.07f);
            want = h.ClipByPlanes(from, want);
            float sm = h.reachTo.HasValue ? handSmooth * 0.6f : handSmooth;
            Vector3 prev = h.handPos;
            h.handPos = Vector3.SmoothDamp(h.handPos, want, ref vel, sm, handMaxSpeed, dt);
            h.handVelS = Vector3.Lerp(h.handVelS, (h.handPos - prev) / dt, 0.5f);
        }

        static void Contact(HeroController h, Vector3 p, Vector3 n)
        {
            h.contact = true; h.contactPoint = p; h.contactN = n; h.lastContactT = Time.time;
        }

        /// <summary>Рука правая: вперёд, вправо и чуть назад; через грудь — до shoulderAcross. За спину — нельзя (кроме справа).
        /// У самого тела (под себя) — спереди и справа свободно.</summary>
        Vector3 ShoulderCone(HeroController h, Vector3 S, Vector3 want)
        {
            Vector3 f = Vector3.ProjectOnPlane(h.transform.forward, Vector3.up);
            Vector3 v = want - S;
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) return want;
            float a = Vector3.SignedAngle(f, flat, Vector3.up);              // + вправо (сторона руки)
            float lo = -shoulderAcross, hi = shoulderOut;
            if (flat.magnitude < 0.25f) { lo = -70f; hi = 160f; }              // у тела — свободнее, но не за спину слева
            float c = Mathf.Clamp(a, lo, hi);
            if (Mathf.Approximately(a, c)) return want;
            return S + Quaternion.AngleAxis(c - a, Vector3.up) * flat + Vector3.up * v.y;
        }
    }
}
