using System.Linq;
using Fortress.Grid;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace Fortress.EditorTools
{
    /// <summary>Навмеш стражи для сцены из сетки. Агент «страж» (Humanoid, id 0): r 0.35, h 1.8, шаг 0.40, уклон 40°,
    /// без прыжков и спрыгиваний — стража ходит только полом, лестницами, дверями и арками.
    /// Створки дверей и крышки люков в запекание не идут (проём проходим, дверь страж открывает сам);
    /// решётки, трещины, ворота и двери на засове запекаются как стена. Мебель, свет, крыши — не пол.
    /// Лаз и сток (nav=player) — не для стражи, калитка — «узко».</summary>
    public static class GridNavBuilder
    {
        public const int AreaNotWalkable = 1, AreaDoor = 3, AreaNarrow = 4, AreaStair = 5;
        public const int TriggerLayer = 2;    // Ignore Raycast: зоны воды, стремянок, выходов — в навмеш не идут

        /// <summary>Параметры агента и имена областей — в ProjectSettings/NavMeshAreas.asset.</summary>
        public static void EnsureAgentAndAreas()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset").FirstOrDefault();
            if (asset == null) { Debug.LogWarning("[Nav] нет ProjectSettings/NavMeshAreas.asset"); return; }
            var so = new SerializedObject(asset);
            var st = so.FindProperty("m_Settings");
            if (st != null && st.arraySize > 0)
            {
                var s0 = st.GetArrayElementAtIndex(0);
                s0.FindPropertyRelative("agentRadius").floatValue = 0.35f;
                s0.FindPropertyRelative("agentHeight").floatValue = 1.8f;
                s0.FindPropertyRelative("agentClimb").floatValue = 0.4f;
                s0.FindPropertyRelative("agentSlope").floatValue = 40f;
                s0.FindPropertyRelative("ledgeDropHeight").floatValue = 0f;
                s0.FindPropertyRelative("maxJumpAcrossDistance").floatValue = 0f;
            }
            var areas = so.FindProperty("areas");
            if (areas != null)
            {
                void Area(int i, string name, float cost)
                {
                    var a = areas.GetArrayElementAtIndex(i);
                    a.FindPropertyRelative("name").stringValue = name;
                    a.FindPropertyRelative("cost").floatValue = cost;
                }
                Area(AreaDoor, "Door", 1.5f); Area(AreaNarrow, "Narrow", 3f); Area(AreaStair, "Stair", 1.3f);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Модификатор на один блок по его виду и меткам.</summary>
        public static void Tag(GameObject go, string kind, string[] tags)
        {
            bool Has(string t) => tags != null && tags.Contains(t);
            bool bolted = tags != null && tags.Any(t => t.StartsWith("bolt:"));
            if ((Has("door") && !bolted) || Has("trapdoor"))
            {
                var m = go.AddComponent<NavMeshModifier>(); m.ignoreFromBuild = true;
                return;
            }
            if (kind == "Prop" || kind == "WallProp" || kind == "Light" || kind == "Nature" || kind == "Roof")
            {
                var m = go.AddComponent<NavMeshModifier>(); m.overrideArea = true; m.area = AreaNotWalkable;
                return;
            }
            if (kind == "Stair")
            {
                var m = go.AddComponent<NavMeshModifier>(); m.overrideArea = true; m.area = AreaStair;
            }
        }

        /// <summary>Объём области на клетку (лаз — не для стражи, калитка — узко).</summary>
        public static void AreaVolume(Transform parent, Vector3 cellFloor, float cell, string area)
        {
            var g = new GameObject("NavArea_" + area);
            g.transform.SetParent(parent);
            g.transform.position = cellFloor + Vector3.up * 1.2f;
            var v = g.AddComponent<NavMeshModifierVolume>();
            v.size = new Vector3(cell, 2.6f, cell);
            v.center = Vector3.zero;
            v.area = area == "PlayerOnly" ? AreaNotWalkable : AreaNarrow;
        }

        /// <summary>Запечь навмеш на корне локации и сохранить его ассетом рядом со сценой.</summary>
        public static NavMeshSurface Bake(Transform root, string sceneName)
        {
            var surf = root.GetComponent<NavMeshSurface>();
            if (!surf) surf = root.gameObject.AddComponent<NavMeshSurface>();
            surf.agentTypeID = 0;
            surf.collectObjects = CollectObjects.Children;
            surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surf.layerMask = ~(1 << TriggerLayer);
            surf.overrideVoxelSize = true; surf.voxelSize = 0.1f;
            surf.overrideTileSize = true; surf.tileSize = 128;
            surf.minRegionArea = 2f;
            surf.defaultArea = 0;
            Physics.SyncTransforms();
            var t0 = System.DateTime.Now;
            surf.BuildNavMesh();
            if (surf.navMeshData != null)
            {
                System.IO.Directory.CreateDirectory("Assets/Location/Generated");
                string p = "Assets/Location/Generated/" + sceneName + "_NavMesh.asset";
                AssetDatabase.DeleteAsset(p);
                AssetDatabase.CreateAsset(surf.navMeshData, p);
            }
            var tri = NavMesh.CalculateTriangulation();
            Debug.Log($"[Nav] навмеш запечён за {(System.DateTime.Now - t0).TotalSeconds:F1} с: треугольников {tri.indices.Length / 3}");
            return surf;
        }

        [MenuItem("Fortress/Grid/Rebake NavMesh (after manual edits)")]
        public static void Rebake()
        {
            var surf = Object.FindFirstObjectByType<NavMeshSurface>();
            if (!surf) { Debug.LogWarning("[Nav] в сцене нет NavMeshSurface — пересобери сцену из карт"); return; }
            EnsureAgentAndAreas();
            Bake(surf.transform, EditorSceneManager.GetActiveScene().name);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// <summary>Отчёт: дойдёт ли каждый страж по своему маршруту (CalculatePath между соседними точками).</summary>
        [MenuItem("Fortress/Grid/Nav Report (guard routes)")]
        public static void Report()
        {
            var sb = new System.Text.StringBuilder();
            int bad = 0;
            foreach (var g in Object.FindObjectsByType<Fortress.Guards.GuardPatrol>(FindObjectsSortMode.None).OrderBy(g => g.guardId))
            {
                if (g.sleeping || g.points.Count == 0) { sb.AppendLine($"{g.guardId} ({g.role}): без обхода"); continue; }
                float len = 0; int fails = 0;
                var pts = g.points.Select(p => p.pos).ToList();
                var pairs = pts.Zip(pts.Skip(1), (a, b) => (a, b)).ToList();
                if (g.loop && pts.Count > 2) pairs.Add((pts[^1], pts[0]));
                foreach (var (a, b) in pairs)
                {
                    if (!NavMesh.SamplePosition(a, out var ha, 1.3f, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out var hb, 1.3f, NavMesh.AllAreas))
                    { fails++; sb.AppendLine($"   точка вне навмеша: {a} или {b}"); continue; }
                    var path = new NavMeshPath();
                    NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path);
                    if (path.status != NavMeshPathStatus.PathComplete)
                    { fails++; sb.AppendLine($"   нет пути {a} -> {b}: {path.status}"); continue; }
                    for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                }
                if (!g.loop) len *= 2;
                bad += fails;
                sb.AppendLine($"{g.guardId} ({g.role}): {(g.loop ? "петля" : "туда-обратно")} {len:F0} м, разрывов {fails}");
            }
            System.IO.File.WriteAllText("D:/Metroidvany/level/nav_report.txt", sb.ToString());
            Debug.Log($"[Nav] маршруты стражи: разрывов {bad}\n" + sb);
        }
    }
}
