using UnityEngine;

namespace Fortress.Hero.Arm2
{
    /// <summary>БЛОК «ХВАТ» (срез 3). ЛКМ зажата и кисть касается поверхности — кисть сцепляется с ней суставом в точке касания
    /// (с миром или с телом, которому принадлежит поверхность, — платформа поедет вместе с кистью). ЛКМ отпущена — сустав снят.
    /// Правила автора: «не цепляться за потолок; предел высоты хвата — 0.4 м» (решение 3А: 0.4 — для пола и уступов, куда
    /// поднимается тело). Кисть ещё летит к цели — хват случится, когда она коснётся (не «мгновенно в точку»: правило 3).
    /// Блок только создаёт и снимает сустав; тянет тело привод руки (блок «Рука»), позу никто не пишет.</summary>
    public class Arm2Grip : MonoBehaviour, IArm2Tunable
    {
        public Arm2Hero hero;
        public Arm2HandContact contact;

        [Tooltip("Предел высоты хвата над опорой тела (м) — автор: 0.4.")]
        public float maxHeight = 0.4f;
        [Tooltip("Браться можно за поверхность не круче этого (нормаль.y ≥ cos): пол, уступ, склон до 60°. Стена и потолок — нет.")]
        public float minNormalY = 0.5f;
        [Tooltip("При хвате за ступень тело можно подтянуть к кисти до этого расстояния вдоль поверхности (м) — ближе обычных 0.18.")]
        public float stepMin = 0.05f;
        [Arm2Tune("хват: скорость съезда кисти, м/с", 0.1f, 3f)]
        [Tooltip("Рука вытянута, «от себя» продолжается — кисть едет по поверхности за телом не быстрее этого.")]
        public float slideSpeed = 1.2f;
        [Tooltip("Круче этого угла (градусы) кисть без зацепа не держит: съезжает и отпускает. Как у старого: 35.")]
        public float steepAngle = 35f;
        [Tooltip("На сколько (м) и с какой скоростью (м/с) кисть съезжает по крутому склону, прежде чем отпустить. Как у старого: 6 см.")]
        public float slipDistance = 0.06f, slipSpeed = 0.25f;
        [Tooltip("Кисть ближе этого (м) к точке на зацепе — берётся за зацеп.")]
        public float holdGrabDistance = 0.05f;
        bool slipping; float slipped;
        static readonly RaycastHit[] rayHits = new RaycastHit[8];
        /// <summary>Кисть съезжает по крутому склону (для замеров).</summary>
        public bool Slipping => slipping && hero.state.gripped;
        Collider gripCollider; float overEdge, hangTime; bool hooked;
        /// <summary>Кисть зацеплена за кромку снаружи (вис).</summary>
        public bool Hooked => hooked;
        [Arm2Tune("хват: сила удержания, Н (0 — не рвётся)", 0f, 5000f)]
        public float breakForce = 0f;

        ConfigurableJoint joint;
        int itemLayer = -1;

        void Awake() { itemLayer = LayerMask.NameToLayer("Item"); }
        public void ApplyTuning() { if (joint) joint.breakForce = breakForce > 0f ? breakForce : Mathf.Infinity; }

        /// <summary>Шаг перед физикой: нажатие / отпускание → сустав. Владелец полей grip* состояния.</summary>
        public void FixedStep(float dt)
        {
            var st = hero.state;
            if (st.pendingPress) st.wantGrip = true;
            if (st.pendingRelease) st.wantGrip = false;
            if (!st.frame.lmb && !st.pendingPress) st.wantGrip = false;       // кнопка не нажата — хвата не ждём (страховка от залипания)
            if (st.gripped && (!joint || !st.wantGrip)) Release();            // отпустили или сустав порвался
            if (st.heldItem && (!itemJoint || !st.wantGrip)) ReleaseItem();
            if (slipLock && (!st.wantGrip || (hero.arm.HandPos - slipFrom).magnitude > slipRegrip)) slipLock = false;
            if (st.heldItem) No("в руке предмет");                                    // предмет в руке: рука свободна, за мир не берётся
            else if (slipLock) No("съехала — ждёт, пока кисть уйдёт с места");
            else if (st.wantGrip && !st.gripped) { if (st.cursorItem) { No("курсор на предмете: " + st.cursorItem.name); TryGripItem(); } else if (!TryGripHold()) TryGrip(); else if (!st.gripped) No("цель — зацеп, кисть летит"); }
            else if (!st.gripped && st.frame.lmb) No("ЛКМ зажата, но хват не запрошен");
            else if (!st.gripped) No("ЛКМ не зажата");
            else if (st.gripped) { if (slipping) Slip(dt); else Slide(dt); }
            if (contact) contact.Clear();
        }

