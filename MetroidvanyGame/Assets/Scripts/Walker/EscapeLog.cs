using UnityEngine;

namespace Fortress.Walker
{
    /// <summary>Служебный журнал: каким выходом игрок покинул стены (только в консоль, без надписи на экране).</summary>
    public class EscapeLog : MonoBehaviour
    {
        public static string lastExit;
        static float startTime, endTime;
        static bool done;
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { lastExit = null; done = false; startTime = 0f; }

        void Start() { startTime = Time.time; }

        public static void Finish()
        {
            if (done) return;
            done = true; endTime = Time.time;
            Report();
        }

        /// <summary>Без надписи на экране: игра не про побег, выход только пишется в журнал.</summary>
        public static void Report()
        {
            float t = (done ? endTime : Time.time) - startTime;
            Debug.Log($"[Exit] {(string.IsNullOrEmpty(lastExit) ? "без метки выхода" : lastExit)} — {Mathf.FloorToInt(t / 60)}:{Mathf.FloorToInt(t % 60):00}");
        }
    }
}
