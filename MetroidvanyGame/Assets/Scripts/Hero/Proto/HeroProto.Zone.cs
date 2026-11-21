using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ЗОНА РУКИ — ПРЕВЫШЕ ВСЕГО (автор 2026-11-21: «зона всегда превыше всего — как и другие фильтры, за них выходить нельзя»).
    ///
    /// Синяя зона (сектор движения: −sectorLeft…+sectorRight от лица) раньше проверялась только в момент хвата. Потом подъём на
    /// камне, лестница, перевал уводили тело мимо кисти — хват оказывался за спиной, вне зоны (скрин: стенд «45° камни только рука»).
    /// Теперь кисть стоит в точке хвата, а КАЖДОЕ движение тела при хвате сначала проверяется зоной: вывело бы кисть из зоны
    /// (или дальше за край, если она уже за краем после поворота) — движение не делается (подъём — ровно до края зоны).
    /// Внешняя сила (толчок стены) вывела кисть из зоны — хват срывается.
    ///
    /// Зона меряется в плоскости опоры (по нормали поверхности под телом): на склоне «впереди» — вдоль склона, поэтому тело,
    /// поднятое над камнем, оказывается с камнем за спиной. На ровном — то же, что сектор по полу.
    /// Исключений нет: лестница вниз тоже — камень у ног берётся и держится только лицом к нему.</summary>
    public partial class HeroProto
    {
        [Tooltip("Допуск края зоны (°): кисть на самом краю не дёргает движение.")]
        public float zoneTolerance = 2f;

        /// <summary>Точка хвата в мире (3D): зацеп / камень — его точка, пол — верх поверхности под кистью.</summary>
        Vector3 GripPoint3 => gripHold ? gripHold.At(gripHoldS) : new Vector3(gripHand.x, gripTop, gripHand.z);

        /// <summary>На сколько градусов кисть за краем зоны, если тело в body (по полу) поднято на lift. 0 — в зоне.</summary>
        float ZoneOut(Vector3 body, float lift)
        {
            if (mode != Mode.Grip || moveAllAround) return 0f;
            Vector3 n = bodyNormal.sqrMagnitude > 0.5f ? bodyNormal.normalized : Vector3.up;
            Vector3 S = Flat(body) + SideOff; S.y = groundY + lift + shoulderLift;
            Vector3 v = GripPoint3 - S;
            Vector3 vp = Vector3.ProjectOnPlane(v, n), fp = Vector3.ProjectOnPlane(Face, n);
            if (vp.magnitude < 0.05f || fp.sqrMagnitude < 1e-6f) return 0f;   // кисть у самого плеча — не меряем
            float a = Vector3.SignedAngle(fp, vp, n);
            if (a > sectorRight) return a - sectorRight;
            if (a < -sectorLeft) return -sectorLeft - a;
            return 0f;
        }

        /// <summary>Движение тела в body / подъём lift разрешено зоной: кисть в зоне, или за краем не дальше, чем сейчас.</summary>
        bool ZoneAllows(Vector3 body, float lift)
        {
            float now = ZoneOut(Body, climbLift), next = ZoneOut(body, lift);
            return next <= Mathf.Max(now, zoneTolerance) + 0.01f;
        }

        /// <summary>Подъём от climbLift до want — до края зоны (бисекция). Возвращает, до какой высоты можно.</summary>
        float ZoneLiftLimit(float want)
        {
            if (ZoneAllows(Body, want)) return want;
            float lo = climbLift, hi = want;
            if (!ZoneAllows(Body, lo)) return climbLift;
            for (int i = 0; i < 12; i++) { float m = (lo + hi) * 0.5f; if (ZoneAllows(Body, m)) lo = m; else hi = m; }
            return lo;
        }
    }
}
