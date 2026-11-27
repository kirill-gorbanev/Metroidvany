using UnityEngine;

namespace Fortress.Hero.Arm2
{
    /// <summary>БЛОК «РУКА» (срез 2: рука-эффектор без хвата). Рука — настоящие тела физики:
    /// плечевое звено (капсула 0.32) и предплечье с кистью (капсула 0.30 + сфера кисти), суставы плечо и локоть.
    /// Правило 1: звенья и кисть — коллайдеры в решателе, поэтому не проходят сквозь стены, блоки, предметы и своё тело.
    /// Правило 2: упёрлась — скользит вдоль поверхности (трение руки 0), это делает решатель, не код.
    /// ПРИВОД КИСТИ — сустав «тело ↔ кисть» с пружиной и демпфером (PD-привод внутри решателя, как у физических рук VR):
    /// точка привода на теле каждый шаг ставится в «призрак» — цель кисти, идущую к точке курсора с пределом скорости.
    /// Сила внутренняя (мышцы): свободная рука не может удержать тело за воздух. Замер 2026-11-25: привод к точке МИРА держал
    /// тело на склоне 36° и разворачивал его в падении — рука «хваталась» без хвата. Чтобы взмах руки не двигал тело:
    /// тело 40 кг, рука 0.6 кг, сила привода меньше трения тела, скорость цели ограничена.
    /// ВТОРОЙ ПРИЗРАК — ЛОКТЯ: расчётная точка локтя для данной пары «плечо — цель кисти» (непрерывная функция, перебора поз нет —
    /// урок 3); конец плечевого звена тянется к ней таким же мягким приводом. У каждого звена — свой один привод.
    /// Блок НЕ пишет position / rotation / velocity звеньев (кроме постановки на старт). Проверок другой геометрией нет (урок 4).</summary>
    public class Arm2Arm : MonoBehaviour, IArm2Tunable
    {
        public Arm2Hero hero;
        public Rigidbody upper, fore;
        public ConfigurableJoint shoulderJoint, elbowJoint, driveJoint, elbowDrive;
        public Collider[] colliders;
        public Transform ghostVisual;
        [Tooltip("Кончик предплечья (центр кисти) в координатах предплечья.")]
        public Vector3 tipLocal = new Vector3(0f, 0f, 0.30f);

        [Arm2Tune("кисть: жёсткость привода", 200f, 20000f)]
        public float stiffness = 3000f;
        [Arm2Tune("кисть: демпфер привода", 5f, 600f)]
        public float damping = 40f;
        [Arm2Tune("кисть: сила руки, Н", 5f, 400f)]
        [Tooltip("Предел силы ПРУЖИНЫ привода свободной кисти: с какой силой рука давит в преграду и как быстро догоняет цель. Демпфер — без предела.")]
        public float maxForce = 40f;
        [Arm2Tune("рука: запас силы на демпфер, Н", 0f, 200f)]
        [Tooltip("Сверх силы пружины приводу оставлено столько на гашение отскоков. Мало — рука трясётся, упёршись звеном; много — удары руки о преграды толкают тело.")]
        public float damperRoom = 20f;
        [Arm2Tune("масса плечевого звена, кг", 0.05f, 3f)]
        public float upperMass = 0.3f;
        [Arm2Tune("масса предплечья с кистью, кг", 0.05f, 3f)]
        public float foreMass = 0.3f;
        [Arm2Tune("рука: гашение вращения звеньев", 0f, 30f)]
        public float linkAngularDamping = 6f;
        [Arm2Tune("рука: гашение хода звеньев", 0f, 10f)]
        public float linkLinearDamping = 0.5f;
        [Arm2Tune("кисть: скорость цели, м/с", 1f, 6f)]
        [Tooltip("Призрак-цель идёт к точке курсора не быстрее этого, ровно на каждом шаге физики. Кисть не летит быстрее: " +
                 "тонкую стену не пробьёт, шаг за шаг физики ограничен (правило 3); скачок цели (зеркало из-за спины) — плавный ход.")]
        public float maxSpeed = 4f;
        [Arm2Tune("кисть: перенос дальше, м", 0.05f, 0.6f)]
        [Tooltip("Цель дальше этого по горизонтали — кисть переносится по воздуху (вверх, вбок, вниз), ближе — скользит по поверхности.")]
        public float hopDistance = 0.15f;
        [Arm2Tune("кисть: высота переноса над целью, м", 0f, 0.15f)]
        public float hopClear = 0.05f;
        [Arm2Tune("кисть: плавность цели, с", 0.005f, 0.15f)]
        [Tooltip("За сколько призрак разгоняется и останавливается. Меньше — резче, больше — мягче и медленнее.")]
        public float ghostSmooth = 0.03f;
        [Arm2Tune("рука: итерации решателя", 4f, 60f)]
        public float solverIterations = 24f;
        [Header("Тяга себя при хвате (срез 3)")]
        [Arm2Tune("тяга: жёсткость привода", 500f, 30000f)]
        public float pullStiffness = 6000f;
        [Arm2Tune("тяга: демпфер привода", 50f, 3000f)]
        [Tooltip("Гасит ход тела относительно кисти. Он же ограничивает скорость тяги: (сила − трение тела 290 Н) / демпфер. Трение тела само сильно гасит.")]
        public float pullDamping = 250f;
        [Arm2Tune("тяга: сила руки, Н", 100f, 1500f)]
        [Tooltip("С какой силой рука тянет тело. Тело 40 кг с трением 0.74 сдвигается от ~290 Н.")]
        public float pullForce = 700f;
        [Arm2Tune("тяга: помощь против трения тела, Н", 0f, 400f)]
        [Tooltip("Добавка к пружине, чуть меньше трения тела о пол (~290 Н): убирает мёртвый ход тяги. Больше трения — тело поедет само.")]
        public float frictionHelp = 270f;
        [Tooltip("«Верёвка» длины руки при хвате: жёсткость, демпфер и предел силы, которая не даёт телу уйти от сцепленной кисти дальше длины руки.")]
        public float ropeStiffness = 20000f, ropeDamping = 800f, ropeForce = 3000f;
        [Header("Вис (срез 6)")]
        [Tooltip("Сколько секунд под телом должно быть пусто, чтобы считать, что оно висит на руке.")]
        public float hangDelay = 0.08f;
        [Arm2Tune("спуск: рука опускает тело, м/с", 0f, 1.5f)]
        [Tooltip("Под телом пусто, кисть держится — с такой скоростью рука сама опускает тело (0 — только жестом «от себя»).")]
        public float hangAutoLower = 0.35f;
        float carryY;
        [Arm2Tune("вис: прижим к стенке, Н", 0f, 200f)]
        public float hangPress = 40f;
        [Tooltip("Гашение хода тела вбок в висе (Н·с/м).")]
        public float hangDamping = 150f;
        [Tooltip("Тело поднимается на уступ не выше этого (м) — автор: 0.4.")]
        public float maxClimb = 0.4f;
        float unsupported;
        /// <summary>Под телом есть опора (по касаниям тела при хвате; для замеров).</summary>
        public bool Supported => unsupported <= 0f;

