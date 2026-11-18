using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Fortress.Grid;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fortress.EditorTools
{
    /// <summary>Набор блоков: материалы китов Quaternius, префаб на каждую модель (коллайдер + FortElement),
    /// каталог габаритов для компилятора сетки (D:/Metroidvany/level/catalog.json).</summary>
    public static class KitSetup
    {
        const string Kits = "Assets/Kits";
        const string MatDir = Kits + "/Materials";
        const string PrefabDir = Kits + "/Prefabs";
        public const string CatalogPath = "D:/Metroidvany/level/catalog.json";

        static readonly (string dir, string pack)[] Packs =
        {
            (Kits + "/QVillage/Models", "Village"),
            (Kits + "/QProps/Models", "Props"),
        };

        [MenuItem("Fortress/Kit/1 Setup Materials+Prefabs+Catalog")]
        public static void SetupAll()
        {
            SetupMaterials();
            BuildPrefabs(false);
            ExportCatalog();
        }

        [MenuItem("Fortress/Kit/Rebuild Prefabs (overwrite)")]
        public static void RebuildPrefabs() { BuildPrefabs(true); ExportCatalog(); }

        // ------------------------------------------------------------------ обратная сторона у односторонних деталей
        [MenuItem("Fortress/Kit/5 Backfaces for roofs")]
        public static void AddBackfaces()
        {
            Directory.CreateDirectory(Kits + "/Generated");
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir + "/Village" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (!(name.StartsWith("Roof_") || name.StartsWith("Overhang_Roof") || name.StartsWith("Window_Roof"))) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root.transform.Find("Backface") == null)
                {
                    var holder = new GameObject("Backface").transform; holder.SetParent(root.transform, false);
                    foreach (var mf in root.GetComponentsInChildren<MeshFilter>().ToArray())
                    {
                        if (mf.transform.IsChildOf(holder)) continue;
                        string mp = Kits + "/Generated/Back_" + name + "_" + mf.sharedMesh.name + ".asset";
                        var back = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
                        if (!back)
                        {   // те же треугольники в обратном порядке и нормали наружу с другой стороны — свет снизу верный
                            back = Object.Instantiate(mf.sharedMesh);
                            for (int s = 0; s < back.subMeshCount; s++)
                            {
                                var t = back.GetTriangles(s);
                                for (int i = 0; i < t.Length; i += 3) { int a = t[i]; t[i] = t[i + 1]; t[i + 1] = a; }
                                back.SetTriangles(t, s);
                            }
                            back.normals = back.normals.Select(v => -v).ToArray();
                            AssetDatabase.CreateAsset(back, mp);
                        }
                        var g = new GameObject("Back_" + mf.name);
                        g.transform.SetParent(holder, false);
                        g.transform.position = mf.transform.position; g.transform.rotation = mf.transform.rotation;
                        g.transform.localScale = Vector3.Scale(mf.transform.lossyScale, new Vector3(1 / root.transform.lossyScale.x, 1 / root.transform.lossyScale.y, 1 / root.transform.lossyScale.z));
                        g.AddComponent<MeshFilter>().sharedMesh = back;
                        g.AddComponent<MeshRenderer>().sharedMaterials = mf.GetComponent<MeshRenderer>().sharedMaterials;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    n++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Kit] обратная сторона добавлена у " + n + " деталей крыш");
        }

        // ------------------------------------------------------------------ составные элементы, земля, стекло
        [MenuItem("Fortress/Kit/3 Composites + Ground + Glass")]
        public static void SetupComposites()
        {
            // стекло видно с обеих сторон
            var glass = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/MI_WindowGlass.mat");
            if (glass) { glass.SetFloat("_Cull", 0f); EditorUtility.SetDirty(glass); }
            var plant = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/EX_Plant.mat");
            if (plant) { plant.enableInstancing = true; EditorUtility.SetDirty(plant); }
            GroundNormals();
            BuildWell();
            BuildFillers();
            AssetDatabase.SaveAssets();
            ExportCatalog();
        }

        /// <summary>Карта нормалей земли из её же текстуры (у FE земли нормалей нет) — рельеф как у стен.</summary>
        static void GroundNormals()
        {
            const string T = "Assets/Location/Textures/", M = "Assets/Location/Materials/";
            foreach (var (tex, mat) in new[] { ("ground_grass", "EXP_grass"), ("ground_soil", "EXP_soil"), ("ground_stones", "EXP_stones") })
            {
                string src = T + tex + ".png", dst = T + tex + "_nrm.png";
                if (!File.Exists(dst)) { File.Copy(src, dst); AssetDatabase.ImportAsset(dst); }
                var ti = (TextureImporter)AssetImporter.GetAtPath(dst);
                ti.textureType = TextureImporterType.NormalMap; ti.convertToNormalmap = true;
                ti.heightmapScale = 0.09f; ti.normalmapFilter = TextureImporterNormalFilter.Sobel;
                ti.SaveAndReimport();
                var m = AssetDatabase.LoadAssetAtPath<Material>(M + mat + ".mat");
                if (!m) continue;
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dst));
                m.SetFloat("_BumpScale", 1.2f); m.EnableKeyword("_NORMALMAP");
                Matte(m, 0.08f);
                EditorUtility.SetDirty(m);
            }
        }

        /// <summary>Угловой блок 1×1×1 м (низ в 0, центр по x/z) тем же материалом, что стена: кладка по миру проходит через угол без шва.</summary>
        static void BuildFillers()
        {
            string dir = PrefabDir + "/Composite"; Directory.CreateDirectory(dir);
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            foreach (var (name, mat) in new[] { ("Corner_Filler", "MI_UnevenBrick"), ("Corner_Filler_Plaster", "MI_Plaster"), ("Corner_Filler_Beam", "MI_WoodTrim"), ("Bars_Post", "MI_MetalOrnaments") })
            {
                var root = new GameObject(name);
                var body = new GameObject("Block"); body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0, 0.5f, 0);
                body.AddComponent<MeshFilter>().sharedMesh = cube;
                var mr = body.AddComponent<MeshRenderer>();
                mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + mat + ".mat");
                body.AddComponent<BoxCollider>();
                GameObjectUtility.SetStaticEditorFlags(body, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
                var fe = root.AddComponent<FortElement>(); fe.kind = ElementKind.Wall; fe.model = name;
                PrefabUtility.SaveAsPrefabAsset(root, dir + "/" + name + ".prefab");
                Object.DestroyImmediate(root);
            }
        }

        static GameObject Part(string prefab, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + prefab + ".prefab");
            var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            g.transform.localPosition = pos; g.transform.localEulerAngles = euler; g.transform.localScale = scale;
            var fe = g.GetComponent<FortElement>(); if (fe) Object.DestroyImmediate(fe);
            return g;
        }

        /// <summary>Колодец из деталей набора: каменный оголовок, два столба, ворот, двускатная крыша, ведро и верёвка.</summary>
        static void BuildWell()
        {
            string dir = PrefabDir + "/Composite"; Directory.CreateDirectory(dir);
            var root = new GameObject("Well_Composite");
            var t = root.transform;
            Part("Village/Stairs_Exterior_SingleSideThick", t, new Vector3(0, 0, 0.8f), new Vector3(0, 90, 0), Vector3.one);
            Part("Village/Stairs_Exterior_SingleSideThick", t, new Vector3(0, 0, -0.8f), new Vector3(0, 90, 0), Vector3.one);
            Part("Village/Stairs_Exterior_SingleSideThick", t, new Vector3(0.8f, 0, 0), Vector3.zero, new Vector3(1, 1, 0.6f));
            Part("Village/Stairs_Exterior_SingleSideThick", t, new Vector3(-0.8f, 0, 0), Vector3.zero, new Vector3(1, 1, 0.6f));
            // крыша: доски лежат на коньковой балке, балка — на столбах, столбы стоят на бортиках (всё касается опоры)
            const float roofY = 2.86f;                       // низ досок у карниза; конёк выше на ~0.9 м
            var ridgeL = Part("Village/Roof_Wooden_2x1_Center", t, new Vector3(0, roofY, 0), Vector3.zero, Vector3.one);
            Part("Village/Roof_Wooden_2x1_Center_Mirror", t, new Vector3(0, roofY, 0), Vector3.zero, Vector3.one);
            float ridgeUnder = roofY + 0.80f;                // низ досок у конька (над балкой)
            foreach (var r in ridgeL.GetComponentsInChildren<Renderer>())
            {   // точная высота: самый нижний край досок над линией конька
                var mc = r.GetComponent<MeshFilter>();
                if (!mc) continue;
                float best = float.MaxValue;
                foreach (var v in mc.sharedMesh.vertices)
                {
                    var w = r.transform.TransformPoint(v) - t.position;
                    if (Mathf.Abs(w.z) < 0.08f) best = Mathf.Min(best, w.y);
                }
                if (best < float.MaxValue) ridgeUnder = best;
            }
            float beamH = 0.21f;                             // сечение столба набора
            float postTop = ridgeUnder - beamH;
            foreach (float x in new[] { -0.8f, 0.8f })
                Part("Village/Corner_Exterior_Wood", t, new Vector3(x, 1.0f, 0), Vector3.zero, new Vector3(1.1f, (postTop - 1.0f) / 3f, 1.1f));
            // коньковая балка лежит на столбах, под досками
            var ridge = Part("Village/Corner_Exterior_Wood", t, new Vector3(1.05f, postTop + beamH / 2f, 0), new Vector3(0, 0, 90), new Vector3(1f, 2.1f / 3f, 1f));
            foreach (var c in ridge.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            // ворот проходит сквозь оба столба
            var axle = Part("Village/Corner_Exterior_Wood", t, new Vector3(1.0f, 1.9f, 0), new Vector3(0, 0, 90), new Vector3(0.8f, 2.0f / 3f, 0.8f));
            foreach (var c in axle.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            Part("Props/Rope_2", t, new Vector3(0.35f, 1.0f, 0.8f), new Vector3(0, 30, 0), new Vector3(0.7f, 0.7f, 0.7f));
            Part("Props/Bucket_Wooden_1", t, new Vector3(-0.4f, 1.0f, -0.8f), new Vector3(0, -20, 0), Vector3.one);
            var fe2 = root.AddComponent<FortElement>(); fe2.kind = ElementKind.Prop; fe2.model = "Well_Composite";
            PrefabUtility.SaveAsPrefabAsset(root, dir + "/Well_Composite.prefab");
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ extra (FantasyEnvironments: природа, мешки, еда)
        [MenuItem("Fortress/Kit/2 Setup Extra (nature, storage, food)")]
        public static void SetupExtra() { BuildExtra(false); ExportCatalog(); }

        static readonly Dictionary<string, (string bark, string leaves)> TreeTex = new()
        {
            { "Birch", ("Birch_bark.png", "Birch_leaves.tga") }, { "Oak", ("Oak_bark.png", "oak_leaves.tga") },
            { "Pine", ("Pine_bark.png", "Pine_leaves.tga") }, { "Deciduous", ("Tree_bark1.png", "Tree_leaves1.tga") },
        };

        static void BuildExtra(bool overwrite)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            string T = Kits + "/Extra/Textures/";
            Material TexMat(string name, string tex, bool cutout)
            {
                var m = MakeMat("EX_" + name, lit);
                m.SetTexture("_BaseMap", Tex(T + tex)); m.SetColor("_BaseColor", Color.white);
                Matte(m, 0.08f);
                if (cutout) AlphaClip(m, 0.45f);
                EditorUtility.SetDirty(m);
                return m;
            }
            string outDir = PrefabDir + "/Extra";
            Directory.CreateDirectory(outDir);
            int made = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Kits + "/Extra/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string n = Path.GetFileNameWithoutExtension(path);
                if (n == "Grass1" || n == "Fern1") continue;   // смещённый пивот в исходнике
                string pp = outDir + "/" + n + ".prefab";
                if (!overwrite && File.Exists(pp)) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var root = new GameObject(n);
                var up = new GameObject("Upright").transform; up.SetParent(root.transform, false);
                up.localRotation = Quaternion.Euler(90f, 0f, 0f);   // у FE высота идёт по −Z
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, up);
                inst.name = "Model";
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("UCX_") || r.name.Contains("_LOD1") || r.name.Contains("_LOD2")) r.enabled = false;
                var lg = inst.GetComponentInChildren<LODGroup>(); if (lg) lg.enabled = false;
                // материалы
                string tree = TreeTex.Keys.FirstOrDefault(k => n.StartsWith(k));
                bool plant = n.StartsWith("Bush") || n.StartsWith("Fern") || n.StartsWith("Flower") || n.StartsWith("Grass") ||
                             n.StartsWith("Mushroom") || n.StartsWith("Plant") || n == "Rye";
                foreach (var r in inst.GetComponentsInChildren<Renderer>())
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        string mn = ms[i] ? ms[i].name : "";
                        if (tree != null) ms[i] = i == 0 ? TexMat(tree + "_bark", TreeTex[tree].bark, false) : TexMat(tree + "_leaves", TreeTex[tree].leaves, true);
                        else if (n == "Deciduous_tree_stump") ms[i] = TexMat("Deciduous_bark", "Tree_bark1.png", false);
                        else if (n == "Pine_stump") ms[i] = TexMat("Pine_bark", "Pine_bark.png", false);
                        else if (plant) ms[i] = TexMat("Plant", "Plant.tga", true);
                        else if (n == "Rock1") ms[i] = TexMat("Rock1", "Rock1.png", false);
                        else if (n == "Rock2") ms[i] = TexMat("Rock2", "Rock2.png", false);
                        else if (n == "Rock3") ms[i] = TexMat("Rock3", "Rock3.png", false);
                        else if (mn.StartsWith("Stone")) ms[i] = TexMat("Stone", "Stone.png", false);
                        else if (mn.StartsWith("Food")) ms[i] = TexMat("Food", "Food.png", false);
                        else if (mn.StartsWith("Storage")) ms[i] = TexMat("Storage", "Storage.png", false);
                        else if (mn.StartsWith("Furniture")) ms[i] = TexMat("Furniture", "Furniture.png", false);
                        else if (mn.StartsWith("WoodenHouse")) ms[i] = TexMat("WoodenHouse", "WoodenHouse.png", false);
                    }
                    r.sharedMaterials = ms;
                    r.shadowCastingMode = plant && !n.StartsWith("Bush") ? ShadowCastingMode.Off : ShadowCastingMode.On;
                }
                var fe = root.AddComponent<FortElement>();
                bool nature = tree != null || plant || n.Contains("stump") || n.StartsWith("Rock") || n.StartsWith("Stone");
                fe.kind = nature ? ElementKind.Nature : ElementKind.Prop; fe.model = n;
                var b = ModelBounds(inst);
                if (tree != null)
                {
                    var c = root.AddComponent<CapsuleCollider>(); c.center = new Vector3(0, 2.5f, 0); c.radius = 0.45f; c.height = 5f;
                }
                else if (n.StartsWith("Rock") || n.StartsWith("Stone") || n.Contains("stump") || n.Contains("fireplace"))
                {
                    foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                        if (!mf.name.StartsWith("UCX_") && !mf.name.Contains("_LOD1") && !mf.name.Contains("_LOD2"))
                            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }
                else if (!plant && !n.StartsWith("Food") && !n.StartsWith("Furniture_book") && b.size.y > 0.3f)
                {
                    var bc = root.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = b.size;
                }
                PrefabUtility.SaveAsPrefabAsset(root, pp);
                Object.DestroyImmediate(root);
                made++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Kit] Extra префабов: " + made);
        }

        // ------------------------------------------------------------------ materials
        static Material MakeMat(string name, Shader sh)
        {
            Directory.CreateDirectory(MatDir);
            string p = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            m.shader = sh;
            return m;
        }

        static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        static void Matte(Material m, float smooth = 0.1f)
        {
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        }

        static void Textured(Material m, Texture2D baseMap, Texture2D normal, float bump = 1f)
        {
            m.SetTexture("_BaseMap", baseMap);
            m.SetColor("_BaseColor", Color.white);
            if (normal)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", bump);
                m.EnableKeyword("_NORMALMAP");
            }
        }

        static void AlphaClip(Material m, float cutoff = 0.4f)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", cutoff);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }

        static void Transparent(Material m, Color c)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        public static Dictionary<string, Material> SetupMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var vc = Shader.Find("Fortress/LitVertexColor") ?? lit;
            string V = Kits + "/QVillage/Textures/", P = Kits + "/QProps/Textures/";
            var map = new Dictionary<string, Material>();

            void Village(string mi, string t, bool hasNormal = true)
            {
                var m = MakeMat(mi, lit);
                Textured(m, Tex(V + "T_" + t + "_BaseColor.png"), hasNormal ? Tex(V + "T_" + t + "_Normal.png") : null);
                Matte(m);
                map[mi] = m;
            }
            Village("MI_WoodTrim", "WoodTrim");
            Village("MI_WoodTrim_Wear", "WoodTrim");
            Village("MI_Plaster", "Plaster");
            Village("MI_RockTrim", "RockTrim");
            Village("MI_Brick", "Brick");
            Village("MI_RedBrick", "RedBrick", false);
            Village("MI_UnevenBrick", "UnevenBrick");
            Village("MI_RoundTiles", "RoundTiles");

            var metal = MakeMat("MI_MetalOrnaments", lit);
            metal.SetTexture("_BaseMap", null);
            metal.SetColor("_BaseColor", new Color(0.16f, 0.16f, 0.17f));
            metal.SetFloat("_Metallic", 0.55f); metal.SetFloat("_Smoothness", 0.35f);
            map["MI_MetalOrnaments"] = metal;

            var glass = MakeMat("MI_WindowGlass", lit);
            glass.SetTexture("_BaseMap", null);
            Transparent(glass, new Color(0.62f, 0.74f, 0.8f, 0.32f));
            glass.SetFloat("_Smoothness", 0.85f); glass.SetFloat("_Metallic", 0f);
            map["MI_WindowGlass"] = glass;

            var vine = MakeMat("MI_Vine", lit);
            Textured(vine, Tex(V + "T_VineLeaf.png"), null);
            vine.SetColor("_BaseColor", new Color(0.42f, 0.62f, 0.25f));
            AlphaClip(vine); Matte(vine);
            map["MI_Vine"] = vine;

            void Prop(string mi, string t, bool vertex)
            {
                var m = MakeMat(mi, vertex ? vc : lit);
                Textured(m, Tex(P + "T_" + t + "_BaseColor.png"), Tex(P + "T_" + t + "_Normal.png"), 0.8f);
                Matte(m, 0.15f);
                map[mi] = m;
            }
            Prop("MI_Trim_Metal", "Trim_Metal", true);
            Prop("MI_Trim_Metal_Vertex", "Trim_Metal", true);
            Prop("MI_Trim_Furniture", "Trim_Furniture", true);
            Prop("MI_Trim_Props", "Trim_Props", true);
            Prop("MI_Trim_Props_Vertex", "Trim_Props", true);
            Prop("MI_Trim_Cloth", "Trim_Cloth", true);
            Prop("MI_Banner", "Trim_Cloth", true);
            var page = MakeMat("MI_Page_Empty", vc);
            Textured(page, Tex(P + "T_Page_Noise.png"), null); Matte(page);
            map["MI_Page_Empty"] = page;
            foreach (var m in map.Values) EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();

            // remap embedded materials of every kit model to these
            foreach (var (dir, _) in Packs)
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                bool changed = false;
                foreach (var r in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                {
                    string n = r.name;
                    if (!map.TryGetValue(n, out var target)) { Debug.LogWarning("[Kit] нет материала " + n + " в " + path); continue; }
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), target);
                    changed = true;
                }
                if (changed) imp.SaveAndReimport();
            }
            Debug.Log("[Kit] материалы: " + map.Count);
            return map;
        }

        // ------------------------------------------------------------------ prefabs
        public static ElementKind KindOf(string n, string pack)
        {
            if (pack == "Village")
            {
                if (n.StartsWith("Wall_") && (n.Contains("Door") || n.Contains("Window"))) return ElementKind.Opening;
                if (n.StartsWith("Wall_") || n.StartsWith("Corner_") || n.StartsWith("Overhang_")) return ElementKind.Wall;
                if (n.StartsWith("Floor_")) return ElementKind.Floor;
                if (n.StartsWith("Stair")) return ElementKind.Stair;
                if (n.StartsWith("Roof_")) return ElementKind.Roof;
                if (n.StartsWith("Door") || n.StartsWith("Window")) return ElementKind.Opening;
                if (n.StartsWith("HoleCover") || n.StartsWith("Balcony")) return ElementKind.Trim;
                if (n.StartsWith("Prop_Vine")) return ElementKind.Nature;
                return ElementKind.Prop;
            }
            if (n.StartsWith("Lantern") || n.StartsWith("Torch") || n.StartsWith("Candle") || n == "Chandelier") return ElementKind.Light;
            if (n.StartsWith("Shelf") || n.StartsWith("Banner") || n == "Peg_Rack" || n == "Shield_Wooden") return ElementKind.WallProp;
            return ElementKind.Prop;
        }

        static bool NoCollider(string n, Bounds b)
        {
            if (n.StartsWith("Prop_Vine") || n.Contains("Cloth") || n.StartsWith("Banner") || n == "Chandelier") return true;
            if (n.StartsWith("Window_") && !n.StartsWith("Window_Roof")) return false;
            return b.size.x < 0.35f && b.size.y < 0.35f && b.size.z < 0.35f; // мелочь — не мешает ходить
        }

        static bool UseMeshCollider(string n, ElementKind k) =>
            k == ElementKind.Wall || k == ElementKind.Opening || k == ElementKind.Stair || k == ElementKind.Roof ||
            k == ElementKind.Trim || n.StartsWith("Stairs_") || n == "Prop_Wagon" || n.StartsWith("Bed") || n.StartsWith("Table") ||
            n.StartsWith("Bench") || n.StartsWith("Workbench") || n.StartsWith("Stall") || n.StartsWith("Bookcase") ||
            n.StartsWith("Prop_MetalFence") || n.StartsWith("Prop_WoodenFence");

        public static Bounds ModelBounds(GameObject inst)
        {
            var rs = inst.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void BuildPrefabs(bool overwrite)
        {
            int made = 0, kept = 0;
            foreach (var (dir, pack) in Packs)
            {
                string outDir = PrefabDir + "/" + pack;
                Directory.CreateDirectory(outDir);
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string n = Path.GetFileNameWithoutExtension(path);
                    string pp = outDir + "/" + n + ".prefab";
                    if (!overwrite && File.Exists(pp)) { kept++; continue; }
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var root = new GameObject(n);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    inst.name = "Model";
                    // точка привязки на линии сетки: стена и её вставки — центр толщины на ребре клетки,
                    // угловой столб — центр в узле сетки (удобно ставить руками с шагом 1 м)
                    if (n.StartsWith("Wall_Plaster_") || n.StartsWith("Wall_UnevenBrick_") || n.StartsWith("DoorFrame_") ||
                        n.StartsWith("WindowShutters_") || (n.StartsWith("Window_") && !n.StartsWith("Window_Roof")))
                        inst.transform.localPosition += new Vector3(0, 0, 0.11f);
                    else if (n.StartsWith("Corner_"))
                    {
                        var cb = ModelBounds(inst);
                        inst.transform.localPosition -= new Vector3(cb.center.x, 0, cb.center.z);
                    }
                    var kind = KindOf(n, pack);
                    var fe = root.AddComponent<FortElement>();
                    fe.kind = kind; fe.model = n;
                    var b = ModelBounds(inst);
                    if (!NoCollider(n, b))
                    {
                        if (UseMeshCollider(n, kind))
                            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                        else
                        {
                            var bc = root.AddComponent<BoxCollider>();
                            bc.center = b.center; bc.size = b.size;
                        }
                    }
                    foreach (var r in inst.GetComponentsInChildren<Renderer>())
                    {
                        r.shadowCastingMode = kind == ElementKind.Nature ? ShadowCastingMode.Off : ShadowCastingMode.On;
                        GameObjectUtility.SetStaticEditorFlags(r.gameObject,
                            kind == ElementKind.Prop || kind == ElementKind.Light || kind == ElementKind.WallProp
                                ? StaticEditorFlags.BatchingStatic
                                : StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    }
                    DarkenPlainMetal(inst);
                    PrefabUtility.SaveAsPrefabAsset(root, pp);
                    Object.DestroyImmediate(root);
                    made++;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Kit] префабы: новых {made}, сохранено без изменений {kept}");
        }

        /// <summary>Металл без цветов вершин (котёл, горшок, железное ведро) берёт светлый участок общей текстуры
        /// и выходит белым, как пластик. Таким мешам — тёмное железо (та же текстура, базовый цвет темнее).</summary>
        static bool DarkenPlainMetal(GameObject go)
        {
            const string darkPath = "Assets/Kits/Materials/MI_Trim_Metal_Dark.mat";
            var dark = AssetDatabase.LoadAssetAtPath<Material>(darkPath);
            bool changed = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh || mf.sharedMesh.colors.Length > 0) continue;
                var mats = r.sharedMaterials;
                bool ch = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i] || mats[i].name != "MI_Trim_Metal") continue;
                    if (!dark)
                    {
                        dark = new Material(mats[i]) { name = "MI_Trim_Metal_Dark" };
                        dark.SetColor("_BaseColor", new Color(0.30f, 0.29f, 0.31f));
                        if (dark.HasProperty("_Smoothness")) dark.SetFloat("_Smoothness", 0.3f);
                        AssetDatabase.CreateAsset(dark, darkPath);
                    }
                    mats[i] = dark; ch = true;
                }
                if (ch) { r.sharedMaterials = mats; changed = true; }
            }
            return changed;
        }

        [MenuItem("Fortress/Kit/6 Dark iron for plain metal props")]
        public static void DarkenExistingProps()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
            {
                string pp = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(pp);
                if (DarkenPlainMetal(root)) { PrefabUtility.SaveAsPrefabAsset(root, pp); n++; }
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Kit] тёмное железо: {n} префабов");
        }

        // ------------------------------------------------------------------ catalog
        [MenuItem("Fortress/Kit/Export Catalog")]
        public static void ExportCatalog()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            bool first = true;
            foreach (var pack in new[] { "Village", "Props", "Extra", "Composite" })
            {
                string d = PrefabDir + "/" + pack;
                if (!Directory.Exists(d)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { d }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
                    inst.transform.position = Vector3.zero;
                    var rs = inst.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) { Object.DestroyImmediate(inst); continue; }
                    var b = ModelBounds(inst);
                    var fe = inst.GetComponent<FortElement>();
                    string k = fe ? fe.kind.ToString() : "Prop";
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "  \"{0}\": {{\"pack\": \"{1}\", \"kind\": \"{2}\", \"prefab\": \"{3}\", \"min\": [{4:F3}, {5:F3}, {6:F3}], \"max\": [{7:F3}, {8:F3}, {9:F3}]}}",
                        go.name, pack, k, path, b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z);
                    Object.DestroyImmediate(inst);
                }
            }
            sb.Append("\n}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
            File.WriteAllText(CatalogPath, sb.ToString());
            Debug.Log("[Kit] каталог: " + CatalogPath);
        }
    }
}
