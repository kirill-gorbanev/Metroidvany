using UnityEngine;

namespace Fortress.Hero.Arm2
{
    /// <summary>Касание кисти — из самой физики (события столкновений предплечья), без запросов. Висит на теле предплечья.
    /// Урок 4: проверка хвата — тем же коллайдером кисти, который двигается, а не отдельной геометрией.</summary>
    public class Arm2HandContact : MonoBehaviour
    {
        public Collider hand;
        public Transform heroRoot;
        /// <summary>Касание кисти за последний шаг физики: есть ли, точка, нормаль (от поверхности к кисти), чей коллайдер.</summary>
        public bool has; public Vector3 point, normal; public Collider other; public float separation;
        /// <summary>Отдельно — касание кисти с ПРЕДМЕТОМ (слой Item), с любой стороны: за предмет берутся в любой точке.</summary>
        public bool hasItem; public Vector3 itemPoint; public Collider itemOther; public float itemSeparation;
        int itemLayer = -1;
        void Awake() { itemLayer = LayerMask.NameToLayer("Item"); }

        /// <summary>Блок хвата прочитал — ждать касаний следующего шага.</summary>
        public void Clear() { has = false; other = null; hasItem = false; itemOther = null; }

        void OnCollisionStay(Collision c) { Take(c); }
        void OnCollisionEnter(Collision c) { Take(c); }

        void Take(Collision c)
        {
            for (int i = 0; i < c.contactCount; i++)
            {
                var cp = c.GetContact(i);
                if (cp.thisCollider != hand || !cp.otherCollider || cp.otherCollider.transform.IsChildOf(heroRoot)) continue;
                if (itemLayer >= 0 && cp.otherCollider.gameObject.layer == itemLayer && (!hasItem || cp.separation < itemSeparation))
                { hasItem = true; itemPoint = cp.point; itemOther = cp.otherCollider; itemSeparation = cp.separation; }
                // из нескольких касаний — то, на которое кисть опирается (нормаль выше)
                if (!has || cp.normal.y > normal.y) { has = true; point = cp.point; normal = cp.normal; other = cp.otherCollider; separation = cp.separation; }
            }
        }
    }
}
