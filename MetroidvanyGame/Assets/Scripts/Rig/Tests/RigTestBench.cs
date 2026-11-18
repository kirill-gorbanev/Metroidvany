using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Fortress.Rig.Human;
using Random = UnityEngine.Random;

namespace Fortress.Rig.Tests
{
    /// <summary>Стенд рига: ситуации на клавишах 1..9, мышь — тычок/перетаскивание, B — мяч, T — замедление,
    /// Пробел — пауза, G — линии рига, Tab — вести табурет (WASD), R — повторить ситуацию.</summary>
    public partial class RigTestBench : MonoBehaviour
    {
        [Header("Что ставить")]
        public GameObject stoolPrefab;
        public GameObject cratePrefab, barrelPrefab, bucketPrefab, mugPrefab;
        [Tooltip("Люди: крестьянин, следопыт, следопытка")] public GameObject[] humanPrefabs;
        [Header("Окружение (набор крепости)")]
        public GameObject stairsPrefab;
        public GameObject platformPrefab, doorWallPrefab, doorFramePrefab, doorPrefab, wallPrefab;
        public GameObject seatPrefab, tablePrefab, appleCratePrefab, metalCratePrefab;
        public Material floorMat, woodMat, ballMat;
        public BenchCamera cam;

        class Test
        {
            public int page;
            public string title;
            public string[] watch;
            public Action setup;
            public Action<float> tick;
        }

        readonly List<Test> tests = new List<Test>();
        int current = -1;
        Transform arena;
        float testTime;
        readonly float[] speeds = { 1f, 0.5f, 0.25f, 0.1f };
        int speedIdx;
        bool paused, help = true;
        [Tooltip("Тычок мышью — изменение скорости, м/с (одинаково ощутимо и табурету, и человеку)")] float poke = 1.8f;
        int page;
        static readonly string[] Pages = { "табурет", "люди", "окружение", "v3: отклик" };
        Rect panelRect;
        PhysicsMaterial crateMat;
        GUIStyle btn, btnOn;
        // ситуации окружения
        HumanPuppet envHuman;
        HumanInteract envHands;
        Rigidbody envItem;
        int envLeg;
        float envWait, envTimer;
        RigSeat envSeat;
        Vector3 envSpotA, envSpotB;
        class Pusher { public HumanPuppet h; public Rigidbody crate; public float z, wait; public int phase; }
        readonly List<Pusher> pushers = new List<Pusher>();
        HumanDriver possessedHuman;
        HumanPuppet pushTarget;
        float pushTimer;
        int pushIdx;
        static readonly float[] PushSteps = { 0.35f, 1.0f, 1.8f, 3.6f };
        Rigidbody grabbed, hand;
        SpringJoint grabJoint;
        float grabDist;
        RigWander possessed;
        float defaultFixed;
        string lastEvent = "";
        float lastEventTime = -99f;
        GUIStyle box, head, line, tagStyle;

        // обстрел
        Transform volleyTarget;
        float volleyTimer;
        int volleyIdx;
        static readonly float[] VolleySpeeds = { 3f, 5f, 7f, 9f, 11f, 13f };

        void Awake()
        {
            Application.runInBackground = true;      // стенд идёт и когда окно Unity не в фокусе
            defaultFixed = Time.fixedDeltaTime;
            Time.fixedDeltaTime = 0.01f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.42f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.19f, 0.16f);
            RenderSettings.fog = false;
            var showcase = GameObject.Find("Люди (витрина)");
            if (showcase) showcase.SetActive(false);      // витрина — только для редактора, в Play людей ставит стенд
            Define();
        }

        void Start()
        {
            int first = tests.FindIndex(x => x.page == 1);
            int last = PlayerPrefs.GetInt("RigBench.Test", first);
            Run(last >= 0 && last < tests.Count ? last : Mathf.Max(0, first));
        }

        void OnDestroy()
        {
            Time.fixedDeltaTime = defaultFixed;
            Time.timeScale = 1f;
        }

