using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Блок 10 · ПОЗА. Сидя ↔ ничком: СКМ переключает, под потолком ниже sitClearance ложится сам и не садится.
    /// Подстраивает капсулу (выше сидя, ниже и вперёд ничком).</summary>
    public class HeroPosture : HeroBlock
    {
        public override int Order => 10;
        public float proneSpeed = 0.9f;
        [Tooltip("Если над тазом потолок ниже — сидеть нельзя, ложится (м).")]
        public float sitClearance = 0.95f;
        float raw, target;

        public override void Tick(HeroController h, float dt)
        {
            bool low = false;
            foreach (var r in Physics.RaycastAll(h.transform.position + Vector3.up * 0.2f, Vector3.up, sitClearance - 0.2f, ~0, QueryTriggerInteraction.Ignore))
                if (!h.Mine(r.transform)) { low = true; break; }
            if (h.toggleProne) target = target > 0.5f ? 0f : 1f;
            raw = Mathf.MoveTowards(raw, low ? 1f : target, proneSpeed * dt);
            h.prone = Mathf.SmoothStep(0f, 1f, raw);
            var cc = h.CC;
            cc.height = Mathf.Lerp(0.9f, 0.5f, h.prone);
            cc.radius = Mathf.Min(0.28f, cc.height * 0.5f);
            cc.center = new Vector3(0f, cc.height * 0.5f, Mathf.Lerp(0f, 0.25f, h.prone));
        }
    }
}