        ConfigurableJoint itemJoint;
        /// <summary>ВЗЯТЬ ПРЕДМЕТ (срез 9, п. 10–11): курсор на предмете, ЛКМ зажата, кисть его коснулась — сустав «кисть ↔ предмет»
        /// в точке касания, с любой стороны предмета. Рука остаётся свободной: кисть идёт за курсором, предмет едет с ней
        /// («предмет в руке, не на верёвке» — сцепка жёсткая, цель предмета никто не считает). Тело за предмет не тянется.</summary>
        void TryGripItem()
        {
            var st = hero.state;
            if (!contact || !contact.hasItem || !contact.itemOther || contact.itemSeparation > 0.005f) return;   // кисть ещё летит к предмету
            var rb = contact.itemOther.attachedRigidbody;
            if (!rb || rb.isKinematic || rb != st.cursorItem) return;
            Vector3 tip = hero.arm.HandPos;
            itemJoint = hero.arm.fore.gameObject.AddComponent<ConfigurableJoint>();
            itemJoint.anchor = hero.arm.tipLocal;
            itemJoint.connectedBody = rb;
            itemJoint.autoConfigureConnectedAnchor = false;
            itemJoint.connectedAnchor = rb.transform.InverseTransformPoint(tip);
            itemJoint.xMotion = itemJoint.yMotion = itemJoint.zMotion = ConfigurableJointMotion.Locked;
            itemJoint.angularXMotion = itemJoint.angularYMotion = itemJoint.angularZMotion = ConfigurableJointMotion.Free;
            itemJoint.enableCollision = true;
            itemJoint.projectionMode = JointProjectionMode.PositionAndRotation; itemJoint.projectionDistance = 0.01f; itemJoint.projectionAngle = 180f;
            LockTurn(rb);
            st.heldItem = rb;
            if (hero.guard) hero.guard.SetHeld(rb, true);
            IgnoreArm(rb, true);
        }


        /// <summary>Рука и предмет В ЭТОЙ ЖЕ РУКЕ не сталкиваются: предплечье упиралось в край взятого за верх кубика и опрокидывало
        /// его (замер: наклон 48°, отрыв от пола 38 мм при любой жёсткости удержания). Сцепка и так держит кисть на его поверхности.</summary>
        void IgnoreArm(Rigidbody item, bool ignore)
        {
            if (!item) return;
            foreach (var ic in item.GetComponentsInChildren<Collider>())
                foreach (var ac in hero.arm.colliders) if (ac && ic) Physics.IgnoreCollision(ac, ic, ignore);
        }

        ConfigurableJoint turnJoint;
        /// <summary>ПРЕДМЕТ В РУКЕ НЕ БОЛТАЕТСЯ И НЕ ОПРОКИДЫВАЕТСЯ. Сцепка с кистью — точка (шарнир): взятый за верх кубик при
        /// протяжке наклонялся и заклинивал между кистью и полом (замер: наклон 56°, отрыв от пола 44 мм; момент-пружина его не
        /// выправлял). Поворот предмета держит второй сустав «предмет ↔ тело героя»: вращение заперто, перемещение свободно.
        /// Предмет поворачивается вместе с телом героя; отдача идёт в тело 40 кг. Это связь решателя, позу никто не пишет.</summary>
        void LockTurn(Rigidbody rb)
        {
            turnJoint = rb.gameObject.AddComponent<ConfigurableJoint>();
            // связь с МИРОМ, не с телом героя: через связь с телом трение предмета о пол разворачивало героя на месте (автор:
            // «поворачиваюсь с предметом в руке — не должен»). С предметом в руке герой не ползёт, курс его не меняется.
            turnJoint.connectedBody = null;
            turnJoint.xMotion = turnJoint.yMotion = turnJoint.zMotion = ConfigurableJointMotion.Free;
            turnJoint.angularXMotion = turnJoint.angularYMotion = turnJoint.angularZMotion = ConfigurableJointMotion.Locked;
            turnJoint.enableCollision = true;
        }