        [Header("Подъём на ступень (срез 5)")]
        public Arm2BodyContact bodyContact;
        [Arm2Tune("ступень: сила подъёма, Н", 300f, 1500f)]
        [Tooltip("Предел силы, с которой рука поднимает тело на ступень (вес тела 40 кг — 392 Н).")]
        public float liftForce = 700f;
        [Arm2Tune("ступень: жёсткость подъёма", 500f, 20000f)]
        public float liftStiffness = 5000f;
        [Arm2Tune("ступень: демпфер подъёма", 50f, 2000f)]
        public float liftDamping = 600f;
        [Arm2Tune("ступень: тяга вперёд при подъёме, Н", 0f, 500f)]
        [Tooltip("Пока тело поднимается вдоль грани, вперёд тянет только с этой силой (трение о грань мешает подъёму).")]
        public float climbPull = 150f;
        [Tooltip("Плечо над центром кисти, когда тело стоит на той же поверхности (м): 0.24 − (радиус кисти − нажим).")]
        public float shoulderRest = 0.208f;
        [Tooltip("Запас над ребром ступени при подъёме (м).")]
        public float climbClearance = 0.03f;
        /// <summary>При хвате игрок отталкивается («от себя»): желаемое расстояние больше настоящего.</summary>
        public bool Pushing { get; private set; }
        /// <summary>Сейчас рука поднимает тело на ступень (для замеров).</summary>
        public bool Climbing { get; private set; }

        [Header("Локоть — у него всегда определённое место")]
        [Arm2Tune("локоть: жёсткость привода", 0f, 6000f)]
        [Tooltip("Локоть тянется к расчётной точке: плечо и цель кисти задают её однозначно (локоть вверх-наружу от линии плечо–кисть). " +
                 "Автор 2026-11-25: «рука постоянно в движении — какая-то тряска, даже если не двигать мышку»: свободный локоть после каждого " +
                 "движения секунду-две плыл и качался (по записи автора — размах 22–39 см при стоящей цели). Упёрся в преграду — уступает (сила ограничена).")]
        public float elbowStiffness = 1200f;
        [Arm2Tune("локоть: демпфер привода", 0f, 200f)]
        public float elbowDamping = 40f;
        [Arm2Tune("локоть: сила, Н", 0f, 80f)]
        public float elbowForce = 25f;
        [Tooltip("Куда смотрит локоть от линии плечо–кисть, в координатах тела: вправо-вверх (рука правая).")]
        public Vector3 elbowPole = new Vector3(1f, 0.7f, 0f);

        [Tooltip("Призрак кисти (цель под курсором) виден, когда кисть отстала от цели дальше этого, м.")]
        public float ghostShowAt = 0.04f;

        PhysicsMaterial mat;
        Vector3 ghostPos, ghostVel, elbowPrev, elbowVel, pullDir = Vector3.forward; bool hasElbowPrev, wasGripped;
        /// <summary>Где сейчас призрак-цель (идёт к цели намерения с пределом скорости).</summary>
        public Vector3 GhostPos => ghostPos;
        /// <summary>На сколько желаемое расстояние может уйти от настоящего (сила тяги / жёсткость), м.</summary>
        public float PullLead => pullForce / Mathf.Max(1f, pullStiffness);
        Pose upperRest, foreRest; bool hasRest;

