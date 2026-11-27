using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Fortress.Hero.Arm2
{
    /// <summary>БЛОК «РУКА НЕ ДВИГАЕТ ПРЕДМЕТЫ». Решение автора 2026-11-25 (вопрос 1, вариант А): «предметы для руки твёрдые,
    /// но несдвигаемые — рука упирается и скользит по ним, как по стене». И прежнее: «чтобы я кистью не толкался».
    /// Как: слои HeroArm и Item сталкиваются (рука не проходит сквозь предмет — правило 1), но в касании «рука — ЛЕЖАЩИЙ предмет»
    /// предмет считается бесконечно тяжёлым (правка касаний решателя: обратная масса предмета = 0). Рука останавливается и
    /// скользит, предмет не получает от неё ни толчка. Это часть самой физики, а не проверка поверх неё.
    /// Только лежащий: предмет, который сам движется (падает, катится, едет), касается руки как обычное тело — иначе он
    /// вдавил бы руку в пол или зажал её (рука 0.3 кг и так уступит). Предмет в руке (срез предметов) рука двигает.</summary>
    public class Arm2ItemGuard : MonoBehaviour
    {
        public Arm2Hero hero;
        public Arm2Arm arm;
        [Tooltip("Предмет «лежит», пока он медленнее этого (м/с и рад/с × 0.1).")]
        public float quietSpeed = 0.05f;
        [Tooltip("Предмет «падает», если его вертикальная скорость больше этого (м/с): тогда он касается руки как обычное тело.")]
        public float fallSpeed = 0.5f;

        // Читаются из потока физики во время шага; пишутся только из главного потока ДО шага (FixedStep) — не одновременно.
        static readonly HashSet<EntityId> quietItems = new HashSet<EntityId>();
        static readonly HashSet<EntityId> armBodies = new HashSet<EntityId>();
        static readonly HashSet<EntityId> heroBodies = new HashSet<EntityId>();
        // ПРЕДМЕТ В РУКЕ НЕ ТОЛКАЕТ ЧУЖИЕ ПРЕДМЕТЫ (срез 9, стопки, 2026-11-27; замер: кубик в руке сдвинул соседний на 16 см и
        // перевернул его). То же правило, что для руки: в касании «предмет в руке — лежащий предмет» лежащий бесконечно тяжёл,
        // предмет в руке упирается в него, как в стену, — и его можно поставить сверху. Исключение — «седоки»: предметы, которые
        // лежат НА предмете в руке; они касаются его как обычные тела и едут на нём за счёт трения (стопку можно тащить).
        static readonly HashSet<EntityId> heldBodies = new HashSet<EntityId>();
        static readonly HashSet<EntityId> riderItems = new HashSet<EntityId>();
        static int owners;
        readonly List<Rigidbody> items = new List<Rigidbody>();
        readonly HashSet<Rigidbody> held = new HashSet<Rigidbody>();
        int armLayer = -1, itemLayer = -1; bool savedArmItem, savedArmArm, hooked;

        void OnEnable()
        {
            armLayer = LayerMask.NameToLayer("HeroArm"); itemLayer = LayerMask.NameToLayer("Item");
            if (armLayer >= 0)
            {
                savedArmArm = Physics.GetIgnoreLayerCollision(armLayer, armLayer);
                Physics.IgnoreLayerCollision(armLayer, armLayer, true);                 // звенья руки между собой не сталкиваются (соседи по суставу)
                if (itemLayer >= 0)
                {
                    savedArmItem = Physics.GetIgnoreLayerCollision(armLayer, itemLayer);
                    Physics.IgnoreLayerCollision(armLayer, itemLayer, false);           // старый герой эту пару выключал — рука шла сквозь предметы
                }
            }
            foreach (var c in arm.colliders) if (c) { c.hasModifiableContacts = true; if (c.attachedRigidbody) armBodies.Add(c.attachedRigidbody.GetEntityId()); }
            // ТЕЛО СКОЛЬЗИТ ВДОЛЬ СТЕН (правило 2: ход в поверхность гасится, остаток идёт вдоль неё). Трение тела нужно на опоре
            // (пороги склона), а о вертикальные грани оно тело клинило: запись автора 00:33 — у угла уступа тело упиралось и не шло
            // («не хватает автообъезда»). В касаниях тела с гранью круче ~63° трение обнуляется — тело съезжает вдоль неё и обходит угол.
            foreach (var c in hero.body.GetComponents<Collider>()) c.hasModifiableContacts = true;
            heroBodies.Add(hero.body.GetComponent<Rigidbody>().GetEntityId());
            Rescan();
            if (owners++ == 0) Physics.ContactModifyEvent += Modify;
            hooked = true;
        }

        void OnDisable()
        {
            if (hooked) { if (--owners == 0) { Physics.ContactModifyEvent -= Modify; quietItems.Clear(); armBodies.Clear(); heroBodies.Clear(); heldBodies.Clear(); riderItems.Clear(); } hooked = false; }
            if (armLayer >= 0)
            {
                Physics.IgnoreLayerCollision(armLayer, armLayer, savedArmArm);
                if (itemLayer >= 0) Physics.IgnoreLayerCollision(armLayer, itemLayer, savedArmItem);
            }
        }

        /// <summary>Собрать предметы сцены (тела на слое Item). Звать после появления новых предметов.</summary>
        public void Rescan()
        {
            items.Clear();
            if (itemLayer < 0) return;
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude))
                if (rb.gameObject.layer == itemLayer) items.Add(rb);
        }

        /// <summary>Предмет взят в руку / отпущен: взятый рука двигает.</summary>
        public void SetHeld(Rigidbody item, bool on)
        {
            if (!item) return;
            if (on) { held.Add(item); heldBodies.Add(item.GetEntityId()); } else { held.Remove(item); heldBodies.Remove(item.GetEntityId()); }
            foreach (var c in item.GetComponentsInChildren<Collider>()) c.hasModifiableContacts = on;
            if (on) MarkRiders(item, 0);                                        // седоки известны сразу, не со следующего шага
        }

        /// <summary>Шаг перед физикой (главный поток): кто из предметов сейчас лежит.</summary>
        public void FixedStep(float dt)
        {
            quietItems.Clear(); riderItems.Clear(); riderBodies.Clear();
            // седоки: предмет, чей низ не ниже верха предмета в руке (с допуском) и который стоит над ним; и седоки седоков
            foreach (var h in held) if (h) MarkRiders(h, 0);
            Riders = riderItems.Count;
            float v2 = quietSpeed * quietSpeed, w2 = v2 * 100f;
            for (int i = 0; i < items.Count; i++)
            {
                var rb = items[i];
                if (!rb || held.Contains(rb) || !rb.gameObject.activeInHierarchy) continue;
                // Автор 2026-11-26: «отпустил ЛКМ и продолжил толкать рукой объект — надо блокать: нет ЛКМ — предмет рукой двигать нельзя».
                // Раньше несдвигаемым был только ЛЕЖАЩИЙ предмет: отпущенный ещё ехал — и рука его толкала дальше. Теперь несдвигаем
                // любой предмет не в руке, кроме падающего (он иначе вдавил бы руку в пол).
                if (rb.IsSleeping() || Mathf.Abs(rb.linearVelocity.y) < fallSpeed) quietItems.Add(rb.GetEntityId());
            }
        }

        readonly HashSet<Rigidbody> riderBodies = new HashSet<Rigidbody>();
        /// <summary>Этот предмет едет на предмете в руке (главный поток): для примерки места он не опора и не преграда.</summary>
        public bool IsRider(Rigidbody rb) => rb && riderBodies.Contains(rb);
        /// <summary>Сколько предметов сейчас едет на предмете в руке (для замеров).</summary>
        public static int Riders;
        void MarkRiders(Rigidbody under, int depth)
        {
            if (depth > 6) return;
            var uc = under.GetComponentInChildren<Collider>(); if (!uc) return; Bounds ub = uc.bounds;
            for (int i = 0; i < items.Count; i++)
            {
                var rb = items[i];
                if (!rb || rb == under || held.Contains(rb) || !rb.gameObject.activeInHierarchy || riderItems.Contains(rb.GetEntityId())) continue;
                var rc = rb.GetComponentInChildren<Collider>(); if (!rc) continue; Bounds b = rc.bounds;
                if (b.min.y < ub.max.y - 0.02f || b.min.y > ub.max.y + 0.03f) continue;
                if (b.max.x < ub.min.x || b.min.x > ub.max.x || b.max.z < ub.min.z || b.min.z > ub.max.z) continue;
                riderItems.Add(rb.GetEntityId()); riderBodies.Add(rb);
                MarkRiders(rb, depth + 1);
            }
        }

        static void Modify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            for (int i = 0; i < pairs.Length; i++)
            {
                var p = pairs[i];
                if (heroBodies.Contains(p.bodyEntityId) || heroBodies.Contains(p.otherBodyEntityId))
                {
                    for (int c = 0; c < p.contactCount; c++)
                        if (Mathf.Abs(p.GetNormal(c).y) < 0.45f) { p.SetStaticFriction(c, 0f); p.SetDynamicFriction(c, 0f); }
                    pairs[i] = p;
                    continue;
                }
                bool thisItem = quietItems.Contains(p.bodyEntityId) && (armBodies.Contains(p.otherBodyEntityId) || (heldBodies.Contains(p.otherBodyEntityId) && !riderItems.Contains(p.bodyEntityId)));
                bool otherItem = !thisItem && quietItems.Contains(p.otherBodyEntityId) && (armBodies.Contains(p.bodyEntityId) || (heldBodies.Contains(p.bodyEntityId) && !riderItems.Contains(p.otherBodyEntityId)));
                if (!thisItem && !otherItem) continue;
                var m = p.massProperties;
                if (thisItem) { m.inverseMassScale = 0f; m.inverseInertiaScale = 0f; }
                else { m.otherInverseMassScale = 0f; m.otherInverseInertiaScale = 0f; }
                p.massProperties = m;
                pairs[i] = p;
            }
        }
    }
}
