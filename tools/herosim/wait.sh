#!/bin/bash
D=$(dirname "$0")
for i in $(seq 1 60); do r=$("$D/mcp.sh" resources/read '{"uri":"mcpforunity://editor/state"}' | python -c "import sys,json; d=json.loads(json.load(sys.stdin)['result']['contents'][0]['text'])['data']; print(d['compilation']['is_compiling'], d['advice'].get('ready_for_tools'), d['editor']['play_mode']['is_playing'])" 2>/dev/null); case "$r" in "False True"*) echo "$r"; break;; esac; sleep 2; done
"$D/mcp.sh" tools/call '{"name":"read_console","arguments":{"types":["error"],"count":10}}' | python -c "import sys,json; print(json.loads(json.load(sys.stdin)['result']['content'][0]['text'])['data'])"
