using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Земля уровня L0 — Unity Terrain: слои (трава, сухая трава, грунт, камень, тропа) с шумом и вытоптанной
    /// полосой у мощения и стен, лёгкий рельеф за картой, дыры под рвом и ямами, трава деталями ландшафта (сама редеет вдали).</summary>
    public static class GroundTerrain
    {
        const string Tex = "Assets/Location/Textures/";
        const string Gen = "Assets/Location/Generated/";

        static TerrainLayer Layer(string name, string tex, float tile, Vector4? tint = null)
        {
            Directory.CreateDirectory(Gen);
            string p = Gen + "TL_" + name + ".terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(p);
            if (!l) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, p); }
            bool kit = tex.StartsWith("Assets/");                  // текстура кита: <путь>_BaseColor / _Normal
            l.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(kit ? tex + "_BaseColor.png" : Tex + tex + ".png");
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(kit ? tex + "_Normal.png" : Tex + tex + "_nrm.png");
            if (n) { l.normalMapTexture = n; l.normalScale = 1.1f; }
            l.tileSize = new Vector2(tile, tile);
            l.smoothness = 0.05f; l.metallic = 0f;
            l.diffuseRemapMin = Vector4.zero; l.diffuseRemapMax = tint ?? Vector4.one;   // сухая трава — та же трава, тёплый оттенок
            EditorUtility.SetDirty(l);
            return l;
        }

        /// <summary>Трава для деталей ландшафта: меш FE поставлен вертикально (в исходнике высота по −Z) и сохранён как ассет.</summary>
        static GameObject GrassPrototype(string model)
        {
            string pp = Gen + "Detail_" + model + ".prefab";
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(pp);
            if (have) return have;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kits/Extra/Models/" + model + ".fbx");
            var inst = (GameObject)Object.Instantiate(src);
            var mf = inst.GetComponentInChildren<MeshFilter>();
            var up = Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0)) * mf.transform.localToWorldMatrix;
            var m = Object.Instantiate(mf.sharedMesh);
            var vs = m.vertices; var ns = m.normals;
            for (int i = 0; i < vs.Length; i++) { vs[i] = up.MultiplyPoint3x4(vs[i]); if (ns.Length > i) ns[i] = up.MultiplyVector(ns[i]).normalized; }
            m.vertices = vs; if (ns.Length == vs.Length) m.normals = ns; m.RecalculateBounds();
            Object.DestroyImmediate(inst);
            AssetDatabase.CreateAsset(m, Gen + "Detail_" + model + ".asset");
            var go = new GameObject("Detail_" + model);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Kits/Materials/EX_Plant.mat");
            var pf = PrefabUtility.SaveAsPrefabAsset(go, pp);
            Object.DestroyImmediate(go);
            return pf;
        }

        static void Chamfer(float[,] d, int n, float px, float pz)
        {
            float dd = Mathf.Sqrt(px * px + pz * pz);
            for (int pass = 0; pass < 2; pass++)
            {
                int s = pass == 0 ? 1 : -1;
                for (int yi = 0; yi < n; yi++)
                for (int xi = 0; xi < n; xi++)
                {
                    int y = pass == 0 ? yi : n - 1 - yi, x = pass == 0 ? xi : n - 1 - xi;
                    float v = d[y, x];
                    if (x - s >= 0 && x - s < n) v = Mathf.Min(v, d[y, x - s] + px);
                    if (y - s >= 0 && y - s < n) v = Mathf.Min(v, d[y - s, x] + pz);
                    if (x - s >= 0 && x - s < n && y - s >= 0 && y - s < n) v = Mathf.Min(v, d[y - s, x - s] + dd);
                    if (x + s >= 0 && x + s < n && y - s >= 0 && y - s < n) v = Mathf.Min(v, d[y - s, x + s] + dd);
                    d[y, x] = v;
                }
            }
        }

        public static Terrain Build(string sceneName, Transform parent, float ox, float oz, int W, int H, float cell, string[] l0)
        {
            const float margin = 90f, baseY = -3f, maxH = 16f;
            float sx = W * cell + 2 * margin, sz = H * cell + 2 * margin;
            const int hres = 1025, ares = 512, dres = 512;   // шаг высот ~0.26 м: край ямы и рва ровно по клетке
            Directory.CreateDirectory(Gen);
            string tp = Gen + sceneName + "_Terrain.asset";
            AssetDatabase.DeleteAsset(tp);
            var td = new TerrainData { heightmapResolution = hres };
            AssetDatabase.CreateAsset(td, tp);
            td.size = new Vector3(sx, maxH, sz);
            td.alphamapResolution = ares;
            float tx0 = ox - margin, tz0 = oz - margin;

            char At(float x, float z)
            {
                int i = Mathf.FloorToInt((x - ox) / cell), j = Mathf.FloorToInt((z - oz) / cell);
                if (l0 == null || j < 0 || j >= l0.Length || i < 0 || i >= l0[j].Length) return '\0';   // за картой
                return l0[j][i];
            }
            bool Ground(char c) => c == '.' || c == ',' || c == '\0';
            bool Hole(char c) => c == '~' || c == 'L' || c == ' ' || c == '_';   // _ — вода под мостом
            // дорога за картой: мощёная клетка на краю карты продолжается наружу прямой дорогой (подводы, караваны)
            float RoadOut(float x, float z)     // 1 на дороге, 0 вдали; только снаружи карты
            {
                if (l0 == null) return 0f;
                float x1 = ox + W * cell, z1 = oz + H * cell;
                bool outX = x < ox || x > x1, outZ = z < oz || z > z1;
                if (outX == outZ) return 0f;                       // внутри карты или за углом
                float best = 0f;
                if (outZ)
                {
                    int j = z < oz ? 0 : H - 1;
                    for (int i = 0; i < W; i++)
                    {
                        if (j >= l0.Length || i >= l0[j].Length || l0[j][i] != ':') continue;
                        float d = Mathf.Abs(x - (ox + (i + 0.5f) * cell)) - cell / 2;
                        best = Mathf.Max(best, 1f - Mathf.SmoothStep(0f, 1f, d / 1.2f));
                    }
                }
                else
                {
                    for (int j = 0; j < H && j < l0.Length; j++)
                    {
                        int i = x < ox ? 0 : W - 1;
                        if (i >= l0[j].Length || l0[j][i] != ':') continue;
                        float d = Mathf.Abs(z - (oz + (j + 0.5f) * cell)) - cell / 2;
                        best = Mathf.Max(best, 1f - Mathf.SmoothStep(0f, 1f, d / 1.2f));
                    }
                }
                return best;
            }
            bool WellHole(float x, float z)
            {
                int i = Mathf.FloorToInt((x - ox) / cell), j = Mathf.FloorToInt((z - oz) / cell);
                if (l0 == null || j < 0 || j >= l0.Length || i < 0 || i >= l0[j].Length || l0[j][i] != 'O') return false;
                float cx = ox + (i + 0.5f) * cell, cz = oz + (j + 0.5f) * cell;
                return Mathf.Abs(x - cx) < 0.6f && Mathf.Abs(z - cz) < 0.6f;
            }
            float MapDist(float x, float z)       // расстояние до края карты снаружи (0 внутри)
            {
                float dx = Mathf.Max(ox - x, 0, x - (ox + W * cell)), dz = Mathf.Max(oz - z, 0, z - (oz + H * cell));
                return Mathf.Sqrt(dx * dx + dz * dz);
            }

            // высоты: 0 под картой (чуть ниже под постройками), холмы за её краем
            var h = new float[hres, hres];
            for (int y = 0; y < hres; y++)
            for (int x = 0; x < hres; x++)
            {
                float wx = tx0 + x / (hres - 1f) * sx, wz = tz0 + y / (hres - 1f) * sz;
                char c = At(wx, wz);
                float wy = 0f;
                if (c == '\0')
                {
                    float d = MapDist(wx, wz);
                    float hill = Mathf.PerlinNoise(wx * 0.018f + 11.3f, wz * 0.018f + 4.7f) * 3.2f + Mathf.PerlinNoise(wx * 0.06f, wz * 0.06f) * 0.6f;
                    wy = hill * Mathf.SmoothStep(0, 1, d / 28f) * (1f - RoadOut(wx, wz));   // дорога ровная
                }
                else wy = 0f;   // под постройками тоже вровень: пол лежит на 1 см выше, а у стен снаружи нет щели
                h[y, x] = (wy - baseY) / maxH;
            }
            td.SetHeights(0, 0, h);

            // дыры: ров, ямы, колодец — по центру ячейки высот, без запаса: край земли в пределах полуячейки (~0.13 м)
            // от линии клетки, его прячут бордюр рва (0.7 м), парапет ямы (±0.2) и верх стенки
            var holes = new bool[hres - 1, hres - 1];
            for (int y = 0; y < hres - 1; y++)
            for (int x = 0; x < hres - 1; x++)
            {
                float hx = tx0 + (x + 0.5f) / (hres - 1f) * sx, hz = tz0 + (y + 0.5f) / (hres - 1f) * sz;
                holes[y, x] = !Hole(At(hx, hz)) && !WellHole(hx, hz);
            }
            td.SetHoles(0, 0, holes);

            // слои
            td.terrainLayers = new[]
            {
                Layer("Grass", "ground_grass", 4f), Layer("DryGrass", "ground_grass", 4.6f, new Vector4(1.25f, 1.12f, 0.55f, 1f)),
                Layer("Stones", "ground_stones", 3f), Layer("Road", "ground_soil", 3.5f),
                Layer("Cobble", "Assets/Kits/QVillage/Textures/T_UnevenBrick", 2f),   // та же кладка, что у плиток мощения
            };
            // расстояние до мощения/построек (для вытоптанной полосы) — двухпроходное поле на сетке слоёв
            float px = sx / ares, pz = sz / ares;
            var dist = new float[ares, ares];
            for (int y = 0; y < ares; y++)
            for (int x = 0; x < ares; x++)
            {
                char c = At(tx0 + (x + 0.5f) * px, tz0 + (y + 0.5f) * pz);
                dist[y, x] = (c != '\0' && !Ground(c) && !Hole(c) && c != ':') ? 0f : 1e6f;   // стены и полы (мощение — отдельно)
            }
            Chamfer(dist, ares, px, pz);
            // знаковое расстояние до края мощения: снаружи > 0, внутри < 0
            var dOut = new float[ares, ares]; var dIn = new float[ares, ares];
            for (int y = 0; y < ares; y++)
            for (int x = 0; x < ares; x++)
            {
                bool pave = At(tx0 + (x + 0.5f) * px, tz0 + (y + 0.5f) * pz) == ':';
                dOut[y, x] = pave ? 0f : 1e6f; dIn[y, x] = pave ? 1e6f : 0f;
            }
            Chamfer(dOut, ares, px, pz); Chamfer(dIn, ares, px, pz);
            var am = new float[ares, ares, 5];
            var grass = new int[dres, dres];
            var flowers = new int[dres, dres];
            for (int y = 0; y < ares; y++)
            for (int x = 0; x < ares; x++)
            {
                float wx = tx0 + (x + 0.5f) * px, wz = tz0 + (y + 0.5f) * pz;
                char c = At(wx, wz);
                float dry = Mathf.Clamp01((Mathf.PerlinNoise(wx * 0.045f + 3.1f, wz * 0.045f + 7.9f) - 0.48f) * 2.6f) * 0.75f;
                dry += Mathf.Clamp01((Mathf.PerlinNoise(wx * 0.21f, wz * 0.21f) - 0.62f) * 3f) * 0.25f;
                float stones = Mathf.Clamp01((Mathf.PerlinNoise(wx * 0.09f + 21f, wz * 0.09f + 5f) - 0.7f) * 3.5f) * 0.6f;
                float path = c == '\0' ? 0f : 1f - Mathf.SmoothStep(0f, 1f, (dist[y, x] - 0.15f) / 1.6f);   // вытоптано у троп и стен
                path *= 0.75f + 0.5f * Mathf.PerlinNoise(wx * 0.7f, wz * 0.7f);                                  // рваный край
                float g = 1f, dr = dry, st = stones, rd = Mathf.Clamp01(path);
                // мощение: край рваный в обе стороны — камни растащены в землю, земля занесена ногами на камень
                float sd = dOut[y, x] > 0 ? dOut[y, x] : -dIn[y, x];
                float en = (Mathf.PerlinNoise(wx * 1.3f + 5f, wz * 1.3f + 1f) - 0.5f) * 0.9f + (Mathf.PerlinNoise(wx * 4f, wz * 4f) - 0.5f) * 0.4f;
                float cb = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((sd + 0.2f + en) / 0.55f + 0.5f));
                float band = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Abs(sd + en * 0.6f) / 0.9f));   // грязь поперёк края
                if (c == ',') { g = 0; dr = 1; st = 0; rd = 0.2f; cb = 0; }                                    // грядки
                if (c == ' ') { float ro = RoadOut(wx, wz); if (ro > 0f) { rd = Mathf.Max(rd, ro); g *= 1f - ro; } }   // дорога за картой
                else if (c != '\0' && !Ground(c) && c != ':') { g = 0; dr = 0; st = 0; rd = 1; cb = 0; }
                rd = Mathf.Max(rd * (1f - cb), band * 0.85f * (1f - cb * 0.55f));
                // трава уступает тропе, мощению и сухим пятнам
                g = Mathf.Max(0, g - rd - dr * 0.6f - st - cb);
                dr *= 1f - cb; st *= 1f - cb;
                float sum = g + dr + st + rd + cb + 1e-4f;
                am[y, x, 0] = g / sum; am[y, x, 1] = dr / sum; am[y, x, 2] = st / sum; am[y, x, 3] = rd / sum; am[y, x, 4] = cb / sum;
                // трава деталями: гуще в поле, реже на сухом, нет на тропе
                int dx = x * dres / ares, dz = y * dres / ares;
                float lush = am[y, x, 0] * (0.55f + 0.6f * Mathf.PerlinNoise(wx * 0.12f + 40f, wz * 0.12f));
                if (Ground(c) && c != ',') grass[dz, dx] = Mathf.RoundToInt(lush * 3f);
                if (Ground(c) && c != ',' && Mathf.PerlinNoise(wx * 0.3f + 9f, wz * 0.3f + 2f) > 0.72f && am[y, x, 0] > 0.6f) flowers[dz, dx] = 1;
            }
            td.SetAlphamaps(0, 0, am);

            var protos = new List<DetailPrototype>();
            foreach (var (model, w0, w1) in new[] { ("Grass2", 0.38f, 0.62f), ("Grass3", 0.36f, 0.6f), ("Flower4", 0.35f, 0.5f) })
            {
                var dp = new DetailPrototype
                {
                    usePrototypeMesh = true, prototype = GrassPrototype(model), renderMode = DetailRenderMode.VertexLit,
                    useInstancing = true, minWidth = w0, maxWidth = w1, minHeight = w0, maxHeight = w1 * 1.1f,
                    noiseSpread = 0.4f, healthyColor = Color.white, dryColor = new Color(0.85f, 0.82f, 0.62f),
                };
                protos.Add(dp);
            }
            td.SetDetailResolution(dres, 32);
            td.detailPrototypes = protos.ToArray();
            var g2 = new int[dres, dres]; var g3 = new int[dres, dres];
            for (int y = 0; y < dres; y++) for (int x = 0; x < dres; x++)
            {   // две травы вперемешку — пятнами
                bool a = Mathf.PerlinNoise(x * 0.07f, y * 0.07f) > 0.5f;
                g2[y, x] = a ? grass[y, x] : grass[y, x] / 2; g3[y, x] = a ? grass[y, x] / 2 : grass[y, x];
            }
            td.SetDetailLayer(0, 0, 0, g2);
            td.SetDetailLayer(0, 0, 1, g3);
            td.SetDetailLayer(0, 0, 2, flowers);

            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Ground_Terrain";
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(tx0, baseY, tz0);
            var t = go.GetComponent<Terrain>();
            t.detailObjectDistance = 70f; t.detailObjectDensity = 1f; t.heightmapPixelError = 4f; t.basemapDistance = 120f;
            t.drawInstanced = true;
            var lit = Shader.Find("HDRP/TerrainLit");
            if (lit) t.materialTemplate = new Material(lit);
            var fe = go.AddComponent<Fortress.Grid.FortElement>(); fe.kind = Fortress.Grid.ElementKind.Ground; fe.model = "Terrain";
            return t;
        }
    }
}