        /// <summary>Отпустить предмет: сустав снят, предмет остаётся где был (для руки снова «лежащий»).</summary>
        public void ReleaseItem()
        {
            var st = hero.state;
            if (itemJoint) { itemJoint.xMotion = itemJoint.yMotion = itemJoint.zMotion = ConfigurableJointMotion.Free; Destroy(itemJoint); }
            itemJoint = null;
            if (turnJoint) { turnJoint.angularXMotion = turnJoint.angularYMotion = turnJoint.angularZMotion = ConfigurableJointMotion.Free; Destroy(turnJoint); }
            turnJoint = null;
            if (st.heldItem && hero.guard) hero.guard.SetHeld(st.heldItem, false);
            IgnoreArm(st.heldItem, false);
            // отпущенный предмет не едет дальше по инерции руки: встал там, где отпустили
            if (st.heldItem) { st.heldItem.linearVelocity = new Vector3(0f, Mathf.Min(0f, st.heldItem.linearVelocity.y), 0f); st.heldItem.angularVelocity = Vector3.zero; }
            st.heldItem = null;
        }

        /// <summary>ХВАТ ЗА ЗАЦЕП (срез 7): цель кисти стоит на камне или перилах и кисть до неё дошла — сцепка с зацепом.
        /// Зацеп держит на любом склоне и на любой высоте в досягаемости (решение автора 3А: «камни и поручни берутся на всей
        /// досягаемости руки, вис на них разрешён»). Плоскость тяги — опора под телом (склон), а не сам зацеп.</summary>
        bool TryGripHold()
        {
            var st = hero.state; var hd = st.targetHold;
            if (!hd) return false;
            Vector3 hand = hero.arm.HandPos;
            // кисть у самого зацепа — с любой его стороны (запись автора 00:32: кисть оказалась за перилами и до «своей» точки не дошла)
            Vector3 onHold = hd.At(hd.ClosestTo(hand));
            float thick = hd is Proto.ProtoNub ? 0.03f : hero.intent.railThickness;
            if ((hand - onHold).magnitude > thick + hero.intent.handRadius + holdGrabDistance) return true;   // цель — зацеп, кисть ещё летит: ждём, за пол не берёмся
            var col = hd.GetComponentInChildren<Collider>();
            var fore = hero.arm.fore;
            joint = fore.gameObject.AddComponent<ConfigurableJoint>();
            joint.anchor = hero.arm.tipLocal;
            joint.connectedBody = col ? col.attachedRigidbody : hd.GetComponentInParent<Rigidbody>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedAnchor = joint.connectedBody ? joint.connectedBody.transform.InverseTransformPoint(hand) : hand;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.enableCollision = true;
            joint.projectionMode = JointProjectionMode.PositionAndRotation; joint.projectionDistance = 0.01f; joint.projectionAngle = 180f;
            joint.breakForce = breakForce > 0f ? breakForce : Mathf.Infinity;
            gripCollider = col; overEdge = 0f; hooked = false; slipping = false;

            // нормаль опоры под телом (из касаний тела); тела нет на опоре — вертикаль
            Vector3 n = Vector3.zero; var bc = hero.arm.bodyContact;
            if (bc) for (int i = 0; i < bc.count; i++) if (bc.normals[i].y > 0.3f) n += bc.normals[i];
            n = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up;
            // Камень лежит на склоне — тянуть вдоль ЕГО склона (верх камня смотрит по нормали склона), даже если тело ещё на полу у
            // подножия: иначе тяга шла по полу в склон, и на 60° тело на него не заезжало (замер: 14 хватов, высота 0.14 м).
            if (hd is Proto.ProtoNub && hd.transform.up.y > 0.3f) n = hd.transform.up;
            else
            {
                // перила: тянуть вдоль поверхности ПОД ними (склон), а не вдоль пола у подножия — иначе тяга шла в склон, трение росло,
                // и тело на склон 36° не заезжало (запись автора 01:03: хват за перила есть, тяга 700 Н, тело на месте). Один луч в момент хвата.
                int nh = Physics.RaycastNonAlloc(new Ray(hand, Vector3.down), rayHits, 0.8f, ~0, QueryTriggerInteraction.Ignore); float bd = float.MaxValue;
                for (int i = 0; i < nh; i++)
                {
                    var rh = rayHits[i];
                    if (rh.distance >= bd || rh.collider.transform.IsChildOf(hero.transform) || rh.collider.GetComponentInParent<Proto.ProtoHold>()) continue;
                    if (rh.normal.y > 0.3f) { bd = rh.distance; n = rh.normal; }
                }
            }
            st.gripped = true; st.gripOnHold = true; st.gripBody = joint.connectedBody; st.gripPoint = hand; st.gripNormal = n; st.pullNormal = n; st.slideWant = 0f;
            Vector3 along = Vector3.ProjectOnPlane(hand - hero.intent.ShoulderWorld(), n);
            st.pullLength = along.magnitude; st.pullMin = Mathf.Min(hero.intent.dMin, st.pullLength);
            Vector3 fwd = hero.body.Rb.rotation * Vector3.forward;
            st.pullDir0 = along.sqrMagnitude > 4e-4f ? along.normalized : Vector3.ProjectOnPlane(fwd, n).normalized;
            st.yawTarget = hero.body.YawRb(0f); st.mirrorGrip = false; st.reverseGear = false;
            return true;
        }

