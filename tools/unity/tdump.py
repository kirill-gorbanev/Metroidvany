import json, subprocess, sys
t0, t1, ev = sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "1"
out = "D:/Metroidvany/tools/unity/tr_all.txt"
code = 'var tr = UnityEngine.Object.FindFirstObjectByType<Fortress.Rig.Tests.RigTrace>(); System.IO.File.WriteAllText("%s", tr.Dump(%sf, %sf, %s)); return "T=" + UnityEngine.Time.time.ToString("F1");' % (out, t0, t1, ev)
r = subprocess.run(["python", "-I", "-X", "utf8", "umcp.py", "call", "execute_code", json.dumps({"action": "execute", "code": code})], capture_output=True, text=True, encoding="utf-8")
print(r.stdout[:300])
