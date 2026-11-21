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

        // (камни сбоку твёрдые — убрано 2026-11-21: автор — «МЫ УДАЛИЛИ твёрдые камни»; камни телу не мешают никак)

        [Tooltip("Кисть не встаёт в тело: перед телом — не ближе этого зазора (м) к его передней стороне.")]
        public float handBodyGap = 0.03f;

        /// <summary>Точка в «зоне тела» для кисти: кубик тела, расширенный вбок до линии плеча (плечо 0.2 стоит снаружи кубика 0.3 —
        /// щель между боком и плечом тоже тело), спереди и сзади — с зазором handBodyGap. У самой линии плеча — допуск 2 см
        /// (кисть прямо перед плечом, вдоль бока — можно).</summary>
        bool InBodyZone(Vector3 h)
        {
            Vector3 u = bodyCube.right, v = bodyCube.forward; u.y = 0f; v.y = 0f; u.Normalize(); v.Normalize();
            Vector3 d = Flat(h) - Body;
            float x = Vector3.Dot(d, u), z = Vector3.Dot(d, v);
            Vector2 half = BodyHalf;
            half.x = Mathf.Max(half.x, Mathf.Abs(shoulderSide));
            return Mathf.Abs(x) < half.x - 0.02f && Mathf.Abs(z) < half.y + handBodyGap;
        }

        /// <summary>КИСТЬ НЕ В ТЕЛЕ (автор 2026-11-22: «руку к углу поставил — и он не смог рассчитать»: кисть в щели между боком
        /// и плечом смотрела в центр тела, ход заперт с обеих сторон). Кисть в зоне тела ПОВОРАЧИВАЕТСЯ ВОКРУГ ПЛЕЧА К ЛИЦУ
        /// до края зоны, длина руки та же — непрерывно. (Первая версия выталкивала к ближайшей стороне кубика — когда курсор
        /// шёл над телом, ближайшая сторона менялась, и кисть перескакивала: автор — «первые расстояния он тп, а дальше норм».)</summary>
        Vector3 HandOutOfBody(Vector3 h)
        {
            if (!boxBody || !bodyCube || !InBodyZone(h)) return h;
            Vector3 S = Shoulder, d = Flat(h - S);
            float len = d.magnitude;
            if (len < 1e-4f) return h;
            Vector3 fc = Face;
            if (InBodyZone(S + fc * len)) return h;                            // и прямо перед плечом в теле — не трогать (не бывает)
            float phi = Vector3.SignedAngle(fc, d, Vector3.up), lo = 0f, hi = phi;   // lo — снаружи (к лицу), hi — в теле
            for (int i = 0; i < 14; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (InBodyZone(S + Quaternion.AngleAxis(mid, Vector3.up) * fc * len)) hi = mid; else lo = mid;
            }
            Vector3 q = Flat(S + Quaternion.AngleAxis(lo, Vector3.up) * fc * len);
            return InObstacle(q, HandGap) ? h : q;
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
