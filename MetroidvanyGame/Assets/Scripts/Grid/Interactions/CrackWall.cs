using UnityEngine;

namespace Fortress.Grid
{
    /// <summary>Трещина в кладке (ребро C): с киркой — E пробивает проход навсегда; без кирки — подсказка.</summary>
    public class CrackWall : Interactable
    {
        bool broken;
        void Awake() { reach = 1.9f; }

        public override string Prompt => broken ? "" : "Трещина в кладке";
        public override string PromptFor(Transform who) =>
            broken ? "" : (PlayerTools.Pickaxe ? "E — пробить трещину киркой" : "Трещина в кладке — нужна кирка");
        public override bool CanInteract(Transform who) => !broken;

        public override void Interact(Transform who)
        {
            if (broken || !PlayerTools.Pickaxe) return;
            broken = true;
            Break(gameObject);
        }

        /// <summary>Все куски кладки этого ребра (их несколько: двойная стена) пропадают вместе.</summary>
        static void Break(GameObject g)
        {
            var me = g.GetComponent<FortElement>();
            foreach (var c in FindObjectsByType<CrackWall>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var fe = c.GetComponent<FortElement>();
                if (c.gameObject != g && (!fe || !me || fe.cell != me.cell)) continue;
                c.broken = true;
                foreach (var col in c.GetComponentsInChildren<Collider>()) col.enabled = false;
                foreach (var r in c.GetComponentsInChildren<Renderer>()) r.enabled = false;
            }
        }
    }

    /// <summary>Инструменты игрока в этом забеге (демо: одна кирка).</summary>
    public static class PlayerTools
    {
        public static bool Pickaxe;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Pickaxe = false; }
    }
}
