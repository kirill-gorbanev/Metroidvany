using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fortress.Grid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Чисто декоративные выступы у стен без коллайдера: столбики и угловые камни на стыках, плинтусы, карнизы,
    /// дверные рамки, обвязка люка. Стены держат сами — игрок идёт вдоль стены и в узком проходе не цепляется
    /// и не встаёт на кромки. Полки, щиты, стойки на стенах — триггеры (E по ним работает, тело не цепляют).
    /// Вызывается из GridSceneBuilder для каждого блока и меню для открытой сцены (сверка с build.json по месту).</summary>
    public static class DecorColliders
    {
        static readonly HashSet<string> Decor = new HashSet<string>
            { "pillar", "quoin", "inner_corner", "skirting", "plinth", "cornice", "frame", "hatch_frame" };

        public static bool IsDecor(string[] tags) => tags != null && tags.Any(Decor.Contains);

        /// <summary>Снять коллайдеры с декоративного блока (или сделать триггерами у предметов на стене). Вернёт, сколько тронуто.</summary>
        public static int Apply(GameObject go, string kind, string[] tags)
        {
            int n = 0;
            if (IsDecor(tags))
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(c); n++; }
            else if (kind == "WallProp")
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) { c.isTrigger = true; n++; }
            return n;
        }

        [System.Serializable] class Piece { public string m, kind; public float[] pos; public string[] tags; }
        [System.Serializable] class BuildData { public Piece[] pieces; }

        static string Key(string model, Vector3 p) =>
            model + "@" + Mathf.RoundToInt(p.x * 100) + "," + Mathf.RoundToInt(p.y * 100) + "," + Mathf.RoundToInt(p.z * 100);

        [MenuItem("Fortress/Level/Decor without colliders (open scene)")]
        public static void ApplyInOpenScene()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Decor] сначала выйди из Play"); return; }
            var scene = EditorSceneManager.GetActiveScene();
            string loc = scene.name.StartsWith("Grid_") ? scene.name.Substring(5) : "fortress";
            string path = "D:/Metroidvany/level/" + loc + "/build.json";
            if (!File.Exists(path)) { Debug.LogWarning("[Decor] нет " + path); return; }
            var b = JsonUtility.FromJson<BuildData>(File.ReadAllText(path));
            var want = new Dictionary<string, Piece>();
            foreach (var p in b.pieces)
                if (IsDecor(p.tags) || p.kind == "WallProp")
                    want[Key(p.m, new Vector3(p.pos[0], p.pos[1], p.pos[2]))] = p;
            int pieces = 0, cols = 0;
            foreach (var fe in Object.FindObjectsByType<FortElement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!want.TryGetValue(Key(fe.model, fe.transform.position), out var p)) continue;
                int n = Apply(fe.gameObject, p.kind, p.tags);
                if (n > 0) { pieces++; cols += n; }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Decor] декор без коллайдеров: блоков {pieces}, коллайдеров {cols} (из {want.Count} в build.json)");
        }
    }
}
