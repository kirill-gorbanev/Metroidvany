using UnityEngine;

namespace Fortress.Grid
{
    /// <summary>Люк в полу: закрыт — по крышке ходят; E — откинуть на петле (сверху и снизу со стремянки).
    /// boltUp: засов сверху — снизу люк не открыть, пока его не отопрут сверху (петля A: «я вернулся»).</summary>
    public class Trapdoor : Interactable
    {
        [Tooltip("Сторона петли: N/E/S/W")] public string hinge = "S";
        public float openAngle = 105f;
        public float speed = 220f;
        public bool locked;
        [Tooltip("Задвинут засов сверху")] public bool boltUp;
        Vector3 pivot, axis; Quaternion rot0; Vector3 pos0;
        float angle, target;

        void Awake()
        {
            reach = 2.3f;
            pos0 = transform.position; rot0 = transform.rotation;
            Vector3 d = hinge switch { "N" => Vector3.forward, "E" => Vector3.right, "W" => Vector3.left, _ => Vector3.back };
            // петля — край крышки со стороны d (по габариту коллайдера/рендера)
            Bounds b = default; bool has = false;
            foreach (var r in GetComponentsInChildren<Renderer>()) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
            Vector3 c = has ? b.center : pos0;
            float ext = has ? Vector3.Dot(b.extents, new Vector3(Mathf.Abs(d.x), 0, Mathf.Abs(d.z))) : 1f;
            pivot = new Vector3(c.x, has ? b.min.y : pos0.y, c.z) + d * ext;
            axis = Vector3.Cross(d, Vector3.up).normalized; // крышка поднимается, отходя от петли вверх
        }

        bool Above(Transform who) => who.position.y > pos0.y - 0.4f;
        public bool IsOpen => target > 1f;
        public void Open() { if (!locked && !boltUp) target = openAngle; }

        public override string Prompt => locked ? "Люк заперт" : (target > 1f ? "E — закрыть люк" : "E — открыть люк");
        public override string PromptFor(Transform who)
        {
            if (boltUp) return Above(who) ? "E — отодвинуть засов люка" : "Люк заперт на засов сверху";
            return Prompt;
        }

        public override void Interact(Transform who)
        {
            if (locked) return;
            if (boltUp)
            {
                if (!Above(who)) return;
                boltUp = false;
            }
            target = target > 1f ? 0f : openAngle;
        }

        void Update()
        {
            if (Mathf.Approximately(angle, target)) return;
            angle = Mathf.MoveTowards(angle, target, speed * Time.deltaTime);
            var q = Quaternion.AngleAxis(-angle, axis);
            transform.SetPositionAndRotation(pivot + q * (pos0 - pivot), q * rot0);
        }
    }
}
