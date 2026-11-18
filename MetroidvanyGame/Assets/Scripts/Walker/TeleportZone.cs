using UnityEngine;

namespace Fortress.Walker
{
    /// <summary>Переход по E: верх трубы -> очаг на кухне (труба как шахта).</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class TeleportZone : MonoBehaviour
    {
        public Vector3 target;
        public string prompt = "E — спрыгнуть в трубу";
    }
}
