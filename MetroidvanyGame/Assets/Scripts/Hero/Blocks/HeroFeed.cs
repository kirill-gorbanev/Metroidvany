using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Блок 90 · ВХОД ПОЗЫ ТЕЛА. Передаёт HeroBody: положение (сидя/ничком), цель руки, ладонь на поверхности,
    /// куда смотреть и усилие (тяга, рука на пределе, подъём, «не осилить», вес ноши) — от него дыхание и лицо.</summary>
    public class HeroFeed : HeroBlock
    {
        public override int Order => 90;
        float effort;

        public override void Tick(HeroController h, float dt)
        {
            var b = h.body;
            float stretch = Mathf.Clamp01((Vector3.Distance(h.S, h.handPos) - h.reach * 0.8f) / 0.35f);
            float pullK = Mathf.Clamp01(h.pullSpeed / h.vmaxBody);
            effort = Mathf.MoveTowards(effort, Mathf.Max(pullK, stretch * 0.6f, h.climbing ? 0.9f : 0f, h.strain, h.load), dt * 2f);
            bool grip = h.mode == HeroController.Mode.Grip;
            b.prone = h.prone;
            b.handTarget = h.handPos;
            b.handWeight = 1f;
            b.handPlanted = grip || h.contact;
            b.handNormal = grip ? h.anchorN : h.contactN;
            b.gripping = grip;
            // сгиб локтя при хвате: чем ближе таз к руке (подтянулся), тем сильнее согнута рука — тяга идёт сгибом локтя
            if (grip)
            {
                float distR = Vector3.ProjectOnPlane(h.Anchor - h.transform.position, Vector3.up).magnitude;
                var pull = h.GetComponent<HeroPull>();
                float minD = pull ? pull.minPelvisToHand : 0.3f;
                float progress = Mathf.Clamp01((h.reachMax - distR) / Mathf.Max(0.05f, h.reachMax - minD));
                b.elbowTarget = Mathf.Lerp(0.85f, 0.55f, progress);
            }
            else b.elbowTarget = 0.97f;
            b.lookTarget = grip ? h.handPos + Vector3.up * 0.1f : (h.hit ? h.rh.point : h.ray.GetPoint(4f));
            b.effort = effort;
        }
    }
}
