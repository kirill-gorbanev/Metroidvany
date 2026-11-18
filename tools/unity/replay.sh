#!/bin/sh
# Безопасный перезапуск Play после правки скриптов:
# выйти из Play -> Refresh -> дождаться конца компиляции и перезагрузки домена (несколько проверок подряд) -> только потом Play.
# (вход в Play при незаконченной компиляции уронил Unity в 22:39 — «forced synchronous recompile» в Mono.)
q() { python -I -X utf8 umcp.py call execute_code "{\"action\":\"execute\",\"code\":\"$1\"}" 2>/dev/null; }
q 'UnityEditor.EditorApplication.isPlaying = false; return \"stop\";' >/dev/null
python -c "import time; time.sleep(3)"
q 'UnityEditor.AssetDatabase.Refresh(); return \"refresh\";' >/dev/null
calm=0; n=0
while [ $calm -lt 3 ] && [ $n -lt 80 ]; do
  python -c "import time; time.sleep(2)"
  r=$(q 'return (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating) ? \"busy\" : \"idle\";')
  if echo "$r" | grep -q '"result":"idle"'; then calm=$((calm+1)); else calm=0; fi
  n=$((n+1))
done
echo "calm after $n checks"
python -I -X utf8 umcp.py call read_console '{"action":"get","types":["error"],"count":10,"format":"plain"}' | head -c 1500; echo
q 'UnityEditor.EditorApplication.isPlaying = true; return \"play\";' >/dev/null
calm=0; n=0
while [ $calm -lt 2 ] && [ $n -lt 60 ]; do
  python -c "import time; time.sleep(2)"
  r=$(q 'return UnityEditor.EditorApplication.isPlaying && UnityEngine.Time.time > 0.5f ? \"on\" : \"wait\";')
  if echo "$r" | grep -q '"result":"on"'; then calm=$((calm+1)); else calm=0; fi
  n=$((n+1))
done
q 'return \"playing=\" + UnityEditor.EditorApplication.isPlaying + \" t=\" + UnityEngine.Time.time;' | head -c 200; echo