        /// <summary>КРУТОЙ СКЛОН БЕЗ ЗАЦЕПА: кисть не держит — съезжает вниз по склону и отпускает (как у старого героя: «кисть на
        /// крутом без зацепа съезжает до 6 см и отпускает»). Круче steepAngle. Один луч в тот же коллайдер за шаг, пока съезжает.</summary>
[Tooltip("Кисть съехала с крутого и отпустила: ЛКМ ещё зажата — снова возьмётся, когда кисть уйдёт с этого места дальше этого (м).")]
        public float slipRegrip = 0.08f;
        bool slipLock; Vector3 slipFrom;
        /// <summary>Кисть съехала и ждёт, пока уйдёт с места (хват запрошен, но здесь не берётся).</summary>
        public bool SlipLocked => slipLock;
        /// <summary>Кисть съехала и отпустила. Раньше хват после этого «умирал» до нового нажатия: автор держал ЛКМ и вёл кисть
        /// с края на край, с края на верх — «не всякая поверхность поддаётся» (запись 16:51: 946 шагов ЛКМ зажата, хват не
        /// запрошен). Теперь ЛКМ зажата — кисть возьмётся за следующее место, как только уйдёт с того, где съехала.</summary>
        void SlipOff() { slipFrom = hero.arm.HandPos; slipLock = true; Release(); }

        void Slip(float dt)
        {
            var st = hero.state;
            if (!joint || !gripCollider) { SlipOff(); return; }
            if (hero.arm.Pushing) return;                                        // отталкиваются от склона — кисть прижата и держит
            float d = slipSpeed * dt; slipped += d;
            Vector3 n = st.gripNormal, down = Vector3.ProjectOnPlane(Vector3.down, n);
            if (slipped > slipDistance || down.sqrMagnitude < 1e-6f) { SlipOff(); return; }
            Vector3 p = st.gripPoint + down.normalized * d;
            if (!gripCollider.Raycast(new Ray(p + n * 0.1f, -n), out RaycastHit hit, 0.2f)) { SlipOff(); return; }
            Vector3 world = hit.point + hit.normal * (hero.intent.handRadius - hero.intent.press);
            if ((world - st.gripPoint).magnitude > d * 3f + 0.002f) { SlipOff(); return; }   // точка хвата не прыгает (правило 3)
            joint.connectedAnchor = joint.connectedBody ? joint.connectedBody.transform.InverseTransformPoint(world) : world;
            st.gripPoint = world;
        }

