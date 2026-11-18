using System;
using System.Collections.Generic;
using Fortress.Grid;
using Fortress.Rig.V3;
using UnityEngine;
using UnityEngine.AI;

namespace Fortress.Guards
{
    [Serializable]
    public class GuardPoint
    {
        public Vector3 pos;
        public float wait;
        [Tooltip("N/E/S/W — куда смотреть на стоянке")] public string look;
        public bool sit;
        [Tooltip("1 — каждый обход, 2 — через раз")] public int every = 1;
        public string act;
        [Header("Сиденье поста (человек рига v3 садится)")]
        public bool hasSeat;
        [Tooltip("Верх сиденья, середина")] public Vector3 seatPos;
        [Tooltip("Куда смотреть сидя")] public Vector3 seatFace;
    }

    /// <summary>Страж: обход по точкам guards.txt на навмеше (стража ходит только полом, лестницами, дверями и арками).
    /// Двери на пути открывает сам и закрывает за собой; засовы, решётки, ворота, лазы, воду и стремянки не берёт —
    /// ими игрок рвёт погоню. Спящий лежит на своей кровати. Зрение, слух и погоня — следующая итерация.
    /// Страж — человек рига v3 (HumanNPC): навмеш только прокладывает путь (агент не двигает тело), идёт человек своими клипами:
    /// держит курс на ближайший угол пути и останавливается клипом в конце; на посту поворачивается, куда смотреть.</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class GuardPatrol : MonoBehaviour
    {
        public string guardId;
        public string role;
        public float walkSpeed = 1.5f;
        public float runSpeed = 5.8f;
        [Tooltip("true — петля, false — туда и обратно")] public bool loop = true;
        public List<GuardPoint> points = new List<GuardPoint>();
        public bool sleeping;
        public Transform body;
        [Header("Кровать спящего (человек рига v3)")]
        public Vector3 bedCenter, bedAlong = Vector3.forward;
        public float bedTop;

        HumanNPC npc;
        bool sitAsked, rising;

        NavMeshAgent agent;
        int idx, dir = 1, cycle;
        float waitLeft;
        bool waiting;
        readonly Collider[] buf = new Collider[24];
        readonly List<DoorSwing> openedByMe = new List<DoorSwing>();

        public string State { get; private set; } = "";

        void Start()
        {
            agent = GetComponent<NavMeshAgent>();
            npc = GetComponentInChildren<HumanNPC>();
            if (npc)
            {
                npc.walkSpeed = walkSpeed;
                npc.runSpeed = Mathf.Clamp(runSpeed, 3f, 5f);
                if (sleeping) npc.Sleep(bedCenter, bedAlong, bedTop);
            }
            if (sleeping || points.Count == 0)
            {
                agent.enabled = false;
                State = sleeping ? "спит" : "на посту";
                return;
            }
            if (npc) { agent.updatePosition = false; agent.updateRotation = false; }
            agent.speed = walkSpeed;
            agent.angularSpeed = 540f; agent.acceleration = 10f; agent.stoppingDistance = 0.3f;
            agent.autoTraverseOffMeshLink = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            GoTo(0);
        }

        void GoTo(int i)
        {
            idx = i;
            waiting = false;
            if (!agent.isOnNavMesh) return;
            var pt = points[i];
            // человек садится на стул поста: подойти к месту перед сиденьем (спиной к нему клип посадки донесёт таз на сиденье)
            Vector3 target = npc && pt.sit && pt.hasSeat ? new Vector3(pt.seatPos.x, pt.pos.y, pt.seatPos.z) + pt.seatFace * 0.42f : pt.pos;
            if (NavMesh.SamplePosition(target, out var hit, 1.3f, NavMesh.AllAreas)) target = hit.position;
            agent.SetDestination(target);
            State = "обход";
        }

        int NextIndex()
        {
            int n = idx;
            for (int guard = 0; guard < points.Count * 3 + 3; guard++)
            {
                n += dir;
                if (loop)
                {
                    if (n >= points.Count) { n = 0; cycle++; }
                }
                else if (points.Count > 1)
                {
                    if (n >= points.Count) { dir = -1; n = points.Count - 2; }
                    else if (n < 0) { dir = 1; n = 1; cycle++; }
                }
                else n = 0;
                var p = points[n];
                if (p.every <= 1 || cycle % p.every == 0) return n;
            }
            return idx;
        }

        static Vector3 LookDir(string look) => look switch { "N" => Vector3.forward, "E" => Vector3.right, "S" => Vector3.back, _ => Vector3.left };

        void Update()
        {
            if (sleeping || agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            OpenDoorsAhead();
            CloseDoorsBehind();
            if (npc) { NpcUpdate(); return; }
            var p = points[idx];
            if (waiting)
            {
                waitLeft -= Time.deltaTime;
                if (!string.IsNullOrEmpty(p.look))
                {
                    Vector3 d = p.look switch { "N" => Vector3.forward, "E" => Vector3.right, "S" => Vector3.back, _ => Vector3.left };
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 300f * Time.deltaTime);
                }
                if (body) body.localPosition = Vector3.Lerp(body.localPosition, p.sit ? new Vector3(0, -0.45f, 0) : Vector3.zero, 6f * Time.deltaTime);
                if (waitLeft <= 0f)
                {
                    if (body) body.localPosition = Vector3.zero;
                    GoTo(NextIndex());
                }
                return;
            }
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
            {
                waiting = true; waitLeft = p.wait;
                State = p.sit ? "сидит на посту" : (p.wait > 0 ? "стоит" : "обход");
            }
        }

        /// <summary>Человек рига v3: навмеш даёт путь, человек идёт клипами. Агент держится там, где тело.</summary>
        void NpcUpdate()
        {
            agent.nextPosition = transform.position;
            var p = points[idx];
            if (waiting)
            {
                npc.ClearPath();
                bool seat = p.sit && p.hasSeat;
                npc.face = seat || string.IsNullOrEmpty(p.look) ? (Vector3?)null : LookDir(p.look);
                // сидит — время ожидания идёт с момента, как сел; пора — встать, а в путь — когда встал
                if (seat && !npc.Seated && !npc.OnSeat && !rising && waitLeft == p.wait) { if (!sitAsked) { npc.SitOn(p.seatPos, p.seatFace); sitAsked = true; } return; }
                if (seat && npc.Seated) State = "сидит на посту";
                waitLeft -= Time.deltaTime;
                if (waitLeft > 0f) return;
                if (seat && npc.OnSeat) { if (!rising) { npc.StandUp(); rising = true; } return; }
                if (npc.Standing) { rising = false; sitAsked = false; GoTo(NextIndex()); }
                return;
            }
            npc.face = null;
            if (agent.pathPending) return;
            float rem = agent.remainingDistance;
            if (float.IsInfinity(rem) || float.IsNaN(rem)) rem = Vector3.Distance(transform.position, agent.destination);
            // проходная точка (без ожидания и взгляда) — не останавливаться, сразу курс на следующую
            if (p.wait <= 0f && string.IsNullOrEmpty(p.look) && !p.sit && rem < 1.8f && points.Count > 1) { GoTo(NextIndex()); return; }
            npc.SetPath(agent.steeringTarget, agent.destination, rem);
            // дошёл: путь кончился и человек встал
            if (rem <= 0.45f && npc.Standing)
            {
                waiting = true; waitLeft = p.wait;
                State = p.sit ? "сидит на посту" : (p.wait > 0 ? "стоит" : "обход");
            }
        }

        /// <summary>Закрытая дверь впереди по ходу (до ~1.5 м): открыть от себя.</summary>
        void OpenDoorsAhead()
        {
            Vector3 fwd = agent.velocity.sqrMagnitude > 0.04f ? agent.velocity.normalized : transform.forward;
            Vector3 c = transform.position + Vector3.up * 1.0f + fwd * 0.9f;
            int n = Physics.OverlapSphereNonAlloc(c, 0.8f, buf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var door = buf[i].GetComponentInParent<DoorSwing>();
                if (!door || door.IsOpen || !door.Passable) continue;
                door.OpenAwayFrom(transform.position);
                if (!openedByMe.Contains(door)) openedByMe.Add(door);
            }
        }

        /// <summary>Закрыть за собой двери, которые открыл сам, когда отошёл.</summary>
        void CloseDoorsBehind()
        {
            for (int i = openedByMe.Count - 1; i >= 0; i--)
            {
                var d = openedByMe[i];
                if (!d) { openedByMe.RemoveAt(i); continue; }
                if ((d.Anchor - transform.position).sqrMagnitude > 2.6f * 2.6f)
                {
                    if (d.IsOpen) d.Close();
                    openedByMe.RemoveAt(i);
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f);
            for (int i = 0; i < points.Count; i++)
            {
                Gizmos.DrawWireSphere(points[i].pos + Vector3.up * 0.2f, 0.25f);
                if (i + 1 < points.Count) Gizmos.DrawLine(points[i].pos + Vector3.up * 0.2f, points[i + 1].pos + Vector3.up * 0.2f);
            }
            if (loop && points.Count > 2) Gizmos.DrawLine(points[^1].pos + Vector3.up * 0.2f, points[0].pos + Vector3.up * 0.2f);
        }
    }
}
