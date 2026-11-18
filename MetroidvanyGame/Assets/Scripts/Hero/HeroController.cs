using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Ядро героя: общее состояние («доска») + конвейер блоков.
    /// Механика управления собрана из отдельных блоков (Scripts/Hero/Blocks), каждый — один компонент с одной задачей:
    ///   0 HeroInput     мышь/скрипт → луч, ЛКМ, протяжка, клик, петля
    ///  10 HeroPosture   сидя / ничком (сам под низким потолком)
    ///  20 HeroReach     куда тянется свободная рука (курсор, автонаведение, «под/над», плечо, плоскости)
    ///  30 HeroGrip      хват: начать / держать / отпустить (автозацеп r, буфер, койот) + жест при хвате
    ///  38 HeroObjects   предметы: держать ящик, толчок, взять / бросить / положить / на голову
    ///  40 HeroPull      тело от жеста при хвате (тяга, отталкивание, вдоль стены, разворот, подъём)
    ///  50 HeroAutoReach перехват: рука согнута до упора — сама переставляется по жесту
    ///  60 HeroTurn      разворот кликом по земле
    ///  70 HeroCollide   голова/грудь не входят в предметы
    ///  80 HeroMotor     движение капсулы + гравитация
    ///  85 HeroHook      зацеп предмета плечом+головой (LOCK)
    ///  90 HeroFeed      вход позы тела (HeroBody): рука, взгляд, усилие
    /// Блоки общаются только через поля ядра; любой можно выключить (enabled = false) и проверить отдельно.</summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(100)]
    public class HeroController : MonoBehaviour
    {
        public HeroBody body;
        public Camera cam;

        [Header("Общее")]
        public float reachFrac = 0.97f;
        [Tooltip("Дополнительная досягаемость за счёт наклона сидя (м).")]
        public float leanReach = 0.28f;
        public float maxSpeedSit = 0.55f, maxSpeedProne = 0.85f;
        [Tooltip("Масса тела героя (кг): толчки, тяга предметов.")]
        public float bodyMass = 45f;
        [Tooltip("r — радиус автозацепа к выступам и поверхностям (м).")]
        public float grabRadius = 0.15f;

        [Header("Ввод из скрипта (тесты, катсцены): вместо мыши")]
        public bool scripted;
        public Vector3 scriptPoint;
        public bool scriptHold;
        public bool scriptToggleProne;
        [Tooltip("Однократно: петля мышью (+1 по часовой, −1 против).")]
        public int scriptLoop;
        [Tooltip("Писать события в консоль.")]
        public bool logEvents;

        public enum Mode { Free, Grip, Carry }

        // ---------- доска: ввод (HeroInput)
        [System.NonSerialized] public Ray ray;
        [System.NonSerialized] public bool hit;
        [System.NonSerialized] public RaycastHit rh;
        [System.NonSerialized] public bool hold, down, up, click, orbiting, toggleProne;
        [System.NonSerialized] public Vector3 dragW;
        [System.NonSerialized] public int loop;
        [System.NonSerialized] public float pressT = -9f;
        [System.NonSerialized] public bool pressHit;
        [System.NonSerialized] public Vector3 pressPoint;
        // ---------- поза
        [System.NonSerialized] public float prone;
        // ---------- рука
        [System.NonSerialized] public Mode mode;
        [System.NonSerialized] public Vector3 handPos, handVelS;
        [System.NonSerialized] public bool contact;
        [System.NonSerialized] public Vector3 contactPoint, contactN = Vector3.up;
        [System.NonSerialized] public float lastContactT = -9f;
        [System.NonSerialized] public bool preferUnder;
        [System.NonSerialized] public Vector3? reachTo;
        [System.NonSerialized] public Vector3 reachToN = Vector3.up;
        [System.NonSerialized] public float reachUntil;
        // ---------- хват и жест при хвате
        [System.NonSerialized] public Transform anchorT;
        [System.NonSerialized] public Vector3 anchorLocal, anchorN = Vector3.up;
        [System.NonSerialized] public Rigidbody anchorRb;
        [System.NonSerialized] public Vector3 dirA, sideA;
        [System.NonSerialized] public float along, side, wantAlong, swipe;
        // ---------- предметы
        [System.NonSerialized] public Rigidbody held, carried, onHead, hooked;
        // ---------- тело
        [System.NonSerialized] public Vector3 move, moved;
        [System.NonSerialized] public bool climbing;
        [System.NonSerialized] public float strain, pullSpeed, load;
        [System.NonSerialized] public float? turnTo;
        // ---------- производные (каждый кадр)
        [System.NonSerialized] public Vector3 S;
        [System.NonSerialized] public float reach, reachMax, vmaxBody, dt;

        public CharacterController CC { get; private set; }
        public Mode State => mode;
        public bool Grabbing => mode == Mode.Grip;
        public float Prone => prone;
        public Rigidbody Carried => carried;
        public Rigidbody OnHead => onHead;
        public Rigidbody Hooked => hooked;
        public Vector3 Anchor => anchorT ? anchorT.TransformPoint(anchorLocal) : anchorLocal;

        readonly List<HeroBlock> blocks = new List<HeroBlock>();

        void Awake()
        {
            CC = GetComponent<CharacterController>();
            if (!body) body = GetComponentInChildren<HeroBody>();
            Application.runInBackground = true;
            Ensure<HeroInput>(); Ensure<HeroPosture>(); Ensure<HeroReach>(); Ensure<HeroGrip>(); Ensure<HeroObjects>();
            Ensure<HeroPull>(); Ensure<HeroAutoReach>(); Ensure<HeroTurn>(); Ensure<HeroCollide>(); Ensure<HeroMotor>();
            Ensure<HeroHook>(); Ensure<HeroFeed>();
            blocks.AddRange(GetComponents<HeroBlock>());
            blocks.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        void Ensure<T>() where T : HeroBlock { if (!GetComponent<T>()) gameObject.AddComponent<T>(); }

        void Start()
        {
            if (!cam) cam = Camera.main;
            handPos = body.Shoulder + transform.forward * 0.25f - transform.up * 0.4f;
        }

        void Update()
        {
            dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (!cam || !body || !body.Ready) return;
            move = Vector3.zero; climbing = false; strain = 0f; pullSpeed = 0f; load = 0f; loop = 0;
            along = side = wantAlong = swipe = 0f;
            S = body.Shoulder;
            reach = body.ArmLength * reachFrac;
            reachMax = reach + leanReach * (1f - prone);
            vmaxBody = Mathf.Lerp(maxSpeedSit, maxSpeedProne, prone);
            foreach (var b in blocks) if (b.enabled) b.Tick(this, dt);
        }

        // ================= общие инструменты блоков =================
        public bool Mine(Transform t) => t.IsChildOf(transform);

        public void Log(string msg) { if (logEvents) Debug.Log("[Hero] " + msg, this); }

        /// <summary>Первое попадание луча мимо своего тела и ноши; under — первое попадание ниже первой поверхности.</summary>
        public bool RayWorld(Ray r, out RaycastHit best, bool under = false)
        {
            best = default;
            var hits = new List<RaycastHit>();
            foreach (var h in Physics.RaycastAll(r, 50f, ~0, QueryTriggerInteraction.Ignore))
                if (!Mine(h.transform) && !(carried && h.rigidbody == carried)) hits.Add(h);
            if (hits.Count == 0) return false;
            hits.Sort((a, b) => a.distance.CompareTo(b.distance));
            best = hits[0];
            if (under)
                foreach (var h in hits)
                    if (h.point.y < hits[0].point.y - 0.1f) { best = h; break; }
            return true;
        }

        public float GroundY()
        {
            var o = transform.position + Vector3.up * 0.3f;
            foreach (var h in Physics.RaycastAll(o, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore))
                if (!Mine(h.transform)) return h.point.y;
            return transform.position.y;
        }

        /// <summary>Автозацеп: ближайшая точка в радиусе r; выступ (брус, перила, край, лёгкий предмет) важнее плоскости.</summary>
        public bool FindGrip(Vector3 p, float r, out Vector3 point, out Vector3 normal, out Collider col)
        {
            point = p; normal = Vector3.up; col = null;
            float bd = float.MaxValue;
            foreach (var c in Physics.OverlapSphere(p, r, ~0, QueryTriggerInteraction.Ignore))
            {
                if (Mine(c.transform) || (carried && c.attachedRigidbody == carried) || (onHead && c.attachedRigidbody == onHead)) continue;
                if (c is MeshCollider mc && !mc.convex)
                {
                    if (Physics.Raycast(p + Vector3.up * r, Vector3.down, out var h, r * 2f) && h.collider == c)
                    { float d0 = Vector3.Distance(p, h.point); if (d0 < bd) { bd = d0; point = h.point; normal = h.normal; col = c; } }
                    continue;
                }
                Vector3 q = c.ClosestPoint(p);
                float dq = Vector3.Distance(p, q);
                var bs = c.bounds.size;
                bool protr = Mathf.Min(bs.x, Mathf.Min(bs.y, bs.z)) < 0.15f || bs.x * bs.y * bs.z < 0.05f;
                float d = dq - (protr ? 0.25f : 0f);
                if (d < bd)
                {
                    bd = d; point = q; col = c;
                    normal = dq > 1e-4f ? (p - q) / dq : Vector3.up;
                }
            }
            return col;
        }

        /// <summary>Рука не проходит сквозь предметы: вектор «плечо→цель» отсекается плоскостью первой поверхности (до 3 плоскостей).</summary>
        public Vector3 ClipByPlanes(Vector3 from, Vector3 want)
        {
            const float palm = 0.03f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 v = want - from;
                float len = v.magnitude;
                if (len < 1e-4f) break;
                RaycastHit best = default; bool any = false; float bd = float.MaxValue;
                foreach (var h in Physics.SphereCastAll(from, 0.035f, v / len, len, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (Mine(h.transform) || (carried && h.rigidbody == carried) || h.distance <= 0f) continue;
                    if (h.distance < bd) { bd = h.distance; best = h; any = true; }
                }
                if (!any) break;
                float depth = Vector3.Dot(want - best.point, best.normal);
                if (depth >= palm) break;
                want += best.normal * (palm - depth);
            }
            return want;
        }

        /// <summary>Своё тело (капсула) не сталкивается с предметом в руке / на голове / зацепленным.</summary>
        public void IgnoreBody(Rigidbody rb, bool ignore)
        {
            if (!rb) return;
            foreach (var c in rb.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(CC, c, ignore);
        }

        public void BeginGrip(Vector3 point, Vector3 normal, Collider col)
        {
            var rb = col ? col.attachedRigidbody : null;
            if (rb && rb == onHead) return;
            var obj = GetComponent<HeroObjects>();
            if (rb && obj && obj.enabled && obj.TryTake(this, rb)) return;   // лёгкий — в руку (Carry)
            mode = Mode.Grip;
            anchorRb = rb;
            anchorT = rb ? rb.transform : (col ? col.transform : null);
            anchorLocal = anchorT ? anchorT.InverseTransformPoint(point) : point;
            anchorN = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            reachTo = null;
            Log($"хват {(col ? col.name : "поверхность")} в {point}");
            if (rb && obj && obj.enabled) obj.TryHold(this, rb);              // подвижный — держать рукой
        }

        public void EndGrip()
        {
            var obj = GetComponent<HeroObjects>();
            if (obj) obj.ReleaseHeld(this);
            mode = Mode.Free; anchorRb = null; anchorT = null;
        }
    }

    /// <summary>Блок механики героя: одна задача, читает/пишет поля ядра.</summary>
    public abstract class HeroBlock : MonoBehaviour
    {
        public abstract int Order { get; }
        public abstract void Tick(HeroController h, float dt);
    }
}
