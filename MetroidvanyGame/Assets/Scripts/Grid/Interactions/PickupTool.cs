using UnityEngine;

namespace Fortress.Grid
{
    /// <summary>Предмет-инструмент: E — взять (кирка со склада).</summary>
    public class PickupTool : Interactable
    {
        public string tool = "pickaxe";
        bool taken;
        void Awake() { reach = 1.8f; }
        public override string Prompt => taken ? "" : "E — взять кирку";
        public override bool CanInteract(Transform who) => !taken;
        public override void Interact(Transform who)
        {
            if (taken) return;
            taken = true;
            if (tool == "pickaxe") PlayerTools.Pickaxe = true;
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }
    }
}
