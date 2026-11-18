p = "D:/Metroidvany/MetroidvanyGame/Assets/Scripts/Hero/HeroBody.cs"
s = open(p, encoding="utf-8").read()


def rep(a, b):
    global s
    assert a in s, "NOT FOUND: " + a[:80]
    s = s.replace(a, b, 1)


rep('''            // голова/грудь упёрлись в предмет — наклон назад (тело не входит внутрь)
            if (Blocked(head ? head.position + root.up * 0.08f : sp3.position, 0.12f) || Blocked(sp3.position + F * 0.06f, 0.14f))
                leanAng = Mathf.Max(0f, leanAng - 260f * dt);''', '''            // голова/грудь упёрлись в предмет — наклон назад и выталкивание (тело не входит внутрь)
            Vector3 pen = Pen(head ? head.position + root.up * 0.08f : sp3.position, 0.12f) + Pen(sp3.position + F * 0.05f, 0.15f);
            Penetration = pen;
            if (pen.sqrMagnitude > 1e-6f) leanAng = Mathf.Max(0f, leanAng - 260f * dt);''')
rep('''        bool Blocked(Vector3 p, float r)
        {''', '''        /// <summary>Насколько и куда вытолкнуть тело (голова/грудь вошли в предмет). Читает HeroController.</summary>
        public Vector3 Penetration { get; private set; }

        Vector3 Pen(Vector3 p, float r)
        {
            Vector3 sum = Vector3.zero;
            foreach (var c in Physics.OverlapSphere(p, r, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.transform.IsChildOf(root)) continue;
                var rb = c.attachedRigidbody;
                if (rb && rb.isKinematic) continue;
                if (c is MeshCollider mc && !mc.convex) continue;
                Vector3 q = c.ClosestPoint(p);
                if (q.y < root.position.y + 0.06f) continue;                // пол — не преграда
                Vector3 d = p - q;
                float m = d.magnitude;
                if (m < 1e-4f)
                {
                    // центр внутри предмета — наружу от его середины
                    Vector3 o = Vector3.ProjectOnPlane(p - c.bounds.center, Vector3.up);
                    sum += (o.sqrMagnitude > 1e-6f ? o.normalized : -root.forward) * r;
                }
                else sum += d / m * (r - m);
            }
            return sum;
        }

        bool Blocked(Vector3 p, float r)
        {''')
open(p, "w", encoding="utf-8").write(s)
print("patched body")
