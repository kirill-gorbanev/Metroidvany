using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fortress.Grid;
using Fortress.Guards;
using Fortress.Walker;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Fortress.EditorTools
{
    /// <summary>Собирает сцену локации из build.json (его пишет level/gridc.py): каждый блок — экземпляр префаба набора.</summary>
    public static class GridSceneBuilder
    {
        [Serializable] class Piece { public string m, prefab, kind, room, id; public float[] pos, rot, scl; public int[] cell; public string[] tags; }
        [Serializable] class WpT { public int k, every; public float[] pos, sit_pos; public float wait; public string look, sit; public string @do; }
        [Serializable] class SleepT { public string bed, on_wake; public float[] pos; public float rot, wake; }
        [Serializable] class GuardT { public string id, role, face, route; public int k; public float[] pos; public float walk, run; public WpT[] wps; public SleepT sleep; }
        [Serializable] class DoorT { public string kind, bolt; public int k; public float[] pos; public float yaw; }
        [Serializable] class AreaT { public string area; public int k; public float[] pos; }
        [Serializable] class ExitT { public string name; public int k; public float[] pos; public float yaw; }
        [Serializable] class NavT { public DoorT[] doors; public AreaT[] areas; public ExitT[] exits; public string finish; }
        [Serializable] class GroundT { public float[] pos; public string mat; public int[] cell; }
        [Serializable] class WaterT { public int[] cell; public float[] pos; public float bottom; }
        [Serializable] class LadderT { public float[] pos; public float yaw, height; public int[] cell; public bool water; }
        [Serializable] class LightT { public float[] pos, color; public float intensity, range; public bool shadows; }
        [Serializable] class PointT { public string name; public float[] pos; }
        [Serializable] class BuildData
        {
            public string name; public float cell, storey; public float[] origin; public int[] size; public int[] levels;
            public float[] spawn; public Piece[] pieces; public GroundT[] ground; public WaterT[] water; public LadderT[] ladders;
            public LightT[] lights; public PointT[] points; public int errors; public string[] l0;
            public GuardT[] guards; public NavT nav;
        }

        const string LevelRoot = "D:/Metroidvany/level/";
        const string LocMat = "Assets/Location/Materials/";

        [MenuItem("Fortress/Grid/Build Scene: fortress")] public static void BuildFortress() => Build("fortress");
        [MenuItem("Fortress/Grid/Build Scene: test")] public static void BuildTest() => Build("test");
        [MenuItem("Fortress/Grid/Build WIP Scene: test")] public static void BuildTestWip() => Build("test", true);
        [MenuItem("Fortress/Grid/Build WIP Scene: fortress")] public static void BuildFortressWip() => Build("fortress", true);

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);
        static Vector3 Dir(string d) => d switch { "N" => Vector3.forward, "E" => Vector3.right, "S" => Vector3.back, "W" => Vector3.left, _ => Vector3.zero };
        static string BoltOf(string[] tags) => tags.FirstOrDefault(t => t.StartsWith("bolt:"))?.Substring(5);

        public static void Build(string loc) => Build(loc, false);

        /// <summary>wip = рабочая сцена Grid_&lt;loc&gt;_wip: проверяю в ней, сцену автора не трогаю.</summary>
        public static void Build(string loc, bool wip)
        {
            string sceneName = "Grid_" + loc + (wip ? "_wip" : "");
            var b = JsonUtility.FromJson<BuildData>(File.ReadAllText(LevelRoot + loc + "/build.json"));
            GridNavBuilder.EnsureAgentAndAreas();
            var brick = AssetDatabase.LoadAssetAtPath<Material>("Assets/Kits/Materials/MI_UnevenBrick.mat");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Location_" + loc).transform;
            var groups = new Dictionary<string, Transform>();
            Transform Group(int k, string kind)
            {
                string lk = "L" + k, key = lk + "/" + kind;
                if (groups.TryGetValue(key, out var t)) return t;
                if (!groups.TryGetValue(lk, out var lt))
                {
                    lt = new GameObject(lk).transform; lt.SetParent(root); groups[lk] = lt;
                }
                t = new GameObject(kind).transform; t.SetParent(lt); groups[key] = t;
                return t;
            }
            var cache = new Dictionary<string, GameObject>();
            var lightPieces = new List<GameObject>();
            var portcullisGroups = new Dictionary<string, Transform>();
            int made = 0, missing = 0;
            bool hasTerrain = b.l0 != null && b.l0.Length > 0;
            foreach (var p in b.pieces)
            {
                if (hasTerrain && p.tags != null && p.tags.Contains("pave")) continue;   // мощение на земле рисует ландшафт
                if (!cache.TryGetValue(p.prefab, out var pf)) cache[p.prefab] = pf = AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab);
                if (pf == null) { missing++; continue; }
                int k = p.cell != null && p.cell.Length > 2 ? Mathf.FloorToInt((p.pos[1] + 0.3f) / b.storey) : 0;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, Group(k, p.kind));
                go.transform.SetPositionAndRotation(V(p.pos), Quaternion.Euler(p.rot[0], p.rot[1], p.rot[2]));
                go.transform.localScale = V(p.scl);
                var fe = go.GetComponent<FortElement>();
                if (fe)
                {
                    if (p.cell != null && p.cell.Length > 2) fe.cell = new Vector3Int(p.cell[0], p.cell[1], p.cell[2]);
                    fe.room = p.room;
                }
                var tags = p.tags ?? new string[0];
                if (!string.IsNullOrEmpty(p.id)) go.name = p.id;
                bool moving = false;
                string bolt = BoltOf(tags);
                if (tags.Contains("ext") && brick)
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                    {   // наружная половина укреплённой стены: деревянная балка над проёмом -> каменная перемычка
                        var ms = r.sharedMaterials; bool ch = false;
                        for (int mi = 0; mi < ms.Length; mi++)
                            if (ms[mi] && (ms[mi].name == "MI_WoodTrim" || ms[mi].name == "MI_WoodTrim_Wear")) { ms[mi] = brick; ch = true; }
                        if (ch) r.sharedMaterials = ms;
                    }
                if (tags.Contains("door"))
                {
                    var ds = go.AddComponent<DoorSwing>(); moving = true;
                    if (bolt != null) { ds.bolted = true; ds.boltSide = Dir(bolt); }
                }
                if (tags.Contains("portcullis"))
                {
                    moving = true;
                    var grp = tags.FirstOrDefault(t => t.StartsWith("group:"));
                    if (grp == null) go.AddComponent<Portcullis>().startRaised = tags.Contains("raised");
                    else
                    {   // широкие ворота: несколько полотен решётки — одна решётка (поднимаются вместе)
                        if (!portcullisGroups.TryGetValue(grp, out var holder))
                        {
                            holder = new GameObject("Portcullis_" + grp.Substring(6)).transform;
                            holder.SetParent(go.transform.parent); holder.position = go.transform.position;
                            portcullisGroups[grp] = holder;
                        }
                        go.transform.SetParent(holder, true);
                    }
                }
                if (tags.Contains("breakable")) { go.AddComponent<BreakableBars>(); moving = true; }
                if (tags.Contains("gate") && bolt != null) { go.AddComponent<BoltedGrate>().boltSide = Dir(bolt); moving = true; }
                if (tags.Contains("crack")) go.AddComponent<CrackWall>();
                if (p.id == "pickaxe") go.AddComponent<PickupTool>().tool = "pickaxe";
                GridNavBuilder.Tag(go, p.kind, tags);
                DecorColliders.Apply(go, p.kind, tags);    // декор у стен — без коллайдера (не цепляет, не встать на кромку)
                if (tags.Contains("trapdoor"))
                {
                    var td = go.AddComponent<Trapdoor>(); moving = true;
                    if (bolt == "U") td.boltUp = true;
                    var h = tags.FirstOrDefault(t => t.StartsWith("hinge:")); if (h != null) td.hinge = h.Substring(6);
                    if (tags.Contains("chain"))
                    {   // цепь-ручка у свободного края крышки — откидывается вместе с ней
                        Vector3 hd = td.hinge switch { "N" => Vector3.forward, "E" => Vector3.right, "W" => Vector3.left, _ => Vector3.back };
                        var chainPf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kits/Prefabs/Props/Chain_Coil.prefab");
                        var ch = (GameObject)PrefabUtility.InstantiatePrefab(chainPf);
                        ch.transform.SetPositionAndRotation(go.transform.position - hd * 0.3f + Vector3.up * 0.03f, Quaternion.Euler(0, 35f, 0));
                        ch.transform.localScale = Vector3.one * 0.32f;
                        foreach (var c in ch.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                        Object.DestroyImmediate(ch.GetComponent<FortElement>());
                        ch.transform.SetParent(go.transform, true);
                    }
                    // низ тонкой доски виден из подвала: вторая доска лицом вниз (у толстых крышек не нужно)
                    if (p.m.StartsWith("Floor_"))
                    {
                        var under = (GameObject)PrefabUtility.InstantiatePrefab(pf, go.transform);
                        under.transform.localPosition = new Vector3(0, -0.03f, 0); under.transform.localRotation = Quaternion.Euler(180, 0, 0);
                        under.transform.localScale = Vector3.one;
                        foreach (var c in under.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                        Object.DestroyImmediate(under.GetComponent<FortElement>());
                    }
                }
                if (moving)
                    foreach (var t in go.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                if (p.kind == "Light") lightPieces.Add(go);
                made++;
            }

            foreach (var holder in portcullisGroups.Values)
            {
                var pc = holder.gameObject.AddComponent<Portcullis>(); pc.startRaised = true;
                foreach (var t in holder.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            }

            // земля: трава/почва — общая сетка на материал; луг вокруг карты
            var env = new GameObject("Ground").transform; env.SetParent(root);
            var mats = new Dictionary<string, Material>
            {
                { "grass", AssetDatabase.LoadAssetAtPath<Material>(LocMat + "EXP_grass.mat") },
                { "soil", AssetDatabase.LoadAssetAtPath<Material>(LocMat + "EXP_soil.mat") },
            };
            bool terrain = b.l0 != null && b.l0.Length > 0;
            if (terrain) GroundTerrain.Build(sceneName, env, b.origin[0], b.origin[1], b.size[0], b.size[1], b.cell, b.l0);
            foreach (var g in (b.ground ?? new GroundT[0]).Where(t => !terrain || Mathf.Abs(t.pos[1]) > 0.5f).GroupBy(g => g.mat))
                QuadMesh("Ground_" + g.Key, env, g.Select(t => (V(t.pos), b.cell / 2f)).ToList(), mats.TryGetValue(g.Key, out var gm) ? gm : mats["grass"], true);
            float x0 = b.origin[0], z0 = b.origin[1], x1 = x0 + b.size[0] * b.cell, z1 = z0 + b.size[1] * b.cell, R = 160f;
            var meadow = new List<(Vector3, Vector3)>
            {
                (new Vector3(x0 - R, 0, z1), new Vector3(x1 + R, 0, z1 + R)), (new Vector3(x0 - R, 0, z0 - R), new Vector3(x1 + R, 0, z0)),
                (new Vector3(x0 - R, 0, z0), new Vector3(x0, 0, z1)), (new Vector3(x1, 0, z0), new Vector3(x1 + R, 0, z1)),
            };
            if (!terrain) RectMesh("Meadow", env, meadow, mats["grass"]);
            // граница карты: невидимые стены
            for (int s = 0; s < 4; s++)
            {
                var w = new GameObject("Border_" + s); w.transform.SetParent(env);
                var bc = w.AddComponent<BoxCollider>();
                float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, ymin = b.levels[0] * b.storey - b.storey;
                bc.center = s switch
                {
                    0 => new Vector3(cx, ymin + 25, z1 + 0.5f), 1 => new Vector3(cx, ymin + 25, z0 - 0.5f),
                    2 => new Vector3(x0 - 0.5f, ymin + 25, cz), _ => new Vector3(x1 + 0.5f, ymin + 25, cz),
                };
                bc.size = s < 2 ? new Vector3(x1 - x0 + 2, 50, 1) : new Vector3(1, 50, z1 - z0 + 2);
            }

            // вода
            var wroot = new GameObject("Water").transform; wroot.SetParent(root);
            var water = b.water ?? new WaterT[0];
            if (water.Length > 0)
            {
                QuadMesh("WaterSurface", wroot, water.Select(w => (V(w.pos), b.cell / 2f)).ToList(),
                         AssetDatabase.LoadAssetAtPath<Material>(LocMat + "Water.mat"), false, ShadowCastingMode.Off);
                foreach (var w in water)
                {
                    var z = new GameObject($"Swim_{w.cell[0]}_{w.cell[1]}_{w.cell[2]}"); z.transform.SetParent(wroot);
                    var bc = z.AddComponent<BoxCollider>(); bc.isTrigger = true; z.layer = GridNavBuilder.TriggerLayer;
                    float top = w.pos[1] + 1.2f;
                    bc.center = new Vector3(w.pos[0], (w.bottom + top) / 2, w.pos[2]);
                    bc.size = new Vector3(b.cell, top - w.bottom, b.cell);
                    z.AddComponent<WaterZone>().surfaceY = w.pos[1];
                }
            }

            // стремянки: две жерди + перекладины из деревянного столба набора
            var post = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kits/Prefabs/Village/Corner_Exterior_Wood.prefab");
            var lroot = new GameObject("Ladders").transform; lroot.SetParent(root);
            foreach (var l in b.ladders ?? new LadderT[0])
            {
                var lad = new GameObject($"Ladder_{l.cell[0]}_{l.cell[1]}_L{l.cell[2]}").transform;
                lad.SetParent(lroot); lad.SetPositionAndRotation(V(l.pos), Quaternion.Euler(0, l.yaw, 0));
                var fe = lad.gameObject.AddComponent<FortElement>(); fe.kind = ElementKind.Ladder; fe.model = "Ladder(Corner_Exterior_Wood)";
                fe.cell = new Vector3Int(l.cell[0], l.cell[1], l.cell[2]);
                float h = l.water ? l.height + 0.9f : l.height;   // в люке жерди не торчат над полом
                foreach (float sx in new[] { -0.3f, 0.3f })
                {
                    var r = (GameObject)PrefabUtility.InstantiatePrefab(post, lad);
                    r.transform.localPosition = new Vector3(sx, 0, 0.06f); r.transform.localRotation = Quaternion.identity;
                    r.transform.localScale = new Vector3(0.45f, h / 3f, 0.45f);
                    foreach (var c in r.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                }
                for (float y = 0.3f; y < h - 0.2f; y += 0.32f)
                {
                    var r = (GameObject)PrefabUtility.InstantiatePrefab(post, lad);
                    r.transform.localPosition = new Vector3(0.3f, y, 0.06f); r.transform.localRotation = Quaternion.Euler(0, 0, 90);
                    r.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);
                    foreach (var c in r.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                }
                var zone = new GameObject("ClimbZone"); zone.transform.SetParent(lad, false); zone.layer = GridNavBuilder.TriggerLayer;
                var bc = zone.AddComponent<BoxCollider>(); bc.isTrigger = true;
                float hz = h + 1.0f;                                // зона выше пола — чтобы выйти наверх
                bc.center = new Vector3(0, hz / 2, 0.4f); bc.size = new Vector3(1.0f, hz, 0.8f);
                var lz = zone.AddComponent<LadderZone>();
                // верх — пол выхода; у цепочки стремянок в одной шахте — выход наверху всей цепочки
                float TopOf(LadderT o) => Mathf.Round((o.pos[1] + o.height) / b.storey) * b.storey;
                float top = TopOf(l);
                if (!l.water)
                    for (int kk = l.cell[2] + 1; ; kk++)
                    {
                        var up = b.ladders.FirstOrDefault(o => !o.water && o.cell[0] == l.cell[0] && o.cell[1] == l.cell[1] && o.cell[2] == kk);
                        if (up == null) break;
                        top = TopOf(up);
                    }
                lz.top = top; lz.bottom = l.pos[1];
                lz.line = lad.position; lz.face = lad.forward;
            }

            // свет
            var lt = new GameObject("Lights").transform; lt.SetParent(root);
            int shadowed = 0;
            foreach (var L in b.lights ?? new LightT[0])
            {
                var pos = V(L.pos);
                var owner = lightPieces.OrderBy(g => (g.transform.position - pos).sqrMagnitude).FirstOrDefault();
                var go = new GameObject("Light");
                if (owner && (owner.transform.position - pos).magnitude < 2.2f) go.transform.SetParent(owner.transform, true);
                else go.transform.SetParent(lt);
                go.transform.position = pos;
                var light = go.AddComponent<Light>(); light.type = LightType.Point;
                light.color = new Color(L.color[0], L.color[1], L.color[2]); light.intensity = L.intensity; light.range = L.range;
                light.shadows = L.shadows && shadowed++ < 8 ? LightShadows.Soft : LightShadows.None;
                if (go.transform.parent != lt && !go.transform.parent.GetComponent<LightToggle>())
                    go.transform.parent.gameObject.AddComponent<LightToggle>().lamp = light;
            }

            SetupAtmosphere(true);
            var navRoot = new GameObject("Nav").transform; navRoot.SetParent(root);
            foreach (var a in b.nav?.areas ?? new AreaT[0]) GridNavBuilder.AreaVolume(navRoot, V(a.pos), b.cell, a.area);
            CreateEscapeZones(b, root);
            GridNavBuilder.Bake(root, sceneName);
            CreateGuards(b, root);
            var spawn = V(b.spawn) + Vector3.up * 0.05f;
            var points = new List<Vector3> { spawn };
            foreach (var pt in b.points ?? new PointT[0]) points.Add(V(pt.pos) + Vector3.up * 0.05f);
            CreatePlayer(spawn, points.ToArray());

            Directory.CreateDirectory("Assets/Scenes");
            string path = "Assets/Scenes/" + sceneName + ".unity";
            EditorSceneManager.SaveScene(scene, path);
            if (!wip)
            {
                var list = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
                list.Insert(0, new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = list.ToArray();
            }
            Debug.Log($"[Grid] {loc}: блоков {made}, нет префаба {missing}, земли {b.ground?.Length}, воды {water.Length}, " +
                      $"стремянок {b.ladders?.Length}, огней {b.lights?.Length}, стражи {b.guards?.Length}, ошибок компилятора {b.errors} -> {path}");
        }

        /// <summary>Выходы из стен (метка побега) и финиш на внешнем берегу.</summary>
        static void CreateEscapeZones(BuildData b, Transform root)
        {
            var er = new GameObject("Escape").transform; er.SetParent(root);
            er.gameObject.AddComponent<EscapeLog>();
            foreach (var e in b.nav?.exits ?? new ExitT[0])
            {
                var z = new GameObject("Exit_" + e.name); z.transform.SetParent(er); z.layer = GridNavBuilder.TriggerLayer;
                z.transform.SetPositionAndRotation(V(e.pos) + Vector3.up * 1.5f, Quaternion.Euler(0, e.yaw, 0));
                var bc = z.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(2.0f, 3.2f, 1.2f);
                z.AddComponent<EscapeZone>().exitName = e.name;
            }
            if (b.nav?.finish == "border")
            {
                float x0 = b.origin[0], z0 = b.origin[1], x1 = x0 + b.size[0] * b.cell, z1 = z0 + b.size[1] * b.cell, c = b.cell;
                var boxes = new[]
                {
                    (new Vector3((x0 + x1) / 2, 1.5f, z0 + c / 2), new Vector3(x1 - x0, 3.4f, c)),
                    (new Vector3((x0 + x1) / 2, 1.5f, z1 - c / 2), new Vector3(x1 - x0, 3.4f, c)),
                    (new Vector3(x0 + c / 2, 1.5f, (z0 + z1) / 2), new Vector3(c, 3.4f, z1 - z0)),
                    (new Vector3(x1 - c / 2, 1.5f, (z0 + z1) / 2), new Vector3(c, 3.4f, z1 - z0)),
                };
                int i = 0;
                foreach (var (cen, size) in boxes)
                {
                    var z = new GameObject("Finish_" + i++); z.transform.SetParent(er); z.layer = GridNavBuilder.TriggerLayer;
                    z.transform.position = cen;
                    var bc = z.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = size;
                    z.AddComponent<EscapeZone>().finish = true;
                }
            }
        }

        static Material GuardMat(string name, Color c)
        {
            string p = LocMat + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m) return m;
            m = Fortress.Rendering.FortRender.NewLit(c, 0.25f);
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        /// <summary>Гарнизон из guards.txt: капсулы-заглушки (готовой модели стражи в паках нет) с агентом навмеша.</summary>
        static void CreateGuards(BuildData b, Transform root)
        {
            if (b.guards == null || b.guards.Length == 0) return;
            var gr = new GameObject("Guards").transform;
            var cloth = GuardMat("Guard", new Color(0.55f, 0.16f, 0.12f));
            var iron = GuardMat("GuardHelmet", new Color(0.22f, 0.22f, 0.24f));
            foreach (var g in b.guards)
            {
                var go = new GameObject($"Guard_{g.id}_{g.role}");
                go.transform.SetParent(gr);
                go.transform.SetPositionAndRotation(V(g.pos), Quaternion.LookRotation(Dir(g.face) == Vector3.zero ? Vector3.forward : Dir(g.face)));
                var body = new GameObject("Body").transform; body.SetParent(go.transform, false);
                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule); Object.DestroyImmediate(cap.GetComponent<Collider>());
                cap.transform.SetParent(body, false); cap.transform.localPosition = new Vector3(0, 0.9f, 0); cap.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                cap.GetComponent<MeshRenderer>().sharedMaterial = cloth;
                var helm = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(helm.GetComponent<Collider>());
                helm.transform.SetParent(body, false); helm.transform.localPosition = new Vector3(0, 1.72f, 0); helm.transform.localScale = new Vector3(0.46f, 0.28f, 0.46f);
                helm.GetComponent<MeshRenderer>().sharedMaterial = iron;
                var nose = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(nose.GetComponent<Collider>());
                nose.transform.SetParent(body, false); nose.transform.localPosition = new Vector3(0, 1.45f, 0.33f); nose.transform.localScale = new Vector3(0.28f, 0.1f, 0.2f);
                nose.GetComponent<MeshRenderer>().sharedMaterial = iron;
                bool sleeping = g.sleep != null && !string.IsNullOrEmpty(g.sleep.bed);
                if (sleeping)
                {   // лежит на своей кровати вдоль длинной стороны
                    var bed = root.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == g.sleep.bed);
                    if (bed)
                    {
                        var rs = bed.GetComponentsInChildren<Renderer>();
                        var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
                        Vector3 along = bb.extents.x > bb.extents.z ? Vector3.right : Vector3.forward;
                        go.transform.SetPositionAndRotation(new Vector3(bb.center.x, bb.max.y - 0.05f, bb.center.z), Quaternion.LookRotation(along));
                        body.localRotation = Quaternion.Euler(90f, 0, 0);
                        body.localPosition = new Vector3(0, 0.3f, -0.9f);
                    }
                }
                var col = go.AddComponent<CapsuleCollider>(); col.center = new Vector3(0, 0.9f, 0); col.height = 1.8f; col.radius = 0.35f;
                if (sleeping) { col.direction = 2; col.center = new Vector3(0, 0.35f, 0); }
                var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
                var ag = go.AddComponent<NavMeshAgent>();
                ag.agentTypeID = 0; ag.radius = 0.35f; ag.height = 1.8f; ag.speed = g.walk; ag.baseOffset = 0f;
                ag.avoidancePriority = g.role == "jailer" ? 60 : 50;
                if (sleeping) ag.enabled = false;
                var gp = go.AddComponent<GuardPatrol>();
                gp.guardId = g.id; gp.role = g.role; gp.walkSpeed = g.walk; gp.runSpeed = g.run; gp.loop = g.route != "back";
                gp.sleeping = sleeping; gp.body = body;
                foreach (var w in g.wps ?? new WpT[0])
                    gp.points.Add(new GuardPoint { pos = V(w.pos), wait = w.wait, look = w.look, sit = !string.IsNullOrEmpty(w.sit),
                                                   every = Mathf.Max(1, w.every), act = w.@do });
                // есть люди рига v3 — страж становится человеком (клипы Mixamo), заглушка уходит
                if (RigGuardNpc.Available) RigGuardNpc.Convert(go, g.k);
            }
        }

        static void QuadMesh(string name, Transform parent, List<(Vector3 c, float half)> quads, Material mat, bool collide,
                             ShadowCastingMode shadows = ShadowCastingMode.On)
        {
            if (quads.Count == 0) return;
            const int perMesh = 15000;
            for (int start = 0, part = 0; start < quads.Count; start += perMesh, part++)
            {
                var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                foreach (var (c, h) in quads.Skip(start).Take(perMesh))
                {
                    int i = vs.Count;
                    vs.Add(c + new Vector3(-h, 0, -h)); vs.Add(c + new Vector3(-h, 0, h)); vs.Add(c + new Vector3(h, 0, h)); vs.Add(c + new Vector3(h, 0, -h));
                    foreach (var v in vs.Skip(i)) uv.Add(new Vector2(v.x, v.z) / 4f);
                    tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                }
                MakeMesh(name + (part > 0 ? "_" + part : ""), parent, vs, uv, tri, mat, collide, shadows);
            }
        }

        static void RectMesh(string name, Transform parent, List<(Vector3 a, Vector3 b)> rects, Material mat)
        {
            var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (var (a, b) in rects)
            {
                int i = vs.Count;
                vs.Add(new Vector3(a.x, a.y, a.z)); vs.Add(new Vector3(a.x, a.y, b.z)); vs.Add(new Vector3(b.x, a.y, b.z)); vs.Add(new Vector3(b.x, a.y, a.z));
                foreach (var v in vs.Skip(i)) uv.Add(new Vector2(v.x, v.z) / 4f);
                tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            MakeMesh(name, parent, vs, uv, tri, mat, true, ShadowCastingMode.On);
        }

        static void MakeMesh(string name, Transform parent, List<Vector3> vs, List<Vector2> uv, List<int> tri, Material mat, bool collide, ShadowCastingMode shadows)
        {
            var me = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            me.SetVertices(vs); me.SetUVs(0, uv); me.SetTriangles(tri, 0); me.RecalculateNormals(); me.RecalculateTangents(); me.RecalculateBounds();
            Directory.CreateDirectory("Assets/Location/Generated");
            string p = "Assets/Location/Generated/" + EditorSceneManager.GetActiveScene().name + "_" + name + ".asset";
            AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(me, p);
            var go = new GameObject(name); go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = me;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = shadows;
            if (collide) go.AddComponent<MeshCollider>().sharedMesh = me;
            var fe = go.AddComponent<FortElement>(); fe.kind = name.StartsWith("Water") ? ElementKind.Water : ElementKind.Ground; fe.model = name;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        [MenuItem("Fortress/Grid/Atmosphere: dusk (stealth)")] static void AtmoDusk() => SetupAtmosphere(true);
        [MenuItem("Fortress/Grid/Atmosphere: evening (bright, as before)")] static void AtmoDay() => SetupAtmosphere(false);

        static void SetupAtmosphere(bool dusk)
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.name.StartsWith("Sun")) Object.DestroyImmediate(l.gameObject);
            var sun = new GameObject(dusk ? "Sun (dusk)" : "Sun (evening)").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1f, 0.78f, 0.58f); sun.intensity = 1.3f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(38f, -128f, 0f);
            if (dusk)
            {   // низкое солнце с запада, тени длинные; огонь в 6-8 раз ярче тени — свет и тень читаются глазом
                // HDRP: солнце 16° — закат красит физическое небо, тени длинные, но двор не тонет в темноте
                sun.color = new Color(1f, 0.74f, 0.52f); sun.intensity = 1.25f; sun.transform.rotation = Quaternion.Euler(16f, 95f, 0f);
            }
            var sky = AssetDatabase.LoadAssetAtPath<Material>(LocMat + "DuskSky.mat");
            if (sky) RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.62f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.48f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.26f, 0.22f);
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.46f, 0.43f, 0.4f));
            sh.AddDirectionalLight(Vector3.up, new Color(0.22f, 0.25f, 0.32f), 1f);
            RenderSettings.ambientProbe = sh;
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LocMat + "FortressLighting.lighting");
            if (ls) Lightmapping.lightingSettings = ls;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.36f, 0.4f, 0.5f); RenderSettings.fogDensity = 0.006f;
            if (dusk)
            {
                RenderSettings.ambientSkyColor = new Color(0.24f, 0.28f, 0.4f);
                RenderSettings.ambientEquatorColor = new Color(0.17f, 0.16f, 0.18f);
                RenderSettings.ambientGroundColor = new Color(0.07f, 0.065f, 0.06f);
                var shd = new SphericalHarmonicsL2();
                shd.AddAmbientLight(new Color(0.17f, 0.17f, 0.2f));
                shd.AddDirectionalLight(Vector3.up, new Color(0.08f, 0.1f, 0.15f), 1f);
                RenderSettings.ambientProbe = shd;
                RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogColor = new Color(0.17f, 0.19f, 0.26f); RenderSettings.fogDensity = 0.01f;
            }
            // HDRP: солнце в люксах, небо/туман/экспозиция — профиль из RenderSettings выше
            Fortress.Rendering.FortRender.SetIntensity(sun, sun.intensity);
            HdrpSetup.RebuildAtmosphere();
        }

        static void CreatePlayer(Vector3 spawn, Vector3[] points)
        {
            var player = new GameObject("Player");
            player.transform.position = spawn;
            var cc = player.AddComponent<CharacterController>();
            var obst = player.AddComponent<NavMeshObstacle>();   // стража обходит игрока (без вырезания навмеша)
            obst.shape = NavMeshObstacleShape.Capsule; obst.radius = 0.35f; obst.height = 1.8f; obst.center = new Vector3(0, 0.9f, 0); obst.carving = false;
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.45f; cc.slopeLimit = 50f; cc.skinWidth = 0.04f;
            var bodyPivot = new GameObject("Body").transform; bodyPivot.SetParent(player.transform, false);
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule); Object.DestroyImmediate(capsule.GetComponent<Collider>());
            capsule.name = "Capsule"; capsule.transform.SetParent(bodyPivot, false);
            capsule.transform.localPosition = new Vector3(0, 0.9f, 0); capsule.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            capsule.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(LocMat + "Player.mat");
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.name = "Facing"; nose.transform.SetParent(bodyPivot, false);
            nose.transform.localPosition = new Vector3(0, 1.45f, 0.33f); nose.transform.localScale = new Vector3(0.3f, 0.12f, 0.2f);
            nose.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(LocMat + "PlayerFacing.mat");
            var lamp = new GameObject("CarryLight").AddComponent<Light>(); lamp.transform.SetParent(player.transform, false);
            lamp.transform.localPosition = new Vector3(0, 2.2f, 0); lamp.type = LightType.Point; lamp.range = 6f; lamp.intensity = 0.6f;
            lamp.color = new Color(1f, 0.8f, 0.6f); lamp.shadows = LightShadows.None;

            var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.05f; cam.farClipPlane = 900f; cam.fieldOfView = 62f;
            camGo.AddComponent<AudioListener>();
            var orbit = camGo.AddComponent<OrbitCamera>(); orbit.target = player.transform;
            var walker = player.AddComponent<PlayerWalker>();
            walker.cameraTransform = camGo.transform; walker.body = bodyPivot; walker.spawn = spawn; walker.points = points;
            new GameObject("HUD").AddComponent<WalkerHUD>().player = walker;
            RigPlayerBody.Attach(player);      // тело человека вместо капсулы (риг v3)
        }
    }
}
