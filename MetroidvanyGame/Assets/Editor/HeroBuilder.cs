using Fortress.Hero;
using UnityEditor;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Герой: префаб из Hero.fbx (blender/hero/build_hero.py). Своего управления нет — его даёт прототип (ProtoHeroModel).
    /// Корень — HeroOutfit, внутри «Модель» (FBX, аниматор выключен) с HeroBody.
    /// Модель разворачивается так, чтобы лицо смотрело в +Z корня.</summary>
    public static class HeroBuilder
    {
        public const string Fbx = "Assets/Characters/Hero/Hero.fbx";
        public const string Prefab = "Assets/Characters/Hero/Hero.prefab";

        [MenuItem("Fortress/Hero/Build Hero Prefab")]
        public static void BuildAll()
        {
            if (BuildPrefab()) Debug.Log("[Hero] готово: " + Prefab);
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; }
            return null;
        }

        public static GameObject BuildPrefab()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (!fbx) { Debug.LogError("[Hero] нет " + Fbx + " — собери blender/hero/build_hero.py"); return null; }
            var root = new GameObject("Hero");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
                model.name = "Модель";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                var anim = model.GetComponent<Animator>();
                if (anim) anim.enabled = false;                       // поза — процедурная (HeroBody)
                // вперёд — перпендикуляр к линии плеч (глаз смещён вбок — по нему нельзя)
                var shL = FindDeep(model.transform, "upperarm_l");
                var shR = FindDeep(model.transform, "upperarm_r");
                if (shL && shR)
                {
                    Vector3 f = Vector3.Cross(Vector3.ProjectOnPlane(shR.position - shL.position, Vector3.up), Vector3.up);
                    // Cross(right, up) = (1,0,0)×(0,1,0) = +Z — лицо
                    if (f.sqrMagnitude > 1e-6f)
                        model.transform.rotation = Quaternion.FromToRotation(f.normalized, Vector3.forward) * model.transform.rotation;
                }
                foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
                var body = model.AddComponent<HeroBody>();
                body.root = root.transform;
                var outfit = root.AddComponent<HeroOutfit>();
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name == "Hero_Body" && r is SkinnedMeshRenderer s) outfit.bodyRenderer = s;
                    else if (r.name.StartsWith("Outfit_")) outfit.outfitParts.Add(r);
                }
                var saved = PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                return saved;
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
