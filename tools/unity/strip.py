"""python -I -X utf8 strip.py out.png x0 x1 y0 y1 in1.png in2.png ... — склеить кропы кадров в полосу (через Unity, без PIL).
Координаты кропа — в пикселях кадра 1600×900, y от верха."""
import json, sys
sys.path.insert(0, "D:/Metroidvany/tools/unity")
import umcp

out = sys.argv[1]
x0, x1, y0, y1 = (int(v) for v in sys.argv[2:6])
ins = sys.argv[6:]
D = "D:/Metroidvany/renders/rig/"
arr = ",".join('"%s"' % (D + f) for f in ins)
code = '''
var files = new string[] { %s };
int x0 = %d, x1 = %d, y0 = %d, y1 = %d;
int w = x1 - x0, h = y1 - y0;
var dst = new UnityEngine.Texture2D(w * files.Length + 6 * (files.Length - 1), h, UnityEngine.TextureFormat.RGB24, false);
var bg = new UnityEngine.Color32[dst.width * dst.height];
for (int i = 0; i < bg.Length; i++) bg[i] = new UnityEngine.Color32(20, 20, 20, 255);
dst.SetPixels32(bg);
for (int k = 0; k < files.Length; k++) {
  var t = new UnityEngine.Texture2D(2, 2);
  UnityEngine.ImageConversion.LoadImage(t, System.IO.File.ReadAllBytes(files[k]));
  var px = t.GetPixels(x0, t.height - y1, w, h);
  dst.SetPixels(k * (w + 6), 0, w, h, px);
  UnityEngine.Object.DestroyImmediate(t);
}
dst.Apply();
System.IO.File.WriteAllBytes("%s", UnityEngine.ImageConversion.EncodeToPNG(dst));
UnityEngine.Object.DestroyImmediate(dst);
return "ok";
''' % (arr, x0, x1, y0, y1, D + out)
msgs = umcp.rpc("tools/call", {"name": "execute_code", "arguments": {"action": "execute", "code": code}})
for m in msgs:
    r = m.get("result") or m.get("error")
    print(r["content"][0]["text"][:600])
