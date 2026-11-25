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
        int physStepsLeft = 1;               // сколько шагов физики осталось в этом кадре (с текущим)

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
        ///   физика шагает фиксированным шагом physicsDt (Physics.Simulate). Фактическое место — обратно в тело.
        /// · Тело в физике сталкивается с предметами и стенами (шаг 4: стены — физика, скольжение вдоль — без трения);
        ///   рельеф, ступени, платформы — пока логика.
        ///   Кто кого — массы при касании (bodyMass против массы предмета).
        /// · Всегда: на опоре, на платформе / карусели (6.1), в падении (6.2), в висе на кромке и камне (6.3).</summary>
        [Tooltip("Масса тела в физике (кг) — сколько оно «весит» для предметов при касании.")]
        public float bodyMass = 5f;
        [Tooltip("Тело, вошедшее в стену (угол кубика на повороте на месте), выталкивается не быстрее этого (м/с). Шаг 5: поворот у стены не отменяется — физика отодвигает тело.")]
        public float bodyDepenSpeed = 2f;

        public static Collider BodyCollider { get; private set; }
        Rigidbody bodyRb; BoxCollider bodyBox; int ignoredColliders = -1, ignoredWallVersion = -1;

        /// <summary>Тело в физике всегда (шаг 6): на опоре (земля, платформа, карусель — ход опоры кладёт логика в желаемое место),
        /// в падении (горизонталь — к месту, что считает блок Fall) и в висе на кромке / камне (высота — climbLift логики).
        /// Стены и предметы решает физика; рельеф, ступени, высота — логика.</summary>
        bool PhysBodyActive => itemPhysics && bodyRb;

        [Tooltip("Шаг физики (с) — ФИКСИРОВАННЫЙ (автор 2026-11-24: «движение должно быть в fixed update — вдавливал предмет вниз, кубик в руке подрагивал»): " +
                 "логика героя — покадрово, физика — каждый кадр, кадр делится на равные шаги примерно этой длины.")]
        public float physicsDt = 0.005f;

        /// <summary>Шаг героя = шаг логики (dt кадра) + накопленные ФИКСИРОВАННЫЕ шаги физики (physicsDt): контакты (предмет
        /// в опору, тело в стену) решаются одним и тем же шагом — не дрожат от того, что длина кадра всё время разная.</summary>
        public void Tick(float dt)
        {
            TickLogic(dt);
            if (!itemPhysics || dt <= 0f) return;
            // КАЖДЫЙ КАДР — РАВНЫЕ ШАГИ ≈ physicsDt (автор 2026-11-24: «в руке начинает дёргаться, без преград»): накопление ровных шагов
            // давало кадры без шага физики (0 шагов, потом 2) — предмет в руке стоял и прыгал, интерполяции в Script-режиме нет.
            // Теперь кадр делится на n равных шагов: шаг почти постоянный (контакт не дрожит), и движение — в каждом кадре.
            int n = Mathf.Clamp(Mathf.RoundToInt(dt / Mathf.Max(0.001f, physicsDt)), 1, 40);
            float h = dt / n;
            // ход предмета в руке — РАВНОМЕРНО по шагам кадра (автор 2026-11-24: «башня улетает»): цель руки ставится раз в кадр;
            // раньше предмет проходил весь ход кадра за первый шаг и стоял на втором — скорость скакала 1 ↔ 0 м/с каждые 5 мс,
            // то, что лежит сверху, проскальзывало и слетало. Теперь скорость = остаток хода / остаток времени кадра.
            for (int i = 0; i < n; i++) { physStepsLeft = n - i; PhysicsStep(h); }
            physStepsLeft = 1;
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
                // стена — скользкая для тела (трение — правило логики wallFriction не нужно: тело скользит вдоль, как раньше)
                bodyBox.material = new PhysicsMaterial("Тело") { dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
                bodyRb.position = go.transform.position = BodyTarget(out _);
            }
            IgnoreWorld();
            bodyRb.mass = Mathf.Max(0.1f, bodyMass);
            bodyRb.maxDepenetrationVelocity = bodyDepenSpeed;                      // угол на повороте вошёл в стену — отодвинуть быстро
            Vector3 target = BodyTarget(out Quaternion rot);
            bodyRb.rotation = rot;                                                   // поворот ставит логика
            Vector3 cur = bodyRb.position;
            if (!PhysBodyActive || Flat(target - cur).magnitude > 0.3f)             // не в физике или телепорт — встать на место
            {
                bodyRb.position = target; bodyRb.linearVelocity = Vector3.zero;
                CarryPhysicsStep(dt);
                Physics.Simulate(dt);
                return;
            }
            bodyRb.position = new Vector3(cur.x, target.y, cur.z);                   // высоту ставит логика
            // БЕЗ ИНЕРЦИИ И БЕЗ «ТЯЖЕСТИ» (автор 2026-11-23: «инерция плохо — убери»; «тяну себя с большим усилием, после остановки
            // инерция»): скорость тела каждый шаг — ровно до желаемого места, ничего не копится. Кто кого с предметом — массы при касании.
            Vector3 vel = Flat(target - cur) / dt;
            // УПОР В СТЕНУ ПОЧТИ В ЛОБ — ХОД ВДОЛЬ СТЕНЫ К РУКЕ (то же правило, что было в Slide, теперь для стены в физике):
            // проба хода телом (SweepTest) упёрлась в стену, «в стену» больше, чем вдоль, — скорость поворачивает вдоль стены к кисти
            float vm = vel.magnitude;
            if (vm * dt > 1e-5f && bodyRb.SweepTest(vel / vm, out var wh, vm * dt + 0.01f, QueryTriggerInteraction.Ignore) && IsWallCollider(wh.collider))
            {
                Vector3 n = Flat(wh.normal); float into = n.sqrMagnitude > 1e-6f ? Vector3.Dot(vel, n.normalized) : 0f;
                if (into < 0f)
                {
                    n.Normalize();
                    Vector3 along = vel - n * into, side = SteerSide(Flat(cur));
                    if (along.magnitude < -into && side.sqrMagnitude > 1e-8f)
                    {
                        Vector3 tg = Vector3.Cross(Vector3.up, n);
                        if (Vector3.Dot(tg, side) < 0f) tg = -tg;
                        vel = tg * vm;
                    }
                }
            }
            bodyRb.linearVelocity = vel;
            CarryPhysicsStep(dt);
            Physics.Simulate(dt);
            Vector3 got = bodyRb.position;
            // ИНЕРЦИЯ НЕ ВОЗИТ В СТЕНУ (автор 2026-11-23: «опять сломалось скольжение»): стен в физике тела нет — по инерции тело
            // доезжало в столб. Куда логика не пускает (Blocked) — скольжение от разрешённого места, скорость — по факту хода.
            Vector3 res = Flat(got);
            if (!falling && Blocked(res, bodyRadius))                               // в полёте «обрыв» под телом — не преграда (Fall сам)
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
            float bottom = falling ? fallY : groundY + climbLift;                    // в полёте опоры нет — низ тела там, где оно летит
            float top = bodyCube.position.y + Mathf.Abs(bodyCube.lossyScale.y) * 0.5f;
            float hgt = Mathf.Max(0.05f, top - bottom);
            if (bodyBox) bodyBox.size = new Vector3(half.x * 2f, hgt, half.y * 2f);
            rot = Quaternion.Euler(0f, bodyCube.eulerAngles.y, 0f);
            return new Vector3(Body.x, bottom + hgt * 0.5f, Body.z);
        }

        /// <summary>Стена (препятствие арены): коллайдер под obstaclesRoot.</summary>
        bool IsWallCollider(Collider c) => c && obstaclesRoot && c.transform.IsChildOf(obstaclesRoot) && !IsLowWallTop(c.bounds.max.y);   // не выше опоры + climbUp — рельеф (логика)

        /// <summary>Тело в физике сталкивается с предметами и СТЕНАМИ (шаг 4 плана, 2026-11-23); рельеф, ступени, платформы — логика.</summary>
        void IgnoreWorld()
        {
            var all = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all.Length == ignoredColliders && wallClassVersion == ignoredWallVersion) return;   // стена стала рельефом (или обратно) — пересчёт
            ignoredColliders = all.Length; ignoredWallVersion = wallClassVersion;
            foreach (var c in all)
            {
                if (!c || c == bodyBox) continue;
                var rb = c.attachedRigidbody;
                if (rb && rb.GetComponent<ProtoItem>()) continue;
                Physics.IgnoreCollision(bodyBox, c, !IsWallCollider(c));
            }
        }
    }
}
