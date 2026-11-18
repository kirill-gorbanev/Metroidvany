using UnityEngine;

namespace Fortress.Rig
{
    /// <summary>Простая «воля» для стенда: идти по точкам или бродить в круге; possessed — ведёт игрок (WASD).</summary>
    [RequireComponent(typeof(LegGait))]
    public class RigWander : MonoBehaviour
    {
        public Vector3[] points;
        public Vector3 center;
        public float radius = 4f;
        public float speed = 0.7f;
        public float arrive = 0.35f;
        public float pause = 0.6f;
        public bool idle;
        [System.NonSerialized] public bool possessed;
        [System.NonSerialized] public Vector3 possessVelocity;

        LegGait gait;
        Rigidbody body;
        int index;
        float wait, stuck;
        Vector3 goal;

        void Start() { gait = GetComponent<LegGait>(); body = GetComponent<Rigidbody>(); Pick(); }

        void Pick()
        {
            if (points != null && points.Length > 0) { goal = points[index % points.Length]; index++; }
            else { var r = Random.insideUnitCircle * radius; goal = center + new Vector3(r.x, 0f, r.y); }
        }

        void FixedUpdate()
        {
            if (possessed) { gait.desiredVelocity = possessVelocity; gait.desiredFacing = possessVelocity; return; }
            if (idle) { gait.desiredVelocity = Vector3.zero; return; }
            Vector3 d = goal - transform.position; d.y = 0f;
            float dist = d.magnitude;
            if (dist < arrive)
            {
                gait.desiredVelocity = Vector3.zero;
                wait += Time.fixedDeltaTime;
                if (wait > pause) { wait = 0f; Pick(); }
                return;
            }
            gait.desiredVelocity = d / dist * speed * Mathf.Clamp01(dist / 0.7f + 0.25f);
            gait.desiredFacing = d;
            // упёрся (ящик, стена, другой табурет) — постоять и выбрать другую цель: так он обходит, а не давит вечно
            Vector3 v = body ? body.linearVelocity : Vector3.zero;
            stuck = new Vector3(v.x, 0f, v.z).magnitude < 0.12f ? stuck + Time.fixedDeltaTime : 0f;
            if (stuck > 1.5f)
            {
                stuck = 0f;
                if (points != null && points.Length > 0) goal = transform.position - d.normalized * 0.8f + Vector3.Cross(Vector3.up, d.normalized) * 0.8f;
                else Pick();
            }
        }
    }
}
