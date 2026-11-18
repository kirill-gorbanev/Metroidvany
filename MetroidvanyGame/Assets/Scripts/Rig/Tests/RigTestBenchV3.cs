using System.Collections.Generic;
using Fortress.Rig.Human;
using Fortress.Rig.V3;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace Fortress.Rig.Tests
{
    /// <summary>Стенд, страница «v3: отклик»: человек рига v3 — видна анимация, движение главное, физика даёт отклик поверх.</summary>
    public partial class RigTestBench
    {
        [Header("Риг v3")]
        [Tooltip("Люди v3 (игрок, ведёт капсула): крестьянин, следопыт, следопытка")] public GameObject[] humanV3Prefabs;
        [Tooltip("NPC v3 (ведут клипы Mixamo): крестьянин, следопыт, следопытка")] public GameObject[] humanNPCPrefabs;

        const int PageV3 = 3;
        HumanReaction v3Target;
        HumanMotor possessedV3;
        float v3Timer;
        int v3Idx;
        class Walker3 { public HumanReaction h; public Rigidbody crate; public Vector3 crateHome, a, b; public int leg; public float stuck, blockedT; }
        readonly List<Walker3> v3Walkers = new List<Walker3>();
        HumanPuppet cmpV2;
        static readonly float[] V3Pushes = { 15f, 30f, 50f, 75f, 100f };

        void ResetV3()
        {
            v3Target = null; v3Timer = 0f; v3Idx = 0;
            v3Walkers.Clear();
            cmpV2 = null;
        }

        void DefineV3()
        {
            tests.Add(new Test
            {
                page = PageV3, title = "Толчки в грудь: растущий импульс",
                watch = new[]
                {
                    "толчки по очереди: 15, 30, 50, 75, 100 Н·с с разных сторон; следующий — когда успокоился",
                    "слабый — качнулся на месте: корпус, голова и руки отзываются, колени пружинят",
                    "сильный — отшатывается и подшагивает к точке захвата (1–3 шага); поза сразу возвращается к анимации",
                },
                setup = () =>
                {
                    var h = V3(0, Vector3.zero, 180f);
                    if (!h) return;
                    h.Motor.idle = true;
                    v3Target = h; v3Timer = 0f; v3Idx = 0;
                    Follow(h.transform, 4.2f);
                },
                tick = dt => PushV3Tick(dt),
            });
            tests.Add(new Test
            {
                page = PageV3, title = "Мячи и бочки на ходу",
                watch = new[]
                {
                    "идёт по квадрату; в него летят с упреждением мячи (1.6 кг, 9–14 м/с) и бочки (14 кг, 5–7 м/с)",
                    "мяч — отдёрнулась часть тела (рука, голова, корпус), шаг не сбился; бочка — отшатнулся и подшагнул",
                    "предметы отлетают с честным импульсом: мяч отскакивает, бочка теряет скорость и падает",
                },
                setup = () =>
                {
                    var h = V3(1, new Vector3(-2.2f, 0f, -2.2f), 90f);
                    if (!h) return;
                    h.Motor.points = Square(2.2f);
                    h.Motor.pause = 0.3f;
                    v3Target = h; v3Timer = 0f; v3Idx = 0;
                    Follow(h.transform, 5.5f);
                },
                tick = dt => VolleyV3Tick(dt),
            });
            tests.Add(new Test
            {
                page = PageV3, title = "Толпа: задевают друг друга",
                watch = new[]
                {
                    "шестеро ходят крест-накрест через середину",
                    "задев плечом или рукой — оба отзываются: разворот плеч, рука отлетает, лёгкое качание",
                    "капсулы не проходят сквозь друг друга; толчок посильнее — подшагнул",
                },
                setup = () =>
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f + 0.12f * i;
                        Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.6f;
                        var h = V3(i % 3, p, 0f);
                        if (!h) continue;
                        h.Motor.points = new[] { -p + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f)), p };
                        h.Motor.walkSpeed = Random.Range(1.0f, 1.45f);
                        h.Motor.pause = Random.Range(0.1f, 0.8f);
                    }
                    if (cam) { cam.follow = null; cam.focus = new Vector3(0f, 0.6f, 0f); cam.distance = 10f; cam.pitch = 42f; cam.yaw = 20f; }
                },
            });
            tests.Add(new Test
            {
                page = PageV3, title = "Ящики: лёгкий едет, тяжёлый — стоп",
                watch = new[]
                {
                    "двое идут прямо на ящики: ближний — лёгкий (15 кг), дальний — окованный (150 кг)",
                    "лёгкий едет перед человеком, шаг не замедляется",
                    "в тяжёлый — упёрся и встал, отшатнулся от удара; ящик почти не сдвинулся. Потом обратно",
                },
                setup = () =>
                {
                    for (int i = 0; i < 2; i++)
                    {
                        float z = i == 0 ? 1.4f : -1.4f;
                        var pf = i == 0 ? cratePrefab : (metalCratePrefab ? metalCratePrefab : cratePrefab);
                        Rigidbody c = pf ? Prop(pf, new Vector3(-0.8f, 0.01f, z), 0f, i == 0 ? 15f : 150f) : null;
                        if (c) c.name = i == 0 ? "Ящик 15 кг" : "Ящик 150 кг";
                        var h = V3(i, new Vector3(-4f, 0f, z), 90f);
                        if (!h) continue;
                        h.Motor.idle = true;
                        v3Walkers.Add(new Walker3 { h = h, crate = c, crateHome = c ? c.position : Vector3.zero, a = new Vector3(-4f, 0f, z), b = new Vector3(4f, 0f, z), leg = 0, stuck = -1.0f - i * 0.6f });
                    }
                    if (cam) { cam.follow = null; cam.focus = new Vector3(0f, 0.6f, 0f); cam.distance = 10f; cam.pitch = 30f; cam.yaw = 0f; }
                },
                tick = dt => CratesV3Tick(dt),
            });
            tests.Add(new Test
            {
                page = PageV3, title = "Лестница: скорость ровная",
                watch = new[]
                {
                    "ходит через помост по лестницам крепости (ступени 20 см) — скорость капсулы не меняется",
                    "каждая стопа встаёт на свою ступень (IK), таз идёт плавно",
                    "подпись над головой — горизонтальная скорость; ткни мышью — отзовётся, не падая",
                },
                setup = () =>
                {
                    StairsBlock(Vector3.zero);
                    var h = V3(0, new Vector3(-5.5f, 0f, 0f), 90f);
                    if (!h) return;
                    h.Motor.points = new[] { new Vector3(5.5f, 0f, 0f), new Vector3(-5.5f, 0f, 0f) };
                    h.Motor.pause = 0.6f;
                    Follow(h.transform, 5f);
                    if (cam) { cam.yaw = 0f; cam.pitch = 12f; }
                },
            });
            tests.Add(new Test
            {
                page = PageV3, title = "Ты — человек v3 (WASD)",
                watch = new[]
                {
                    "WASD — идти, Shift — бежать: скорость и поворот сразу; корпус догоняет поворот и наклоняется в разгон",
                    "ЛКМ — ткнуть в любую кость (себя или других), B — мяч туда, куда смотрит мышь, +/− — сила тычка",
                    "лёгкий ящик едет перед тобой, тяжёлый не пускает; бочку и мяч можно пнуть; лестница справа",
                },
                setup = () =>
                {
                    StairsBlock(new Vector3(5f, 0f, 0f));
                    if (cratePrefab)
                    {
                        Prop(cratePrefab, new Vector3(-2.5f, 0.01f, 1.5f), 10f, 15f).name = "Ящик 15 кг";
                        Prop(cratePrefab, new Vector3(-2.5f, 0.01f, -1.8f), -15f, 12f).name = "Ящик 12 кг";
                    }
                    if (metalCratePrefab) Prop(metalCratePrefab, new Vector3(0f, 0.01f, -3.5f), 0f, 150f).name = "Ящик 150 кг";
                    if (barrelPrefab) for (int i = 0; i < 2; i++) Prop(barrelPrefab, new Vector3(-4.5f + 1.2f * i, 0.01f, 3.2f), 0f, 14f).name = "Бочка";
                    for (int i = 0; i < 2; i++) Stool(new Vector3(-1f + 2f * i, 0f, 3.5f), Quaternion.identity).GetComponent<RigWander>().radius = 3f;
                    for (int i = 0; i < 2; i++)
                    {
                        var n = V3(1 + i, new Vector3(-3f + 6f * i, 0f, -1f), 0f);
                        if (!n) continue;
                        n.Motor.radius = 3.5f; n.Motor.center = new Vector3(-1f, 0f, 0f); n.Motor.pause = 1.5f;
                    }
                    var me = V3(0, Vector3.zero, 0f, true);
                    if (me) PossessV3(me.Motor);
                },
            });
            tests.Add(new Test
            {
                page = PageV3, title = "Сравнение: v2 (слева) и v3 (справа)",
                watch = new[]
                {
                    "оба ходят туда-обратно; каждые 3.5 с обоих толкают одинаково: 0.4–1.2 м/с всему телу",
                    "v2 — видно физическое тело на пружинах (запаздывает, «деревянный»); v3 — видна анимация, отклик поверх",
                    "сравни повороты, остановки и как оба ловят равновесие",
                },
                setup = () =>
                {
                    cmpV2 = Human(0, new Vector3(-3.5f, 0f, -1.2f), 90f);
                    if (cmpV2) cmpV2.driver.points = new[] { new Vector3(3.5f, 0f, -1.2f), new Vector3(-3.5f, 0f, -1.2f) };
                    var h = V3(0, new Vector3(-3.5f, 0f, 1.2f), 90f);
                    if (h) { h.Motor.points = new[] { new Vector3(3.5f, 0f, 1.2f), new Vector3(-3.5f, 0f, 1.2f) }; h.Motor.walkSpeed = 1.0f; }
                    v3Target = h; v3Timer = 0f; v3Idx = 0;
                    if (cam) { cam.follow = null; cam.focus = new Vector3(0f, 0.8f, 0f); cam.distance = 9f; cam.pitch = 22f; cam.yaw = 0f; }
                },
                tick = dt => CompareTick(dt),
            });
        }

        /// <summary>Человек v3: NPC (ходят клипы) или игрок (ведёт капсула, WASD).</summary>
        HumanReaction V3(int idx, Vector3 pos, float yaw, bool player = false)
        {
            var list = !player && humanNPCPrefabs != null && humanNPCPrefabs.Length > 0 ? humanNPCPrefabs : humanV3Prefabs;
            var pf = list != null && list.Length > 0 ? list[Mathf.Clamp(idx, 0, list.Length - 1)] : null;
            if (!pf) { Note("нет людей v3 — меню Fortress/Rig/Build Humans v3"); return null; }
            var go = Instantiate(pf, pos, Quaternion.Euler(0f, yaw, 0f), arena);
            go.name = pf.name.Replace("HumanV3_", "").Replace("HumanNPC_", "").Replace("_", " ");
            return go.GetComponent<HumanReaction>();
        }

        static bool Calm(HumanReaction h) => h.Motor.Knock.magnitude < 0.1f && !h.Animate.Stepping && h.SwayDeg < 1.2f
                                             && !(h.Npc && (h.Npc.Busy || h.Npc.State == HumanNPC.Mode.Turn));

        void PushV3Tick(float dt)
        {
            if (!v3Target) return;
            if (!Calm(v3Target)) { v3Timer = Mathf.Min(v3Timer, 1.6f); return; }
            v3Timer += dt;
            if (v3Timer < 2.6f) return;
            v3Timer = 0f;
            float J = V3Pushes[v3Idx % V3Pushes.Length];
            // сзади, сбоку, спереди, сбоку, по диагонали — со стороны тела
            Vector3 f = v3Target.Animate.BodyForward, r = Vector3.Cross(Vector3.up, f);
            Vector3[] dirs = { f, r, -f, -r, (f - r).normalized };
            Vector3 dir = dirs[v3Idx % dirs.Length];
            v3Idx++;
            v3Target.PushChest(dir * J);
            Note($"толчок {J:F0} Н·с");
        }

        void VolleyV3Tick(float dt)
        {
            if (!v3Target) return;
            v3Timer += dt;
            if (v3Timer < 1.9f) return;
            v3Timer = 0f;
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 v = v3Target.Motor.Velocity;
            if (v3Idx++ % 2 == 0)
            {
                float sp = Random.Range(9f, 14f);
                // мяч в грудь, голову или руку — на упреждение: куда дойдёт, пока летит
                int[] parts = { 1, 1, 2, 3, 5, 0 };
                var t = v3Target.bones[parts[Random.Range(0, parts.Length)]];
                Vector3 aim = t.position + (t == v3Target.Head ? Vector3.up * 0.08f : Vector3.zero);
                Vector3 to = aim + v * (4f / sp);
                Throw(to + dir * 4f + Vector3.up * 0.2f, to, sp);
                Note($"мяч {sp:F0} м/с → {t.name}");
            }
            else if (barrelPrefab)
            {
                float sp = Random.Range(5f, 7f);
                Vector3 to = v3Target.bones[0].position + Vector3.up * 0.1f + v * (4f / sp);
                var rb = Prop(barrelPrefab, to - dir * 4f + Vector3.up * 0.1f, 0f, 14f);
                rb.transform.rotation = Quaternion.LookRotation(Vector3.up, dir);
                rb.linearVelocity = dir * sp + Vector3.up * (0.5f * -Physics.gravity.y * (4f / sp));
                Destroy(rb.gameObject, 8f);
                Note($"бочка {sp:F1} м/с");
            }
        }

        void CratesV3Tick(float dt)
        {
            foreach (var w in v3Walkers)
            {
                if (!w.h) continue;
                var m = w.h.Motor;
                w.stuck += dt;
                if (w.stuck < 0f) continue;
                if (w.leg == 0)
                {
                    m.goTo = w.b;
                    // упёрся: хочет идти, а стоит — постоять 2 с и повернуть назад
                    bool blocked = w.stuck > 1.0f && !m.Recovering &&
                                   (m.Velocity.magnitude < 0.3f || (w.h.Npc && w.h.Npc.State == HumanNPC.Mode.Blocked));
                    w.blockedT = blocked ? w.blockedT + dt : Mathf.Max(0f, w.blockedT - 0.5f * dt);
                    if (m.Arrived || w.blockedT > 2.0f) { w.leg = 1; w.stuck = 0f; w.blockedT = 0f; }
                }
                else
                {
                    m.goTo = w.a;
                    if (m.Arrived && w.stuck > 0.5f)
                    {
                        w.leg = 0; w.stuck = -0.8f;
                        if (w.crate)
                        {
                            w.crate.position = w.crateHome; w.crate.rotation = Quaternion.identity;
                            w.crate.linearVelocity = Vector3.zero; w.crate.angularVelocity = Vector3.zero;
                            w.crate.transform.SetPositionAndRotation(w.crateHome, Quaternion.identity);
                        }
                    }
                }
            }
        }

        void CompareTick(float dt)
        {
            if (!v3Target) return;
            v3Timer += dt;
            if (v3Timer < 3.5f) return;
            if (cmpV2 && cmpV2.state != HumanPuppet.State.Active) return;
            v3Timer = 0f;
            float dv = new[] { 0.4f, 0.8f, 1.2f }[v3Idx % 3];
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v3Idx++;
            if (cmpV2)
            {
                var chest = cmpV2.Chest;
                foreach (var rb in cmpV2.body.Bodies) rb.AddForce(dir * dv * 0.75f, ForceMode.VelocityChange);
                chest.AddForce(dir * dv * 0.9f, ForceMode.VelocityChange);
                cmpV2.body.Hit(chest.gameObject, dv * cmpV2.body.Mass, dir);
            }
            v3Target.PushChest(dir * dv * 70f);
            Note($"толчок {dv:F1} м/с обоим ({dv * 70f:F0} Н·с)");
        }

        // ---------------------------------------------------------------- управление и мышь
        void PossessV3(HumanMotor m)
        {
            Release();
            possessedV3 = m;
            if (m) { m.possessed = true; Follow(m.transform, 4f); Note("ведёшь: " + m.name); }
        }

        void ReleaseV3()
        {
            if (possessedV3) { possessedV3.possessed = false; possessedV3.input = Vector3.zero; }
            possessedV3 = null;
        }

        void InputV3()
        {
            if (!possessedV3) return;
            var k = Keyboard.current;
            Vector2 mv = Vector2.zero;
            if (k != null)
            {
                mv.x = (k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0);
                mv.y = (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0);
            }
            Transform c = cam ? cam.transform : Camera.main.transform;
            Vector3 f = Vector3.ProjectOnPlane(c.forward, Vector3.up).normalized, r = Vector3.Cross(Vector3.up, f);
            float sp = (k != null && k.leftShiftKey.isPressed) ? possessedV3.runSpeed : possessedV3.walkSpeed;
            possessedV3.input = Vector3.ClampMagnitude(f * mv.y + r * mv.x, 1f) * sp;
        }

        /// <summary>Тычок по зонду человека v3: точка кости получает изменение скорости «сила тычка» (м/с).</summary>
        bool PokeV3(Ray ray)
        {
            Physics.SyncTransforms();
            if (!Physics.Raycast(ray, out var hit, 100f, RigLayers.SolidMask | RigLayers.ProbeMask, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider.gameObject.layer != RigLayers.Probe) return false;
            var r = hit.collider.GetComponentInParent<HumanReaction>();
            if (!r) return false;
            int part = r.PartOf(hit.collider);
            float J = r.Poke(hit.collider, hit.point, ray.direction, poke);
            Note($"тычок {poke:F1} м/с → {r.name}: {r.PartName(part)} ({J:F0} Н·с)");
            return true;
        }

        void LabelsV3(Camera c)
        {
            foreach (var r in arena.GetComponentsInChildren<HumanReaction>())
            {
                Vector3 sp = c.WorldToScreenPoint(r.Head.position + Vector3.up * 0.35f);
                if (sp.z < 0f) continue;
                var m = r.Motor;
                Vector3 v = m.Velocity; v.y = 0f;
                string st = r.Npc ? (r.SwayDeg > 0.8f ? $"{r.Npc.Say} · качается {r.SwayDeg:F0}°" : v.magnitude > 0.1f ? $"{r.Npc.Say} {v.magnitude:F2} м/с" : r.Npc.Say) :
                            r.Animate.Stepping ? $"шагает · {r.Animate.Steps}" :
                            r.SwayDeg > 0.8f ? $"качается {r.SwayDeg:F0}°" :
                            v.magnitude > 0.1f ? $"идёт {v.magnitude:F2} м/с" : "стоит";
                if (m == possessedV3) st = "▶ " + st;
                bool hit = Time.time - r.LastHitTime < 1.5f;
                if (hit) st += $"\n{r.LastWhat} {r.LastImpulse:F0} Н·с";
                tagStyle.normal.textColor = hit ? new Color(1f, 0.75f, 0.35f) : new Color(0.55f, 0.9f, 1f);
                GUI.Label(new Rect(sp.x - 110, Screen.height - sp.y - 20, 220, 40), st, tagStyle);
            }
        }
    }
}
