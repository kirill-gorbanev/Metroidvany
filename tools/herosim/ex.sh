#!/bin/bash
# usage: ex.sh <file.cs>  — execute C# body in Unity, print result text
D=$(dirname "$0")
P=$(python -c "import sys,json; print(json.dumps({'name':'execute_code','arguments':{'action':'execute','code':open(sys.argv[1],encoding='utf-8').read()}}))" "$1")
"$D/mcp.sh" tools/call "$P" | python -c "
import sys,json
r=json.load(sys.stdin)['result']
t=r['content'][0]['text']
try:
  d=json.loads(t); print(d.get('message','')); x=d.get('data'); print(x.get('result', x) if isinstance(x,dict) else x)
except Exception: print(t)"
