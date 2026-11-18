using UnityEngine;

namespace Fortress.Grid
{
    /// <summary>Решётка ворот: E — поднять / опустить.</summary>
    public class Portcullis : Interactable
    {
        public float lift = 2.6f;
        public float speed = 1.4f;
        [Tooltip("Решётка поднята с начала (ворота открыты: днём идут подводы)")] public bool startRaised;
        Vector3 down; float t, target;

        void Awake()
        {
            down = transform.localPosition; reach = 2.6f;
            if (startRaised) { t = target = 1f; transform.localPosition = down + Vector3.up * lift; }
        }
        public override string Prompt => target > 0.5f ? "E — опустить решётку" : "E — поднять решётку";
        public override void Interact(Transform who) { target = target > 0.5f ? 0f : 1f; }

        void Update()
        {
            t = Mathf.MoveTowards(t, target, speed / lift * Time.deltaTime);
            transform.localPosition = down + Vector3.up * (lift * Mathf.SmoothStep(0, 1, t));
        }
    }
}