        /// <summary>Центр кисти в мире — по позе физики предплечья.</summary>
        public Vector3 HandPos => fore.position + fore.rotation * tipLocal;
        public Vector3 ElbowPos => fore.position;

        void Awake()
        {
            // поза покоя руки относительно тела — для постановки на старт
            var b = hero.body.transform;
            upperRest = new Pose(b.InverseTransformPoint(upper.position), Quaternion.Inverse(b.rotation) * upper.rotation);
            foreRest = new Pose(b.InverseTransformPoint(fore.position), Quaternion.Inverse(b.rotation) * fore.rotation);
            hasRest = true;
            ghostPos = HandPos;
            ApplyTuning();
        }

        public void ApplyTuning()
        {
            if (!upper || !fore) return;
            if (!mat) mat = new PhysicsMaterial("Arm2 рука") { bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum };
            mat.staticFriction = 0f; mat.dynamicFriction = 0f; mat.frictionCombine = PhysicsMaterialCombine.Minimum;   // рука скользит вдоль поверхности
            foreach (var c in colliders) if (c) c.sharedMaterial = mat;
            Setup(upper, upperMass); Setup(fore, foreMass);
            ApplyDrives();
            var e = new JointDrive { positionSpring = elbowStiffness, positionDamper = elbowDamping, maximumForce = elbowForce + damperRoom };
            elbowDrive.xDrive = e; elbowDrive.yDrive = e; elbowDrive.zDrive = e;
            // СВОБОДНАЯ РУКА ТЕЛО НЕ КАЧАЕТ. Автор 2026-11-26: «потряхивание тела» — кисть по одну сторону столба, курсор по другую:
            // привод давит рукой в столб, а отдача привода качала тело (наклон до 7°, подъём на 8 мм, запись 16:48). Для двух
            // ведущих суставов тело считается тяжелее в 1/driveBodyShare раз — отдача привода в тело слабее во столько же.
            // Суставы плеча и локтя и хват не тронуты: вис и тяга работают как прежде.
            if (driveJoint) driveJoint.connectedMassScale = driveBodyShare;
            elbowDrive.connectedMassScale = driveBodyShare;
        }
        [Arm2Tune("рука: доля отдачи привода в тело", 0.02f, 1f)]
        [Tooltip("1 — честная отдача (рука качает тело, когда упирается); меньше — тело для привода свободной руки тяжелее.")]
        public float driveBodyShare = 0.1f;

        /// <summary>Привод кисти: свободная рука — мягкий и слабый (не двигает тело); при хвате — выключен (кисть держит сустав
        /// хвата, тело тянет сила через центр масс).</summary>
        void ApplyDrives()
        {
            bool g = hero && hero.state.gripped;
            var d = g ? new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = 0f }        // при хвате привод кисти выключен: тело тянет сила (см. SetAnchors)
                      : new JointDrive { positionSpring = stiffness, positionDamper = damping, maximumForce = maxForce + damperRoom };   // предел пружины — в SetAnchors
            driveJoint.xDrive = d; driveJoint.yDrive = d; driveJoint.zDrive = d;
        }

        void Setup(Rigidbody rb, float mass)
        {
            rb.mass = mass; rb.useGravity = false; rb.isKinematic = false;     // вес руки держат приводы кисти и локтя
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearDamping = linkLinearDamping; rb.angularDamping = linkAngularDamping;
            rb.maxLinearVelocity = maxSpeed; rb.maxAngularVelocity = 7f; // страховка: сорвавшись с ребра, звено не летит быстрее (кончик ≤ ~6 м/с)
            rb.maxDepenetrationVelocity = 1f;
            rb.solverIterations = Mathf.RoundToInt(solverIterations); rb.solverVelocityIterations = 8;
            rb.sleepThreshold = 0f;                                             // спящему звену не приходят касания — клик лежащей кистью не дал бы хвата
        }

