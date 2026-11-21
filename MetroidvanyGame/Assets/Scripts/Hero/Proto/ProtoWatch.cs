using System.Collections.Generic;
using UnityEngine;

namespace Fortress.Hero.Proto
{
    /// <summary>СТОРОЖА — отдельный блок: каждый шаг проверяют инварианты героя-прототипа и пишут нарушения.
    /// Логику не меняют, только читают. Используются ботом и сценариями (HeroSim), можно и в живой игре.
    ///   тело в стене · тело внутри уступа · кисть в стене · рука длиннее L · рука сквозь тело ·
    ///   скачок тела (XZ) · скачок высоты · кисть прыгнула по высоте · под полом · NaN.</summary>
    public class ProtoWatch
    {
        public readonly HeroProto h;
        public readonly List<string> log = new List<string>();
        public readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        public float time;
        /// <summary>Допуски (м за шаг 0.02 с).</summary>
        public float jumpXZ = 0.15f, jumpY = 0.15f, handJumpY = 0.35f;

        Vector3 pb; float pg, phy; bool first = true, wasFalling;

        public ProtoWatch(HeroProto hero) { h = hero; }

        void Hit(string kind, string detail)
        {
            counts[kind] = counts.TryGetValue(kind, out int c) ? c + 1 : 1;
            if (counts[kind] <= 4)
                log.Add($"{time:F2}с  {kind}: {detail}  [{(h.Gripping ? "хват" : "свободна")}] тело ({h.BodyXZ.x:F2}, {h.BodyY:F2}, {h.BodyXZ.z:F2})");
        }

        public int Total { get { int n = 0; foreach (var v in counts.Values) n += v; return n; } }

        public void Check(float dt)
        {
            time += dt;
            Vector3 b = h.BodyXZ; float g = h.BodyY;
            if (float.IsNaN(b.x) || float.IsNaN(b.z) || float.IsNaN(g)) { Hit("NaN", ""); return; }
            if (h.DiagBodyInWall(0.03f)) Hit("тело в стене", "");
            if (h.DiagBodyInSolid) Hit("тело внутри уступа", "");   // глубже порога подъёма (догонять высоту ступени — нормально)
            if (h.DiagHandInWall) Hit("кисть в стене", $"кисть ({h.HandXZ.x:F2},{h.HandXZ.z:F2})");
            float arm = (h.HandXZ - h.ShoulderXZ).magnitude;
            if (h.Gripping && arm > h.GripReach + 0.03f) Hit("рука длиннее L", $"{arm:F2} > {h.GripReach:F2}");
            if (h.armKeepOut > 0f && h.DiagArmToBody < h.armKeepOut * 0.6f) Hit("рука сквозь тело", $"{h.DiagArmToBody:F2}");
            if (g < -0.01f) Hit("под полом", $"y {g:F2}");
            if (h.DiagCrushed) Hit("раздавлен", "подвижное прижало к неподвижному");
            bool air = h.Falling || wasFalling || h.DiagRespawned;           // полёт, приземление, возврат на старт — не «скачок»
            wasFalling = h.Falling;
            if (!first && !air)
            {
                // ожидаемое место: прошлое, сдвинутое платформой, если тело на ней
                Vector3 prev3 = new Vector3(pb.x, pg, pb.z);
                if (h.rideMover) prev3 = h.rideMover.Delta.MultiplyPoint3x4(prev3);
                float dxz = new Vector2(b.x - prev3.x, b.z - prev3.z).magnitude;
                if (dxz > jumpXZ) Hit("скачок тела", $"{dxz:F2} м за шаг");
                if (Mathf.Abs(g - prev3.y) > jumpY) Hit("скачок высоты", $"{g - prev3.y:+0.00;-0.00} м за шаг");
                if (Mathf.Abs(h.HandY - phy) > handJumpY && !h.handMover) Hit("кисть прыгнула по высоте", $"{h.HandY - phy:+0.00;-0.00}");
            }
            first = false; pb = b; pg = g; phy = h.HandY;
        }

        public string Report()
        {
            if (counts.Count == 0) return "сторожа: нарушений нет";
            var s = new System.Text.StringBuilder("сторожа:");
            foreach (var kv in counts) s.Append($"  {kv.Key} ×{kv.Value};");
            foreach (var l in log) s.Append("\n    ").Append(l);
            return s.ToString();
        }
    }
}
