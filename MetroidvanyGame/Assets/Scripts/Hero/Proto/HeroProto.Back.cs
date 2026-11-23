using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>СПИНА — СЛОТ (LOCK). Отдельный блок поверх перетаскивания (HeroProto.Drag). Простая версия (автор 2026-11-22:
    /// «на спине как будто слот инвентаря — положил буквально transform и зафиксировал»; жест — «провести за спину»).
    ///
    /// · ПОЛОЖИТЬ: тащишь предмет, курсор за спиной (позади центра тела по лицу) — предмет подсвечен «на спину»; отпустил ЛКМ —
    ///   предмет встаёт на спину и закреплён: каждый шаг его место и поворот берутся от тела (как будто прикручен).
    /// · СНЯТЬ: курсор на предмете на спине — кисть тянется за ним; ЛКМ — предмет в руке, перед собой (дальше тащишь как обычно).
    /// · Слот один: занят — второй предмет на спину не встаёт (подсветки нет, отпустил — лёг на пол).
    /// · Предмет на спине закреплён (ProtoItem.Lock: физика выключена); падение героя его не роняет. Снят — снова физика.</summary>
    public partial class HeroProto
    {
        [Header("Спина (слот)")]
        [Tooltip("Слот на спине включён: тащишь предмет, курсор за спиной, отпустил — предмет на теле. Выключено автором 2026-11-22: «мешает» (срабатывал случайно).")]
        public bool backSlot = false;
        [Tooltip("Предмет на спине: насколько сдвинут от центра тела назад, к спине (доля половины тела).")]
        [Range(0f, 1f)] public float backSlotBack = 0.35f;
        [Tooltip("Снятый со спины предмет встаёт перед собой на таком расстоянии от плеча (м).")]
        public float backTakeDist = 0.4f;

        ProtoItem backItem;                 // что на спине (LOCK)
        bool backArmed;                     // тащишь предмет, курсор за спиной — отпустишь, и он на спине

        public ProtoItem BackItem => backItem;

        /// <summary>Пока тащишь: курсор за спиной и слот свободен — «на спину» взведено.</summary>
        void BackArmStep(Vector2 px)
        {
            backArmed = false;
            if (!backSlot || !dragItem || backItem || !mirrorBehind) return;
            if (FloorPoint(px, out var p) && Vector3.Dot(Flat(p) - Body, Face) < 0f) backArmed = true;
            if (backArmed) gestureOwner = "на спину (отпусти ЛКМ)";
        }

        /// <summary>Отпустил ЛКМ: взведено — предмет на спину, true. Иначе false (обычное «положил»).</summary>
        bool TryLockToBack()
        {
            if (!backArmed || !dragItem) return false;
            var it = dragItem;
            it.EndCarry();
            dragItem = null; backArmed = false;
            backItem = it; it.Lock(true);                              // закреплён: физика выключена, не препятствие, не ловится как лежащий
            it.SetLook(false, false);
            lastRelease = "положил на спину";
            BackSlotStep();
            return true;
        }

        /// <summary>LOCK: предмет на спине — каждый шаг на месте слота: низ на верху тела, у спины, поворот как у тела.</summary>
        void BackSlotStep()
        {
            if (!backItem) return;
            if (!backItem.isActiveAndEnabled) { backItem = null; return; }   // зона выключена
            var t = backItem.transform;
            t.rotation = Quaternion.Euler(0f, bodyCube.eulerAngles.y, 0f);
            float top = bodyCube.position.y + Mathf.Abs(bodyCube.lossyScale.y) * 0.5f;
            Vector3 slot = Body - Face * (BodyHalf.y * backSlotBack);
            Bounds b = backItem.WorldBounds;
            t.position += new Vector3(slot.x - b.center.x, top - b.min.y, slot.z - b.center.z);
        }

        /// <summary>Курсор на предмете на спине — его можно снять (кисть тянется перед собой).</summary>
        bool FindBackItem(Ray r, out Vector3 hand)
        {
            hand = default;
            if (!backItem || !backItem.WorldBounds.IntersectRay(r)) return false;
            hand = HandAtLedge(Reach(Shoulder + Face * backTakeDist));
            return true;
        }

        /// <summary>Снять со спины: предмет — в руку, перед собой, на поверхность под кистью.</summary>
        void UnlockFromBack(Vector3 h)
        {
            var it = backItem; backItem = null;
            float g = HandLevel(h); if (float.IsInfinity(g)) g = groundY;
            it.transform.rotation = Quaternion.identity;
            Bounds b = it.WorldBounds;
            it.transform.position += new Vector3(h.x - b.center.x, g - b.min.y, h.z - b.center.z);
            it.Lock(false);                                            // снова физика
            lastRelease = "снял со спины";
        }

        void BackReset()
        {
            if (backItem) { backItem.Lock(false); backItem.SetLook(false, false); }
            backItem = null; backArmed = false;
        }
    }
}
