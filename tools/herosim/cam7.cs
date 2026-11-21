// Умная камера (зона 7): режим лазания на крутом, на ровном — нет; склон за спиной закрывает героя — камера подъезжает.
Fortress.Hero.Proto.ProtoZones.Go(7);
if (!Fortress.EditorTools.HeroSim.Setup()) return "нет Play / героя";
var H = Fortress.EditorTools.HeroSim.H; var C = Fortress.EditorTools.HeroSim.Cam; var sb = new System.Text.StringBuilder("УМНАЯ КАМЕРА (зона 7)\n");
System.Func<Fortress.Hero.Proto.HeroProto.InputFrame> free = () => new Fortress.Hero.Proto.HeroProto.InputFrame { pos = new UnityEngine.Vector2(-9999f, -9999f) };
try {
  Fortress.EditorTools.HeroSim.Teleport(new UnityEngine.Vector3(1.3f, 0.6f, 2.0f), 0f); H.driver = free;
  for (int i = 0; i < 100; i++) Fortress.EditorTools.HeroSim.Step();
  sb.Append("ровный пол: лазание " + C.ClimbK.ToString("F2") + " (цель 0), наклон " + C.transform.eulerAngles.x.ToString("F0") + "°, дальность " + C.DistanceNow.ToString("F2") + " м\n");
  Fortress.Hero.Proto.ProtoHold u = null; foreach (var x in H.Holds) if (x.name == "Склон 1: камень 2п") u = x;
  var c = u.At(0.5f);
  Fortress.EditorTools.HeroSim.Teleport(new UnityEngine.Vector3(c.x - 0.1f, c.y - 0.3f, c.z - 0.3f), 0f); H.driver = free;
  for (int i = 0; i < 3; i++) Fortress.EditorTools.HeroSim.Step();
  H.BeginGripHold(u, 0.5f); H.driver = () => new Fortress.Hero.Proto.HeroProto.InputFrame { pressed = true };
  for (int i = 0; i < 100; i++) Fortress.EditorTools.HeroSim.Step();
  sb.Append("   состояние " + H.DiagState + ", крутое " + H.OnSteep + ", кисть y " + H.handCube.position.y.ToString("F2") + " плечо y " + H.shoulderCube.position.y.ToString("F2") + ", тело " + H.BodyXZ.ToString("F2") + " " + H.BodyY.ToString("F2") + "\n"); sb.Append("держусь за камень на 60°: лазание " + C.ClimbK.ToString("F2") + " (цель 1), наклон " + C.transform.eulerAngles.x.ToString("F0") + "°, дальность " + C.DistanceNow.ToString("F2") + " м\n");
  H.driver = free; for (int i = 0; i < 5; i++) Fortress.EditorTools.HeroSim.Step();
  H.driver = free; for (int i = 0; i < 5; i++) Fortress.EditorTools.HeroSim.Step();
} finally { Fortress.EditorTools.HeroSim.End(); }
Fortress.Hero.Proto.ProtoZones.Go(6);
if (!Fortress.EditorTools.HeroSim.Setup()) return sb.ToString() + "нет героя";
H = Fortress.EditorTools.HeroSim.H; C = Fortress.EditorTools.HeroSim.Cam;
try {
  Fortress.EditorTools.HeroSim.Teleport(new UnityEngine.Vector3(18.4f, 0f, 2.0f), 90f); H.driver = free;
  C.ResetView(); for (int i = 0; i < 150; i++) Fortress.EditorTools.HeroSim.Step();
  sb.Append("в тоннеле зоны 6 (крыша над героем): дальность " + C.DistanceNow.ToString("F2") + " м из " + C.distance.ToString("F2") + " (цель меньше), тело y " + H.BodyY.ToString("F2") + "\n");
} finally { Fortress.EditorTools.HeroSim.End(); }
return sb.ToString();
