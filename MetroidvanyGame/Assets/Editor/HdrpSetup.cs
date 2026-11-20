using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fortress.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Fortress.EditorTools
{
    /// <summary>Перенос на HDRP и поддержка сцен в нём.
    ///
    /// 1) Setup HDRP — ассет конвейера, качество, линейный цвет.
    /// 2) Convert materials — материалы URP Lit и Fortress/* (сохранённые свойства URP) → HDRP Lit / Fortress HDRP.
    /// 3) Convert all scenes — каждая сцена: свет в HDRP-шкале, данные камер, атмосфера, грязь в стыках.
    ///
    /// Атмосфера сцены берётся из её RenderSettings (сборщики по-прежнему пишут туда фон и туман): градиентное небо =
    /// фоновый свет (тройной или ровный), туман = туман RenderSettings (плюс объёмный — свет факелов в дымке),
    /// постоянная экспозиция FortRender.ExposureEV, на улице — физическое небо от солнца, нейтральный тонмаппинг и лёгкая цветокоррекция, SSAO, контактные тени, отражения, лёгкое свечение.
    /// При каждом сохранении сцены (sceneSaving) недостающее досоздаётся само, а свет в URP-шкале (меньше 60) пересчитывается —
    /// сборщики могут ставить привычные числа.</summary>
    [InitializeOnLoad]
    public static class HdrpSetup
    {
        const string Dir = "Assets/Settings/HDRP";
        const string AssetPath = Dir + "/HDRP_Fortress.asset";
        const string ProfileDir = Dir + "/Scenes";
        const string GrimeMat = "Assets/Kits/Materials/CreaseGrime.mat";
        const string AtmoName = "HDRP Atmosphere", GrimeName = "Crease Grime";
        const string UrpLitGuid = "933532a4fcc9baf4fa0491de14d08ed7";
        /// <summary>Свет слабее — значит, задан в URP-шкале (в HDRP при EV 9 такой не виден вовсе).</summary>
        const float UrpScaleLimit = 60f;

        static HdrpSetup()
        {
            EditorSceneManager.sceneSaving += (scene, path) =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || !Active) return;
                ConvertScene(scene, false, quiet: true);
            };
        }

        static bool Active => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset;

        // ------------------------------------------------------------------ конвейер

        [MenuItem("Fortress/Render/1 Setup HDRP (asset + quality)")]
        public static void SetupPipeline()
        {
            Directory.CreateDirectory(Dir);
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(AssetPath);
            if (!asset)
            {
                asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            var s = asset.currentPlatformRenderPipelineSettings;
            s.supportSSAO = true;
            s.supportSSR = true;
            s.supportVolumetrics = true;
            s.supportCustomPass = true;
            s.supportMotionVectors = true;
            asset.currentPlatformRenderPipelineSettings = s;
            EditorUtility.SetDirty(asset);

            PlayerSettings.colorSpace = ColorSpace.Linear;
            GraphicsSettings.defaultRenderPipeline = asset;
            // у каждого уровня качества — этот же ассет (раньше были PC/Mobile URP)
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (qs)
            {
                var so = new SerializedObject(qs);
                var arr = so.FindProperty("m_QualitySettings");
                for (int i = 0; i < arr.arraySize; i++)
                {
                    var rp = arr.GetArrayElementAtIndex(i).FindPropertyRelative("customRenderPipeline");
                    if (rp != null) rp.objectReferenceValue = asset;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[HDRP] конвейер: " + AssetPath);
        }

        // ------------------------------------------------------------------ материалы

        class Saved
        {
            public readonly Dictionary<string, float> f = new();
            public readonly Dictionary<string, Color> c = new();
            public readonly Dictionary<string, (Texture t, Vector2 scale, Vector2 offset)> t = new();
            public readonly HashSet<string> kw = new();
        }

        static Saved Read(Material m)
        {
            var r = new Saved();
            var so = new SerializedObject(m);
            var sp = so.FindProperty("m_SavedProperties");
            var te = sp.FindPropertyRelative("m_TexEnvs");
            for (int i = 0; i < te.arraySize; i++)
            {
                var e = te.GetArrayElementAtIndex(i);
                var v = e.FindPropertyRelative("second");
                r.t[e.FindPropertyRelative("first").stringValue] = (v.FindPropertyRelative("m_Texture").objectReferenceValue as Texture,
                    v.FindPropertyRelative("m_Scale").vector2Value, v.FindPropertyRelative("m_Offset").vector2Value);
            }
            var fl = sp.FindPropertyRelative("m_Floats");
            for (int i = 0; i < fl.arraySize; i++)
            {
                var e = fl.GetArrayElementAtIndex(i);
                r.f[e.FindPropertyRelative("first").stringValue] = e.FindPropertyRelative("second").floatValue;
            }
            var co = sp.FindPropertyRelative("m_Colors");
            for (int i = 0; i < co.arraySize; i++)
            {
                var e = co.GetArrayElementAtIndex(i);
                r.c[e.FindPropertyRelative("first").stringValue] = e.FindPropertyRelative("second").colorValue;
            }
            foreach (var name in new[] { "m_ValidKeywords", "m_InvalidKeywords" })
            {
                var k = so.FindProperty(name);
                if (k == null) continue;
                for (int i = 0; i < k.arraySize; i++) r.kw.Add(k.GetArrayElementAtIndex(i).stringValue);
            }
            return r;
        }

        [MenuItem("Fortress/Render/2 Convert materials (URP → HDRP)")]
        public static void ConvertMaterials()
        {
            var lit = FortRender.Lit;
            if (!lit) { Debug.LogError("[HDRP] нет шейдера HDRP/Lit — пакет HDRP не установлен?"); return; }
            int n = 0, skipped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) continue;
                bool urpLit = File.ReadAllText(path).Contains("guid: " + UrpLitGuid);
                bool fort = FortRender.IsFortress(m.shader);
                if (!urpLit && !fort) continue;
                var s = Read(m);
                if (fort && (s.t.ContainsKey("_BaseColorMap") || !s.t.ContainsKey("_BaseMap"))) { skipped++; continue; }   // уже HDRP
                if (urpLit) m.shader = lit;
                Apply(m, s, worldUV: m.shader.name == FortRender.WorldUVName);
                EditorUtility.SetDirty(m);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[HDRP] материалов переведено: {n}, уже были HDRP: {skipped}");
        }

        static void Apply(Material m, Saved s, bool worldUV)
        {
            if (s.t.TryGetValue("_BaseMap", out var bm))
            {
                FortRender.SetBaseMap(m, bm.t);
                m.SetTextureScale("_BaseColorMap", bm.scale);
                m.SetTextureOffset("_BaseColorMap", bm.offset);
            }
            if (s.c.TryGetValue("_BaseColor", out var col)) FortRender.SetColor(m, col);
            if (s.t.TryGetValue("_BumpMap", out var nm) && nm.t)
                FortRender.SetNormalMap(m, nm.t, s.f.TryGetValue("_BumpScale", out var bs) ? bs : 1f);
            if (s.f.TryGetValue("_Smoothness", out var sm)) FortRender.SetSmoothness(m, sm);
            if (s.f.TryGetValue("_Metallic", out var mt)) FortRender.SetMetallic(m, mt);
            if (s.kw.Contains("_EMISSION") && s.c.TryGetValue("_EmissionColor", out var em) && em.maxColorComponent > 0.001f)
                FortRender.SetEmission(m, em);
            if (s.f.TryGetValue("_AlphaClip", out var ac) && ac > 0.5f)
                FortRender.SetAlphaClip(m, s.f.TryGetValue("_Cutoff", out var cut) ? cut : 0.5f);
            bool transparent = s.f.TryGetValue("_Surface", out var surf) && surf > 0.5f;
            FortRender.SetTransparent(m, transparent);
            FortRender.SetDoubleSided(m, s.f.TryGetValue("_Cull", out var cull) && cull < 0.5f);
            FortRender.Validate(m);
        }

        // ------------------------------------------------------------------ сцены

        [MenuItem("Fortress/Render/3 Convert all scenes")]
        public static void ConvertAllScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var first = SceneManager.GetActiveScene().path;
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                ConvertScene(scene, true, quiet: false);
                EditorSceneManager.SaveScene(scene);
            }
            if (!string.IsNullOrEmpty(first)) EditorSceneManager.OpenScene(first, OpenSceneMode.Single);
        }

        [MenuItem("Fortress/Render/Convert open scene")]
        public static void ConvertOpenScene()
        {
            var scene = SceneManager.GetActiveScene();
            ConvertScene(scene, false, quiet: false);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        [MenuItem("Fortress/Render/Rebuild atmosphere from RenderSettings (open scene)")]
        public static void RebuildAtmosphere()
        {
            var scene = SceneManager.GetActiveScene();
            ConvertScene(scene, true, quiet: false);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>Сцена под HDRP: убрать пропавшие компоненты URP, камеры и свет — с данными HDRP, свет в URP-шкале
        /// пересчитать, атмосферу (если нет или rebuildAtmosphere) и грязь в стыках — досоздать.</summary>
        public static void ConvertScene(Scene scene, bool rebuildAtmosphere, bool quiet)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            int missing = 0, lights = 0, sceneMats = 0;
            var convertedInScene = new HashSet<Material>();
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    missing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (var root in roots)
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                    if (!cam.GetComponent<HDAdditionalCameraData>()) cam.gameObject.AddComponent<HDAdditionalCameraData>();
                // материалы, созданные сборщиками прямо в сцене (не ассеты): шейдер URP пропал → HDRP/Lit по сохранённым свойствам
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (!m || AssetDatabase.Contains(m) || !convertedInScene.Add(m)) continue;
                        if (m.shader && !m.shader.name.Contains("InternalError")) continue;
                        var s = Read(m);
                        m.shader = FortRender.Lit;
                        Apply(m, s, false);
                        sceneMats++;
                    }
                foreach (var l in root.GetComponentsInChildren<Light>(true))
                {
                    if (!l.GetComponent<HDAdditionalLightData>()) l.gameObject.AddComponent<HDAdditionalLightData>();
                    if (l.intensity < UrpScaleLimit) { FortRender.SetIntensity(l, l.intensity); lights++; }
                }
                // объёмы с профилями URP (их эффекты пропали вместе с пакетом)
                foreach (var v in root.GetComponentsInChildren<Volume>(true))
                    if (v.gameObject.name != AtmoName && v.sharedProfile && v.sharedProfile.components.Any(c => c == null))
                        Object.DestroyImmediate(v);
            }

            bool active = scene == SceneManager.GetActiveScene();
            var atmo = Find(scene, AtmoName);
            if (active && (!atmo || rebuildAtmosphere || !atmo.GetComponent<Volume>()?.sharedProfile))
                BuildAtmosphere(scene, atmo);
            if (!Find(scene, GrimeName)) BuildGrime(scene);
            if (!quiet) Debug.Log($"[HDRP] {scene.name}: пропавших компонентов URP убрано {missing}, свет пересчитан {lights}, материалов сцены {sceneMats}");
        }

        static GameObject Find(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

        static void BuildAtmosphere(Scene scene, GameObject go)
        {
            if (!go)
            {
                go = new GameObject(AtmoName);
                SceneManager.MoveGameObjectToScene(go, scene);
            }
            var vol = go.GetComponent<Volume>() ?? go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0;

            Directory.CreateDirectory(ProfileDir);
            string path = $"{ProfileDir}/{(string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name)}_Atmosphere.asset";
            // фоновый свет: цвета тройного (небо/горизонт/земля) или ровного фона RenderSettings — числа берутся как
            // есть (URP давал их зондом без перевода из гаммы). Градиентное небо тех же цветов. Если сцена уже на
            // «Skybox» (HDRP сам переключает) — оставить цвета прежнего профиля.
            Color top, mid, bot;
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            GradientSky oldSky = null;
            if (old) old.TryGet(out oldSky);
            if (RenderSettings.ambientMode == AmbientMode.Trilight)
            { top = RenderSettings.ambientSkyColor; mid = RenderSettings.ambientEquatorColor; bot = RenderSettings.ambientGroundColor; }
            else if (RenderSettings.ambientMode == AmbientMode.Flat)
                top = mid = bot = RenderSettings.ambientLight;
            else if (oldSky)
            { top = oldSky.top.value; mid = oldSky.middle.value; bot = oldSky.bottom.value; }
            else
            { top = new Color(0.5f, 0.54f, 0.62f); mid = new Color(0.42f, 0.4f, 0.38f); bot = new Color(0.22f, 0.2f, 0.18f); }
            if (old) AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            float skyEV = Mathf.Log(FortRender.NitsScale, 2f);
            // под открытым небом (у сцены туман RenderSettings — локации) — физическое небо от солнца: закат сам
            // красит небо и фоновый свет; арены и стенды без тумана — ровное градиентное небо из фона RenderSettings
            bool outdoor = RenderSettings.fog;
            var sun = RenderSettings.sun;
            if (!sun) sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).FirstOrDefault(l => l.type == LightType.Directional);
            float sunElev = sun ? Mathf.Asin(Mathf.Clamp(-sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg : 45f;

            var ve = Add<VisualEnvironment>(p);
            ve.skyAmbientMode.value = SkyAmbientMode.Dynamic;
            if (outdoor)
            {
                ve.skyType.value = (int)SkyType.PhysicallyBased;
                Add<PhysicallyBasedSky>(p);
                // низкое солнце даёт мало неба — тени подсвечиваются голубым небом сильнее, днём — немного
                var il = Add<IndirectLightingController>(p);
                il.indirectDiffuseLightingMultiplier.value = sunElev < 25f ? 4f : 1.5f;
                il.reflectionLightingMultiplier.value = 1.5f;
                if (sun && sun.GetComponent<HDAdditionalLightData>() is HDAdditionalLightData hd)
                    hd.volumetricDimmer = 0.3f;            // солнце в дымке — слегка; факелы — полностью
            }
            else
            {
                ve.skyType.value = (int)SkyType.Gradient;
                var sky = Add<GradientSky>(p);
                Color L(Color c) { c.a = 1f; return c; }
                sky.top.value = L(top); sky.middle.value = L(mid); sky.bottom.value = L(bot);
                sky.gradientDiffusion.value = 1f;
                sky.skyIntensityMode.value = SkyIntensityMode.Exposure;
                sky.exposure.value = skyEV;
            }

            var ex = Add<Exposure>(p);
            ex.mode.value = ExposureMode.Fixed;
            ex.fixedExposure.value = FortRender.ExposureEV;

            var fog = Add<Fog>(p);
            if (outdoor)
            {
                // туман — постоянного сумеречного цвета (цвет «от неба» у заката светится в сторону солнца)
                fog.enabled.value = true;
                fog.meanFreePath.value = 350f;
                fog.baseHeight.value = -20f;
                fog.maximumHeight.value = 120f;
                fog.colorMode.value = FogColorMode.ConstantColor;
                fog.color.value = new Color(0.2f, 0.21f, 0.26f) * FortRender.NitsScale;
                fog.enableVolumetricFog.value = true;      // свет факелов и окон в дымке
                fog.albedo.value = new Color(1f, 0.92f, 0.85f);
                fog.depthExtent.value = 48f;
            }
            else fog.enabled.value = false;

            var mb = Add<MotionBlur>(p);
            mb.intensity.value = 0f;                       // как в URP: без размытия движения (у HDRP по умолчанию включено)

            // постобработка: нейтральный тонмаппинг (ACES гасил тени), чуть контраста, холодные тени / тёплые света,
            // свечение только у ярких источников (порог: солнце не заливает полкадра), лёгкая виньетка
            var tm = Add<Tonemapping>(p);
            tm.mode.value = TonemappingMode.Neutral;

            var ca = Add<ColorAdjustments>(p);
            ca.contrast.value = 15f;
            ca.saturation.value = 5f;
            ca.postExposure.value = 0f;
            ca.colorFilter.overrideState = false;
            ca.hueShift.overrideState = false;

            var smh = Add<ShadowsMidtonesHighlights>(p);
            smh.shadows.value = new Vector4(0.9f, 0.95f, 1.1f, 0f);
            smh.highlights.value = new Vector4(1.08f, 1f, 0.9f, 0f);
            smh.midtones.overrideState = false;

            var bloom = Add<Bloom>(p);
            bloom.intensity.value = 0.15f;
            bloom.threshold.value = 1.2f;
            bloom.scatter.value = 0.5f;

            var vg = Add<Vignette>(p);
            vg.intensity.value = 0.25f;
            vg.smoothness.value = 0.5f;
            vg.color.value = Color.black;

            var ao = Add<ScreenSpaceAmbientOcclusion>(p);
            ao.intensity.value = 1.1f;
            ao.radius.value = 0.6f;
            ao.directLightingStrength.value = 0.35f;

            var cs = Add<ContactShadows>(p);
            cs.enable.value = true;
            cs.length.value = 0.12f;

            var ssr = Add<ScreenSpaceReflection>(p);
            ssr.enabled.value = true;

            var sh = Add<HDShadowSettings>(p);
            sh.maxShadowDistance.value = 90f;

            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            vol.sharedProfile = p;
        }

        static T Add<T>(VolumeProfile p) where T : VolumeComponent
        {
            var c = p.Add<T>(true);
            c.name = typeof(T).Name;
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        static void BuildGrime(Scene scene)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GrimeMat);
            if (!mat) return;
            var go = new GameObject(GrimeName);
            SceneManager.MoveGameObjectToScene(go, scene);
            var v = go.AddComponent<CustomPassVolume>();
            v.isGlobal = true;
            v.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            var pass = v.AddPassOfType<FullScreenCustomPass>();
            pass.name = "CreaseGrime";
            pass.fullscreenPassMaterial = mat;
            pass.materialPassName = "CreaseGrime";
            pass.fetchColorBuffer = true;
        }
    }
}
