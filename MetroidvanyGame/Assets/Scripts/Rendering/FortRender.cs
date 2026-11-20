using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Fortress.Rendering
{
    /// <summary>HDRP: материалы и свет для сборщиков и рантайма — одно место вместо имён свойств по всему коду.
    ///
    /// Шкала света. Сцены задавались в «URP-единицах» (солнце ~1.2, факел ~1). В HDRP — физические единицы и экспозиция:
    /// камера держит постоянную экспозицию <see cref="ExposureEV"/>, а сила света умножается на <see cref="LightScale"/>
    /// (люксы для солнца, канделы для точечных), излучение и небо — на <see cref="NitsScale"/>. Тогда прежние числа
    /// дают ту же яркость кадра, что в URP, а HDRP добавляет свои тени, объёмный туман, отражения, затенение.</summary>
    public static class FortRender
    {
        public const string LitName = "HDRP/Lit";
        public const string WorldUVName = "Fortress/LitWorldUV";
        public const string VertexColorName = "Fortress/LitVertexColor";

        /// <summary>Постоянная экспозиция камеры (EV100) — см. HdrpSetup (профиль объёма).</summary>
        public const float ExposureEV = 9f;
        /// <summary>Яркость (нит), которая на экране даёт 1.0 при экспозиции ExposureEV.</summary>
        public static readonly float NitsScale = 1.2f * Mathf.Pow(2f, ExposureEV);
        /// <summary>URP-сила света → люксы/канделы (диффуз HDRP делит на π, URP — нет).</summary>
        public static readonly float LightScale = Mathf.PI * NitsScale;

        public static Shader Lit => Shader.Find(LitName);

        public static bool IsFortress(Shader s) => s && s.name.StartsWith("Fortress/Lit");
        public static bool IsLit(Shader s) => s && (s.name == LitName || IsFortress(s));

        /// <summary>Новый материал HDRP/Lit с цветом и гладкостью.</summary>
        public static Material NewLit(Color c, float smoothness = 0.25f, string name = null)
        {
            var m = new Material(Lit);
            if (name != null) m.name = name;
            SetColor(m, c);
            SetSmoothness(m, smoothness);
            Validate(m);
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static Color GetColor(Material m) =>
            m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;

        public static void SetBaseMap(Material m, Texture t)
        {
            if (m.HasProperty("_BaseColorMap")) m.SetTexture("_BaseColorMap", t);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
        }

        public static Texture GetBaseMap(Material m) =>
            m.HasProperty("_BaseColorMap") ? m.GetTexture("_BaseColorMap") : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;

        public static void SetBaseMapScale(Material m, Vector2 s)
        {
            if (m.HasProperty("_BaseColorMap")) m.SetTextureScale("_BaseColorMap", s);
        }

        /// <summary>Карта нормалей (null — убрать). Ключевое слово ставит Validate.</summary>
        public static void SetNormalMap(Material m, Texture t, float scale = 1f)
        {
            if (!m.HasProperty("_NormalMap")) return;
            m.SetTexture("_NormalMap", t);
            m.SetFloat("_NormalScale", scale);
        }

        public static void SetSmoothness(Material m, float s)
        {
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", s);
        }

        public static void SetMetallic(Material m, float v)
        {
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", v);
        }

        /// <summary>Свечение в URP-шкале (цвет HDR ~0..3) → ниты под экспозицию сцены.</summary>
        public static void SetEmission(Material m, Color urpEmission)
        {
            if (!m.HasProperty("_EmissiveColor")) return;
            if (m.HasProperty("_UseEmissiveIntensity")) m.SetFloat("_UseEmissiveIntensity", 0f);
            m.SetColor("_EmissiveColor", urpEmission.linear * NitsScale);
            if (m.HasProperty("_EmissiveColorLDR")) m.SetColor("_EmissiveColorLDR", urpEmission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        public static void SetDoubleSided(Material m, bool on)
        {
            if (m.HasProperty("_DoubleSidedEnable")) m.SetFloat("_DoubleSidedEnable", on ? 1f : 0f);
        }

        public static bool IsDoubleSided(Material m) => m.HasProperty("_DoubleSidedEnable") && m.GetFloat("_DoubleSidedEnable") > 0.5f;

        public static void SetAlphaClip(Material m, float cutoff)
        {
            if (m.HasProperty("_AlphaCutoffEnable")) m.SetFloat("_AlphaCutoffEnable", 1f);
            if (m.HasProperty("_AlphaCutoff")) m.SetFloat("_AlphaCutoff", cutoff);
        }

        public static void SetTransparent(Material m, bool on)
        {
            if (m.HasProperty("_SurfaceType")) m.SetFloat("_SurfaceType", on ? 1f : 0f);
            if (on && m.HasProperty("_BlendMode")) m.SetFloat("_BlendMode", 0f);   // Alpha
        }

        public static bool IsTransparent(Material m) => m.HasProperty("_SurfaceType") && m.GetFloat("_SurfaceType") > 0.5f;

        /// <summary>Ключевые слова, проходы, очередь — по свойствам. Звать после любых правок свойств.
        /// Шейдеры Fortress/* — копии HDRP/Lit: проверяются как HDRP/Lit.</summary>
        public static void Validate(Material m)
        {
            if (!m || !m.shader) return;
            var sh = m.shader;
            if (IsFortress(sh))
            {
                m.shader = Lit;
                HDMaterial.ValidateMaterial(m);
                int rq = m.renderQueue;
                m.shader = sh;
                m.renderQueue = rq;
            }
            else HDMaterial.ValidateMaterial(m);
        }

        // ------------------------------------------------------------------ свет

        /// <summary>Свет в URP-шкале → HDRP (солнце — люксы, точечные/прожекторы — канделы).</summary>
        public static void SetIntensity(Light l, float urpIntensity)
        {
            if (!l.GetComponent<HDAdditionalLightData>()) l.gameObject.AddComponent<HDAdditionalLightData>();
            l.lightUnit = l.type == LightType.Directional ? LightUnit.Lux : LightUnit.Candela;
            l.intensity = urpIntensity * LightScale;
        }

        /// <summary>Сила света обратно в URP-шкале (для переключателей, которые хранят «как было»).</summary>
        public static float GetUrpIntensity(Light l) => l.intensity / LightScale;
    }
}
