using UnityEngine;

namespace Fortress.Walker
{
    /// <summary>Камера от третьего лица: мышь — обзор, колесо — дистанция, не проваливается в стены.
    /// Esc освобождает курсор, клик — снова захватывает.</summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float height = 1.6f;
        public float distance = 4.0f;
        public float minDistance = 1.2f;
        public float maxDistance = 9f;
        public float sensitivity = 0.12f;
        public float yaw, pitch = 15f;
        [Tooltip("Сглаживание высоты точки обзора (с): ступени лестницы не дёргают камеру")] public float heightSmooth = 0.15f;
        PlayerWalker walker;
        float pivotY, pivotVel;
        bool pivotInit;
        float curD, curDVel;
        bool dInit;

        void Start() { Lock(true); }

        static void Lock(bool on)
        {
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        void LateUpdate()
        {
            if (!target) return;
            if (WalkerInput.EscDown) Lock(false);
            if (WalkerInput.ClickDown && Cursor.lockState != CursorLockMode.Locked) Lock(true);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 look = WalkerInput.Look;
                yaw += look.x * sensitivity;
                pitch = Mathf.Clamp(pitch - look.y * sensitivity, -45f, 80f);
            }
            distance = Mathf.Clamp(distance - WalkerInput.Scroll * 0.004f, minDistance, maxDistance);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
            // высота точки обзора — плавно: капсула на лестнице поднимается ступеньками; телепорт — сразу
            float ty = target.position.y;
            if (!pivotInit || Mathf.Abs(ty - pivotY) > 2f) { pivotY = ty; pivotVel = 0f; pivotInit = true; dInit = false; }
            pivotY = Mathf.SmoothDamp(pivotY, ty, ref pivotVel, heightSmooth);
            Vector3 pivot = new Vector3(target.position.x, pivotY + height, target.position.z);
            Vector3 dir = rot * Vector3.back;
            float d = distance;
            // стена или потолок между героем и камерой — камера подъезжает к герою (обычная камера от третьего лица)
            var hits = Physics.SphereCastAll(pivot, 0.22f, dir, distance, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(target)) continue;
                if (h.distance > 0f && h.distance < d) d = Mathf.Max(0.3f, h.distance - 0.05f);
            }
            // к герою — сразу (стена не мелькает), назад — плавно (не скачет)
            if (!dInit || d < curD) { curD = d; curDVel = 0f; dInit = true; }
            else curD = Mathf.SmoothDamp(curD, d, ref curDVel, 0.25f);
            d = curD;
            Vector3 cam = pivot + dir * d;
            // камера не заходит в тонкие детали (настил, балки): если рядом с ней коллайдер — ближе к игроку
            for (int s = 0; s < 8 && d > 0.3f; s++)
            {
                bool inside = false;
                foreach (var c in Physics.OverlapSphere(cam, 0.18f, ~0, QueryTriggerInteraction.Ignore))
                    if (!c.transform.IsChildOf(target)) { inside = true; break; }
                if (!inside) break;
                d = Mathf.Max(0.3f, d - 0.25f);
                cam = pivot + dir * d;
            }
            // плоскость воды видна только сверху: камера плывущего не ныряет под неё
            if (!walker) walker = target.GetComponent<PlayerWalker>();
            if (walker && !float.IsNaN(walker.WaterSurface) && cam.y < walker.WaterSurface + 0.3f)
                cam.y = walker.WaterSurface + 0.3f;
            transform.position = cam;
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * 0.1f - cam, Vector3.up);
            if ((cam - (pivot + dir * d)).sqrMagnitude < 1e-6f) transform.rotation = rot;
        }
    }
}
