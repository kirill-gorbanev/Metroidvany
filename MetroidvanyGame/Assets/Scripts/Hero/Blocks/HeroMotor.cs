using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Блок 80 · МОТОР. Двигает капсулу (CharacterController) на сумму движений блоков + гравитация
    /// (при подъёме — без гравитации). Записывает фактическое смещение (moved) для блоков после него.</summary>
    public class HeroMotor : HeroBlock
    {
        public override int Order => 80;
        float vy;

        public override void Tick(HeroController h, float dt)
        {
            var cc = h.CC;
            if (h.climbing) vy = 0f;
            else { if (cc.isGrounded && vy < 0f) vy = -1f; vy += Physics.gravity.y * dt; }
            Vector3 before = h.transform.position;
            cc.Move(h.move + Vector3.up * vy * dt);
            h.moved = h.transform.position - before;
        }
    }
}
