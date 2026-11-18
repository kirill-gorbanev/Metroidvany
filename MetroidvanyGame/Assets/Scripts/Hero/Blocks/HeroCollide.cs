using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Блок 70 · ТЕЛО НЕ ВХОДИТ В ПРЕДМЕТЫ. Голова и грудь (HeroBody.Penetration), вошедшие в предмет,
    /// выталкивают тело наружу (а HeroBody сам гасит наклон).</summary>
    public class HeroCollide : HeroBlock
    {
        public override int Order => 70;
        public float pushSpeed = 0.6f;

        public override void Tick(HeroController h, float dt)
        {
            Vector3 push = Vector3.ProjectOnPlane(h.body.Penetration, Vector3.up);
            if (push.sqrMagnitude > 1e-6f) h.move += Vector3.ClampMagnitude(push, pushSpeed * dt + push.magnitude * 0.5f);
        }
    }
}
