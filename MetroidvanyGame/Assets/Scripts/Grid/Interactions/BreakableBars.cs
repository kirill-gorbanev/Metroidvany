using UnityEngine;

namespace Fortress.Grid
{
    /// <summary>Решётка, которую можно выбить: E — решётка падает от игрока и перестаёт мешать.</summary>
    public class BreakableBars : Interactable
    {
        bool broken; float t; Vector3 axis; Vector3 pivot;

        void Awake() { reach = 1.6f; }
        public override string Prompt => broken ? "" : "E — выбить решётку";
        public override bool CanInteract(Transform who) => !broken;

        public override void Interact(Transform who)
        {
            if (broken) return;
            broken = true;
            Vector3 n = transform.forward;
            if (Vector3.Dot(who.position - transform.position, n) > 0) n = -n;   // падает от игрока
            axis = Vector3.Cross(Vector3.up, n).normalized;
            pivot = transform.position;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        void Update()
        {
            if (!broken || t >= 1f) return;
            float dt = Mathf.Min(Time.deltaTime * 2.2f, 1f - t);
            t += dt;
            transform.RotateAround(pivot, axis, 84f * dt);
        }
    }
}
