using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Блок 85 · ЗАЦЕП ПЛЕЧОМ + ГОЛОВОЙ (LOCK). Ползёшь вперёд при хвате, а у культи плеча подвижный предмет —
    /// он зацепляется и едет вместе с телом, не отлетая. Отцеп: тело отъезжает назад, лечь/сесть, клик ЛКМ по нему.</summary>
    public class HeroHook : HeroBlock
    {
        public override int Order => 85;
        public float hookRadius = 0.22f, hookMaxMass = 80f;
        Vector3 local; Quaternion rot;

        public override void Tick(HeroController h, float dt)
        {
            Vector3 F = h.transform.forward;
            if (h.toggleProne) Unhook(h);
            if (!h.hooked && h.mode == HeroController.Mode.Grip && Vector3.Dot(h.moved, F) > 1e-4f)
            {
                foreach (var c in Physics.OverlapSphere(h.body.StumpShoulder, hookRadius, ~0, QueryTriggerInteraction.Ignore))
                {
                    var rb = c.attachedRigidbody;
                    if (!rb || rb.isKinematic || rb == h.carried || rb == h.onHead || rb == h.held || rb == h.anchorRb || rb.mass > hookMaxMass) continue;
                    h.hooked = rb; rb.isKinematic = true; h.IgnoreBody(rb, true);
                    local = h.transform.InverseTransformPoint(rb.position);
                    rot = Quaternion.Inverse(h.transform.rotation) * rb.rotation;
                    h.Log($"зацепил плечом: {rb.name}");
                    break;
                }
            }
            if (!h.hooked) return;
            if (Vector3.Dot(h.moved, F) < -0.002f) { Unhook(h); return; }
            Vector3 p = h.transform.TransformPoint(local);
            h.hooked.position = p; h.hooked.transform.position = p;
            h.hooked.rotation = h.transform.rotation * rot; h.hooked.transform.rotation = h.hooked.rotation;
        }

        public void Unhook(HeroController h)
        {
            if (!h.hooked) return;
            h.hooked.isKinematic = false; h.hooked.linearVelocity = Vector3.zero;
            h.IgnoreBody(h.hooked, false);
            h.Log($"отцепил {h.hooked.name}");
            h.hooked = null;
        }
    }
}
