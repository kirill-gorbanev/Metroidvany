using UnityEngine;

namespace Fortress.Rig.React
{
    /// <summary>Фильтр второго порядка «догоняет цель» (f, ζ, r):  y + k1·ẏ + k2·ÿ = x + k3·ẋ.
    /// f — частота (Гц, скорость отклика), ζ — затухание (меньше 1 — с перехлёстом), r — «упреждение» (больше 0 — рвётся вперёд, меньше 0 — замах назад).
    /// k1 = ζ/(πf), k2 = 1/(2πf)², k3 = r·ζ/(2πf). Устойчиво при любом шаге (k2 поднимается до границы устойчивости).</summary>
    public struct SecondOrder
    {
        float k1, k2, k3;
        Vector3 xp;
        public Vector3 y, yd;

        public void Init(float f, float zeta, float r, Vector3 x0)
        {
            Tune(f, zeta, r);
            xp = x0; y = x0; yd = Vector3.zero;
        }

        public void Tune(float f, float zeta, float r)
        {
            float w = 2f * Mathf.PI * Mathf.Max(f, 0.01f);
            k1 = zeta / (Mathf.PI * Mathf.Max(f, 0.01f));
            k2 = 1f / (w * w);
            k3 = r * zeta / w;
        }

        public Vector3 Update(float dt, Vector3 x)
        {
            if (dt <= 1e-6f) return y;
            Vector3 xd = (x - xp) / dt;
            xp = x;
            float k2s = Mathf.Max(k2, 1.1f * (dt * dt / 4f + dt * k1 / 2f));
            y += dt * yd;
            yd += dt * (x + k3 * xd - y - k1 * yd) / k2s;
            return y;
        }
    }

    /// <summary>То же для угла (градусы) — цель «разворачивается» так, чтобы идти коротким путём.</summary>
    public struct SecondOrderAngle
    {
        float k1, k2, k3;
        float xp;
        public float y, yd;

        public void Init(float f, float zeta, float r, float x0)
        {
            Tune(f, zeta, r);
            xp = x0; y = x0; yd = 0f;
        }

        public void Tune(float f, float zeta, float r)
        {
            float w = 2f * Mathf.PI * Mathf.Max(f, 0.01f);
            k1 = zeta / (Mathf.PI * Mathf.Max(f, 0.01f));
            k2 = 1f / (w * w);
            k3 = r * zeta / w;
        }

        public float Update(float dt, float xRaw)
        {
            if (dt <= 1e-6f) return y;
            float x = xp + Mathf.DeltaAngle(xp, xRaw);    // цель без скачка через 360°
            float xd = (x - xp) / dt;
            xp = x;
            // корпус не «отстаёт на круг»: цель не дальше полуоборота от текущего
            float off = Mathf.DeltaAngle(y, x);
            x = y + off;
            float k2s = Mathf.Max(k2, 1.1f * (dt * dt / 4f + dt * k1 / 2f));
            y += dt * yd;
            yd += dt * (x + k3 * xd - y - k1 * yd) / k2s;
            return y;
        }
    }
}
