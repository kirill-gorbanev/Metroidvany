using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ПАДЕНИЕ ПРЕДМЕТА ВНЕ РАВНОВЕСИЯ — отдельный блок поверх перетаскивания (HeroProto.Drag).
    /// Автор 2026-11-22: «есть разница — объект просто стоит против объект падает. Разделение зависит от действия игрока: если он
    /// толкает — есть время падения, но сам по себе телом не расталкивает, другие объекты друг другом не двигает — я выбрал объект А,
    /// перетаскал и установил вне равновесия — тогда объект А падает до тех пор, пока не встанет, и тогда фиксируется».
    ///
    /// · Отпустил ЛКМ: под центром предмета нет опоры (ступень, другой предмет, пол — ниже его низа) — вне равновесия.
    /// · Тогда кинематическое тело взятого (ProtoItem.BeginCarry) становится обычным — Unity-физика: падает, кувыркается, встаёт.
    /// · Взял предмет — всё, что на нём лежит, тоже физическое (HeroProto.Drag.WakeAbove): трение везёт, рывок — соскальзывает.
    /// · Весь остальной мир для него — неподвижные коллайдеры (ставятся один раз при первом падении): рельеф, стены, пол, кубик тела,
    ///   другие предметы. Он о них бьётся, но никого не двигает.
    /// · Встал (почти не движется restTime) — физика снимается, предмет снова лежит и зафиксирован. Может лечь наклонно.
    /// · В равновесии (центр над опорой) — лежит, как раньше, без физики.</summary>
    public partial class HeroProto
    {
        [Header("Падение предмета вне равновесия")]
        [Tooltip("Отпущенный вне равновесия предмет падает по физике, пока не встанет.")]
        public bool itemTopple = true;
        [Tooltip("Опора под центром предмета ниже его низа больше чем на столько (м) — вне равновесия.")]
        public float toppleTol = 0.02f;

        bool fallWorldReady;

        /// <summary>Под центром предмета есть опора (поверхность или верх другого предмета) на уровне его низа?</summary>
        bool ItemBalanced(ProtoItem it)
        {
            Bounds b = it.WorldBounds;
            Vector3 c = Flat(b.center);
            Rect r = Rect.MinMaxRect(c.x - 0.01f, c.z - 0.01f, c.x + 0.01f, c.z + 0.01f);
            float sup = ItemSupport(it, r, b.min.y + 0.005f);
            return sup >= b.min.y - toppleTol;
        }

        /// <summary>Неподвижные коллайдеры мира — один раз: рельеф и стены (по сеткам), пол (y = 0), кубик тела.
        /// Без Rigidbody — их ничто не двигает. Логику героя не трогают: герой физику Unity не использует.</summary>
        void EnsureFallWorld()
        {
            if (fallWorldReady) return;
            fallWorldReady = true;
            foreach (var root in new[] { surfaceRoot, obstaclesRoot })
            {
                if (!root) continue;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh && !mf.GetComponent<Collider>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            var floor = new GameObject("Пол (физика падающих предметов)").AddComponent<BoxCollider>();
            floor.center = new Vector3(0f, -0.5f, 0f); floor.size = new Vector3(4000f, 1f, 4000f);
            // ТЕЛО ДЛЯ ФИЗИКИ — отдельный высокий кинематический «толкатель» по следу тела (а не коллайдер на кубике): тонкую доску
            // физика выталкивала по короткой оси — вниз, под кубик (автор 2026-11-22: доска залезла под тело). От пола до метра
            // вверх — ни подлезть, ни перескочить: выталкивает только вбок; MovePosition — физика знает скорость тела, толчок честный.
            var go = new GameObject("Тело (толкатель для физики предметов)");
            pusher = go.AddComponent<Rigidbody>(); pusher.isKinematic = true; pusher.interpolation = RigidbodyInterpolation.Interpolate;
            pusherBox = go.AddComponent<BoxCollider>();
            SyncPusher(true);
        }

        Rigidbody pusher; BoxCollider pusherBox;
        [Tooltip("Высота физического «толкателя» тела над полом (м): предметы не подлезают под тело и не перескакивают через него.")]
        public float pusherHeight = 1f;

        /// <summary>Толкатель — на месте тела: след кубика тела, от уровня тела до pusherHeight, поворот как у тела.</summary>
        void SyncPusher(bool teleport = false)
        {
            if (!pusher || !bodyCube) return;
            Vector2 half = BodyHalf;
            pusherBox.size = new Vector3(half.x * 2f, pusherHeight, half.y * 2f);
            Vector3 p = new Vector3(Body.x, groundY + climbLift + pusherHeight * 0.5f - 0.02f, Body.z);
            Quaternion r = Quaternion.Euler(0f, bodyCube.eulerAngles.y, 0f);
            if (teleport) { pusher.transform.SetPositionAndRotation(p, r); pusher.position = p; pusher.rotation = r; }
            else { pusher.MovePosition(p); pusher.MoveRotation(r); }
        }
    }
}
