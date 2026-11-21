// Размеры героя ничком (HeroBody.prone = 1): длина / ширина / высота тела, плечо, рука. Для шага 8 (тело лёжа на животе).
// Запуск в Play (execute_code). Модель ставится в (100, 0, 100) лицом на +Z, после замера удаляется.
var src = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Characters/Hero/Hero.prefab");
var go = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(src, new UnityEngine.Vector3(100f, 0f, 100f), UnityEngine.Quaternion.identity);
var hb = go.GetComponentInChildren<Fortress.Hero.HeroBody>(true);
if (hb == null) { UnityEngine.Object.Destroy(go); return "нет HeroBody в Hero.prefab"; }
foreach (var mb in go.GetComponentsInChildren<UnityEngine.MonoBehaviour>(true)) if (mb != hb && mb.GetType().Name != "HeroBody") mb.enabled = false;
var sb = new System.Text.StringBuilder();
foreach (float pr in new float[] { 0f, 1f })
{
    hb.prone = pr; hb.handWeight = 0f; hb.effort = 0f;
    var m = hb.GetType().GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
    for (int i = 0; i < 30; i++) { if (m != null) m.Invoke(hb, null); }
    var b = new UnityEngine.Bounds(go.transform.position, UnityEngine.Vector3.zero); bool any = false;
    foreach (var r in go.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
    {
        r.updateWhenOffscreen = true;
        var mesh = new UnityEngine.Mesh(); r.BakeMesh(mesh, true);
        foreach (var v in mesh.vertices) { var w = r.transform.TransformPoint(v); if (!any) { b = new UnityEngine.Bounds(w, UnityEngine.Vector3.zero); any = true; } else b.Encapsulate(w); }
    }
    var o = go.transform.position;
    sb.Append(pr == 0f ? "СИДЯ" : "НИЧКОМ").Append(": длина (Z) ").Append(b.size.z.ToString("F2")).Append(" м [").Append((b.min.z - o.z).ToString("F2")).Append(" … ").Append((b.max.z - o.z).ToString("F2"))
      .Append("], ширина (X) ").Append(b.size.x.ToString("F2")).Append(" м [").Append((b.min.x - o.x).ToString("F2")).Append(" … ").Append((b.max.x - o.x).ToString("F2"))
      .Append("], высота ").Append(b.size.y.ToString("F2")).Append(" м; плечо (правое) ").Append((hb.Shoulder - o).ToString("F2"))
      .Append(", голова ").Append((hb.HeadPosition - o).ToString("F2")).Append(", грудь ").Append((hb.ChestPosition - o).ToString("F2"))
      .Append(", рука ").Append(hb.ArmLength.ToString("F2")).Append(" м\n");
}
UnityEngine.Object.Destroy(go);
return sb.ToString();
