using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fortress.Walker;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Сборка сцены-бродилки из Assets/Location/Fortress.fbx (экспорт из Blender, см. D:/Metroidvany/blender/export_unity.py).
/// Меню: Fortress / Build Walker Scene, Fortress / Capture Check Views.
/// </summary>
public static class FortressSetup
{
    const string FbxPath = "Assets/Location/Fortress.fbx";
    const string ScenePath = "Assets/Scenes/Fortress.unity";
    const string MatDir = "Assets/Location/Materials";

    // ---------------------------------------------------------------- Blender -> Unity по маркерам
    static Matrix4x4 b2u = Matrix4x4.identity;

    static readonly (string name, Vector3 blender)[] Anchors =
    {
        ("MK_START", new Vector3(-33f, 9.6f, -5.9f)),
        ("MK_TP_chimney", new Vector3(-14f, 16f, 0.2f)),
        ("VOL_WATER_N", new Vector3(0f, 41f, -3.65f)),
        ("VOL_WATER_E", new Vector3(51f, -2f, -3.65f)),
    };

    static void SolveMapping(Dictionary<string, Bounds> markers)
    {
        var B = new Matrix4x4(); var U = new Matrix4x4();
        for (int i = 0; i < 4; i++)
        {
            Vector3 b = Anchors[i].blender, u = markers[Anchors[i].name].center;
            B.SetColumn(i, new Vector4(b.x, b.y, b.z, 1)); U.SetColumn(i, new Vector4(u.x, u.y, u.z, 1));
        }
        b2u = U * B.inverse;
    }

    static Vector3 BP(float x, float y, float z) => b2u.MultiplyPoint3x4(new Vector3(x, y, z));
    static Vector3 BD(float x, float y, float z) => b2u.MultiplyVector(new Vector3(x, y, z)).normalized;

