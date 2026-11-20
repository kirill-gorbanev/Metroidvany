using Fortress.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fortress.EditorTools
{
    /// <summary>Стыки: кладка по мировым координатам (рисунок сходится на углах и между кусками) и полноэкранная
    /// грязь в вогнутых стыках. HDRP: затенение (SSAO) — в профиле атмосферы сцены (HdrpSetup), грязь —
    /// Custom Pass «Crease Grime» в каждой сцене (HdrpSetup досоздаёт его при сохранении).</summary>
    public static class SeamSetup
    {
        [MenuItem("Fortress/Kit/4 Seams: world-UV masonry + SSAO + crease grime")]
        public static void Setup()
        {
            // 1) кладка и штукатурка по мировым координатам (плитка 2 м — как у набора)
            var world = Shader.Find(FortRender.WorldUVName);
            foreach (var name in new[] { "MI_UnevenBrick", "MI_Plaster", "MI_Brick", "MI_RedBrick" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Kits/Materials/" + name + ".mat");
                if (!m || !world) continue;
                m.shader = world;
                FortRender.SetBaseMapScale(m, new Vector2(0.5f, 0.5f));
                FortRender.Validate(m);
                EditorUtility.SetDirty(m);
            }
            // 2) грязь в стыках — материал полноэкранного прохода
            const string matPath = "Assets/Kits/Materials/CreaseGrime.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("Fortress/CreaseGrime")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = Shader.Find("Fortress/CreaseGrime");
            mat.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Kits/QVillage/Textures/T_BrushedNoise.png"));
            mat.SetFloat("_Strength", 0.5f); mat.SetFloat("_CrackStrength", 0.2f); mat.SetFloat("_NoiseScale", 0.6f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            // 3) открытая сцена: проход грязи и атмосфера (SSAO в профиле)
            var scene = SceneManager.GetActiveScene();
            HdrpSetup.ConvertScene(scene, false, quiet: true);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Seams] кладка по миру, грязь в стыках — включено");
        }
    }
}
