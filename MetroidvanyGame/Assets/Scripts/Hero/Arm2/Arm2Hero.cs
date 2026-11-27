using System;
using UnityEngine;

namespace Fortress.Hero.Arm2
{
    /// <summary>НОВЫЙ КОНТРОЛЛЕР ГЕРОЯ (Arm2) — корень. Автор 2026-11-25: «новый контроллер на физике Unity… одна правда о руке
    /// и теле — физика; логика выдаёт только намерение».
    /// ЧЕТЫРЕ ПРАВИЛА (design/hero_arm_knowledge.md): 1) сквозь твёрдое — никогда; 2) скольжение вдоль поверхности;
    /// 3) плавность, без телепортов; 4) физика Unity: Rigidbody, суставы, коллайдеры, FixedUpdate.
    /// Корень ничего не двигает. Он: держит общее состояние (Arm2State), задаёт шаг физики и порядок блоков.
    /// Поток кадра: ввод и намерение (Update) → блоки привода (FixedUpdate) → шаг физики → чтение → рисунок и камера (LateUpdate).
    /// После шага физики позу тел никто не переписывает.</summary>
    [DefaultExecutionOrder(-50)]
    public class Arm2Hero : MonoBehaviour, IArm2Tunable
    {
        public Arm2Body body;
        public Arm2Camera cam;
        public Arm2Input input;
        public Arm2Intent intent;
        public Arm2Arm arm;
        public Arm2ItemGuard guard;
        public Arm2Grip grip;

        [Header("Физика")]
        [Arm2Tune("шаг физики, с", 0.002f, 0.02f)]
        [Tooltip("Шаг физики. Автор отверг 50 Гц (0.02): на 247 к/с тело шло ступеньками. С интерполяцией Rigidbody — 0.005.")]
        public float physicsDt = 0.005f;
        [Tooltip("Самый длинный кадр, который физика догоняет (с): при просадке ФПС шагов за кадр не больше maxFrame / physicsDt.")]
        public float maxFrame = 0.05f;

        /// <summary>Общее состояние блоков. У каждого поля один владелец — см. Arm2State.</summary>
        public readonly Arm2State state = new Arm2State();

        /// <summary>Ручной шаг (замер, повтор записи): блоки и физику шагает симуляция, Update / FixedUpdate молчат.</summary>
        public static bool Manual;
        public static Arm2Hero Live { get; private set; }

        float savedFixedDt, savedMaxDt; bool saved, savedPatch, lastLmb; SimulationMode savedMode;

        void OnEnable()
        {
            Live = this;
            savedFixedDt = Time.fixedDeltaTime; savedMaxDt = Time.maximumDeltaTime; saved = true;
            // Старый герой шагает физику сам (SimulationMode.Script); новому нужен обычный цикл FixedUpdate.
            // Оба героя в одной сцене спорят за режим физики: старый шагал бы её сам из Update, у нового молча ломались бы шаг и интерполяция.
            var old = FindAnyObjectByType<Proto.HeroProto>();
            if (old && old.isActiveAndEnabled) Debug.LogError("[Arm2] в сцене есть старый герой HeroProto — два героя в одной сцене не работают (спор за шаг физики)");
            savedMode = Physics.simulationMode;
            if (Physics.simulationMode != SimulationMode.FixedUpdate) Physics.simulationMode = SimulationMode.FixedUpdate;
            // Пятно трения PhysX по умолчанию держит коробку вдвое круче формулы (замер 2026-11-25: трение 0.66 — до 51° вместо 33°)
            // и зависит от формы пятна контакта. С этой настройкой трение совпадает с формулой — пороги склона и «сила кисти
            // меньше трения тела» считаются честно.
            savedPatch = Physics.improvedPatchFriction; Physics.improvedPatchFriction = true;
            ApplyTuning();
        }

        void OnDisable()
        {
            if (Live == this) Live = null;
            if (saved) { Time.fixedDeltaTime = savedFixedDt; Time.maximumDeltaTime = savedMaxDt; Physics.improvedPatchFriction = savedPatch; Physics.simulationMode = savedMode; saved = false; }
        }

        public void ApplyTuning()
        {
            Time.fixedDeltaTime = Mathf.Clamp(physicsDt, 0.002f, 0.02f);
            Time.maximumDeltaTime = maxFrame;
        }

        void Update() { if (!Manual) FrameStep(input ? input.Read() : default); }
        void FixedUpdate() { if (!Manual) FixedStep(Time.fixedDeltaTime); }
        void LateUpdate() { if (!Manual) LateStep(Time.deltaTime); }      // то же время, по которому идёт интерполяция Rigidbody

