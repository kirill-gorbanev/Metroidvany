using UnityEngine;

namespace Fortress.Hero.Arm2
{
    /// <summary>БЛОК «ТЕЛО» (срез 1). Тело героя — обычный Rigidbody с коробкой 0.3 × 0.2 × 0.3 м.
    /// Правило: тело стоит, скользит, падает ТОЛЬКО физикой (трение, столкновения, центр масс над опорой).
    /// Здесь нет ни одной записи position / rotation / velocity — блок только настраивает Rigidbody.
    /// Урок 11: край — это геометрия, флагов «обрыв = стена» нет. Урок 12: скриптовой высоты тела нет.
    /// Урок 15: интерполяция включена, шаг физики мелкий (Arm2Hero.physicsDt).
    /// Привод тела (один способ — силы): реакция сустава руки и момент рыскания — придут в срезах 3 и 4.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Arm2Body : MonoBehaviour, IArm2Tunable
    {
        // ТЕЛО — КАПСУЛА вдоль «вперёд» (радиус 0.1, длина 0.3) с низким центром масс — «неваляшка». Автор 2026-11-25: «мне не нравится, как он
        // сильно переворачивается — если центр масс опустить и сделать капсулу, а не куб, он может стать неваляшкой». Коробка, упав на
        // бок, так и лежала (по записи автора — наклон 90° все 2 с); у круглого тела с центром масс ниже оси устойчивое положение одно.
        // Одна капсула вставала сама, но качалась от взмахов свободной руки (замер: сдвиг тела 1–2.5 см, курс до 13°). Поэтому сечение —
        // три капсулы: две нижние рядом (широкое устойчивое дно, две линии опоры) и одна верхняя по центру (круглый верх: вверх дном и
        // на боку опора — одна линия, тело скатывается обратно). Габарит прежний: 0.3 × 0.2 × 0.3.
        public const float Width = 0.3f, Height = 0.2f, Length = 0.3f, Radius = 0.085f, SideX = 0.065f, LowY = -0.015f, TopY = 0.015f;

        [Arm2Tune("масса тела, кг", 5f, 80f)]
        [Tooltip("Тело тяжёлое, рука лёгкая: взмах свободной руки не должен сдвигать и крутить тело (замер среза 2: при 10 кг тело крутило до 180°).")]
        public float mass = 40f;
        [Arm2Tune("центр масс ниже оси тела, м", 0f, 0.095f)]
        [Tooltip("Насколько центр масс ниже середины коробки: ниже — устойчивее на склоне и у края.")]
        public float comDrop = 0.09f;
        [Arm2Tune("трение тела", 0.1f, 1.5f)]
        [Tooltip("Трение тела о поверхность. С Physics.improvedPatchFriction (ставит Arm2Hero) трение коробки совпадает с формулой: " +
                 "0.74 — замером стоит на 32°, сползает на 36° (пороги автора: до 32° стоит, от 35° сползает); 0.68 сползало уже с 32°. " +
                 "Без этой настройки PhysX держал коробку вдвое круче формулы (0.66 — до 51°).")]
        public float friction = 0.74f;
        [Arm2Tune("гашение вращения", 0f, 10f)]
        public float angularDamping = 5f;
        [Arm2Tune("гашение хода", 0f, 5f)]
        public float linearDamping = 0.05f;
        [Arm2Tune("итерации решателя", 4f, 40f)]
        public float solverIterations = 12f;

        public Arm2Hero hero;
        [Header("Курс тела при хвате")]
        [Arm2Tune("курс: жёсткость, Н·м/рад", 0f, 3000f)]
        public float yawStiffness = 1500f;
        [Arm2Tune("курс: демпфер", 0f, 200f)]
        public float yawDamping = 80f;
        [Arm2Tune("курс: предел момента, Н·м", 0f, 400f)]
        public float yawTorque = 250f;
        [Arm2Tune("курс: помощь против трения, Н·м", 0f, 150f)]
        [Tooltip("Трение тела о пол держит курс моментом ~30 Н·м, плюс рука при повороте тащит тело вбок: без помощи малые повороты недобирали треть.")]
        public float yawFrictionHelp = 60f;

        [Header("Ровное тело при подъёме на ступень")]
        [Arm2Tune("подъём: держать ровно, Н·м/рад", 0f, 3000f)]
        public float levelStiffness = 1200f;
        [Arm2Tune("подъём: демпфер наклона", 0f, 200f)]
        public float levelDamping = 60f;
        [Arm2Tune("подъём: предел момента, Н·м", 0f, 500f)]
        public float levelTorque = 250f;

        [Header("Сам встаёт (без хвата)")]
        [Arm2Tune("встать: жёсткость, Н·м/рад", 0f, 1500f)]
        public float rightStiffness = 300f;
        [Arm2Tune("встать: демпфер", 0f, 100f)]
        public float rightDamping = 25f;
        [Arm2Tune("встать: предел момента, Н·м", 0f, 300f)]
        public float rightTorque = 90f;

        [Header("Склон: окно перехвата")]
        [Arm2Tune("склон: окно перехвата, с", 0f, 1f)]
        public float holdWindow = 0.45f;
        public float brakeDamping = 400f;
        bool wasGripOnHold; float brake;
        [Arm2Tune("тело: упор от разворота без хвата, Н·м", 0f, 400f)]
        [Tooltip("Без хвата тело на опоре сопротивляется развороту на месте с моментом до этого значения (как трение покоя).")]
        public float yawRest = 150f;
        float yawRef, yawSupYaw; bool hasYawRef; Rigidbody yawSup;
        /// <summary>БЕЗ ХВАТА ТЕЛО НА МЕСТЕ НЕ ПОВОРАЧИВАЕТСЯ. Автор 2026-11-26: «поворачиваюсь на месте с предметом в руке — не
        /// должен, я зафиксирован должен быть»; «без клика ЛКМ, просто из-за столкновения руки — поворот на месте». Привод руки
        /// (до 40 Н на плече 0.6 м — 24 Н·м) упирался в преграду или тащил предмет, а трение тела о пол вокруг вертикали — около
        /// 29 Н·м (капсулы касаются пола у центра): на грани, тело крутило. Здесь — то же трение покоя, но с запасом: момент
        /// вокруг вертикали держит курс, пока его хватает (до yawRest), сверх — курс уступает (не привод: сам тело не крутит).
        /// Опора едет или крутится (платформа, карусель) — курс едет вместе с ней. В воздухе и при хвате — выключено.</summary>
        void YawRest(float dt)
        {
            var bc = hero.arm ? hero.arm.bodyContact : null; bool on = false; Rigidbody sup = null;
            if (bc) for (int i = 0; i < bc.count; i++) if (bc.normals[i].y > 0.5f) { on = true; if (bc.bodies[i]) sup = bc.bodies[i]; }
            if (!on || yawRest <= 0f) { hasYawRef = false; return; }
            float yaw = YawRb(yawRef), supRate = 0f;
            if (!hasYawRef || sup != yawSup) { if (!hasYawRef) yawRef = yaw; hasYawRef = true; yawSup = sup; if (sup) yawSupYaw = sup.rotation.eulerAngles.y; }
            else if (sup) { float py = sup.rotation.eulerAngles.y, d = Mathf.DeltaAngle(yawSupYaw, py); yawRef += d; yawSupYaw = py; supRate = d * Mathf.Deg2Rad / dt; }
            const float k = 4000f, damp = 100f;
            float err = Mathf.DeltaAngle(yaw, yawRef) * Mathf.Deg2Rad, maxErr = yawRest / k;
            if (Mathf.Abs(err) > maxErr) { err = Mathf.Sign(err) * maxErr; yawRef = yaw + err * Mathf.Rad2Deg; }     // сорвало — курс уступает
            float torque = Mathf.Clamp(k * err - damp * (Rb.angularVelocity.y - supRate), -yawRest, yawRest);
            Rb.AddTorque(Vector3.up * torque, ForceMode.Force);
        }
        float platformYaw; bool hasPlatformYaw;
        bool StandsOn(Rigidbody rb)
        {
            var bc = hero.arm ? hero.arm.bodyContact : null; if (!bc) return false;
            for (int i = 0; i < bc.count; i++) if (bc.normals[i].y > 0.3f && bc.bodies[i] == rb) return true;
            return false;
        }
        bool righting; float restTime;
        static readonly RaycastHit[] rayHits = new RaycastHit[8];

        public Rigidbody Rb { get; private set; }
        /// <summary>Главный коллайдер тела (капсула).</summary>
        public CapsuleCollider Box { get; private set; }
        PhysicsMaterial mat;

        void Awake() { Grab(); ApplyTuning(); }

        void Grab()
        {
            if (!Rb) Rb = GetComponent<Rigidbody>();
            if (!Box) Box = GetComponent<CapsuleCollider>();
        }

        public void ApplyTuning()
        {
            Grab();
            Rb.mass = mass;
            Rb.useGravity = true; Rb.isKinematic = false;
            Rb.constraints = RigidbodyConstraints.None;                         // вращение свободно: устоит или опрокинется — решает физика
            Rb.interpolation = RigidbodyInterpolation.Interpolate;              // урок 15: без ступенек на высоком ФПС
            Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Rb.centerOfMass = new Vector3(0f, -comDrop, 0f);
            Rb.linearDamping = linearDamping; Rb.angularDamping = angularDamping;
            Rb.solverIterations = Mathf.RoundToInt(solverIterations); Rb.solverVelocityIterations = 4;
            Rb.maxDepenetrationVelocity = 1f;                                   // выталкивание из наложения — плавно, без выстрела (правило 3)
            if (!mat) mat = new PhysicsMaterial("Arm2 тело") { bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum };
            mat.staticFriction = friction; mat.dynamicFriction = friction * 0.9f;
            mat.frictionCombine = PhysicsMaterialCombine.Maximum;               // порог склона задаёт тело, а не материал пола (у пола по умолчанию 0.6)
            foreach (var col in GetComponents<Collider>()) col.sharedMaterial = mat;   // коробка и сфера-плечо
        }

        /// <summary>Шаг перед физикой. Срез 1: привода нет — тело стоит на трении.</summary>
        public void FixedStep(float dt)
        {
            // УДЕРЖАНИЕ КУРСА ПРИ ХВАТЕ. Рука тянет тело за точку сбоку от центра (плечо в 0.2 м справа) — без удержания тело
            // разворачивалось бы за рукой, как санки за верёвкой. Момент вокруг вертикали (сила, не запись угла) ведёт рыскание
            // к курсу хвата; жест поворота (срез 4) будет менять этот курс. Без хвата момента нет — тело стоит на трении.
            var st = hero ? hero.state : null;
            if (st == null) return;
            if (!st.gripped)
            {
                // ОКНО ПЕРЕХВАТА (срез 7): отпустил зацеп на крутом — тело ещё holdWindow секунд держится на склоне (успеть взять
                // следующий камень), потом сползает по правилу склона. У старого героя окно было 0.2 с при мгновенной кисти
                // (0.5 с автор назвал «слишком долго зависаю»); здесь кисть летит по-настоящему, поэтому 0.35 с.
                // Сила — ровно против сползания вдоль опоры (тяжесть вдоль склона + гашение хода), только пока тело на опоре.
                if (wasGripOnHold) { brake = holdWindow; wasGripOnHold = false; }
                if (brake > 0f)
                {
                    brake -= dt;
                    var bc = hero.arm ? hero.arm.bodyContact : null; Vector3 n = Vector3.zero;
                    if (bc) for (int i = 0; i < bc.count; i++) if (bc.normals[i].y > 0.3f) n += bc.normals[i];
                    if (n.sqrMagnitude > 1e-6f)
                    {
                        n.Normalize();
                        Vector3 gT = Vector3.ProjectOnPlane(Physics.gravity * Rb.mass, n), vT = Vector3.ProjectOnPlane(Rb.linearVelocity, n);
                        Rb.AddForce(Vector3.ClampMagnitude(-gT - vT * brakeDamping, Rb.mass * Physics.gravity.magnitude * 1.5f), ForceMode.Force);
                    }
                }
                YawRest(dt);
                SelfRight(); return;
            }
            hasYawRef = false;
            wasGripOnHold = st.gripOnHold; brake = 0f;
            // ПОДВИЖНАЯ ОПОРА (срез 8): держишься за платформу и стоишь на ней же — курс держится относительно неё (карусель крутит
            // тело вместе с собой, а не выкручивает его против своего хода). Стоишь на полу, а держишься за платформу — курс мировой.
            if (st.gripBody)
            {
                float py = st.gripBody.rotation.eulerAngles.y;
                if (hasPlatformYaw && StandsOn(st.gripBody)) st.yawTarget += Mathf.DeltaAngle(platformYaw, py);
                platformYaw = py; hasPlatformYaw = true;
            }
            else hasPlatformYaw = false;
            // ТЯГА: рука тянет тело вдоль поверхности хвата — сила через центр масс (величину считает блок «Рука»).
            // Отдача — на то, за что держится кисть (платформа, предмет), в точке хвата; у неподвижного мира её некуда прикладывать.
            if (st.pullVec.sqrMagnitude > 1e-6f)
            {
                Rb.AddForce(st.pullVec, ForceMode.Force);
                if (st.gripBody && !st.gripBody.isKinematic) st.gripBody.AddForceAtPosition(-st.pullVec, st.gripPoint, ForceMode.Force);
            }
            // подъём на ступень: рука поднимает тело — сила через центр масс (не опрокидывает); величину считает блок «Рука»
            if (st.liftForce > 0f) Rb.AddForce(st.gripNormal * st.liftForce, ForceMode.Force);
            // ВИС / СПУСК: рука несёт вес тела — сила вверх через центр масс (величину считает блок «Рука»)
            if (st.carryForce > 0f)
            {
                Rb.AddForce(Vector3.up * st.carryForce, ForceMode.Force);
                if (st.gripBody && !st.gripBody.isKinematic) st.gripBody.AddForceAtPosition(Vector3.down * st.carryForce, st.gripPoint, ForceMode.Force);
            }
            {
                // РОВНОЕ ТЕЛО ПРИ ХВАТЕ: рука держит корпус — верх тела по нормали поверхности хвата. Иначе при подъёме на ступень
                // и спуске с неё тяга и касание ребра кувыркают тело (замер: наклон до 177°, после спуска лежит на боку).
                // Без хвата момента нет: устоит тело на краю или опрокинется — решает физика. Момент — сила, не запись угла.
                Vector3 lvl = st.pullNormal;                                    // ровно — по опоре под телом (у зацепа — по его склону)
                Vector3 up = Rb.rotation * Vector3.up, axis = Vector3.Cross(up, lvl);
                float sin = axis.magnitude;
                if (sin > 1e-4f)
                {
                    float ang = Mathf.Asin(Mathf.Clamp01(sin)); if (Vector3.Dot(up, lvl) < 0f) ang = Mathf.PI - ang;
                    axis /= sin;
                    Vector3 w = Rb.angularVelocity; Vector3 wPerp = w - lvl * Vector3.Dot(w, lvl);
                    Rb.AddTorque(Vector3.ClampMagnitude(axis * (levelStiffness * ang) - wPerp * levelDamping, levelTorque), ForceMode.Force);
                }
                else Rb.AddTorque(-(Rb.angularVelocity - st.gripNormal * Vector3.Dot(Rb.angularVelocity, st.gripNormal)) * levelDamping, ForceMode.Force);
            }
            if (yawStiffness <= 0f) return;
            float err = Mathf.DeltaAngle(YawRb(st.yawTarget), st.yawTarget) * Mathf.Deg2Rad;
            // помощь против трения тела о пол (как у тяги): плавно на первых 0.5° ошибки, сама тело не крутит
            float help = yawFrictionHelp * Mathf.Clamp(err / (0.5f * Mathf.Deg2Rad), -1f, 1f);
            float torque = Mathf.Clamp(yawStiffness * err + help - yawDamping * Rb.angularVelocity.y, -yawTorque, yawTorque);
            Rb.AddTorque(Vector3.up * torque, ForceMode.Force);
        }

        /// <summary>САМ ВСТАЁТ (без хвата). Тело лежит на опоре, но наклонено — привалилось к грани ступени, встало «на попа» у стены:
        /// форма-неваляшка там не помогает (стоит, как лестница у стены; по записям автора — наклон 60–87°, «застрял»). Момент
        /// поворачивает верх тела к нормали опоры под ним (на склоне — к нормали склона, а не к вертикали). В воздухе момента нет.
        /// Опора — из касаний самого тела. Момент ограничен: это «перекатиться на живот», а не привод.</summary>
        void SelfRight()
        {
            var bc = hero.arm ? hero.arm.bodyContact : null;
            if (!bc || rightTorque <= 0f) return;
            Vector3 n = Vector3.zero;
            for (int i = 0; i < bc.count; i++) if (bc.normals[i].y > 0.5f) n += bc.normals[i];
            if (n.sqrMagnitude < 1e-6f) { righting = false; restTime = 0f; return; }     // не на опоре
            n.Normalize();
            // Касание наклонное — это может быть ребро ступени, а не склон: на ребре тело застывало под 51° и считалось «ровным»
            // относительно нормали ребра (запись автора 01:01, «застрял»). Тогда равняемся на поверхность ПОД телом: один луч вниз
            // из центра, только когда касание наклонное.
            if (n.y < 0.97f)
            {
                int nh = Physics.RaycastNonAlloc(new Ray(Rb.position, Vector3.down), rayHits, 1.5f, ~0, QueryTriggerInteraction.Ignore); float bd = float.MaxValue; Vector3 under = Vector3.up;
                for (int i = 0; i < nh; i++)
                {
                    var rh = rayHits[i];
                    if (rh.distance >= bd || rh.collider.transform.IsChildOf(hero.transform) || rh.collider.GetComponentInParent<Proto.ProtoHold>()) continue;
                    bd = rh.distance; under = rh.normal;
                }
                n = under.y > 0.3f ? under : Vector3.up;
            }
            Vector3 up = Rb.rotation * Vector3.up, axis = Vector3.Cross(up, n);
            float sin = axis.magnitude;
            float ang = Mathf.Asin(Mathf.Clamp01(sin)); if (Vector3.Dot(up, n) < 0f) ang = Mathf.PI - ang;
            // Только для тела, которое ЗАСТЫЛО наклонённым (наклон > 10° и почти не вращается 0.15 с). Тело, которое прямо сейчас
            // опрокидывается через край, не трогаем: устоит оно на краю или упадёт — решает физика (замер среза 1: с моментом
            // «всегда» тело с центром масс за краем перестало падать). Начав вставать, доводим до ровного.
            if (!righting)
            {
                restTime = ang > 10f * Mathf.Deg2Rad && Rb.angularVelocity.magnitude < 0.8f ? restTime + Time.fixedDeltaTime : 0f;
                if (restTime < 0.15f) return;
                righting = true;
            }
            if (ang < 3f * Mathf.Deg2Rad) { righting = false; restTime = 0f; return; }
            axis = sin > 1e-4f ? axis / sin : Rb.rotation * Vector3.forward;
            Vector3 w = Rb.angularVelocity;
            Rb.AddTorque(Vector3.ClampMagnitude(axis * (rightStiffness * ang) - (w - n * Vector3.Dot(w, n)) * rightDamping, rightTorque), ForceMode.Force);
        }

        /// <summary>ПОСТАНОВКА НА СТАРТ (единственное место, где поза тела задаётся напрямую): старт сцены, клавиши песочницы,
        /// начало замера. В игре не вызывается — это не движение.</summary>
        public void PlaceAtStart(Vector3 pos, Quaternion rot)
        {
            Grab();
            Rb.position = pos; Rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            Rb.linearVelocity = Vector3.zero; Rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            Rb.WakeUp();                                                        // спящее тело после постановки повисло бы в воздухе
        }

        /// <summary>Рыскание по позе ФИЗИКИ (для приводов и замеров; Transform при ручном шаге может отставать).</summary>
        public float YawRb(float fallback)
        {
            Vector3 f = Rb.rotation * Vector3.forward; f.y = 0f;
            return f.sqrMagnitude < 0.04f ? fallback : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        /// <summary>Рыскание тела по проекции «вперёд» на пол (устойчиво к наклону); при кувырке — прошлое значение. По рисунку — для камеры.</summary>
        public float Yaw(float fallback)
        {
            Vector3 f = transform.forward; f.y = 0f;
            return f.sqrMagnitude < 0.04f ? fallback : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
    }
}
