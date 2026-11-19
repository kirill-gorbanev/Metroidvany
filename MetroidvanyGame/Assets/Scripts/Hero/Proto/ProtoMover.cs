using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>Подвижная поверхность — отдельный блок: лифт, платформа-челнок, поворотный круг.
    /// Ездит туда-обратно на offset с остановками wait и/или крутится spinSpeed °/с. Без физики.
    /// Каждый шаг сообщает свой сдвиг за кадр (Delta: мир до → мир после); HeroProto везёт на нём тело и кисть.
    /// Поверхность — сетки детей этого объекта (сам корень — без масштаба). Обновляется раньше героя.</summary>
    [DefaultExecutionOrder(-100)]
    public class ProtoMover : MonoBehaviour
    {
        [Tooltip("Куда ехать от начального места (м, мир). 0 — не ездит.")]
        public Vector3 offset;
        [Tooltip("Время в пути в одну сторону (с).")]
        public float travelTime = 3f;
        [Tooltip("Стоянка на каждом конце (с).")]
        public float wait = 2f;
        [Tooltip("Вращение вокруг вертикали (°/с), без остановок. 0 — не крутится.")]
        public float spinSpeed;
        [Tooltip("Поворот РЫВКАМИ: на столько градусов за travelTime, потом стоянка wait. 0 — выкл.")]
        public float spinStep;
        [Tooltip("Сдвиг фазы (с) — чтобы несколько платформ не ходили в такт.")]
        public float phase;
        [Tooltip("Ехать само (в Update). Выключено — только Step() (тесты).")]
        public bool autoRun = true;

        /// <summary>Сдвиг за последний шаг: точка в мире до шага → после.</summary>
        public Matrix4x4 Delta { get; private set; } = Matrix4x4.identity;
        /// <summary>Номер шага — чтобы герой применил сдвиг ровно один раз.</summary>
        public int StepId { get; private set; }

        Vector3 start; Quaternion startRot; float time;

        void Awake() { start = transform.position; startRot = transform.rotation; time = phase; }

        void Update() { if (autoRun && !HeroProto.ManualTick) Step(Time.deltaTime); }

        public void Step(float dt)
        {
            Matrix4x4 prev = transform.localToWorldMatrix;
            time += dt;
            if (offset != Vector3.zero)
            {
                float tt = Mathf.Max(0.01f, travelTime), cycle = 2f * (tt + wait), ph = Mathf.Repeat(time, cycle), k;
                if (ph < wait) k = 0f;
                else if (ph < wait + tt) k = Smooth((ph - wait) / tt);
                else if (ph < 2f * wait + tt) k = 1f;
                else k = 1f - Smooth((ph - 2f * wait - tt) / tt);
                transform.position = start + offset * k;
            }
            if (spinSpeed != 0f) transform.rotation = Quaternion.AngleAxis(spinSpeed * time, Vector3.up) * startRot;
            else if (spinStep != 0f)
            {
                // стоянка, потом поворот на spinStep; угол растёт ступеньками
                float tt = Mathf.Max(0.01f, travelTime), cyc = tt + wait;
                float n = Mathf.Floor(time / cyc), ph = time - n * cyc;
                float k = ph < wait ? 0f : Smooth((ph - wait) / tt);
                transform.rotation = Quaternion.AngleAxis(spinStep * (n + k), Vector3.up) * startRot;
            }
            Delta = transform.localToWorldMatrix * prev.inverse;
            StepId++;
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);
    }
}
