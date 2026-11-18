#!/bin/sh
# дождаться, пока Unity не компилирует и не импортирует (3 спокойные проверки подряд)
q() { python -I -X utf8 umcp.py call execute_code "{\"action\":\"execute\",\"code\":\"$1\"}" 2>/dev/null; }
calm=0; n=0
while [ $calm -lt 3 ] && [ $n -lt ${1:-150} ]; do
  python -c "import time; time.sleep(2)"
  r=$(q 'return (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating) ? \"busy\" : \"idle\";')
  if echo "$r" | grep -q '"result":"idle"'; then calm=$((calm+1)); else calm=0; fi
  n=$((n+1))
done
echo "idle after $n checks"
