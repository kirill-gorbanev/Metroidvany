using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Блок 30 · ХВАТ. Начать: ЛКМ (или буфер нажатия) + ладонь у поверхности/выступа в радиусе r (автозацеп),
    /// или «койот» — последняя точка касания; перехват долетел — схватиться. Держать: рука в точке хвата (едет с предметом).
    /// Отпустить: ЛКМ отпущена или рука вытянута за предел.
    /// При хвате раскладывает жест на «на себя / от себя» (along) и «вбок» (side) относительно направления таз→рука.</summary>
    public class HeroGrip : HeroBlock
    {
        public override int Order => 30;
        [Tooltip("Буфер нажатия: ЛКМ, нажатая раньше касания, всё равно хватает (с).")]
        public float grabBuffer = 0.20f;
        [Tooltip("Койот: ЛКМ сразу после потери касания хватает последнюю точку (с).")]
        public float coyoteTime = 0.12f;
        public float pullGain = 1.0f;

        public override void Tick(HeroController h, float dt)
        {
            if (h.mode == HeroController.Mode.Free)
            {
                if (h.reachTo.HasValue)
                {
                    if (!h.hold || Time.time > h.reachUntil) { h.reachTo = null; return; }
                    if (Vector3.Distance(h.handPos, h.reachTo.Value) < 0.07f)
                    {
                        var rt = h.reachTo.Value;
                        if (h.FindGrip(rt, h.grabRadius, out var p2, out var n2, out var c2)) h.BeginGrip(p2, n2, c2);
                        else h.BeginGrip(rt, h.reachToN, null);
                    }
                    return;
                }
                if (h.hold && (h.down || Time.time - h.pressT <= grabBuffer))
                {
                    if (h.FindGrip(h.handPos, h.grabRadius, out var gp, out var gn, out var gc)) h.BeginGrip(gp, gn, gc);
                    else if (Time.time - h.lastContactT <= coyoteTime) h.BeginGrip(h.contactPoint, h.contactN, null);
                }
                return;
            }
            if (h.mode != HeroController.Mode.Grip) return;

            Vector3 anchor = h.Anchor;
            h.handPos = anchor + h.anchorN * 0.025f;
            h.handVelS = Vector3.zero;
            Vector3 toR = Vector3.ProjectOnPlane(anchor - h.transform.position, Vector3.up);
            h.dirA = toR.sqrMagnitude > 1e-3f ? toR.normalized : h.transform.forward;
            h.sideA = Vector3.Cross(Vector3.up, h.dirA);
            h.along = Vector3.Dot(-h.dragW, h.dirA) * pullGain;     // мышь на себя → + (тело к руке), зеркально
            h.side = Vector3.Dot(-h.dragW, h.sideA) * pullGain;
            h.wantAlong = h.along;
            h.swipe = -h.along / dt;                                 // > 0 — жест от себя (м/с)

            if (h.up || Vector3.Distance(h.S, anchor) > h.reachMax + 0.28f)
            {
                h.Log(h.up ? "отпустил (ЛКМ)" : $"отпустил: рука вытянута ({Vector3.Distance(h.S, anchor):F2} > {h.reachMax + 0.28f:F2})");
                var hitRb = h.anchorRb;
                h.EndGrip();
                var hook = h.GetComponent<HeroHook>();
                if (h.click && hook && h.hooked && hitRb == h.hooked) hook.Unhook(h);
            }
        }
    }
}
