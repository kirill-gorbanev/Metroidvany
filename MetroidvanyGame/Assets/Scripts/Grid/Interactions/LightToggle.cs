using UnityEngine;

namespace Fortress.Grid
{
    /// <summary>Источник света (факел, фонарь, свеча): E — погасить / зажечь.</summary>
    public class LightToggle : Interactable
    {
        public Light lamp;
        void Awake()
        {
            reach = 1.5f;
            if (!lamp) lamp = GetComponentInChildren<Light>();
            // настенный светильник (фонарь на кронштейне, факел) висит над полом — коробкой не мешать проходу
            // (фонарь торчал от стены на 1.3 м с низом на 1.17 м — капсула под ним не проходила); E работает и по триггеру
            foreach (var c in GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                var b = c.bounds;
                if (Physics.Raycast(new Vector3(b.center.x, b.min.y - 0.02f, b.center.z), Vector3.down, out var h, 6f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                    && b.min.y - h.point.y > 0.9f)
                    c.isTrigger = true;
            }
        }
        public override string Prompt => lamp && lamp.enabled ? "E — погасить" : "E — зажечь";
        public override void Interact(Transform who) { if (lamp) lamp.enabled = !lamp.enabled; }
    }
}
