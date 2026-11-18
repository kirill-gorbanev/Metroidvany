using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Hero
{
    /// <summary>Одежда героя: включает меши наряда и прячет зоны кожи под ним (в игре подменяет материал зоны на невидимый;
    /// в редакторе ничего не трогает, чтобы не сохранить временный материал в сцену).
    /// Зоны кожи — материалы M_Hero_Skin_{Head, Torso, ArmR, StumpArm, StumpLegs} (blender/hero/build_hero.py);
    /// порядок подсеток после импорта FBX не тот, что в Blender, поэтому зоны ищутся по имени материала.</summary>
    public class HeroOutfit : MonoBehaviour
    {
        public SkinnedMeshRenderer bodyRenderer;
        public List<Renderer> outfitParts = new List<Renderer>();
        public bool worn = true;
        [Tooltip("Какие зоны кожи прятать, когда наряд надет (часть имени материала).")]
        public string[] hideZones = { "Torso", "StumpLegs" };   // культя плеча остаётся: внутри закрытого рукава

        Material[] skin;
        static Material hidden;

        static Material Hidden()
        {
            if (hidden) return hidden;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            hidden = new Material(sh) { name = "Hero_HiddenZone", hideFlags = HideFlags.DontSave };
            hidden.SetFloat("_AlphaClip", 1f); hidden.SetFloat("_Cutoff", 1f);
            hidden.EnableKeyword("_ALPHATEST_ON");
            hidden.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
            hidden.renderQueue = 2450;
            return hidden;
        }

        void Start() { Apply(); }

        public void Apply()
        {
            if (!bodyRenderer || !Application.isPlaying) return;
            if (skin == null) skin = bodyRenderer.sharedMaterials;
            foreach (var r in outfitParts)
            {
                if (!r) continue;
                r.enabled = worn;
                // ткань — двусторонняя: изнутри рукава/штанины не просвечивает насквозь
                foreach (var m in r.materials)
                {
                    if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                    m.doubleSidedGI = true;
                }
            }
            var mats = (Material[])skin.Clone();
            if (worn)
                for (int i = 0; i < mats.Length; i++)
                    foreach (var z in hideZones)
                        if (mats[i] && mats[i].name.Contains("_Skin_" + z)) { mats[i] = Hidden(); break; }
            bodyRenderer.sharedMaterials = mats;
        }

        public void SetWorn(bool on) { worn = on; Apply(); }
    }
}