    // ---------------------------------------------------------------- материалы
    static Material Mat(string name, Color color, float smooth = 0.2f, Color? emission = null)
    {
        Directory.CreateDirectory(MatDir);
        string p = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh); AssetDatabase.CreateAsset(m, p);
        }
        m.SetColor("_BaseColor", color); m.color = color;
        m.SetFloat("_Smoothness", smooth);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------------------------------------------------------------- сборка
    [MenuItem("Fortress/Build Walker Scene")]
    public static void Build()
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        if (imp == null) { Debug.LogError("[Fortress] нет " + FbxPath); return; }
        bool dirty = imp.importCameras || imp.importLights || imp.animationType != ModelImporterAnimationType.None
                     || imp.globalScale != 1f || !imp.useFileScale || imp.materialLocation != ModelImporterMaterialLocation.External;
        imp.importCameras = false; imp.importLights = false; imp.animationType = ModelImporterAnimationType.None;
        imp.importAnimation = false; imp.globalScale = 1f; imp.useFileScale = true;
        // текстуры зашиты в FBX: распаковать, материалы — отдельными файлами (чтобы править)
        const string texDir = "Assets/Location/Textures";
        if (!Directory.Exists(texDir) || Directory.GetFiles(texDir, "*.png").Length == 0)
        {
            Directory.CreateDirectory(texDir);
            imp.ExtractTextures(texDir);
            AssetDatabase.Refresh();
            dirty = true;
        }
        // карты нормалей кита распаковались как цветные текстуры -> синие тени: пометить как Normal Map
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
        {
            var tp = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileNameWithoutExtension(tp).ToLowerInvariant().Contains("normal")) continue;
            var ti = (TextureImporter)AssetImporter.GetAtPath(tp);
            if (ti != null && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); dirty = true;
            }
        }
        imp.materialLocation = ModelImporterMaterialLocation.External;
        imp.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        imp.materialSearch = ModelImporterMaterialSearch.Everywhere;
        if (dirty) imp.SaveAndReimport();
        FixCutoutMaterials();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var fort = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        fort.name = "Fortress";
        PrefabUtility.UnpackPrefabInstance(fort, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        Transform Find(string n) => fort.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
        var rc = Find("ROOT_COLLIDE"); var rn = Find("ROOT_NOCOLLIDE"); var rw = Find("ROOT_WATER"); var rm = Find("ROOT_MARKERS");

        // коллизии: вся статика, кроме мелочи < 0.6 м
        int cols = 0, skipped = 0;
        foreach (var mf in rc.GetComponentsInChildren<MeshFilter>(true))
        {
            var r = mf.GetComponent<MeshRenderer>();
            var s = r ? r.bounds.size : Vector3.one;
            if (Mathf.Max(s.x, Mathf.Max(s.y, s.z)) < 0.6f) { skipped++; continue; }
            var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; cols++;
            GameObjectUtility.SetStaticEditorFlags(mf.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }
        // вода и круг
        var waterMat = Mat("Water", new Color(0.05f, 0.12f, 0.11f), 0.92f);
        foreach (var r in rw.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = waterMat;
        var circle = Find("MAGIC_CIRCLE_EXPORT");
        if (circle) circle.GetComponent<MeshRenderer>().sharedMaterial = Mat("MagicCircle", new Color(0.2f, 0.85f, 1f), 0.5f, new Color(0.3f, 1.6f, 2.2f));

        // маркеры -> зоны, свет, точки
        var markers = new Dictionary<string, Bounds>();
        foreach (Transform t in rm) { var r = t.GetComponent<MeshRenderer>(); if (r) markers[t.name] = r.bounds; }
        SolveMapping(markers);
        var gameplay = new GameObject("Gameplay").transform;
        var lights = new GameObject("Lights").transform;
        int lightCount = 0;
        foreach (var kv in markers)
        {
            string n = kv.Key; Bounds b = kv.Value;
            GameObject Zone(string zname)
            {
                var go = new GameObject(zname); go.transform.SetParent(gameplay); go.transform.position = b.center;
                var bc = go.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = b.size; return go;
            }
            if (n.StartsWith("VOL_LADDER")) Zone("Ladder_" + n.Substring(11)).AddComponent<LadderZone>();
            else if (n.StartsWith("VOL_WATER")) Zone("Water_" + n.Substring(10)).AddComponent<WaterZone>().surfaceY = b.max.y - 0.05f;
            else if (n.StartsWith("VOL_BREAK")) Zone("Break_" + n.Substring(10)).AddComponent<BreakZone>();
            else if (n.StartsWith("VOL_TP_"))
            {
                string key = n.Substring(7);
                var tz = Zone("Chimney_" + key).AddComponent<TeleportZone>();
                if (markers.TryGetValue("MK_TP_" + key, out var tb)) tz.target = tb.center + Vector3.up * 0.2f;
            }
            else if (n.StartsWith("LT_"))
            {
                var parts = n.Split('_');
                if (parts.Length >= 5 && float.TryParse(parts[1], out var watts))
                {
                    var go = new GameObject("Light_" + lightCount++); go.transform.SetParent(lights); go.transform.position = b.center;
                    var L = go.AddComponent<Light>(); L.type = LightType.Point;
                    L.color = new Color(int.Parse(parts[2]) / 255f, int.Parse(parts[3]) / 255f, int.Parse(parts[4].Split('.')[0]) / 255f);
                    L.intensity = Mathf.Clamp(watts / 90f, 0.6f, 14f);
                    L.range = Mathf.Clamp(Mathf.Sqrt(watts) * 0.85f, 4f, 32f);
                    L.shadows = watts >= 600 ? LightShadows.Soft : LightShadows.None;
                }
            }
        }
        Vector3 spawn = markers.ContainsKey("MK_START") ? BP(-32.2f, 9.0f, -5.85f) : Vector3.zero;   // свободный пол камеры №1, рядом с лежаком
        Object.DestroyImmediate(rm.gameObject);

        // свет и атмосфера: сумерки
        var sun = new GameObject("Sun (dusk)").AddComponent<Light>();
        sun.type = LightType.Directional; sun.color = new Color(1f, 0.76f, 0.55f); sun.intensity = 1.25f; sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.LookRotation(BD(0.7f, 0.55f, -0.42f), Vector3.up);
        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetColor("_SkyTint", new Color(0.42f, 0.5f, 0.72f)); sky.SetColor("_GroundColor", new Color(0.25f, 0.24f, 0.22f));
        sky.SetFloat("_AtmosphereThickness", 1.3f); sky.SetFloat("_Exposure", 1.1f);
        AssetDatabase.CreateAsset(sky, $"{MatDir}/DuskSky.mat");
        RenderSettings.skybox = sky; RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.58f, 0.62f, 0.74f);
        RenderSettings.ambientEquatorColor = new Color(0.52f, 0.48f, 0.44f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.26f, 0.22f);
        // фоновый свет без запечённых данных берётся из стандартного синего неба: задать явно и запечь только окружение
        var sh = new SphericalHarmonicsL2();
        sh.AddAmbientLight(new Color(0.46f, 0.43f, 0.4f));
        sh.AddDirectionalLight(Vector3.up, new Color(0.22f, 0.25f, 0.32f), 1f);
        RenderSettings.ambientProbe = sh;
        var ls = new LightingSettings { name = "FortressLighting", bakedGI = false, realtimeGI = false };
        AssetDatabase.CreateAsset(ls, $"{MatDir}/FortressLighting.lighting");
        Lightmapping.lightingSettings = ls;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.36f, 0.4f, 0.5f); RenderSettings.fogDensity = 0.006f;

        // персонаж: капсула-заглушка с «носом», CharacterController
        var player = new GameObject("Player");
        player.transform.position = spawn;
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.45f; cc.slopeLimit = 50f; cc.skinWidth = 0.04f;
        var bodyPivot = new GameObject("Body").transform; bodyPivot.SetParent(player.transform, false);
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule); Object.DestroyImmediate(capsule.GetComponent<Collider>());
        capsule.name = "Capsule"; capsule.transform.SetParent(bodyPivot, false);
        capsule.transform.localPosition = new Vector3(0, 0.9f, 0); capsule.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
        capsule.GetComponent<MeshRenderer>().sharedMaterial = Mat("Player", new Color(0.85f, 0.45f, 0.18f), 0.3f);
        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(nose.GetComponent<Collider>());
        nose.name = "Facing"; nose.transform.SetParent(bodyPivot, false);
        nose.transform.localPosition = new Vector3(0, 1.45f, 0.33f); nose.transform.localScale = new Vector3(0.3f, 0.12f, 0.2f);
        nose.GetComponent<MeshRenderer>().sharedMaterial = Mat("PlayerFacing", new Color(0.15f, 0.12f, 0.1f));
        var lamp = new GameObject("CarryLight").AddComponent<Light>(); lamp.transform.SetParent(player.transform, false);
        lamp.transform.localPosition = new Vector3(0, 2.2f, 0); lamp.type = LightType.Point; lamp.range = 7f; lamp.intensity = 0.8f;
        lamp.color = new Color(1f, 0.8f, 0.6f); lamp.shadows = LightShadows.None;

        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.05f; cam.farClipPlane = 900f; cam.fieldOfView = 62f;
        camGo.AddComponent<AudioListener>();
        var orbit = camGo.AddComponent<OrbitCamera>(); orbit.target = player.transform;
        var walker = player.AddComponent<PlayerWalker>();
        walker.cameraTransform = camGo.transform; walker.body = bodyPivot; walker.spawn = spawn;
        walker.points = new[]
        {
            spawn,
            BP(-27f, 14f, 0.1f),     // 2 зал
            BP(-6f, -2f, 0.1f),      // 3 двор у колодца
            BP(20f, -27f, 9.2f),     // 4 обход по стене (юг)
            BP(-26f, 12f, 25.1f),    // 5 крыша донжона, жаровня
            BP(-28f, -16.5f, -5.9f), // 6 дно каменоломни (мимо тележки)
        };
        // камера смотрит из камеры №1 на решётку
        orbit.yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(BD(0f, 1f, 0f), Vector3.up)).eulerAngles.y;
        new GameObject("HUD").AddComponent<WalkerHUD>().player = walker;

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Lightmapping.Bake();                      // только окружение (GI выключено) — фоновый свет и отражения
        RenderSettings.ambientProbe = sh;
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log($"[Fortress] сцена собрана: коллайдеров {cols}, мелочь без коллайдера {skipped}, огней {lightCount}, старт {spawn}");
    }

    /// <summary>Листва, трава, плющ, знамёна, рожь: альфа-отсечение и двусторонность.</summary>
    static void FixCutoutMaterials()
    {
        string[] keys = { "leaves", "plant", "vine", "banner", "rye", "grass", "bush", "fern" };
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Location" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) continue;
            string low = m.name.ToLowerInvariant();
            // импорт FBX ставит металличность/глянец -> бирюзовые отражения неба: делаем всё матовым
            if (!new[] { "water", "magiccircle", "player", "playerfacing", "duskysky", "dusksky" }.Contains(low))
            {
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", low.Contains("metal") || low.Contains("iron") ? 0.35f : 0.12f);
                if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
                m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                EditorUtility.SetDirty(m);
            }
            bool cut = keys.Any(k => low.Contains(k));
            if (!cut && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") is Texture2D t)
                cut = keys.Any(k => t.name.ToLowerInvariant().Contains(k));
            if (!cut || !m.HasProperty("_AlphaClip")) continue;
            m.SetFloat("_AlphaClip", 1f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cutoff", 0.45f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.renderQueue = (int)RenderQueue.AlphaTest; EditorUtility.SetDirty(m); n++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Fortress] материалов с альфа-отсечением: {n}");
    }

    /// <summary>Для пакетного режима: собрать сцену и сделать контрольные снимки.</summary>
    public static void BuildAndCapture()
    {
        Build();
        CaptureViews();
    }

    /// <summary>Проверка проходимости точек: пол-коллайдер под ногами и свободная капсула персонажа.</summary>
    [MenuItem("Fortress/Check Walk Points")]
    public static void CheckPoints()
    {
        var w = Object.FindFirstObjectByType<PlayerWalker>();
        if (w == null) { Debug.LogError("[Fortress] нет Player в сцене"); return; }
        string[] names = { "камера №1", "зал", "двор", "стена", "крыша", "каторга" };
        int ok = 0;
        for (int i = 0; i < w.points.Length; i++)
        {
            Vector3 p = w.points[i];
            bool floor = Physics.Raycast(p + Vector3.up * 1.0f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore);
            Vector3 feet = floor ? hit.point : p;
            bool stuck = Physics.CheckCapsule(feet + Vector3.up * 0.45f, feet + Vector3.up * 1.45f, 0.33f, ~0, QueryTriggerInteraction.Ignore);
            bool good = floor && !stuck;
            if (good) ok++;
            Debug.Log($"[Fortress] точка {i + 1} {names[i]}: пол {(floor ? hit.collider.name + $" ({hit.distance - 1f:F2} м)" : "НЕТ")}, капсула {(stuck ? "ЗАСТРЯЛА" : "свободна")} -> {(good ? "OK" : "ПРОБЛЕМА")}");
        }
        int zones = Object.FindObjectsByType<LadderZone>(FindObjectsSortMode.None).Length;
        int water = Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None).Length;
        int cols = Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Length;
        Debug.Log($"[Fortress] итого точек OK {ok}/{w.points.Length}; стремянок {zones}, зон воды {water}, меш-коллайдеров {cols}");
    }

    // ---------------------------------------------------------------- контрольные снимки
    [MenuItem("Fortress/Capture Check Views")]
    public static void CaptureViews()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var markersOk = RecoverMapping();
        if (!markersOk) { Debug.LogError("[Fortress] нет точек для снимков"); return; }
        string dir = "D:/Metroidvany/renders";
        Shot($"{dir}/unity_01_yard.png", BP(14, -14, 3.2f), BP(-24, 14, 9));
        Shot($"{dir}/unity_02_hall.png", BP(-21, 9, 2.4f), BP(-32, 21, 1.6f));
        Shot($"{dir}/unity_03_prison.png", BP(-18.6f, 18.5f, -4f), BP(-30, 9.5f, -5.6f));
        Shot($"{dir}/unity_04_exterior.png", BP(78, -96, 62), BP(-6, 2, 4));
        var player = GameObject.Find("Player");
        var cam = Camera.main;
        if (player && cam)
        {
            var orbit = cam.GetComponent<OrbitCamera>();
            Vector3 pivot = player.transform.position + Vector3.up * 1.6f;
            Vector3 back = Quaternion.Euler(orbit.pitch, orbit.yaw, 0) * Vector3.back;
            float d = 3.5f;
            if (Physics.SphereCast(pivot, 0.22f, back, out var hit, d, ~0, QueryTriggerInteraction.Ignore)) d = Mathf.Max(0.3f, hit.distance - 0.05f);
            Shot($"{dir}/unity_05_start_cell.png", pivot + back * d, pivot + Quaternion.Euler(orbit.pitch, orbit.yaw, 0) * Vector3.forward * 4f);
        }
        Debug.Log("[Fortress] снимки сохранены в " + dir);
    }

    static bool RecoverMapping()
    {
        var w = Object.FindFirstObjectByType<PlayerWalker>();
        if (w == null || w.points == null || w.points.Length < 6) return false;
        // восстановить преобразование по сохранённым точкам (старт, зал, двор, стена)
        Vector3[] b = { new Vector3(-32.2f, 9.0f, -5.85f), new Vector3(-27f, 14f, 0.1f), new Vector3(-6f, -2f, 0.1f), new Vector3(20f, -27f, 9.2f) };
        var B = new Matrix4x4(); var U = new Matrix4x4();
        for (int i = 0; i < 4; i++)
        {
            B.SetColumn(i, new Vector4(b[i].x, b[i].y, b[i].z, 1));
            var u = w.points[i]; U.SetColumn(i, new Vector4(u.x, u.y, u.z, 1));
        }
        b2u = U * B.inverse;
        return true;
    }

    public static void Shot(string path, Vector3 pos, Vector3 look)
    {
        var go = new GameObject("_shot"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
        cam.fieldOfView = 62f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 900f;
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        var req = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req); else cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = null;
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
    }
}
