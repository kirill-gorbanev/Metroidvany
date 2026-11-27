using UnityEngine;

namespace Fortress.Hero.Arm2
{
    /// <summary>ЗАМЕР ПЛАВНОСТИ В НАСТОЯЩЕМ ЦИКЛЕ ИГРЫ (инструмент; урок 15: на 247 к/с тело на платформе шло ступеньками).
    /// Тело стоит на челноке; каждый кадр — сдвиг тела на экране и его место относительно платформы.
    /// «Ступенька» — кадр, где платформа идёт ровным ходом, а тело на экране прошло на 20 % меньше или больше положенного.
    /// Только читает позы.</summary>
    public class Arm2SmoothProbe : MonoBehaviour
    {
        public Arm2Body body; public Arm2Mover mover; public float duration = 6f;
        public bool Done { get; private set; }
        int frames, cruise, steps; float time, maxDev, maxRel, minDt = 9f, maxDt;
        Vector3 prev, rel0; bool has;

        void LateUpdate()
        {
            if (Done || !body || !mover) return;
            float dt = Time.deltaTime; if (dt <= 0f) return;
            Vector3 p = body.transform.position, rel = p - mover.transform.position;
            if (has)
            {
                frames++; time += dt; minDt = Mathf.Min(minDt, dt); maxDt = Mathf.Max(maxDt, dt);
                maxRel = Mathf.Max(maxRel, (rel - rel0).magnitude);
                float v = mover.Velocity.magnitude;
                if (Mathf.Abs(v - mover.speed) < 0.001f)                         // ровный ход платформы
                {
                    cruise++;
                    float ratio = (p - prev).magnitude / (v * dt);
                    maxDev = Mathf.Max(maxDev, Mathf.Abs(ratio - 1f));
                    if (ratio < 0.8f || ratio > 1.2f) steps++;
                }
                if (time >= duration) Done = true;
            }
            else { rel0 = rel; has = true; }
            prev = p;
        }

        public string Report() =>
            $"кадров {frames} за {time:F1} с ({(time > 0f ? frames / time : 0f):F0} к/с, кадр {minDt * 1000f:F1}…{maxDt * 1000f:F1} мс), шаг физики {Time.fixedDeltaTime * 1000f:F1} мс | " +
            $"на ровном ходу кадров {cruise}, ступенек {steps}, отклонение хода за кадр макс {maxDev * 100f:F0} % | тело относительно платформы ушло макс {maxRel * 1000f:F1} мм";
    }
}
