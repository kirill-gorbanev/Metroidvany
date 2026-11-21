using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Hero.Proto
{
    /// <summary>ЗОНЫ ОДНОЙ АРЕНЫ (шаг 4 долга): все прежние арены 2–8 в одной сцене, каждая — зона на своих прежних координатах.
    /// Активна одна зона за раз: стены, поверхность и декор остальных выключены — герой их не видит (сбор стен, поверхности,
    /// зацепов и платформ берёт только активное). Клавиши 2…8 — перейти в зону (герой на её старт, камера за спину).
    /// Почему не «стоят рядом»: координаты прежние — замеры и эталон общего прогона переносятся без правок, до цифры.</summary>
    public class ProtoZones : MonoBehaviour
    {
        [System.Serializable]
        public class Zone
        {
            public int number;
            public string title;
            public GameObject walls, surface, decor;
            public Vector3 spawn;
            public float spawnYaw;
        }

        public Zone[] zones = new Zone[0];
        public int startZone = 8;
        public HeroProto hero;
        public ProtoOrbitCamera cam;

        public static ProtoZones Instance { get; private set; }
        public int Current { get; private set; }

        void Awake()
        {
            Instance = this;
            int z = startZone;
            foreach (var zz in zones) SetZoneActive(zz, zz.number == z);
            Current = z;                                  // старт героя в стартовой зоне — где его поставил сборщик (HeroProto.Start)
        }

        void Update()
        {
            if (HeroProto.ManualTick) return;
            var k = Keyboard.current; if (k == null) return;
            Key[] keys = { Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < keys.Length; i++)
                if (k[keys[i]].wasPressedThisFrame && Find(i + 2) != null) Activate(i + 2);
            if (k[Key.Digit0].wasPressedThisFrame && Find(10) != null) Activate(10);          // зона 10 — клавиша 0
        }

        Zone Find(int n) { foreach (var z in zones) if (z.number == n) return z; return null; }

        static void SetZoneActive(Zone z, bool on)
        {
            if (z.walls) z.walls.SetActive(on);
            if (z.surface) z.surface.SetActive(on);
            if (z.decor) z.decor.SetActive(on);
        }

        /// <summary>Перейти в зону n: включить её, выключить остальные, герой — на её старт, всё состояние с нуля.</summary>
        public bool Activate(int n)
        {
            var zone = Find(n); if (zone == null) return false;
            foreach (var z in zones) SetZoneActive(z, z == zone);
            Current = n;
            if (hero)
            {
                hero.DiagRecollect();
                hero.DiagSetSpawn(new Vector3(zone.spawn.x, hero.Height(zone.spawn), zone.spawn.z), zone.spawnYaw);   // высота — верх поверхности, как на старте
                foreach (var m in HeroProto.LiveMovers) if (m) m.ResetPhase();
                hero.DiagReset();
            }
            if (cam) cam.ResetView();
            return true;
        }

        /// <summary>Из симуляции (HeroSim / общий прогон): перейти в зону n.</summary>
        public static bool Go(int n) => Instance && Instance.Activate(n);

        void OnGUI()
        {
            if (!hero || !hero.showHints || HeroProto.ManualTick) return;
            var z = Find(Current);
            GUI.Label(new Rect(12, 30, 1400, 24), $"Зона {Current}: {(z != null ? z.title : "?")}   ·   клавиши 2–9, 0 — другая зона");
        }
    }
}
