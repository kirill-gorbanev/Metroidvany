using UnityEngine;

namespace Fortress.Grid
{
    /// <summary>База для интерактивности элемента. Персонаж ищет ближайший Interactable в радиусе
    /// и по E вызывает Interact. Наследуй, чтобы дать элементу поведение.</summary>
    public abstract class Interactable : MonoBehaviour
    {
        [Tooltip("Дистанция, с которой работает E")] public float reach = 1.8f;
        public abstract string Prompt { get; }
        /// <summary>Подсказка с учётом того, кто смотрит (сторона засова, есть ли инструмент).</summary>
        public virtual string PromptFor(Transform who) => Prompt;
        public virtual bool CanInteract(Transform who) => enabled;
        public abstract void Interact(Transform who);

        public Vector3 Anchor
        {
            get
            {
                var c = GetComponentInChildren<Collider>();
                return c ? c.bounds.center : transform.position;
            }
        }
    }
}
