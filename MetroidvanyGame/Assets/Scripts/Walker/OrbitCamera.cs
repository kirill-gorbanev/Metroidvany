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
        [Tooltip("Окно сквозь стены вокруг героя (SeeThroughCamera): камера не прижимается к герою, стены между ними вырезаются сеткой")]
        public bool seeThroughWalls = true;
        [Tooltip("С окном сквозь стены: стены подтягивают камеру не ближе этого к герою, м")] public float keepMin = 1.8f;
        [Tooltip("С окном сквозь стены: камера не ниже этого над полом под ней, м")] public float floorClear = 0.35f;
        [Tooltip("С окном сквозь стены: камера не выше этого под потолком комнаты героя, м")] public float ceilClear = 0.2f;
        PlayerWalker walker;
        SeeThroughCamera seeThrough;
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
            if (!seeThrough) seeThrough = GetComponent<SeeThroughCamera>();
            if (!seeThrough && seeThroughWalls)
            {   // окно сквозь стены ставится само (и после перекомпиляции в Play)
                seeThrough = gameObject.AddComponent<SeeThroughCamera>();
                seeThrough.target = target;
            }
            if (seeThrough && !seeThrough.target) seeThrough.target = target;
            bool through = seeThrough && seeThrough.isActiveAndEnabled;
            Vector3 cam;
            if (through)
            {
                // разрез перед героем (SeeThroughCamera): стена или потолок сзади подтягивают камеру, но не ближе keepMin;
                // если и там заслоняет — всё ближе героя убирается целиком в высоте его этажа (изнанка стен тёмная);
                // под пол камера не уходит — поднимается над ним, выше потолка комнаты героя — тоже нет
                var hits = Physics.SphereCastAll(pivot, 0.22f, dir, distance, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h.collider.transform.IsChildOf(target)) continue;
                    if (h.distance > 0f && h.distance < d) d = h.distance - 0.05f;
                }
                d = Mathf.Max(d, Mathf.Min(keepMin, distance));
                // к герою — быстро (стена не мелькает), назад — плавно (не скачет)
                if (!dInit || d < curD) curD = Mathf.MoveTowards(dInit ? curD : d, d, 25f * Time.deltaTime);
                else curD = Mathf.SmoothDamp(curD, d, ref curDVel, 0.3f);
                dInit = true;
                d = curD;
                cam = pivot + dir * d;
                // не выше потолка комнаты героя (низкий подвал: иначе камера над перекрытием, смотрит на пол этажа выше)
                if (cam.y > pivot.y && Physics.SphereCast(pivot, 0.15f, Vector3.up, out var ch, cam.y - pivot.y + 0.3f, ~0, QueryTriggerInteraction.Ignore)
                    && !ch.collider.transform.IsChildOf(target))
                    cam.y = Mathf.Min(cam.y, pivot.y + ch.distance + 0.15f - ceilClear);
                // не под полом: камера ниже головы и под ней пол выше неё — поднять над полом
                if (cam.y < pivot.y)
                {
                    Vector3 from = new Vector3(cam.x, pivot.y, cam.z);
                    if (Physics.Raycast(from, Vector3.down, out var fh, pivot.y - cam.y + floorClear, ~0, QueryTriggerInteraction.Ignore)
                        && !fh.collider.transform.IsChildOf(target) && cam.y < fh.point.y + floorClear)
                        cam.y = fh.point.y + floorClear;
                }
            }
            else
            {
                var hits = Physics.SphereCastAll(pivot, 0.22f, dir, distance, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h.collider.transform.IsChildOf(target)) continue;
                    if (h.distance > 0f && h.distance < d) d = Mathf.Max(0.3f, h.distance - 0.05f);
                }
                cam = pivot + dir * d;
            }
            // камера не заходит в тонкие детали (настил, балки): если рядом с ней коллайдер — ближе к игроку
            for (int s = 0; s < 8 && d > 0.3f && !through; s++)
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
