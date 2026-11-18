using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Сборка человека из готовых частей Quaternius (общий скелет UE-стиля: root/pelvis/spine/neck_01/Head…):
    /// наряд (без головы) + голова, вырезанная из базового тела по весам костей Head/neck_01, + глаза, брови, причёска.
    /// Все сетки привязываются к костям наряда по именам — это один персонаж с одним аниматором.</summary>
    public static class CharacterAssembler
    {
        const string Q = "Assets/Characters/Quaternius/";
        const string Out = "Assets/Characters/Assembled";

        public class Spec
        {
            public string name, outfit, headBody;
            public string[] extras = new string[0];   // причёски/брови (FBX со скелетом)
            public bool bodyEyes = true;               // глаза и брови из базового тела
        }

        public static readonly Spec[] Cast =
        {
            new Spec { name = "Peasant_Bearded", outfit = Q + "Outfits/Male_Peasant.fbx", headBody = Q + "Base/Superhero_Male_FullBody.fbx",
                       extras = new[] { Q + "Hair/Hair_Beard.fbx", Q + "Hair/Hair_SimpleParted.fbx" } },
            new Spec { name = "Ranger_Male", outfit = Q + "Outfits/Male_Ranger.fbx", headBody = Q + "Base/Superhero_Male_FullBody.fbx",
                       extras = new[] { Q + "Hair/Hair_Buzzed.fbx" } },
            new Spec { name = "Ranger_Female", outfit = Q + "Outfits/Female_Ranger.fbx", headBody = Q + "Base/Superhero_Female_FullBody.fbx",
                       extras = new[] { Q + "Hair/Hair_Buns.fbx" } },
        };

        [MenuItem("Fortress/Characters/Assemble Cast (outfit + head + hair)")]
        public static void AssembleCast()
        {
            foreach (var s in Cast) Assemble(s);
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolder(string f)
        {
            if (AssetDatabase.IsValidFolder(f)) return;
            EnsureFolder(Path.GetDirectoryName(f).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(f).Replace('\\', '/'), Path.GetFileName(f));
        }

        public static GameObject Assemble(Spec s)
        {
            EnsureFolder(Out + "/Meshes");
            var outfitPf = AssetDatabase.LoadAssetAtPath<GameObject>(s.outfit);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(outfitPf);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = s.name;
            var byName = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) byName[t.name] = t;
            var outfitRootBone = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0].rootBone;

            // голова из базового тела
            var bodyPf = AssetDatabase.LoadAssetAtPath<GameObject>(s.headBody);
            var smrs = bodyPf.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var body = smrs.OrderByDescending(r => r.sharedMesh.vertexCount).First();
            var head = CutHead(body, s.name);
            Attach(root.transform, "Head_" + s.name, head, body.sharedMaterials, body.bones, byName, outfitRootBone);
            if (s.bodyEyes)
                foreach (var r in smrs.Where(r => r != body))
                    Attach(root.transform, r.name, r.sharedMesh, r.sharedMaterials, r.bones, byName, outfitRootBone);
            foreach (var e in s.extras)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(e);
                if (!pf) { Debug.LogWarning("[Chars] нет " + e); continue; }
                foreach (var r in pf.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    Attach(root.transform, r.name, r.sharedMesh, r.sharedMaterials, r.bones, byName, outfitRootBone);
            }
            string path = $"{Out}/{s.name}.prefab";
            AssetDatabase.DeleteAsset(path);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log($"[Chars] собран {path}: голова {head.vertexCount} верш.");
            return prefab;
        }

        static void Attach(Transform parent, string name, Mesh mesh, Material[] mats, Transform[] srcBones,
                           Dictionary<string, Transform> byName, Transform rootBone)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            var r = g.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = mesh;
            r.sharedMaterials = mats;
            r.bones = srcBones.Select(b => b && byName.TryGetValue(b.name, out var t) ? t : rootBone).ToArray();
            r.rootBone = rootBone;
            r.updateWhenOffscreen = true;
        }

        /// <summary>Голова = треугольники, у которых все вершины весят в основном на Head/neck_01 (и выше по шее).</summary>
        static Mesh CutHead(SkinnedMeshRenderer body, string tag)
        {
            var src = body.sharedMesh;
            var bones = body.bones;
            var keepBone = new HashSet<int>();
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] && (bones[i].name == "Head" || bones[i].name == "neck_01")) keepBone.Add(i);
            var w = src.boneWeights;
            bool Keep(int v) => keepBone.Contains(w[v].boneIndex0) && w[v].weight0 >= 0.5f;
            var verts = src.vertices; var norms = src.normals; var tans = src.tangents; var uv = src.uv;
            var map = new Dictionary<int, int>();
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nt = new List<Vector4>(); var nu = new List<Vector2>(); var nw = new List<BoneWeight>();
            var m = new Mesh { name = "Head_" + tag };
            var subs = new List<List<int>>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s);
                var keep = new List<int>();
                for (int i = 0; i < tris.Length; i += 3)
                {
                    if (!(Keep(tris[i]) && Keep(tris[i + 1]) && Keep(tris[i + 2]))) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tris[i + k];
                        if (!map.TryGetValue(v, out int nvI))
                        {
                            nvI = nv.Count; map[v] = nvI;
                            nv.Add(verts[v]); nn.Add(norms[v]); nw.Add(w[v]);
                            if (tans.Length == verts.Length) nt.Add(tans[v]);
                            nu.Add(uv.Length == verts.Length ? uv[v] : Vector2.zero);
                        }
                        keep.Add(nvI);
                    }
                }
                subs.Add(keep);
            }
            m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nu);
            if (nt.Count == nv.Count) m.SetTangents(nt);
            m.boneWeights = nw.ToArray();
            m.bindposes = src.bindposes;
            m.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s);
            m.RecalculateBounds();
            string p = $"{Out}/Meshes/Head_{tag}.asset";
            AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(m, p);
            return m;
        }
    }
}
