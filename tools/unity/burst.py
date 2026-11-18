"""python -I -X utf8 burst.py <prefix> <shots> <timescale> "<C# action>" [side|front|back] [dist]
Замедлить время, выполнить действие (C#: переменная r — первый человек v3 в ситуации), снять серию кадров сбоку с подписью игрового времени."""
import json, sys, time
sys.path.insert(0, "D:/Metroidvany/tools/unity")
import umcp


def cs(code):
    try:
        msgs = umcp.rpc("tools/call", {"name": "execute_code", "arguments": {"action": "execute", "code": code}})
        for m in msgs:
            r = m.get("result") or m.get("error")
            j = json.loads(r["content"][0]["text"])
            return j["data"]["result"] if j.get("success") else "ERR " + json.dumps(j, ensure_ascii=False)[:1500]
    except Exception as e:
        return "ERR " + str(e)[:300]


prefix, n, ts, action = sys.argv[1], int(sys.argv[2]), float(sys.argv[3]), sys.argv[4]
view = sys.argv[5] if len(sys.argv) > 5 else "side"
dist = float(sys.argv[6]) if len(sys.argv) > 6 else 3.0
HEAD = ('var ar = UnityEngine.GameObject.Find("Ситуация"); var r = ar.GetComponentInChildren<Fortress.Rig.V3.HumanReaction>(); ')
print(cs(HEAD + 'UnityEngine.Time.timeScale = %ff; %s; return "ok t=" + UnityEngine.Time.time;' % (ts, action)))
fixed = view.startswith("fix")
if fixed: view = view[3:]
vec = {"side": "UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, f)", "front": "f", "back": "-f"}[view]
if fixed:
    # камера неподвижна: точка и направление — на момент удара
    print(cs(HEAD + 'var f = r.Animate.BodyForward; var c = r.Motor.transform.position + UnityEngine.Vector3.up * 1.0f; var pos = c + %s * %ff + UnityEngine.Vector3.up * 0.35f; var g = new UnityEngine.GameObject("_burstcam"); g.transform.position = pos; g.transform.rotation = UnityEngine.Quaternion.LookRotation(c - pos); return "fixed";' % (vec, dist)))
for k in range(n):
    if fixed:
        code = HEAD + ('var g = UnityEngine.GameObject.Find("_burstcam").transform; FortressSetup.Shot("D:/Metroidvany/renders/rig/%s_%02d.png", g.position, g.position + g.forward * 3f); '
                       'return "t=" + UnityEngine.Time.time.ToString("F2") + " pk=" + r.PeakAngle.ToString("F0") + " sw=" + r.SwayDeg.ToString("F1") + " kn=" + r.Motor.Knock.magnitude.ToString("F2") + " st=" + r.Animate.StepWeight.ToString("F1") + " steps=" + r.Animate.Steps;') % (prefix, k)
        print(k, cs(code), flush=True)
        continue
    code = HEAD + ('var f = r.Animate.BodyForward; var c = r.Motor.transform.position + UnityEngine.Vector3.up * 1.0f; '
                   'var pos = c + %s * %ff + UnityEngine.Vector3.up * 0.35f; '
                   'FortressSetup.Shot("D:/Metroidvany/renders/rig/%s_%02d.png", pos, c); '
                   'return "t=" + UnityEngine.Time.time.ToString("F2") + " pk=" + r.PeakAngle.ToString("F0") + " sw=" + r.SwayDeg.ToString("F1") + " kn=" + r.Motor.Knock.magnitude.ToString("F2") + " st=" + r.Animate.StepWeight.ToString("F1");') % (vec, dist, prefix, k)
    print(k, cs(code), flush=True)
print(cs('UnityEngine.Time.timeScale = 1f; var g = UnityEngine.GameObject.Find("_burstcam"); if (g != null) UnityEngine.Object.Destroy(g); return "ts=1";'))
