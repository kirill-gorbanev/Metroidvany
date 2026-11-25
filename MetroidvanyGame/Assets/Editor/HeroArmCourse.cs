using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>ПОЛИГОН РУКИ (автор 2026-11-25: «создать карту, где Fable сможет оценить и проходить карту нашим персонажем» — до
    /// документа и промта для нового контроллера руки). Коридор вдоль X: 11 станций через 3 м, между ними ровный пол — карту можно
    /// пройти героем подряд. Каждая станция — один случай, на котором контроллер руки уже спотыкался (или то, что работает и не должно
    /// сломаться). Данные общие: сцену собирает HeroArenaBuilder.BuildArmCourse, замер по станциям — HeroSim.ArmCourse.
    ///
    /// Герой на каждой станции — в точке spawn, лицом на север (+Z, yaw 0), если не сказано иное. Цели — точки мира, куда наводится
    /// курсор (кисть), по очереди. Вид станции:
    ///   reach  — навести кисть на цель, ЛКМ, потянуть вверх, отпустить (предметы, пол, верхи блоков);
    ///   hold   — навести на зацеп (камень, поручень), ЛКМ, держать — должен быть хват за зацеп;
    ///   climb  — подъём на уступ ботом (стенд HeroSim.Stand), expect — должен ли дойти (подъём не выше 0.4 м);
    ///   descent — спуск спиной с площадки 1.2 в вис, опускание вдоль стенки, отпустить.</summary>
    public static class HeroArmCourse
    {
        public const string ScenePath = "Assets/Scenes/HeroArmCourse.unity";
        public const float Step = 3f, Spawn0 = 1.5f, SpawnZ = 1.0f;
        public static float X(int i) => Spawn0 + i * Step;

        public struct Station
        {
            public string name, kind, check;
            public Vector3 spawn; public float yaw;
            public Vector3[] targets;
            public bool[] expect;          // climb: дойти ли до каждой цели
        }

        public static Station[] Stations => new[]
        {
            new Station { name = "1 столб у плеча", kind = "reach", spawn = S(0), check = "рука не сквозь столб; кисть за столбом — локоть обходит, или кисть не идёт",
                targets = new[] { P(0, 0.26f, 0.06f, 1.95f), P(0, 0.45f, 0f, 1.95f), P(0, 0.26f, 1.2f, 1.56f) } },
            new Station { name = "2 высокий блок, предмет сверху", kind = "reach", spawn = S(1), check = "предмет на верху блока 0.6 и за блоком: рука не сквозь угол блока, кисть не в воздухе",
                targets = new[] { P(1, 0f, 0.67f, 1.75f), P(1, 0f, 0.06f, 2.15f), P(1, 0f, 0.6f, 1.9f) } },
            new Station { name = "3 тумбы 0.15–0.60 с предметами", kind = "reach", spawn = S(2), check = "прицел на плоскость предмета на тумбе (наведение не отбрасывается), хват, перенос",
                targets = TumbaItems(2) },
            new Station { name = "4 стопка и соседи вплотную", kind = "reach", spawn = S(3), check = "средний в стопке, верхний, сосед: рука не толкает соседей, не сквозь стопку",
                targets = new[] { P(3, 0.15f, 0.22f, 1.525f), P(3, 0.15f, 0.45f, 1.6f), P(3, -0.02f, 0.15f, 1.6f), P(3, 0.32f, 0.15f, 1.6f) } },
            new Station { name = "5 предмет у тела", kind = "reach", spawn = S(4), check = "за спиной и у бока: рука не сквозь своё тело, предмет не толкает тело",
                targets = new[] { P(4, 0f, 0.07f, 0.65f), P(4, 0.3f, 0.06f, 0.8f), P(4, -0.35f, 0f, 1.0f) } },
            new Station { name = "6 щель 0.2 между блоками", kind = "reach", spawn = S(5), check = "кисть в щель: локоть и предплечье не в блоках",
                targets = new[] { P(5, 0.2f, 0.04f, 1.65f), P(5, 0.05f, 0.25f, 1.65f), P(5, 0.35f, 0.25f, 1.65f) } },
            new Station { name = "7 высокая полка 0.75", kind = "reach", spawn = S(6), check = "полка дальше руки: кисть не висит в воздухе, ложится на достижимую поверхность",
                targets = new[] { P(6, 0.25f, 0.8f, 1.91f), P(6, 0.25f, 0.75f, 1.85f) } },
            new Station { name = "8 уступы 0.3 / 0.4 / 0.5", kind = "climb", spawn = S(7), check = "0.3 и 0.4 — заходит, 0.5 — нет (подъём не выше 0.4)",
                targets = new[] { P(7, -0.9f, 0.3f, 2.6f), P(7, 0f, 0.4f, 2.6f), P(7, 0.9f, 0.5f, 2.6f) }, expect = new[] { true, true, false } },
            new Station { name = "9 спуск спиной с 1.2", kind = "descent", spawn = new Vector3(X(8), 1.2f, 2.5f), check = "вис на кромке, опускание вдоль стенки, отпустил низко — без оглушения; рука не в стенке",
                targets = new[] { P(8, 0.2f, 1.2f, 2.85f) } },
            new Station { name = "10 стена с камнями", kind = "hold", spawn = new Vector3(X(9), 0f, 1.45f), check = "хват за камень на стене (вис на камне), рука не в стене",
                targets = new[] { P(9, 0.2f, 0.55f, 1.97f), P(9, -0.2f, 0.85f, 1.97f), P(9, 0.2f, 1.1f, 1.97f) } },
            new Station { name = "11 поручень 0.55", kind = "hold", spawn = S(10), check = "хват за поручень",
                targets = new[] { P(10, 0.2f, 0.55f, 1.6f), P(10, -0.4f, 0.55f, 1.6f) } },
        };

        static Vector3 S(int i) => new Vector3(X(i), 0f, SpawnZ);
        static Vector3 P(int i, float dx, float y, float z) => new Vector3(X(i) + dx, y, z);

        // тумбы станции 3 — дугой перед плечом (как в лаборатории): радиус 0.55 от плеча, высоты и углы
        public static readonly float[] TumbaH = { 0.15f, 0.30f, 0.45f, 0.60f }, TumbaAng = { -40f, -13f, 13f, 40f };
        public static Vector3 TumbaCenter(int i, int k)
        {
            float a = TumbaAng[k] * Mathf.Deg2Rad, shx = X(i) + 0.2f;
            return new Vector3(shx + 0.55f * Mathf.Sin(a), 0f, SpawnZ + 0.55f * Mathf.Cos(a));
        }
        static Vector3[] TumbaItems(int i)
        {
            var r = new Vector3[TumbaH.Length];
            for (int k = 0; k < r.Length; k++) { var c = TumbaCenter(i, k); r[k] = new Vector3(c.x, TumbaH[k] + 0.04f, c.z); }
            return r;
        }
    }
}
