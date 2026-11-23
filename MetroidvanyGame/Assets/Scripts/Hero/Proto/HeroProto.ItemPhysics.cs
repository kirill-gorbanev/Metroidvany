using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ФИЗИКА ПРЕДМЕТОВ — МИР И ТЕЛО ГЕРОЯ (переделка 2026-11-22: предметы целиком на физике Unity, «позиции» — только у тела).
    ///
    /// · Мир для предметов — неподвижные коллайдеры (EnsureItemWorld, один раз): рельеф и стены (по сеткам), пол y = 0.
    ///   Логику героя они не трогают: герой физику Unity не использует.
    /// · Тело героя — Rigidbody (PhysicsStep): предметы для тела решает физика — массы при касании.
    /// · Падение, стопки, «вытащить снизу», наклон — сама физика (ProtoItem).</summary>
    public partial class HeroProto
    {
        [Header("Физика предметов")]
        [Tooltip("Предметы — физические тела Unity: рука ведёт силой, тело мягко толкает лёгкие.")]
        public bool itemPhysics = true;

        bool itemWorldReady;

        /// <summary>Неподвижные коллайдеры мира — один раз: рельеф и стены (по сеткам, все зоны), пол (y = 0).</summary>
        public void EnsureItemWorld()
        {
            if (itemWorldReady) return;
            itemWorldReady = true;
            foreach (var root in new[] { surfaceRoot, obstaclesRoot })
            {
                if (!root) continue;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh && !mf.GetComponent<Collider>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            var floor = new GameObject("Пол (физика предметов)").AddComponent<BoxCollider>();
            floor.center = new Vector3(0f, -0.5f, 0f); floor.size = new Vector3(4000f, 1f, 4000f);
        }

        /// <summary>ТЕЛО — RIGIDBODY (шаг 2 перевода тела на физику, 2026-11-23; автор: «если бы ты использовал rigidbody для персонажа —
        /// мог бы за один шаг исправить»: зажатый предмет трясся, не разворачивался к стене).
        /// · Логика героя (рука, линия хода, стены, ступени) как была — она ставит ЖЕЛАЕМОЕ место тела (воронка MoveBodyXZ).
        /// · После шага логики тело-Rigidbody едет к желаемому месту ровно за шаг (без инерции — автор: «инерция плохо»);
        ///   физика шагает вместе с героем (Physics.Simulate, тот же dt). Фактическое место — обратно в тело.
        /// · Тело в физике сталкивается ТОЛЬКО с предметами: стены, рельеф, платформы — пока логика (шаг 4 плана).
        ///   Кто кого — массы при касании (bodyMass против массы предмета).
        /// · Только на земле без платформы: падение, вис на кромке, езда на платформе — тело ставит логика, физика за ней (шаг 6).</summary>
        [Tooltip("Масса тела в физике (кг) — сколько оно «весит» для предметов при касании.")]
        public float bodyMass = 5f;

        public static Collider BodyCollider { get; private set; }
        Rigidbody bodyRb; BoxCollider bodyBox; int ignoredColliders = -1;

        /// <summary>Тело сейчас в физике (на земле, не падает, не висит, не едет на платформе).</summary>
        bool PhysBodyActive => itemPhysics && bodyRb && !falling && climbLift < 0.01f && !rideMover;

        /// <summary>Шаг героя = шаг логики + шаг физики (тот же dt).</summary>
        public void Tick(float dt)
        {
            TickLogic(dt);
            PhysicsStep(dt);
        }

        void PhysicsStep(float dt)
        {
            if (!itemPhysics || !bodyCube || dt <= 0f) return;
            if (!bodyRb)
            {
                EnsureItemWorld();
                Physics.simulationMode = SimulationMode.Script;                     // физика шагает вместе с героем
                var go = new GameObject("Тело (Rigidbody)");
                bodyRb = go.AddComponent<Rigidbody>();
                bodyRb.useGravity = false;
                bodyRb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
                bodyRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                bodyRb.maxDepenetrationVelocity = 0.5f;
                bodyBox = go.AddComponent<BoxCollider>(); BodyCollider = bodyBox;
                bodyRb.position = go.transform.position = BodyTarget(out _);
            }
            IgnoreWorld();
            bodyRb.mass = Mathf.Max(0.1f, bodyMass);
            Vector3 target = BodyTarget(out Quaternion rot);
            bodyRb.rotation = rot;                                                   // поворот ставит логика
            Vector3 cur = bodyRb.position;
            if (!PhysBodyActive || Flat(target - cur).magnitude > 0.3f)             // не в физике или телепорт — встать на место
            {
                bodyRb.position = target; bodyRb.linearVelocity = Vector3.zero;
                Physics.Simulate(dt);
                return;
            }
            bodyRb.position = new Vector3(cur.x, target.y, cur.z);                   // высоту ставит логика
            // БЕЗ ИНЕРЦИИ И БЕЗ «ТЯЖЕСТИ» (автор 2026-11-23: «инерция плохо — убери»; «тяну себя с большим усилием, после остановки
            // инерция»): скорость тела каждый шаг — ровно до желаемого места, ничего не копится. Кто кого с предметом — массы при касании.
            bodyRb.linearVelocity = Flat(target - cur) / dt;
            Physics.Simulate(dt);
            Vector3 got = bodyRb.position;
            // ИНЕРЦИЯ НЕ ВОЗИТ В СТЕНУ (автор 2026-11-23: «опять сломалось скольжение»): стен в физике тела нет — по инерции тело
            // доезжало в столб. Куда логика не пускает (Blocked) — скольжение от разрешённого места, скорость — по факту хода.
            Vector3 res = Flat(got);
            if (Blocked(res, bodyRadius))
            {
                res = Slide(Body, res - Body);
                got = new Vector3(res.x, got.y, res.z);
                bodyRb.position = got;
                bodyRb.linearVelocity = Flat(got - cur) / dt;
            }
            if (Flat(got - target).sqrMagnitude > 1e-8f)
            {
                MoveBodyXZ(got.x, got.z);                                            // где тело на самом деле
                if (mode == Mode.Grip) { float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0); }   // ход — от фактического места
            }
        }

        /// <summary>Желаемое место коллайдера тела: след кубика, от земли до верха кубика, поворот как у тела.</summary>
        Vector3 BodyTarget(out Quaternion rot)
        {
            Vector2 half = BodyHalf;
            float bottom = groundY + climbLift, top = bodyCube.position.y + Mathf.Abs(bodyCube.lossyScale.y) * 0.5f;
            float hgt = Mathf.Max(0.05f, top - bottom);
            if (bodyBox) bodyBox.size = new Vector3(half.x * 2f, hgt, half.y * 2f);
            rot = Quaternion.Euler(0f, bodyCube.eulerAngles.y, 0f);
            return new Vector3(Body.x, bottom + hgt * 0.5f, Body.z);
        }

        /// <summary>Тело в физике не сталкивается ни с чем, кроме предметов (стены, рельеф, платформы — логика).</summary>
        void IgnoreWorld()
        {
            var all = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all.Length == ignoredColliders) return;
            ignoredColliders = all.Length;
            foreach (var c in all)
            {
                if (!c || c == bodyBox) continue;
                var rb = c.attachedRigidbody;
                if (rb && rb.GetComponent<ProtoItem>()) continue;
                Physics.IgnoreCollision(bodyBox, c, true);
            }
        }
    }
}