        // ---------------------------------------------------------------- ситуации
        void Define()
        {
            tests.Add(new Test
            {
                title = "Табурет ходит",
                watch = new[]
                {
                    "ножки шагают парами — две напротив друг друга, потом другие две",
                    "опорная ножка стоит на месте и не скользит, пока сиденье едет над ней",
                    "сиденье почти не качается; на углах табурет переступает и поворачивает",
                },
                setup = () =>
                {
                    var s = Stool(new Vector3(-2.5f, 0f, -2.5f), Quaternion.identity);
                    var w = s.GetComponent<RigWander>();
                    w.points = Square(2.5f); w.speed = 0.7f;
                    Follow(s);
                },
            });
            tests.Add(new Test
            {
                title = "Ступенька и хлам на полу",
                watch = new[]
                {
                    "ножка ставится на ступеньку (15 см), сиденье поднимается вслед и остаётся ровным",
                    "на досках и кирпичах ножки встают на разную высоту — табурет не заваливается",
                    "лёгкие доски табурет задевает и сдвигает",
                },
                setup = () =>
                {
                    Box("Ступень", new Vector3(0f, 0.075f, 0f), new Vector3(2.4f, 0.15f, 5f), floorMat, false, 0f);
                    Box("Ступень 2", new Vector3(0f, 0.2f, 0f), new Vector3(0.9f, 0.1f, 5f), floorMat, false, 0f);
                    for (int i = 0; i < 9; i++)
                    {
                        var p = new Vector3(Random.Range(-4.5f, -1.8f) * (i % 2 == 0 ? 1 : -1), 0.03f, Random.Range(-2.2f, 2.2f));
                        Box("Доска", p, new Vector3(0.75f, 0.035f, 0.14f), woodMat, true, 0.7f).rotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        var p = new Vector3(Random.Range(-4.5f, -1.8f) * (i % 2 == 0 ? -1 : 1), 0.04f, Random.Range(-2.2f, 2.2f));
                        Box("Кирпич", p, new Vector3(0.22f, 0.08f, 0.12f), floorMat, false, 0f).rotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
                    }
                    var s = Stool(new Vector3(-4.5f, 0f, 0.6f), Quaternion.identity);
                    var w = s.GetComponent<RigWander>();
                    w.points = new[] { new Vector3(4.5f, 0f, 0.6f), new Vector3(4.5f, 0f, -0.9f), new Vector3(-4.5f, 0f, -0.9f), new Vector3(-4.5f, 0f, 0.6f) };
                    w.speed = 0.55f;
                    Follow(s);
                },
            });
            tests.Add(new Test
            {
                title = "Обстрел мячами",
                watch = new[]
                {
                    "мячи летят всё сильнее: 3, 5, 7, 9, 11, 13 м/с; лежачего не бьют — ждут, пока встанет",
                    "слабый — переступает в сторону удара и идёт дальше; средний — шатается (тонус падает)",
                    "сильный — обмякает и падает, потом встаёт или переворачивается сам",
                },
                setup = () =>
                {
                    var s = Stool(new Vector3(-2f, 0f, -2f), Quaternion.identity);
                    var w = s.GetComponent<RigWander>();
                    w.points = Square(2f); w.speed = 0.6f;
                    volleyTarget = s; volleyTimer = 0f; volleyIdx = 0;
                    Follow(s);
                },
                tick = dt =>
                {
                    if (!volleyTarget) return;
                    var vr = volleyTarget.GetComponent<RigBody>();
                    // лежачего не бьют: следующий мяч — когда встал и отдышался
                    if (vr && (vr.tone < 0.8f || Vector3.Dot(volleyTarget.up, Vector3.up) < 0.8f)) { volleyTimer = 0f; return; }
                    volleyTimer += dt;
                    if (volleyTimer < 2.1f) return;
                    volleyTimer = 0f;
                    float sp = VolleySpeeds[volleyIdx++ % VolleySpeeds.Length];
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    // на упреждение: куда табурет дойдёт, пока летит мяч
                    var trb = volleyTarget.GetComponent<Rigidbody>();
                    Vector3 vt = trb ? new Vector3(trb.linearVelocity.x, 0f, trb.linearVelocity.z) : Vector3.zero;
                    Vector3 to = volleyTarget.position + Vector3.up * 0.05f + vt * (4f / sp);
                    Throw(to + dir * 4f + Vector3.up * 0.3f, to, sp);
                    Note($"мяч {sp:F0} м/с");
                },
            });
            tests.Add(new Test
            {
                title = "Кверху ногами",
                watch = new[]
                {
                    "лежащий табурет сначала сучит ножками",
                    "потом раскачивается, подпрыгивает и переворачивается на ножки",
                    "встав — переступает, выравнивается и стоит",
                },
                setup = () =>
                {
                    Stool(new Vector3(-2f, 0.35f, 0f), Quaternion.Euler(180f, 0f, 0f)).GetComponent<RigWander>().idle = true;
                    Stool(new Vector3(0f, 0.35f, 0f), Quaternion.Euler(0f, 30f, 90f)).GetComponent<RigWander>().idle = true;
                    Stool(new Vector3(2f, 0.7f, 0f), Quaternion.Euler(70f, 20f, 0f)).GetComponent<RigWander>().idle = true;
                    if (cam) { cam.follow = null; cam.focus = new Vector3(0f, 0.3f, 0f); cam.distance = 6f; }
                },
            });
            tests.Add(new Test
            {
                title = "Ведро на сиденье",
                watch = new[]
                {
                    "табурет несёт ведро: ведро лежит физически, его ничто не держит",
                    "на поворотах и при толчках ведро ёрзает и может упасть",
                    "кружка на втором табурете — лёгкая, её стряхнуть проще",
                },
                setup = () =>
                {
                    var s = Stool(new Vector3(-1.5f, 0f, -1.5f), Quaternion.identity);
                    var w = s.GetComponent<RigWander>();
                    w.points = Square(1.5f); w.speed = 0.45f;
                    float top = SeatTop();
                    if (bucketPrefab) Prop(bucketPrefab, s.position + Vector3.up * top, 0f, 1.2f);
                    var s2 = Stool(new Vector3(1.5f, 0f, 1.5f), Quaternion.identity);
                    var w2 = s2.GetComponent<RigWander>();
                    w2.points = new[] { new Vector3(1.5f, 0f, -1.5f), new Vector3(-1.5f, 0f, 1.5f) }; w2.speed = 0.5f;
                    if (mugPrefab) Prop(mugPrefab, s2.position + Vector3.up * top, 0f, 0.3f);
                    Follow(s);
                },
            });
            tests.Add(new Test
            {
                title = "Стадо среди ящиков",
                watch = new[]
                {
                    "табуреты толкаются между собой и с ящиками — всё по физике",
                    "ящики и бочки сдвигаются; табурет, упёршись, обходит или протискивается",
                    "столкнувшись, табурет шатается и переступает",
                },
                setup = () =>
                {
                    for (int i = 0; i < 4; i++) if (cratePrefab) Prop(cratePrefab, RandomFloor(4.5f), Random.Range(0f, 90f), 4f);
                    for (int i = 0; i < 3; i++) if (barrelPrefab) Prop(barrelPrefab, RandomFloor(4.5f), 0f, 7f);
                    for (int i = 0; i < 6; i++)
                    {
                        var s = Stool(RandomFloor(4f), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                        var w = s.GetComponent<RigWander>();
                        w.radius = 4.5f; w.speed = Random.Range(0.45f, 0.8f); w.pause = Random.Range(0.2f, 1.2f);
                    }
                    if (cam) { cam.follow = null; cam.focus = new Vector3(0f, 0.3f, 0f); cam.distance = 11f; cam.pitch = 40f; }
                },
            });
            tests.Add(new Test
            {
                title = "Ты — табурет (WASD)",
                watch = new[]
                {
                    "WASD — вести табурет, Shift — быстрее; камера за ним",
                    "попробуй врезаться в ящик, бочку, ведро, в другой табурет",
                    "мышью можно ткнуть или потащить что угодно, даже себя",
                },
                setup = () =>
                {
                    var me = Stool(Vector3.zero, Quaternion.identity);
                    for (int i = 0; i < 3; i++) if (cratePrefab) Prop(cratePrefab, RandomFloor(5f), Random.Range(0f, 90f), 4f);
                    for (int i = 0; i < 2; i++) if (barrelPrefab) Prop(barrelPrefab, RandomFloor(5f), 0f, 7f);
                    for (int i = 0; i < 3; i++) if (bucketPrefab) Prop(bucketPrefab, RandomFloor(5f), 0f, 1.2f);
                    for (int i = 0; i < 2; i++)
                    {
                        var s = Stool(RandomFloor(4f), Quaternion.identity);
                        s.GetComponent<RigWander>().radius = 4.5f;
                    }
                    Possess(me.GetComponent<RigWander>());
                },
            });

            // ======================= страница «люди»
            tests.Add(new Test
            {
                page = 1, title = "Толчки в грудь: слабо → сильно",
                watch = new[]
                {
                    "толчки по очереди: 0.35, 1.0, 1.8, 3.6 м/с; следующий — когда снова стоит",
                    "слабый — качнулся на месте; средний — переступает, ловя равновесие",
                    "сильный — падает, руки вперёд; полежав — встаёт (мышцы и опора возвращаются плавно)",
                },
                setup = () =>
                {
                    var h = Human(0, Vector3.zero, 180f);
                    if (!h) return;
                    h.driver.idle = true;
                    pushTarget = h; pushTimer = 0f; pushIdx = 0;
                    Follow(h.body.transform, 4.2f);
                },
                tick = dt => PushTick(dt, PushSteps, false),
            });
            tests.Add(new Test
            {
                page = 1, title = "Подножка: бревно и табурет на пути",
                watch = new[]
                {
                    "идёт туда-обратно, не глядя под ноги; на пути низкое бревно (18 см) и лежащий табурет",
                    "нога цепляется — тело уходит вперёд, он шагает, ловя равновесие, или падает",
                    "упав — встаёт и идёт дальше",
                },
                setup = () =>
                {
                    var log = Box("Бревно", new Vector3(-1.2f, 0.09f, 0f), new Vector3(0.18f, 0.18f, 2.5f), woodMat, false, 0f);
                    var st = Instantiate(stoolPrefab, new Vector3(1.6f, 0.22f, 0f), Quaternion.Euler(0f, 0f, 90f), arena);
                    st.name = "Табурет (лежит)";
                    st.GetComponent<LegGait>().enabled = false;      // обычный, не живой — просто лежит
                    var h = Human(0, new Vector3(-4f, 0f, 0f), 90f);
                    if (!h) return;
                    // идёт, не глядя под ноги: «призрак» препятствий не замечает, а настоящие ноги в них врезаются
                    var gcc = h.driver.GetComponent<CharacterController>();
                    foreach (var col in log.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(gcc, col);
                    foreach (var col in st.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(gcc, col);
                    h.driver.points = new[] { new Vector3(4f, 0f, 0f), new Vector3(-4f, 0f, 0f) };
                    Follow(h.body.transform, 5f);
                },
            });
            tests.Add(new Test
            {
                page = 1, title = "Край помоста",
                watch = new[]
                {
                    "стоит у края помоста (1 м); его толкают к краю",
                    "переступает — а шагнуть некуда: срывается вниз",
                    "приземляется, лежит, встаёт внизу",
                },
                setup = () =>
                {
                    Box("Помост", new Vector3(0f, 0.5f, 0f), new Vector3(3f, 1f, 3f), floorMat, false, 0f);
                    var h = Human(1, new Vector3(0.9f, 1f, 0f), 270f);
                    if (!h) return;
                    h.driver.idle = true;
                    pushTarget = h; pushTimer = 0f; pushIdx = 0;
                    Follow(h.body.transform, 5f);
                },
                tick = dt => PushTick(dt, new[] { 1.3f }, true),
            });
            tests.Add(new Test
            {
                page = 1, title = "Табуреты под ногами",
                watch = new[]
                {
                    "человек ходит туда-обратно, вокруг бродят живые табуреты",
                    "табурет, попав под ногу, отлетает и встаёт; человек спотыкается о него",
                    "всё по физике: никто никого не «проходит насквозь»",
                },
                setup = () =>
                {
                    var h = Human(2, new Vector3(-3f, 0f, 0f), 90f);
                    if (!h) return;
                    h.driver.points = new[] { new Vector3(3f, 0f, 0.5f), new Vector3(-3f, 0f, -0.5f) };
                    for (int i = 0; i < 4; i++) Stool(RandomFloor(2.5f), Quaternion.identity).GetComponent<RigWander>().radius = 3f;
                    Follow(h.body.transform, 5f);
                },
            });
            tests.Add(new Test
            {
                page = 1, title = "Бочки в человека",
                watch = new[]
                {
                    "в человека летят бочки (14 кг) всё быстрее: 4, 6, 8, 10 м/с",
                    "медленная — отшатнулся; быстрая — сбила с ног",
                    "бочки катятся дальше и сшибают друг друга",
                },
                setup = () =>
                {
                    var h = Human(1, Vector3.zero, 180f);
                    if (!h) return;
                    h.driver.idle = true;
                    pushTarget = h; pushTimer = 0f; pushIdx = 0;
                    Follow(h.body.transform, 5.5f);
                },
                tick = dt => BarrelTick(dt),
            });
            tests.Add(new Test
            {
                page = 1, title = "Ты — человек (WASD)",
                watch = new[]
                {
                    "WASD — идти, Shift — бежать; камера за тобой",
                    "врезайся в ящики, бочки, табуреты; ткни себя мышью",
                    "Tab — переключиться на табурет, Esc — отпустить",
                },
                setup = () =>
                {
                    var h = Human(0, Vector3.zero, 0f);
                    if (!h) return;
                    for (int i = 0; i < 3; i++) if (cratePrefab) Prop(cratePrefab, RandomFloorAway(5f, 1.6f), Random.Range(0f, 90f), 4f);
                    for (int i = 0; i < 2; i++) if (barrelPrefab) Prop(barrelPrefab, RandomFloorAway(5f, 1.6f), 0f, 7f);
                    for (int i = 0; i < 3; i++) Stool(RandomFloorAway(4f, 1.6f), Quaternion.identity).GetComponent<RigWander>().radius = 4.5f;
                    PossessHuman(h.driver);
                },
            });
            DefineEnvironment();
            DefineV3();
        }

        // ---------------------------------------------------------------- страница «окружение»
        void DefineEnvironment()
        {
            tests.Add(new Test
            {
                page = 2, title = "Лестница: вверх и вниз",
                watch = new[]
                {
                    "ходит через помост по лестницам крепости (ступени 20 см)",
                    "каждая стопа встаёт на свою ступень; таз поднимается плавно, без рывков",
                    "ткни его мышью на лестнице — оступится, может скатиться",
                },
                setup = () =>
                {
                    StairsBlock(Vector3.zero);
                    var h = Human(0, new Vector3(-5.5f, 0f, 0f), 90f);
                    if (!h) return;
                    h.driver.points = new[] { new Vector3(5.5f, 0f, 0f), new Vector3(-5.5f, 0f, 0f) };
                    h.driver.pause = 0.8f;
                    Follow(h.body.transform, 5.5f);
                    if (cam) { cam.yaw = 0f; cam.pitch = 14f; }
                },
            });
            tests.Add(new Test
            {
                page = 2, title = "Ноша: со стола на помост и обратно",
                watch = new[]
                {
                    "берёт ящик яблок (7 кг) со стола, несёт по лестнице, ставит на помост; потом обратно",
                    "кисти держат ящик за бока; ящик качается в руках, руки чуть провисают под весом",
                    "с ношей идёт медленнее; ставя на пол — приседает",
                },
                setup = () =>
                {
                    StairsBlock(Vector3.zero);
                    Kit(tablePrefab, new Vector3(-6.3f, 0f, 0f), 90f);
                    envItem = appleCratePrefab ? Prop(appleCratePrefab, new Vector3(-6.0f, 0.83f, 0f), 90f, 7f) : null;
                    var h = Human(1, new Vector3(-3.6f, 0f, 0f), 270f);
                    if (!h) return;
                    SetEnvHuman(h);
                    envSpotA = new Vector3(0.1f, 1.0f, 0f);
                    envSpotB = new Vector3(-6.0f, 0.81f, 0f);
                    envLeg = 0; envWait = 0f;
                    Follow(h.body.transform, 6f);
                    if (cam) { cam.yaw = 0f; cam.pitch = 18f; }
                },
                tick = dt => CarryShuttle(dt),
            });
            tests.Add(new Test
            {
                page = 2, title = "Ноша и помехи",
                watch = new[]
                {
                    "носит ящик по кругу; в ящик летят мячи (3 кг, 12 м/с), в человека — бочки, под ноги лезут табуреты",
                    "сильный удар выбивает ящик из рук; упав — роняет",
                    "оправившись, идёт к ящику, поднимает и несёт дальше",
                },
                setup = () =>
                {
                    envItem = appleCratePrefab ? Prop(appleCratePrefab, new Vector3(1f, 0.02f, 0f), 0f, 7f) : null;
                    var h = Human(0, new Vector3(-1f, 0f, 0f), 90f);
                    if (!h) return;
                    SetEnvHuman(h);
                    h.driver.points = Square(2.6f);
                    h.driver.pause = 0.2f;
                    for (int i = 0; i < 2; i++) Stool(RandomFloorAway(3.5f, 1.8f), Quaternion.identity).GetComponent<RigWander>().radius = 3.5f;
                    envWait = 0f; envTimer = 0f; envLeg = 0;
                    Follow(h.body.transform, 6f);
                },
                tick = dt => CarryHarass(dt),
            });
            tests.Add(new Test
            {
                page = 2, title = "Толкать ящики: лёгкий, тяжёлый, неподъёмный",
                watch = new[]
                {
                    "трое толкают ящики: 25 кг, 90 кг и окованный 400 кг",
                    "лёгкий едет легко; тяжёлый — медленно, человек налегает всем телом",
                    "неподъёмный не сдвинуть: упирается, сдаётся, мотает головой — и пробует снова",
                },
                setup = () =>
                {
                    pushers.Clear();
                    float[] mass = { 25f, 90f, 400f };
                    for (int i = 0; i < 3; i++)
                    {
                        float z = 3f - 3f * i;
                        var pf = i < 2 ? cratePrefab : (metalCratePrefab ? metalCratePrefab : cratePrefab);
                        var c = Prop(pf, new Vector3(-1.5f, 0.01f, z), 0f, mass[i]);
                        c.name = $"Ящик {mass[i]:F0} кг";
                        var h = Human(i, new Vector3(-4.5f, 0f, z), 90f);
                        if (!h) continue;
                        pushers.Add(new Pusher { h = h, crate = c, z = z, wait = 0.5f + 0.4f * i });
                        Hook(h);
                    }
                    if (cam) { cam.follow = null; cam.focus = new Vector3(0f, 0.6f, 0f); cam.distance = 12f; cam.pitch = 32f; cam.yaw = 20f; }
                },
                tick = dt => PushersTick(dt),
            });
            tests.Add(new Test
            {
                page = 2, title = "Дверь",
                watch = new[]
                {
                    "ходит туда-обратно через дверь в стене; дверь на петлях, закрывается сама",
                    "подходя, выставляет руку и открывает дверь толчком; дверь качается и хлопает за спиной",
                    "ткни дверь мышью ему навстречу — получит дверью",
                },
                setup = () =>
                {
                    Kit(doorWallPrefab, Vector3.zero, 90f);
                    Kit(doorFramePrefab, Vector3.zero, 90f);
                    foreach (float z in new[] { -4f, -2f, 2f, 4f }) Kit(wallPrefab, new Vector3(0f, 0f, z), 90f);
                    Door(new Vector3(0f, 0f, -0.56f), 90f);
                    var h = Human(1, new Vector3(-4f, 0f, 0f), 90f);
                    if (!h) return;
                    h.driver.points = new[] { new Vector3(4f, 0f, 0.1f), new Vector3(-4f, 0f, -0.1f) };
                    h.driver.pause = 1.0f;
                    Hook(h);
                    Follow(h.body.transform, 5.5f);
                    if (cam) { cam.yaw = 30f; cam.pitch = 20f; }
                },
            });
            tests.Add(new Test
            {
                page = 2, title = "Опереться о стену",
                watch = new[]
                {
                    "стоит боком к стене; его толкают к ней: 1.0, 1.8, 3.0 м/с",
                    "по ходу — стена: выставляет ладони и упирается, не бьётся лицом",
                    "сильный толчок: врезается, упирается, может сползти и упасть у стены",
                },
                setup = () =>
                {
                    var h = Human(2, new Vector3(0f, 0f, -8.75f), 90f);
                    if (!h) return;
                    h.driver.idle = true;
                    pushTarget = h; pushTimer = 0f; pushIdx = 0;
                    Follow(h.body.transform, 4.5f);
                    if (cam) { cam.yaw = 90f; cam.pitch = 14f; }      // сбоку, вдоль стены
                },
                tick = dt => PushTick(dt, new[] { 1.0f, 1.8f, 3.0f }, false, Vector3.back),
            });
            tests.Add(new Test
            {
                page = 2, title = "Лавка: сесть, посидеть, встать",
                watch = new[]
                {
                    "подходит к лавке, поворачивается спиной, садится — таз физически ложится на сиденье",
                    "сидит; табуреты лезут под ноги; ткни его — качнётся, но усидит; сильно — свалится",
                    "встаёт, прохаживается и садится снова на другое место",
                },
                setup = () =>
                {
                    envSeat = Seat(Vector3.zero, 0f);
                    var h = Human(0, new Vector3(2.5f, 0f, 2.5f), 225f);
                    if (!h) return;
                    SetEnvHuman(h);
                    h.driver.idle = true;
                    for (int i = 0; i < 2; i++) Stool(RandomFloorAway(3.5f, 2f), Quaternion.identity).GetComponent<RigWander>().radius = 3.5f;
                    envWait = 0f; envTimer = 0f; envLeg = 0;
                    Follow(h.body.transform, 5f);
                },
                tick = dt => BenchSitter(dt),
            });
            tests.Add(new Test
            {
                page = 2, title = "Ты — человек: E — взять / поставить / толкать / сесть",
                watch = new[]
                {
                    "WASD — идти, Shift — бежать; E — действие с тем, что перед тобой",
                    "ящик яблок (на столе и на полу) — взять и поставить; большой ящик — толкать; лавка — сесть",
                    "с ношей — на лестницу и на помост; Tab — переключиться, Esc — отпустить",
                },
                setup = () =>
                {
                    StairsBlock(new Vector3(4.6f, 0f, 0f));
                    Kit(tablePrefab, new Vector3(-5.5f, 0f, 3.6f), 0f);
                    if (appleCratePrefab)
                    {
                        Prop(appleCratePrefab, new Vector3(-5.5f, 0.83f, 3.3f), 0f, 7f);
                        Prop(appleCratePrefab, new Vector3(-2.2f, 0.02f, 2.6f), 30f, 9f);
                    }
                    if (cratePrefab) Prop(cratePrefab, new Vector3(-3f, 0.01f, -3f), 0f, 60f).name = "Ящик 60 кг";
                    if (barrelPrefab) Prop(barrelPrefab, new Vector3(-5.5f, 0.01f, -1f), 0f, 30f).name = "Бочка 30 кг";
                    Seat(new Vector3(0f, 0f, 6f), 0f);
                    var h = Human(0, Vector3.zero, 0f);
                    if (!h) return;
                    Hook(h);
                    PossessHuman(h.driver);
                },
            });
        }

        void SetEnvHuman(HumanPuppet h)
        {
            envHuman = h;
            envHands = h ? h.GetComponent<HumanInteract>() : null;
            Hook(h);
        }

        /// <summary>Подписаться на «реплики» рук человека (взял, выронил, не сдвинуть) — в строку событий стенда.</summary>
        void Hook(HumanPuppet h)
        {
            var it = h ? h.GetComponent<HumanInteract>() : null;
            if (it) { string n = h.name; it.Said += s => Note(n + ": " + s); }
        }

        bool Free(HumanPuppet h) => h && h.state == HumanPuppet.State.Active;

        void CarryShuttle(float dt)
        {
            if (!Free(envHuman) || !envHands || !envItem) return;
            if (envHands.task != HumanInteract.Task.None) { envWait = 0f; return; }
            envWait += dt;
            if (envWait < 1.0f) return;
            envWait = 0f;
            // ящик там, где он есть (мог упасть) → отнести на другое место
            bool onPlatform = envItem.position.x > -1.2f && envItem.position.y > 0.8f;
            envHands.PickUp(envItem);
            envHands.PutDown(onPlatform ? envSpotB : envSpotA);
        }

        void CarryHarass(float dt)
        {
            if (!envHuman || !envHands || !envItem) return;
            if (Free(envHuman) && envHands.task == HumanInteract.Task.None && !envHands.Carrying)
            {
                envWait += dt;
                if (envWait > 1.2f) { envWait = 0f; envHands.PickUp(envItem); }
            }
            if (!envHands.Carrying || envHands.task != HumanInteract.Task.Carry) { envTimer = 0f; return; }
            envTimer += dt;
            if (envTimer < 4f) return;
            envTimer = 0f;
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            // на упреждение: куда ящик/человек дойдут, пока летит снаряд
            Vector3 lead = new Vector3(envHuman.driver.Velocity.x, 0f, envHuman.driver.Velocity.z);
            if (envLeg++ % 2 == 0)
            {
                float sp = 12f;
                Vector3 to = envItem.worldCenterOfMass + lead * (4f / sp);
                Throw(to + dir * 4f + Vector3.up * 0.3f, to, sp, 3f);
                Note("мяч в ящик 12 м/с");
            }
            else if (barrelPrefab)
            {
                float sp = 7f;
                Vector3 to = envHuman.Chest.position + lead * (4f / sp);
                var rb = Prop(barrelPrefab, to - dir * 4f + Vector3.up * 0.1f, 0f, 14f);
                rb.transform.rotation = Quaternion.LookRotation(Vector3.up, dir);
                rb.linearVelocity = dir * sp + Vector3.up * (0.5f * -Physics.gravity.y * (4f / sp));
                Destroy(rb.gameObject, 10f);
                Note("бочка 7 м/с");
            }
        }

        void PushersTick(float dt)
        {
            foreach (var p in pushers)
            {
                if (!Free(p.h)) continue;
                var it = p.h.GetComponent<HumanInteract>();
                var d = p.h.driver;
                if (it.task != HumanInteract.Task.None) { p.wait = 0f; continue; }
                switch (p.phase)
                {
                    case 0:     // толкать вправо
                        p.wait += dt;
                        if (p.wait < 1.2f) break;
                        p.wait = 0f;
                        if (p.crate.position.x > 2.2f) { p.phase = 1; break; }
                        it.Push(p.crate, Vector3.right, 3.2f);
                        break;
                    case 1:     // обойти ящик сбоку
                        d.goTo = new Vector3(p.crate.position.x, 0f, p.z + (p.z >= 0f ? 1.4f : -1.4f));
                        if (d.Arrived) p.phase = 2;
                        break;
                    case 2:     // зайти с другой стороны и толкать обратно
                        d.goTo = null;
                        it.Push(p.crate, Vector3.left, 3.2f);
                        p.phase = 3;
                        break;
                    case 3:
                        p.wait += dt;
                        if (p.wait < 1.2f) break;
                        p.wait = 0f;
                        if (p.crate.position.x < -1.0f) { p.phase = 4; break; }
                        it.Push(p.crate, Vector3.left, 3.2f);
                        break;
                    case 4:
                        d.goTo = new Vector3(p.crate.position.x, 0f, p.z + (p.z >= 0f ? 1.4f : -1.4f));
                        if (d.Arrived) { d.goTo = null; p.phase = 0; p.wait = 1.2f; }
                        break;
                }
            }
        }

        void BenchSitter(float dt)
        {
            if (!Free(envHuman) || !envHands || !envSeat) return;
            var it = envHands;
            if (it.task == HumanInteract.Task.Sitting)
            {
                envTimer += dt;
                if (envTimer > 6f) { envTimer = 0f; it.Stand(); }
                return;
            }
            if (it.task != HumanInteract.Task.None) { envWait = 0f; return; }
            envWait += dt;
            var d = envHuman.driver;
            if (envLeg % 2 == 0)
            {
                if (envWait < 1.0f) return;
                envWait = 0f; envLeg++;
                // сесть на ближайшее к случайной точке место лавки
                var near = new Vector3(Random.Range(-1.2f, 1.2f), 0f, d.transform.position.z >= 0f ? 1f : -1f);
                envSeat.Nearest(near, out var p, out var fw);
                d.goTo = null;
                it.SitOn(p, fw);
                envTimer = 0f;
            }
            else
            {
                // прогуляться вдоль лавки (по своей стороне) и вернуться сесть на другое место
                if (!d.goTo.HasValue)
                {
                    float side = d.transform.position.z >= 0f ? 1f : -1f;
                    float x = d.transform.position.x > 0f ? Random.Range(-3f, -1.5f) : Random.Range(1.5f, 3f);
                    d.goTo = new Vector3(x, 0f, side * Random.Range(1.4f, 2.4f));
                    envWait = 0f;
                }
                else if ((d.Arrived && envWait > 1.5f) || envWait > 7f) { d.goTo = null; envLeg++; envWait = 0f; }
            }
        }

        // ---------------------------------------------------------------- толчки людям
        void PushTick(float dt, float[] steps, bool towardEdge) => PushTick(dt, steps, towardEdge, null);

        void PushTick(float dt, float[] steps, bool towardEdge, Vector3? fixedDir)
        {
            if (!pushTarget) return;
            if (pushTarget.state != HumanPuppet.State.Active) { pushTimer = 0f; return; }
            pushTimer += dt;
            if (pushTimer < 2.5f) return;
            pushTimer = 0f;
            float dv = steps[pushIdx++ % steps.Length];
            Vector3 dir;
            if (fixedDir.HasValue) dir = fixedDir.Value;
            else if (towardEdge) dir = Vector3.right;
            else { float a = Random.Range(0f, Mathf.PI * 2f); dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); }
            // толчок = изменение скорости всего тела (3/4 поровну всем частям) + добавка в грудь (наклоняет корпус)
            var chest = pushTarget.Chest;
            foreach (var rb in pushTarget.body.Bodies) rb.AddForce(dir * dv * 0.75f, ForceMode.VelocityChange);
            chest.AddForce(dir * dv * 0.9f, ForceMode.VelocityChange);
            float imp = dv * pushTarget.body.Mass;
            pushTarget.body.Hit(chest.gameObject, imp, dir);
            Note($"толчок {dv:F2} м/с");
        }

        void BarrelTick(float dt)
        {
            if (!pushTarget || !barrelPrefab) return;
            if (pushTarget.state != HumanPuppet.State.Active) { pushTimer = 0f; return; }
            pushTimer += dt;
            if (pushTimer < 3f) return;
            pushTimer = 0f;
            float sp = new[] { 4f, 6f, 8f, 10f }[pushIdx++ % 4];
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 to = pushTarget.Chest.position;
            var rb = Prop(barrelPrefab, to - dir * 4f + Vector3.up * 0.1f, 0f, 14f);
            rb.transform.rotation = Quaternion.LookRotation(Vector3.up, dir);
            rb.linearVelocity = dir * sp + Vector3.up * (0.5f * -Physics.gravity.y * (4f / sp));
            Destroy(rb.gameObject, 12f);
            Note($"бочка {sp:F0} м/с");
        }


        // ---------------------------------------------------------------- запуск
        void Run(int i)
        {
            if (i < 0 || i >= tests.Count) return;
            EndGrab();
            Release();
            if (arena) { arena.gameObject.SetActive(false); Destroy(arena.gameObject); }
            arena = new GameObject("Ситуация").transform;
            volleyTarget = null;
            pushTarget = null;
            envHuman = null; envHands = null; envItem = null; envSeat = null;
            pushers.Clear();
            ResetV3();
            current = i;
            PlayerPrefs.SetInt("RigBench.Test", i);
            page = tests[i].page;
            testTime = 0f;
            Random.InitState(1234 + i);
            tests[i].setup?.Invoke();
            Note("запуск: " + tests[i].title);
        }

        void Update()
        {
            Keys();
            MouseActions();
            if (current >= 0 && !paused)
            {
                testTime += Time.deltaTime;
                tests[current].tick?.Invoke(Time.deltaTime);
            }
            InputV3();
            if (possessedHuman)
            {
                var k = Keyboard.current;
                Vector2 mv = Vector2.zero;
                if (k != null)
                {
                    mv.x = (k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0);
                    mv.y = (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0);
                }
                Transform c = cam ? cam.transform : Camera.main.transform;
                Vector3 f = Vector3.ProjectOnPlane(c.forward, Vector3.up).normalized, r = Vector3.Cross(Vector3.up, f);
                float sp = (k != null && k.leftShiftKey.isPressed) ? possessedHuman.runSpeed : possessedHuman.walkSpeed;
                possessedHuman.possessVelocity = Vector3.ClampMagnitude(f * mv.y + r * mv.x, 1f) * sp;
            }
            if (possessed)
            {
                var k = Keyboard.current;
                Vector2 mv = Vector2.zero;
                if (k != null)
                {
                    mv.x = (k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0);
                    mv.y = (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0);
                }
                Transform c = cam ? cam.transform : Camera.main.transform;
                Vector3 f = Vector3.ProjectOnPlane(c.forward, Vector3.up).normalized, r = Vector3.Cross(Vector3.up, f);
                var gait = possessed.GetComponent<LegGait>();
                float sp = (k != null && k.leftShiftKey.isPressed) ? 1f : 0.6f;
                possessed.possessVelocity = Vector3.ClampMagnitude(f * mv.y + r * mv.x, 1f) * gait.maxSpeed * sp;
            }
        }

        void Keys()
        {
            var k = Keyboard.current;
            if (k == null) return;
            var digits = new[] { k.digit1Key, k.digit2Key, k.digit3Key, k.digit4Key, k.digit5Key, k.digit6Key, k.digit7Key, k.digit8Key, k.digit9Key };
            var onPage = new List<int>();
            for (int i = 0; i < tests.Count; i++) if (tests[i].page == page) onPage.Add(i);
            for (int i = 0; i < digits.Length && i < onPage.Count; i++) if (digits[i].wasPressedThisFrame) Run(onPage[i]);
            if (k.periodKey.wasPressedThisFrame || k.pageDownKey.wasPressedThisFrame) { page = (page + 1) % Pages.Length; Note("страница: " + Pages[page]); }
            if (k.commaKey.wasPressedThisFrame || k.pageUpKey.wasPressedThisFrame) { page = (page + Pages.Length - 1) % Pages.Length; Note("страница: " + Pages[page]); }
            if (k.rKey.wasPressedThisFrame) Run(current);
            if (k.eKey.wasPressedThisFrame && possessedHuman)
            {
                var it = possessedHuman.GetComponentInParent<HumanPuppet>()?.GetComponent<HumanInteract>();
                if (it) Note(it.Use());
            }
            if (k.spaceKey.wasPressedThisFrame) { paused = !paused; ApplyTime(); }
            if (k.tKey.wasPressedThisFrame) { speedIdx = (speedIdx + 1) % speeds.Length; ApplyTime(); }
            if (k.gKey.wasPressedThisFrame) RigDraw.enabled = !RigDraw.enabled;
            if (k.hKey.wasPressedThisFrame) help = !help;
            if (k.tabKey.wasPressedThisFrame) PossessNext();
            if (k.escapeKey.wasPressedThisFrame) Release();
            if (k.fKey.wasPressedThisFrame) FollowNext();
            if (k.equalsKey.wasPressedThisFrame || k.numpadPlusKey.wasPressedThisFrame) { poke = Mathf.Min(12f, poke * 1.4f); Note($"сила тычка {poke:F1} м/с"); }
            if (k.minusKey.wasPressedThisFrame || k.numpadMinusKey.wasPressedThisFrame) { poke = Mathf.Max(0.2f, poke / 1.4f); Note($"сила тычка {poke:F1} м/с"); }
            if (k.bKey.wasPressedThisFrame)
            {
                var c = CamComp();
                var m = Mouse.current;
                Ray ray = c.ScreenPointToRay(m != null ? m.position.ReadValue() : new Vector2(Screen.width / 2f, Screen.height / 2f));
                Vector3 to = Physics.Raycast(ray, out var h, 60f, RigLayers.SolidMask, QueryTriggerInteraction.Ignore) ? h.point : ray.GetPoint(10f);
                Throw(c.transform.position + c.transform.forward * 0.6f, to, 12f);
                Note("мяч 12 м/с");
            }
        }

        void ApplyTime() => Time.timeScale = paused ? 0f : speeds[speedIdx];

        Camera CamComp() => cam ? cam.GetComponent<Camera>() : Camera.main;

        // ---------------------------------------------------------------- мышь: тычок и перетаскивание
        void MouseActions()
        {
            var m = Mouse.current;
            var k = Keyboard.current;
            if (m == null) return;
            var c = CamComp();
            if (!c) return;
            Ray ray = c.ScreenPointToRay(m.position.ReadValue());
            Vector2 mp = m.position.ReadValue();
            bool overPanel = help && panelRect.Contains(new Vector2(mp.x, Screen.height - mp.y));
            // человек v3: тычок по зонду кости (отклик поверх анимации)
            if (m.leftButton.wasPressedThisFrame && !overPanel && !(k != null && k.leftShiftKey.isPressed) && PokeV3(ray)) return;
            if (m.leftButton.wasPressedThisFrame && !overPanel &&
                Physics.Raycast(ray, out var hit, 100f, RigLayers.SolidMask, QueryTriggerInteraction.Ignore) &&
                hit.rigidbody && !hit.rigidbody.isKinematic)
            {
                if (k != null && k.leftShiftKey.isPressed) StartGrab(hit);
                else
                {
                    var rig = hit.rigidbody.GetComponentInParent<RigBody>();
                    float imp = poke * (rig ? rig.Mass : hit.rigidbody.mass);
                    hit.rigidbody.AddForceAtPosition(ray.direction * imp, hit.point, ForceMode.Impulse);
                    if (rig) rig.Hit(hit.rigidbody.gameObject, imp, ray.direction);
                    Note($"тычок {poke:F1} м/с → {(rig ? rig.title : hit.rigidbody.name)}");
                }
            }
            if (grabbed)
            {
                if (m.leftButton.isPressed) hand.MovePosition(ray.GetPoint(grabDist));
                else EndGrab();
            }
        }

        void StartGrab(RaycastHit hit)
        {
            if (!hand)
            {
                var h = new GameObject("Рука");
                hand = h.AddComponent<Rigidbody>();
                hand.isKinematic = true;
            }
            hand.transform.position = hit.point;
            hand.position = hit.point;
            grabbed = hit.rigidbody;
            grabDist = hit.distance;
            grabJoint = grabbed.gameObject.AddComponent<SpringJoint>();
            grabJoint.connectedBody = hand;
            grabJoint.autoConfigureConnectedAnchor = false;
            grabJoint.anchor = grabbed.transform.InverseTransformPoint(hit.point);
            grabJoint.connectedAnchor = Vector3.zero;
            var rig = grabbed.GetComponentInParent<RigBody>();
            float mass = rig ? rig.Mass : grabbed.mass;
            grabJoint.spring = 120f * mass;
            grabJoint.damper = 8f * mass;
            grabJoint.maxDistance = 0.02f;
            Note("тащу: " + (rig ? rig.title : grabbed.name));
        }

        void EndGrab()
        {
            if (grabJoint) Destroy(grabJoint);
            grabJoint = null;
            grabbed = null;
        }

        // ---------------------------------------------------------------- управление и камера
        void Possess(RigWander w)
        {
            Release();
            possessed = w;
            if (w) { w.possessed = true; Follow(w.transform); Note("ведёшь: " + w.GetComponent<RigBody>().title); }
        }

        void Release()
        {
            if (possessed) { possessed.possessed = false; possessed.possessVelocity = Vector3.zero; }
            possessed = null;
            if (possessedHuman) { possessedHuman.possessed = false; possessedHuman.possessVelocity = Vector3.zero; }
            possessedHuman = null;
            ReleaseV3();
        }

        void PossessHuman(HumanDriver d)
        {
            Release();
            possessedHuman = d;
            if (d)
            {
                d.possessed = true;
                var hp = d.GetComponentInParent<HumanPuppet>();
                Follow(hp ? hp.body.transform : d.transform, 4f);
                Note("ведёшь: человека");
            }
        }

        void PossessNext()
        {
            if (!arena) return;
            var all = new List<Component>();
            all.AddRange(arena.GetComponentsInChildren<HumanDriver>());
            all.AddRange(arena.GetComponentsInChildren<RigWander>());
            all.AddRange(arena.GetComponentsInChildren<Fortress.Rig.V3.HumanMotor>());
            if (all.Count == 0) return;
            Component cur = possessedHuman ? possessedHuman : possessedV3 ? possessedV3 : (Component)possessed;
            int i = cur ? all.IndexOf(cur) : -1;
            var next = all[(i + 1) % all.Count];
            if (next is HumanDriver hd) PossessHuman(hd);
            else if (next is Fortress.Rig.V3.HumanMotor hm) PossessV3(hm);
            else Possess((RigWander)next);
        }

        void FollowNext()
        {
            if (!arena || !cam) return;
            var all = arena.GetComponentsInChildren<RigBody>();
            if (all.Length == 0) return;
            int i = -1;
            for (int j = 0; j < all.Length; j++) if (cam.follow == all[j].transform) i = j;
            Follow(all[(i + 1) % all.Length].transform);
        }

        void Follow(Transform t, float dist = 3.2f)
        {
            if (!cam) return;
            cam.follow = t;
            cam.distance = dist;
            cam.pitch = 22f;
        }

        HumanPuppet Human(int idx, Vector3 pos, float yaw)
        {
            var pf = humanPrefabs != null && humanPrefabs.Length > 0 ? humanPrefabs[Mathf.Clamp(idx, 0, humanPrefabs.Length - 1)] : null;
            if (!pf) { Note("нет префабов людей — пересобери стенд"); return null; }
            var go = Instantiate(pf, pos, Quaternion.Euler(0f, yaw, 0f), arena);
            go.name = pf.name.Replace("Human_", "");
            return go.GetComponent<HumanPuppet>();
        }

        // ---------------------------------------------------------------- постройка
        Transform Stool(Vector3 ground, Quaternion rot)
        {
            float h = stoolPrefab.transform.position.y;
            var go = Instantiate(stoolPrefab, ground + Vector3.up * h, rot, arena);
            go.name = "Табурет";
            var w = go.AddComponent<RigWander>();
            w.center = Vector3.zero;
            return go.transform;
        }

        /// <summary>Кусок набора крепости (лестница, стена, стол) — неподвижный, со своими коллайдерами.</summary>
        GameObject Kit(GameObject pf, Vector3 pos, float yaw)
        {
            if (!pf) return null;
            var g = Instantiate(pf, pos, Quaternion.Euler(0f, yaw, 0f), arena);
            g.name = pf.name;
            return g;
        }

        /// <summary>Лестница — помост 1 м — лестница (по оси X), center — середина помоста.</summary>
        void StairsBlock(Vector3 center)
        {
            Kit(stairsPrefab, center + new Vector3(-2f, 0f, 0f), -90f);
            Kit(platformPrefab, center, 0f);
            Kit(stairsPrefab, center + new Vector3(2f, 0f, 0f), 90f);
        }

        /// <summary>Дверь на петлях: петля в hinge, полотно уходит вправо от петли (по повороту yaw); закрывается пружиной.</summary>
        Rigidbody Door(Vector3 hinge, float yaw)
        {
            if (!doorPrefab) return null;
            var g = Instantiate(doorPrefab, hinge, Quaternion.Euler(0f, yaw, 0f), arena);
            g.name = "Дверь";
            foreach (var c in g.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            var bc = g.AddComponent<BoxCollider>();
            bc.center = new Vector3(-0.53f, 1.05f, 0f);
            bc.size = new Vector3(1.0f, 2.04f, 0.08f);
            var rb = g.AddComponent<Rigidbody>();
            rb.mass = 18f;
            rb.angularDamping = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            g.layer = RigLayers.Body;
            var hj = g.AddComponent<HingeJoint>();
            hj.anchor = new Vector3(0f, 1f, 0f);
            hj.axis = Vector3.up;
            hj.useSpring = true;
            hj.spring = new JointSpring { spring = 22f, damper = 5f, targetPosition = 0f };
            hj.useLimits = true;
            hj.limits = new JointLimits { min = -100f, max = 100f };
            // полотно не трётся о косяк и стену
            foreach (var wc in arena.GetComponentsInChildren<Collider>())
                if (!wc.attachedRigidbody) Physics.IgnoreCollision(bc, wc);
            return rb;
        }

        /// <summary>Лавка набора с местом для сидения.</summary>
        RigSeat Seat(Vector3 pos, float yaw)
        {
            var g = Kit(seatPrefab, pos, yaw);
            if (!g) return null;
            var s = g.AddComponent<RigSeat>();
            s.localA = new Vector3(-1.1f, 0.5f, 0f);
            s.localB = new Vector3(1.1f, 0.5f, 0f);
            s.localForward = Vector3.forward;
            s.bothSides = true;
            return s;
        }

        /// <summary>Верх сиденья над корнем табурета (метка SeatTop в префабе).</summary>
        float SeatTop()
        {
            var t = stoolPrefab.transform.Find("SeatTop");
            return t ? t.localPosition.y + 0.01f : 0.18f;
        }

        /// <summary>Вещь с телом. Ящики — со своим коробчатым коллайдером из набора (ровное дно: не качается на ребре),
        /// остальное (бочки, вёдра) — по выпуклой сетке (катится).</summary>
        Rigidbody Prop(GameObject pf, Vector3 pos, float yaw, float mass)
        {
            var go = Instantiate(pf, pos, Quaternion.Euler(0f, yaw, 0f), arena);
            bool box = pf.name.Contains("Crate") && go.GetComponentInChildren<BoxCollider>();
            if (box)
            {
                // дерево по камню: скользит, если налечь (трение покоя в PhysX и так «липкое»)
                if (!crateMat) crateMat = new PhysicsMaterial("Ящик по камню") { dynamicFriction = 0.2f, staticFriction = 0.25f, frictionCombine = PhysicsMaterialCombine.Minimum };
                foreach (var bc in go.GetComponentsInChildren<BoxCollider>()) bc.sharedMaterial = crateMat;
            }
            if (!box)
            {
                foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true;
                }
            }
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            RigLayers.SetBody(go);
            return rb;
        }

        Transform Box(string name, Vector3 center, Vector3 size, Material mat, bool dynamic, float mass)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(arena);
            g.transform.position = center;
            g.transform.localScale = size;
            if (mat) g.GetComponent<Renderer>().sharedMaterial = mat;
            if (dynamic)
            {
                var rb = g.AddComponent<Rigidbody>();
                rb.mass = mass;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                g.layer = RigLayers.Body;
            }
            return g.transform;
        }

        void Throw(Vector3 from, Vector3 to, float speed, float mass = 1.6f)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            b.name = "Мяч";
            b.layer = RigLayers.Body;
            b.transform.SetParent(arena);
            b.transform.position = from;
            b.transform.localScale = Vector3.one * 0.24f;
            if (ballMat) b.GetComponent<Renderer>().sharedMaterial = ballMat;
            var rb = b.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Vector3 d = to - from;
            float t = d.magnitude / speed;
            rb.linearVelocity = d.normalized * speed + Vector3.up * (0.5f * -Physics.gravity.y * t);
            Destroy(b, 10f);
        }

