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

        [Header("Умная камера (шаг 9 долга)")]
        [Tooltip("Камера подстраивается: смотрит вверх при лазании, чуть вперёд от тела, подъезжает, когда стена закрывает героя.")]
        public bool smart = true;
        [Tooltip("Лазание (держишься на крутом / висишь / кисть выше плеча): наклон камеры меньше на столько градусов — смотрит вверх.")]
        public float climbPitchDrop = 22f;
        [Tooltip("Лазание: точка взгляда смещается к кисти на эту долю пути тело → кисть.")]
        [Range(0f, 1f)] public float climbFocusToHand = 0.5f;
        [Tooltip("Взгляд впереди тела (м) — герой ниже на экране, впереди больше места (курсор реже уходит ниже героя).")]
        public float lead = 0.45f;
        [Tooltip("Стена / уступ между героем и камерой — камера подъезжает ближе (не ближе этого, м).")]
        public float occlusionMinDistance = 0.7f;
        public float smartFollow = 4f;

        HeroProto hero;
        float climbK, distEff = -1f;
        Renderer[] wallRends;
        /// <summary>Сейчас: насколько камера в режиме лазания (0…1) и действующая дальность (для замеров).</summary>
        public float ClimbK => climbK;
        public float DistanceNow => distEff < 0f ? distance : distEff;

        public static bool Orbiting { get; private set; }
        Vector3 focus;
        float lastBodyYaw;

        float pitch0 = float.NaN, distance0;
        /// <summary>Симуляция: камера за спину телу, наклон и дальность — как на старте.</summary>
        public void ResetView()
        {
            if (float.IsNaN(pitch0)) return;
            pitch = pitch0; distance = distance0; climbK = 0f; distEff = -1f; wallRends = null;
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
            Vector3 want = target.position + Vector3.up * lookHeight;
            if (smart && Hero())
            {
                // ЛАЗАНИЕ: держишься на крутом, висишь, кисть заметно выше плеча — смотреть вверх, к кисти
                var st = hero.State;
                // на крутом, висишь, держишься выше плеча
                bool climb = st == HeroProto.HeroState.Hanging || hero.OnSteep
                             || (hero.Gripping && hero.handCube && hero.handCube.position.y > hero.shoulderCube.position.y + 0.1f);
                climbK = Mathf.MoveTowards(climbK, climb ? 1f : 0f, dt * 2.5f);
                Vector3 face = hero.FaceDir;
                Vector3 toHand = hero.handCube ? hero.handCube.position - target.position : Vector3.zero;
                want += face * lead * (1f - climbK) + toHand * climbFocusToHand * climbK;
            }
            else climbK = 0f;
            focus = Vector3.Lerp(focus, want, 1f - Mathf.Exp(-follow * dt));
            // СТЕНА МЕЖДУ ГЕРОЕМ И КАМЕРОЙ: подъехать ближе (плавно; отъезжать — тоже плавно)
            float dWant = distance;
            if (smart && Hero()) dWant = Mathf.Max(occlusionMinDistance, Mathf.Min(distance, Clear(Quaternion.Euler(PitchNow, yaw, 0f))));
            distEff = distEff < 0f ? dWant : Mathf.Lerp(distEff, dWant, 1f - Mathf.Exp(-(dWant < distEff ? 12f : smartFollow) * dt));
            Apply();
        }

        float PitchNow => Mathf.Clamp(pitch - climbPitchDrop * climbK, minPitch, maxPitch);

        bool Hero()
        {
            if (hero) return true;
            if (!target) return false;
            hero = target.GetComponentInParent<HeroProto>();
            if (!hero) hero = FindObjectOfType<HeroProto>();
            return hero;
        }

        /// <summary>Свободная дальность от точки взгляда назад по лучу камеры: стены (кубики-препятствия) и поверхность выше луча.</summary>
        float Clear(Quaternion rot)
        {
            Vector3 back = -(rot * Vector3.forward);
            float free = distance;
            if (hero.obstaclesRoot)
            {
                if (wallRends == null || (wallRends.Length > 0 && !wallRends[0])) wallRends = hero.obstaclesRoot.GetComponentsInChildren<Renderer>();
                var ray = new Ray(focus, back);
                foreach (var r in wallRends)
                    if (r && r.enabled && r.gameObject.activeInHierarchy && r.bounds.IntersectRay(ray, out float d) && d < free) free = d;
            }
            for (int i = 1; i <= 16; i++)
            {
                float d = distance * i / 16f; if (d >= free) break;
                Vector3 p = focus + back * d;
                if (hero.Height(p) > p.y + 0.05f) { free = d; break; }        // уступ / стена-поверхность выше луча
            }
            return free < distance ? free - 0.15f : distance;              // запас от стены — только если стена есть
        }

        void Apply()
        {
            var rot = Quaternion.Euler(smart ? PitchNow : pitch, yaw, 0f);
            float d = smart && distEff > 0f ? distEff : distance;
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * d, rot);
        }
    }
}
