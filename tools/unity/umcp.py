"""Минимальный клиент MCP (streamable HTTP) к MCP For Unity: python umcp.py list | call <tool> '<json args>' | res <uri>"""
import json, sys, urllib.request, time

URL = "http://127.0.0.1:8080/mcp"
SID_FILE = __file__ + ".sid"


def post(payload, sid=None):
    data = json.dumps(payload).encode()
    req = urllib.request.Request(URL, data=data, method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Accept", "application/json, text/event-stream")
    if sid:
        req.add_header("Mcp-Session-Id", sid)
    with urllib.request.urlopen(req, timeout=180) as r:
        new_sid = r.headers.get("Mcp-Session-Id") or sid
        body = r.read().decode("utf-8", "replace")
    msgs = []
    if body.lstrip().startswith("{"):
        msgs.append(json.loads(body))
    else:
        for line in body.splitlines():
            if line.startswith("data:"):
                try:
                    msgs.append(json.loads(line[5:].strip()))
                except Exception:
                    pass
    return new_sid, msgs


def session():
    try:
        return open(SID_FILE).read().strip()
    except Exception:
        pass
    sid, _ = post({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                   "params": {"protocolVersion": "2025-03-26", "capabilities": {},
                              "clientInfo": {"name": "claude-code-session", "version": "1"}}})
    post({"jsonrpc": "2.0", "method": "notifications/initialized"}, sid)
    open(SID_FILE, "w").write(sid or "")
    return sid


def rpc(method, params, retry=True):
    sid = session()
    try:
        _, msgs = post({"jsonrpc": "2.0", "id": int(time.time() * 1000) % 1000000, "method": method, "params": params}, sid)
    except urllib.error.HTTPError as e:
        if retry and e.code in (400, 404):
            open(SID_FILE, "w").write("")
            import os; os.remove(SID_FILE)
            return rpc(method, params, False)
        raise
    return msgs


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "list":
        for m in rpc("tools/list", {}):
            for t in m.get("result", {}).get("tools", []):
                print(t["name"], "-", (t.get("description") or "").split("\n")[0][:110])
    elif cmd == "schema":
        for m in rpc("tools/list", {}):
            for t in m.get("result", {}).get("tools", []):
                if t["name"] == sys.argv[2]:
                    print(json.dumps(t.get("inputSchema"), ensure_ascii=False)[:3000])
    elif cmd == "call":
        args = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
        for m in rpc("tools/call", {"name": sys.argv[2], "arguments": args}):
            r = m.get("result", m.get("error"))
            if isinstance(r, dict) and "content" in r:
                for c in r["content"]:
                    print(c.get("text", c)[:6000] if isinstance(c, dict) else c)
            else:
                print(json.dumps(r, ensure_ascii=False)[:6000])
    elif cmd == "res":
        for m in rpc("resources/read", {"uri": sys.argv[2]}):
            print(json.dumps(m.get("result", m.get("error")), ensure_ascii=False)[:4000])
