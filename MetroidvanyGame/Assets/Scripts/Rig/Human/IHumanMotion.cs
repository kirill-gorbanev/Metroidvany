using UnityEngine;

namespace Fortress.Rig.Human
{
    /// <summary>Источник движения человека для подстройки позы (HumanGhostIK): корень, который ходит по полу, и его скорость.
    /// v2 — HumanDriver (невидимый призрак), v3 — HumanMotor (капсула движения, видимая анимация).</summary>
    public interface IHumanMotion
    {
        /// <summary>Корень: стоит на полу, смотрит, куда идёт.</summary>
        Transform Root { get; }
        /// <summary>Фактическая горизонтальная скорость корня (м/с).</summary>
        Vector3 Velocity { get; }
    }
}
