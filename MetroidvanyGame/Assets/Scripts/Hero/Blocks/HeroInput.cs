using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Hero
{
    /// <summary>Блок 0 · ВВОД. Мышь (или скрипт) → луч курсора, ЛКМ (держать / нажал / отпустил / клик),
    /// протяжка в мире по осям камеры (метры за кадр), ПКМ — облёт камеры (рука замирает), СКМ — лечь/сесть,
    /// ПЕТЛЯ мышью (курсор описал круг) → переключает «под / над» поверхностью.</summary>
    public class HeroInput : HeroBlock
    {
        public override int Order => 0;
        [Tooltip("Метров тяги на пиксель сдвига мыши.")]
        public float mouseToMeters = 0.0022f;
        public float clickMaxTime = 0.35f, clickMaxPixels = 22f;
        [Header("Петля")]
        [Tooltip("Петля: курсор поворачивает суммарно на столько градусов…")]
        public float loopMinAngle = 300f;
        [Tooltip("…за это время (с)…")]
        public float loopMaxTime = 0.9f;
        [Tooltip("…и укладывается в квадрат такого размера (пиксели).")]
        public float loopMaxSize = 180f;

        bool prevHold; Vector2 lastPx; float movedPx;
        Vector3 prevScript; bool hasPrevScript;
        readonly List<(float t, Vector2 p)> path = new List<(float, Vector2)>();

        public override void Tick(HeroController h, float dt)
        {
            var cam = h.cam; var m = Mouse.current;
            Vector2 px;
            Vector3 cr = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            Vector3 cf = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (h.scripted || m == null)
            {
                h.ray = new Ray(cam.transform.position, (h.scriptPoint - cam.transform.position).normalized);
                h.hold = h.scriptHold; h.toggleProne = h.scriptToggleProne; h.scriptToggleProne = false;
                px = cam.WorldToScreenPoint(h.scriptPoint);
                h.dragW = hasPrevScript ? Vector3.ProjectOnPlane(h.scriptPoint - prevScript, Vector3.up) : Vector3.zero;
                prevScript = h.scriptPoint; hasPrevScript = true;
                h.orbiting = false;
                if (h.scriptLoop != 0) { h.loop = h.scriptLoop; h.scriptLoop = 0; }
            }
            else
            {
                px = m.position.ReadValue();
                h.ray = cam.ScreenPointToRay(px);
                h.hold = m.leftButton.isPressed;
                h.toggleProne = m.middleButton.wasPressedThisFrame;
                h.orbiting = m.rightButton.isPressed;
                Vector2 md = m.delta.ReadValue();
                h.dragW = (cr * md.x + cf * md.y) * mouseToMeters;
                h.loop = DetectLoop(px);
            }
            h.down = h.hold && !prevHold; h.up = !h.hold && prevHold; prevHold = h.hold;
            h.hit = h.RayWorld(h.ray, out h.rh, h.preferUnder);
            if (h.down) { h.pressT = Time.time; movedPx = 0f; h.pressHit = h.hit; h.pressPoint = h.hit ? h.rh.point : Vector3.zero; }
            if (h.hold) movedPx += (px - lastPx).magnitude;
            lastPx = px;
            h.click = h.up && Time.time - h.pressT < clickMaxTime && movedPx < clickMaxPixels;
            if (h.loop != 0)
            {
                h.preferUnder = !h.preferUnder;
                h.Log("петля → " + (h.preferUnder ? "ПОД поверхность" : "НА поверхность"));
            }
        }

        /// <summary>Петля: суммарный поворот направления движения курсора ≥ loopMinAngle за loopMaxTime, в маленьком квадрате.</summary>
        int DetectLoop(Vector2 px)
        {
            float t = Time.time;
            if (path.Count == 0 || (px - path[path.Count - 1].p).sqrMagnitude > 9f) path.Add((t, px));
            while (path.Count > 0 && t - path[0].t > loopMaxTime) path.RemoveAt(0);
            if (path.Count < 6) return 0;
            float sum = 0f; Vector2 mn = path[0].p, mx = path[0].p;
            for (int i = 2; i < path.Count; i++)
            {
                Vector2 a = path[i - 1].p - path[i - 2].p, b = path[i].p - path[i - 1].p;
                sum += Vector2.SignedAngle(a, b);
                mn = Vector2.Min(mn, path[i].p); mx = Vector2.Max(mx, path[i].p);
            }
            Vector2 size = mx - mn;
            if (Mathf.Abs(sum) >= loopMinAngle && size.x < loopMaxSize && size.y < loopMaxSize && size.magnitude > 20f)
            {
                path.Clear();
                return sum > 0f ? -1 : 1;     // экран: y вверх; положительный угол — против часовой
            }
            return 0;
        }
    }
}
