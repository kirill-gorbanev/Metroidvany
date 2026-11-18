using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Блок 50 · ПЕРЕХВАТ (автоматический). Держишь ЛКМ и тянешь на себя, а тело уже упёрлось в руку
    /// (рука согнута до предела) — рука сама отпускает и переставляется вперёд по направлению жеста к следующей опоре
    /// (выступ важнее пола), там хватается (блок HeroGrip) — и тянешь дальше. Ползти можно одним непрерывным жестом.</summary>
    public class HeroAutoReach : HeroBlock
    {
        public override int Order => 50;
        [Tooltip("Сколько тянуть «в упор», прежде чем рука переставится (с).")]
        public float pinnedTime = 0.06f;
        [Tooltip("Насколько далеко вперёд переставлять (доля досягаемости).")]
        public float reachAhead = 0.55f;
        public float searchRadius = 0.30f;
        float bent;

        public override void Tick(HeroController h, float dt)
        {
            if (h.mode != HeroController.Mode.Grip || h.held || h.climbing) { bent = 0f; return; }
            bool pinned = h.wantAlong > 1e-4f && h.along < h.wantAlong * 0.25f;
            bent = pinned ? bent + dt : (h.wantAlong < -1e-4f ? 0f : bent);   // пауза жеста не сбрасывает
            if (!h.hold || bent < pinnedTime) return;
            Vector3 g = (h.dirA + h.sideA * Mathf.Clamp(h.side / Mathf.Max(h.wantAlong, 1e-4f), -0.8f, 0.8f)).normalized;
            if (Next(h, g, out var p, out var n))
            {
                h.EndGrip();
                h.reachTo = p; h.reachToN = n; h.reachUntil = Time.time + 1.5f; bent = 0f;
                h.Log($"перехват → {p}");
            }
            else h.Log("перехват: опоры впереди нет");
        }

        bool Next(HeroController h, Vector3 dir, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = Vector3.up;
            Vector3 S = h.S;
            // вперёд по жесту — но в пределах досягаемости плеча (с учётом высоты плеча над опорой)
            float gy = h.GroundY();
            float hgt = Mathf.Max(0f, S.y - gy);
            float horiz = Mathf.Sqrt(Mathf.Max(0.01f, h.reach * h.reach * 0.8f - hgt * hgt));
            Vector3 P = new Vector3(S.x, gy, S.z) + dir * Mathf.Min(horiz, h.reachMax * reachAhead);
            P = h.ClipByPlanes(S, P + Vector3.up * 0.3f);
            if (Physics.Raycast(P + Vector3.up * 0.4f, Vector3.down, out var r, 1.6f, ~0, QueryTriggerInteraction.Ignore) && !h.Mine(r.transform))
                P = r.point;
            if (!h.FindGrip(P, searchRadius, out point, out normal, out _)) return false;
            return Vector3.Distance(S, point) <= h.reachMax;
        }
    }
}
