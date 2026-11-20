using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Тело героя (без ног, без левой руки) — поза целиком процедурная, без клипов.
    /// Каждый кадр: поза покоя из бинда → посадка/положение ничком (prone 0..1) → наклон и поворот корпуса к руке →
    /// IK правой руки (две кости, аналитически) → ладонь на поверхности → голова и глаз на цель → лицо (шейп-кеи).
    /// Работает в мировых направлениях (вперёд/вверх/вправо — у root), к осям костей не привязан.
    /// Вход ставит прототип (ProtoHeroModel).</summary>
    [DefaultExecutionOrder(200)]
    public class HeroBody : MonoBehaviour
    {
        [Header("Вход (ставит ProtoHeroModel)")]
        [Range(0f, 1f)] public float prone;
        public Vector3 handTarget;
        [Range(0f, 1f)] public float handWeight = 1f;
        public bool handPlanted;
        public Vector3 handNormal = Vector3.up;
        public Vector3 lookTarget;
        [Range(0f, 1f)] public float effort;
        [Tooltip("Рука держится за опору (хват): наклон не выпрямляет руку — тянет сгиб локтя.")]
        public bool gripping;
        [Tooltip("Желаемая длина руки (доля полной): 0.97 — тянется прямой, 0.55 — сильно согнута. Ставит HeroFeed.")]
        [Range(0.4f, 1f)] public float elbowTarget = 0.97f;
        [Tooltip("Рамка тела: вперёд/вверх/вправо. Пусто — родитель.")]
        public Transform root;

        [Header("Поза")]
        public float sitPelvisHeight = 0.13f;
        public float pronePelvisHeight = 0.11f;
        [Tooltip("Ничком таз уходит назад от точки опоры (м).")]
        public float proneShift = -0.30f;
        [Tooltip("Сутулость сидя, градусы на весь позвоночник.")]
        public float hunch = 20f;
        public float maxReachLean = 70f, maxTwist = 35f;
        [Tooltip("Голова опущена, смотрит исподлобья: доля поворота головы к цели (остальное — глаз).")]
        [Range(0f, 1f)] public float headLookWeight = 0.55f;
        public float lookLimit = 75f, eyeLimit = 28f;

        [Header("Лицо")]
        public float blinkMin = 1.8f, blinkMax = 5.5f;

        Transform pelvis, sp1, sp2, sp3, neck, head, clavR, clavL, upR, loR, handR, upL, loL, thighL, thighR, calfL, calfR, eyeR, midR, thumbR;
        Transform[] all;
        Quaternion[] bindRot;
        Vector3[] bindPos;
        float lenUp, lenLo;
        Vector3 headFaceL, eyeFaceL;
        SkinnedMeshRenderer[] smrs;
        float breathPhase, blinkT, blinkNext = 2f, effortS;
        float leanAng;               // наклон к руке (подстраивается, пока рука не достанет)
        Vector3 poleS;
        Vector3 leanDir;

        public float ArmLength => lenUp + lenLo;
        public Vector3 Shoulder => upR ? upR.position : transform.position;
        public Vector3 HandPosition => handR ? handR.position : transform.position;
        public Vector3 ChestPosition => sp3 ? sp3.position : transform.position;
        public Vector3 HeadPosition => head ? head.position : transform.position;
        public bool Ready => all != null && pelvis && upR && loR && handR;
        public Transform HeadBone => head;
        /// <summary>Макушка (для ноши на голове).</summary>
        public Vector3 HeadTop => head ? head.position + root.up * 0.20f : transform.position;
        /// <summary>Культя левого плеча (зацеп плечом+головой).</summary>
        public Vector3 StumpShoulder => upL ? upL.position : transform.position;
        /// <summary>Сгиб локтя 0 (прямая) … 1 (согнута до предела).</summary>
        public float ElbowBend => (loR && handR && upR) ? 1f - Mathf.Clamp01((Vector3.Distance(upR.position, handR.position) - 0.12f) / (ArmLength - 0.12f)) : 0f;

        Transform Find(string n)
        {
            foreach (var t in all) if (t.name == n) return t;
            return null;
        }

        void Awake()
        {
            if (!root) root = transform.parent ? transform.parent : transform;
            all = GetComponentsInChildren<Transform>(true);
            bindRot = new Quaternion[all.Length];
            bindPos = new Vector3[all.Length];
            for (int i = 0; i < all.Length; i++) { bindRot[i] = all[i].localRotation; bindPos[i] = all[i].localPosition; }
            pelvis = Find("pelvis"); sp1 = Find("spine_01"); sp2 = Find("spine_02"); sp3 = Find("spine_03");
            neck = Find("neck_01"); head = Find("Head");
            clavR = Find("clavicle_r"); clavL = Find("clavicle_l");
            upR = Find("upperarm_r"); loR = Find("lowerarm_r"); handR = Find("hand_r");
            upL = Find("upperarm_l"); loL = Find("lowerarm_l");
            thighL = Find("thigh_l"); thighR = Find("thigh_r"); calfL = Find("calf_l"); calfR = Find("calf_r");
            eyeR = Find("eye_r"); midR = Find("middle_01_r"); thumbR = Find("thumb_01_r");
            if (!Ready) { Debug.LogError("[Hero] не найдены кости героя", this); enabled = false; return; }
            lenUp = Vector3.Distance(upR.position, loR.position);
            lenLo = Vector3.Distance(loR.position, handR.position);
            if (head) headFaceL = head.InverseTransformDirection(root.forward);
            if (eyeR) eyeFaceL = eyeR.InverseTransformDirection(root.forward);
            smrs = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            handTarget = Shoulder + root.forward * 0.25f - root.up * 0.4f;
            lookTarget = HeadPosition + root.forward * 2f;
            blinkNext = Random.Range(blinkMin, blinkMax);
        }

        /// <summary>Насколько и куда вытолкнуть тело (голова/грудь вошли в предмет). </summary>
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
        {
            foreach (var c in Physics.OverlapSphere(p, r, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.transform.IsChildOf(root)) continue;
                var rb = c.attachedRigidbody;
                if (rb && rb.isKinematic) continue;                     // ноша / на голове / зацеп
                if (!(c is MeshCollider mc && !mc.convex) && c.ClosestPoint(p).y < root.position.y + 0.06f) continue;   // пол — не преграда
                return true;
            }
            return false;
        }

        static void Rot(Transform t, Quaternion q) { if (t) t.rotation = q * t.rotation; }

        /// <summary>Повернуть кость так, чтобы направление from (мировое, от кости) смотрело в to.</summary>
        static void Aim(Transform t, Vector3 from, Vector3 to, float w = 1f)
        {
            if (!t || from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f || w <= 0f) return;
            Rot(t, Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(from, to), w));
        }

        void LateUpdate()
        {
            if (!Ready) return;
            for (int i = 0; i < all.Length; i++) { all[i].localRotation = bindRot[i]; all[i].localPosition = bindPos[i]; }
            float dt = Time.deltaTime;
            Vector3 U = root.up, F = root.forward, R = root.right;
            float sit = 1f - prone;

            // 1. корпус: ничком — таз поворачивается «верх → вперёд» (живот к полу), сидя — лёгкий завал назад
            Rot(pelvis, Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(U, F), prone * 0.94f));
            Rot(pelvis, Quaternion.AngleAxis(-8f * sit, R));
            pelvis.position = root.position + U * Mathf.Lerp(sitPelvisHeight, pronePelvisHeight, prone) + F * (proneShift * prone);
            // наклон к руке: сидя сгибается в тазобедренных (60%), остальное — позвоночник
            Vector3 toT = Vector3.ProjectOnPlane(handTarget - root.position, U);
            if (toT.sqrMagnitude > 1e-4f) leanDir = toT.normalized;
            Vector3 leanAxis = leanDir.sqrMagnitude > 0.5f ? Vector3.Cross(U, leanDir) : R;
            Rot(pelvis, Quaternion.AngleAxis(leanAng * 0.6f, leanAxis));

            // культи бёдер: сидя — вперёд, ничком — вдоль тела
            foreach (var (thigh, calf, s) in new[] { (thighL, calfL, -1f), (thighR, calfR, 1f) })
            {
                if (!thigh || !calf) continue;
                Vector3 cur = (calf.position - thigh.position).normalized;
                Vector3 sitDir = (F + R * (0.18f * s) - U * 0.12f).normalized;
                Aim(thigh, cur, Vector3.Slerp(cur, sitDir, sit));
            }

            // позвоночник: сидя — сутулится, ничком — грудь приподнята
            float bend = hunch * sit - 9f * prone;
            foreach (var sp in new[] { sp1, sp2, sp3 }) Rot(sp, Quaternion.AngleAxis(bend / 3f, R));

            // 2. к руке: поворот корпуса и наклон, когда цель дальше удобного
            Vector3 torsoUp = Vector3.Slerp(U, F, prone);
            Vector3 flat = Vector3.ProjectOnPlane(handTarget - sp3.position, U);
            if (flat.sqrMagnitude > 1e-4f)
            {
                float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(F, U), flat, U);
                yaw = Mathf.Clamp(yaw * 0.55f, -maxTwist, maxTwist) * handWeight * (1f - 0.6f * prone);
                foreach (var sp in new[] { sp1, sp2, sp3 }) Rot(sp, Quaternion.AngleAxis(yaw / 3f, torsoUp));
                foreach (var sp in new[] { sp1, sp2, sp3 }) Rot(sp, Quaternion.AngleAxis(leanAng * 0.4f / 3f, leanAxis));
            }

            // плечи подняты (сжался), левая культя висит/прижата
            if (clavR && upR) { Vector3 d = upR.position - clavR.position; Aim(clavR, d, d.normalized + U * 0.22f + F * 0.18f); }
            if (clavL && upL) { Vector3 d = upL.position - clavL.position; Aim(clavL, d, d.normalized + U * 0.22f + F * 0.18f); }
            if (upL && loL)
            {
                Vector3 axisT = (sp3.position - pelvis.position).normalized;
                Vector3 sitD = -U * 0.9f - R * 0.35f + F * 0.15f;
                Vector3 proD = -R * 0.7f - axisT * 0.35f - U * 0.25f;
                Aim(upL, loL.position - upL.position, Vector3.Slerp(sitD.normalized, proD.normalized, prone));
            }

            // голова: опущена, ничком — поднята, чтобы смотреть вперёд
            Rot(neck, Quaternion.AngleAxis(12f * sit - 48f * prone, R));
            Rot(head, Quaternion.AngleAxis(4f * sit - 34f * prone, R));

            // 3. IK правой руки
            Vector3 S = upR.position, T = handTarget;
            Vector3 dv = T - S;
            float a = lenUp, b = lenLo;
            float L = Mathf.Clamp(dv.magnitude, Mathf.Abs(a - b) + 0.02f, (a + b) * 0.999f);
            Vector3 dn = dv.sqrMagnitude > 1e-6f ? dv.normalized : -U;
            // полюс локтя: наружу-вниз; цель сзади/под телом — локоть вверх-наружу (иначе сустав выворачивается)
            Vector3 poleW = Vector3.Slerp((R * 0.6f - U * 0.5f - F * 0.2f).normalized, (R * 0.7f + U * 0.5f).normalized, prone);
            float behind = Mathf.Clamp01((0.25f - Vector3.Dot(dn, F)) / 0.5f);
            poleW = Vector3.Slerp(poleW, (R * 0.5f + U * 0.8f).normalized, behind);
            poleS = poleS.sqrMagnitude < 0.5f ? poleW : Vector3.Slerp(poleS, poleW, 1f - Mathf.Exp(-10f * dt)).normalized;
            Vector3 pole = poleS;
            Vector3 pn = Vector3.ProjectOnPlane(pole, dn);
            pn = pn.sqrMagnitude > 1e-6f ? pn.normalized : Vector3.Cross(dn, F).normalized;
            float cosA = Mathf.Clamp((a * a + L * L - b * b) / (2f * a * L), -1f, 1f);
            float A = Mathf.Acos(cosA);
            Vector3 E = S + (dn * Mathf.Cos(A) + pn * Mathf.Sin(A)) * a;
            Aim(upR, loR.position - upR.position, E - S, handWeight);
            // наклон: не достаёт — сильнее; выпрямляется, только когда рука сильно согнута (мёртвая зона —
            // иначе наклон всё время держал руку прямой и локоть не работал). При хвате наклон не растёт сам —
            // тянет сгиб локтя, а наклоняется корпус, лишь когда рука уже прямая и не достаёт.
            float dist = dv.magnitude, full = a + b;
            float grow = dist - full * Mathf.Min(0.97f, elbowTarget + 0.03f), shrink = dist - full * Mathf.Max(0.45f, elbowTarget - 0.12f);
            float dLean = grow > 0f ? grow : (shrink < 0f ? shrink : 0f);
            leanAng = Mathf.Clamp(leanAng + dLean * 420f * dt * handWeight, 0f, maxReachLean * (1f - 0.75f * prone));
            // голова/грудь упёрлись в предмет — наклон назад и выталкивание (тело не входит внутрь)
            Vector3 pen = Pen(head ? head.position + root.up * 0.08f : sp3.position, 0.12f) + Pen(sp3.position + F * 0.05f, 0.15f);
            Penetration = pen;
            if (pen.sqrMagnitude > 1e-6f) leanAng = Mathf.Max(0f, leanAng - 260f * dt);
            Aim(loR, handR.position - loR.position, (S + dn * L) - loR.position, handWeight);

            // ладонь: на поверхности — пальцы вдоль тяги, большой палец к телу
            if (midR && thumbR)
            {
                Vector3 fing = midR.position - handR.position;
                if (handPlanted)
                {
                    Vector3 n = handNormal.sqrMagnitude > 1e-4f ? handNormal.normalized : U;
                    Vector3 along = Vector3.ProjectOnPlane(T - S, n);
                    if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(F, n);
                    Aim(handR, fing, along.normalized, handWeight);
                    fing = (midR.position - handR.position).normalized;
                    Vector3 th = Vector3.ProjectOnPlane(thumbR.position - handR.position, fing);
                    Vector3 thWant = Vector3.ProjectOnPlane(-Vector3.Cross(n, fing), fing);
                    if (th.sqrMagnitude > 1e-6f && thWant.sqrMagnitude > 1e-6f)
                        Rot(handR, Quaternion.AngleAxis(Vector3.SignedAngle(th, thWant, fing) * handWeight, fing));
                }
                else
                {
                    Vector3 fore = handR.position - loR.position;           // кисть расслаблена, чуть свисает
                    Aim(handR, fing, fore.normalized - U * 0.35f, 0.6f * handWeight);
                }
            }

            // 4. взгляд: голова частично (исподлобья), глаз — остальное
            if (head)
            {
                Vector3 want = lookTarget - head.position;
                if (want.sqrMagnitude > 1e-4f)
                {
                    want.Normalize();
                    Vector3 face = head.TransformDirection(headFaceL);
                    Vector3 lim = Vector3.RotateTowards(face, want, lookLimit * Mathf.Deg2Rad, 0f);
                    Aim(neck, face, lim, 0.35f * headLookWeight);
                    face = head.TransformDirection(headFaceL);
                    Aim(head, face, lim, 0.65f * headLookWeight);
                    if (eyeR)
                    {
                        Vector3 ef = eyeR.TransformDirection(eyeFaceL);
                        Vector3 ew = lookTarget - eyeR.position;
                        Aim(eyeR, ef, Vector3.RotateTowards(ef, ew.normalized, eyeLimit * Mathf.Deg2Rad, 0f));
                    }
                }
            }

            Face(dt);
        }

        void Face(float dt)
        {
            effortS = Mathf.MoveTowards(effortS, effort, dt * (effort > effortS ? 3f : 0.6f));
            breathPhase += dt * Mathf.PI * 2f * (0.22f + effortS * 0.4f);
            float breath = (0.5f + 0.5f * Mathf.Sin(breathPhase)) * (35f + 65f * effortS);
            blinkT += dt;
            float blink = 0f;
            if (blinkT > blinkNext)
            {
                float k = (blinkT - blinkNext) / 0.16f;
                blink = k < 1f ? Mathf.Sin(k * Mathf.PI) * 100f : 0f;
                if (k >= 1f) { blinkT = 0f; blinkNext = Random.Range(blinkMin, blinkMax); }
            }
            Set("Breath_In", breath);
            Set("Blink_R", Mathf.Max(blink, effortS * 25f));
            Set("Brow_Sad", 45f * (1f - effortS));
            Set("Brow_Angry", 40f * effortS);
            Set("Wince", 75f * effortS);
            Set("Mouth_Frown", 30f + 30f * effortS);
        }

        void Set(string shape, float w)
        {
            foreach (var r in smrs)
            {
                if (!r || !r.sharedMesh) continue;
                int i = r.sharedMesh.GetBlendShapeIndex(shape);
                if (i >= 0) r.SetBlendShapeWeight(i, w);
            }
        }
    }
}
