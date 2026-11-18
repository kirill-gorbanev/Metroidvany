"""python envrun.py <testIndex|-1> <seconds> <interval> [shotprefix]"""
import json, subprocess, sys, time
def cs(code):
    r = subprocess.run(["python", "-I", "-X", "utf8", "umcp.py", "call", "execute_code", json.dumps({"action": "execute", "code": code})], capture_output=True, text=True, encoding="utf-8")
    try:
        j = json.loads(r.stdout); return j["data"]["result"] if j.get("success") else ("ERR " + r.stdout[:700])
    except Exception:
        return "ERR " + r.stdout[:500] + r.stderr[:300]
test, secs, step = int(sys.argv[1]), float(sys.argv[2]), float(sys.argv[3])
shot = sys.argv[4] if len(sys.argv) > 4 else None
probe = open("probe_env.cs.txt", encoding="utf-8").read()
if test >= 0:
    print(cs('var b = UnityEngine.Object.FindFirstObjectByType<Fortress.Rig.Tests.RigTestBench>(); b.GetType().GetMethod("Run", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(b, new object[]{%d}); UnityEngine.Application.runInBackground = true; return "run %d";' % (test, test)))
t0 = time.time(); k = 0
while time.time() - t0 < secs:
    time.sleep(step)
    print(cs(probe).strip()[:900])
    if shot:
        k += 1
        cs('var c = UnityEngine.Camera.main.transform; FortressSetup.Shot("D:/Metroidvany/renders/rig/%s_%02d.png", c.position, c.position + c.forward * 5f); return "s";' % (shot, k))
