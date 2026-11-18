import sys, json, time
sys.path.insert(0, "D:/Metroidvany/tools/unity"); import umcp
P = 'var p = UnityEngine.Object.FindFirstObjectByType<Fortress.Hero.Proto.HeroProto>(); '
def cs(code):
    for m in umcp.rpc("tools/call", {"name": "execute_code", "arguments": {"action": "execute", "code": P + code}}):
        r = m.get("result") or m.get("error"); j = json.loads(r["content"][0]["text"])
        return j["data"]["result"] if j.get("success") else "ERR " + json.dumps(j, ensure_ascii=False)[:400]
ST = 'return "body=" + p.bodyCube.position.ToString("F2") + " hand=" + p.handCube.position.ToString("F2") + " elbow=" + p.elbowCube.position.ToString("F2") + " t=" + p.t.ToString("F2") + " mode=" + p.mode;'
def shot(n): return cs(f'var c = p.cam.transform; global::FortressSetup.Shot("D:/Metroidvany/renders/hero/{n}.png", c.position, c.position + c.forward); return "shot";')
print(cs('p.testHold = true; p.BeginGrip(new UnityEngine.Vector3(0.25f, 0f, 0.5f)); ' + ST)); time.sleep(0.5)
print(shot("p0_grip"))
# жест «на себя»: вниз по экрану (камера сзади) — 6 рывков по 25 px
for i in range(6):
    cs('p.Gesture(new UnityEngine.Vector2(0f, -25f)); return "";'); time.sleep(0.3)
print("на себя:", cs(ST)); print(shot("p1_pulled"))
for i in range(3):
    cs('p.Gesture(new UnityEngine.Vector2(60f, 0f)); return "";'); time.sleep(0.3)
print("вбок (отброшен):", cs(ST))
for i in range(4):
    cs('p.Gesture(new UnityEngine.Vector2(0f, 30f)); return "";'); time.sleep(0.3)
print("от себя:", cs(ST)); print(shot("p2_pushed"))
print(cs('p.testHold = false; return "ok";'))
