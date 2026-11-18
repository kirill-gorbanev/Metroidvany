using UnityEditor;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Ручная сборка по сетке: шаг 1 м (стены на рёбрах клеток 2 м, полы в центрах), этаж 3 м, поворот 90°.</summary>
    public static class GridSnapTools
    {
        [MenuItem("Fortress/Grid/Snap Settings: 1 m, 90°")]
        public static void SnapSettings()
        {
            EditorSnapSettings.move = new Vector3(1f, 1f, 1f);
            EditorSnapSettings.rotate = 90f;
            EditorSnapSettings.gridSnapEnabled = true;
            Debug.Log("[Grid] привязка: шаг 1 м, поворот 90° (Ctrl — пошаговое перемещение)");
        }

        [MenuItem("Fortress/Grid/Snap Selection to Grid %#g")]
        public static void SnapSelection()
        {
            foreach (var t in Selection.transforms)
            {
                Undo.RecordObject(t, "Snap to grid");
                var p = t.position;
                t.position = new Vector3(Mathf.Round(p.x), Mathf.Round(p.y * 2f) / 2f, Mathf.Round(p.z));
                var e = t.eulerAngles;
                t.eulerAngles = new Vector3(Mathf.Round(e.x / 90f) * 90f, Mathf.Round(e.y / 90f) * 90f, Mathf.Round(e.z / 90f) * 90f);
            }
        }
    }
}
