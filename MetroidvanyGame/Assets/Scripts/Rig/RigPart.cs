using UnityEngine;

namespace Fortress.Rig
{
    /// <summary>Часть рига: пересылает удары своему телу и помнит последнее касание «крутой» поверхности
    /// (вертикальная грань: подступёнок, бревно, стена) — так отличают зацеп носком от обычного шага по полу.</summary>
    public class RigPart : MonoBehaviour
    {
        RigBody body;
        /// <summary>Когда часть последний раз упиралась в крутую грань, и нормаль этой грани.</summary>
        public float SteepTime { get; private set; } = -99f;
        public Vector3 SteepNormal { get; private set; }
        public string SteepWhat { get; private set; }

        void Awake() => body = GetComponentInParent<RigBody>();
        void OnCollisionEnter(Collision c) { if (body && !body.Owns(c.collider)) { body.OnPartHit(this, c); Steep(c); } }
        void OnCollisionStay(Collision c)
        {
            if (!body || body.Owns(c.collider)) return;
            body.LastContactTime = Time.time;
            Steep(c);
        }

        void Steep(Collision c)
        {
            for (int i = 0; i < c.contactCount; i++)
            {
                var n = c.GetContact(i).normal;
                if (Mathf.Abs(n.y) < 0.6f) { SteepTime = Time.time; SteepNormal = n; SteepWhat = c.collider.name; return; }
            }
        }
    }
}
