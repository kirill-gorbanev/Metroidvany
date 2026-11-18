using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Hero
{
    /// <summary>Камера героя (ТЗ: ПКМ — облёт вокруг тела, отпустил — снова рука).
    /// Сзади-справа над плечом (рука правая); колесо — ближе/дальше. Облёт остаётся, где оставили;
    /// за телом камера идёт только по повороту героя (сам он крутится только по клику по земле).</summary>
    public class HeroCamera : MonoBehaviour
    {
        public Transform target;
        public float distance = 2.4f, pitch = 28f, yawOffset = 25f, height = 0.55f;
        public float follow = 4f;
        float yaw, orbitYaw, lastTargetYaw;
        Vector3 focus;

        void Start()
        {
            if (target) { yaw = lastTargetYaw = target.eulerAngles.y; focus = target.position + Vector3.up * height; }
        }

        void LateUpdate()
        {
            if (!target) return;
            float dt = Time.unscaledDeltaTime;
            var m = Mouse.current;
            if (m != null)
            {
                float s = m.scroll.ReadValue().y;
                if (Mathf.Abs(s) > 0.01f) distance = Mathf.Clamp(distance * (1f - Mathf.Sign(s) * 0.1f), 0.9f, 8f);
                if (m.rightButton.isPressed)
                {
                    Vector2 d = m.delta.ReadValue();
                    orbitYaw += d.x * 0.25f; pitch = Mathf.Clamp(pitch - d.y * 0.2f, 5f, 80f);
                }
            }
            yaw = Mathf.LerpAngle(yaw, target.eulerAngles.y, 1f - Mathf.Exp(-follow * 0.5f * dt));
            focus = Vector3.Lerp(focus, target.position + Vector3.up * height, 1f - Mathf.Exp(-follow * dt));
            var rot = Quaternion.Euler(pitch, yaw + yawOffset + orbitYaw, 0f);
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }
    }
}
