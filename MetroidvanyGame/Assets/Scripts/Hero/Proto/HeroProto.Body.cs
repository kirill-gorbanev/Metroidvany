using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ФОРМА ТЕЛА — отдельный блок (шаг 5 долга, часть 2).
    /// Автор: «куда проблемнее, когда края тела проходят в стены». Было: тело сталкивалось кругом bodyRadius 0.2 м, а нарисовано
    /// кубиком 0.4 × 0.4 — углы кубика торчали за круг на 8 см и входили в стены и уступы.
    /// Стало: тело сталкивается СВОИМ КУБИКОМ — повёрнутым прямоугольником по размеру кубика (по повороту тела).
    /// Стены — пересечение прямоугольников (разделяющие оси); уступы и обрывы — пробы по углам и серединам сторон.
    /// Поворот на месте, в который угол кубика вошёл бы в стену, не делается. Для лежащего тела (шаг 8) — тот же прямоугольник, длинный.</summary>
    public partial class HeroProto
    {
        [Tooltip("Тело сталкивается своим кубиком (повёрнутый прямоугольник по размеру кубика), а не кругом bodyRadius.")]
        public bool boxBody = true;
        [Tooltip("Пробы уступов и обрывов — на такой доле от центра до края кубика (1 — по самому краю).")]
        [Range(0.5f, 1f)] public float boxProbe = 1f;

        /// <summary>Половины кубика тела по его осям (x — вбок, y — вперёд), м.</summary>
        Vector2 BodyHalf => bodyCube ? new Vector2(Mathf.Abs(bodyCube.lossyScale.x) * 0.5f, Mathf.Abs(bodyCube.lossyScale.z) * 0.5f) : new Vector2(bodyRadius, bodyRadius);
        /// <summary>Самая дальняя точка кубика от центра (для оценок «рядом»).</summary>
        float BodyReach => boxBody ? BodyHalf.magnitude : bodyRadius;

        /// <summary>Прямоугольник тела (центр c, поворот yaw тела) пересекает прямоугольник-препятствие глубже tol?</summary>
        bool BoxHitsRect(Vector3 c, Rect o, float tol)
        {
            Vector2 h = BodyHalf;
            Vector3 u = bodyCube.right, v = bodyCube.forward; u.y = 0f; v.y = 0f; u.Normalize(); v.Normalize();
            Vector2 oc = o.center, oh = o.size * 0.5f;
            // оси: мировые X, Z и оси тела
            Vector2[] axes = { new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(u.x, u.z), new Vector2(v.x, v.z) };
            foreach (var a in axes)
            {
                float bc = c.x * a.x + c.z * a.y;
                float br = Mathf.Abs(u.x * a.x + u.z * a.y) * h.x + Mathf.Abs(v.x * a.x + v.z * a.y) * h.y;
                float rc = oc.x * a.x + oc.y * a.y;
                float rr = Mathf.Abs(a.x) * oh.x + Mathf.Abs(a.y) * oh.y;
                if (Mathf.Abs(bc - rc) >= br + rr - tol) return false;      // есть просвет по этой оси
            }
            return true;
        }

        /// <summary>Пробы кубика тела для уступов и обрывов: 4 угла и 4 середины сторон (на boxProbe от центра).</summary>
        Vector3[] BodyProbes(Vector3 p)
        {
            Vector2 h = BodyHalf * boxProbe;
            Vector3 u = bodyCube.right, v = bodyCube.forward; u.y = 0f; v.y = 0f; u.Normalize(); v.Normalize();
            p = Flat(p);
            return new[]
            {
                p + u * h.x, p - u * h.x, p + v * h.y, p - v * h.y,
                p + u * h.x + v * h.y, p + u * h.x - v * h.y, p - u * h.x + v * h.y, p - u * h.x - v * h.y,
            };
        }
    }
}
