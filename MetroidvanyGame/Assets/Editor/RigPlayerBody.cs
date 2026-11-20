using Fortress.Rig.Human;
using Fortress.Rig.V3;
using Fortress.Walker;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Игроку в замке — тело человека вместо капсулы-заглушки (WalkerBody): модель следопыта с контроллером NPC
    /// (клипы Mixamo), смотрит и шагает за капсулой PlayerWalker. Вызывается из GridSceneBuilder.CreatePlayer и меню для открытой сцены.</summary>
    public static class RigPlayerBody
    {
        const string Character = "Assets/Characters/Assembled/Ranger_Male.prefab";

        public static bool Attach(GameObject player)
        {
            var walker = player.GetComponent<PlayerWalker>();
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(RigHumanV3Builder.NpcController);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Character);
            if (!walker || !ac || !src) return false;
            var old = player.transform.Find("Модель");
            if (old) Object.DestroyImmediate(old.gameObject);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src, player.transform);
            model.name = "Модель";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;     // клипы Mixamo ставят тело лицом в +Z модели
            var anim = model.GetComponent<Animator>();
            anim.runtimeAnimatorController = ac;
            anim.applyRootMotion = false;
            anim.updateMode = AnimatorUpdateMode.Normal;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
            if (!model.GetComponent<HumanGhostIK>()) model.AddComponent<HumanGhostIK>();
            var wb = player.GetComponent<WalkerBody>() ?? player.AddComponent<WalkerBody>();
            wb.anim = anim; wb.walker = walker;
            return true;
        }

        [MenuItem("Fortress/Player/Human body instead of capsule (open scene)")]
        public static void AttachInOpenScene()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Player] сначала выйди из Play"); return; }
            var p = Object.FindFirstObjectByType<PlayerWalker>();
            if (!p) { Debug.LogWarning("[Player] в сцене нет PlayerWalker"); return; }
            bool ok = Attach(p.gameObject);
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Player] тело человека: " + (ok ? "поставлено" : "не вышло (нет контроллера NPC или персонажа)"));
        }
    }
}