        static Vector3[] Square(float r) => new[]
        {
            new Vector3(r, 0f, -r), new Vector3(r, 0f, r), new Vector3(-r, 0f, r), new Vector3(-r, 0f, -r),
        };

        /// <summary>Случайная точка пола не ближе minDist от центра (чтобы предмет не появился внутри человека).</summary>
        static Vector3 RandomFloorAway(float r, float minDist)
        {
            for (int i = 0; i < 20; i++)
            {
                var p = RandomFloor(r);
                if (p.magnitude >= minDist) return p;
            }
            return new Vector3(r, 0f, 0f);
        }

        static Vector3 RandomFloor(float r)
        {
            var p = Random.insideUnitCircle * r;
            return new Vector3(p.x, 0f, p.y);
        }

        void Note(string s) { lastEvent = s; lastEventTime = Time.unscaledTime; }

        // ---------------------------------------------------------------- экран
        /// <summary>Панель слева: страницы и ситуации — кнопками (мышью) или клавишами.</summary>
        void Panel()
        {
            if (btn == null)
            {
                btn = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 14, wordWrap = false };
                btn.padding.left = 8;
                btnOn = new GUIStyle(btn) { fontStyle = FontStyle.Bold };
                btnOn.normal.textColor = new Color(1f, 0.86f, 0.45f);
                btnOn.hover.textColor = btnOn.normal.textColor;
            }
            float w = 350f, x = 10f, y = 10f;
            var onPage = new List<int>();
            for (int i = 0; i < tests.Count; i++) if (tests[i].page == page) onPage.Add(i);
            float h = 34f + 30f + onPage.Count * 27f + 10f + 18f * 9f + 14f;
            panelRect = new Rect(x, y, w, h);
            GUI.Box(panelRect, "", box);
            GUI.Label(new Rect(x + 10, y + 6, w - 20, 22), "<b>Стенд рига</b>   (H — скрыть)", box);
            y += 34f;
            float tw = (w - 20f) / Pages.Length;
            for (int p = 0; p < Pages.Length; p++)
                if (GUI.Button(new Rect(x + 10 + p * tw, y, tw - 4, 26), Pages[p], p == page ? btnOn : btn)) { page = p; Note("страница: " + Pages[p]); }
            y += 30f;
            for (int n = 0; n < onPage.Count; n++)
            {
                int i = onPage[n];
                if (GUI.Button(new Rect(x + 10, y, w - 20, 25), $"{n + 1} — {tests[i].title}", i == current ? btnOn : btn)) Run(i);
                y += 27f;
            }
            y += 6f;
            GUI.Label(new Rect(x + 6, y, w - 12, 18f * 9f + 10f),
                "клик по ситуации или 1–9; , . — страницы; R — заново\n" +
                "ЛКМ — ткнуть, Shift+ЛКМ — тащить, B — мяч\n" +
                "+/− — сила тычка, Пробел — пауза, T — замедление\n" +
                "G — линии рига, F — следить за другим\n" +
                "Tab — вести человека/табурет (WASD, Shift)\n" +
                "E — взять / поставить / толкать / сесть\n" +
                "Esc — отпустить\n" +
                "ПКМ — вращать камеру, колесо — зум, СКМ — сдвиг", box);
        }

        void OnGUI()
        {
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = true, richText = true };
                box.normal.textColor = Color.white;
                head = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                head.normal.textColor = new Color(1f, 0.86f, 0.45f);
                line = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperCenter, wordWrap = true };
                line.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
                tagStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            if (current >= 0)
            {
                var t = tests[current];
                GUI.Label(new Rect(0, 10, Screen.width, 30), $"{current + 1}. {t.title}", head);
                float y = 40f;
                foreach (var w in t.watch)
                {
                    GUI.Label(new Rect(Screen.width * 0.2f, y, Screen.width * 0.6f, 22), "• " + w, line);
                    y += 21f;
                }
            }
            if (help) Panel();
            string status = $"время ×{speeds[speedIdx]}{(paused ? "  — ПАУЗА" : "")}   ·   тычок {poke:F1} м/с   ·   линии {(RigDraw.enabled ? "вкл" : "выкл")}";
            GUI.Label(new Rect(0, Screen.height - 30, Screen.width, 24), status, line);
            if (Time.unscaledTime - lastEventTime < 2.5f)
                GUI.Label(new Rect(0, Screen.height - 56, Screen.width, 24), lastEvent, head);

            // подписи над телами
            var c = CamComp();
            if (!c || !arena) return;
            LabelsV3(c);
            foreach (var hp in arena.GetComponentsInChildren<HumanPuppet>())
            {
                if (!hp.head) continue;
                Vector3 hsp = c.WorldToScreenPoint(hp.head.position + Vector3.up * 0.35f);
                if (hsp.z < 0f) continue;
                tagStyle.normal.textColor = Color.Lerp(new Color(1f, 0.35f, 0.3f), new Color(0.5f, 1f, 0.55f), hp.body.tone);
                bool meH = possessedHuman && possessedHuman.GetComponentInParent<HumanPuppet>() == hp;
                var hi = hp.GetComponent<HumanInteract>();
                string act = hi && hi.task != HumanInteract.Task.None && hp.state == HumanPuppet.State.Active ? hi.Mode : hp.Mode;
                if (hp.Bracing) act = "упирается";
                string ht = $"{(meH ? "▶ " : "")}{act} · {hp.body.tone * 100f:F0}%";
                if (Time.time - hp.LastHitTime < 1.5f) ht += $"\nудар {hp.LastHit:F2} м/с";
                GUI.Label(new Rect(hsp.x - 100, Screen.height - hsp.y - 20, 200, 40), ht, tagStyle);
            }
            foreach (var rig in arena.GetComponentsInChildren<RigBody>())
            {
                if (rig.GetComponentInParent<HumanPuppet>()) continue;
                Vector3 sp = c.WorldToScreenPoint(rig.transform.position + Vector3.up * 0.55f);
                if (sp.z < 0f) continue;
                var g = rig.GetComponent<LegGait>();
                bool me = possessed && possessed.gameObject == rig.gameObject;
                tagStyle.normal.textColor = Color.Lerp(new Color(1f, 0.35f, 0.3f), new Color(0.5f, 1f, 0.55f), rig.tone);
                string txt = $"{(me ? "▶ " : "")}{(g ? g.Mode : rig.State)} · {rig.tone * 100f:F0}%";
                if (Time.time - rig.LastHitTime < 1.5f) txt += $"\nудар {rig.LastHit:F1}";
                GUI.Label(new Rect(sp.x - 90, Screen.height - sp.y - 20, 180, 40), txt, tagStyle);
            }
        }
    }
}
