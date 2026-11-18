using UnityEngine;

namespace Fortress.Walker
{
    /// <summary>Зона ломаемых решёток: E выключает ближайший кусок решётки в зоне.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class BreakZone : MonoBehaviour
    {
        public string prompt = "E — выбить решётку";
        public float reach = 2.4f;

        public bool TryBreak(Vector3 from)
        {
            var box = GetComponent<BoxCollider>();
            var hits = Physics.OverlapBox(box.bounds.center, box.bounds.extents, Quaternion.identity, ~0,
                                          QueryTriggerInteraction.Ignore);
            Collider best = null; float bestD = reach;
            foreach (var h in hits)
            {
                if (h.GetComponentInParent<CharacterController>() != null) continue;
                float d = Vector3.Distance(from, h.bounds.ClosestPoint(from));
                if (d < bestD) { bestD = d; best = h; }
            }
            if (best == null) return false;
            best.gameObject.SetActive(false);
            return true;
        }
    }
}
