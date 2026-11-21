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
        void LedgeReset() { onNubFoot = false; footNub = null; footCool = 0f; nubRaise = 0f; letGoPx = letGoBack = letGoIdle = 0f; letGoCount = 0; letGoLog.Clear(); grabLog.Clear(); letGoLast = null; }
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
        /// <summary>ЕДИНИЦЫ ЖЕСТА: сырые пиксели мыши → пиксели экрана высотой 1080. Все пороги жестов заданы «как на 1080»,
        /// то есть в доле высоты экрана: в маленьком окне Game и на 4K жест той же доли экрана делает то же самое
        /// (езда и поворот и так считаются в доле экрана — через длину линии хода на экране).</summary>
        float Px1080 => 1080f / Mathf.Max(1f, Screen.height);

        [Tooltip("«От себя» до упора: рука уже прямая, а жест «от себя» продолжается столько пикселей (при высоте экрана 1080; иначе — та же доля экрана) — кисть отпускает (спуск).")]
        public float letGoPushPx = 60f;
        [Tooltip("«Отпустить»: обратный ход («на себя») меньше этого (px при 1080) не сбрасывает счёт — дрожь руки.")]
        public float letGoBackPx = 15f;
        [Tooltip("«Отпустить»: пауза в жесте дольше этого (с) сбрасывает счёт.")]
        public float letGoPause = 0.5f;
        float letGoPx, letGoBack, letGoIdle; Vector3 letGoFrom;
        [System.NonSerialized] public int letGoCount;
        /// <summary>Сколько накоплено до «отпустить» (0…1): кисть бледнеет — видно, что сейчас разожмётся.</summary>
        public float LetGoProgress => mode == Mode.Grip ? Mathf.Clamp01(letGoPx / Mathf.Max(1f, letGoPushPx)) : 0f;

        // ДИАГНОСТИКА «отпустить» (шаг 3 долга): журнал причин — только смены причины, последние 30, со временем.
        // Читать: HeroProto.DiagLetGoLog (автор воспроизводит в Play, Claude читает).
        readonly System.Collections.Generic.List<string> letGoLog = new System.Collections.Generic.List<string>();
        string letGoLast;
        void LetGoWhy(string why)
        {
            string key = why.Split(' ')[0];
            if (key == letGoLast && !why.StartsWith("ОТПУСТИЛ")) { letGoLog[letGoLog.Count - 1] = Time.time.ToString("F2") + "  " + why; return; }
            letGoLast = key;
            letGoLog.Add(Time.time.ToString("F2") + "  " + why + "   [крутое " + onSteep + ", кромка " + ClimbGrip + ", зацеп " + (gripHold ? gripHold.name : "пол") + ", t " + t.ToString("F2") + "]");
            if (letGoLog.Count > 30) letGoLog.RemoveAt(0);
        }
        public string DiagLetGoLog => string.Join("\n", letGoLog);

        // ДИАГНОСТИКА ХВАТА (автор: «на кругу не смог зацепиться вообще»): на каждое нажатие ЛКМ — что взяла кисть
        // и почему не взяла ближайшие зацепы (как в FindHold). Читать: DiagGrabLog.
        readonly System.Collections.Generic.List<string> grabLog = new System.Collections.Generic.List<string>();
        public string DiagGrabLog => string.Join("\n", grabLog);
        void GrabWhy(Vector2 px)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Time.time.ToString("F2")).Append("  курсор ").Append(px.ToString("F0")).Append(" → ")
              .Append(hoverHold ? "ЗАЦЕП " + hoverHold.name : "пол").Append(onSpin ? " (тело на диске)" : "");
            Vector3 S3 = Shoulder3;
            bool haveFloor = FloorPoint(px, out var fp), floorGood = false;
            if (haveFloor && HandOk(fp)) { HandHit(fp, out var fm, out var fn); floorGood = Vector3.Angle(fn, Vector3.up) <= steepAngle && !FloorSpins(fm, fp); }
            sb.Append(", пол под курсором ").Append(haveFloor ? (floorGood ? "хороший (радиус узкий)" : "не хороший (радиус широкий)") : "нет");
            foreach (var hd in holds)
            {
                if (!hd) continue;
                float dAny = float.MaxValue; int ok = 0;
                for (int i = 0; i <= 20; i++) { Vector3 P = hd.At(i / 20f); Vector3 sp = cam.WorldToScreenPoint(P); if (sp.z > 0f) dAny = Mathf.Min(dAny, ((Vector2)sp - px).magnitude); if (HoldOk(P, hd)) ok++; }
                if (dAny > 0.3f * Screen.height) continue;
                float far = (hd.At(hd.ClosestTo(S3)) - S3).magnitude;
                sb.Append("\n      ").Append(hd.name).Append(": до курсора ").Append(dAny.ToString("F0")).Append(" px (радиус ").Append(SnapPx(hd, floorGood).ToString("F0"))
                  .Append("), до плеча ").Append(far.ToString("F2")).Append(" м (рука ").Append(L.ToString("F2")).Append("), достижимых точек ").Append(ok).Append("/21");
            }
            grabLog.Add(sb.ToString());
            if (grabLog.Count > 15) grabLog.RemoveAt(0);
        }

        /// <summary>ОТ СЕБЯ ДО УПОРА — ОТПУСТИТЬ (автор: «движение назад не работает, я как бы зафиксирован»).
        /// Держишься за то, что выше тебя (кромка, камень, поручень выше плеча) или стоишь на крутом, рука уже прямая (тело
        /// в дальнем конце хода), а жест «от себя» продолжается letGoPushPx — кисть отпускает: тело встаёт на камень под собой
        /// или сползает. На ровном полу правило не действует (там «от себя» до упора — просто стоп, как было).</summary>
        bool LetGoPush(float alongPx)
        {
            if (mode != Mode.Grip) { letGoPx = 0f; return false; }
            if (climbLift > 0.001f) { if (alongPx < 0f) LetGoWhy("нет: висит на кромке/камне (подъём " + climbLift.ToString("F2") + ") — «от себя» сперва опускает"); letGoPx = 0f; return false; }
            // журнал автора (2026-11-21): счёт сбрасывали дрожь руки «на себя» в 1–8 px и паузы — до 60 px подряд человек не доводит.
            // Теперь: обратный ход меньше letGoBackPx — не сброс; пауза дольше letGoPause — сброс (не отпускать «из прошлого»).
            if (alongPx > 0f)
            {
                if (letGoPx > 0f) { letGoBack += alongPx * Px1080; if (letGoBack > letGoBackPx) { LetGoWhy("сброс: жест «на себя» (" + letGoBack.ToString("F0") + " px)"); letGoPx = 0f; } }
                return false;
            }
            if (alongPx == 0f)
            {
                if (letGoPx > 0f) { letGoIdle += dtNow; if (letGoIdle > letGoPause) { LetGoWhy("сброс: пауза"); letGoPx = 0f; } }
                return false;
            }
            letGoIdle = 0f; letGoBack = 0f;
            bool atFar = t <= 0.03f && tTarget <= 0.01f;
            bool aboveSh = gripHold && gripHold.At(gripHoldS).y > ShoulderY + 0.05f, bent = ComputeGripL() < L - 0.02f;
            bool pinned = onSteep || ClimbGrip || aboveSh || bent;
            if (!pinned) { LetGoWhy("нет: не прижат (не крутое, не кромка, зацеп не выше плеча, рука не согнута) — на ровном «от себя» до упора просто стоп"); letGoPx = 0f; return false; }
            if (letGoPx <= 0f) letGoFrom = Body;
            letGoPx += -alongPx * Px1080;
            if (letGoPx < letGoPushPx) { LetGoWhy("копится " + letGoPx.ToString("F0") + "/" + letGoPushPx.ToString("F0")); return false; }
            // рука прямая (дальний конец хода) — или тело за весь жест не сдвинулось: на 60° рука по 3D подтягивает его обратно
            // каждый кадр, до дальнего конца оно не доходит, и раньше кисть не отпускала (автор: «на 3 работает, на 1 — нет»)
            bool stuck = (Body - letGoFrom).magnitude < 0.02f;
            if (!atFar && !stuck) { LetGoWhy("сброс: тело отходит (сдвиг " + ((Body - letGoFrom).magnitude * 100f).ToString("F0") + " см, t " + t.ToString("F2") + ")"); letGoPx = 0f; return false; }       // тело отходит — пусть отходит, окно заново
            LetGoWhy("ОТПУСТИЛ (" + (atFar ? "рука прямая" : "тело стоит") + ")");
            letGoPx = 0f; letGoCount++;
            ReleaseGrip("«от себя» — отпустил");
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

        [Tooltip("Спуск по камням: на сколько метров опускается тело к нижнему камню на пиксель жеста «на себя» (при высоте экрана 1080; иначе — та же доля экрана).")]
        public float ladderPerPx = 0.002f;

        /// <summary>СПУСК: держишься за камень ниже плеча — «на себя» ведёт тело к этому камню (вниз по склону), пока тело не встанет на него
        /// (кисть остаётся сбоку, на 0.15 м справа от центра — рука не сквозь тело). Обычная езда тут не годится: камень почти под тобой,
        /// по полу до него меньше сгиба руки dMin. true — жест ушёл в спуск.</summary>
        bool LadderConvert(float alongPx)
        {
            if (alongPx > 0f && LadderUp) return LadderStep(alongPx);
            if (!LadderDown || alongPx <= 0f) return false;
            return LadderStep(alongPx);
        }

        /// <summary>ЛЕСТНИЦА ВВЕРХ (зеркало спуска). Автор 2026-11-21: «чтобы залезть на 3 камень, надо тянуть в 2 раза больше…
        /// как будто предел для сгибания локтя». Держишься за камень-ступеньку выше тела меньше чем на порог шага, рука согнута
        /// до упора (езде некуда: камень почти над плечом, до него меньше dMin) — раньше «на себя» не делало НИЧЕГО (подъёма нет:
        /// камень ниже порога считался ступенькой; езды нет). Теперь «на себя» ведёт тело к камню, пока оно не встанет на него
        /// (рука держит — камень под телом берётся, Ledge.NubFootLevel).</summary>
        bool LadderUp
        {
            get
            {
                if (!OnNub || !NubStandable || Hanging || t < 0.97f) return false;
                float top = gripHold.At(gripHoldS).y;
                return top > BodyLevel + nubHangMin && top <= BodyLevel + maxStep && footNub != gripHold;
            }
        }

        bool LadderStep(float alongPx)
        {
            Vector3 side = SideDir;                                  // вбок (справа): кисть в конце сбоку от тела
            Vector3 goal = Flat(gripHold.At(gripHoldS)) - side * 0.15f;
            Vector3 d = goal - Body; float dist = d.magnitude;
            if (dist < 0.02f) return true;
            // шаг — только против стен и обрывов: Slide ещё запрещает «рука глубже в тело», а кисть тут за спиной и по пути
            // проходит у самого тела; в конце она сбоку на 0.15 м
            Vector3 np = Body + d / dist * Mathf.Min(alongPx * Px1080 * ladderPerPx, dist);
            if (LadderBlocked(np)) return true;
            if (!ZoneAllows(np, climbLift)) return true;                  // кисть вышла бы из зоны — стоп (блок Zone)
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

        /// <summary>Вис на выступе: камень выше порога шага от пола. Камень, на который встать НЕЛЬЗЯ (foothold = false, только рука),
        /// — выше уровня тела хоть на nubHangMin: «ступенькой» он не станет, ждать порога шага незачем.
        /// (Автор 2026-11-21, стенд 8: камень выше тела на 0.38 и почти над плечом — езде некуда (рука согнута до упора),
        ///  а подъём не включался: камень ниже порога шага считался ступенькой. «На себя» не делало ничего, пока тело
        ///  не сползало ниже, — «двойное утягивание, как будто камень 2 преграда».)</summary>
        bool NubHang => OnNub && gripTop > groundY + (NubStandable ? maxStep + 0.005f : nubHangMin);
        bool NubStandable => gripHold is ProtoNub n && n.foothold;
        [Tooltip("Камень только для руки: вис включается, когда камень выше уровня тела хоть на столько (м).")]
        public float nubHangMin = 0.05f;
        [Tooltip("Камень только для руки: «на себя» поднимает тело, пока камень не окажется на столько (м) выше уровня тела (кисть у пояса).")]
        public float nubHangKeep = 0.1f;
        [Tooltip("Подъём тела на камне на пиксель жеста «на себя» (м/px при 1080). Кромка — climbPerPx 0.0012 (каждый сантиметр куплен рукой); на камне вдвое быстрее — примерно как езда (автор: «тянуть в 2 раза больше»).")]
        public float nubClimbPerPx = 0.0024f;

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
