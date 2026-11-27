using Fortress.Hero.Arm2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// <summary>Сборщик нового героя (Arm2): префаб HeroArm2 и песочница HeroArm2Sandbox. Старого героя и его сцены не трогает.
    /// Отличие от сборщика арены: у всего твёрдого — настоящие коллайдеры в сцене (старый герой создавал их сам при запуске),
    /// платформы — кинематические Rigidbody (Arm2Mover).</summary>
    public static class HeroArm2Builder
    {
        public const string PrefabPath = "Assets/Characters/Hero/HeroArm2.prefab";
        public const string MatDir = "Assets/Characters/Hero/Arm2Materials";
        public const string SandboxPath = "Assets/Scenes/HeroArm2Sandbox.unity";

        static readonly Color BodyC = new Color(0.80f, 0.62f, 0.42f), FaceC = new Color(0.95f, 0.90f, 0.75f);
        static readonly Color GroundC = new Color(0.20f, 0.21f, 0.22f), WallC = new Color(0.55f, 0.45f, 0.36f);
        static readonly Color StepA = new Color(0.44f, 0.46f, 0.50f), StepB = new Color(0.50f, 0.52f, 0.56f), RampC = new Color(0.47f, 0.42f, 0.36f);
        static readonly Color PlatC = new Color(0.30f, 0.55f, 0.70f), ItemC = new Color(0.72f, 0.52f, 0.30f), ItemC2 = new Color(0.45f, 0.60f, 0.40f);

        static Material MatAsset(string n, Color c)
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Characters/Hero", "Arm2Materials");
            string p = $"{MatDir}/{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = Fortress.Rendering.FortRender.NewLit(c, 0.25f, n); AssetDatabase.CreateAsset(m, p); }
            return m;
        }

        /// <summary>Рисунок-кубик без коллайдера (коллайдер — на теле физики, не на рисунке).</summary>
        static Transform Visual(string n, Transform parent, Vector3 localPos, Vector3 size, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = localPos; g.transform.localScale = size;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            return g.transform;
        }

        [MenuItem("Fortress/Hero2/Build Hero Prefab (новый герой)")]
        public static void BuildPrefab()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Arm2] префаб: выйди из Play"); return; }
            var root = new GameObject("HeroArm2");
            var hero = root.AddComponent<Arm2Hero>();
            hero.input = root.AddComponent<Arm2Input>();
            root.AddComponent<Arm2Tuning>().prefabPath = PrefabPath;

            // ТЕЛО: Rigidbody + коробка 0.3 × 0.2 × 0.3 без масштаба (суставам руки нужен масштаб 1); рисунок — дети
            var bodyGo = new GameObject("Тело");
            bodyGo.transform.SetParent(root.transform, false);
            bodyGo.transform.localPosition = new Vector3(0f, Arm2Body.Height * 0.5f, 0f);
            bodyGo.AddComponent<Rigidbody>();
            // три капсулы вдоль «вперёд»: две нижние рядом и верхняя по центру (см. Arm2Body)
            var mBody = MatAsset("Arm2 тело", BodyC);
            foreach (var c in new[] { new Vector3(-Arm2Body.SideX, Arm2Body.LowY, 0f), new Vector3(Arm2Body.SideX, Arm2Body.LowY, 0f), new Vector3(0f, Arm2Body.TopY, 0f) })
            {
                var cap = bodyGo.AddComponent<CapsuleCollider>(); cap.direction = 2; cap.radius = Arm2Body.Radius; cap.height = Arm2Body.Length; cap.center = c;
                var v = Capsule("Тело: рисунок", bodyGo.transform, -Arm2Body.Length * 0.5f, Arm2Body.Length * 0.5f, Arm2Body.Radius, mBody);
                v.localPosition += c;
            }
            hero.body = bodyGo.AddComponent<Arm2Body>();
            Visual("Тело: лицо", bodyGo.transform, new Vector3(0f, 0.06f, 0.1f), new Vector3(0.1f, 0.05f, 0.06f), MatAsset("Arm2 лицо", FaceC));

            BuildArm(root, hero, bodyGo);

            var camGo = new GameObject("Камера") { tag = "MainCamera" };
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.fieldOfView = 50f;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.13f); cam.clearFlags = CameraClearFlags.SolidColor;
            var orbit = camGo.AddComponent<Arm2Camera>();
            orbit.target = hero.body; hero.cam = orbit;
            hero.intent.cam = cam;
            {
                var rot = Quaternion.Euler(orbit.pitch, 0f, 0f);
                Vector3 focus = bodyGo.transform.position + Vector3.up * orbit.lookHeight;
                camGo.transform.SetPositionAndRotation(focus - rot * Vector3.forward * orbit.distance, rot);
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[Arm2] префаб героя: " + PrefabPath);
        }

        // ---------- РУКА (срез 2): два звена-тела на суставах, призрак-цель, привод кисти ----------
        static readonly Color ArmC = new Color(0.72f, 0.56f, 0.40f), HandC = new Color(0.90f, 0.74f, 0.55f), GhostC = new Color(0.55f, 0.85f, 1f);
        const float ArmR = 0.02f, HandR = 0.035f;

        static Transform Capsule(string n, Transform parent, float z0, float z1, float r, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Capsule); g.name = n;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = new Vector3(0f, 0f, (z0 + z1) * 0.5f);
            g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            g.transform.localScale = new Vector3(r * 2f, (z1 - z0) * 0.5f, r * 2f);
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            return g.transform;
        }

        static ConfigurableJoint Ball(Rigidbody rb, Rigidbody to, bool collide)
        {
            var j = rb.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = to; j.anchor = Vector3.zero; j.autoConfigureConnectedAnchor = true;
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
            j.enableCollision = collide;
            j.projectionMode = JointProjectionMode.PositionAndRotation; j.projectionDistance = 0.005f; j.projectionAngle = 180f;
            return j;
        }

        static void BuildArm(GameObject root, Arm2Hero hero, GameObject bodyGo)
        {
            var intent = root.AddComponent<Arm2Intent>(); intent.hero = hero; hero.intent = intent;
            intent.handRadius = HandR;
            int armLayer = LayerMask.NameToLayer("HeroArm");
            float l1 = intent.upperArm, l2 = intent.foreArm;
            // поза покоя: плечо справа над телом, кисть на полу впереди плеча, локоть вбок
            Vector3 S = bodyGo.transform.TransformPoint(intent.shoulderLocal);
            Vector3 Hd = new Vector3(S.x, HandR, S.z + 0.42f);
            Vector3 dir = (Hd - S).normalized; float d = Vector3.Distance(S, Hd);
            float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d), h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 pole = new Vector3(1f, 0.7f, 0f).normalized; Vector3 perp = (pole - dir * Vector3.Dot(pole, dir)).normalized;   // как Arm2Arm.elbowPole
            Vector3 E = S + dir * a + perp * h;

            GameObject Link(string n, Vector3 pos, Vector3 to)
            {
                var g = new GameObject(n); g.transform.SetParent(root.transform, false);
                g.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(to - pos, Vector3.up));
                if (armLayer >= 0) g.layer = armLayer;
                g.AddComponent<Rigidbody>();
                return g;
            }
            var mArm = MatAsset("Arm2 рука", ArmC); var mHand = MatAsset("Arm2 кисть", HandC);

            var up = Link("Рука: плечевое звено", S, E);
            var upCol = up.AddComponent<CapsuleCollider>(); upCol.direction = 2; upCol.radius = ArmR;
            upCol.center = new Vector3(0f, 0f, (0.05f + l1) * 0.5f); upCol.height = l1 - 0.05f;      // у корня плеча коллайдера нет (урок 9)
            Capsule("рисунок", up.transform, 0f, l1, ArmR, mArm);

            var fo = Link("Рука: предплечье с кистью", E, Hd);
            var foCol = fo.AddComponent<CapsuleCollider>(); foCol.direction = 2; foCol.radius = ArmR;
            foCol.center = new Vector3(0f, 0f, l2 * 0.5f); foCol.height = l2;
            var handCol = fo.AddComponent<SphereCollider>(); handCol.center = new Vector3(0f, 0f, l2); handCol.radius = HandR;
            Capsule("рисунок", fo.transform, 0f, l2, ArmR, mArm);
            var hv = GameObject.CreatePrimitive(PrimitiveType.Sphere); hv.name = "кисть: рисунок";
            Object.DestroyImmediate(hv.GetComponent<Collider>());
            hv.transform.SetParent(fo.transform, false); hv.transform.localPosition = new Vector3(0f, 0f, l2); hv.transform.localScale = Vector3.one * HandR * 2f;
            hv.GetComponent<MeshRenderer>().sharedMaterial = mHand;

            var gv = GameObject.CreatePrimitive(PrimitiveType.Sphere); gv.name = "Призрак кисти: рисунок";
            Object.DestroyImmediate(gv.GetComponent<Collider>());
            gv.transform.SetParent(root.transform, false); gv.transform.position = Hd; gv.transform.localScale = Vector3.one * HandR * 1.4f;
            var gm = MatAsset("Arm2 призрак", GhostC); gv.GetComponent<MeshRenderer>().sharedMaterial = gm;
            gv.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gv.SetActive(false);

            var arm = root.AddComponent<Arm2Arm>(); arm.hero = hero; hero.arm = arm;
            arm.upper = up.GetComponent<Rigidbody>(); arm.fore = fo.GetComponent<Rigidbody>();
            arm.tipLocal = new Vector3(0f, 0f, l2);
            arm.colliders = new Collider[] { upCol, foCol, handCol };
            arm.ghostVisual = gv.transform;
            arm.shoulderJoint = Ball(arm.upper, bodyGo.GetComponent<Rigidbody>(), true);   // плечевое звено с телом сталкивается: рука не сквозь своё тело
            arm.elbowJoint = Ball(arm.fore, arm.upper, false);
            var dj = fo.AddComponent<ConfigurableJoint>();
            // привод кисти: от тела; точку на теле каждый шаг ставит Arm2Arm (цель кисти в координатах тела)
            var bodyRb = bodyGo.GetComponent<Rigidbody>();
            dj.connectedBody = bodyRb; dj.anchor = arm.tipLocal; dj.autoConfigureConnectedAnchor = false;
            dj.connectedAnchor = bodyGo.transform.InverseTransformPoint(Hd); dj.enableCollision = true;       // предплечье с телом сталкивается
            dj.xMotion = dj.yMotion = dj.zMotion = ConfigurableJointMotion.Free;
            dj.angularXMotion = dj.angularYMotion = dj.angularZMotion = ConfigurableJointMotion.Free;
            dj.targetPosition = Vector3.zero;
            arm.driveJoint = dj;
            // привод локтя: тоже от тела — конец плечевого звена тянется к расчётной точке локтя
            var ej = up.AddComponent<ConfigurableJoint>();
            ej.connectedBody = bodyRb; ej.anchor = new Vector3(0f, 0f, l1); ej.autoConfigureConnectedAnchor = false;
            ej.connectedAnchor = bodyGo.transform.InverseTransformPoint(E); ej.enableCollision = true;
            ej.xMotion = ej.yMotion = ej.zMotion = ConfigurableJointMotion.Free;
            ej.angularXMotion = ej.angularYMotion = ej.angularZMotion = ConfigurableJointMotion.Free;
            ej.targetPosition = Vector3.zero;
            arm.elbowDrive = ej;

            var guard = root.AddComponent<Arm2ItemGuard>(); guard.hero = hero; guard.arm = arm; hero.guard = guard;
            // хват: касание кисти — события столкновений предплечья; блок хвата — на корне
            var hc = fo.AddComponent<Arm2HandContact>(); hc.hand = handCol; hc.heroRoot = root.transform;
            var grip = root.AddComponent<Arm2Grip>(); grip.hero = hero; grip.contact = hc; hero.grip = grip;
            hero.body.hero = hero;
            var bc = bodyGo.AddComponent<Arm2BodyContact>(); bc.heroRoot = root.transform; arm.bodyContact = bc;

            // КОРЕНЬ ПЛЕЧА закрыт коллайдером тела: плечо торчит из коробки на 5 см вбок и 4 см вверх, а коллайдер звена начинается
            // ещё через 5 см — без этого тело могло бы завести точку сустава в стену (боком к стене, под навесом), и запертый
            // сустав потащил бы звено в твёрдое. Сфера не касается капсулы звена (3 см против начала капсулы в 5 см).
            var sh = bodyGo.AddComponent<SphereCollider>(); sh.center = intent.shoulderLocal; sh.radius = 0.03f;
            var shv = GameObject.CreatePrimitive(PrimitiveType.Sphere); shv.name = "Тело: плечо";
            Object.DestroyImmediate(shv.GetComponent<Collider>());
            shv.transform.SetParent(bodyGo.transform, false); shv.transform.localPosition = intent.shoulderLocal; shv.transform.localScale = Vector3.one * 0.06f;
            shv.GetComponent<MeshRenderer>().sharedMaterial = mArm;
        }

        // ---------- кирпичи песочницы: всё твёрдое — с коллайдером ----------
        static Transform Solid(string n, Vector3 center, Vector3 size, Quaternion rot, Color c, Transform parent)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n;                 // BoxCollider остаётся
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(center, rot); g.transform.localScale = size;
            g.GetComponent<MeshRenderer>().sharedMaterial = HeroProtoBuilder.Mat(n, c);
            g.isStatic = true;
            return g.transform;
        }
        static Transform Box(string n, float x0, float x1, float y0, float y1, float z0, float z1, Color c, Transform parent)
            => Solid(n, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, y1 - y0, z1 - z0), Quaternion.identity, c, parent);

        static void Sign(string text, Vector3 pos, Transform parent)
        {
            var g = new GameObject("Подпись: " + text);
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(pos + Vector3.up * 0.01f, Quaternion.Euler(70f, 0f, 0f));
            var tm = g.AddComponent<TextMesh>();
            tm.text = text; tm.fontSize = 64; tm.characterSize = 0.02f; tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center; tm.color = new Color(0.95f, 0.93f, 0.85f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font; g.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        static Arm2Mover Mover(string n, Vector3 center, Vector3 size, Transform parent)
        {
            var t = Solid(n, center, size, Quaternion.identity, PlatC, parent);
            t.gameObject.isStatic = false;
            t.gameObject.AddComponent<Rigidbody>().isKinematic = true;
            return t.gameObject.AddComponent<Arm2Mover>();
        }

        static void Item(string n, Vector3 center, float size, float mass, Color c, Transform parent)
        {
            var t = Solid(n, center, Vector3.one * size, Quaternion.identity, c, parent);
            t.gameObject.isStatic = false;
            int layer = LayerMask.NameToLayer("Item"); if (layer >= 0) t.gameObject.layer = layer;
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        // ---------- ПРЕГРАДЫ ДЛЯ РУКИ (станции Z X C V B) ----------
        static readonly Color ObA = new Color(0.58f, 0.50f, 0.40f), ObB = new Color(0.42f, 0.50f, 0.56f), ObC = new Color(0.52f, 0.56f, 0.44f), ObD = new Color(0.60f, 0.44f, 0.44f);

        /// <summary>Коробка в координатах станции (O — место героя, лицом на +Z).</summary>
        static Transform OB(string n, Vector3 O, float x0, float x1, float y0, float y1, float z0, float z1, Color c, Transform parent)
            => Box(n, O.x + x0, O.x + x1, O.y + y0, O.y + y1, O.z + z0, O.z + z1, c, parent);

        static Transform Ball(string n, Vector3 centre, float radius, Color c, Transform parent)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.name = n;                // SphereCollider остаётся
            g.transform.SetParent(parent, false); g.transform.position = centre; g.transform.localScale = Vector3.one * radius * 2f;
            g.GetComponent<MeshRenderer>().sharedMaterial = HeroProtoBuilder.Mat(n, c); g.isStatic = true;
            return g.transform;
        }

        /// <summary>Цилиндр от a до b: коллайдер — выпуклая сетка (у примитива Unity это капсула, а нужны плоские торцы).</summary>
        static Transform Rod(string n, Vector3 a, Vector3 b, float radius, Color c, Transform parent)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.name = n;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, (b - a).normalized));
            g.transform.localScale = new Vector3(radius * 2f, Vector3.Distance(a, b) * 0.5f, radius * 2f);
            g.AddComponent<MeshCollider>().convex = true;
            g.GetComponent<MeshRenderer>().sharedMaterial = HeroProtoBuilder.Mat(n, c); g.isStatic = true;
            return g.transform;
        }

        /// <summary>Рельеф — сетка высот с коллайдером-сеткой (Arm2Sandbox.ReliefHeight).</summary>
        static void Relief(string n, Vector3 O, Color c, Transform parent)
        {
            const float step = 0.04f;
            int nx = Mathf.RoundToInt((Arm2Sandbox.ReliefX1 - Arm2Sandbox.ReliefX0) / step) + 1, nz = Mathf.RoundToInt((Arm2Sandbox.ReliefZ1 - Arm2Sandbox.ReliefZ0) / step) + 1;
            var v = new Vector3[nx * nz]; var tri = new int[(nx - 1) * (nz - 1) * 6];
            for (int iz = 0; iz < nz; iz++) for (int ix = 0; ix < nx; ix++)
            {
                float x = Arm2Sandbox.ReliefX0 + ix * step, z = Arm2Sandbox.ReliefZ0 + iz * step;
                v[iz * nx + ix] = new Vector3(x, Arm2Sandbox.ReliefHeight(x, z) + 0.002f, z);
            }
            int t = 0;
            for (int iz = 0; iz < nz - 1; iz++) for (int ix = 0; ix < nx - 1; ix++)
            {
                int a = iz * nx + ix, b = a + 1, d = a + nx, e = d + 1;
                tri[t++] = a; tri[t++] = d; tri[t++] = b; tri[t++] = b; tri[t++] = d; tri[t++] = e;
            }
            var mesh = new Mesh { name = n, vertices = v, triangles = tri }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var g = new GameObject(n); g.transform.SetParent(parent, false); g.transform.position = O;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = HeroProtoBuilder.Mat(n, c);
            g.AddComponent<MeshCollider>().sharedMesh = mesh; g.isStatic = true;
        }

        static void BuildObstacles(Transform world, Transform decor)
        {
            Quaternion Tilt(float deg, float yaw = 0f) => Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-deg, 0f, 0f);
            for (int i = 0; i < Arm2Sandbox.ObstacleNames.Length; i++)
                Sign(Arm2Sandbox.ObstacleNames[i], new Vector3(Arm2Sandbox.ObstacleOrigin(i).x, 0f, Arm2Sandbox.ObstacleOrigin(i).z - 0.75f), decor);

            {   // Z — углы и щели: внутренний угол из двух стенок, щель 9 см (кисть 7 см), щель 5 см (кисть не пролезет), столбик, ниша
                Vector3 O = Arm2Sandbox.ObstacleOrigin(0);
                OB("Z стенка дальняя", O, 0.05f, 0.72f, 0f, 0.4f, 0.62f, 0.68f, ObA, world);
                OB("Z стенка правая", O, 0.66f, 0.72f, 0f, 0.4f, 0.15f, 0.62f, ObA, world);
                OB("Z щель 9: левый блок", O, -0.36f, -0.2f, 0f, 0.25f, 0.28f, 0.5f, ObB, world);
                OB("Z щель 9: правый блок", O, -0.11f, 0.02f, 0f, 0.25f, 0.28f, 0.5f, ObB, world);
                OB("Z щель 5: правый блок", O, 0.07f, 0.18f, 0f, 0.25f, 0.28f, 0.5f, ObC, world);
                OB("Z столбик", O, 0.38f, 0.46f, 0f, 0.5f, 0.3f, 0.38f, ObD, world);
                OB("Z ниша: крыша", O, 0.05f, 0.4f, 0.16f, 0.2f, 0.5f, 0.62f, ObD, world);       // под крышей у дальней стенки — ниша высотой 16 см
            }
            {   // X — ступеньки по 5 см, полка-навес на 20 см (под неё и на неё), столик 12 см, уступчики разной высоты
                Vector3 O = Arm2Sandbox.ObstacleOrigin(1);
                for (int k = 0; k < 7; k++)
                    OB($"X ступенька {k + 1}", O, -0.05f, 0.3f, 0f, 0.05f * (k + 1), 0.2f + k * 0.06f, 0.26f + k * 0.06f, k % 2 == 0 ? StepA : StepB, world);
                OB("X полка: стойка", O, 0.62f, 0.68f, 0f, 0.23f, 0.15f, 0.5f, ObA, world);
                OB("X полка: доска", O, 0.36f, 0.68f, 0.2f, 0.23f, 0.15f, 0.5f, ObA, world);
                OB("X столик", O, -0.42f, -0.14f, 0f, 0.12f, 0.2f, 0.42f, ObB, world);
                OB("X кубик на столике", O, -0.34f, -0.24f, 0.12f, 0.22f, 0.26f, 0.36f, ObD, world);
                OB("X порожек 2 см", O, -0.4f, 0.3f, 0f, 0.02f, 0.1f, 0.14f, ObC, world);
            }
            {   // C — рельеф (бугры до 16 см), купол, наклонные плиты 25° и 55°
                Vector3 O = Arm2Sandbox.ObstacleOrigin(2);
                Relief("C рельеф", O, ObC, world);
                Ball("C купол", O + new Vector3(0.52f, 0.02f, 0.3f), 0.14f, ObB, world);
                Solid("C плита 25°", O + new Vector3(0.6f, 0.07f, 0.62f), new Vector3(0.3f, 0.03f, 0.3f), Tilt(25f, -30f), ObA, world);
                Solid("C плита 55°", O + new Vector3(0.36f, 0.12f, 0.78f), new Vector3(0.3f, 0.03f, 0.3f), Tilt(55f), ObD, world);
            }
            {   // V — тонкая стенка 2 см, круглый столб, перекладина на 20 см, шар, арка с проёмом 14 см, наклонный шест
                Vector3 O = Arm2Sandbox.ObstacleOrigin(3);
                OB("V стенка 2 см", O, -0.3f, -0.28f, 0f, 0.3f, 0.15f, 0.6f, ObA, world);
                Rod("V столб", O + new Vector3(0.1f, 0f, 0.42f), O + new Vector3(0.1f, 0.5f, 0.42f), 0.04f, ObB, world);
                Rod("V перекладина", O + new Vector3(-0.28f, 0.2f, 0.62f), O + new Vector3(0.3f, 0.2f, 0.62f), 0.02f, ObD, world);
                OB("V стойка перекладины", O, 0.3f, 0.34f, 0f, 0.22f, 0.6f, 0.64f, ObD, world);
                Ball("V шар", O + new Vector3(-0.1f, 0.09f, 0.28f), 0.09f, ObC, world);
                OB("V арка: левая", O, 0.4f, 0.45f, 0f, 0.3f, 0.3f, 0.36f, ObA, world);
                OB("V арка: правая", O, 0.59f, 0.64f, 0f, 0.3f, 0.3f, 0.36f, ObA, world);
                OB("V арка: верх", O, 0.4f, 0.64f, 0.25f, 0.3f, 0.3f, 0.36f, ObA, world);
                Rod("V шест наклонный", O + new Vector3(0.7f, 0f, 0.2f), O + new Vector3(0.45f, 0.45f, 0.6f), 0.025f, ObB, world);
            }
            {   // B — помост 0.3: герой наверху; яма 30 × 26 см перед плечом, край помоста в 55 см впереди, ступень вниз справа
                Vector3 O = Arm2Sandbox.ObstacleOrigin(4); float h = Arm2Sandbox.PlatformH;
                Vector3 F = new Vector3(O.x, 0f, O.z);                                             // от пола
                OB("B помост: зад", F, -0.8f, 0.9f, 0f, h, -0.6f, 0.2f, StepB, world);
                OB("B помост: слева от ямы", F, -0.8f, 0.05f, 0f, h, 0.2f, 0.46f, StepB, world);
                OB("B помост: справа от ямы", F, 0.35f, 0.9f, 0f, h, 0.2f, 0.46f, StepB, world);
                OB("B помост: за ямой", F, -0.8f, 0.6f, 0f, h, 0.46f, 0.55f, StepB, world);       // перемычка 9 см, за ней край
                OB("B ступень вниз", F, 0.6f, 0.9f, 0f, 0.15f, 0.46f, 0.8f, StepA, world);
                OB("B бортик у края", F, -0.5f, -0.2f, h, h + 0.08f, 0.5f, 0.55f, ObA, world);
            }
        }

        [MenuItem("Fortress/Hero2/Build Sandbox (песочница)")]
        public static void BuildSandbox()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Arm2] песочница: выйди из Play"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildPrefab();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = new GameObject("Песочница: твёрдое").transform;
            var decor = new GameObject("Песочница: подписи").transform;

            Box("Пол", -8f, 9.5f, -0.5f, 0f, -7.5f, 8f, GroundC, world);
            Box("Стена", -1f, 1f, 0f, 1.2f, 4f, 4.2f, WallC, world);
            Box("Столб", 0.45f, 0.65f, 0f, 1.2f, 0.9f, 1.1f, WallC, world);
            Box("Ступень 0.3", -3.6f, -2.4f, 0f, 0.3f, 2f, 3.2f, StepA, world);
            Box("Уступ 0.5", -2f, -0.8f, 0f, 0.5f, 2f, 3.2f, StepB, world);
            Box("Ступень 0.4", -5.4f, -4.2f, 0f, 0.4f, 2f, 3.2f, StepB, world);
            Box("Площадка 1.2", Arm2Sandbox.PlatX - 1f, Arm2Sandbox.PlatX + 1f, 0f, Arm2Sandbox.PlatH, Arm2Sandbox.PlatZ0, Arm2Sandbox.PlatZ0 + 2.5f, StepA, world);
            Sign("площадка 1.2 (клавиша «−»)", new Vector3(Arm2Sandbox.PlatX, 0f, Arm2Sandbox.PlatZ0 - 0.5f), decor);
            Sign("ступень 0.4", new Vector3(-4.8f, 0f, 1.6f), decor);
            for (int i = 0; i < Arm2Sandbox.RampAngles.Length; i++)
            {
                Vector3 top = Arm2Sandbox.RampTop(i, out Vector3 n);
                Solid($"Склон {Arm2Sandbox.RampAngles[i]:0}°", top - n * (Arm2Sandbox.RampThick * 0.5f),
                      new Vector3(Arm2Sandbox.RampWidth, Arm2Sandbox.RampThick, Arm2Sandbox.RampLen),
                      Quaternion.Euler(-Arm2Sandbox.RampAngles[i], 0f, 0f), RampC, world);
                Sign($"склон {Arm2Sandbox.RampAngles[i]:0}°", new Vector3(Arm2Sandbox.RampX(i), 0f, 1.5f), decor);
            }
            {   // ЗАЦЕПЫ (срез 7): камни на склоне 45° и перила вдоль склона 36° — только для руки (тело сквозь них проходит)
                int i36 = Arm2Sandbox.RampIndex(36f);
                foreach (float ang in new[] { 45f, 60f })
                {
                    int ia = Arm2Sandbox.RampIndex(ang); Arm2Sandbox.RampTop(ia, out Vector3 na);
                    foreach (float al in Arm2Sandbox.NubAlong)
                    {
                        var nub = Solid($"Камень {ang:0}° {al:0.00}", Arm2Sandbox.RampPoint(ia, al, Arm2Sandbox.NubSide) + na * 0.025f, new Vector3(0.14f, 0.05f, 0.06f), Quaternion.Euler(-ang, 0f, 0f), ObD, world);
                        nub.gameObject.isStatic = false; nub.gameObject.AddComponent<Fortress.Hero.Proto.ProtoNub>();
                    }
                }
                Arm2Sandbox.RampTop(i36, out Vector3 n36);
                Vector3 r0 = Arm2Sandbox.RampPoint(i36, 0.1f, Arm2Sandbox.RailSide) + n36 * Arm2Sandbox.RailHeight, r1 = Arm2Sandbox.RampPoint(i36, 1.9f, Arm2Sandbox.RailSide) + n36 * Arm2Sandbox.RailHeight;
                var rail = Rod("Перила", r0, r1, 0.02f, ObB, world);
                rail.gameObject.isStatic = false;
                var rh = rail.gameObject.AddComponent<Fortress.Hero.Proto.ProtoHold>(); rh.a = new Vector3(0f, -1f, 0f); rh.b = new Vector3(0f, 1f, 0f);
                foreach (float al in new[] { 0.1f, 1.0f, 1.9f })
                {
                    Vector3 basePt = Arm2Sandbox.RampPoint(i36, al, Arm2Sandbox.RailSide);
                    var post = Rod($"Перила: стойка {al:0.0}", basePt, basePt + n36 * Arm2Sandbox.RailHeight, 0.012f, ObB, world);
                    Object.DestroyImmediate(post.GetComponent<Collider>());                              // стойки — рисунок
                }
            }
            var shuttle = Mover(Arm2Sandbox.ShuttleName, Arm2Sandbox.ShuttlePos, new Vector3(1.2f, 0.2f, 1.2f), world);
            shuttle.axis = Vector3.right; shuttle.travel = 4f; shuttle.speed = 0.5f; shuttle.accel = 1f;
            var spinner = Mover(Arm2Sandbox.SpinnerName, Arm2Sandbox.SpinnerPos, new Vector3(1.6f, 0.2f, 1.6f), world);
            spinner.travel = 0f; spinner.spin = 30f;
            Item("Кубик 0.12", new Vector3(-0.45f, 0.06f, 0.8f), 0.12f, 0.5f, ItemC, world);
            Item("Кубик 0.15", new Vector3(-0.75f, 0.075f, 0.6f), 0.15f, 1f, ItemC2, world);
            Sign("ступень 0.3", new Vector3(-3f, 0f, 1.6f), decor); Sign("уступ 0.5", new Vector3(-1.4f, 0f, 1.6f), decor);
            BuildObstacles(world, decor);
            Sign("челнок", new Vector3(-1f, 0f, -3f), decor); Sign("карусель", new Vector3(3f, 0f, -3.1f), decor);

            var sun = new GameObject("Свет").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            var hero = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            new GameObject("Песочница: клавиши 1…9").AddComponent<Arm2Sandbox>();
            EditorSceneManager.SaveScene(scene, SandboxPath);
            // атмосфера HDRP новой сцены сохраняется как Untitled — дать ей имя сцены (как у полигона руки)
            const string Unt = "Assets/Settings/HDRP/Scenes/Untitled_Atmosphere.asset", Own = "Assets/Settings/HDRP/Scenes/HeroArm2Sandbox_Atmosphere.asset";
            if (AssetDatabase.LoadMainAssetAtPath(Unt) != null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(Own) != null) AssetDatabase.DeleteAsset(Own);
                AssetDatabase.MoveAsset(Unt, Own);
                AssetDatabase.SaveAssets();
            }
            Debug.Log("[Arm2] песочница: " + SandboxPath + " (герой: " + hero.name + ")");
        }
    }
}
