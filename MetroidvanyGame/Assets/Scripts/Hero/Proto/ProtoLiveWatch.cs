using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ЖИВОЙ СТОРОЖ — проверка в настоящем цикле игры (Update, реальные кадры), а не в ручных шагах симуляции.
    /// Ловит то, что бот в HeroSim не видит: рывки на длинном кадре, платформы, которые не шагают каждый кадр
    /// (так проявилась проба FixedUpdate 50/с — «дёргается на вращении»). Ставит HeroSim.LiveStart, читает LiveReport.</summary>
    public class ProtoLiveWatch : MonoBehaviour
    {
        public ProtoWatch W;
        public int frames, framesNoMoverStep;
        public float maxDt;
        ProtoMover mover; int lastStep = -1;

        void Start() { foreach (var m in HeroProto.LiveMovers) if (m && m.autoRun) { mover = m; break; } }

        void LateUpdate()
        {
            if (HeroProto.ManualTick || W == null) return;
            frames++;
            maxDt = Mathf.Max(maxDt, Time.deltaTime);
            W.Check(Time.deltaTime);
            if (mover) { if (mover.StepId == lastStep) framesNoMoverStep++; lastStep = mover.StepId; }
        }

        public string Report() =>
            $"кадров {frames}, самый длинный {maxDt * 1000f:F0} мс, кадров без шага платформы {framesNoMoverStep}" +
            (mover ? "" : " (платформ нет)") + "; " + (W != null ? W.Report() : "");
    }
}
