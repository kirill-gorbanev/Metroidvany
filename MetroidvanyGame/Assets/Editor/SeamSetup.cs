using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Fortress.EditorTools
{
    /// <summary>Стыки: кладка по мировым координатам (рисунок сходится на углах и между кусками),
    /// усиленный SSAO и полноэкранный шейдер грязи в вогнутых стыках.</summary>
    public static class SeamSetup
    {
        const string Renderer = "Assets/Settings/PC_Renderer.asset";

        [MenuItem("Fortress/Kit/4 Seams: world-UV masonry + SSAO + crease grime")]
        public static void Setup()
        {
            // 1) кладка и штукатурка по мировым координатам (плитка 2 м — как у набора)
            var world = Shader.Find("Fortress/LitWorldUV");
            foreach (var name in new[] { "MI_UnevenBrick", "MI_Plaster", "MI_Brick", "MI_RedBrick" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Kits/Materials/" + name + ".mat");
                if (!m || !world) continue;
                m.shader = world;
                m.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f));
                if (m.GetTexture("_BumpMap")) m.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(m);
            }
            // 2) SSAO: контактные тени в углах, под полками, за бочками
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Renderer);
            foreach (var f in data.rendererFeatures)
            {
                if (f == null || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                var so = new SerializedObject(f);
                so.FindProperty("m_Settings.Intensity").floatValue = 1.1f;
                so.FindProperty("m_Settings.Radius").floatValue = 0.45f;
                so.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.45f;
                so.FindProperty("m_Settings.Falloff").floatValue = 60f;
                so.FindProperty("m_Settings.Samples").intValue = 0;        // High
                so.FindProperty("m_Settings.BlurQuality").intValue = 0;    // High
                so.ApplyModifiedProperties();
            }
            // 3) грязь в стыках — полноэкранный проход
            const string matPath = "Assets/Kits/Materials/CreaseGrime.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("Fortress/CreaseGrime")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Kits/QVillage/Textures/T_BrushedNoise.png"));
            mat.SetFloat("_Strength", 0.5f); mat.SetFloat("_CrackStrength", 0.2f); mat.SetFloat("_NoiseScale", 0.6f);
            EditorUtility.SetDirty(mat);
            var grime = data.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f => f.name == "CreaseGrime");
            if (grime == null)
            {
                grime = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                grime.name = "CreaseGrime";
                AssetDatabase.AddObjectToAsset(grime, data);
                data.rendererFeatures.Add(grime);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(grime, out string _, out long id);
                var so = new SerializedObject(data);
                var map = so.FindProperty("m_RendererFeatureMap");
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                so.ApplyModifiedProperties();
            }
            grime.passMaterial = mat;
            grime.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            grime.requirements = ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth;
            grime.fetchColorBuffer = true;
            grime.SetActive(true);
            EditorUtility.SetDirty(grime);
            data.SetDirty();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[Seams] кладка по миру, SSAO, грязь в стыках — включено");
        }
    }
}
