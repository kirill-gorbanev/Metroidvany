using UnityEngine;

namespace Fortress.Walker
{
    /// <summary>
    /// Бродилка от третьего лица на CharacterController.
    /// WASD — ходьба (от камеры), Shift — бег, Ctrl/C — присед, Пробел — прыжок, E — действие,
    /// R — на старт, F — полёт сквозь стены (осмотр), стремянки и вода — по зонам.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerWalker : MonoBehaviour
    {
        public Transform cameraTransform;
        public Transform body;
        public float walkSpeed = 3.4f;
        public float runSpeed = 6.5f;
        public float crouchSpeed = 1.5f;
        public float crouchHeight = 1.1f;
        public float jumpHeight = 1.1f;
        public float gravity = -22f;
        public float flySpeed = 12f;
        [Tooltip("Радиус капсулы на стремянке: проходит в люк 1×2 м")]
        public float ladderRadius = 0.28f;
        [Tooltip("Центр капсулы от линии стремянки на стремянке, м")]
        public float ladderOffset = 0.30f;
        [Tooltip("Ступни ниже поверхности воды при плавании, м (голова и плечи над водой)")]
        public float swimDepth = 1.1f;
        public Vector3 spawn;
        [Tooltip("1 камера, 2 зал, 3 двор, 4 стена, 5 крыша, 6 каменоломня")]
        public Vector3[] points;

        public string Prompt { get; private set; } = "";
        public string Mode { get; private set; } = "";
        public bool Flying { get; private set; }
        public bool Crouched { get; private set; }
        /// <summary>Высота поверхности воды, в которой сейчас игрок (для камеры), иначе NaN.</summary>
        public float WaterSurface { get; private set; } = float.NaN;

        CharacterController cc;
        Vector3 velocity;
        float baseRadius;
        LadderZone climbing;       // стремянка, за которую держимся
        bool dismount;             // наверху: шагаем на площадку
        readonly Collider[] buf = new Collider[16];

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            baseRadius = cc.radius;
            if (spawn == Vector3.zero) spawn = transform.position;
        }

        /// <summary>Присед: капсула 1.1 м — низкие укрытия (бочки, ящики, парапет) прячут; встать — если над головой свободно.</summary>
        void SetCrouch(bool want)
        {
            if (want == Crouched) return;
            if (!want)
            {
                Vector3 a = transform.position + Vector3.up * (crouchHeight - 0.05f);
                Vector3 b = transform.position + Vector3.up * (1.8f - cc.radius);
                int n = Physics.OverlapCapsuleNonAlloc(a, b, cc.radius * 0.95f, buf, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) if (buf[i] != cc) return;     // над головой низко — сидим дальше
            }
            Crouched = want;
            cc.height = want ? crouchHeight : 1.8f;
            cc.center = new Vector3(0, cc.height / 2f, 0);
            if (body) body.localScale = new Vector3(1f, want ? crouchHeight / 1.8f : 1f, 1f);
        }

        void Grab(LadderZone z)
        {
            climbing = z; dismount = false; velocity = Vector3.zero;
            SetCrouch(false);
            cc.radius = Mathf.Min(baseRadius, ladderRadius);
        }

        void LetGo()
        {
            climbing = null; dismount = false;
            cc.radius = baseRadius;
        }

        void Teleport(Vector3 p)
        {
            LetGo();
            cc.enabled = false;
            transform.position = p;
            cc.enabled = !Flying;
            velocity = Vector3.zero;
        }

        void Respawn() => Teleport(spawn);

        readonly Collider[] near = new Collider[48];

        /// <summary>Ближайший интерактивный элемент перед игроком в пределах его reach.</summary>
        Fortress.Grid.Interactable NearestInteractable(bool onLadder)
        {
            Vector3 eye = transform.position + Vector3.up * 1.1f;
            Vector3 look = body ? body.forward : transform.forward;
            int n = Physics.OverlapSphereNonAlloc(eye, 2.8f, near, ~0, QueryTriggerInteraction.Collide);
            Fortress.Grid.Interactable best = null; float bestScore = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var it = near[i].GetComponentInParent<Fortress.Grid.Interactable>();
                if (!it || !it.CanInteract(transform)) continue;
                if (onLadder && it is Fortress.Grid.Trapdoor) { best = it; break; }
                Vector3 p = near[i].bounds.ClosestPoint(eye);
                float d = Vector3.Distance(eye, p);
                if (d > it.reach) continue;
                Vector3 to = Vector3.ProjectOnPlane(p - eye, Vector3.up);
                float score = d - (to.sqrMagnitude > 0.01f ? Vector3.Dot(to.normalized, look) * 0.6f : 0.6f);
                if (score < bestScore) { bestScore = score; best = it; }
            }
            return best;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (WalkerInput.RespawnDown) Respawn();
            int jump = WalkerInput.PointDown;
            if (jump >= 0 && points != null && jump < points.Length) Teleport(points[jump]);
            if (WalkerInput.FlyToggleDown) { LetGo(); Flying = !Flying; cc.enabled = !Flying; velocity = Vector3.zero; }

