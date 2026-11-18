using UnityEngine;

namespace Fortress.Walker
{
    /// <summary>Объём воды (ров): плавание у поверхности, выход — по стремянке пристани.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class WaterZone : MonoBehaviour
    {
        public float surfaceY;
        public float swimSpeed = 2.2f;
    }
}
