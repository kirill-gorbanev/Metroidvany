using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Rig.Tests
{
    /// <summary>Камера стенда: правая кнопка — вращать, средняя — двигать, колесо — ближе/дальше; следит за выбранным телом.</summary>
    public class BenchCamera : MonoBehaviour
    {
        public Vector3 focus = new Vector3(0f, 0.4f, 0f);
        public float distance = 5.5f, yaw = 35f, pitch = 26f;
        public Transform follow;

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var m = Mouse.current;
            if (m != null)
            {
                Vector2 d = m.delta.ReadValue();
                if (m.rightButton.isPressed) { yaw += d.x * 0.22f; pitch = Mathf.Clamp(pitch - d.y * 0.22f, 2f, 88f); }
                if (m.middleButton.isPressed)
                {
                    float k = distance * 0.0016f;
                    focus -= transform.right * d.x * k + Vector3.ProjectOnPlane(transform.up, Vector3.up).normalized * d.y * k;
                    follow = null;
                }
                float s = m.scroll.ReadValue().y;
                if (Mathf.Abs(s) > 0.01f) distance = Mathf.Clamp(distance * (1f - Mathf.Sign(s) * 0.1f), 1.0f, 40f);
            }
            if (follow) focus = Vector3.Lerp(focus, follow.position + Vector3.up * 0.25f, 1f - Mathf.Exp(-6f * dt));
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }
    }
}
