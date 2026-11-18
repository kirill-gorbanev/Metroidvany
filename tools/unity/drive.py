"""Ведёт игрока v3 (ситуация «Ты — человек»): подаёт ввод в мотор по расписанию и печатает скорость, углы капсулы и корпуса, наклон."""
import json, sys, time
sys.path.insert(0, "D:/Metroidvany/tools/unity")
import umcp


def cs(code):
    msgs = umcp.rpc("tools/call", {"name": "execute_code", "arguments": {"action": "execute", "code": code}})
    for m in msgs:
        r = m.get("result") or m.get("error")
        j = json.loads(r["content"][0]["text"])
        return j["data"]["result"] if j.get("success") else "ERR " + json.dumps(j, ensure_ascii=False)[:800]


GET = ('var b = UnityEngine.Object.FindFirstObjectByType<Fortress.Rig.Tests.RigTestBench>(); '
       'var fl = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; '
       'var m = (Fortress.Rig.V3.HumanMotor)b.GetType().GetField("possessedV3", fl).GetValue(b); '
       'if (m == null) m = (Fortress.Rig.V3.HumanMotor)b.GetType().GetField("drv", fl)?.GetValue(b); ')
# забрать управление у клавиатуры стенда: possessedV3 = null, но мотор остаётся «игроком»
print(cs(GET + 'b.GetType().GetField("possessedV3", fl).SetValue(b, null); m.possessed = true; '
         'UnityEngine.GameObject.Find("Ситуация").name = "Ситуация"; '
         'var g = new UnityEngine.GameObject("_drv"); g.transform.SetParent(m.transform, false); return "ok " + m.name;'))
FIND = 'var m = UnityEngine.GameObject.Find("_drv").transform.parent.GetComponent<Fortress.Rig.V3.HumanMotor>(); var a = m.GetComponentInChildren<Fortress.Rig.V3.HumanAnimate>(); '
REPORT = ('return "t=" + UnityEngine.Time.time.ToString("F2") + " v=" + m.Velocity.magnitude.ToString("F2") + " cap=" + m.transform.eulerAngles.y.ToString("F0") + '
          '" body=" + UnityEngine.Quaternion.LookRotation(a.BodyForward).eulerAngles.y.ToString("F0") + " lean=" + a.LeanPitch.ToString("F1") + "," + a.LeanRoll.ToString("F1") + " anim=" + a.AnimSpeed.ToString("F2");')
plan = [((0, 0, 0), 0.6), ((0, 0, 1.25), 1.2), ((1.25, 0, 0), 1.0), ((0, 0, 3.4), 1.5), ((0, 0, -3.4), 1.2), ((0, 0, 0), 1.0)]
for (x, y, z), dur in plan:
    print(f"-- ввод ({x},{z})")
    cs(FIND + f'm.input = new UnityEngine.Vector3({x}f, 0f, {z}f); return "";')
    t0 = time.time()
    while time.time() - t0 < dur:
        print("  ", cs(FIND + REPORT), flush=True)
cs(FIND + 'm.input = UnityEngine.Vector3.zero; return "";')
