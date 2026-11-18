using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Блок 38 · ПРЕДМЕТЫ.
    /// • Подвижный (ящик) при хвате — в руке: тянешь — едет к тебе, толкаешь — от тебя; смещение делится по массам
    ///   (ma = N − g − F: тело получает m/(m+M), предмет — M/(m+M)). Резкий свайп от себя — толчок: отпускает с импульсом.
    /// • Лёгкий (≤ liftMaxMass) — берётся в руку и несётся. Отпустил: свайп — бросок, плавно — положил,
    ///   у макушки — на голову (LOCK, держится прямо, чуть клонится с головой). ЛКМ по нему на голове — снова в руке.</summary>
    public class HeroObjects : HeroBlock
    {
        public override int Order => 38;
        [Header("Держать / толкать")]
        [Tooltip("Тяжелее этого (× масса тела) — не сдвинуть, только опора.")]
        public float holdMaxRatio = 4f;
        [Tooltip("Скорость свайпа (м/с), с которой жест — толчок.")]
        public float swipeSpeed = 1.2f;
        public float pushImpulsePerSpeed = 9f, maxPushImpulse = 40f;
        [Tooltip("Предмет в руке не ближе этого к тазу (м).")]
        public float minHoldDist = 0.35f;
        [Header("Взять / бросить / на голову")]
        public float liftMaxMass = 6f;
        public float throwSpeed = 1.3f, throwGain = 1.7f;
        public float headLockRadius = 0.32f;

        Vector3 heldPos; Vector3 carryOffset; Quaternion carryRot; float headLift;

        public bool TryTake(HeroController h, Rigidbody rb)
        {
            if (rb.isKinematic || rb.mass > liftMaxMass || rb == h.hooked) return false;
            h.IgnoreBody(rb, true);
            h.carried = rb; h.mode = HeroController.Mode.Carry;
            rb.isKinematic = true;
            carryOffset = Vector3.ClampMagnitude(rb.position - h.handPos, 0.15f) + Vector3.down * 0.05f;
            carryRot = rb.rotation;
            h.Log($"взял {rb.name}");
            return true;
        }

        public void TryHold(HeroController h, Rigidbody rb)
        {
            if (rb.isKinematic || rb == h.hooked || rb.mass >= h.bodyMass * holdMaxRatio) return;
            h.held = rb; rb.isKinematic = true; h.IgnoreBody(rb, true);
            heldPos = rb.position;
        }

        public void ReleaseHeld(HeroController h)
        {
            if (!h.held) return;
            h.held.isKinematic = false; h.held.linearVelocity = Vector3.zero;
            h.IgnoreBody(h.held, false);
            h.held = null;
        }

        public override void Tick(HeroController h, float dt)
        {
            // ---- ящик в руке
            if (h.mode == HeroController.Mode.Grip && h.held)
            {
                if (h.swipe > swipeSpeed)
                {
                    float J = Mathf.Min(maxPushImpulse, pushImpulsePerSpeed * h.swipe);
                    var rb = h.held; var at = h.Anchor; var dir = h.dirA;
                    h.EndGrip();                                           // отпускает (ReleaseHeld)
                    rb.AddForceAtPosition(dir * J, at, ForceMode.Impulse);
                    h.Log($"толчок {rb.name}: свайп {h.swipe:F1} м/с → {J:F1} Н·с");
                    h.along = h.side = h.wantAlong = 0f;
                }
                else
                {
                    float vmax = h.vmaxBody * dt;
                    float along = Mathf.Clamp(h.along, -vmax, vmax);
                    float share = h.held.mass / (h.held.mass + h.bodyMass);   // доля тела
                    float obj = -along * (1f - share);                        // + — предмет от тела
                    float d = Vector3.Dot(Vector3.ProjectOnPlane(h.held.position - h.transform.position, Vector3.up), h.dirA);
                    if (d + obj < minHoldDist) obj = Mathf.Max(obj, minHoldDist - d);
                    heldPos = h.held.position + h.dirA * obj;
                    h.held.position = heldPos; h.held.transform.position = heldPos;
                    h.along = along * share;                                  // телу — остаток
                    h.side = 0f;
                    h.load = Mathf.Max(h.load, Mathf.Clamp01(h.held.mass / (h.bodyMass * 2f)));
                }
            }

            // ---- лёгкий в руке
            if (h.mode == HeroController.Mode.Carry && h.carried)
            {
                var c = h.carried;
                Vector3 p = h.handPos + carryOffset;
                c.position = p; c.transform.position = p; c.rotation = carryRot; c.transform.rotation = carryRot;
                h.load = Mathf.Max(h.load, Mathf.Clamp01(c.mass / liftMaxMass) * 0.6f);
                if (h.up)
                {
                    if (h.handVelS.magnitude > throwSpeed) Throw(h, h.handVelS * throwGain);
                    else if (Vector3.Distance(h.handPos, h.body.HeadTop) < headLockRadius && !h.onHead) PutOnHead(h);
                    else Place(h);
                }
            }

            // ---- на голове
            if (h.onHead && h.body.HeadBone)
            {
                Vector3 hup = Vector3.Slerp(Vector3.up, h.body.HeadBone.position - h.body.ChestPosition, 0.25f).normalized;
                var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(h.transform.forward, hup), hup);
                Vector3 p = h.body.HeadTop + hup * (headLift + 0.01f);
                h.onHead.position = p; h.onHead.transform.position = p; h.onHead.rotation = rot; h.onHead.transform.rotation = rot;
                if (h.mode == HeroController.Mode.Free && h.down && Vector3.Distance(h.handPos, h.onHead.position) < h.grabRadius + 0.25f)
                {
                    var rb = h.onHead; h.onHead = null;
                    rb.isKinematic = false;
                    TryTake(h, rb);
                }
            }
        }

        void Throw(HeroController h, Vector3 v)
        {
            var rb = h.carried; h.carried = null; h.mode = HeroController.Mode.Free;
            rb.isKinematic = false; rb.linearVelocity = v + Vector3.up * 0.8f;
            h.IgnoreBody(rb, false);
            h.Log($"бросил {rb.name}: {rb.linearVelocity}");
        }

        void Place(HeroController h)
        {
            var rb = h.carried; h.carried = null; h.mode = HeroController.Mode.Free;
            rb.isKinematic = false; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            h.IgnoreBody(rb, false);
            h.Log($"положил {rb.name}");
        }

        void PutOnHead(HeroController h)
        {
            var rb = h.carried; h.carried = null; h.mode = HeroController.Mode.Free;
            h.onHead = rb; rb.isKinematic = true;
            rb.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(h.transform.forward, Vector3.up), Vector3.up);
            rb.transform.rotation = rb.rotation;
            Physics.SyncTransforms();
            float bottom = float.MaxValue;
            foreach (var c in rb.GetComponentsInChildren<Collider>()) bottom = Mathf.Min(bottom, c.bounds.min.y);
            headLift = rb.transform.position.y - bottom;
            h.Log($"на голову: {rb.name}");
        }
    }
}
