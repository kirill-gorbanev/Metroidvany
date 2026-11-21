using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Hero.Proto
{
    /// <summary>Камера прототипа — отдельный блок: облёт вокруг персонажа.
    /// Вправо/влево камера поворачивается только вместе с телом (поворот жестом ЛКМ, followBodyTurn).
    /// ПКМ (зажать) + мышь вверх/вниз — наклон камеры (рука в это время замирает). Колесо — ближе/дальше.
    /// Колесо — ближе/дальше. Плавно едет за телом.</summary>
    public class ProtoOrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float yaw = 0f, pitch = 50f, distance = 2.6f;
        public float yawSpeed = 0.3f, pitchSpeed = 0.2f;
        public float minPitch = 10f, maxPitch = 85f, minDistance = 0.8f, maxDistance = 8f;
        [Tooltip("Точка взгляда — над телом (м).")]
        public float lookHeight = 0.15f;
        public float follow = 6f;
        [Tooltip("Камера поворачивается вместе с телом, когда оно поворачивается жестом (ЛКМ). Облёт ПКМ — поверх.")]
        public bool followBodyTurn = true;
        [Tooltip("На старте встать за спину телу (по его повороту).")]
        public bool startBehindBody = true;

        public static bool Orbiting { get; private set; }
        Vector3 focus;
        float lastBodyYaw;

        float pitch0 = float.NaN, distance0;
        /// <summary>Симуляция: камера за спину телу, наклон и дальность — как на старте.</summary>
        public void ResetView()
        {
            if (float.IsNaN(pitch0)) return;
            pitch = pitch0; distance = distance0;
            if (target) { focus = target.position + Vector3.up * lookHeight; lastBodyYaw = target.eulerAngles.y; yaw = target.eulerAngles.y; }
            Apply();
        }

        void Start()
        {
            pitch0 = pitch; distance0 = distance;
            if (target)
            {
                focus = target.position + Vector3.up * lookHeight; lastBodyYaw = target.eulerAngles.y;
                if (startBehindBody) yaw = target.eulerAngles.y;           // камера смотрит туда же, куда тело
            }
            Apply();
        }

        void LateUpdate() { if (!HeroProto.ManualTick) Step(Time.unscaledDeltaTime); }

        /// <summary>Шаг камеры (симуляция вызывает сама).</summary>
        public void Step(float dt)
        {
            if (!target) return;
            var m = HeroProto.ManualTick ? null : Mouse.current;
            // ПКМ (зажать) + мышь вверх/вниз — наклон камеры; вправо/влево не крутит (это делает тело на ЛКМ)
            Orbiting = m != null && m.rightButton.isPressed;
            if (m != null)
            {
                if (Orbiting)
                    pitch = Mathf.Clamp(pitch - m.delta.ReadValue().y * (1080f / Mathf.Max(1f, Screen.height)) * pitchSpeed, minPitch, maxPitch);   // градусы на px при высоте экрана 1080 (доля экрана)
                float s = m.scroll.ReadValue().y;
                if (Mathf.Abs(s) > 0.01f) distance = Mathf.Clamp(distance * (1f - Mathf.Sign(s) * 0.1f), minDistance, maxDistance);
            }
            if (followBodyTurn)
            {
                float by = target.eulerAngles.y;
                yaw += Mathf.DeltaAngle(lastBodyYaw, by);
                lastBodyYaw = by;
            }
            focus = Vector3.Lerp(focus, target.position + Vector3.up * lookHeight, 1f - Mathf.Exp(-follow * dt));
            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }
    }
}
