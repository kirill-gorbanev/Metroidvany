var sb = new System.Text.StringBuilder();
foreach (int seed in new[] { 1, 2, 3 }) { Fortress.EditorTools.HeroSim.ResetWorld(); sb.Append(Fortress.EditorTools.HeroSim.Fuzz(60f, seed)).Append("\n"); }
Fortress.EditorTools.HeroSim.ResetWorld(); sb.Append(Fortress.EditorTools.HeroSim.Course6(true)).Append("\n");
return sb.ToString();
