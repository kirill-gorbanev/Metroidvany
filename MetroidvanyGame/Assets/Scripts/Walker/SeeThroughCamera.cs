using UnityEngine;
using UnityEngine.Rendering;

namespace Fortress.Walker
{
    /// <summary>Разрез, когда камеру заслоняет (стена или потолок между ней и героем, камера в толще стены или под землёй) —
    /// «кукольный домик» (Kits/Shaders/SeeThrough.hlsl):
    /// вне комнаты героя (между ближними стенами, от его пола до его потолка) не рисуется ничего, фон тёмный —
    /// куски других мест не видны, за стену не подглядеть; внутри комнаты всё, что ближе героя к камере, убрано, край — сеткой.
    /// Под открытым небом (нет потолка) комнаты нет — только разрез ближе героя.
    /// Камера (OrbitCamera) при этом подтягивается к стене, но не ближе своего минимума.
    /// Висит на камере; значения ставит только для своей камеры (окно сцены и зонды — без разреза).</summary>
    [DefaultExecutionOrder(200)]
    public class SeeThroughCamera : MonoBehaviour
    {
        public Transform target;
        [Tooltip("Разрез на столько ближе центра героя, м (стол рядом с ним остаётся)")] public float margin = 0.15f;
        [Tooltip("Ширина сетчатого края разреза, м")] public float band = 0.2f;
        [Tooltip("Разрез появляется/уходит за, с")] public float fade = 0.15f;
        [Tooltip("Растворение сеткой у границы комнаты: от внутренних лиц стен наружу, м")] public float wallPad = 0.5f;
        [Tooltip("Фон вне комнаты героя")] public Color voidColor = new Color(0.02f, 0.018f, 0.016f);

        static readonly int IdA = Shader.PropertyToID("_SeeThroughA"), IdB = Shader.PropertyToID("_SeeThroughB"),
            IdC = Shader.PropertyToID("_SeeThroughC");
        Camera cam;
        float strength;
        Vector4 a, b, cc;
        bool enclosed, terrainHidden, voidOn;
        CameraClearFlags clear0;
        Color bg0;
        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly RaycastHit[] wallHits = new RaycastHit[24];
        readonly Collider[] inside = new Collider[8];

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            if (cam && !voidOn) { clear0 = cam.clearFlags; bg0 = cam.backgroundColor; }
            RenderPipelineManager.beginCameraRendering += Begin;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            Shader.SetGlobalVector(IdC, Vector4.zero);
            SetTerrainHidden(false);
            SetVoid(false);
            HidePeopleOutside(false);
        }

        bool Ignored(Collider c) =>
            c.transform.IsChildOf(target) || c.attachedRigidbody || c is CharacterController;

        bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; float len = d.magnitude;
            if (len < 0.05f) return false;
            int n = Physics.SphereCastNonAlloc(from, 0.12f, d / len, hits, len, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (Ignored(hits[i].collider)) continue;                             // сам герой, люди, мелочь на физике
                if (hits[i].distance <= 0f) continue;                                // начало внутри коллайдера
                return true;
            }
            return false;
        }

        /// <summary>Камера в сплошном (земляной массив, коробка стены).</summary>
        bool InsideSolid(Vector3 c)
        {
            int n = Physics.OverlapSphereNonAlloc(c, 0.1f, inside, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!Ignored(inside[i])) return true;
            return false;
        }

        /// <summary>Точка под поверхностью земли (Terrain).</summary>
        static bool Underground(Vector3 c)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                Vector3 tp = t.transform.position, size = t.terrainData.size;
                if (c.x < tp.x || c.z < tp.z || c.x > tp.x + size.x || c.z > tp.z + size.z) continue;
                if (c.y < tp.y + t.SampleHeight(c) - 0.05f) return true;
            }
            return false;
        }

        /// <summary>До ближней стены от точки по направлению (мебель, люди, решётки на физике не в счёт), м.</summary>
        float ToWall(Vector3 from, Vector3 dir, float max)
        {
            int n = Physics.RaycastNonAlloc(from, dir, wallHits, max, ~0, QueryTriggerInteraction.Ignore);
            float best = max;
            for (int i = 0; i < n; i++)
            {
                var c = wallHits[i].collider;
                if (Ignored(c) || wallHits[i].distance >= best) continue;
                var fe = c.GetComponentInParent<Fortress.Grid.FortElement>();
                if (fe && (fe.kind == Fortress.Grid.ElementKind.Prop || fe.kind == Fortress.Grid.ElementKind.WallProp
                           || fe.kind == Fortress.Grid.ElementKind.Light)) continue;
                best = wallHits[i].distance;
            }
            return best;
        }

        // ---- комнаты из разметки уровня ----
        // FortElement.room — буква зоны карты (одна буква встречается в разных местах), поэтому комната — связные клетки
        // пола одной буквы на этаже; та же буква прямо этажом выше/ниже (галерея над залом) — та же комната.
        const float Storey = 3f;
        System.Collections.Generic.Dictionary<Vector3Int, (string room, Vector3 pos)> cells;
        Vector3Int roomCell = new Vector3Int(int.MinValue, 0, 0);
        bool roomOk;
        Vector3 roomMin, roomMax;      // центры крайних клеток комнаты (x, z), низший и высший пол (y)

        void BuildCells()
        {
            cells = new System.Collections.Generic.Dictionary<Vector3Int, (string, Vector3)>();
            foreach (var fe in FindObjectsByType<Fortress.Grid.FortElement>(FindObjectsSortMode.None))
            {
                if (fe.kind != Fortress.Grid.ElementKind.Floor || string.IsNullOrEmpty(fe.room) || fe.room == "OUT") continue;
                Vector3 q = fe.transform.position;
                if (cells.TryGetValue(fe.cell, out var had) && had.pos.y >= q.y) continue;   // изнанка настила ниже — не пол
                cells[fe.cell] = (fe.room, q);
            }
        }

        /// <summary>Комната под героем (клетка пола под ним → связные клетки). Вернёт, известна ли комната.</summary>
        bool UpdateRoom(Vector3 p, Vector3 chest)
        {
            if (cells == null) BuildCells();
            Vector3Int cell = roomCell; bool found = false; float nearest = float.MaxValue;
            int n = Physics.RaycastNonAlloc(chest, Vector3.down, wallHits, 4f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (wallHits[i].distance >= nearest) continue;
                var fe = wallHits[i].collider.GetComponentInParent<Fortress.Grid.FortElement>();
                if (!fe || fe.kind != Fortress.Grid.ElementKind.Floor || !cells.ContainsKey(fe.cell)) continue;
                nearest = wallHits[i].distance; cell = fe.cell; found = true;
            }
            if (!found)
            {   // на лестнице, предмете: прежняя комната, пока герой в её пределах
                bool inLast = roomOk && p.x > roomMin.x - 1.5f && p.x < roomMax.x + 1.5f && p.z > roomMin.z - 1.5f && p.z < roomMax.z + 1.5f
                              && p.y > roomMin.y - 1f && p.y < roomMax.y + Storey;
                if (!inLast) roomOk = false;
                return roomOk;
            }
            if (cell == roomCell && roomOk) return true;
            roomCell = cell;
            string letter = cells[cell].room;
            var seen = new System.Collections.Generic.HashSet<Vector3Int> { cell };
            var queue = new System.Collections.Generic.Queue<Vector3Int>();
            queue.Enqueue(cell);
            roomMin = Vector3.one * float.MaxValue; roomMax = Vector3.one * float.MinValue;
            var steps = new[] { new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
                                new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
            while (queue.Count > 0 && seen.Count < 2000)
            {
                var c = queue.Dequeue();
                Vector3 q = cells[c].pos;
                roomMin = Vector3.Min(roomMin, q); roomMax = Vector3.Max(roomMax, q);
                foreach (var s in steps)
                {
                    var nb = c + s;
                    if (seen.Contains(nb) || !cells.TryGetValue(nb, out var v) || v.room != letter) continue;
                    seen.Add(nb); queue.Enqueue(nb);
                }
            }
            roomOk = true;
            return true;
        }

        void SetTerrainHidden(bool hide)
        {
            if (hide == terrainHidden) return;
            terrainHidden = hide;
            foreach (var t in Terrain.activeTerrains) t.drawHeightmap = !hide;
        }

        void SetVoid(bool on)
        {
            if (on == voidOn || !cam) return;
            voidOn = on;
            if (on) { clear0 = cam.clearFlags; bg0 = cam.backgroundColor; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = voidColor; }
            else { cam.clearFlags = clear0; cam.backgroundColor = bg0; }
        }

        void LateUpdate()
        {
            if (!cam || !target) return;
            Vector3 c = cam.transform.position, p = target.position;
            Vector3 head = p + Vector3.up * 1.5f, chest = p + Vector3.up * 1.0f;
            bool under = Underground(c) && Underground(head);
            // заслоняет: луч камера → герой (голова, грудь, колени), герой → камера (камера в толще стены),
            // камера в сплошном или под землёй вместе с героем
            bool blocked = under || InsideSolid(c) || Blocked(c, head) || Blocked(c, p + Vector3.up * 0.95f)
                           || Blocked(c, p + Vector3.up * 0.35f) || Blocked(head, c);
            strength = Mathf.MoveTowards(strength, blocked ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, fade));

            // комната героя — из разметки уровня (клетки пола одной буквы зоны, связные; галерея над залом — с ним);
            // под крышей — «кукольный домик», под открытым небом — только разрез ближе героя
            bool roofed = Physics.Raycast(head, Vector3.up, out var up, 12f, ~0, QueryTriggerInteraction.Ignore) && !Ignored(up.collider);
            bool known = UpdateRoom(p, chest);
            enclosed = roofed;
            float ceil;
            if (enclosed && known)
            {
                // коробка = внутренние лица стен (клетки ±1 м, стена по ребру ~0.2 м внутрь), пола, потолка + wallPad:
                // от лиц наружу — растворение сеткой
                float inX0 = roomMin.x - 0.8f, inX1 = roomMax.x + 0.8f, inZ0 = roomMin.z - 0.8f, inZ1 = roomMax.z + 0.8f;
                ceil = roomMax.y + Storey - 0.25f;
                b = new Vector4(inX0 - wallPad, inZ0 - wallPad, inX1 + wallPad, inZ1 + wallPad);
                cc = new Vector4(strength, roomMin.y - 0.05f - wallPad, ceil + wallPad, wallPad);
            }
            else if (enclosed)
            {   // комнаты в разметке нет (на лестнице между этажами и т. п.) — ближние стены по осям сетки
                float xm = ToWall(chest, Vector3.left, 30f), xp = ToWall(chest, Vector3.right, 30f);
                float zm = ToWall(chest, Vector3.back, 30f), zp = ToWall(chest, Vector3.forward, 30f);
                ceil = up.point.y;
                b = new Vector4(p.x - xm - wallPad, p.z - zm - wallPad, p.x + xp + wallPad, p.z + zp + wallPad);
                cc = new Vector4(strength, p.y - 0.05f - wallPad, ceil + wallPad, wallPad);
            }
            else
            {
                ceil = 1e5f;
                b = new Vector4(-1e5f, -1e5f, 1e5f, 1e5f);
                cc = new Vector4(strength, -1e5f, 1e5f, 1f);
            }
            float depth = Vector3.Dot(p + Vector3.up * 0.95f - c, cam.transform.forward);
            a = new Vector4(Mathf.Max(0f, depth - margin) * strength, band, p.y + 0.03f, enclosed ? ceil - 0.03f : 1e5f);

            bool room = strength > 0.001f && enclosed;
            SetTerrainHidden(under || room);       // земля разрезом не режется: в «кукольном домике» её нет
            SetVoid(room);
            HidePeopleOutside(room && strength > 0.5f);
        }

        // люди (их материалы — в моделях персонажей, шейдер не наш): в «кукольном домике» вне комнаты героя не рисуются
        Animator[] people;
        float peopleT;
        readonly System.Collections.Generic.List<Renderer> hiddenPeople = new System.Collections.Generic.List<Renderer>();

        void HidePeopleOutside(bool on)
        {
            foreach (var r in hiddenPeople) if (r) r.forceRenderingOff = false;
            hiddenPeople.Clear();
            if (!on) return;
            if (people == null || Time.time > peopleT) { people = FindObjectsByType<Animator>(FindObjectsSortMode.None); peopleT = Time.time + 2f; }
            foreach (var an in people)
            {
                if (!an || an.transform.IsChildOf(target)) continue;
                Vector3 q = an.transform.position;
                float pad = cc.w * 0.5f;      // человек в толще стены у границы — уже снаружи
                bool outside = q.x < b.x + pad || q.x > b.z - pad || q.z < b.y + pad || q.z > b.w - pad || q.y < cc.y - 0.2f || q.y > cc.z;
                if (!outside) continue;
                foreach (var r in an.GetComponentsInChildren<Renderer>())
                    if (!r.forceRenderingOff) { r.forceRenderingOff = true; hiddenPeople.Add(r); }
            }
        }

        void Begin(ScriptableRenderContext ctx, Camera c)
        {
            bool mine = c == cam && strength > 0.001f;
            Shader.SetGlobalVector(IdC, mine ? cc : Vector4.zero);
            Shader.SetGlobalVector(IdA, a);
            Shader.SetGlobalVector(IdB, b);
        }
    }
}
