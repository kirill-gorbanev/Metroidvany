using System.Collections.Generic;
using Fortress.Grid;
using Fortress.Walker;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Окно сквозь стены вокруг героя: камере — SeeThroughCamera, материалам локации на обычном URP Lit —
    /// шейдер Fortress/LitSeeThrough (копия Lit с вырезкой; LitWorldUV/LitVertexColor режут сами).
    /// Без SeeThroughCamera шейдер рисует как обычный Lit.</summary>
    public static class SeeThroughSetup
    {
        const string LitName = "Universal Render Pipeline/Lit", SeeName = "Fortress/LitSeeThrough";

        /// <summary>Камере игрока — окно сквозь стены (зовёт RigPlayerBody.Attach при сборке сцены).</summary>
        public static void AttachCamera(PlayerWalker walker)
        {
            if (!walker || !walker.cameraTransform) return;
            var st = walker.cameraTransform.GetComponent<SeeThroughCamera>() ?? walker.cameraTransform.gameObject.AddComponent<SeeThroughCamera>();
            st.target = walker.transform;
        }

        /// <summary>Материалы локации (MeshRenderer у блоков и предметов набора) с URP Lit → LitSeeThrough. Вернёт число материалов.</summary>
        public static int SwitchLevelMaterials()
        {
            var see = Shader.Find(SeeName);
            if (!see) { Debug.LogWarning("[SeeThrough] нет шейдера " + SeeName); return 0; }
            var done = new HashSet<Material>();
            foreach (var fe in Object.FindObjectsByType<FortElement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var r in fe.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (!m || done.Contains(m)) continue;
                        // только материалы набора (вода и прочее своё — нет: срез воды открыл бы дно)
                        if (!AssetDatabase.GetAssetPath(m).StartsWith("Assets/Kits/")) continue;
                        string sn = m.shader.name;
                        if (sn == LitName)
                        {
                            int rq = m.renderQueue;
                            m.shader = see;
                            m.renderQueue = rq;
                        }
                        else if (sn != SeeName && sn != "Fortress/LitWorldUV" && sn != "Fortress/LitVertexColor") continue;
                        // непрозрачный односторонний — двусторонний с тёмной изнанкой (_ST_CAP): камера в стене или за ней
                        // видит тёмную толщу, сквозь стену — только окно вокруг героя. Уже двусторонние (листва) не трогаем.
                        bool opaque = m.renderQueue < 2500 && (!m.HasProperty("_Surface") || m.GetFloat("_Surface") < 0.5f);
                        if (opaque && m.HasProperty("_Cull") && (m.GetFloat("_Cull") > 1.5f || m.IsKeywordEnabled("_ST_CAP")))
                        {
                            m.SetFloat("_Cull", 0f);
                            m.EnableKeyword("_ST_CAP");
                        }
                        EditorUtility.SetDirty(m);
                        done.Add(m);
                    }
            // вода рва: в «кукольном домике» тоже режется (без тёмной изнанки)
            var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Location/Materials/Water.mat");
            if (water && water.shader.name == LitName)
            {
                int rq = water.renderQueue; water.shader = see; water.renderQueue = rq;
                EditorUtility.SetDirty(water); done.Add(water);
            }
            AssetDatabase.SaveAssets();
            return done.Count;
        }

        [MenuItem("Fortress/Camera/See-through walls (open scene)")]
        public static void SetupOpenScene()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[SeeThrough] сначала выйди из Play"); return; }
            var walker = Object.FindFirstObjectByType<PlayerWalker>();
            AttachCamera(walker);
            int n = SwitchLevelMaterials();
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SeeThrough] камера: {(walker && walker.cameraTransform ? "есть" : "нет")}, материалов на LitSeeThrough: {n}");
        }
    }
}
