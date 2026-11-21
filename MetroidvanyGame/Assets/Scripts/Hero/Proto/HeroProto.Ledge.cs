using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ХВАТ ЗА ВЫСТУПЫ — камни, края, неровности стены (ProtoNub). Отдельный блок поверх общего состояния героя.
    /// В прежних версиях — «автозацеп r»: кисть прилипала к выступу в радиусе r = 0.15 м; выступ важнее пола.
    ///
    /// Цель ощущения (геймдизайнер): выше порога стена перестаёт быть стеной, если на ней есть за что взяться.
    /// Каждый камень — ступенька для руки.
    ///
    /// Правила:
    ///  1. ВЫСТУП — короткий зацеп по верху камня. Держит на любом склоне, держит вес; едет с платформой и с диском.
    ///  2. ПРИЛИПАНИЕ малое: над хорошим полом — nubSnapScreenFloor (пол рядом с камнем остаётся полом);
    ///     над склоном, стеной, уступом, пустотой — nubSnapScreen или точка руки ближе nubSnapWorld (прежний r).
    ///     Поручень рядом сильнее (nubYield): при равном расстоянии берётся поручень.
    ///  3. ВИС: выступ выше порога шага, тело упёрлось (стена, уступ, рука согнута до упора) — «на себя» поднимает тело,
    ///     как у кромки, но только до «камень стал ступенькой». Перевала через камень нет. Отпустил — назад вниз (fallBack).
    ///  4. Знак за кадр: камень под курсором светлеет, кисть жёлтая (как у поручня); схвачен — камень оранжевый.</summary>
    public partial class HeroProto
    {
        [Header("Выступы")]
        [Tooltip("Прилипание к выступу над склоном / стеной / пустотой: курсор ближе этой доли высоты экрана. 0.10 ≈ 110 px на 1080.")]
        [Range(0.01f, 0.4f)] public float nubSnapScreen = 0.10f;
        [Tooltip("То же над хорошим полом: только курсор прямо на камне. 0.04 ≈ 43 px на 1080 (у поручня 0.06).")]
        [Range(0.01f, 0.4f)] public float nubSnapScreenFloor = 0.04f;
        [Tooltip("Прилипание по полу (прежний автозацеп r): точка руки ближе этого (м) к достижимой точке камня — захват. Не над хорошим полом.")]
        public float nubSnapWorld = 0.15f;
        [Tooltip("Поручень сильнее камня: к счёту камня добавляется эта доля высоты экрана.")]
        public float nubYield = 0.02f;

        [Tooltip("Камень — ступенька: центр тела ближе этого (м) к камню, а верх камня не выше уровня + порог шага — тело стоит на нём и не сползает.")]
        public float nubFootR = 0.16f;

        ProtoNub nubLit; Renderer nubLitR; Color nubLitC;
        bool onNubFoot; ProtoNub footNub; float footCool;
        [Tooltip("Сошёл с камня без руки — столько секунд на следующий камень не встаёшь (успевает начаться сползание).")]
        public float nubFootCool = 0.3f;

        /// <summary>Тело стоит на камне (ступенька на склоне): не сползает, можно отпустить руку и спокойно взяться за следующий.</summary>
        public bool OnNubFoot => onNubFoot;

        /// <summary>КАМЕНЬ ПОД ТЕЛОМ (автор: «думал, поднимусь и встану на камень, чтобы легко перехватить следующий»).
        /// Неподвижный камень под центром тела (ближе nubFootR), верх — от уровня − 0.15 до уровня + порог шага:
        /// уровень тела поднимается на верх камня, и тело на нём не сползает. Камни на подвижных элементах — только для руки.</summary>
        float NubFootLevel(float g)
        {
            onNubFoot = false;
            footCool = Mathf.Max(0f, footCool - dtNow);
            if (float.IsInfinity(g) || Hanging) { footNub = null; nubRaise = 0f; return g; }
            Vector3 b = Body;
            // ближайший подходящий камень (не самый высокий: соседний ряд тоже в радиусе) и тот, на котором стоишь
            ProtoNub bestNub = null; float bestTop = 0f, bestD = float.MaxValue, curTop = 0f; bool curIn = false;
            foreach (var hd in holds)
            {
                if (!(hd is ProtoNub nb) || !nb.foothold || hd.GetComponentInParent<ProtoMover>()) continue;
                Vector3 a = Flat(hd.A), d = Flat(hd.B) - a; float dd = Vector3.Dot(d, d);
                float s = dd > 1e-8f ? Mathf.Clamp01(Vector3.Dot(b - a, d) / dd) : 0f;
                float fd = (a + d * s - b).magnitude;
                if (fd > nubFootR) continue;
                float top = hd.At(s).y;
                if (top < g - 0.15f || top > BodyLevel + maxStep) continue;
                if (nb == footNub) { curIn = true; curTop = top; }
                if (fd < bestD) { bestD = fd; bestTop = top; bestNub = nb; }
            }
            // КТО ДЕРЖИТ:
            //  • рука держит (хват, кисть не съезжает) — встаёшь на ближайший камень (так приходят на камни: подъём, спуск «на себя»);
            //  • без руки — только тот камень, на котором уже стоишь; ушёл с него — footCool на новый не встать (камни в колонке через 17 см,
            //    иначе колонка — сплошная ступенька), а сползающее тело камни не ловят (автор: «ступеньки мешали спуску»): остановиться — рукой;
            //  • стоишь спокойно (рука свободна, не сползаешь, не в окне перед сползанием, пауза прошла) — встаёшь на камень, у которого оказался.
            //    Едешь на съезжающей кисти — камни не ловят (иначе с края настила тело вставало на верхний камень и не съезжало).
            bool held = mode == Mode.Grip && !gripSlip;
            ProtoNub take = null; float takeTop = 0f;
            if (held && bestNub) { take = bestNub; takeTop = bestTop; }
            else if (curIn) { take = footNub; takeTop = curTop; }
            else if (bestNub && mode == Mode.Free && steepTimer <= 0f && slideV <= 0f && footCool <= 0f) { take = bestNub; takeTop = bestTop; }   // рука свободна, тело стоит
            if (!take)
            {
                if (footNub && !held) footCool = nubFootCool;              // сошёл с камня без руки
                footNub = null; nubRaise = 0f; return g;
            }
            footNub = take;
            onNubFoot = true; nubRaise = Mathf.Max(0f, takeTop - g);
            return Mathf.Max(g, takeTop);
        }

        float nubRaise;
        /// <summary>На сколько камень под телом поднял его над поверхностью: порог спуска меряется от поверхности,
        /// иначе склон ниже камня кажется обрывом и с камня не сойти вниз.</summary>
        float NubRaise => onNubFoot ? nubRaise : 0f;

        /// <summary>Хват за выступ.</summary>
        public bool OnNub => mode == Mode.Grip && gripHold is ProtoNub;
        /// <summary>Кисть наведена на выступ (свободна).</summary>
        public bool HoverNub => mode == Mode.Free && hoverHold is ProtoNub;

        /// <summary>Какой зацеп возьмёт кисть при курсоре в px (сценарии, замеры).</summary>
        public ProtoHold DiagHoldAt(Vector2 px) { FindHold(px, out var b, out _); return b; }
        public ProtoHold GripHoldNow => mode == Mode.Grip ? gripHold : null;
        public bool DiagHoldOk(Vector3 p, ProtoHold hd = null) => HoldOk(p, hd);

        // ---------------- спуск ----------------
        [Tooltip("«От себя» до упора: рука уже прямая, а жест «от себя» продолжается столько пикселей — кисть отпускает (спуск).")]
        public float letGoPushPx = 60f;
        float letGoPx; Vector3 letGoFrom;
        [System.NonSerialized] public int letGoCount;

        /// <summary>ОТ СЕБЯ ДО УПОРА — ОТПУСТИТЬ (автор: «движение назад не работает, я как бы зафиксирован»).
        /// Держишься за то, что выше тебя (кромка, камень, поручень выше плеча) или стоишь на крутом, рука уже прямая (тело
        /// в дальнем конце хода), а жест «от себя» продолжается letGoPushPx — кисть отпускает: тело встаёт на камень под собой
        /// или сползает. На ровном полу правило не действует (там «от себя» до упора — просто стоп, как было).</summary>
        bool LetGoPush(float alongPx)
        {
            if (mode != Mode.Grip || climbLift > 0.001f) { letGoPx = 0f; return false; }
            if (alongPx >= 0f) { if (alongPx > 0f) letGoPx = 0f; return false; }
            bool atFar = t <= 0.03f && tTarget <= 0.01f;
            bool pinned = onSteep || ClimbGrip || (gripHold && gripHold.At(gripHoldS).y > ShoulderY + 0.05f) || ComputeGripL() < L - 0.02f;
            if (!pinned) { letGoPx = 0f; return false; }
            if (letGoPx <= 0f) letGoFrom = Body;
            letGoPx += -alongPx;
            if (letGoPx < letGoPushPx) return false;
            // рука прямая (дальний конец хода) — или тело за весь жест не сдвинулось: на 60° рука по 3D подтягивает его обратно
            // каждый кадр, до дальнего конца оно не доходит, и раньше кисть не отпускала (автор: «на 3 работает, на 1 — нет»)
            bool stuck = (Body - letGoFrom).magnitude < 0.02f;
            if (!atFar && !stuck) { letGoPx = 0f; return false; }       // тело отходит — пусть отходит, окно заново
            letGoPx = 0f; letGoCount++;
            mode = Mode.Free; pendingTurn = 0f; gripHold = null; gripSlip = false; spinSlip = false; handMover = null;
            lastGestureAlong = lastGestureSide = Vector2.zero;
            return true;
        }

        /// <summary>ЛЕСТНИЦА ВНИЗ (автор: «это же почти как лестница»): стоя на камне, рука берёт камень ниже плеча
        /// и у ног / за спиной — не только в секторе перед лицом (через тело — по-прежнему нет).</summary>
        bool LadderReach(ProtoHold hd, Vector3 P) => hd is ProtoNub && onNubFoot && P.y < ShoulderY;

        /// <summary>Держишься за камень ниже плеча — езда к нему в полную силу при любом угле кисти (спуск по камням).</summary>
        bool LadderDown => OnNub && gripHold.At(gripHoldS).y < ShoulderY - 0.02f;

        /// <summary>Шаг спуска по камням: тело держит рука, поэтому порог спуска (0.4 по краю тела) не действует — на 60° перепад по ширине тела
        /// больше полуметра. Стоп: стена, плечо в стене, под центром нет своей поверхности или обрыв глубже ladderDrop.</summary>
        bool LadderBlocked(Vector3 p)
        {
            foreach (var o in obstacles)
            {
                float cx = Mathf.Clamp(p.x, o.xMin, o.xMax), cz = Mathf.Clamp(p.z, o.yMin, o.yMax);
                float dx = p.x - cx, dz = p.z - cz;
                if (dx * dx + dz * dz < bodyRadius * bodyRadius) return true;
            }
            if (ShoulderBlocked(p)) return true;
            if (!HasSurface) return false;
            float h = LevelHeight(p, BodyLevel, maxStep);
            return float.IsInfinity(h) || h < groundY - ladderDrop;
        }

        [Tooltip("Спуск по камням: глубже этого (м) под центром тела за шаг — обрыв, дальше не спускает.")]
        public float ladderDrop = 0.8f;

        [Tooltip("Спуск по камням: на сколько метров опускается тело к нижнему камню на пиксель жеста «на себя».")]
        public float ladderPerPx = 0.002f;

        /// <summary>СПУСК: держишься за камень ниже плеча — «на себя» ведёт тело к этому камню (вниз по склону), пока тело не встанет на него
        /// (кисть остаётся сбоку, на 0.15 м справа от центра — рука не сквозь тело). Обычная езда тут не годится: камень почти под тобой,
        /// по полу до него меньше сгиба руки dMin. true — жест ушёл в спуск.</summary>
        bool LadderConvert(float alongPx)
        {
            if (!LadderDown || alongPx <= 0f) return false;
            Vector3 side = SideOff; side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.zero;
            Vector3 goal = Flat(gripHold.At(gripHoldS)) - side * 0.15f;
            Vector3 d = goal - Body; float dist = d.magnitude;
            if (dist < 0.02f) return true;
            // шаг — только против стен и обрывов: Slide ещё запрещает «рука глубже в тело», а кисть тут за спиной и по пути
            // проходит у самого тела; в конце она сбоку на 0.15 м
            Vector3 np = Body + d / dist * Mathf.Min(alongPx * ladderPerPx, dist);
            if (LadderBlocked(np)) return true;
            bodyCube.position = new Vector3(np.x, bodyCube.position.y, np.z);
            float d0 = tTarget - t; RebuildLine(Shoulder); tTarget = Mathf.Clamp01(t + d0);
            return true;
        }

        /// <summary>Радиус прилипания на экране (px) для зацепа: камень — меньше поручня.</summary>
        float SnapPx(ProtoHold hd, bool floorGood)
        {
            if (hd is ProtoNub) return (floorGood ? nubSnapScreenFloor : nubSnapScreen) * Screen.height;
            return (floorGood ? holdSnapScreenFloor : holdSnapScreen) * Screen.height;
        }

        float SnapWorld(ProtoHold hd) => hd is ProtoNub ? nubSnapWorld : holdSnapWorld;

        float SnapBias(ProtoHold hd) => hd is ProtoNub ? nubYield * Screen.height : 0f;

        /// <summary>Вис на выступе: камень выше порога шага от пола.</summary>
        bool NubHang => OnNub && gripTop > groundY + maxStep + 0.005f;

        /// <summary>Тело дальше к камню не идёт: упёрлось в уступ / стену или рука согнута до упора.</summary>
        bool NubAtWall()
        {
            if (AtLedge() || t >= 0.97f) return true;
            Vector3 dir = Flat(gripHand - Body);
            if (dir.sqrMagnitude < 1e-6f) return true;
            return Blocked(Body + dir.normalized * 0.1f, bodyRadius);
        }

        /// <summary>Подсветка камня: наведён — светлее, схвачен — оранжевый. Только знак в мире.</summary>
        void NubVisuals()
        {
            ProtoNub want = OnNub ? (ProtoNub)gripHold : HoverNub ? (ProtoNub)hoverHold : null;
            if (want != nubLit)
            {
                if (nubLitR) nubLitR.material.color = nubLitC;
                nubLit = want; nubLitR = want ? want.GetComponent<Renderer>() : null;
                if (nubLitR) nubLitC = nubLitR.material.color;
            }
            if (!nubLitR) return;
            Color c = OnNub ? new Color(1f, 0.55f, 0.15f) : Color.Lerp(nubLitC, Color.white, 0.45f);
            if (nubLitR.material.color != c) nubLitR.material.color = c;
        }
    }
}
