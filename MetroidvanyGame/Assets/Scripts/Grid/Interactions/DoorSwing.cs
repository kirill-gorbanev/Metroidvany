using UnityEngine;

namespace Fortress.Grid
{
    /// <summary>Дверь на петле (корень префаба стоит на петле): E — открыть от себя / закрыть.
    /// Засов (boltSide != 0): пока задвинут, открыть можно только с его стороны — «отодвинуть засов»; потом дверь обычная.
    /// Стража открывает двери сама (OpenAwayFrom/Close), засовы и запертые — нет.</summary>
    public class DoorSwing : Interactable
    {
        public float openAngle = 100f;
        public float speed = 240f;
        public bool locked;
        [Tooltip("Засов задвинут")] public bool bolted;
        [Tooltip("Куда от двери сторона засова (мировое направление)")] public Vector3 boltSide;
        float baseYaw, target, current;
        bool open;

        void Awake() { baseYaw = transform.localEulerAngles.y; }

        public bool IsOpen => open;
        public bool Passable => !locked && !bolted;

        bool OnBoltSide(Transform who) => Vector3.Dot(who.position - Anchor, boltSide) > 0f;

        public override string Prompt
        {
            get
            {
                if (locked) return "Заперто";
                return open ? "E — закрыть дверь" : "E — открыть дверь";
            }
        }

        public override string PromptFor(Transform who)
        {
            if (bolted) return OnBoltSide(who) ? "E — отодвинуть засов" : "Заперто на засов с той стороны";
            return Prompt;
        }

        public override void Interact(Transform who)
        {
            if (locked) return;
            if (bolted)
            {
                if (!OnBoltSide(who)) return;
                bolted = false;          // засов отодвинут навсегда
            }
            if (!open) OpenAwayFrom(who.position);
            else Close();
        }

        /// <summary>Открыть от точки p (от того, кто открывает).</summary>
        public void OpenAwayFrom(Vector3 p)
        {
            if (locked || bolted) return;
            open = true;
            Vector3 n = Quaternion.Euler(0, baseYaw, 0) * Vector3.forward;
            float side = Vector3.Dot(p - transform.position, n);
            target = side > 0 ? -openAngle : openAngle;
        }

        public void Close() { open = false; target = 0f; }

        void Update()
        {
            current = Mathf.MoveTowards(current, target, speed * Time.deltaTime);
            var e = transform.localEulerAngles; e.y = baseYaw + current; transform.localEulerAngles = e;
        }
    }
}