        /// <summary>Где быть локтю: в плоскости «плечо — кисть — полюс» (полюс — вправо-вверх от тела), по длинам звеньев.
        /// Непрерывна по плечу и кисти, пока линия плечо–кисть не вдоль полюса (кисть прямо вправо-вверх от плеча — вне сектора).</summary>
        public Vector3 ElbowWanted(Vector3 S, Vector3 hand)
        {
            float l1 = hero.intent.upperArm, l2 = hero.intent.foreArm;
            Vector3 v = hand - S; float d = Mathf.Clamp(v.magnitude, Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.001f);
            Vector3 dir = v.sqrMagnitude > 1e-8f ? v.normalized : hero.body.Rb.rotation * Vector3.forward;
            float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d), h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 pole = hero.body.Rb.rotation * elbowPole.normalized;
            Vector3 perp = pole - dir * Vector3.Dot(pole, dir);
            perp = perp.sqrMagnitude > 1e-6f ? perp.normalized : Vector3.up;
            return S + dir * a + perp * h;
        }

        /// <summary>Точки приводов на теле: призрак кисти и расчётное место локтя — в координатах тела (по позе физики).
        /// Место локтя — по плечу и призраку, а не по настоящей кисти: обратной связи от физики к цели нет.</summary>
        void SetAnchors()
        {
            var b = hero.body.Rb; Quaternion inv = Quaternion.Inverse(b.rotation);
            // ПРИВОД = ПРУЖИНА С ПРЕДЕЛОМ + ДЕМПФЕР БЕЗ ПРЕДЕЛА.
            // Автор 2026-11-25: «когда сама рука — а не кисть — сталкивается, появляется тряска». По записям: звено упёрлось, кисть не
            // доходит до цели на 13–24 см, привод давит с полной силой. У сустава Unity предел силы общий на пружину и демпфер:
            // пружина его выбирала целиком, демпферу не оставалось — отскоки от преграды нечем было гасить.
            // Теперь предел — только у пружины: точка привода не дальше (сила руки / жёсткость) от НАСТОЯЩЕЙ кисти, а предел силы
            // сустава поднят, и демпфер работает всегда. Плюс упреждение на скорость призрака — в движении демпфер кисть не тормозит.
            // Настоящая поза читается только чтобы ограничить силу пружины; цель (призрак) за упёршейся кистью не тянется.
            float dt = Time.fixedDeltaTime;
            Vector3 S = hero.intent.ShoulderWorld();
            Vector3 el = ElbowWanted(S, hero.state.gripped ? HandPos : ghostPos);
            elbowVel = hasElbowPrev && dt > 0f ? (el - elbowPrev) / dt : Vector3.zero; elbowPrev = el; hasElbowPrev = true;
            Vector3 hand = HandPos, elbow = ElbowPos;
            Vector3 handAnchor;
            if (hero.state.gripped)
            {
                // ХВАТ: кисть сцеплена суставом хвата, привод «тело ↔ кисть» выключен (ApplyDrives) — рука просто следует за телом.
                // ТЯГУ делает сила на тело через его центр масс (её прикладывает блок «Тело»): рука тянет тело вдоль поверхности хвата.
                // Почему не привод сустава: его сила приложена в точке кисти; кисть на ступени выше тела — и тяга опрокидывала тело
                // ещё до ступени (след замера: наклон 60°), а при хвате на полу тянула за точку сбоку и крутила тело.
                // Тело по-прежнему двигают только силы; позу и скорость никто не пишет.
                var st = hero.state; var brb = hero.body.Rb;
                Vector3 n = st.gripNormal;
                // ПЛОСКОСТЬ ТЯГИ — ОПОРА ПОД ТЕЛОМ, а не поверхность под кистью. Автор 2026-11-26: «поверхность склон не определяет
                // направление жеста — не получается притянуться, оттолкнуться». Кисть на склоне, тело на полу (или наоборот): в плоскости
                // склона проекция плеча оказывалась не там, где тело, и «на себя» тянуло не к кисти, а в пол. Тело едет по СВОЕЙ опоре к
                // кисти; опора сменилась (заехало на склон) — плоскость плавно поворачивается за ней. У зацепов плоскость задаёт хват.
                // Пока рука поднимает тело на ступень или несёт его в висе, плоскость не трогаем; касание ребра (нормали опоры
                // расходятся) — тоже не опора: иначе при перевале через ребро ступени плоскость и тело наклонялись на 30° (замер).
                if (!st.gripOnHold && bodyContact && !Climbing && !st.hanging)
                {
                    Vector3 sup = Vector3.zero, first = Vector3.zero; int cnt = 0; bool agree = true;
                    for (int i = 0; i < bodyContact.count; i++)
                    {
                        Vector3 nc = bodyContact.normals[i]; if (nc.y <= 0.3f) continue;
                        if (cnt == 0) first = nc; else if (Vector3.Dot(first, nc) < 0.985f) agree = false;      // расходятся больше ~10°
                        sup += nc; cnt++;
                    }
                    if (cnt >= 2 && agree) st.pullNormal = Vector3.Slerp(st.pullNormal, sup.normalized, 1f - Mathf.Exp(-12f * dt)).normalized;
                }
                Vector3 pn = st.pullNormal;
                // мера — расстояние плечо–кисть ВДОЛЬ плоскости тяги (pullLength — оно же), не короче сгиба до упора
                Vector3 tl = Vector3.ProjectOnPlane(hand - S, pn); float len = tl.magnitude;
                if (len > 0.02f) pullDir = tl / len;                            // совсем близко — держим прошлое направление (в момент хвата — из хвата)
                Vector3 t = pullDir;
                float err = len - st.pullLength;                                // рука длиннее, чем хочет игрок, — тело едет к кисти
                Vector3 gripVel = st.gripBody ? st.gripBody.GetPointVelocity(hand) : Vector3.zero;
                Vector3 relVel = brb.linearVelocity - gripVel;
                // ТРЕНИЕ ТЕЛА. Тело 40 кг держит трение ~290 Н: одной пружине нужна ошибка ~5 см, чтобы его стронуть, — мёртвая зона и
                // люфт при смене направления. К пружине добавляется сила чуть меньше трения — плавно на первых 5 мм ошибки.
                float pull = pullStiffness * err + frictionHelp * Mathf.Clamp(err / 0.005f, -1f, 1f) - pullDamping * Vector3.Dot(relVel, t);

                // ПОДЪЁМ НА СТУПЕНЬ (срез 5). Кисть держится за поверхность выше опоры тела (хват выше 0.4 не берётся — блок «Хват»),
                // а тело упёрлось в грань впереди по ходу тяги — рука поднимает тело, пока плечо не окажется над поверхностью хвата
                // так же, как когда тело на ней стоит; затем тяга затаскивает тело наверх. Подъём держится, пока тело не встало на
                // опору без преграды впереди (или пока не отпустили / не толкнули «от себя»). Пока грань впереди — вперёд тянет слабо:
                // трение о грань мешало бы подъёму. «Упёрлось» и «стоит» — из касаний самого тела (события физики).
                bool ahead = false, support = false, standing = false;
                if (bodyContact)
                {
                    for (int i = 0; i < bodyContact.count; i++)
                    {
                        Vector3 nc = bodyContact.normals[i]; float up = Vector3.Dot(nc, n);
                        if (Vector3.Dot(nc, t) < -0.5f && Mathf.Abs(up) < 0.5f) ahead = true;
                        if (up > 0.7f) support = true;
                        if (nc.y > 0.3f) standing = true;                       // тело на чём-то лежит (склон до ~72° — тоже опора)
                    }
                    bodyContact.Clear();
                }
                // ВИС И СПУСК БЕЗ ПАДЕНИЯ (срез 6). Автор 2026-11-26: «ты должен был сделать, чтоб спуск проходил без падения» и «не нравится,
                // как я на бок опрокидываюсь». Кисть держится, а под телом ничего нет (ушло за край спиной или передом) — РУКА НЕСЁТ ВЕС
                // ТЕЛА: сила вверх через центр масс держит его на высоте, где оно потеряло опору, и плавно опускает (сама — hangAutoLower,
                // и быстрее по жесту «от себя»), пока тело не встанет на опору или рука не вытянется до предела — тогда тело висит на
                // «верёвке» длины руки. Тело при этом остаётся ровным (блок «Тело»). Отпустил ЛКМ — падает.
                // «Под телом ничего нет» — из касаний самого тела; флагов «край / обрыв» нет: за край тело уводит обычная тяга.
                unsupported = standing ? 0f : unsupported + dt;
                bool hang = unsupported > hangDelay && !Climbing;
                if (hang && !st.hanging) carryY = brb.position.y;
                st.hanging = hang;
                // насколько тело ниже, чем стояло бы на поверхности хвата; пока грань впереди — с запасом над ребром (иначе низ тела
                // останавливался в сантиметре под ребром: след замера)
                float needRaw = shoulderRest - Vector3.Dot(S - hand, n);
                float need = needRaw + (ahead ? climbClearance : 0f);
                // «Тело поднимается на уступ не выше 0.4» (автор): подъём начинается, только если подниматься не больше этого.
                // Из виса — по жесту «на себя» (hangPullUp), если висит не глубже 0.4.
                if (!Climbing)
                {
                    bool fromHang = st.hanging && st.hangPullUp && ahead;
                    // начинается только если кисть и правда выше опоры тела (без запаса над ребром): запись автора 00:33 — у угла уступа
                    // подъём включался на ровном полу, тяга падала до 150 Н, и тело застревало
                    if (ahead && needRaw > 0.04f && need < maxClimb + 0.05f && (err > -0.01f || fromHang))
                    {
                        Climbing = true;
                        if (fromHang) { st.pullLength = len; err = 0f; pull = 0f; st.hanging = false; hang = false; }
                    }
                }
                else if (err < -0.02f || (support && !ahead && need < 0.03f)) Climbing = false;
                float lift = 0f, cap = pullForce;
                if (Climbing)
                {
                    lift = Mathf.Clamp(liftStiffness * need + brb.mass * Physics.gravity.magnitude - liftDamping * Vector3.Dot(relVel, n), 0f, liftForce);
                    if (ahead) cap = climbPull;
                }
                // Кисть съезжает по крутому склону (без зацепа) — она не держит, значит и не тянет. Записи автора 00:30: с полной тягой
                // 700 Н за время съезда тело успевало подняться — и так, хват за хватом, залезало на склон 45° без камней.
                // Оттолкнуться («от себя») от крутого склона можно — кисть прижата к нему (запись автора 01:03: «не получается и отталкиваться»).
                Pushing = err < -0.005f;
                bool slipGrip = hero.grip && hero.grip.Slipping;
                if (slipGrip) lift = 0f;
                // ДЛИНА РУКИ: кисть сцеплена, и дальше длины руки тело от неё не уйдёт (свалилось с края, толкнули) — «верёвка»:
                // сила к кисти, только когда рука вытянута до предела. Держит тело, а не лёгкие звенья руки.
                Vector3 arm3 = hand - S; float d3 = arm3.magnitude, over = d3 - hero.intent.L * 0.98f;
                Vector3 rope = over > 0f && d3 > 1e-4f ? arm3 / d3 * Mathf.Clamp(ropeStiffness * over - ropeDamping * Vector3.Dot(relVel, arm3 / d3), 0f, ropeForce) : Vector3.zero;

                float carry = 0f;
                if (hang)
                {
                    float y = brb.position.y;
                    if (over < -0.01f) carryY -= hangAutoLower * dt + st.hangLower;     // рука ещё не вытянута — опускаем
                    st.hangLower = 0f;
                    carryY = Mathf.Clamp(carryY, y - 0.05f, y + 0.01f);                // цель высоты не убегает от тела
                    carry = Mathf.Clamp(brb.mass * Physics.gravity.magnitude + liftStiffness * (carryY - y) - liftDamping * relVel.y, 0f, liftForce);
                    // в висе вдоль поверхности хвата не тянет (жест «от себя» опускает); лёгкий прижим к стенке, чтобы не болтало
                    // (прижим — к точке под кистью и гаснет, когда плечо под ней: стенка для тела скользкая, и постоянный прижим вбок
                    // возил бы висящее тело вдоль неё; плюс гашение хода вбок)
                    Vector3 side = relVel - Vector3.up * relVel.y;
                    st.pullVec = (len > 0.02f ? tl / len : Vector3.zero) * (hangPress * Mathf.Clamp01(len / 0.1f)) - side * hangDamping + rope;
                }
                else st.pullVec = t * Mathf.Clamp(pull, -cap, slipGrip ? 0f : cap) + rope;      // владелец — этот блок; прикладывает блок «Тело»
                st.carryForce = carry;
                st.liftForce = lift;
                handAnchor = hand;                                              // привод кисти выключен; точка привода — в кисти
            }
            else
                handAnchor = hand + Vector3.ClampMagnitude(ghostPos - hand, stiffness > 1f ? maxForce / stiffness : 0f)
                                 + (stiffness > 1f ? ghostVel * (damping / stiffness) : Vector3.zero);
            Vector3 elbowAnchor = elbow + Vector3.ClampMagnitude(el - elbow, elbowStiffness > 1f ? elbowForce / elbowStiffness : 0f)
                                  + (elbowStiffness > 1f ? Vector3.ClampMagnitude(elbowVel, maxSpeed) * (elbowDamping / elbowStiffness) : Vector3.zero);
            driveJoint.connectedAnchor = inv * (handAnchor - b.position);
            elbowDrive.connectedAnchor = inv * (elbowAnchor - b.position);
        }

        // ---------- ОБХОД ПРЕГРАДЫ (2026-11-27, запись автора 14:11: кисть под перемычкой арки, цель — на её верху) ----------
        // Призрак идёт к цели по прямой и преград не видит: если кисть упёрлась (отстала от призрака и к нему не движется),
        // цель сама не достанется. Правило одно для любого твёрдого: кисть отходит от преграды туда, где над ней свободно
        // (сначала — назад к плечу, потом вбок), поднимается выше цели, идёт над ней и опускается. Маршрут считается один раз
        // на застревание (несколько запросов к физике), а не каждый шаг; не нашёлся — второй раз для той же цели не ищется.
        [Arm2Tune("кисть: обход — отстала дальше, м", 0.02f, 0.2f)]
        public float detourLag = 0.06f;
        [Arm2Tune("кисть: обход — ждать, с", 0.05f, 1f)]
        public float detourWait = 0.15f;
        readonly Vector3[] detour = new Vector3[3]; int detourIdx = -1; float blockedTime, detourTime, lagRef; Vector3 detourFor, detourFailed; bool hasDetourFailed;
        static readonly RaycastHit[] castHits = new RaycastHit[16]; static readonly Collider[] overlapHits = new Collider[16];
        /// <summary>Кисть идёт в обход преграды (для замеров).</summary>
        public bool Detouring => detourIdx >= 0;
        /// <summary>Счётчики обходов (для замеров): начато, дошло до конца, сорвано в пути, маршрут не нашёлся.</summary>
        public static int DetourStarted, DetourDone, DetourAborted, DetourNoPath;

        bool FreeAt(Vector3 p, float r)
        {
            int n = Physics.OverlapSphereNonAlloc(p, r, overlapHits, ~0, QueryTriggerInteraction.Ignore);
            var held = hero.state.heldItem;                                        // предмет в своей руке — не преграда
            for (int i = 0; i < n; i++) if (!overlapHits[i].transform.IsChildOf(hero.transform) && !(held && overlapHits[i].attachedRigidbody == held)) return false;
            return true;
        }
        bool FreeWay(Vector3 a, Vector3 b, float r)
        {
            Vector3 d = b - a; float len = d.magnitude; if (len < 1e-4f) return true;
            int n = Physics.SphereCastNonAlloc(a, r, d / len, castHits, len, ~0, QueryTriggerInteraction.Ignore);
            var held = hero.state.heldItem;
            for (int i = 0; i < n; i++) if (!castHits[i].collider.transform.IsChildOf(hero.transform) && !(held && castHits[i].collider.attachedRigidbody == held)) return false;
            return true;
        }

        /// <summary>Маршрут в обход: точка отхода A (над ней свободно), B над ней выше цели, C над целью. false — не нашёлся.</summary>
        bool PlanDetour(Vector3 hand, Vector3 want, Vector3 S, float reach)
        {
            float r = hero.intent.handRadius * 0.9f;
            Vector3 back = S - hand; back.y = 0f; if (back.sqrMagnitude < 1e-6f) return false; back.Normalize();
            Vector3 side = new Vector3(back.z, 0f, -back.x);
            float top = Mathf.Max(hand.y, want.y) + hopClear + 0.02f;
            Vector3 C = new Vector3(want.x, top, want.z);
            if ((C - S).magnitude > reach || !FreeAt(C, r)) return false;
            for (int dir = 0; dir < 3; dir++)
            {
                Vector3 u = dir == 0 ? back : dir == 1 ? side : -side;
                for (int k = 1; k <= 8; k++)
                {
                    Vector3 A = hand + u * (0.04f * k), B = new Vector3(A.x, top, A.z);
                    if ((B - S).magnitude > reach) break;
                    if (!FreeAt(A, r)) continue;
                    if (!FreeWay(A, B, r) || !FreeWay(B, C, r)) continue;
                    detour[0] = A; detour[1] = B; detour[2] = C; return true;
                }
            }
            return false;
        }

        /// <summary>Куда вести призрак на этом шаге с учётом обхода: точка маршрута (true) или сама цель (false).</summary>
        bool DetourStep(Vector3 want, Vector3 S, float reach, float dt, out Vector3 via)
        {
            via = want; Vector3 hand = HandPos;
            Vector3 toGhost = ghostPos - hand; float lag = toGhost.magnitude;
            // «упёрлась»: призрак уже пришёл, куда шёл, а кисть отстала и отставание не сокращается (хотя бы на 1 см за время ожидания).
            // Скорость кисти тут не годится: под перемычкой кисть ездит вбок за целью и «движется», оставаясь запертой.
            Vector3 aim = detourIdx >= 0 ? detour[detourIdx] : want;
            if (lag <= detourLag || (ghostPos - aim).magnitude > 0.03f) { blockedTime = 0f; lagRef = lag; }
            else if (lag < lagRef - 0.01f) { blockedTime = 0f; lagRef = lag; }
            else blockedTime += dt;
            if (hasDetourFailed && (want - detourFailed).magnitude > 0.05f) hasDetourFailed = false;
            if (detourIdx >= 0)
            {
                detourTime += dt;
                // цель ушла, обход затянулся или кисть упёрлась и на нём — обход снят; для этой цели второй раз не ищется
                if ((want - detourFor).magnitude > 0.08f) detourIdx = -1;
                else if (detourTime > 1.5f || blockedTime > 0.25f) { detourIdx = -1; detourFailed = want; hasDetourFailed = true; blockedTime = 0f; DetourAborted++; }
                else
                {
                    if ((hand - detour[detourIdx]).magnitude < 0.03f) { detourIdx++; blockedTime = 0f; }
                    if (detourIdx > 2) { detourIdx = -1; DetourDone++; }
                }
                if (detourIdx >= 0) { via = detour[detourIdx]; return true; }
                return false;
            }
            if (blockedTime > detourWait && !hasDetourFailed)
            {
                blockedTime = 0f;
                if (PlanDetour(hand, want, S, reach)) { detourIdx = 0; detourTime = 0f; detourFor = want; via = detour[0]; DetourStarted++; return true; }
                detourFailed = want; hasDetourFailed = true; DetourNoPath++;
            }
            return false;
        }

        /// <summary>Шаг перед физикой: призрак — в цель намерения. Больше блок ничего не двигает.</summary>
        public void FixedStep(float dt)
        {
            var st = hero.state;
            if (st.gripped != wasGripped)
            {
                wasGripped = st.gripped; ApplyDrives(); Climbing = false; Pushing = false; st.liftForce = 0f; st.pullVec = Vector3.zero; st.carryForce = 0f; st.hangLower = 0f; st.hanging = false; st.hangPullUp = false; unsupported = 0f;
                hasElbowPrev = false;                                           // расчётное место локтя сменило опору (призрак ↔ кисть) — без толчка упреждения
                if (st.gripped) pullDir = st.pullDir0;
            }
            if (st.gripped)
            {
                // ХВАТ: кисть сцеплена с поверхностью. Тот же привод «тело ↔ кисть» теперь тянет ТЕЛО: его точка на теле ставится
                // туда, где кисть должна быть относительно тела при длине руки, которую хочет игрок (pullLength), — по линии
                // плечо → кисть, как она есть. Направление не навязывается: тело может разворачиваться вокруг кисти.
                ghostPos = HandPos; ghostVel = Vector3.zero;                    // после отпускания призрак пойдёт от кисти
                detourIdx = -1; blockedTime = 0f; hasDetourFailed = false;
            }
            else if (st.handTargetValid)
            {
                // Цель намерения приходит раз в кадр, шагов физики на кадр — от 0 до 10: призрак идёт к ней ровным ходом на каждом
                // шаге (иначе его скорость — «гребёнка», и демпфер привода дёргает кисть). И всегда в досягаемости от плеча, где бы
                // тело ни оказалось (платформа, сползание): руку в струну призрак не тянет.
                Vector3 want = st.handTarget, S = hero.intent.ShoulderWorld(); float reach = hero.intent.Reach;
                Vector3 v = want - S; float d = v.magnitude; if (d > reach) want = S + v / d * reach;
                // ПЕРЕНОС КИСТИ: цель далеко (дальше hopDistance по горизонтали) — кисть сначала поднимается на высоту цели с запасом,
                // потом идёт вбок, потом опускается. По прямой она упиралась в грань ступени и ползла по ней вверх около секунды
                // (запись автора 01:02: «рука не попадает сверху, даже если курсор высоко»). Близкая цель — прямо, кисть скользит по поверхности.
                Vector3 via = want, flat = want - ghostPos; flat.y = 0f;
                if (DetourStep(want, S, reach, dt, out Vector3 around)) via = around;       // кисть упёрлась — в обход преграды
                else if (st.heldItem)
                {
                    // ПЕРЕНОС С ПРЕДМЕТОМ (2026-11-27, проба CarryZProbe: кубик цеплялся краем за бок столбика на 2 см ниже его верха).
                    // Предмет шире кисти, и «срезать угол» ему нельзя: сначала вверх — пока сама КИСТЬ (а не призрак) не поднимется
                    // выше цели, потом вбок на этой высоте, и вниз — только когда кисть уже над местом (а не за 15 см до него).
                    float top = want.y + hopClear;
                    if (flat.magnitude > 0.03f)
                        via = HandPos.y < top - 0.02f ? new Vector3(ghostPos.x, Mathf.Max(ghostPos.y, top), ghostPos.z)
                                                      : new Vector3(want.x, Mathf.Max(ghostPos.y, top), want.z);
                    else if (new Vector2(HandPos.x - want.x, HandPos.z - want.z).magnitude > 0.03f) via = new Vector3(want.x, Mathf.Max(ghostPos.y, want.y), want.z);
                }
                else if (flat.magnitude > hopDistance)
                {
                    float top = Mathf.Max(ghostPos.y, want.y + hopClear);
                    via = top - ghostPos.y > 0.02f ? new Vector3(ghostPos.x, top, ghostPos.z) + flat * 0.15f      // вверх (и чуть вбок)
                                                 : new Vector3(want.x, top, want.z);                               // вбок на этой высоте
                }
                // плавный разгон и остановка призрака (без рывка скорости) с пределом скорости
                ghostPos = Vector3.SmoothDamp(ghostPos, via, ref ghostVel, ghostSmooth, maxSpeed, dt);
                v = ghostPos - S; d = v.magnitude; if (d > reach) ghostPos = S + v / d * reach;
            }
            SetAnchors();
            // чтение прошлого шага физики — в состояние (владелец handPos / handLag — этот блок)
            st.handPos = HandPos;
            st.handLag = st.handTargetValid ? Vector3.Distance(st.handPos, st.handTarget) : 0f;
        }

        /// <summary>После кадра: призрак кисти — рисунок под целью; виден, только когда настоящая кисть отстала.</summary>
        public void LateStep()
        {
            if (!ghostVisual) return;
            var st = hero.state;
            Vector3 handDrawn = fore.transform.TransformPoint(tipLocal);
            bool show = st.handTargetValid && Vector3.Distance(handDrawn, st.handTarget) > ghostShowAt;
            if (ghostVisual.gameObject.activeSelf != show) ghostVisual.gameObject.SetActive(show);
            if (show) ghostVisual.position = st.handTarget;
        }

        /// <summary>ПОСТАНОВКА НА СТАРТ вместе с телом (старт сцены, клавиши песочницы, начало замера) — не движение в игре.</summary>
        public void PlaceAtStart()
        {
            if (!hasRest) return;
            var b = hero.body.Rb;
            Put(upper, b.position + b.rotation * upperRest.position, b.rotation * upperRest.rotation);
            Put(fore, b.position + b.rotation * foreRest.position, b.rotation * foreRest.rotation);
            Vector3 tip = HandPos;
            ghostPos = tip; ghostVel = Vector3.zero; hasElbowPrev = false; SetAnchors();
            detourIdx = -1; blockedTime = 0f; hasDetourFailed = false;
            hero.state.handTarget = tip; hero.state.handTargetValid = false; hero.state.cursorValid = false;
            hero.state.handPos = tip; hero.state.handLag = 0f;
            Physics.SyncTransforms();
        }

        static void Put(Rigidbody rb, Vector3 p, Quaternion q)
        {
            rb.position = p; rb.rotation = q; rb.transform.SetPositionAndRotation(p, q);
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.WakeUp();
        }
    }
}
