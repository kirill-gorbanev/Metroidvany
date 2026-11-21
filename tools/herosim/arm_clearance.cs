Fortress.Hero.Proto.ProtoZones.Go(7);
if (!Fortress.EditorTools.HeroSim.Setup()) return "no";
var H = Fortress.EditorTools.HeroSim.H; var sb = new System.Text.StringBuilder();
try {
 foreach (float cl in new float[] { -100f, 0.03f }) {
  H.armClearance = cl;
  foreach (string lane in new string[] { "Склон 1", "Склон 3" }) {
   Fortress.Hero.Proto.ProtoHold f = null, u = null;
   foreach (var x in H.Holds) { if (x.name == lane + ": камень 1п") f = x; if (x.name == lane + ": камень 3п") u = x; }
   var c = f.At(0.5f);
   H.DiagTeleport(new UnityEngine.Vector3(c.x - 0.1f, c.y - 0.3f, c.z - 0.35f), 0f);
   H.driver = () => new Fortress.Hero.Proto.HeroProto.InputFrame { pos = new UnityEngine.Vector2(-9999f, -9999f) };
   for (int i = 0; i < 3; i++) Fortress.EditorTools.HeroSim.Step();
   H.BeginGripHold(u, 0.5f);
   H.driver = () => new Fortress.Hero.Proto.HeroProto.InputFrame { pressed = true };
   for (int i = 0; i < 40; i++) Fortress.EditorTools.HeroSim.Step(); float min = 9f; for (int i = 0; i < 10; i++) { Fortress.EditorTools.HeroSim.Step(); min = UnityEngine.Mathf.Min(min, H.DiagArmClearance()); } sb.Append("   плечо " + H.shoulderCube.position.ToString("F2") + " локоть " + H.elbowCube.position.ToString("F2") + " кисть " + H.handCube.position.ToString("F2") + "\n");
   sb.Append((cl < 0 ? "без правки" : "с правкой") + ", " + lane + ", кисть на 3п выше тела: наименьший зазор руки над склоном " + (min * 100f).ToString("F1") + " см\n");
  }
 }
 H.armClearance = 0.03f;
} finally { Fortress.EditorTools.HeroSim.End(); }
return sb.ToString();
