"""python -I -X utf8 v3run.py <testIndex|-1> <seconds> <interval> [shotprefix] — запустить ситуацию стенда и печатать телеметрию v3."""
import json, os, sys, time
sys.path.insert(0, "D:/Metroidvany/tools/unity")
import umcp

HERE = os.path.dirname(os.path.abspath(__file__))  # рядом лежит probe_v3.cs.txt


def cs(code):
    try:
        msgs = umcp.rpc("tools/call", {"name": "execute_code", "arguments": {"action": "execute", "code": code}})
        for m in msgs:
            r = m.get("result") or m.get("error")
            j = json.loads(r["content"][0]["text"])
            return j["data"]["result"] if j.get("success") else "ERR " + json.dumps(j, ensure_ascii=False)[:1500]
    except Exception as e:
        return "ERR " + str(e)[:300]


test, secs, step = int(sys.argv[1]), float(sys.argv[2]), float(sys.argv[3])
shot = sys.argv[4] if len(sys.argv) > 4 else None
probe = open(os.path.join(HERE, "probe_v3.cs.txt"), encoding="utf-8").read()
if test >= 0:
    print(cs('var b = UnityEngine.Object.FindFirstObjectByType<Fortress.Rig.Tests.RigTestBench>(); b.GetType().GetMethod("Run", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(b, new object[]{%d}); UnityEngine.Application.runInBackground = true; return "run %d";' % (test, test)))
t0 = time.time(); k = 0
while time.time() - t0 < secs:
    time.sleep(step)
    print(str(cs(probe)).strip()[:1800], flush=True)
    if shot:
        k += 1
        cs('var c = UnityEngine.Camera.main.transform; FortressSetup.Shot("D:/Metroidvany/renders/rig/%s_%02d.png", c.position, c.position + c.forward * 5f); return "s";' % (shot, k))
