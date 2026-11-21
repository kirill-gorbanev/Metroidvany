#!/bin/bash
# usage: mcp.sh <method> '<params-json>'   (session cached in $SF)
U=http://127.0.0.1:8080/mcp; SF=$(dirname "$0")/mcp.sid
H=(-H "Content-Type: application/json" -H "Accept: application/json, text/event-stream")
if [ ! -s "$SF" ]; then
  sid=$(curl -s -m 10 -D - -o /dev/null -X POST $U "${H[@]}" -d '{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"cc","version":"0"}}}' | tr -d '\r' | awk -F': ' 'tolower($1)=="mcp-session-id"{print $2}')
  echo "$sid" > "$SF"
  curl -s -m 10 -X POST $U "${H[@]}" -H "mcp-session-id: $sid" -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' >/dev/null
fi
sid=$(cat "$SF")
curl -s -m ${T:-300} -X POST $U "${H[@]}" -H "mcp-session-id: $sid" -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"$1\",\"params\":$2}" | sed -n 's/^data: //p'
