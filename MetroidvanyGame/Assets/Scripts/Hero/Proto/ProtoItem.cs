using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>ПРЕДМЕТ — маркер: то, что рука берёт и тащит (блок HeroProto.Drag). Кубик или любой объект с рендером.
    /// Не стена и не опора: лежит в декоре зоны. Свой Transform — и есть предмет: тащим его, кладём, дальше — на спину / на голову (LOCK).
    /// Живые (включённые) предметы — в списке Live: выключенная зона арены свои предметы не показывает.</summary>
    public class ProtoItem : MonoBehaviour
    {
        public static readonly List<ProtoItem> Live = new List<ProtoItem>();

        /// <summary>Тащит рука сейчас.</summary>
        [System.NonSerialized] public bool held;

        Renderer rend; MaterialPropertyBlock mpb; Color baseColor = Color.white; bool haveColor;

        void OnEnable() { if (!Live.Contains(this)) Live.Add(this); }
        void OnDisable() { Live.Remove(this); held = false; }

        void Awake()
        {
            rend = GetComponentInChildren<Renderer>();
            if (rend && rend.sharedMaterial)
            {
                var m = rend.sharedMaterial;
                if (m.HasProperty("_BaseColor")) { baseColor = m.GetColor("_BaseColor"); haveColor = true; }
                else if (m.HasProperty("_Color")) { baseColor = m.GetColor("_Color"); haveColor = true; }
            }
        }

        /// <summary>Габарит в мире (по рендеру; без рендера — по масштабу единичного кубика).</summary>
        public Bounds WorldBounds => rend ? rend.bounds : new Bounds(transform.position, transform.lossyScale);
        /// <summary>Половина высоты: от центра до низа.</summary>
        public float HalfHeight => WorldBounds.extents.y;
        /// <summary>Радиус следа на полу (для стен и тела): полудиагональ габарита по XZ.</summary>
        public float Radius { get { var e = WorldBounds.extents; return Mathf.Sqrt(e.x * e.x + e.z * e.z); } }
        /// <summary>Центр низа (на поверхности).</summary>
        public Vector3 Bottom { get { var b = WorldBounds; return new Vector3(b.center.x, b.min.y, b.center.z); } }

        /// <summary>Подсветка: наведена кисть — светлее, тащим — ещё светлее. Только вид.</summary>
        public void SetLook(bool hover, bool drag, bool toBack = false)
        {
            if (!rend || !haveColor) return;
            mpb ??= new MaterialPropertyBlock();
            Color c = toBack ? Color.Lerp(baseColor, new Color(0.45f, 1f, 0.55f), 0.6f)      // отпустишь — на спину
                : drag ? Color.Lerp(baseColor, Color.white, 0.45f) : hover ? Color.Lerp(baseColor, Color.white, 0.25f) : baseColor;
            rend.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c); mpb.SetColor("_Color", c);
            rend.SetPropertyBlock(mpb);
        }

        void OnDrawGizmos()
        {
            var b = WorldBounds; Gizmos.color = held ? new Color(0.3f, 1f, 0.5f) : new Color(0.4f, 0.8f, 1f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