        /// <summary>Почему хват не случился, по шагам физики (для разбора записей): причина → число шагов.</summary>
        public static readonly System.Collections.Generic.Dictionary<string, int> Why = new System.Collections.Generic.Dictionary<string, int>();
        public static string Last = "-";
        static void No(string why) { Why.TryGetValue(why, out int n); Why[why] = n + 1; Last = why; }
        void TryGrip()
        {
            var st = hero.state;
            if (!contact || !contact.has || !contact.other) { No("нет касания кисти"); return; }     // кисть ничего не касается — ждём
            if (contact.normal.y < minNormalY) { No("нормаль круче 60°"); return; }                   // стена или потолок
            if (contact.separation > 0.005f) { No(contact.separation > 0.012f ? "зазор > 12 мм" : "зазор 5–12 мм"); return; }   // ещё не коснулась (упреждающее касание)
            if (itemLayer >= 0 && contact.other.gameObject.layer == itemLayer) { No("кисть на предмете, курсор не на нём"); return; }
            var body = hero.body.Rb;
            float support = body.position.y - Arm2Body.Height * 0.5f;                                 // опора тела
            if (contact.point.y - support > maxHeight + 0.02f) { No("выше 0.4"); return; }            // выше 0.4 — только ощупать

            var fore = hero.arm.fore;
            joint = fore.gameObject.AddComponent<ConfigurableJoint>();
            joint.anchor = hero.arm.tipLocal;
            joint.connectedBody = contact.other.attachedRigidbody;                                    // нет тела — сцепка с миром
            // точка хвата — явно (кисть сейчас); дальше её двигает только Slide. Автонастройка не годится: её значение считается
            // позже, и выключение сразу после создания оставляло точку в нуле мира (замер: героя выстрелило через всю сцену).
            joint.autoConfigureConnectedAnchor = false;
            Vector3 tip = hero.arm.HandPos;
            joint.connectedAnchor = joint.connectedBody ? joint.connectedBody.transform.InverseTransformPoint(tip) : tip;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.enableCollision = true;
            joint.projectionMode = JointProjectionMode.PositionAndRotation; joint.projectionDistance = 0.01f; joint.projectionAngle = 180f;
            joint.breakForce = breakForce > 0f ? breakForce : Mathf.Infinity;

            gripCollider = contact.other;
            st.gripped = true; st.gripOnHold = false; st.gripBody = joint.connectedBody; st.slideWant = 0f; overEdge = 0f; hooked = false;
            st.gripPoint = hero.arm.HandPos;
            // Плоскость тяги — ГРАНЬ, за которую взялись, а не нормаль касания: на ребре ступени нормаль касания наклонена
            // (до 60°), и тяга «вдоль поверхности» потянула бы тело вверх. Один луч вниз в тот же коллайдер, только в момент хвата.
            Vector3 n = contact.normal;
            if (n.y < 0.98f)
            {
                // на ребре кисть висит за краем грани: луч вниз из её центра грань не находит — пробуем со сдвигом внутрь (против
                // горизонтальной части нормали касания). Не нашли — это ребро: плоскость тяги горизонтальная, «крутым склоном» ребро
                // не считается (бот-хаотик 2026-11-26: на ребре ступени включался «съезд кисти», точка хвата убегала, тело выстреливало).
                Vector3 inward = new Vector3(-n.x, 0f, -n.z); inward = inward.sqrMagnitude > 1e-6f ? inward.normalized * hero.intent.handRadius : Vector3.zero;
                if (contact.other.Raycast(new Ray(st.gripPoint + Vector3.up * 0.1f, Vector3.down), out RaycastHit face, 0.25f) && face.normal.y >= minNormalY) n = face.normal;
                else if (contact.other.Raycast(new Ray(st.gripPoint + inward + Vector3.up * 0.1f, Vector3.down), out face, 0.25f) && face.normal.y >= minNormalY) n = face.normal;
                else n = Vector3.up;
            }
            st.gripNormal = n;
            slipping = n.y < Mathf.Cos(steepAngle * Mathf.Deg2Rad); slipped = 0f;          // крутизна — по грани, не по касанию ребра
            // плоскость тяги — опора под телом (нет опоры — горизонталь)
            {
                Vector3 sup = Vector3.zero; var bcs = hero.arm.bodyContact;
                if (bcs) for (int i = 0; i < bcs.count; i++) if (bcs.normals[i].y > 0.3f) sup += bcs.normals[i];
                st.pullNormal = sup.sqrMagnitude > 1e-6f ? sup.normalized : Vector3.up;
            }
            Vector3 along = Vector3.ProjectOnPlane(st.gripPoint - hero.intent.ShoulderWorld(), st.pullNormal);
            st.pullLength = along.magnitude;                                                          // тяги пока нет: расстояние — как есть
            st.pullMin = Mathf.Min(hero.intent.dMin, st.pullLength);                                  // кисть была ближе сгиба до упора — предел там же (без рывка на первом пикселе)
            Vector3 fwd = hero.body.Rb.rotation * Vector3.forward;
            st.pullDir0 = along.sqrMagnitude > 4e-4f ? along.normalized : Vector3.ProjectOnPlane(fwd, st.pullNormal).normalized;
            // Ближний предел — ещё и по контуру своего тела: если по ходу тяги кисть окажется перед лицом тела, тело не наезжает на
            // свою сцепленную кисть (иначе 700 Н давили бы 40 кг в предплечье). Считается один раз, в момент хвата.
            {
                var rb = hero.body.Rb; Quaternion inv = Quaternion.Inverse(rb.rotation);
                Vector3 hl = inv * (st.gripPoint - rb.position), tl = inv * st.pullDir0;
                float edge = Arm2Body.Length * 0.5f + hero.intent.handRadius + hero.intent.bodyGap, side = Arm2Body.Width * 0.5f + hero.intent.handRadius + hero.intent.bodyGap;
                if (tl.z > 0.2f && hl.z > edge && Mathf.Abs(hl.y) < Arm2Body.Height * 0.5f + hero.intent.handRadius)   // кисть на высоте тела, а не под уступом / над ним
                {
                    float dmax = (hl.z - edge) / tl.z;
                    if (Mathf.Abs(hl.x - dmax * tl.x) < side) st.pullMin = Mathf.Max(st.pullMin, st.pullLength - dmax);
                }
            }
            // Хват за ступень (поверхность выше опоры тела): тело надо затащить на неё дальше обычного сгиба — чтобы центр масс
            // оказался за ребром, иначе после отпускания оно падает обратно (замер). Кисть на ступени — сбоку от тела, не под ним.
            if (contact.point.y - support > 0.05f) st.pullMin = Mathf.Min(st.pullMin, stepMin);
            st.yawTarget = hero.body.YawRb(0f);
            st.mirrorGrip = hero.intent.LastMirrorT > 0.5f; st.reverseGear = false;
        }

