using UnityEngine;

namespace Fortress.Walker
{
    /// <summary>Выход из стен (exit в site.txt): игрок, прошедший здесь, запоминает имя выхода.
    /// Финиш (finish border): внешний берег за рвом — побег засчитан с меткой последнего выхода.</summary>
    public class EscapeZone : MonoBehaviour
    {
        public string exitName;
        public bool finish;

        void OnTriggerEnter(Collider other)
        {
            if (!other.GetComponent<PlayerWalker>()) return;
            if (finish) EscapeLog.Finish();
            else EscapeLog.lastExit = exitName;
        }
    }
}