            Vector3 camF = cameraTransform ? cameraTransform.forward : transform.forward;
            Vector3 fwd = Vector3.ProjectOnPlane(camF, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector2 mv = WalkerInput.Move;
            Vector3 wish = fwd * mv.y + right * mv.x;

            if (Flying)
            {
                Mode = "полёт (F)";
                WaterSurface = float.NaN;
                Vector3 v = camF * mv.y + right * mv.x;
                if (WalkerInput.JumpHeld) v += Vector3.up;
                if (WalkerInput.Crouch) v += Vector3.down;
                transform.position += v * flySpeed * (WalkerInput.Run ? 2.5f : 1f) * dt;
                Prompt = "";
                return;
            }

            // зоны вокруг капсулы
            LadderZone ladder = null; WaterZone water = null; TeleportZone tp = null; BreakZone brk = null;
            Vector3 p0 = transform.position + Vector3.up * cc.radius, p1 = transform.position + Vector3.up * (cc.height - cc.radius);
            int n = Physics.OverlapCapsuleNonAlloc(p0, p1, cc.radius + 0.15f, buf, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                if (!buf[i].isTrigger) continue;
                var lz = buf[i].GetComponent<LadderZone>();
                if (lz && (!ladder || lz == climbing)) ladder = lz;
                water = water ? water : buf[i].GetComponent<WaterZone>();
                tp = tp ? tp : buf[i].GetComponent<TeleportZone>();
                brk = brk ? brk : buf[i].GetComponent<BreakZone>();
            }
            if (climbing && ladder && ladder != climbing && Mathf.Approximately(ladder.top, climbing.top)) climbing = ladder;  // цепочка стремянок
            if (climbing && !ladder) LetGo();                     // ушли из зоны стремянки

            var act = NearestInteractable(climbing || ladder);
            Prompt = tp ? tp.prompt : (act ? act.PromptFor(transform) : (brk ? brk.prompt : ""));
            if (WalkerInput.InteractDown)
            {
                if (act && !tp) { act.Interact(transform); return; }
                if (tp) { Teleport(tp.target); return; }
                if (brk) brk.TryBreak(transform.position + Vector3.up * 1.0f);
            }

            Vector3 pos = transform.position;
            // взяться за стремянку: лицом к ней и вперёд, Пробел, или падая в люк над ней
            if (!climbing && ladder && pos.y < ladder.top - 0.25f)
            {
                bool facing = wish.sqrMagnitude > 0.04f && Vector3.Dot(wish.normalized, -ladder.face) > 0.4f && mv.y > 0.1f;
                bool falling = !cc.isGrounded && velocity.y < -1.5f;
                if (facing || WalkerInput.JumpDown || falling) Grab(ladder);
            }

            bool swimming = water && !climbing && pos.y < water.surfaceY - 0.8f;
            WaterSurface = water ? water.surfaceY : float.NaN;
            SetCrouch(WalkerInput.Crouch && !swimming && !climbing);
            float speed = Crouched ? crouchSpeed : (WalkerInput.Run ? runSpeed : walkSpeed);

            if (climbing)
            {
                Mode = "стремянка (C — отпустить)";
                var L = climbing;
                float climb = Mathf.Clamp(mv.y + (WalkerInput.JumpHeld ? 1f : 0f), -1f, 1f);
                if (climb > 0.1f)
                {   // упёрся головой в закрытый люк — откинуть крышку
                    Vector3 head = pos + Vector3.up * (cc.height + 0.25f);
                    int hn = Physics.OverlapSphereNonAlloc(head, 0.55f, near, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < hn; i++)
                    {
                        var td = near[i].GetComponentInParent<Fortress.Grid.Trapdoor>();
                        if (td && !td.IsOpen) td.Open();
                    }
                }
                Vector3 face = L.face; face.y = 0; face.Normalize();
                Vector3 onLine = new Vector3(L.line.x, pos.y, L.line.z) + face * ladderOffset;
                Vector3 h = onLine - pos; h.y = 0;
                float vy = climb * L.climbSpeed;
                if (climb > 0.1f && pos.y >= L.top - 0.06f) dismount = true;
                if (dismount)
                {   // верх: ступни чуть выше пола выхода, шаг на площадку (через стену или из люка)
                    vy = Mathf.Clamp((L.top + 0.06f - pos.y) * 10f, -1f, 2.5f);
                    h = -face * 2.4f;
                    float away = Vector3.Dot(new Vector3(pos.x - L.line.x, 0, pos.z - L.line.z), -face);
                    if (away > 0.45f || climb < -0.1f) { if (climb < -0.1f) dismount = false; else LetGo(); }
                }
                else
                {
                    h *= 10f;
                    if (pos.y > L.top + 0.1f) vy = Mathf.Min(vy, 0f);     // выше выхода не лезем
                }
                if (climbing)
                {
                    velocity = new Vector3(h.x, vy, h.z);
                    bool atBottom = cc.isGrounded && climb < -0.1f && pos.y < L.bottom + 0.4f;
                    bool intoWater = water && climb < -0.1f && pos.y < water.surfaceY - swimDepth;   // спустился в воду — плывём
                    if (WalkerInput.Crouch || atBottom || intoWater) LetGo();
                }
                if (!climbing) velocity.y = Mathf.Min(velocity.y, 0f);
            }
            else if (swimming)
            {
                Mode = "вплавь";
                float targetY = water.surfaceY - swimDepth;
                Vector3 h = wish * water.swimSpeed;
                velocity = new Vector3(h.x, (targetY - pos.y) * 3f, h.z);
            }
            else
            {
                Mode = cc.isGrounded ? (Crouched ? "присед" : "") : "в воздухе";
                Vector3 h = wish * speed;
                velocity.x = h.x; velocity.z = h.z;
                // прижим к опоре: вниз по ступеням капсула не отрывается (иначе «в воздухе» на каждой ступени — рывки)
                if (cc.isGrounded && velocity.y < 0) velocity.y = -Mathf.Max(2f, h.magnitude * 1.25f);
                if (cc.isGrounded && WalkerInput.JumpDown && !Crouched) velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                velocity.y += gravity * dt;
            }

            cc.Move(velocity * dt);
            if (!climbing && !swimming) SlideOffFurniture(dt);

            Vector3 lookDir = climbing ? -climbing.face : wish;
            lookDir.y = 0;
            if (body && lookDir.sqrMagnitude > 0.01f)
                body.rotation = Quaternion.Slerp(body.rotation, Quaternion.LookRotation(lookDir, Vector3.up), 12f * dt);

            if (transform.position.y < -60f) Respawn();
        }

