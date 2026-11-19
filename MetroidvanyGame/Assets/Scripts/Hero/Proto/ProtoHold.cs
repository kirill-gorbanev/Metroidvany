using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ЗАЦЕП — отдельный блок: перила, поручень, столб, ручка. Отрезок a→b (в своих координатах объекта).
    /// Кисть, наведённая рядом (по лучу курсора), прилипает к ближайшей точке отрезка. Держит на любом склоне.
    /// Под ProtoMover — едет вместе с ним (кисть тоже).</summary>
    public class ProtoHold : MonoBehaviour
    {
        [Tooltip("Концы зацепа в координатах объекта. Брус-куб: (0,0,−0.5)…(0,0,0.5); цилиндр: (0,−1,0)…(0,1,0).")]
        public Vector3 a = new Vector3(0f, 0f, -0.5f), b = new Vector3(0f, 0f, 0.5f);

        public Vector3 A => transform.TransformPoint(a);
        public Vector3 B => transform.TransformPoint(b);
        /// <summary>Точка зацепа по параметру s ∈ [0, 1].</summary>
        public Vector3 At(float s) => Vector3.Lerp(A, B, s);

        /// <summary>Ближайшая к лучу точка отрезка: параметр s и расстояние до луча.</summary>
        public float ClosestToRay(Ray r, out float s)
        {
            Vector3 p = A, d = B - A, q = r.origin, e = r.direction.normalized;
            float dd = Vector3.Dot(d, d), de = Vector3.Dot(d, e), ee = 1f;
            Vector3 w = p - q;
            float dw = Vector3.Dot(d, w), ew = Vector3.Dot(e, w);
            float den = dd * ee - de * de;
            s = den > 1e-8f ? Mathf.Clamp01((de * ew - ee * dw) / den) : 0f;
            Vector3 ps = p + d * s;
            float tr = Mathf.Max(0f, Vector3.Dot(ps - q, e));
            return (ps - (q + e * tr)).magnitude;
        }

        /// <summary>Ближайшая к точке точка отрезка (параметр).</summary>
        public float ClosestTo(Vector3 x)
        {
            Vector3 d = B - A; float dd = Vector3.Dot(d, d);
            return dd > 1e-8f ? Mathf.Clamp01(Vector3.Dot(x - A, d) / dd) : 0f;
        }

        void OnDrawGizmos() { Gizmos.color = new Color(1f, 0.85f, 0.2f); Gizmos.DrawLine(A, B); }
    }
}
