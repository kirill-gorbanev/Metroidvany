using UnityEngine;

namespace Fortress.Grid
{
    /// <summary>Решётка с засовом (сток цистерны): с той стороны, где засов, — «отодвинуть засов», и решётка
    /// отворяется калиткой навсегда; с другой стороны (из лаза, из рва) — заперто.</summary>
    public class BoltedGrate : Interactable
    {
        [Tooltip("Куда от решётки сторона засова (мировое направление)")] public Vector3 boltSide;
        bool open; float t; Vector3 pivot; float sign = 1f;

        void Awake() { reach = 1.8f; }

        bool OnBoltSide(Transform who) => Vector3.Dot(who.position - Anchor, boltSide) > 0f;
        public override string Prompt => open ? "" : "Решётка на засове";
        public override string PromptFor(Transform who) =>
            open ? "" : (OnBoltSide(who) ? "E — отодвинуть засов решётки" : "Решётка заперта на засов с той стороны");
        public override bool CanInteract(Transform who) => !open;

        public override void Interact(Transform who)
        {
            if (open || !OnBoltSide(who)) return;
            open = true;
            // петля — вертикальный край решётки; створка уходит в сторону засова (к тому, кто открыл)
            Bounds b = default; bool has = false;
            foreach (var r in GetComponentsInChildren<Renderer>()) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
            Vector3 along = b.extents.x > b.extents.z ? Vector3.right : Vector3.forward;
            pivot = (has ? b.center : transform.position) - along * (has ? Vector3.Dot(b.extents, along) : 0.9f);
            sign = Vector3.Dot(Vector3.Cross(Vector3.up, along), boltSide) > 0 ? -1f : 1f;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        void Update()
        {
            if (!open || t >= 1f) return;
            float dt = Mathf.Min(Time.deltaTime * 1.6f, 1f - t);
            t += dt;
            transform.RotateAround(pivot, Vector3.up, sign * 100f * dt);
        }
    }
}
