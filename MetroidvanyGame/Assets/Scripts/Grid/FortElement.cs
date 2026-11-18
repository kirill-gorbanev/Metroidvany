using UnityEngine;

namespace Fortress.Grid
{
    /// <summary>Вид элемента набора: от него зависят коллизия и то, какую интерактивность к нему удобно добавить.</summary>
    public enum ElementKind { Wall, Opening, Floor, Stair, Roof, Trim, Prop, WallProp, Light, Nature, Water, Ground, Ladder }

    /// <summary>Метка на каждом блоке/предмете локации. Сюда автор вешает интерактивность (компоненты-наследники
    /// <see cref="Interactable"/> на префабе — получат все экземпляры; на экземпляре — только он).</summary>
    public class FortElement : MonoBehaviour
    {
        public ElementKind kind;
        [Tooltip("Имя модели в наборе (Quaternius и др.)")] public string model;
        [Tooltip("Клетка сетки и уровень, куда поставил сборщик (для отладки и правок)")] public Vector3Int cell;
        [Tooltip("Комната/зона из карты")] public string room;
    }
}
