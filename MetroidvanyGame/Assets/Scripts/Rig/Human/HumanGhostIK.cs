using UnityEngine;

namespace Fortress.Rig.Human
{
    /// <summary>Подстройка позы «призрака» под окружение (на объекте с аниматором призрака).
    /// Стопы: каждая встаёт на свою опору (ступень, порог, помост) — луч вниз под щиколоткой и носком;
    /// таз опускается к нижней опоре и сглаживается — на лестнице он идёт ровно, а не прыгает со ступенькой.
    /// Руки: цели ставит HumanInteract (взять ящик, держать ношу, упереться в дверь).
    /// Сесть: таз опускается на сиденье и сдвигается на него.
    /// Видимое физическое тело повторяет эту позу мышцами — так ноги реально встают на ступени.
    /// Висит на модели призрака (рядом с аниматором); корень призрака (куда он идёт и смотрит) — HumanDriver выше.</summary>
    [RequireComponent(typeof(Animator))]
    public class HumanGhostIK : MonoBehaviour
    {
        [Tooltip("Ставить стопы на опору под ними")] public bool feet = true;
        [Tooltip("Насколько выше/ниже пола под призраком может встать стопа (м)")] public float reach = 0.45f;
        [Tooltip("Сглаживание высоты таза (с)")] public float pelvisSmooth = 0.09f;
        [Tooltip("Запас над ступенькой, когда нога переносится на ступень выше (м)")] public float stepClear = 0.07f;
        [Tooltip("Высота кости таза сидя над корнем в анимации Sitting_* (м, замер клипа)")] public float sitPelvisHeight = 0.55f;
        [Tooltip("Сглаживание опоры под каждой стопой (с, в мире): кромка ступени не дёргает ногу")] public float footSmooth = 0.06f;

        [System.NonSerialized] public float feetWeight = 1f;
        // руки: мировые цели и вес (0 — анимация)
        [System.NonSerialized] public Vector3 handL, handR;
        [System.NonSerialized] public float handLW, handRW;
        [System.NonSerialized] public Vector3 elbowHintL, elbowHintR;
        [System.NonSerialized] public float hintW;
        // взгляд
        [System.NonSerialized] public Vector3 lookAt;
        [System.NonSerialized] public float lookW;
        // сесть: верх сиденья (мир), точка на сиденье, куда сесть, и насколько «сел» (0..1)
        [System.NonSerialized] public Vector3 seatPoint;
        [System.NonSerialized] public float seatW;
        /// <summary>Сдвиг тела вверх (м): сиденье выше/ниже того, под которое снят клип посадки (риг v3, HumanNPC).</summary>
        [System.NonSerialized] public float bodyLift;
        /// <summary>Наклон корпуса вперёд (градусы): тянется к дальнему (поставить на стол, взять с пола).</summary>
        [System.NonSerialized] public float lean;
        float leanSm;

        /// <summary>Свои цели стоп поверх анимации (риг v3: подшагивание при отбрасывании). Вес 0..1, позиции — щиколотки в мире.</summary>
        [System.NonSerialized] public float overrideW;
        [System.NonSerialized] public readonly Vector3[] overridePos = new Vector3[2];
        /// <summary>Стопы анимации на опоре (до своих целей) — прошлый кадр, мир.</summary>
        public readonly Vector3[] AnimFoot = new Vector3[2];
        /// <summary>Высота щиколотки над полом, когда стопа стоит (м).</summary>
        public float FootBottom => footBottom;

        Animator anim;
        IHumanMotion driver;
        Transform root;
        Transform hips;
        float gap = float.NaN;               // высота корня призрака над полом, когда стоит на ровном
        float groundSm, groundVel;
        bool smInit;
        readonly float[] footG = new float[2], footGVel = new float[2];   // опора под стопой, сглаженная в мире
        bool footInit;
        float hipsOverBody;                  // кость таза относительно «тела» аниматора (по высоте) — для посадки
        Vector3 lastBody;
        float footBottom = 0.08f;
        public float GroundUnderFeet { get; private set; }
        /// <summary>Нога в переносе (по анимации: стопа оторвана от опоры) — 0 левая, 1 правая.</summary>
        public readonly bool[] Swing = new bool[2];

        void Awake()
        {
            anim = GetComponent<Animator>();
            driver = GetComponentInParent<IHumanMotion>();
            root = driver != null ? driver.Root : transform;
            // таз — по имени (как его тянет физическое тело); «Hips» аватара здесь может указывать на корень скелета
            hips = FindBone(transform, "pelvis") ?? (anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null);
            if (anim.isHuman) footBottom = anim.leftFeetBottomHeight * anim.humanScale;
        }

        static Transform FindBone(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = FindBone(c, n); if (r) return r; }
            return null;
        }

        static float Probe(Vector3 p, float from, float depth, float fallback)
        {
            return Physics.Raycast(new Vector3(p.x, from, p.z), Vector3.down, out var h, depth, RigLayers.WorldMask, QueryTriggerInteraction.Ignore)
                ? h.point.y : fallback;
        }

        void OnAnimatorIK(int layer)
        {
            if (!anim.isHuman) return;
            if (layer == 0)
            {
                if (Mathf.Abs(bodyLift) > 1e-4f) anim.bodyPosition += Vector3.up * bodyLift;
                Feet();
                Seat();
                leanSm = Mathf.MoveTowards(leanSm, lean, 60f * Time.deltaTime);
                if (Mathf.Abs(leanSm) > 0.1f)
                {
                    Vector3 r = root.right; r.y = 0f;
                    anim.bodyRotation = Quaternion.AngleAxis(leanSm, r.normalized) * anim.bodyRotation;
                }
            }
            Hands();
            if (layer == 0 && lookW > 0.01f)
            {
                anim.SetLookAtWeight(lookW, 0.15f, 0.7f, 1f, 0.6f);
                anim.SetLookAtPosition(lookAt);
            }
        }

