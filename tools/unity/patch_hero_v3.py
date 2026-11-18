"""Патч HeroController v3: автонаведение, перехват, разворот вокруг руки, предмет в руке, рука под себя, ведро на голове."""
p = "D:/Metroidvany/MetroidvanyGame/Assets/Scripts/Hero/HeroController.cs"
s = open(p, encoding="utf-8").read()


def rep(a, b, count=1):
    global s
    assert a in s, "NOT FOUND: " + a[:80]
    s = s.replace(a, b, count)


start = s.index("            else if (mode == Mode.Free || mode == Mode.Carry)\n            {\n                Vector3 want;")
end = s.index("            // ---- поворот (только по клику по земле")
new = open("D:/Metroidvany/tools/unity/patch_hero_v3_block.cs.txt", encoding="utf-8").read()
s = s[:start] + new + s[end:]

rep("        float? turnTo;", """        float? turnTo;
        Vector3? reachTo; Vector3 reachToN = Vector3.up; float reachUntil, bentT;
        Rigidbody heldRb;""")
rep('        [Header("Подтягивание (зеркально мыши)")]', '''        [Header("Автонаведение и перехват")]
        [Tooltip("Радиус, в котором рука сама наводится на выступ/поверхность у курсора (м).")]
        public float assistRadius = 0.30f;
        [Tooltip("Перехват: держишь ЛКМ и тянешь дальше, когда рука согнута до предела, — рука сама переставляется вперёд.")]
        public bool autoReach = true;
        [Tooltip("Градусов разворота на метр бокового жеста (вокруг руки).")]
        public float turnPerMeter = 260f;
        [Tooltip("Скорость отталкивания назад относительно тяги вперёд.")]
        public float backFrac = 0.8f;

        [Header("Подтягивание (зеркально мыши)")]''')
rep("public float clickMaxTime = 0.25f, clickMaxPixels = 10f;", "public float clickMaxTime = 0.35f, clickMaxPixels = 22f;")
rep('''            mode = Mode.Grab;
            Log($"хват {(col ? col.name : "поверхность")} в {point}");
            anchorRb = rb;''', '''            mode = Mode.Grab;
            Log($"хват {(col ? col.name : "поверхность")} в {point}");
            anchorRb = rb;
            if (rb && !rb.isKinematic && rb != hooked && rb.mass < bodyMass * 4f)
            {
                heldRb = rb; rb.isKinematic = true; IgnoreBody(rb, true);       // держим рукой: едет вместе с рукой
            }''')
rep("        void EndGrip() { mode = Mode.Free; anchorRb = null; anchorT = null; }", '''        void EndGrip() { ReleaseHeld(); mode = Mode.Free; anchorRb = null; anchorT = null; }

        void ReleaseHeld()
        {
            if (!heldRb) return;
            heldRb.isKinematic = false; heldRb.linearVelocity = Vector3.zero;
            IgnoreBody(heldRb, false);
            heldRb = null;
        }

        /// <summary>Следующая точка хвата по направлению жеста: на досягаемости, выступы важнее пола.</summary>
        bool NextReach(Vector3 S, Vector3 dir, float reachMax, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = Vector3.up;
            Vector3 P = S + dir * reachMax * 0.85f;
            P = ClipByPlanes(S, P);
            Vector3 probe = P;
            if (Physics.Raycast(P + Vector3.up * 0.4f, Vector3.down, out var h, 1.5f, ~0, QueryTriggerInteraction.Ignore) && !Mine(h.transform))
                probe = h.point;
            return FindGrip(probe, assistRadius, out point, out normal, out _) && Vector3.Distance(S, point) <= reachMax;
        }''')
rep('''                Vector3 q = c.ClosestPoint(p);
                float d = Vector3.Distance(p, q);
                if (d < bd)''', '''                Vector3 q = c.ClosestPoint(p);
                float d = Vector3.Distance(p, q);
                // выступ (брус, перила, край, лёгкий предмет) важнее плоскости рядом
                var bs = c.bounds.size;
                bool protr = Mathf.Min(bs.x, Mathf.Min(bs.y, bs.z)) < 0.15f || bs.x * bs.y * bs.z < 0.05f;
                float dq = d;
                d -= protr ? 0.25f : 0f;
                if (d < bd)''')
rep('''                    bd = d; point = q; col = c;
                    normal = d > 1e-4f ? (p - q) / d : Vector3.up;''', '''                    bd = d; point = q; col = c;
                    normal = dq > 1e-4f ? (p - q) / dq : Vector3.up;''')
rep("            if (flat.sqrMagnitude < 1e-4f) return want;",
    "            if (flat.sqrMagnitude < 0.25f * 0.25f) return want;            // у самого тела (под себя) — без ограничения")
# ведро на голове: дном на макушку, держится прямо, чуть наклоняется с головой
rep('''            var hb = body.HeadBone;
            onHead.isKinematic = true;
            var top = body.HeadTop;
            headOffset = hb.InverseTransformPoint(top + Vector3.up * 0.02f);
            headRot = Quaternion.Inverse(hb.rotation) * Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up), Vector3.up);''', '''            onHead.isKinematic = true;
            // дно ноши — на макушку: смещение от опорной точки предмета до низа его коллайдеров
            onHead.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up), Vector3.up);
            Physics.SyncTransforms();
            float bottom = float.MaxValue;
            foreach (var c in onHead.GetComponentsInChildren<Collider>()) bottom = Mathf.Min(bottom, c.bounds.min.y);
            headLift = onHead.position.y - bottom;''')
rep("        Rigidbody onHead; Vector3 headOffset; Quaternion headRot;", "        Rigidbody onHead; float headLift;")
rep('''                onHead.MovePosition(body.HeadBone.TransformPoint(headOffset));
                onHead.MoveRotation(body.HeadBone.rotation * headRot);''', '''                // держится прямо, лишь немного клонится вслед за головой
                Vector3 hup = Vector3.Slerp(Vector3.up, body.HeadBone.position - body.ChestPosition, 0.25f).normalized;
                var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, hup), hup);
                onHead.MovePosition(body.HeadTop + hup * (headLift + 0.01f));
                onHead.MoveRotation(rot);''')
open(p, "w", encoding="utf-8").write(s)
print("patched")
