using UnityEngine;

namespace Fortress.Rig
{
    /// <summary>Место, где можно сесть (лавка, сундук, низкая стена). Линия сиденья — от A до B по верху, в осях объекта;
    /// сесть можно с любой стороны (bothSides) или только лицом по forward.</summary>
    public class RigSeat : MonoBehaviour
    {
        public Vector3 localA = new Vector3(-1.1f, 0.5f, 0f), localB = new Vector3(1.1f, 0.5f, 0f);
        public Vector3 localForward = Vector3.forward;
        public bool bothSides = true;

        /// <summary>Ближайшая к from точка сиденья и куда смотреть сидя.</summary>
        public void Nearest(Vector3 from, out Vector3 point, out Vector3 forward)
        {
            Vector3 a = transform.TransformPoint(localA), b = transform.TransformPoint(localB);
            Vector3 ab = b - a;
            float k = Mathf.Clamp01(Vector3.Dot(from - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            point = a + ab * k;
            forward = transform.TransformDirection(localForward);
            forward.y = 0f; forward.Normalize();
            if (bothSides && Vector3.Dot(from - point, forward) < 0f) forward = -forward;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.TransformPoint(localA), transform.TransformPoint(localB));
        }
    }
}
