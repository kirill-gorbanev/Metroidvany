using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Hero.Arm2
{
    /// <summary>ПЕСОЧНИЦА нового героя (сцена HeroArm2Sandbox): пол, стена, столб, ступень 0.3, уступ 0.5, склоны 20 / 32 / 36 / 45°,
    /// челнок, карусель, пара предметов — всё с настоящими коллайдерами. Здесь делаются и настраиваются пробы; в полигон и зоны —
    /// только когда в песочнице хорошо. Этот класс — данные станций (одни для сборщика, клавиш и замера) и клавиши 1…9:
    /// постановка тела на станцию (это старт, не движение).</summary>
    public class Arm2Sandbox : MonoBehaviour
    {
        public struct Station
        {
            public string name; public Vector3 center; public Quaternion rot;
            public string mover;        // имя платформы: center — относительно неё
            public bool slides;         // ожидание: true — сползает, false — стоит
        }

        public const float RampLen = 2f, RampWidth = 1.2f, RampThick = 0.2f, RampZ0 = 2f;
        public static readonly float[] RampAngles = { 20f, 32f, 36f, 45f, 60f };   // пороги автора: до 32° стоит, от 35° сползает
        public static float RampX(int i) => 1.6f + i * 1.5f;
        const float Half = Arm2Body.Height * 0.5f, Gap = 0.01f;

        /// <summary>Середина верхней грани склона i и нормаль.</summary>
        public static Vector3 RampTop(int i, out Vector3 normal)
        {
            float a = RampAngles[i] * Mathf.Deg2Rad;
            normal = new Vector3(0f, Mathf.Cos(a), -Mathf.Sin(a));
            return new Vector3(RampX(i), Mathf.Sin(a) * RampLen * 0.5f, RampZ0 + Mathf.Cos(a) * RampLen * 0.5f);
        }

        /// <summary>Площадка 1.2 для края и виса: южный край z = PlatZ0, герой стоит спиной к нему.</summary>
        public const float PlatX = -6.8f, PlatH = 1.2f, PlatZ0 = 5f;
        /// <summary>Камни на склоне 45°: расстояния вдоль склона от его низа (м) и боковой сдвиг от середины (под правое плечо).</summary>
        public static readonly float[] NubAlong = { 0.30f, 0.58f, 0.86f, 1.14f, 1.42f, 1.70f };
        public const float NubSide = 0.2f, RailSide = 0.45f, RailHeight = 0.22f;
        /// <summary>Точка на верхней грани склона i: along — вдоль склона от низа, side — вбок от середины.</summary>
        public static Vector3 RampPoint(int i, float along, float side)
        {
            float a = RampAngles[i] * Mathf.Deg2Rad;
            return new Vector3(RampX(i) + side, Mathf.Sin(a) * along, RampZ0 + Mathf.Cos(a) * along);
        }
        public static int RampIndex(float angle) { for (int i = 0; i < RampAngles.Length; i++) if (Mathf.Approximately(RampAngles[i], angle)) return i; return 0; }
        public static readonly Vector3 ShuttlePos = new Vector3(-3f, 0.1f, -2f), SpinnerPos = new Vector3(3f, 0.1f, -2f);
        public const string ShuttleName = "Челнок", SpinnerName = "Карусель";

        public static Station[] Stations()
        {
            int nr = RampAngles.Length; var s = new Station[6 + nr];
            s[5 + nr] = Flat("− площадка 1.2 (спиной к краю)", new Vector3(PlatX, PlatH, PlatZ0 + 0.45f));
            s[0] = Flat("1 пол", new Vector3(0f, 0f, 0f));
            s[1] = Flat("2 ступень 0.3", new Vector3(-3f, 0.3f, 2.6f));
            s[2] = Flat("3 уступ 0.5", new Vector3(-1.4f, 0.5f, 2.6f));
            for (int i = 0; i < nr; i++)
            {
                Vector3 top = RampTop(i, out Vector3 n);
                s[3 + i] = new Station { name = $"{4 + i} склон {RampAngles[i]:0}°", center = top + n * (Half + Gap),
                                         rot = Quaternion.Euler(-RampAngles[i], 0f, 0f), slides = RampAngles[i] >= 35f };
            }
            s[3 + nr] = new Station { name = $"{4 + nr} челнок", mover = ShuttleName, center = new Vector3(0f, 0.1f + Half + Gap, 0f), rot = Quaternion.identity };
            s[4 + nr] = new Station { name = $"{5 + nr} карусель", mover = SpinnerName, center = new Vector3(0.4f, 0.1f + Half + Gap, 0f), rot = Quaternion.identity };
            return s;
        }
        static Station Flat(string n, Vector3 feet) => new Station { name = n, center = feet + Vector3.up * (Half + Gap), rot = Quaternion.identity };

        // ПРЕГРАДЫ ДЛЯ РУКИ. Автор 2026-11-25: «добавить в локацию преграды для руки — чтобы проверить, как она сталкивается, внутрь
        // входит и около, по поверхностям; создай просто сложную топологию поверхности, куда я буду ставить руку».
        // Герой пока не ездит, поэтому преграды стоят вокруг пяти мест героя, всё в досягаемости руки (0.59 м от плеча).
        public static readonly string[] ObstacleNames =
            { "Z углы и щели", "X ступеньки и полка", "C рельеф и купол", "V тонкое и круглое", "B помост: яма и край" };
        public static readonly Key[] ObstacleKeys = { Key.Z, Key.X, Key.C, Key.V, Key.B };
        public const float PlatformH = 0.3f;
        /// <summary>Где стоит герой у преграды i (точка на опоре под центром тела); лицом на +Z.</summary>
        public static Vector3 ObstacleOrigin(int i) => new Vector3(-6f + i * 3f, i == 4 ? PlatformH : 0f, -5.2f);
        public static Station[] ObstacleStations()
        {
            var s = new Station[ObstacleNames.Length];
            for (int i = 0; i < s.Length; i++) s[i] = Flat(ObstacleNames[i], ObstacleOrigin(i));
            return s;
        }
        /// <summary>Высота рельефа станции C в точке (x, z) относительно места героя; к краям поля сходит в ноль.</summary>
        public static float ReliefHeight(float x, float z)
        {
            float u = Mathf.InverseLerp(ReliefX0, ReliefX1, x), w = Mathf.InverseLerp(ReliefZ0, ReliefZ1, z);
            float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(w, 1f - w)) * 6f);
            float h = 0.05f + 0.04f * (Mathf.Sin(x * 9f) + Mathf.Cos(z * 11f)) + 0.03f * Mathf.Sin((x + z) * 17f);
            return Mathf.Max(0f, h) * edge;
        }
        public const float ReliefX0 = -0.45f, ReliefX1 = 0.35f, ReliefZ0 = 0.12f, ReliefZ1 = 0.8f;

        /// <summary>Поставить тело на станцию (старт). Платформа — в её нынешнем месте.</summary>
        public static void Place(Arm2Hero hero, Station st)
        {
            Vector3 p = st.center; Quaternion r = st.rot;
            if (!string.IsNullOrEmpty(st.mover))
            {
                var m = GameObject.Find(st.mover);
                if (m) { p = m.transform.position + m.transform.rotation * st.center; r = m.transform.rotation * st.rot; }   // без масштаба платформы
            }
            hero.PlaceAtStart(p, r);
            if (hero.cam) hero.cam.ResetView();
        }

        Station[] stations, obstacles; string shown = ""; float shownAt = -99f;

        void Update()
        {
            var kb = Keyboard.current; var hero = Arm2Hero.Live;
            if (kb == null || !hero || Arm2Hero.Manual) return;
            stations ??= Stations();
            for (int i = 0; i < stations.Length && i < 11; i++)
                if (kb[i == 10 ? Key.Minus : i == 9 ? Key.Digit0 : Key.Digit1 + i].wasPressedThisFrame) { Place(hero, stations[i]); shown = stations[i].name; shownAt = Time.unscaledTime; }
            obstacles ??= ObstacleStations();
            for (int i = 0; i < obstacles.Length; i++)
                if (kb[ObstacleKeys[i]].wasPressedThisFrame) { Place(hero, obstacles[i]); shown = "преграды — " + obstacles[i].name; shownAt = Time.unscaledTime; }
        }

        void OnGUI()
        {
            GUI.Label(new Rect(12f, 28f, 900f, 22f), "1…9, 0, «−» — поставить тело на станцию (4…8 — склоны 20 / 32 / 36 / 45 / 60°; «−» — площадка 1.2, спиной к краю)   ПКМ + мышь — наклон камеры   колесо — ближе / дальше");
            GUI.Label(new Rect(12f, 68f, 900f, 22f), "преграды для руки:  Z — углы и щели   X — ступеньки и полка   C — рельеф и купол   V — тонкое и круглое   B — помост: яма и край");
            if (Time.unscaledTime - shownAt < 2f) GUI.Label(new Rect(12f, 48f, 600f, 22f), "станция: " + shown);
        }
    }
}
