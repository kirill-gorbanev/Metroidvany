using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>СОСТОЯНИЯ — отдельный блок: селектор по приоритету (шаг 6 долга, этап 1).
    ///
    /// Каждый шаг выбирается ОДНО состояние — первое сверху, чьё условие выполнено. Над ним — наложения, которые идут всегда
    /// (едешь с опорой, тебя тянет опора кисти, копится «отпустить»). Это слой названий поверх уже работающей логики:
    /// поведение не меняется (общий прогон сверяет каждое число с эталоном), зато
    ///   · видно, что сейчас и почему (строка состояния в подсказках, DiagState);
    ///   · считаются переходы «А → Б» — таблица стыков превращается в таблицу переходов, бот её заполняет;
    ///   · новый блок (падение, предмет, перехват) — новая строка в списке приоритетов, а не флаги в чужих местах.
    /// Этап 2 (дальше, по одному): логику каждого состояния переносить в его ветку (вход / шаг / выход) и сверять с эталоном.
    ///
    /// ПРИОРИТЕТЫ (сверху — сильнее):
    ///   хват:     Вис (подтянулся у кромки / на камне) → Лестница вниз → Кисть съезжает → На зацепе → Хват за пол
    ///   свободна: (падает, оглушён) → Тащит предмет → Слёт с карусели → Сползает → Окно склона → Стоит на камне → На диске → Стоит
    /// Жест при хвате разбирается в том же порядке: «отпустить» → лестница → подъём у кромки → езда + поворот.</summary>
    public partial class HeroProto
    {
        public enum HeroState { Stand, OnDisc, SlopeWindow, Sliding, Flung, GripFloor, OnHold, HandSlipping, Hanging, Falling, Stunned, Dragging }

        static readonly Dictionary<HeroState, string> stateName = new Dictionary<HeroState, string>
        {
            { HeroState.Stand, "стоит" }, { HeroState.OnDisc, "на диске" },
            { HeroState.SlopeWindow, "окно склона" }, { HeroState.Sliding, "сползает" }, { HeroState.Flung, "слёт с карусели" },
            { HeroState.GripFloor, "хват за пол" }, { HeroState.OnHold, "на зацепе" }, { HeroState.HandSlipping, "кисть съезжает" },
            { HeroState.Hanging, "вис" },
            { HeroState.Falling, "падает" }, { HeroState.Stunned, "оглушён" }, { HeroState.Dragging, "тащит предмет" },
        };

        [Tooltip("Выносливость хвата (с): столько можно держаться на крутом / висеть, потом кисть разжимается. Бесконечность — без усталости (автор 2026-11-21: «пока не вижу причины, но переменную заведи»).")]
        public float gripStamina = float.PositiveInfinity;
        float holdTime;

        HeroState state = HeroState.Stand;
        string stateWhy = "";
        /// <summary>Кто забрал жест в этом шаге: отпустить / лестница / подъём / езда.</summary>
        string gestureOwner = "";
        readonly List<string> stateLog = new List<string>();
        readonly Dictionary<string, int> transitions = new Dictionary<string, int>();

        public HeroState State => state;
        /// <summary>«сейчас: X, потому что …» + наложения.</summary>
        public string DiagState => StateName(state) + " — " + stateWhy + Overlays();
        public string DiagStateLog => string.Join("\n", stateLog);
        public IReadOnlyDictionary<string, int> DiagTransitions => transitions;
        public static string StateName(HeroState s) => stateName[s];

        /// <summary>Селектор: первое сверху, чьё условие выполнено. Только читает состояние модели.</summary>
        HeroState SelectState(out string why)
        {
            if (mode == Mode.Grip)
            {
                if (Hanging) { why = $"подтянулся на {climbLift:F2} м у {(OnNub ? "камня" : "кромки")}, держишься"; return HeroState.Hanging; }
                if (GripSlipping) { why = spinSlip ? "кисть на диске срывает" : "кисть на крутом без зацепа"; return HeroState.HandSlipping; }
                if (OnHold) { why = $"держишься за {(OnNub ? "камень" : "зацеп")} «{gripHold.name}»"; return HeroState.OnHold; }
                why = onSteep ? "кисть на полу, тело на крутом — держит" : "кисть на полу"; return HeroState.GripFloor;
            }
            if (falling) { why = $"{fallCause}, пролетел {fallStartY - fallY:F2} м"; return HeroState.Falling; }
            if (stunT > 0f) { why = $"{lastLanding}, ещё {stunT:F1} с"; return HeroState.Stunned; }
            if (dragItem) { why = $"кисть держит «{dragItem.name}», ЛКМ зажата"; return HeroState.Dragging; }
            if (Flung) { why = "сорвался с диска, летишь"; return HeroState.Flung; }
            if (slideV > 0f || spinV > 0f) { why = slideV > 0f ? $"крутой склон без опоры, {slideV:F1} м/с" : "диск уносит"; return HeroState.Sliding; }
            if (onSteep && steepTimer > 0f) { why = $"крутой склон без опоры, {steepTimer * 1000f:F0} из {slideDelay * 1000f:F0} мс"; return HeroState.SlopeWindow; }
            if (onSpin) { why = "в зоне уноса карусели"; return HeroState.OnDisc; }
            why = "опора под ногами"; return HeroState.Stand;
        }

        string Overlays()
        {
            string s = "";
            if (rideMover) s += " · едешь с опорой";
            if (mode == Mode.Grip && handMover && handMover != rideMover) s += " · опора кисти тянет тело";
            if (LetGoProgress > 0f) s += $" · «отпустить» {LetGoProgress * 100f:F0}%";
            if (gestureOwner.Length > 0) s += " · жест → " + gestureOwner;
            if (!float.IsInfinity(gripStamina) && mode == Mode.Grip) s += $" · выносливость {Mathf.Max(0f, gripStamina - holdTime):F1} с";
            return s;
        }

        /// <summary>Конец шага: выбрать состояние, записать переход, выносливость.</summary>
        void StateStep(float dt)
        {
            // ВЫНОСЛИВОСТЬ: держишься на крутом / висишь — время идёт; на ровном и без хвата — восстанавливается сразу.
            bool straining = mode == Mode.Grip && (Hanging || onSteep);
            holdTime = straining ? holdTime + dt : 0f;
            if (straining && holdTime > gripStamina)
            {
                ReleaseGrip("кончилась выносливость", true); holdTime = 0f;
            }
            var s = SelectState(out stateWhy);
            if (s != state)
            {
                string key = StateName(state) + " → " + StateName(s);
                transitions[key] = transitions.TryGetValue(key, out int c) ? c + 1 : 1;
                bool released = mode == Mode.Free && lastRelease.Length > 0;
                stateLog.Add($"{Time.time:F2}  {key}  ({stateWhy}{(released ? "; отпустил: " + lastRelease : "")})");
                if (released) lastRelease = "";
                if (stateLog.Count > 40) stateLog.RemoveAt(0);
                state = s;
            }
        }

        /// <summary>ВЫХОД ИЗ ХВАТА — один на всех (этап 2). Раньше девять мест сбрасывали каждое свой набор
        /// (где-то оставался старый зацеп, опора кисти или флаг съезда). Причина пишется в журнал переходов.</summary>
        void ReleaseGrip(string why, bool flash = false)
        {
            reverseGear = false;                                          // новый хват — с «переднего»
            mode = Mode.Free; pendingTurn = 0f; gripHold = null; gripSlip = false; spinSlip = false; handMover = null;
            if (flash) slipFlash = 0.5f;
            lastRelease = why;
        }
        string lastRelease = "";

        void StateReset() { state = HeroState.Stand; stateWhy = ""; gestureOwner = ""; holdTime = 0f; stateLog.Clear(); transitions.Clear(); }

        /// <summary>Строка состояния на экране (при подсказках) — автор видит, что сейчас и почему.</summary>
        void OnGUI()
        {
            if (!showHints || !Application.isPlaying || ManualTick) return;
            GUI.Label(new Rect(12, 8, 1400, 24), "Состояние: " + DiagState);
        }
    }
}
