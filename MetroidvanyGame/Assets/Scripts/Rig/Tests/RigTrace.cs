using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Fortress.Rig.Human;

namespace Fortress.Rig.Tests
{
    /// <summary>Отладка стенда: запись по шагам физики (стопы тела и призрака, таз, состояние) для первого человека в ситуации.
    /// Повесить на любой объект; Dump(t0, t1) — строки за отрезок времени.</summary>
    public class RigTrace : MonoBehaviour
    {
        public int keep = 1500;
        public HumanPuppet target;
        readonly List<string> rows = new List<string>();

        void FixedUpdate()
        {
            if (!target)
            {
                var a = GameObject.Find("Ситуация");
                if (a) target = a.GetComponentInChildren<HumanPuppet>();
                if (!target) return;
            }
            var t = target;
            var fl = t.feet[0]; var fr = t.feet[1];
            var gl = fl.GetComponent<RigMuscle>().follow; var gr = fr.GetComponent<RigMuscle>().follow;
            var gp = t.driver.transform.position;
            rows.Add($"{Time.time:F2} {t.state} root={gp.x:F2},{gp.y:F2} pel={t.body.Root.position.y:F2}/{t.targetPelvis.position.y:F2} " +
                     $"L={fl.position.x:F2},{fl.position.y:F2}>{gl.position.x:F2},{gl.position.y:F2} R={fr.position.x:F2},{fr.position.y:F2}>{gr.position.x:F2},{gr.position.y:F2}");
            if (rows.Count > keep) rows.RemoveAt(0);
        }

        public string Dump(float t0, float t1, int every = 1)
        {
            var sb = new StringBuilder();
            int k = 0;
            foreach (var r in rows)
            {
                if (!float.TryParse(r.Substring(0, r.IndexOf(' ')), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out float tt)) continue;
                if (tt < t0 || tt > t1) continue;
                if (k++ % every == 0) sb.AppendLine(r);
            }
            return sb.ToString();
        }
    }
}