        void Feet()
        {
            Vector3 rp = root.position;
            float dt = Time.deltaTime;
            // ровный пол: замерить, на сколько корень призрака выше пола (кожа CharacterController)
            float gRoot = Probe(rp, rp.y + 0.6f, 1.6f, float.NaN);
            if (float.IsNaN(gap) && !float.IsNaN(gRoot)) gap = rp.y - gRoot;
            if (float.IsNaN(gap)) return;
            float baseG = rp.y - gap;
            Vector3 fwd = root.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 vel = driver != null ? driver.Velocity : Vector3.zero;
            float w = feet ? feetWeight : 0f;

            var goals = new[] { AvatarIKGoal.LeftFoot, AvatarIKGoal.RightFoot };
            var pos = new Vector3[2];
            var off = new float[2];
            float top = baseG + reach + 0.35f, depth = 2f * reach + 0.7f;
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = anim.GetIKPosition(goals[i]);
                float h = p.y - baseG;
                Vector3 ff = fwd;     // носок — по направлению тела (оси костей стоп у левой и правой разные)
                // опора под щиколоткой и под носком — стопа встаёт на выступающую
                float g = Mathf.Max(Probe(p, top, depth, baseG), Probe(p + ff * 0.13f, top, depth, baseG));
                // нога в воздухе (шаг): смотреть, куда она опустится, — заранее поднять над ступенькой
                bool swing = h > footBottom + 0.035f;
                Swing[i] = swing;
                float lift = 0f;
                if (swing && vel.sqrMagnitude > 0.04f)
                {
                    float ga = Probe(p + ff * 0.13f + vel * 0.14f, top, depth, baseG);
                    // впереди ступень выше — пронести носок над кромкой с запасом
                    if (ga > g + 0.05f) lift = stepClear * Mathf.Clamp01((h - footBottom) / 0.12f);
                    g = Mathf.Max(g, ga);
                }
                // опора в мире сглажена: проба перешла кромку ступени — стопа переезжает плавно, а не скачком;
                // прыжок корня капсулы на ступень сюда не попадает (высота в мире)
                if (!footInit || w <= 0.001f || Mathf.Abs(g - footG[i]) > 0.8f) { footG[i] = g; footGVel[i] = 0f; }
                else footG[i] = Mathf.SmoothDamp(footG[i], g, ref footGVel[i], footSmooth, Mathf.Infinity, dt);
                g = footG[i];
                off[i] = Mathf.Clamp(g - baseG, -reach, reach);
                pos[i] = p + Vector3.up * (off[i] + lift);
                AnimFoot[i] = pos[i];
                if (overrideW > 0.001f)
                {
                    // свои цели (шаг ловит равновесие): таз — к нижней из них
                    pos[i] = Vector3.Lerp(pos[i], overridePos[i], overrideW);
                    off[i] = Mathf.Lerp(off[i], Mathf.Clamp(overridePos[i].y - footBottom - baseG, -reach, reach), overrideW);
                }
            }
            footInit = true;
            // таз — к нижней опоре (верхняя нога согнётся), плавно
            float pelvisG = baseG + Mathf.Min(off[0], off[1]);
            if (!smInit || w <= 0.001f) { groundSm = pelvisG; groundVel = 0f; smInit = true; }
            groundSm = Mathf.SmoothDamp(groundSm, pelvisG, ref groundVel, pelvisSmooth, Mathf.Infinity, dt);
            GroundUnderFeet = groundSm;
            if (w > 0.001f)
            {
                anim.bodyPosition += Vector3.up * ((groundSm - baseG) * w);
                for (int i = 0; i < 2; i++)
                {
                    anim.SetIKPositionWeight(goals[i], w);
                    anim.SetIKPosition(goals[i], pos[i]);
                }
            }
            else
                for (int i = 0; i < 2; i++) anim.SetIKPositionWeight(goals[i], 0f);
        }

        void Seat()
        {
            // кость таза относительно «тела» аниматора: замер по прошлому кадру (кости пишутся после IK)
            if (hips && lastBody != Vector3.zero) hipsOverBody = Mathf.Lerp(hipsOverBody, hips.position.y - lastBody.y, 0.2f);
            if (seatW > 0.001f)
            {
                Vector3 body = anim.bodyPosition;
                // таз — на высоту сиденья (кость таза на 7 см выше верха); по горизонтали ставит корень (HumanInteract).
                // Без обратной связи: высота таза сидя в анимации — замер клипа (решатель ног двигает тело после нас)
                float dy = Mathf.Clamp(seatPoint.y + 0.07f - (root.position.y + sitPelvisHeight), -0.25f, 0.25f);
                anim.bodyPosition = body + Vector3.up * (dy * seatW);
            }
            lastBody = anim.bodyPosition;
        }

        void Hands()
        {
            anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, handLW);
            anim.SetIKPositionWeight(AvatarIKGoal.RightHand, handRW);
            if (handLW > 0.001f) anim.SetIKPosition(AvatarIKGoal.LeftHand, handL);
            if (handRW > 0.001f) anim.SetIKPosition(AvatarIKGoal.RightHand, handR);
            anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, hintW * handLW);
            anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, hintW * handRW);
            if (hintW > 0.001f)
            {
                anim.SetIKHintPosition(AvatarIKHint.LeftElbow, elbowHintL);
                anim.SetIKHintPosition(AvatarIKHint.RightElbow, elbowHintR);
            }
        }
    }
}
