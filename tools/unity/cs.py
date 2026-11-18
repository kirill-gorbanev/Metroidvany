"""python -I -X utf8 cs.py file.cs  — выполнить C#-сниппет в Unity (execute_code) и напечатать результат."""
import json, sys
sys.path.insert(0, "D:/Metroidvany/tools/unity")
import umcp

code = open(sys.argv[1], encoding="utf-8").read() if sys.argv[1] != "-" else sys.stdin.read()
msgs = umcp.rpc("tools/call", {"name": "execute_code", "arguments": {"action": "execute", "code": code}})
for m in msgs:
    r = m.get("result") or m.get("error")
    try:
        txt = r["content"][0]["text"]
        j = json.loads(txt)
        if j.get("success"):
            print(j["data"].get("result"))
        else:
            print("ERR", json.dumps(j, ensure_ascii=False)[:3000])
    except Exception:
        print(json.dumps(m, ensure_ascii=False)[:3000])