        /// <summary>Кадр: ввод → состояние. Позу не трогает.</summary>
        public void FrameStep(Arm2InputFrame f)
        {
            state.frame = f;
            // Кадров и шагов физики — разное число (247 к/с при шаге 0.005 с: каждый пятый кадр без шага; на низком ФПС — несколько
            // шагов на кадр). Всё «событийное» копится до ближайшего шага и гасится им — нажатие не теряется и не считается дважды.
            state.pendingDelta += f.delta;
            if (f.lmbDown || (f.lmb && !lastLmb)) state.pendingPress = true;
            if (!f.lmb && (lastLmb || f.lmbDown)) state.pendingRelease = true;      // клик короче кадра: нажали и уже отпустили
            lastLmb = f.lmb;
            if (intent) intent.FrameStep(f);        // курсор → цель кисти (запросы к физике — здесь, раз в кадр, не на шаге физики)
        }

        /// <summary>Шаг перед физикой: блоки по порядку. Тело стоит само, на трении; рука — призрак в цель (срез 2).</summary>
        float landTime;
        public void FixedStep(float dt)
        {
            if (guard) guard.FixedStep(dt);
            if (grip) grip.FixedStep(dt);            // нажатие / отпускание → сустав хвата
            if (intent) intent.FixedStep(dt);        // жест при хвате → длина руки, которую хочет игрок
            if (body) body.FixedStep(dt);            // удержание курса тела при хвате
            if (arm) arm.FixedStep(dt);              // точки приводов кисти и локтя
            if (arm && arm.bodyContact) arm.bodyContact.Clear();   // касания тела этого шага прочитаны блоками «Тело» и «Рука»
            // накопленное с прошлого шага потрачено блоками — погасить (владелец накопленного — корень)
            // ЖЕСТ МЕЖДУ НАЖАТИЕМ И ХВАТОМ НЕ ТЕРЯЕТСЯ (2026-11-27): ЛКМ нажата, кисть ещё садится (до pressLand) — движение мыши
            // копится и достаётся жесту на первом шаге хвата. Дольше не копится: старый взмах через секунду не выстрелит.
            bool landing = state.wantGrip && !state.gripped && !state.heldItem;
            landTime = landing ? landTime + dt : 0f;
            if (!(landing && intent && landTime <= intent.pressLand)) state.pendingDelta = Vector2.zero;
            state.pendingPress = false; state.pendingRelease = false;
        }

        /// <summary>После кадра: камера читает интерполированную позу.</summary>
        public void LateStep(float dt)
        {
            if (cam) cam.Step(dt, state.frame);
            if (arm) arm.LateStep();
        }

        /// <summary>ПОСТАНОВКА ГЕРОЯ НА СТАРТ (тело и рука вместе): старт, клавиши песочницы, начало замера и повтора. Не движение в игре.</summary>
        /// <summary>Выключатель блока предметов (для проверки и сравнения): предмет под курсором не берётся.</summary>
        public static bool NoItems;
        public void PlaceAtStart(Vector3 pos, Quaternion rot)
        {
            if (grip) grip.Release();            // сначала отпустить: иначе перенос руки дёрнул бы то, за что она держится (предмет)
            body.PlaceAtStart(pos, rot);
            if (arm) arm.PlaceAtStart();
            if (intent) intent.ResetIntent();
            state.cursorItem = null;
            state.wantGrip = false; state.liftForce = 0f; state.pullVec = Vector3.zero; state.carryForce = 0f; state.hangLower = 0f; state.hanging = false; state.hangPullUp = false; state.slideWant = 0f;
            state.pendingDelta = Vector2.zero; state.pendingPress = false; state.pendingRelease = false;
        }
    }

