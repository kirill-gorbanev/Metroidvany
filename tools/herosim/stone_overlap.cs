// Подъём по камням — тело НЕ налезает на камни (автор 2026-11-21: «чтобы я на камни не налезал»).
// Зона 8 (этап 1, 45°) и зона 7 (дорожки 1 и 3, 45°): берём камни правой колонки снизу вверх, «на себя» по 10 px за шаг,
// между хватами — пауза (рука отпущена). Каждый шаг: пересекается ли кубик тела с каким-нибудь камнем (по объёму) и висит ли тело.
var sb = new System.Text.StringBuilder("ТЕЛО И КАМНИ (налезает ли тело на камни, вис)\n");
foreach (var spec in new string[] { "8|Склон 1: камень |ю|90", "7|Склон 1: камень |п|0", "7|Склон 3: камень |п|0" })
{
    var parts = spec.Split('|');
    int zone = int.Parse(parts[0]); string prefix = parts[1], col = parts[2]; float yaw = float.Parse(parts[3]);
    Fortress.Hero.Proto.ProtoZones.Go(zone);
    if (!Fortress.EditorTools.HeroSim.Setup()) return "нет Play / героя";
    var H = Fortress.EditorTools.HeroSim.H;
    try
    {
        var stones = new System.Collections.Generic.List<Fortress.Hero.Proto.ProtoHold>();
        foreach (var x in H.Holds) if (x is Fortress.Hero.Proto.ProtoNub && x.name.StartsWith(prefix)) stones.Add(x);
        var bodyR = H.bodyCube.GetComponent<UnityEngine.Renderer>();
        System.Func<string> touch = () =>
        {
            var bb = bodyR.bounds; bb.Expand(-0.01f);
            foreach (var st in stones) { var sbnd = st.GetComponent<UnityEngine.Renderer>().bounds; sbnd.Expand(-0.01f); if (bb.Intersects(sbnd)) return st.name.Substring(prefix.Length); }
            return null;
        };
        Fortress.Hero.Proto.ProtoHold first = null; foreach (var x in stones) if (x.name == prefix + "1" + col) first = x;
        var c = first.At(0.5f);
        var back = UnityEngine.Quaternion.Euler(0f, yaw, 0f) * UnityEngine.Vector3.back;
        var right = UnityEngine.Quaternion.Euler(0f, yaw, 0f) * UnityEngine.Vector3.right;
        Fortress.EditorTools.HeroSim.Teleport(c + back * 0.35f - right * 0.35f + UnityEngine.Vector3.down * 0.3f, yaw);   // середина склона, правая колонка — у руки
        H.driver = () => new Fortress.Hero.Proto.HeroProto.InputFrame { pos = new UnityEngine.Vector2(-9999f, -9999f) };
        for (int i = 0; i < 5; i++) Fortress.EditorTools.HeroSim.Step();
        int steps = 0, overlapSteps = 0, hangSteps = 0; string overlapWho = ""; float y0 = H.BodyY; int got = 0;
        for (int k = 1; k <= 6; k++)
        {
            Fortress.Hero.Proto.ProtoHold u = null; foreach (var x in stones) if (x.name == prefix + k + col) u = x;
            if (u == null) break;
            float bs = -1f, bd = 9f;
            for (int i = 0; i <= 20; i++) { var P = u.At(i / 20f); float d = (P - H.ShoulderPos3).magnitude; if (H.DiagHoldOk(P, u) && d < bd) { bd = d; bs = i / 20f; } }
            if (bs < 0f) { sb.Append("   камень " + k + col + " не достать (тело " + H.BodyY.ToString("F2") + ")\n"); break; }
            H.BeginGripHold(u, bs); got++;
            H.driver = () => new Fortress.Hero.Proto.HeroProto.InputFrame { pressed = true, delta = new UnityEngine.Vector2(0f, -10f) };
            float yl = -1f; int still = 0;
            for (int i = 0; i < 100; i++)
            {
                Fortress.EditorTools.HeroSim.Step(); steps++;
                var w = touch(); if (w != null) { overlapSteps++; if (!overlapWho.Contains(w)) overlapWho += w + " "; }
                if (H.Hanging) hangSteps++;
                if (UnityEngine.Mathf.Abs(H.BodyY - yl) < 0.001f && H.LineT > 0.97f) { if (++still >= 3) break; } else still = 0;
                yl = H.BodyY;
            }
            H.driver = () => new Fortress.Hero.Proto.HeroProto.InputFrame { pos = new UnityEngine.Vector2(-9999f, -9999f) };
            for (int i = 0; i < 10; i++) { Fortress.EditorTools.HeroSim.Step(); steps++; var w = touch(); if (w != null) { overlapSteps++; if (!overlapWho.Contains(w)) overlapWho += w + " "; } }
        }
        sb.Append("зона " + zone + ", " + prefix.TrimEnd(' ', ':') + ": камней взято " + got + ", тело " + y0.ToString("F2") + " → " + H.BodyY.ToString("F2")
            + "; шагов, где тело в камне, " + overlapSteps + " из " + steps + (overlapWho.Length > 0 ? " (камни " + overlapWho.Trim() + ")" : "")
            + "; шагов виса (фиолетовый) " + hangSteps + "\n");
    }
    finally { Fortress.EditorTools.HeroSim.End(); }
}
return sb.ToString();
