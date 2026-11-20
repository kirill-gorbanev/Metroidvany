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

        ProtoNub nubLit; Renderer nubLitR; Color nubLitC;

        /// <summary>Хват за выступ.</summary>
        public bool OnNub => mode == Mode.Grip && gripHold is ProtoNub;
        /// <summary>Кисть наведена на выступ (свободна).</summary>
        public bool HoverNub => mode == Mode.Free && hoverHold is ProtoNub;

        /// <summary>Какой зацеп возьмёт кисть при курсоре в px (сценарии, замеры).</summary>
        public ProtoHold DiagHoldAt(Vector2 px) { FindHold(px, out var b, out _); return b; }
        public ProtoHold GripHoldNow => mode == Mode.Grip ? gripHold : null;
        public bool DiagHoldOk(Vector3 p) => HoldOk(p);

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
