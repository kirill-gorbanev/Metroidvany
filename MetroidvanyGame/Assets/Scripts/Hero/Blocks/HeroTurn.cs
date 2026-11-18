using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Блок 60 · РАЗВОРОТ КЛИКОМ. Короткий клик ЛКМ по земле (не по предмету) — повернуться к точке.
    /// Сам по себе герой не крутится.</summary>
    public class HeroTurn : HeroBlock
    {
        public override int Order => 60;
        public float turnRate = 140f;

        public override void Tick(HeroController h, float dt)
        {
            if (h.click && h.pressHit && h.mode == HeroController.Mode.Free && !h.carried)
            {
                Vector3 d = Vector3.ProjectOnPlane(h.pressPoint - h.transform.position, Vector3.up);
                if (d.sqrMagnitude > 0.01f) { h.turnTo = Quaternion.LookRotation(d).eulerAngles.y; h.Log($"разворот к {h.pressPoint}"); }
            }
            if (!h.turnTo.HasValue) return;
            if (h.mode == HeroController.Mode.Grip) { h.turnTo = null; return; }
            float y = Mathf.MoveTowardsAngle(h.transform.eulerAngles.y, h.turnTo.Value, turnRate * dt);
            h.transform.rotation = Quaternion.Euler(0f, y, 0f);
            if (Mathf.Abs(Mathf.DeltaAngle(y, h.turnTo.Value)) < 0.5f) h.turnTo = null;
        }
    }
}