        /// <summary>Мебель, ящики, бочки, ограды, решётки, люди и подвижные тела — не опора: встал на них (прыжком, со стула) —
        /// соскальзываем к ближнему свободному краю.</summary>
        void SlideOffFurniture(float dt)
        {
            if (!cc.isGrounded) return;
            Vector3 c = transform.position + Vector3.up * (cc.radius + 0.05f);
            if (!Physics.SphereCast(c, cc.radius * 0.9f, Vector3.down, out var hit, 0.3f, ~0, QueryTriggerInteraction.Ignore)) return;
            if (hit.collider == cc || !IsFurniture(hit.collider)) return;
            Bounds b = hit.collider.bounds;
            Vector3 p = transform.position;
            // к ближней грани (по осям мира); грань у стены — не туда
            var dirs = new[] { Vector3.left, Vector3.right, Vector3.back, Vector3.forward };
            var dist = new[] { p.x - b.min.x, b.max.x - p.x, p.z - b.min.z, b.max.z - p.z };
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                if (dist[i] >= bestD) continue;
                Vector3 exit = p + dirs[i] * (dist[i] + cc.radius + 0.1f);
                exit.y = b.min.y + 1.0f;
                bool wall = false;
                int n = Physics.OverlapSphereNonAlloc(exit, cc.radius * 0.8f, buf, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++) if (buf[k] != cc && buf[k] != hit.collider) { wall = true; break; }
                if (wall) continue;
                best = i; bestD = dist[i];
            }
            Vector3 away = best >= 0 ? dirs[best] : Vector3.ProjectOnPlane(p - b.center, Vector3.up).normalized;
            if (away.sqrMagnitude < 0.01f) away = body ? -body.forward : -transform.forward;
            away.y = 0f;
            cc.Move(away.normalized * 3f * dt);
        }

        static bool IsFurniture(Collider c)
        {
            if (c is CharacterController) return true;                                       // другой человек
            if (c.attachedRigidbody && !c.attachedRigidbody.isKinematic) return true;         // табурет, ящик на физике
            var fe = c.GetComponentInParent<Fortress.Grid.FortElement>();
            if (!fe) return false;
            var k = fe.kind;
            bool thing = k == Fortress.Grid.ElementKind.Prop || k == Fortress.Grid.ElementKind.WallProp || k == Fortress.Grid.ElementKind.Light;
            return thing && c.bounds.size.y > 0.2f;                                           // бордюр, коврик — можно
        }
    }
}
