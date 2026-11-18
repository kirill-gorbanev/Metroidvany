"""Сценарии ТЗ героя на стенде HeroTest (ввод скриптом). hero_do.py <сценарий>"""
import sys, json, time
sys.path.insert(0, "D:/Metroidvany/tools/unity")
import umcp
H = ('var h = UnityEngine.GameObject.Find("Hero"); var c = h.GetComponent<Fortress.Hero.HeroController>(); '
     'var b = h.GetComponentInChildren<Fortress.Hero.HeroBody>(); c.scripted = true; c.logEvents = true; ')
def cs(code):
    for m in umcp.rpc("tools/call", {"name": "execute_code", "arguments": {"action": "execute", "code": H + code}}):
        r = m.get("result") or m.get("error"); j = json.loads(r["content"][0]["text"])
        return j["data"]["result"] if j.get("success") else "ERR " + json.dumps(j, ensure_ascii=False)[:500]
V = lambda x, y, z: f"new UnityEngine.Vector3({x}f,{y}f,{z}f)"
ST = ('return "root=" + h.transform.position.ToString("F2") + " yaw=" + h.transform.eulerAngles.y.ToString("F0") + " hand=" + b.HandPosition.ToString("F2") + '
      '" mode=" + c.State + " carried=" + (c.Carried ? c.Carried.name : "-") + " head=" + (c.OnHead ? c.OnHead.name : "-") + " hook=" + (c.Hooked ? c.Hooked.name : "-") + " effort=" + b.effort.ToString("F2") + " elbow=" + b.ElbowBend.ToString("F2");')
def pt(x, y, z, hold, wait=0.15):
    r = cs(f"c.scriptPoint = {V(x,y,z)}; c.scriptHold = {'true' if hold else 'false'}; " + ST); time.sleep(wait); return r
def pos(name): return cs(f'var g = UnityEngine.GameObject.Find("{name}"); return g.transform.position.ToString("F2");')
def tp(x, z, yaw): return cs(f'var cc = h.GetComponent<UnityEngine.CharacterController>(); cc.enabled = false; h.transform.SetPositionAndRotation({V(x,0.02,z)}, UnityEngine.Quaternion.Euler(0,{yaw}f,0)); cc.enabled = true; return "tp";')
def shot(name, off=(1.0, 0.9, 1.4)):
    return cs(f'var p = h.transform.position; var f = h.transform.forward; var r = h.transform.right; '
              f'global::FortressSetup.Shot("D:/Metroidvany/renders/hero/{name}.png", p + f * {off[2]}f + r * {off[0]}f + UnityEngine.Vector3.up * {off[1]}f, p + UnityEngine.Vector3.up * 0.4f + f * 0.3f); return "shot";')
sc = sys.argv[1]
if sc == "carry":
    tp(0, 0, 0); time.sleep(0.5)
    p = pos("Ведро 2 кг"); print("bucket", p)
    x, y, z = [float(s.replace(",", ".")) for s in p.strip("()").split(", ")]
    for i in range(6): print(pt(x, y + 0.3, z, False, 0.2))
    print(pt(x, y + 0.3, z, True, 0.3))
    print(pt(x, y + 0.3, z, True, 0.3))
    print("-- к голове")
    for i in range(8): print(cs('c.scriptPoint = b.HeadTop + UnityEngine.Vector3.up * 0.05f; c.scriptHold = true; ' + ST)); time.sleep(0.2)
    print(pt(0, 0, 0.5, False, 0.4).split(" mode")[1] if False else cs('c.scriptHold = false; ' + ST)); time.sleep(0.5)
    print(cs(ST)); print(shot("t_head"))
elif sc == "throw":
    tp(0, 0, 0); time.sleep(0.3)
    print(cs('if (c.OnHead) { c.scriptPoint = c.OnHead.position; c.scriptHold = true; } ' + ST)); time.sleep(0.4)
    print(cs('c.scriptHold = true; ' + ST))
    for i in range(3): print(pt(0.1, 0.4, 0.3, True, 0.15))
    print(pt(0.4, 0.5, 1.5, True, 0.05)); print(pt(0.6, 0.6, 2.5, False, 0.6))
    print("bucket now", pos("Ведро 2 кг"))
