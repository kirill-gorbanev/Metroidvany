using UnityEngine;

namespace Fortress.Walker
{
    /// <summary>Объём стремянки. Залезть: встать лицом к стремянке и жать W, или Пробел, или упасть в люк над ней.
    /// Пока держишься — W/Пробел вверх, S вниз, C — отпустить. Наверху сам переходишь на площадку выхода.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class LadderZone : MonoBehaviour
    {
        public float climbSpeed = 2.4f;
        [Tooltip("Мировая высота пола выхода наверху (у цепочки стремянок — верх всей цепочки)")]
        public float top;
        [Tooltip("Мировая высота низа стремянки")]
        public float bottom;
        [Tooltip("Точка на линии стремянки (у её основания)")]
        public Vector3 line;
        [Tooltip("Горизонталь от стены к игроку; выход наверху — в обратную сторону (через стену или люк)")]
        public Vector3 face = Vector3.forward;
    }
}
