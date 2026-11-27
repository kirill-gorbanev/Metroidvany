using UnityEngine;

namespace Fortress.Hero.Arm2
{
    /// <summary>БЛОК «КАМЕРА» нового героя. Поведение — как у ProtoOrbitCamera (проверено автором): орбита за спиной;
    /// вправо-влево — только вместе с телом; ПКМ + мышь вверх / вниз — наклон; колесо — дальше / ближе.
    /// Умной камеры нет (автор 2026-11-21: «отключи этот эффект камеры»).
    /// Читает интерполированную позу тела после кадра (LateUpdate) — ничего в физику не пишет.
    /// Отличие от старой: рыскание берётся по проекции «вперёд» тела на пол — при кувырке тела камера не дёргается.</summary>
    public class Arm2Camera : MonoBehaviour
    {
        public Arm2Body target;
        public float yaw = 0f, pitch = 50f, distance = 2.6f;
        public float pitchSpeed = 0.2f;
        public float minPitch = 10f, maxPitch = 85f, minDistance = 0.8f, maxDistance = 8f;
        [Tooltip("Точка взгляда — над телом (м).")]
        public float lookHeight = 0.15f;
        [Arm2Tune("камера: скорость догона", 1f, 30f)]
        public float follow = 6f;

        Vector3 focus; float lastBodyYaw; bool started;
        float pitch0, distance0;

        void Start() { ResetView(); }

        /// <summary>Вид из записи ввода: луч курсора идёт из камеры — тот же ввод должен попадать в те же точки.</summary>
        public void SetView(float yaw, float pitch, float distance)
        {
            if (!started) { pitch0 = this.pitch; distance0 = this.distance; started = true; }
            this.yaw = yaw; this.pitch = pitch; this.distance = distance;
            if (target) { focus = target.transform.position + Vector3.up * lookHeight; lastBodyYaw = target.Yaw(yaw); }
            Apply();
        }

        /// <summary>Камера за спину телу, наклон и дальность — как на старте.</summary>
        public void ResetView()
        {
            if (!started) { pitch0 = pitch; distance0 = distance; started = true; }
            pitch = pitch0; distance = distance0;
            if (target) { focus = target.transform.position + Vector3.up * lookHeight; lastBodyYaw = yaw = target.Yaw(yaw); }
            Apply();
        }

        public void Step(float dt, Arm2InputFrame f)
        {
            if (!target) return;
            if (f.rmb)
                pitch = Mathf.Clamp(pitch - f.delta.y * (1080f / Mathf.Max(1, f.screenH)) * pitchSpeed, minPitch, maxPitch);   // градусы на px при высоте экрана 1080
            if (Mathf.Abs(f.scroll) > 0.01f) distance = Mathf.Clamp(distance * (1f - Mathf.Sign(f.scroll) * 0.1f), minDistance, maxDistance);
            float by = target.Yaw(lastBodyYaw);
            yaw += Mathf.DeltaAngle(lastBodyYaw, by); lastBodyYaw = by;
            Vector3 want = target.transform.position + Vector3.up * lookHeight;
            focus = Vector3.Lerp(focus, want, 1f - Mathf.Exp(-follow * dt));
            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }
    }
}
