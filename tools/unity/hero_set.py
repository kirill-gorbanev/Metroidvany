"""Ведёт героя на стенде HeroTest: hero_set.py x z [hold 0/1] [toggle] [shot-name] — ставит точку «курсора» на полу (y по лучу вниз)."""
import sys, json
sys.path.insert(0, "D:/Metroidvany/tools/unity")
import umcp
x, z = float(sys.argv[1]), float(sys.argv[2])
hold = len(sys.argv) > 3 and sys.argv[3] == "1"
toggle = len(sys.argv) > 4 and sys.argv[4] == "t"
shot = sys.argv[5] if len(sys.argv) > 5 else ""
code = ('var h = UnityEngine.GameObject.Find("Hero"); var c = h.GetComponent<Fortress.Hero.HeroController>(); var b = h.GetComponentInChildren<Fortress.Hero.HeroBody>(); '
        'c.scripted = true; var o = new UnityEngine.Vector3(%ff, 1.5f, %ff); UnityEngine.RaycastHit hit; '
        'c.scriptPoint = UnityEngine.Physics.Raycast(o, UnityEngine.Vector3.down, out hit, 3f) ? hit.point : new UnityEngine.Vector3(o.x, 0f, o.z); '
        'c.scriptHold = %s; if (%s) c.scriptToggleProne = true; ' % (x, z, "true" if hold else "false", "true" if toggle else "false"))
if shot:
    code += ('var p = h.transform.position; var f = h.transform.forward; var r = h.transform.right; '
             'global::FortressSetup.Shot("D:/Metroidvany/renders/hero/%s_34.png", p + f * 1.4f + r * 1.0f + UnityEngine.Vector3.up * 0.9f, p + UnityEngine.Vector3.up * 0.4f); '
             'global::FortressSetup.Shot("D:/Metroidvany/renders/hero/%s_side.png", p + r * 1.9f + f * 0.3f + UnityEngine.Vector3.up * 0.5f, p + f * 0.3f + UnityEngine.Vector3.up * 0.35f); ' % (shot, shot))
code += ('return "root=" + h.transform.position.ToString("F2") + " yaw=" + h.transform.eulerAngles.y.ToString("F0") + " hand=" + b.HandPosition.ToString("F2") + " tgt=" + b.handTarget.ToString("F2") + " grab=" + c.Grabbing + " prone=" + b.prone.ToString("F2") + " effort=" + b.effort.ToString("F2");')
msgs = umcp.rpc("tools/call", {"name": "execute_code", "arguments": {"action": "execute", "code": code}})
for m in msgs:
    r = m.get("result") or m.get("error"); j = json.loads(r["content"][0]["text"])
    print(j["data"]["result"] if j.get("success") else json.dumps(j, ensure_ascii=False)[:600])