        /// <summary>КИСТЬ СЪЕЗЖАЕТ ЗА ТЕЛОМ (срез 6). Рука вытянута, жест «от себя» продолжается — точка хвата плавно едет по той же
        /// поверхности к телу (не быстрее slideSpeed), и тело отползает дальше. У кромки поверхность под кистью кончается — кисть
        /// встаёт на кромке и держит: тело уходит за край спиной и повисает на руке, которая держится за самый край (урок 14:
        /// иначе прямая плечо–кисть режет угол уступа). Правило одно для любого места — края оно не ищет, флагов «обрыв» нет.
        /// Один луч вниз в тот же коллайдер за шаг физики, только пока кисть едет.</summary>
        void Slide(float dt)
        {
            var st = hero.state;
            if (!joint || !gripCollider || st.gripOnHold) { st.slideWant = 0f; return; }
            // В ВИСЕ кисть сама доползает до кромки и цепляется за неё снаружи (hooked): тогда рука висит вдоль стенки, а не режет угол.
            hangTime = st.hanging ? hangTime + dt : 0f;
            bool creep = st.hanging && !hooked && hangTime > 0.25f;             // короткое «под телом пусто» (перевал через ребро ступени) — не вис
            float d = creep ? slideSpeed * dt : Mathf.Min(st.slideWant, slideSpeed * dt);
            if (d <= 1e-5f || hooked) { st.slideWant = 0f; return; }
            if (!creep) st.slideWant -= d;
            Vector3 n = st.gripNormal, hand = st.gripPoint;
            Vector3 toBody = Vector3.ProjectOnPlane(hero.intent.ShoulderWorld() - hand, n);
            if (toBody.sqrMagnitude < 1e-6f) return;
            toBody.Normalize();
            Vector3 p = hand + toBody * d, world;
            if (gripCollider.Raycast(new Ray(p + n * 0.1f, -n), out RaycastHit hit, 0.2f) && hit.normal.y >= minNormalY)
            {
                world = hit.point + hit.normal * (hero.intent.handRadius - hero.intent.press);
                if ((world - hand).magnitude > d * 3f + 0.002f) { st.slideWant = 0f; return; }       // точка хвата не прыгает (правило 3)
                st.gripNormal = hit.normal; overEdge = 0f;
            }
            else
            {
                // под кистью поверхность кончилась — кромка. Кисть выезжает за неё на свой радиус (на той же высоте) и там держит.
                overEdge += d;
                world = p;
                if (overEdge >= hero.intent.handRadius + 0.01f) { hooked = true; st.slideWant = 0f; }
            }
            joint.connectedAnchor = joint.connectedBody ? joint.connectedBody.transform.InverseTransformPoint(world) : world;
            st.gripPoint = world;
        }

        /// <summary>Снять хват (отпустили ЛКМ, сустав порвался, постановка на старт).</summary>
        public void Release()
        {
            if (joint)
            {
                // Destroy срабатывает в конце кадра, а шагов физики до того может быть несколько: сустав отпускает сразу
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
                Destroy(joint);
            }
            joint = null; ReleaseItem();
            var st = hero.state; st.gripped = false; st.gripOnHold = false; st.gripBody = null; st.slideWant = 0f; gripCollider = null; slipping = false;
        }
    }
}
