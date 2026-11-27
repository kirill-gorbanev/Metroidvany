using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Hero.Arm2
{
    /// <summary>ПОЛЗУНКИ НАСТРОЙКИ В ИГРЕ (инструмент). F1 — окно: все поля с [Arm2Tune] у блоков героя (жёсткость, массы, шаг
    /// физики, скорости). Меняются прямо в Play; «Сохранить в префаб» пишет числа в префаб героя.
    /// Автор: «ощущение настраивает автор сам, не дожидаясь правок». Сам ничего не двигает.</summary>
    public class Arm2Tuning : MonoBehaviour
    {
        public bool show;
        [Tooltip("Префаб героя, куда кнопка «Сохранить в префаб» пишет числа (ставит сборщик).")]
        public string prefabPath;
        struct Row { public MonoBehaviour owner; public FieldInfo field; public Arm2TuneAttribute at; }
        readonly List<Row> rows = new List<Row>();
        Vector2 scroll; string note = "";

        void Start() { Collect(); }

        void Collect()
        {
            rows.Clear();
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!mb) continue;
                foreach (var f in mb.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var at = f.GetCustomAttribute<Arm2TuneAttribute>();
                    if (at != null && f.FieldType == typeof(float)) rows.Add(new Row { owner = mb, field = f, at = at });
                }
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) show = !show;
        }

        void OnGUI()
        {
            if (!show) { GUI.Label(new Rect(12f, 8f, 600f, 22f), "F1 — настройки   F9 — запись ввода"); return; }
            float k = Mathf.Max(1f, Screen.height / 1080f);
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            GUILayout.BeginArea(new Rect(10f, 10f, 430f, Screen.height / k - 20f), GUI.skin.box);
            GUILayout.Label("Настройки героя (F1 — скрыть)");
            scroll = GUILayout.BeginScrollView(scroll);
            MonoBehaviour last = null;
            foreach (var r in rows)
            {
                if (!r.owner) continue;
                if (r.owner != last) { GUILayout.Space(6f); GUILayout.Label("— " + r.owner.GetType().Name); last = r.owner; }
                float v = (float)r.field.GetValue(r.owner);
                GUILayout.BeginHorizontal();
                GUILayout.Label(r.at.label, GUILayout.Width(210f));
                float nv = GUILayout.HorizontalSlider(v, r.at.min, r.at.max, GUILayout.Width(130f));
                GUILayout.Label(nv.ToString(nv < 1f ? "F3" : "F2"), GUILayout.Width(60f));
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(nv, v))
                {
                    r.field.SetValue(r.owner, nv);
                    if (r.owner is IArm2Tunable t) t.ApplyTuning();
                }
            }
            GUILayout.EndScrollView();
#if UNITY_EDITOR
            if (GUILayout.Button("Сохранить в префаб")) note = SaveToPrefab();
#endif
            if (note.Length > 0) GUILayout.Label(note);
            GUILayout.EndArea();
            GUI.matrix = old;
        }

#if UNITY_EDITOR
        /// <summary>Числа ползунков — в префаб героя (по типу блока и имени поля). Ссылки и прочие поля не трогает.</summary>
        public string SaveToPrefab()
        {
            string path = prefabPath;
            if (string.IsNullOrEmpty(path)) path = UnityEditor.PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            if (string.IsNullOrEmpty(path) || !UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path)) return "префаб героя не найден";
            var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
            int n = 0;
            try
            {
                foreach (var r in rows)
                {
                    if (!r.owner) continue;
                    // блок в префабе — по пути в иерархии (у руки будет несколько блоков одного типа)
                    string rel = UnityEditor.AnimationUtility.CalculateTransformPath(r.owner.transform, transform);
                    var node = rel.Length == 0 ? root.transform : root.transform.Find(rel);
                    var dst = node ? node.GetComponent(r.owner.GetType()) : null;
                    if (!dst) continue;
                    r.field.SetValue(dst, r.field.GetValue(r.owner)); n++;
                }
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
            return $"сохранено в префаб: {n} чисел ({System.DateTime.Now:HH:mm:ss})";
        }
#endif
    }
}
