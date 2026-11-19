using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>БОТ — отдельный блок: водит героя тем же вводом, что и мышь (курсор в пикселях, ЛКМ, сдвиг мыши).
    /// Попытка = навести кисть → нажать → тянуть (на себя, иногда от себя, вбок — поворот к цели) → отпустить.
    /// randomness 0 — идёт к цели (маршрут), 1 — бродяга со случайным разбросом.
    /// Застрял (6 пустых попыток подряд) → проба выхода во все стороны; не сдвинулся никуда — ЛОВУШКА.
    /// Метрики ощущения: пустые попытки (ввод был, тело не сдвинулось), жестов на метр.</summary>
    public class ProtoBot
    {
        readonly HeroProto h; readonly Camera cam; readonly System.Random rng;
        public float randomness = 1f;
        public Vector3 target; public bool hasTarget;

        enum Ph { Aim, Press, Pull, Release }
        Ph ph = Ph.Aim; int ticks, pullTicks; float pullPx, sidePx; Vector3 aim, attemptStart; bool push;

        // метрики
        public int attempts, dead, consecutiveDead, traps;
        public float travelled; Vector3 lastBody; float attemptY;
        public readonly List<string> events = new List<string>();
        // проба выхода
        int escapeStep = -1; Vector3 escapeFrom;
        static readonly float[] EscapeAng = { 0f, 45f, -40f, 65f, -45f, 20f, -20f, 0f };
        public bool trapped;

        public ProtoBot(HeroProto hero, Camera camera, int seed) { h = hero; cam = camera; rng = new System.Random(seed); lastBody = h.BodyXZ; }

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public HeroProto.InputFrame Frame()
        {
            var f = new HeroProto.InputFrame();
            Vector3 body = h.BodyXZ;
            if (hasTarget && AtTarget) { escapeStep = -1; reached++; NewTarget(null); ph = Ph.Release; ticks = 0; }
            travelled += (body - lastBody).magnitude; lastBody = body;
            switch (ph)
            {
                case Ph.Aim:
                    if (ticks == 0) PlanAttempt();
                    f.pos = Screen(aim);
                    if (++ticks >= 6) { ph = Ph.Press; ticks = 0; }
                    break;
                case Ph.Press:
                    f.pos = Screen(aim); f.down = true; f.pressed = true;
                    attemptStart = body; attemptY = h.BodyY; ph = Ph.Pull; ticks = 0;
                    break;
                case Ph.Pull:
                    f.pos = Screen(aim); f.pressed = true;
                    f.delta = new Vector2(sidePx, push ? pullPx : -pullPx);
                    if (++ticks >= pullTicks) { ph = Ph.Release; ticks = 0; }
                    break;
                case Ph.Release:
                    f.pos = Screen(aim);
                    if (++ticks >= 3) { EndAttempt(); ph = Ph.Aim; ticks = 0; }
                    break;
            }
            return f;
        }

        Vector2 Screen(Vector3 w) => cam.WorldToScreenPoint(w);

        void PlanAttempt()
        {
            Vector3 body = h.BodyXZ, face = h.FaceDir;
            Vector3 to = hasTarget ? target - body : face; to.y = 0f;   // цель — по полу (высота — только для «дошёл»)
            float a = to.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(face, to, Vector3.up) : 0f;
            push = false; sidePx = 0f;
            float aimAng;
            if (escapeStep >= 0)
            {
                aimAng = EscapeAng[escapeStep];
                push = escapeStep == EscapeAng.Length - 1;      // последняя проба — от себя (назад)
                sidePx = escapeStep % 3 == 1 ? 8f : escapeStep % 3 == 2 ? -8f : 0f;
            }
            else
            {
                aimAng = Mathf.Clamp(a, -45f, 65f) + R(-40f, 40f) * randomness;
                if (Mathf.Abs(a) > 110f) { aimAng = 0f; push = true; }        // цель за спиной — от себя (тело назад)
                if (Mathf.Abs(a) > 25f) sidePx = Mathf.Sign(a) * Mathf.Min(Mathf.Abs(a) * 0.25f, 12f);   // мышь вправо = поворот вправо
                if (randomness > 0f && rng.NextDouble() < 0.15 * randomness) push = true;
                if (randomness > 0f && rng.NextDouble() < 0.2 * randomness) sidePx += R(-10f, 10f);
            }
            float reach = R(0.35f, 0.6f);
            aim = h.ShoulderXZ + Quaternion.AngleAxis(aimAng, Vector3.up) * face * reach;
            // высота прицела: поверхность своего уровня (кромка уступа — тоже), иначе уровень тела
            float ly = h.HandLevelAt(aim);
            aim.y = float.IsInfinity(ly) ? h.GroundY : ly;
            // зацеп по пути к цели — лучше поверхности (на крутом поверхность не держит)
            if (useHolds && escapeStep < 0 && BestHold(to, out Vector3 hp)) { aim = hp; holdAims++; }
            pullPx = R(6f, 14f); pullTicks = randomness > 0f ? (int)R(8f, 20f) : (int)R(18f, 30f);
        }

        public bool useHolds = true;
        public int holdAims;

        /// <summary>Точка на зацепе в досягаемости и в секторе, дающая больше всего хода к цели.</summary>
        bool BestHold(Vector3 to, out Vector3 best)
        {
            best = default; float bestP = 0.08f;
            Vector3 S3 = h.ShoulderPos3, body = h.BodyXZ, face = h.FaceDir;
            bool climbing = hasTarget && target.y > h.BodyY + 0.1f;
            Vector3 dir = to.sqrMagnitude > 1e-4f ? to.normalized : face;
            foreach (var hd in h.Holds)
            {
                if (!hd) continue;
                for (int i = 0; i <= 40; i++)
                {
                    Vector3 P = hd.At(i / 40f);
                    if ((P - S3).magnitude > h.L - 0.03f) continue;
                    Vector3 v = new Vector3(P.x - S3.x, 0f, P.z - S3.z);
                    if (v.magnitude > 0.03f) { float a = Vector3.SignedAngle(face, v, Vector3.up); if (a < -45f || a > 65f) continue; }
                    float flat = Vector3.Dot(new Vector3(P.x - body.x, 0f, P.z - body.z), dir);
                    if (flat < 0.05f) continue;                                   // зацеп не по пути — не нужен
                    float prog = flat + (climbing ? (P.y - h.BodyY) * 0.3f : 0f);
                    if (prog > bestP) { bestP = prog; best = P; }
                }
            }
            return bestP > 0.08f;
        }

        void EndAttempt()
        {
            attempts++;
            float moved = (h.BodyXZ - attemptStart).magnitude + Mathf.Abs(h.BodyY - attemptY);
            bool isDead = moved < 0.01f;
            if (isDead) { dead++; consecutiveDead++; } else consecutiveDead = 0;

            if (escapeStep >= 0)
            {
                if ((h.BodyXZ - escapeFrom).magnitude > 0.03f) { escapeStep = -1; NewTarget("вышел после пробы"); return; }
                if (++escapeStep >= EscapeAng.Length)
                {
                    traps++; trapped = true; escapeStep = -1;
                    events.Add($"ЛОВУШКА: ни в одну сторону не сдвинуться, тело ({h.BodyXZ.x:F2}, {h.GroundY:F2}, {h.BodyXZ.z:F2}), лицо {Mathf.Atan2(h.FaceDir.x, h.FaceDir.z) * Mathf.Rad2Deg:F0}°");
                }
                return;
            }
            if (consecutiveDead >= 6) { escapeStep = 0; escapeFrom = h.BodyXZ; consecutiveDead = 0; }
            if (hasTarget && AtTarget) { reached++; NewTarget(null); }
            else if (hasTarget && ++targetAttempts > maxTargetAttempts) { failedTargets++; NewTarget(null); }
        }

        /// <summary>Цель достигнута: по полу ближе 0.35 м и (если у цели задана высота) на её уровне.</summary>
        public float reachRadius = 0.35f;
        bool AtTarget => new Vector2(target.x - h.BodyXZ.x, target.z - h.BodyXZ.z).magnitude < reachRadius && (target.y <= 0f || Mathf.Abs(h.BodyY - target.y) < 0.15f);
        public int reached, failedTargets, targetAttempts, maxTargetAttempts = 60;
        public System.Func<Vector3> pickTarget;
        void NewTarget(string why)
        {
            targetAttempts = 0;
            if (pickTarget != null) { target = pickTarget(); hasTarget = true; }
            else hasTarget = false;
        }
        public void Start(Vector3? first) { if (first.HasValue) { target = first.Value; hasTarget = true; } else NewTarget(null); }

        public string Report() =>
            $"попыток {attempts}, пустых {dead} ({(attempts > 0 ? 100f * dead / attempts : 0f):F0}%), путь {travelled:F1} м, " +
            $"жестов на метр {(travelled > 0.01f ? attempts / travelled : 0f):F1}, целей достигнуто {reached}, не дошёл {failedTargets}, ловушек {traps}" +
            (events.Count > 0 ? "\n    " + string.Join("\n    ", events) : "");
    }
}