    /// <summary>ОБЩЕЕ СОСТОЯНИЕ. У каждого поля ОДИН владелец (кто пишет); остальные только читают.</summary>
    public class Arm2State
    {
        /// <summary>Ввод этого кадра. Владелец: Arm2Hero.FrameStep.</summary>
        public Arm2InputFrame frame;
        /// <summary>Ход мыши, накопленный с прошлого шага физики. Владелец: Arm2Hero (копит); блок намерения — забирает.</summary>
        public Vector2 pendingDelta;
        /// <summary>ЛКМ нажали / отпустили с прошлого шага физики. Владелец: Arm2Hero (копит и гасит после блоков шага).</summary>
        public bool pendingPress, pendingRelease;
        /// <summary>Точка под курсором на первой поверхности. Владелец: Arm2Intent.</summary>
        public Vector3 cursorPoint; public bool cursorValid;
        /// <summary>Куда кисть ХОЧЕТ (центр кисти, мир). Владелец: Arm2Intent (в игре); постановка на старт сбрасывает.</summary>
        public Vector3 handTarget; public bool handTargetValid;
        /// <summary>Где кисть ЕСТЬ по физике и насколько отстала от цели. Владелец: Arm2Arm (чтение после шага физики).</summary>
        public Vector3 handPos; public float handLag;
        /// <summary>ЛКМ держат — кисть возьмётся, как только коснётся годной поверхности. Владелец: Arm2Grip.</summary>
        public bool wantGrip;
        /// <summary>Кисть сцеплена: с чем (null — мир), где, нормаль поверхности. Владелец: Arm2Grip.</summary>
        public bool gripped; public Rigidbody gripBody; public Vector3 gripPoint, gripNormal;
        /// <summary>ПЛОСКОСТЬ ТЯГИ при хвате: нормаль опоры ПОД ТЕЛОМ (для зацепов — как задал хват). В ней меряется расстояние плечо–кисть,
        /// идёт сила тяги и разбирается жест. Владелец: Arm2Grip ставит в момент хвата, Arm2Arm плавно ведёт за опорой тела.</summary>
        public Vector3 pullNormal = Vector3.up;
        /// <summary>При хвате: какое расстояние плечо–кисть хочет игрок (жест «на себя» — короче, «от себя» — длиннее).
        /// Владелец: Arm2Grip ставит в момент хвата, Arm2Intent меняет жестом.</summary>
        public float pullLength;
        /// <summary>При хвате: ближний предел тяги (сгиб до упора или расстояние в момент хвата, если кисть была ближе) и
        /// направление тяги вдоль поверхности в момент хвата. Владелец: Arm2Grip.</summary>
        public float pullMin; public Vector3 pullDir0;
        /// <summary>При хвате: курс тела, который держим (рыскание, градусы). Владелец: Arm2Grip в момент хвата (срез 4 — жест поворота).</summary>
        public float yawTarget;
        /// <summary>Задняя передача (едет спиной — руль перевёрнут). Владелец: Arm2Intent. Хват из-за спины (зеркальный). Владелец: Arm2Grip.</summary>
        public bool reverseGear, mirrorGrip;
        /// <summary>Сила, с которой рука поднимает тело на ступень (Н, вдоль gripNormal). Владелец: Arm2Arm; прикладывает Arm2Body на следующем шаге.</summary>
        public float liftForce;
        /// <summary>Сила тяги руки на тело при хвате (Н, мир; через центр масс). Владелец: Arm2Arm; прикладывает Arm2Body на следующем шаге.</summary>
        public Vector3 pullVec;
        /// <summary>ВИС: под телом пусто, рука несёт его вес (carryForce, Н, вверх через центр масс). Владелец: Arm2Arm.
        /// Arm2Intent: hangLower — на сколько метров опустить по жесту «от себя»; hangPullUp — просьба подтянуться.</summary>
        public bool hanging, hangPullUp; public float carryForce, hangLower;
        /// <summary>Рука вытянута до предела, а жест «от себя» продолжается: на сколько метров кисть должна съехать по поверхности
        /// вслед за телом. Владелец: Arm2Intent копит; Arm2Grip тратит (двигает точку хвата).</summary>
        public float slideWant;
        /// <summary>ЗАЦЕП (камень, перила) под курсором: цель кисти стоит на нём. Владелец: Arm2Intent. Хват за зацеп: Arm2Grip.</summary>
        public Proto.ProtoHold targetHold; public bool gripOnHold;
        /// <summary>ПРЕДМЕТ В РУКЕ (срез 9): кисть сцеплена с предметом, рука остаётся свободной (идёт за курсором), тело не тянется.
        /// heldLift — на сколько центр кисти выше обычного места над поверхностью (взялся за верх — кисть там и остаётся).
        /// Владелец: Arm2Grip. cursorItem — предмет под курсором в досягаемости руки; владелец: Arm2Intent.</summary>
        public Rigidbody heldItem; public Rigidbody cursorItem;
    }

    /// <summary>Ползунок настройки в игре (окно Arm2Tuning, F1). Автор: «ощущение настраивает автор сам, не дожидаясь правок».</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class Arm2TuneAttribute : Attribute
    {
        public readonly string label; public readonly float min, max;
        public Arm2TuneAttribute(string label, float min, float max) { this.label = label; this.min = min; this.max = max; }
    }

    /// <summary>Блок с ползунками: после смены значения окно зовёт ApplyTuning — блок переносит числа в Rigidbody / сустав.</summary>
    public interface IArm2Tunable { void ApplyTuning(); }
}