elif sc == "push":
    tp(0.9, 0.35, 0); time.sleep(0.5)
    p = pos("Ящик 15 кг"); print("crate", p)
    x, y, z = [float(s.replace(",", ".")) for s in p.strip("()").split(", ")]
    for i in range(5): print(pt(x - 0.1, 0.3, z - 0.32, False, 0.2))
    print(pt(x - 0.1, 0.3, z - 0.32, True, 0.3))
    print(pt(x - 0.1, 0.3, z - 0.02, True, 0.03)); print(pt(x - 0.1, 0.3, z + 0.3, True, 0.03))
    print(pt(x, 0, z, False, 1.5)); print("crate now", pos("Ящик 15 кг"))
elif sc in ("climb40", "climb60"):
    x = 2.4 if sc == "climb40" else -2.4; zf = -0.9 if sc == "climb40" else -1.1
    tp(x, zf + 0.62, 180); time.sleep(0.6)
    top = 0.4 if sc == "climb40" else 0.6
    for i in range(5): print(pt(x + 0.1, top, zf - 0.12, False, 0.2))
    print(pt(x + 0.1, top, zf - 0.12, True, 0.3))
    for i in range(14): print(pt(x + 0.1, top, zf - 0.12 + 0.05 * (i + 1), True, 0.12))
    print(pt(x + 0.1, top, zf, False, 0.3)); print(shot("t_" + sc, (1.4, 0.7, 0.4)))
elif sc == "turn":
    tp(0, 0, 0); time.sleep(0.3)
    print(pt(2.0, 0, 0.3, False, 0.2)); print(pt(2.0, 0, 0.3, True, 0.08)); print(pt(2.0, 0, 0.3, False, 1.2)); print(cs(ST))
elif sc == "crawl":   # держим ЛКМ и долго тянем на себя — перехват руки
    tp(0, 0, 0); time.sleep(0.4)
    for i in range(4): pt(0.15, 0, 0.6, False, 0.2)
    print(pt(0.15, 0, 0.6, True, 0.3))
    z = 0.6
    for i in range(40):
        z -= 0.06; r = pt(0.15, 0, z, True, 0.12)
        if i % 8 == 7: print(r)
    print(pt(0.15, 0, z, False, 0.3))
elif sc == "back":    # рука под себя/сбоку и отталкиваемся — движение спиной
    tp(0, 0, 0); time.sleep(0.4)
    for i in range(5): pt(0.25, 0, 0.15, False, 0.2)
    print(pt(0.25, 0, 0.15, True, 0.3))
    z = 0.15
    for i in range(12):
        z += 0.05; print(pt(0.25, 0, z, True, 0.12))
    print(pt(0.25, 0, z, False, 0.3))
elif sc == "drag":    # схватить ящик и тянуть к себе
    tp(0.9, 0.35, 0); time.sleep(0.5)
    p = pos("Ящик 15 кг"); x, y, z = [float(s.replace(",", ".")) for s in p.strip("()").split(", ")]
    print("crate", p)
    for i in range(5): pt(x - 0.1, 0.35, z - 0.45, False, 0.2)
    print(pt(x - 0.1, 0.35, z - 0.45, True, 0.3))
    zz = z - 0.45
    for i in range(10): zz -= 0.04; pt(x - 0.1, 0.35, zz, True, 0.12)
    print(pt(x - 0.1, 0.35, zz, False, 0.4)); print("crate now", pos("Ящик 15 кг"))
elif sc == "wall":    # рука на брусе сбоку, жест вбок — едет вдоль стены
    tp(3.85, -0.6, 0); time.sleep(0.5)
    for i in range(5): pt(4.2, 0.45, -0.4, False, 0.2)
    print(pt(4.2, 0.45, -0.4, True, 0.3))
    z = -0.4
    for i in range(12):
        z -= 0.05; r = pt(4.2, 0.45, z, True, 0.12)
    print(r); print(pt(4.2, 0.45, z, False, 0.3))
elif sc == "under":   # у стола: курсор на столешницу → петля → рука под стол
    tp(-1.45, 2.0, 270); time.sleep(0.5)
    for i in range(5): r = pt(-2.25, 0.62, 2.0, False, 0.2)
    print("над:", r)
    cs("c.scriptLoop = 1; return \"\";"); time.sleep(0.3)
    for i in range(6): r = pt(-2.25, 0.62, 2.0, False, 0.2)
    print("под:", r)
    print(shot("t_under", (0.0, 0.5, 1.2)))
