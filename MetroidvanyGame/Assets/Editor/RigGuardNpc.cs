using System.Linq;
using Fortress.Guards;
using Fortress.Rig.V3;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace Fortress.EditorTools
{
    /// <summary>Стража замка — люди рига v3 (HumanNPC_*, клипы Mixamo) вместо капсул-заглушек.
    /// Заглушку (GuardPatrol + NavMeshAgent + капсула со «шлемом») меняет на NPC с теми же обходом, скоростями и кроватью:
    /// навмеш прокладывает путь, человек идёт своими клипами. Вызывается из GridSceneBuilder.CreateGuards и меню для открытой сцены.</summary>
    public static class RigGuardNpc
    {
        const string Pf = "Assets/Rig/Prefabs/HumanNPC_";

        public static bool Available => AssetDatabase.LoadAssetAtPath<GameObject>(Pf + "Ranger_Male.prefab");

        static GameObject PrefabFor(string role, int k)
        {
            string n = role switch
            {
                "jailer" => "Peasant_Bearded",
                "watch" or "archer" => "Ranger_Female",
                "patrol" => "Ranger_Male",
                _ => k % 2 == 0 ? "Ranger_Male" : "Peasant_Bearded",
            };
            return AssetDatabase.LoadAssetAtPath<GameObject>(Pf + n + ".prefab");
        }

        /// <summary>Заменить заглушку на NPC; вернуть нового стража (или заглушку, если NPC нет).</summary>
        public static GameObject Convert(GameObject placeholder, int k)
        {
            var old = placeholder.GetComponent<GuardPatrol>();
            if (!old || placeholder.GetComponentInChildren<HumanNPC>()) return placeholder;
            var pf = PrefabFor(old.role, k);
            if (!pf) return placeholder;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, placeholder.transform.parent);
            go.name = placeholder.name;
            go.transform.SetPositionAndRotation(placeholder.transform.position, placeholder.transform.rotation);

            var oldAgent = placeholder.GetComponent<NavMeshAgent>();
            var ag = go.AddComponent<NavMeshAgent>();
            ag.agentTypeID = oldAgent ? oldAgent.agentTypeID : 0;
            ag.radius = 0.3f; ag.height = 1.8f; ag.baseOffset = 0f;
            ag.speed = old.walkSpeed;
            ag.avoidancePriority = oldAgent ? oldAgent.avoidancePriority : 50;

            var gp = go.AddComponent<GuardPatrol>();
            EditorUtility.CopySerialized(old, gp);
            gp.body = null;
            if (old.sleeping)
            {
                // заглушка лежала на верху кровати вдоль длинной стороны: центр, ось, высота верха
                gp.bedCenter = placeholder.transform.position;
                gp.bedAlong = placeholder.transform.forward;
                gp.bedTop = placeholder.transform.position.y + 0.05f;
                ag.enabled = false;
            }
            Object.DestroyImmediate(placeholder);
            BindSeats(gp);
            return go;
        }

        /// <summary>Сидячие точки поста: найти сиденье рядом (стул, табурет, лавка) — верх и середина сиденья по сетке,
        /// куда смотреть сидя: у стула — от спинки, у табурета — взгляд поста.</summary>
        public static int BindSeats(GuardPatrol gp)
        {
            int n = 0;
            foreach (var p in gp.points)
            {
                p.hasSeat = false;
                if (!p.sit) continue;
                MeshFilter best = null; float bd = 1.3f;
                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    string nm = mf.name + " " + (mf.transform.parent ? mf.transform.parent.name : "");
                    if (!(nm.Contains("post") || nm.Contains("stool") || nm.Contains("Chair") || nm.Contains("Stool") || nm.Contains("Bench") || nm.Contains("seat"))) continue;
                    if (!mf.sharedMesh || mf.GetComponentInParent<GuardPatrol>()) continue;
                    Vector3 d = mf.transform.position - p.pos; float dy = Mathf.Abs(d.y); d.y = 0f;
                    if (dy > 1.2f || d.magnitude >= bd) continue;
                    bd = d.magnitude; best = mf;
                }
                if (!best) continue;
                var vs = best.sharedMesh.vertices;
                float floor = best.transform.position.y, seatTop = 0f;
                Vector3 back = Vector3.zero; int nb = 0;
                foreach (var v in vs) { var w = best.transform.TransformPoint(v); if (w.y - floor > 0.75f) { back += w; nb++; } }
                Vector3 c0 = best.GetComponent<Renderer>() ? best.GetComponent<Renderer>().bounds.center : best.transform.position;
                if (nb > 0) back /= nb;
                Vector3 backDir = nb > 0 ? back - c0 : Vector3.zero; backDir.y = 0f;
                foreach (var v in vs)
                {
                    var w = best.transform.TransformPoint(v); float h = w.y - floor;
                    if (h < 0.75f && (nb == 0 || Vector3.Dot(w - c0, backDir) < 0f) && h > seatTop) seatTop = h;
                }
                Vector3 sum = Vector3.zero; int ns = 0;
                foreach (var v in vs)
                {
                    var w = best.transform.TransformPoint(v); float h = w.y - floor;
                    if (h > seatTop - 0.04f && h <= seatTop + 0.001f) { sum += w; ns++; }
                }
                Vector3 seat = ns > 0 ? sum / ns : c0;
                seat.y = floor + seatTop;
                Vector3 face = nb > 0 ? -backDir.normalized : Look(p.look);
                p.hasSeat = true; p.seatPos = seat; p.seatFace = face;
                n++;
            }
            return n;
        }

        static Vector3 Look(string look) => look switch { "N" => Vector3.forward, "E" => Vector3.right, "S" => Vector3.back, "W" => Vector3.left, _ => Vector3.forward };

        [MenuItem("Fortress/Guards/Bind post seats (open scene)")]
        public static void BindSeatsInOpenScene()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Guards] сначала выйди из Play"); return; }
            int n = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var g in Object.FindObjectsByType<GuardPatrol>(FindObjectsSortMode.None))
            {
                n += BindSeats(g);
                foreach (var p in g.points) if (p.hasSeat) sb.Append($"{g.guardId}: сиденье {p.seatPos} лицом {p.seatFace}; ");
                EditorUtility.SetDirty(g);
            }
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Guards] сидячих точек с сиденьем: {n}. {sb}");
        }

        [MenuItem("Fortress/Guards/Replace placeholders with NPC (open scene)")]
        public static void ReplaceInOpenScene()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Guards] сначала выйди из Play"); return; }
            if (!Available) { Debug.LogWarning("[Guards] нет префабов HumanNPC_* — меню Fortress/Rig/Build Humans v3"); return; }
            var list = Object.FindObjectsByType<GuardPatrol>(FindObjectsSortMode.None).Where(g => !g.GetComponentInChildren<HumanNPC>()).OrderBy(g => g.guardId).ToList();
            int k = 0;
            foreach (var g in list) Convert(g.gameObject, k++);
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Guards] стражей заменено на людей: {list.Count} ({scene.path})");
        }
    }
}
