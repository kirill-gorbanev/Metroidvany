using UnityEngine;

namespace Fortress.Hero.Arm2
{
    /// <summary>Касания тела — из самой физики (события столкновений тела), без запросов. Висит на теле героя.
    /// Нужны блоку «Рука», чтобы понять: тело упёрлось во что-то впереди по ходу тяги (грань ступени).
    /// Урок 4: это те же коллайдеры, что двигаются; урок 11: никаких своих прямоугольников «обрыв / стена».</summary>
    public class Arm2BodyContact : MonoBehaviour
    {
        public Transform heroRoot;
        public const int Max = 12;
        public readonly Vector3[] normals = new Vector3[Max];
        public readonly Vector3[] points = new Vector3[Max];
        /// <summary>Чьё это касание: тело физики (платформа) или null — неподвижный мир.</summary>
        public readonly Rigidbody[] bodies = new Rigidbody[Max];
        public int count;

        /// <summary>Блок прочитал — ждать касаний следующего шага.</summary>
        public void Clear() { count = 0; }

        void OnCollisionStay(Collision c) { Take(c); }
        void OnCollisionEnter(Collision c) { Take(c); }

        void Take(Collision c)
        {
            for (int i = 0; i < c.contactCount && count < Max; i++)
            {
                var cp = c.GetContact(i);
                if (!cp.otherCollider || cp.otherCollider.transform.IsChildOf(heroRoot)) continue;     // своя рука — не опора и не преграда
                normals[count] = cp.normal; points[count] = cp.point; bodies[count] = cp.otherCollider.attachedRigidbody; count++;
            }
        }
    }
}
