using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fortress.Grid;
using UnityEditor;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Правило «ничего не висит в воздухе»: от земли по касаниям строится цепочка опор.
    /// Элемент без цепочки к земле — висит. Касание: габарит элемента (+2 см) пересекает настоящую форму коллайдера
    /// соседа или его габарит. Отчёт — в консоль и в D:/Metroidvany/level/support_report.txt; висящие выделяются.</summary>
    public static class SupportCheck
    {
        [MenuItem("Fortress/Grid/Check Support (nothing floats)")]
        public static void Run()
        {
            Physics.SyncTransforms();   // сцену только что собрали скриптом: коллайдеры должны попасть в физику до проверок
            var all = Object.FindObjectsByType<FortElement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(e => e.GetComponentInChildren<Renderer>() != null).ToList();
            var bounds = new Dictionary<FortElement, Bounds>();
            foreach (var e in all)
            {
                var rs = e.GetComponentsInChildren<Renderer>();
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                bounds[e] = b;
            }
            // опора с земли: ландшафт, земля, пол/стены нижнего уровня, вода (её дно)
            float minY = bounds.Values.Min(b => b.min.y);
            // скальное основание: пол самого нижнего уровня карты (ниже могут быть только дно воды и подпорные стенки)
            var sm = System.Text.RegularExpressions.Regex.Match(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, @"^Grid_(.+?)(_wip)?$");
            if (sm.Success)
            {
                string bj = $"D:/Metroidvany/level/{sm.Groups[1].Value}/build.json";
                if (System.IO.File.Exists(bj))
                {
                    string txt = System.IO.File.ReadAllText(bj);
                    var lv = System.Text.RegularExpressions.Regex.Match(txt, "\"levels\":\\s*\\[(-?\\d+)");
                    var st = System.Text.RegularExpressions.Regex.Match(txt, "\"storey\":\\s*([0-9.]+)");
                    if (lv.Success)
                        minY = Mathf.Max(minY, int.Parse(lv.Groups[1].Value) *
                            (st.Success ? float.Parse(st.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 3f));
                }
            }
            var supported = new HashSet<FortElement>();
            var queue = new Queue<FortElement>();
            foreach (var e in all)
                if (e.kind == ElementKind.Ground || e.kind == ElementKind.Water || bounds[e].min.y <= minY + 0.05f ||
                    Physics.Raycast(bounds[e].center, Vector3.down, out var hit, bounds[e].extents.y + 0.06f) && hit.collider.GetComponent<Terrain>())
                { supported.Add(e); queue.Enqueue(e); }
            // кто кого касается
            var elems = all.ToArray();
            var touch = new Dictionary<FortElement, List<FortElement>>();
            foreach (var e in elems) touch[e] = new List<FortElement>();
            for (int i = 0; i < elems.Length; i++)
            {
                var bi = bounds[elems[i]]; bi.Expand(0.04f);
                foreach (var c in Physics.OverlapBox(bi.center, bi.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    var o = c.GetComponentInParent<FortElement>();
                    if (o && o != elems[i] && touch.ContainsKey(o) && !touch[elems[i]].Contains(o)) { touch[elems[i]].Add(o); touch[o].Add(elems[i]); }
                }
                for (int j = i + 1; j < elems.Length; j++)
                {   // без коллайдеров (мелочь, балки) — по габаритам
                    if (elems[i].GetComponentInChildren<Collider>() && elems[j].GetComponentInChildren<Collider>()) continue;
                    var bj = bounds[elems[j]]; bj.Expand(0.04f);
                    if (bi.Intersects(bj) && !touch[elems[i]].Contains(elems[j])) { touch[elems[i]].Add(elems[j]); touch[elems[j]].Add(elems[i]); }
                }
            }
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                foreach (var v in touch[u]) if (supported.Add(v)) queue.Enqueue(v);
            }
            var floating = all.Where(e => !supported.Contains(e) && e.kind != ElementKind.Light).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"Проверено {all.Count}, висит {floating.Count}");
            foreach (var e in floating)
                sb.AppendLine($"{e.model} {e.name} клетка {e.cell} центр {bounds[e].center:F2}");
            System.IO.File.WriteAllText("D:/Metroidvany/level/support_report.txt", sb.ToString());
            Debug.Log("[Support] " + sb.ToString().Split('\n')[0]);
            Selection.objects = floating.Select(e => (Object)e.gameObject).ToArray();
        }
    }
}
