#!/bin/bash
# ОБЩИЙ ПРОГОН ГЕРОЯ: одна арена HeroArena.unity, зоны 2–8 (прежние арены) (бродяга на 3 зёрнах + замеры каждой арены) и живая проверка на аренах 7 и 8.
# Результат — out/report.txt; сравнение с эталоном baseline.txt — каждое изменённое число (diff.py).
#   ./run_all.sh            прогон + сравнение
#   ./run_all.sh accept     прогон + сравнение + новый эталон (после «ок» автора)
# Нужен открытый Unity (MetroidvanyGame) с MCP for Unity на 127.0.0.1:8080. Play включается и выключается сам.
D=$(cd "$(dirname "$0")" && pwd); cd "$D"; mkdir -p out
R=out/report.txt; : > $R
call() { ./mcp.sh tools/call "$1" >/dev/null; }
call '{"name":"set_active_instance","arguments":{"instance":"MetroidvanyGame@ed623bcb3aa93e1e"}}'
call '{"name":"manage_editor","arguments":{"action":"stop"}}'; sleep 2
E=$(./wait.sh | tail -1)
if [ "$E" != "[]" ]; then echo "ОШИБКИ КОМПИЛЯЦИИ: $E"; exit 1; fi
for n in ${ARENAS:-2 3 4 5 6 7 8}; do
  call "{\"name\":\"manage_editor\",\"arguments\":{\"action\":\"stop\"}}"; sleep 2
  call '{"name":"manage_scene","arguments":{"action":"load","path":"Assets/Scenes/HeroArena.unity"}}'   # одна арена, зоны 2–8
  printf 'Fortress.Hero.Proto.ProtoZones.Go(%s);
return Fortress.EditorTools.HeroSim.ResetWorld();
' $n > out/probe.cs
  # Play включается не всегда с первого раза — проверка: герой найден и мир сброшен («ok»)
  ok=""
  for try in 1 2 3 4 5; do
    call '{"name":"manage_editor","arguments":{"action":"play"}}'; sleep 5; ./wait.sh >/dev/null
    T=60 ./ex.sh out/probe.cs >/dev/null 2>&1        # первый вызов после входа в Play возвращает null
    if T=60 ./ex.sh out/probe.cs | grep -q '^ok$'; then ok=1; break; fi
    sleep 3
  done
  [ -z "$ok" ] && echo "АРЕНА $n: Play не включился" | tee -a $R
  echo "===== АРЕНА $n" >> $R
  for f in a$n.cs a${n}b.cs; do                 # долгие замеры зоны — вторым вызовом (один вызов не дождаться)
    [ -f $f ] || continue
    out=$(T=900 ./ex.sh $f | grep -v '^Code executed successfully' | grep -v '^None$')
    if [ -z "$out" ]; then out=$(T=900 ./ex.sh $f | grep -v '^Code executed successfully' | grep -v '^None$'); fi   # пустой ответ — ещё раз
    if [ -z "$out" ]; then out="ЗАМЕРЫ $f НЕ ВЕРНУЛИСЬ"; fi
    echo "$out" >> $R
  done
  if [ $n = 7 ] || [ $n = 8 ]; then
    T=60 ./ex.sh live_start.cs >/dev/null; sleep 20
    T=60 ./ex.sh live_report.cs | grep -v '^Code executed' | sed 's/^/~ /' >> $R   # «~» — живое, числа плавают, в сравнение не идёт
  fi
  echo "арена $n готова"
done
call '{"name":"manage_editor","arguments":{"action":"stop"}}'
PYTHONIOENCODING=utf-8 python diff.py baseline.txt $R
# часть зон (ARENAS=…) — в эталон вписываются только они, остальные зоны эталона остаются
if [ -n "$ARENAS" ]; then PYTHONIOENCODING=utf-8 python merge.py baseline.txt $R > out/merged.txt; else cp $R out/merged.txt; fi
if [ "$1" = "accept" ]; then cp out/merged.txt baseline.txt; echo "эталон обновлён"; fi
