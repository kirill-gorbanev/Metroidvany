using UnityEngine;

namespace Fortress.Rig
{
    /// <summary>Слои физики рига. «Призраки» (невидимые аниматоры людей, CharacterController) сталкиваются только со
    /// статичным миром (пол, стены, помосты) — сквозь всё подвижное они проходят, а в подвижное врезаются настоящие тела.
    /// Тела ригов, табуреты, ящики, бочки, мячи — слой RigBody.
    /// Риг v3: RigProbe — капсулы костей видимого человека, только для запросов (контакты, мышь), ни с чем не сталкиваются;
    /// RigMover — капсула движения человека v3: мир и другие такие же капсулы, без предметов (предметы считает отклик).</summary>
    public static class RigLayers
    {
        public const int Ghost = 8, Body = 9, Probe = 10, Mover = 11, Trigger = 2;
        /// <summary>Лучи «что под ногами» и мышь стенда: всё, кроме триггеров, призраков и людей v3 (зонды, капсулы).</summary>
        public const int SolidMask = ~((1 << Trigger) | (1 << Ghost) | (1 << Probe) | (1 << Mover));
        /// <summary>Только статичный мир (без тел, призраков и людей) — лучи стоп не видят людей.</summary>
        public const int WorldMask = SolidMask & ~(1 << Body);
        /// <summary>Капсулы костей людей v3.</summary>
        public const int ProbeMask = 1 << Probe;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Physics.IgnoreLayerCollision(Ghost, Body, true);
            Physics.IgnoreLayerCollision(Ghost, Ghost, true);
            Physics.IgnoreLayerCollision(Ghost, Mover, true);
            // зонды — только для запросов
            for (int l = 0; l < 32; l++) Physics.IgnoreLayerCollision(Probe, l, true);
            // капсула движения: мир и другие капсулы; предметы и тела v2 — через отклик (эффективные массы), не таран
            Physics.IgnoreLayerCollision(Mover, Body, true);
        }

        /// <summary>Поставить объект и всех детей с коллайдерами на слой тел.</summary>
        public static void SetBody(GameObject g)
        {
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = Body;
        }
    }
}
