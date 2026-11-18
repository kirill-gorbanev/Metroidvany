using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Rig.Human
{
    /// <summary>Руки человека в мире: взять вещь (ящик со стола или с пола), нести её, поставить; толкать тяжёлое;
    /// сесть на лавку и встать; на ходу — выставить руку навстречу двери или тому, что на пути.
    /// Всё физическое: ношу держат кисти (пружинные хваты на предплечьях), вес ноши тянет руки и корпус,
    /// сильный удар выбивает её из рук; толкает тело — силой, которая ограничена (неподъёмное не сдвинуть).
    /// Позу задаёт «призрак» (анимации + цели рук HumanGhostIK), физическое тело повторяет её мышцами.</summary>
    [DefaultExecutionOrder(-25)]
    public class HumanInteract : MonoBehaviour
    {
        public enum Task { None, ToPick, Pick, Carry, ToPlace, Place, ToPush, Push, GiveUp, ToSit, Sit, Sitting, Stand }

        [Header("Ноша")]
        [Tooltip("Ноша перед животом: насколько вперёд от груди и вниз")] public float holdForward = 0.16f, holdDown = 0.34f;
        [Tooltip("Хват: жёсткость пружины кисть—ноша (Н/м)")] public float gripSpring = 2600f;
        public float gripDamper = 70f;
        [Tooltip("Хват: предел силы кисти (Н) — сильнее рвёт из рук")] public float gripMaxForce = 520f;
        [Tooltip("Вырвало из рук, если кисть дальше от хвата (м)")] public float gripSlip = 0.26f;
        [Tooltip("Выбило из рук, если ношу рвануло относительно ладоней быстрее (м/с): мяч, бочка, удар")] public float gripSnap = 2.4f;
        [Tooltip("Руки, занятые делом (держат, толкают), сильнее во столько раз")] public float armBoost = 3f;
        [Tooltip("Притяжение физической кисти к кисти призрака, занятой делом (1/с²)")] public float handPull = 260f;
        [Range(0, 1), Tooltip("Какую долю веса ноши держат мышцы рук «сами» (остальное — тянет руки и корпус вниз)")] public float carryAssist = 0.7f;
        [Tooltip("Тяжелее этого (кг) — не поднять, только толкать")] public float maxCarry = 30f;

        [Header("Толкать")]
        public float pushSpeed = 0.55f;
        [Tooltip("Призрак не уходит вперёд тела больше чем на столько (м): сила толчка ограничена")] public float pushLag = 0.14f;
        [Tooltip("Предел «силы» толкания: ускорение всего тела (м/с²) — тело не таранит вещь")] public float pushAccel = 12f;
        [Tooltip("Сила рук и ног при толкании (Н): тяжелее (трение больше) — едет медленнее, совсем тяжёлое — не сдвинуть")] public float pushEffort = 650f;

        [Header("Рефлексы")]
        [Tooltip("На ходу выставлять руку навстречу двери / тому, что на уровне груди на пути")] public bool reachReflex = true;

        public Task task { get; private set; }
        public Rigidbody Item { get; private set; }
        public bool Carrying => grip;
        public event Action<string> Said;
        public string Mode => task switch
        {
            Task.ToPick => "идёт взять", Task.Pick => "берёт", Task.Carry => "несёт", Task.ToPlace => "несёт поставить",
            Task.Place => "ставит", Task.ToPush => "идёт толкать", Task.Push => "толкает", Task.GiveUp => "не сдвинуть",
            Task.ToSit => "идёт сесть", Task.Sit => "садится", Task.Sitting => "сидит", Task.Stand => "встаёт", _ => "",
        };

        HumanPuppet hp;
        HumanDriver drv;
        HumanGhostIK ik;
        Animator anim;
        Transform gChest;
        Rigidbody armL, armR;
        Transform handL, handR;
        Transform gHandL, gHandR;
        float attachTime;
        ConfigurableJoint jL, jR;
        bool grip;
        Vector3 gripL, gripR;          // точки хвата в осях ноши
        Bounds ib;                      // габарит ноши в её осях
        int sideAxis;                   // ось ноши поперёк тела (0 = x, 2 = z)
        Quaternion holdRel;             // поворот ноши относительно направления тела
        readonly List<Collider> ignored = new List<Collider>();
        float restoreAt = -1f;
        Vector3 placeSpot, approachDir;
        bool placeQueued;
        Vector3 pushDir, pushStart;
        float pushDist, pushStuck, pushStand, pushFriction, pushForce;
        /// <summary>Насколько сейчас налегает (0..1) — для стенда.</summary>
        public float Effort { get; private set; }
        Vector3 seatPt, seatFwd, sitFrom, sitTo;
        [Tooltip("Анимация «сесть»: таз сидящего позади корня на столько (м, замер клипа)")] public float sitPelvisBack = 0.15f;
        [Tooltip("Садится на переднюю часть сиденья: таз впереди середины на столько (м)")] public float sitFront = 0.08f;
        float t;
        float wL, wR, wantW, blend, carryW;
        int carryLayer = -1;
        float reachW, reachSeen;
        Vector3 reachPt;
        bool reachRight;
        Vector3 aimL, aimR;
        bool low;

        static readonly int MoveState = Animator.StringToHash("Move");

        void Start() => Init();

        /// <summary>Связи (лениво: команду могут дать в тот же кадр, когда человека поставили).</summary>
        bool Init()
        {
            if (drv) return true;
            hp = GetComponent<HumanPuppet>();
            if (!hp || !hp.driver) return false;
            drv = hp.driver;
            anim = hp.anim;
            ik = drv.GetComponentInChildren<HumanGhostIK>();
            var cm = hp.chest.GetComponent<RigMuscle>();
            gChest = cm && cm.follow ? cm.follow : drv.transform;
            armL = hp.arms[1].GetComponent<Rigidbody>(); armR = hp.arms[3].GetComponent<Rigidbody>();
            handL = Find(armL.transform, "hand_l") ?? armL.transform;
            handR = Find(armR.transform, "hand_r") ?? armR.transform;
            carryLayer = anim.GetLayerIndex("Carry");
            gHandL = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            gHandR = anim.GetBoneTransform(HumanBodyBones.RightHand);
            return true;
        }

        static Transform Find(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = Find(c, n); if (r) return r; }
            return null;
        }

        // ================================================================ команды
        /// <summary>Подойти и взять вещь (ящик, ведро). Тяжелее maxCarry — не берёт.</summary>
        public bool PickUp(Rigidbody rb)
        {
            if (!Init() || !rb || grip || rb.mass > maxCarry) return false;
            Cancel();
            Item = rb;
            ib = LocalBounds(rb);
            task = Task.ToPick; t = 0f;
            return true;
        }

        /// <summary>Отнести ношу и поставить в точку (низ ноши — на эту точку: пол, стол, помост).</summary>
        public void PutDown(Vector3 spot)
        {
            if (!Init() || (!grip && task != Task.ToPick && task != Task.Pick)) return;
            placeSpot = spot;
            placeQueued = true;
            if (task == Task.Carry) StartToPlace();
        }

        void StartToPlace()
        {
            approachDir = Flat(placeSpot - drv.transform.position);
            approachDir = approachDir.sqrMagnitude > 1e-3f ? approachDir.normalized : Flat(drv.transform.forward).normalized;
            task = Task.ToPlace; t = 0f;
        }

        /// <summary>Поставить ношу прямо перед собой (на пол или на стол, если он перед тобой).</summary>
        public void PutDownHere()
        {
            if (!Init() || !grip) return;
            Vector3 f = Flat(drv.transform.forward).normalized;
            Vector3 p = drv.transform.position + f * (Depth(f) + 0.45f);
            float y = drv.transform.position.y;
            if (Physics.Raycast(p + Vector3.up * 1.6f, Vector3.down, out var h, 3f, RigLayers.WorldMask, QueryTriggerInteraction.Ignore)) y = h.point.y;
            placeSpot = new Vector3(p.x, y, p.z);
            approachDir = f;
            placeQueued = true;
            task = Task.Place; t = 0f;
            BeginPlace();
        }

        /// <summary>Толкать тяжёлое в направлении dir на расстояние dist.</summary>
        public void Push(Rigidbody rb, Vector3 dir, float dist)
        {
            if (!Init() || !rb || grip) return;
            Cancel();
            Item = rb;
            ib = LocalBounds(rb);
            pushDir = Flat(dir).normalized;
            pushDist = dist;
            task = Task.ToPush; t = 0f;
        }

        /// <summary>Сесть: seatTop — точка на верху сиденья, fwd — куда смотреть сидя.</summary>
        public void SitOn(Vector3 seatTop, Vector3 fwd)
        {
            if (!Init() || grip) return;
            Cancel();
            seatPt = seatTop; seatFwd = Flat(fwd).normalized;
            task = Task.ToSit; t = 0f;
        }

        public void Stand()
        {
            if (!Init()) return;
            if (task != Task.Sitting && task != Task.Sit) return;
            task = Task.Stand; t = 0f;
            anim.CrossFadeInFixedTime("Sitting_Exit", 0.2f);
        }

        /// <summary>Выпустить ношу (уронить).</summary>
        public void Drop(string why = null)
        {
            if (grip)
            {
                Release();
                if (why != null) Said?.Invoke(why);
            }
            if (task == Task.Pick || task == Task.Carry || task == Task.ToPlace || task == Task.Place)
            {
                task = Task.None;
                if (hp.state == HumanPuppet.State.Active) anim.CrossFadeInFixedTime(MoveState, 0.3f);
            }
        }

        /// <summary>Бросить текущее дело (ношу не роняет, если несёт).</summary>
        public void Cancel()
        {
            if (!Init()) return;
            drv.goTo = null; drv.face = null; drv.keepFacing = false; drv.overrideVelocity = null;
            if ((task == Task.Sit || task == Task.Sitting || task == Task.Stand) && hp.state == HumanPuppet.State.Active) drv.frozen = false;
            hp.maxAccelH = float.PositiveInfinity;
            hp.seated = false;
            if (task == Task.Push) { IgnoreArms(false); hp.allowLag = false; }
            if (anim) anim.speed = 1f;
            if (task == Task.Sitting || task == Task.Sit || task == Task.Stand || task == Task.Push || task == Task.GiveUp)
                if (hp.state == HumanPuppet.State.Active) anim.CrossFadeInFixedTime(MoveState, 0.3f);
            task = grip ? Task.Carry : Task.None;
            placeQueued = false;
        }

        /// <summary>«Действие» для игрока (E): поставить ношу / встать / взять / толкать / сесть — по тому, что перед ним.</summary>
        public string Use()
        {
            if (!Init()) return "";
            if (grip) { PutDownHere(); return "ставит ношу"; }
            if (task == Task.Sitting) { Stand(); return "встаёт"; }
            Vector3 o = drv.transform.position + Vector3.up * 0.6f;
            Vector3 f = Flat(drv.transform.forward).normalized;
            Rigidbody best = null; float bestD = 1.7f;
            foreach (var c in Physics.OverlapSphere(o + f * 0.9f, 1.0f, ~0, QueryTriggerInteraction.Ignore))
            {
                var rb = c.attachedRigidbody;
                if (!rb || rb.isKinematic || rb.GetComponentInParent<RigBody>() || rb.GetComponent<Joint>() is HingeJoint) continue;
                float d = Flat(rb.worldCenterOfMass - drv.transform.position).magnitude;
                if (d < bestD) { bestD = d; best = rb; }
            }
            if (best)
            {
                if (best.mass <= maxCarry) { PickUp(best); return "берёт: " + best.name; }
                Push(best, f, 1.2f); return "толкает: " + best.name;
            }
            foreach (var c in Physics.OverlapSphere(o + f * 0.6f, 1.0f, RigLayers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                var seat = c.GetComponentInParent<RigSeat>();
                if (!seat) continue;
                seat.Nearest(drv.transform.position, out var p, out var fw);
                SitOn(p, fw); return "садится";
            }
            return "рядом нечего взять";
        }

        // ================================================================ шаг
        void FixedUpdate()
        {
            if (!Init()) return;
            float dt = Time.fixedDeltaTime;
            t += dt;
            if (restoreAt > 0f && Time.time > restoreAt) RestoreCollisions();

            // упал — всё из рук, дело брошено
            if (hp.state == HumanPuppet.State.Fallen || hp.state == HumanPuppet.State.GettingUp)
            {
                if (grip) Drop("выронил");
                if (task != Task.None) { Cancel(); task = Task.None; }
                wantW = 0f;
                Apply(dt);
                return;
            }
            // игрок сам повёл — подход отменяется
            if (drv.possessed && drv.possessVelocity.sqrMagnitude > 0.01f &&
                (task == Task.ToPick || task == Task.ToPlace || task == Task.ToPush || task == Task.ToSit || task == Task.Push))
                Cancel();
            // вырвало из рук (не сразу после хвата: кисти ещё подтягивают ношу)
            if (grip && (!Item || ((task == Task.Carry || task == Task.ToPlace) && Time.time - attachTime > 0.7f && HandGap() > gripSlip))) Drop("выбили из рук");
            // рывок: ношу резко дёрнуло относительно ладоней — пальцы не удержат
            if (grip && Item && Time.time - attachTime > 0.5f &&
                (Item.linearVelocity - (armL.GetPointVelocity(handL.position) + armR.GetPointVelocity(handR.position)) * 0.5f).magnitude > gripSnap)
                Drop("выбило из рук");

            bool active = hp.state == HumanPuppet.State.Active;
            switch (task)
            {
                case Task.None:
                    wantW = 0f;
                    break;

                case Task.ToPick:
                {
                    if (!Item) { task = Task.None; break; }
                    Vector3 c = Item.worldCenterOfMass;
                    Vector3 to = Flat(c - drv.transform.position);
                    Vector3 dir = to.sqrMagnitude > 1e-3f ? to.normalized : Flat(drv.transform.forward).normalized;
                    drv.goTo = Flat(c) - dir * (Depth(dir) + 0.38f) + Vector3.up * drv.transform.position.y;
                    drv.face = c;
                    wantW = 0f;
                    if (active && drv.Arrived && drv.Facing) { drv.goTo = null; task = Task.Pick; t = 0f; BeginPick(); }
                    break;
                }

                case Task.Pick:
                {
                    if (!Item) { Drop(); break; }
                    // руки — к бокам ноши; на 0.55 с — хват; потом — подняться и прижать к себе
                    wantW = 1f;
                    WorldGrips(out var gl, out var gr);
                    HoldPoints(out var hl, out var hr);
                    if (t >= 0.55f && !grip) Attach();
                    if (t >= 0.75f && blend == 0f) { anim.CrossFadeInFixedTime(MoveState, 0.45f); }
                    blend = t >= 0.75f ? Mathf.Clamp01((t - 0.75f) / 0.55f) : 0f;
                    aimL = Vector3.Lerp(gl, hl, Smooth(blend)); aimR = Vector3.Lerp(gr, hr, Smooth(blend));
                    carryW = Mathf.MoveTowards(carryW, t >= 0.75f ? 1f : 0f, dt / 0.4f);
                    drv.face = null;
                    if (t >= 1.35f) { blend = 0f; if (placeQueued) StartToPlace(); else { task = Task.Carry; t = 0f; } }
                    break;
                }

                case Task.Carry:
                    wantW = 1f;
                    HoldPoints(out aimL, out aimR);
                    carryW = Mathf.MoveTowards(carryW, 1f, dt / 0.4f);
                    if (placeQueued) StartToPlace();
                    break;

                case Task.ToPlace:
                {
                    wantW = 1f;
                    HoldPoints(out aimL, out aimR);
                    carryW = Mathf.MoveTowards(carryW, 1f, dt / 0.4f);
                    drv.goTo = Flat(placeSpot) - approachDir * (Depth(approachDir) + 0.32f) + Vector3.up * drv.transform.position.y;
                    drv.face = placeSpot;
                    if (active && drv.Arrived && drv.Facing) { drv.goTo = null; task = Task.Place; t = 0f; BeginPlace(); }
                    break;
                }

                case Task.Place:
                {
                    // приподнять над опорой → перенести → опустить → отпустить (не цеплять край стола)
                    wantW = t < 1.0f ? 1f : 0f;
                    HoldPoints(out var hl, out var hr);
                    PlaceGrips(out var pl, out var pr);
                    float up = Mathf.Max(0f, pl.y + 0.1f - hl.y);
                    Vector3 l1 = hl + Vector3.up * up, r1 = hr + Vector3.up * up;
                    Vector3 l2 = pl + Vector3.up * Mathf.Max(0.1f, l1.y - pl.y > 0f ? 0.1f : 0f), r2 = pr + Vector3.up * 0.1f;
                    l2.y = pl.y + 0.1f;
                    if (t < 0.3f) { float k = Smooth(t / 0.3f); aimL = Vector3.Lerp(hl, l1, k); aimR = Vector3.Lerp(hr, r1, k); }
                    else if (t < 0.6f) { float k = Smooth((t - 0.3f) / 0.3f); aimL = Vector3.Lerp(l1, l2, k); aimR = Vector3.Lerp(r1, r2, k); }
                    else { float k = Smooth(Mathf.Clamp01((t - 0.6f) / 0.3f)); aimL = Vector3.Lerp(l2, pl, k); aimR = Vector3.Lerp(r2, pr, k); }
                    carryW = Mathf.MoveTowards(carryW, 0f, dt / 0.6f);
                    if (t >= 0.95f && grip) { Release(); placeQueued = false; Said?.Invoke("поставил"); }
                    if (t >= 1.05f && t - dt < 1.05f) anim.CrossFadeInFixedTime(MoveState, 0.4f);
                    if (t >= 1.5f) { task = Task.None; drv.face = null; Item = null; }
                    break;
                }

                case Task.ToPush:
                {
                    if (!Item) { task = Task.None; break; }
                    Vector3 c = Item.worldCenterOfMass;
                    drv.goTo = Flat(c) - pushDir * (Depth(pushDir) + 0.42f) + Vector3.up * drv.transform.position.y;
                    drv.face = drv.goTo.Value + pushDir * 3f;
                    wantW = 0f;
                    if (active && drv.Arrived && drv.Facing)
                    {
                        drv.goTo = null;
                        task = Task.Push; t = 0f;
                        pushStart = Item.position; pushStuck = 0f;
                        pushStand = Mathf.Max(0.38f, Vector3.Dot(FacePoint() - drv.transform.position, pushDir));
                        // оценка трения покоя: в PhysX «липкое» трение держит в ~2.5 раза крепче номинала
                        var mat = Item.GetComponentInChildren<Collider>()?.sharedMaterial;
                        float mu = mat ? (mat.frictionCombine == PhysicsMaterialCombine.Minimum ? Mathf.Min(mat.staticFriction, 0.6f) : (mat.staticFriction + 0.6f) * 0.5f) : 0.6f;
                        pushFriction = mu * 2.5f * Item.mass * -Physics.gravity.y;
                        pushForce = 0f;
                        drv.keepFacing = true;
                        drv.face = drv.transform.position + pushDir * 5f;
                        anim.CrossFadeInFixedTime("Push_Loop", 0.35f);
                        IgnoreArms(true);
                        hp.allowLag = true;
                    }
                    break;
                }

                case Task.Push:
                {
                    if (!Item) { Cancel(); break; }
                    wantW = 1f;
                    FacePoints(out aimL, out aimR);
                    hp.maxAccelH = pushAccel;
                    float iv = Vector3.Dot(Item.linearVelocity, pushDir);
                    // усилие: ладони на грани — давит до предела сил; скорость, которую он может держать, — по запасу сил над трением
                    float vT = pushSpeed * Mathf.Clamp01((pushEffort - pushFriction) / pushEffort * 1.6f);
                    Effort = 0f;
                    if (active && t > 0.45f && HandsOnFace())
                    {
                        // налегает всё сильнее, пока вещь не пошла (до предела сил), потом держит ход
                        pushForce = Mathf.Clamp(pushForce + (vT - iv) * Item.mass * 4f * dt + (iv < 0.03f ? pushEffort * 0.6f * dt : 0f), 0f, pushEffort);
                        float f = Mathf.Clamp(pushForce + (vT - iv) * Item.mass * 3f, 0f, pushEffort);
                        float cy = Item.worldCenterOfMass.y;
                        Item.AddForceAtPosition(pushDir * f * 0.5f, new Vector3(handL.position.x, cy, handL.position.z), ForceMode.Force);
                        Item.AddForceAtPosition(pushDir * f * 0.5f, new Vector3(handR.position.x, cy, handR.position.z), ForceMode.Force);
                        Effort = f / pushEffort;
                    }
                    // призрак держится на своём расстоянии от грани: вещь пошла — пошёл за ней
                    float gap = Vector3.Dot(FacePoint() - drv.transform.position, pushDir) - pushStand;
                    float v = t < 0.45f ? Mathf.Clamp(gap * 3f, 0f, 0.3f) : Mathf.Clamp(Mathf.Max(0f, iv) + gap * 2.5f, 0f, pushSpeed);
                    if (active) drv.overrideVelocity = pushDir * v;
                    drv.face = drv.transform.position + pushDir * 5f;
                    anim.speed = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(iv / 0.45f));
                    float moved = Vector3.Dot(Item.position - pushStart, pushDir);
                    pushStuck = iv < 0.04f && t > 1.0f ? pushStuck + dt : 0f;
                    if (moved >= pushDist) { Said?.Invoke($"сдвинул на {moved:F1} м"); EndPush(); task = Task.None; }
                    else if (pushStuck > 2.6f)
                    {
                        Said?.Invoke("не сдвинуть");
                        EndPush();
                        task = Task.GiveUp; t = 0f;
                        anim.CrossFadeInFixedTime("Idle_No_Loop", 0.3f);
                    }
                    break;
                }

                case Task.GiveUp:
                    wantW = 0f;
                    drv.overrideVelocity = Vector3.zero;
                    if (t > 2.4f) { drv.overrideVelocity = null; anim.CrossFadeInFixedTime(MoveState, 0.4f); task = Task.None; Item = null; }
                    break;

                case Task.ToSit:
                {
                    wantW = 0f;
                    drv.goTo = Flat(seatPt) + seatFwd * 0.62f + Vector3.up * drv.transform.position.y;
                    drv.face = drv.goTo.Value + seatFwd * 3f;
                    if (active && drv.Arrived && drv.Facing)
                    {
                        drv.goTo = null;
                        task = Task.Sit; t = 0f;
                        hp.seated = true;
                        ik.seatPoint = seatPt + seatFwd * sitFront;
                        // корень — вплотную к лавке, чтобы таз из анимации лёг на сиденье (без столкновений призрака с лавкой)
                        drv.frozen = true;
                        sitFrom = drv.transform.position;
                        sitTo = Flat(seatPt + seatFwd * (sitFront + sitPelvisBack)) + Vector3.up * sitFrom.y;
                        anim.CrossFadeInFixedTime("Sitting_Enter", 0.25f);
                    }
                    break;
                }

                case Task.Sit:
                    wantW = 0f;
                    drv.Nudge(Vector3.Lerp(sitFrom, sitTo, Smooth(Mathf.Clamp01(t / 0.8f))));
                    ik.seatW = Smooth(Mathf.Clamp01((t - 0.15f) / 1.0f));
                    if (t >= 1.2f) { anim.CrossFadeInFixedTime("Sitting_Idle_Loop", 0.3f); task = Task.Sitting; t = 0f; }
                    break;

                case Task.Sitting:
                    wantW = 0f;
                    ik.seatW = 1f;
                    break;

                case Task.Stand:
                    wantW = 0f;
                    ik.seatW = 1f - Smooth(Mathf.Clamp01((t - 0.1f) / 0.8f));
                    drv.Nudge(Vector3.Lerp(sitTo, sitFrom, Smooth(Mathf.Clamp01((t - 0.2f) / 0.7f))));
                    if (t >= 0.95f)
                    {
                        drv.frozen = false;
                        hp.seated = false;
                        ik.seatW = 0f;
                        drv.face = null;
                        anim.CrossFadeInFixedTime(MoveState, 0.3f);
                        task = Task.None;
                    }
                    break;
            }
            if (task != Task.Sit && task != Task.Sitting && task != Task.Stand) ik.seatW = Mathf.MoveTowards(ik.seatW, 0f, dt * 2f);
            if (!grip && task != Task.Pick && task != Task.Place) carryW = Mathf.MoveTowards(carryW, 0f, dt / 0.4f);

            if (grip) HoldItem(dt);
            Arms(dt);
            // тянется дальше вытянутой руки — наклониться корпусом
            float leanWant = 0f;
            if ((task == Task.Pick || task == Task.Place || task == Task.Push) && wantW > 0.5f)
            {
                Vector3 mid = (aimL + aimR) * 0.5f;
                Vector3 sh = gChest.position + Vector3.up * 0.15f;
                float d = Flat(mid - drv.transform.position).magnitude;
                leanWant = Mathf.Clamp01((d - 0.38f) / 0.3f) * 30f + Mathf.Clamp01((sh.y - mid.y - 0.75f) / 0.4f) * 15f;
                if (task == Task.Push) leanWant = Mathf.Min(leanWant, 15f);     // Push_Loop и так налегает
            }
            ik.lean = leanWant;
            drv.hold = task == Task.Pick || task == Task.Place || task == Task.GiveUp || task == Task.Sit || task == Task.Sitting || task == Task.Stand;
            Reach(dt);
            Apply(dt);
        }

        void BeginPick()
        {
            WorldGrips(out var gl, out var gr);
            float floor = drv.transform.position.y;
            low = Mathf.Min(gl.y, gr.y) < floor + 0.62f;
            if (low) anim.CrossFadeInFixedTime("Crouch_Idle_Loop", 0.35f);
            blend = 0f;
        }

        void BeginPlace()
        {
            low = placeSpot.y < drv.transform.position.y + 0.45f;
            if (low) anim.CrossFadeInFixedTime("Crouch_Idle_Loop", 0.35f);
        }

        void EndPush()
        {
            IgnoreArms(false);
            hp.allowLag = false;
            drv.overrideVelocity = null; drv.keepFacing = false; drv.face = null;
            hp.maxAccelH = float.PositiveInfinity;
            anim.speed = 1f;
            anim.CrossFadeInFixedTime(MoveState, 0.35f);
        }

        // ================================================================ руки «призрака»
        void Apply(float dt)
        {
            float target = wantW;
            wL = Mathf.MoveTowards(wL, target, dt / 0.3f);
            wR = Mathf.MoveTowards(wR, target, dt / 0.3f);
            ik.handL = aimL; ik.handR = aimR;
            ik.handLW = wL; ik.handRW = wR;
            if (grip || task == Task.Pick || task == Task.Place || task == Task.Push)
            {
                Vector3 r = Flat(drv.transform.right).normalized, f = Flat(drv.transform.forward).normalized;
                ik.elbowHintL = gChest.position - r * 0.5f - f * 0.15f + Vector3.down * 0.25f;
                ik.elbowHintR = gChest.position + r * 0.5f - f * 0.15f + Vector3.down * 0.25f;
                ik.hintW = 0.6f;
            }
            else ik.hintW = 0f;
            // рефлекс «рука навстречу» — поверх, когда руки свободны
            if (reachW > 0.001f && wantW < 0.01f && wL < 0.01f)
            {
                if (reachRight) { ik.handR = reachPt; ik.handRW = reachW; }
                else { ik.handL = reachPt; ik.handLW = reachW; }
            }
            if (carryLayer >= 0) anim.SetLayerWeight(carryLayer, carryW);
            // взгляд: на вещь, с которой работает, или туда, куда тянется рука
            Vector3? look = null;
            if (Item && (task == Task.ToPick || task == Task.Pick || task == Task.Push || task == Task.ToPush)) look = Item.worldCenterOfMass;
            else if (task == Task.ToPlace || task == Task.Place) look = placeSpot;
            else if (reachW > 0.2f) look = reachPt;
            ik.lookW = Mathf.MoveTowards(ik.lookW, look.HasValue ? 0.8f : 0f, dt * 2f);
            if (look.HasValue) ik.lookAt = look.Value;
        }

        /// <summary>На ходу: дверь, человек, висящее — на уровне груди прямо по ходу: выставить руку и упереться.</summary>
        void Reach(float dt)
        {
            reachSeen -= dt;
            bool can = reachReflex && wantW < 0.01f && !grip && hp.state == HumanPuppet.State.Active &&
                       (task == Task.None || task == Task.ToPick || task == Task.ToPush || task == Task.ToSit);
            Vector3 v = Flat(drv.Velocity);
            if (can && v.sqrMagnitude > 0.12f)
            {
                Vector3 o = gChest.position, d = v.normalized;
                float floor = drv.transform.position.y;
                if (Physics.SphereCast(o, 0.16f, d, out var h, 0.95f, 1 << RigLayers.Body, QueryTriggerInteraction.Ignore) &&
                    !hp.body.Owns(h.collider) && h.rigidbody && h.point.y > floor + 0.75f && h.point.y < floor + 1.8f)
                {
                    if (reachSeen <= 0f) reachRight = Vector3.Dot(h.point - o, Flat(drv.transform.right)) >= 0f;
                    reachSeen = 0.35f;
                    reachPt = h.point + h.normal * 0.05f;
                }
            }
            reachW = Mathf.MoveTowards(reachW, can && reachSeen > 0f ? 1f : 0f, dt / (reachSeen > 0f ? 0.18f : 0.35f));
        }

        // ================================================================ ноша: хват
        void Attach()
        {
            if (!Item) return;
            Vector3 r = Flat(drv.transform.right).normalized;
            ChooseGrips(r);
            jL = Grip(armL, handL, gripL);
            jR = Grip(armR, handR, gripR);
            grip = true;
            attachTime = Time.time;
            // не толкаться ношей о собственное тело
            ignored.Clear();
            foreach (var ic in Item.GetComponentsInChildren<Collider>())
                foreach (var bc in hp.body.GetComponentsInChildren<Collider>())
                {
                    Physics.IgnoreCollision(ic, bc, true);
                    ignored.Add(ic); ignored.Add(bc);
                }
            restoreAt = -1f;
            // поворот ноши относительно направления тела — держать, как взял (ровно)
            Quaternion yaw = Quaternion.LookRotation(Flat(drv.transform.forward).normalized, Vector3.up);
            Vector3 itemFwd = Flat(Item.rotation * Vector3.forward);
            holdRel = Quaternion.Inverse(yaw) * Quaternion.LookRotation(itemFwd.sqrMagnitude > 1e-3f ? itemFwd.normalized : Vector3.forward, Vector3.up);
            drv.speedScale = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(Item.mass / maxCarry));
            Said?.Invoke($"взял ({Item.mass:F0} кг)");
        }

        ConfigurableJoint Grip(Rigidbody arm, Transform hand, Vector3 itemLocal)
        {
            var j = Item.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = arm;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = itemLocal;
            j.connectedAnchor = arm.transform.InverseTransformPoint(hand.position);
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Free;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
            var d = new JointDrive { positionSpring = gripSpring, positionDamper = gripDamper, maximumForce = gripMaxForce };
            j.xDrive = d; j.yDrive = d; j.zDrive = d;
            j.targetPosition = Vector3.zero;
            j.enableCollision = false;
            return j;
        }

        void Release()
        {
            if (jL) Destroy(jL);
            if (jR) Destroy(jR);
            jL = jR = null;
            grip = false;
            drv.speedScale = 1f;
            restoreAt = Time.time + 0.5f;
        }

        void RestoreCollisions()
        {
            for (int i = 0; i + 1 < ignored.Count; i += 2)
                if (ignored[i] && ignored[i + 1]) Physics.IgnoreCollision(ignored[i], ignored[i + 1], false);
            ignored.Clear();
            restoreAt = -1f;
        }

        float HandGap()
        {
            if (!grip || !Item) return 0f;
            float a = Vector3.Distance(handL.position, Item.transform.TransformPoint(gripL));
            float b = Vector3.Distance(handR.position, Item.transform.TransformPoint(gripR));
            return Mathf.Max(a, b);
        }

        /// <summary>Руки при деле: мышцы сильнее, кисти тянутся к кистям призрака (к ноше, к грани ящика).</summary>
        void Arms(float dt)
        {
            bool busy = grip || task == Task.Pick || task == Task.Place || task == Task.Push;
            float b = busy ? armBoost : 1f;
            foreach (var m in hp.arms) m.boost = Mathf.MoveTowards(m.boost, b, dt * 6f);
            if (!busy || hp.state != HumanPuppet.State.Active) return;
            Pull(armL, handL, gHandL, wL);
            Pull(armR, handR, gHandR, wR);
        }

        void Pull(Rigidbody arm, Transform hand, Transform target, float w)
        {
            if (!target || w < 0.05f) return;
            Vector3 d = target.position - hand.position;
            Vector3 v = arm.GetPointVelocity(hand.position);
            Vector3 vt = drv.Velocity;
            Vector3 a = (d * handPull + (vt - v) * 2f * Mathf.Sqrt(handPull)) * w;
            arm.AddForceAtPosition(Vector3.ClampMagnitude(a, 60f) * arm.mass, hand.position, ForceMode.Force);
        }

        /// <summary>Мышцы рук держат часть веса; ноша держится ровно и повёрнута, как её взяли.</summary>
        void HoldItem(float dt)
        {
            Item.AddForce(-Physics.gravity * carryAssist, ForceMode.Acceleration);
            Quaternion yaw = Quaternion.LookRotation(Flat(drv.transform.forward).normalized, Vector3.up);
            Quaternion want = yaw * holdRel;
            Quaternion dq = want * Quaternion.Inverse(Item.rotation);
            dq.ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            if (Mathf.Abs(ang) > 0.01f && axis.sqrMagnitude > 0.5f)
            {
                Vector3 acc = axis.normalized * (ang * Mathf.Deg2Rad * 60f) - Item.angularVelocity * 9f;
                Item.AddTorque(Vector3.ClampMagnitude(acc, 60f), ForceMode.Acceleration);
            }
        }

        /// <summary>Выбрать бока ноши под кисти: ось ноши, ближайшая к «поперёк тела».</summary>
        void ChooseGrips(Vector3 rightW)
        {
            Vector3 rl = Item.transform.InverseTransformDirection(rightW);
            sideAxis = Mathf.Abs(rl.x) >= Mathf.Abs(rl.z) ? 0 : 2;
            float s = Mathf.Sign(sideAxis == 0 ? rl.x : rl.z);
            Vector3 a = sideAxis == 0 ? Vector3.right : Vector3.forward;
            float e = sideAxis == 0 ? ib.extents.x : ib.extents.z;
            Vector3 c = ib.center + Vector3.up * ib.extents.y * 0.15f;
            gripL = c - a * (e * s);
            gripR = c + a * (e * s);
        }

        void WorldGrips(out Vector3 l, out Vector3 r)
        {
            if (!grip) ChooseGrips(Flat(drv.transform.right).normalized);
            Vector3 out1 = Item.transform.TransformPoint(gripL), out2 = Item.transform.TransformPoint(gripR);
            Vector3 side = (out2 - out1).normalized;
            l = out1 - side * 0.035f;
            r = out2 + side * 0.035f;
        }

        float HalfWidth() => Vector3.Distance(Item.transform.TransformPoint(gripL), Item.transform.TransformPoint(gripR)) * 0.5f;

        void HoldPoints(out Vector3 l, out Vector3 r)
        {
            Vector3 f = Flat(drv.transform.forward).normalized, rt = Flat(drv.transform.right).normalized;
            float depth = Item ? Depth(f) : 0.15f;
            Vector3 c = gChest.position + f * (holdForward + depth) + Vector3.down * holdDown;
            float hw = Item ? HalfWidth() + 0.035f : 0.2f;
            l = c - rt * hw; r = c + rt * hw;
        }

        void PlaceGrips(out Vector3 l, out Vector3 r)
        {
            Vector3 rt = Flat(drv.transform.right).normalized;
            // центр ноши над точкой, низ — на опоре (+1 см)
            float gripAboveBottom = (gripL.y - ib.min.y);
            Vector3 c = placeSpot + Vector3.up * (gripAboveBottom + 0.015f);
            float hw = HalfWidth() + 0.035f;
            l = c - rt * hw; r = c + rt * hw;
        }

        Vector3 FacePoint() => Flat(Item.worldCenterOfMass) - pushDir * Depth(pushDir) + Vector3.up * drv.transform.position.y;

        /// <summary>Обе ладони у ближней грани (не дальше 16 см от неё и не мимо ящика).</summary>
        bool HandsOnFace()
        {
            Vector3 f = FacePoint();
            foreach (var h in new[] { handL, handR })
            {
                float along = Vector3.Dot(h.position - f, pushDir);
                if (along < -0.22f || along > 0.12f) return false;
                Vector3 side = Vector3.ProjectOnPlane(h.position - Item.worldCenterOfMass, pushDir);
                if (Flat(side).magnitude > Depth(Vector3.Cross(Vector3.up, pushDir)) + 0.1f) return false;
            }
            return true;
        }

        /// <summary>Толкать: кисти — на ближнюю грань, на высоте груди (или по верху низкого ящика).</summary>
        void FacePoints(out Vector3 l, out Vector3 r)
        {
            Vector3 rt = Vector3.Cross(Vector3.up, pushDir).normalized;
            Vector3 c = Item.worldCenterOfMass;
            WorldSpan(out float bottom, out float top);
            // ладони — на грань повыше середины, но не выше груди (сила считается на высоте центра тяжести — ящик не кренится)
            float floor = drv.transform.position.y;
            float y = Mathf.Clamp(Mathf.Max(bottom + (top - bottom) * 0.6f, floor + 0.8f), floor + 0.3f, Mathf.Min(top - 0.08f, floor + 1.15f));
            Vector3 face = Flat(c) - pushDir * (Depth(pushDir) + 0.04f);
            l = new Vector3(face.x, y, face.z) - rt * 0.2f;
            r = new Vector3(face.x, y, face.z) + rt * 0.2f;
        }

        /// <summary>Полуглубина вещи вдоль направления (по её габариту).</summary>
        float Depth(Vector3 dirW)
        {
            if (!Item) return 0.2f;
            Vector3 d = Item.transform.InverseTransformDirection(dirW);
            Vector3 e = Vector3.Scale(ib.extents, Item.transform.lossyScale);
            return Mathf.Abs(d.x) * e.x + Mathf.Abs(d.y) * e.y + Mathf.Abs(d.z) * e.z;     // и лёжа на боку
        }

        /// <summary>Низ и верх вещи в мире (как бы она ни лежала).</summary>
        void WorldSpan(out float bottom, out float top)
        {
            Vector3 e = Vector3.Scale(ib.extents, Item.transform.lossyScale);
            Vector3 c = Item.transform.TransformPoint(ib.center);
            Vector3 up = Item.transform.InverseTransformDirection(Vector3.up);
            float h = Mathf.Abs(up.x) * e.x + Mathf.Abs(up.y) * e.y + Mathf.Abs(up.z) * e.z;
            bottom = c.y - h; top = c.y + h;
        }

        /// <summary>Пока толкает — тело не бьётся о вещь: толкает усилие на высоте центра тяжести,
        /// а не «рука бога» корпуса, который налегает на верхний край (вещь не опрокидывается).</summary>
        void IgnoreArms(bool on)
        {
            if (!Item) return;
            foreach (var bc in hp.body.GetComponentsInChildren<Collider>())
                foreach (var ic in Item.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(bc, ic, on);
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Габарит вещи в её собственных осях (по сеткам).</summary>
        public static Bounds LocalBounds(Rigidbody rb)
        {
            var b = new Bounds();
            bool has = false;
            foreach (var mf in rb.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 l = rb.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!has) { b = new Bounds(l, Vector3.zero); has = true; } else b.Encapsulate(l);
                }
            }
            if (!has) b = new Bounds(Vector3.zero, Vector3.one * 0.4f);
            return b;
        }
    }
}
