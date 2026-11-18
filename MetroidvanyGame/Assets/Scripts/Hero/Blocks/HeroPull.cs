using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Блок 40 · ТЕЛО ОТ ЖЕСТА ПРИ ХВАТЕ (зеркально мыши).
    /// • На себя — тело к руке, от себя — назад (в пределах сгиба руки: таз не ближе minPelvisToHand, рука не длиннее досягаемости).
    /// • Вбок: рука СБОКУ (брус, стена) — тело едет вдоль неё; рука ВПЕРЕДИ — тело поворачивается вокруг ладони.
    /// • ОБРАТНЫЙ ХВАТ (рука сзади): только движение задом — к руке; от себя и повороты — нет.
    /// • Подъём из сгиба руки: край выше пола и не выше maxClimb — тело поднимается; выше — только напрягается.</summary>
    public class HeroPull : HeroBlock
    {
        public override int Order => 40;
        [Tooltip("Ближе этого таз к точке хвата не подтягивается (м).")]
        public float minPelvisToHand = 0.30f;
        [Tooltip("Отталкивание назад — доля скорости тяги.")]
        public float backFrac = 0.8f;
        [Tooltip("Рука сбоку, если угол от «вперёд» больше (градусы): вбок — вдоль, иначе — разворот.")]
        public float sideAngle = 25f;
        [Tooltip("Рука сзади (обратный хват), если угол больше (градусы).")]
        public float reverseAngle = 110f;
        public float strafeGain = 0.6f, turnPerMeter = 200f, turnRate = 120f;
        [Tooltip("Предел подъёма: выше этого край не осилить (м).")]
        public float maxClimb = 0.42f;

        public override void Tick(HeroController h, float dt)
        {
            if (h.mode != HeroController.Mode.Grip) return;
            Vector3 anchor = h.Anchor;
            Vector3 F = Vector3.ProjectOnPlane(h.transform.forward, Vector3.up).normalized;
            float angA = Vector3.SignedAngle(F, h.dirA, Vector3.up);
            bool reverse = Mathf.Abs(angA) > reverseAngle;
            bool sideways = !reverse && Mathf.Abs(angA) > sideAngle;
            float vmax = h.vmaxBody * dt;

            float along = Mathf.Clamp(h.along, -vmax * backFrac, vmax);
            if (reverse) along = Mathf.Max(0f, along);                  // обратный хват — только к руке (задом)
            float distR = Vector3.ProjectOnPlane(anchor - h.transform.position, Vector3.up).magnitude;
            float distS = Vector3.Dot(Vector3.ProjectOnPlane(anchor - h.S, Vector3.up), h.dirA);
            if (distR - along < minPelvisToHand) along = Mathf.Max(0f, distR - minPelvisToHand);
            if (distS - along > h.reachMax) along = Mathf.Min(0f, distS - h.reachMax);
            h.along = along;
            Vector3 move = h.dirA * along;

            if (!reverse && Mathf.Abs(h.side) > 1e-5f)
            {
                if (sideways)
                    move += h.sideA * Mathf.Clamp(h.side * strafeGain, -vmax * 0.6f, vmax * 0.6f);   // вдоль стены/бруса
                else
                {
                    float ang = Mathf.Clamp(h.side * turnPerMeter, -turnRate * dt, turnRate * dt);   // вокруг ладони
                    var q = Quaternion.AngleAxis(ang, Vector3.up);
                    Vector3 piv = new Vector3(anchor.x, h.transform.position.y, anchor.z);
                    move += (piv + q * (h.transform.position - piv)) - h.transform.position;
                    h.transform.rotation = q * h.transform.rotation;
                }
            }
            h.move += move;
            h.pullSpeed = Mathf.Max(h.pullSpeed, move.magnitude / dt);

            float dh = anchor.y - h.transform.position.y;
            if (along > 0f && dh > h.CC.stepOffset && distR < 0.55f)
            {
                if (dh <= maxClimb) { h.move += Vector3.up * Mathf.Min(dh + 0.02f, along * 2.2f + 0.002f); h.climbing = true; }
                else h.strain = 1f;
            }
        }
    }
}
